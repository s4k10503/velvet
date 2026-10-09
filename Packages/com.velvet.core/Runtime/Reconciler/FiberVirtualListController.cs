#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Velvet
{
    // Virtualization controller that places only visible-range items into the DOM inside a ScrollView.
    // Watches scroll position and GeometryChangedEvent, and updates the DOM directly when the visible range changes.
    internal sealed class FiberVirtualListController : IDisposable
    {
        private readonly ScrollView _scrollView;
        private readonly VisualElement _spacer;
        private readonly VisualElement _visibleContainer;
        private readonly IReconcilerBridge _reconciler;
        // The fiber that rendered V.VirtualList and the live context cursor, captured so item renders (which
        // happen outside the reconcile pass) can inherit the context enclosing the list. _enclosingContext is
        // a snapshot of that context's top values, refreshed on each Update (both ctor and Update run
        // mid-reconcile with a correct cursor). Both may be null when the controller is driven without a host
        // / context (e.g. a unit test) — then item renders fall back to the prior context-less behaviour.
        private readonly ComponentFiber? _hostFiber;
        private readonly ComponentContextStack? _contextStack;
        private List<KeyValuePair<object, object>>? _enclosingContext;

        private VirtualListNode _node;
        private VNode[] _renderedNodes;
        // Kept beside the node rather than written onto it: a node the renderer returns for several items
        // holds one key between all of them.
        private string?[] _renderedKeys;
        private VisualElement[] _renderedElements;
        // _firstRenderedIndex cannot stand in for this: ForceRefresh drops it to -1 while the buffers
        // above still hold the rows it named.
        private int _bufferFirstItemIndex = -1;
        private int _firstRenderedIndex = -1;
        private int _lastRenderedIndex = -1;
        // The viewport's extent along the scroll axis, as every offset and size below is: its width in a
        // horizontal list.
        private float _viewportHeight;
        // The axis the spacer, the container and the rows were last laid along.
        private bool _horizontal;
        private bool _isDisposed;
        private readonly VirtualListHandle _handle;
        // A ScrollToItem target the scroller's range fell short of, applied again at the content's next layout.
        private float? _pendingScrollTarget;
        private ValueAnimation<float>? _smoothScroll;
        private bool _writingScroll;
        private int _scrollRequestVersion;
        private float _axisValue;
        private (float low, float high) _axisRange;
        // Where each item starts, and at [Items.Count] where the list ends, for a list whose items each take
        // the height ItemHeightAt gives; unread for a list of one height, whose offsets are a product.
        private double[] _offsets = { 0 };

        // The prior pass's rows, split by what the next pass may find them under: a keyed row by its key,
        // with the item index it was rendered for; a further row under a key an earlier row already holds by
        // that key and that index; an unkeyed one by its item index. What a pass leaves in them, whether it
        // finishes or throws, is what DisposeUntakenRows releases.
        private readonly Dictionary<string, (VNode node, VisualElement element, int itemIndex)> _oldNodesByKey = new();
        private readonly Dictionary<(string key, int itemIndex), (VNode node, VisualElement element)> _oldRepeatedKeyRows = new();
        private readonly Dictionary<int, (VNode node, VisualElement element)> _oldNodesByItemIndex = new();
        private readonly HashSet<string> _reusedKeys = new();

        public FiberVirtualListController(
            ScrollView scrollView,
            VirtualListNode node,
            IReconcilerBridge reconciler,
            ComponentFiber? hostFiber = null,
            ComponentContextStack? contextStack = null)
        {
            _scrollView = scrollView ?? throw new ArgumentNullException(nameof(scrollView));
            _node = node ?? throw new ArgumentNullException(nameof(node));
            _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
            _hostFiber = hostFiber;
            _contextStack = contextStack;
            // Capture the enclosing context now (the cursor is correct mid-reconcile, where CreateElement
            // constructs this controller). The list's items render later, when the cursor is empty.
            _enclosingContext = MotionContext.OutlivingPass(contextStack?.SnapshotTops());

            if (node.Name != null)
            {
                scrollView.name = node.Name;
            }

            // Route class application through the same canonical entry point ElementNode uses, so the
            // ScrollView honours variant-token skipping, arbitrary values (w-[120px], bg-[addr:…]) and the
            // font-[…] skip. Variant manipulators and the inline font layer are applied by FiberNodeFactory
            // after the controller is created.
            FiberElementFactory.ApplyClassNames(scrollView, node.ClassNames);

            MeasureItems();
            _spacer = new VisualElement { style = { flexShrink = 0 } };
            _visibleContainer = new VisualElement { style = { position = Position.Absolute } };
            _horizontal = node.Horizontal;
            _scrollView.mode = _horizontal ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical;
            SizeSpacer();
            RememberAxisState();

            _renderedNodes = Array.Empty<VNode>();
            _renderedKeys = Array.Empty<string?>();
            _renderedElements = Array.Empty<VisualElement>();

            _scrollView.contentContainer.Add(_spacer);
            _scrollView.contentContainer.Add(_visibleContainer);

            _scrollView.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _scrollView.verticalScroller.valueChanged += OnVerticalScrollValueChanged;
            _scrollView.horizontalScroller.valueChanged += OnHorizontalScrollValueChanged;
            _scrollView.contentContainer.RegisterCallback<GeometryChangedEvent>(OnContentGeometryChanged);

            _handle = new VirtualListHandle(this, scrollView);
            node.ListRef?.Set(_handle);
        }

        public void Update(VirtualListNode newNode)
        {
            if (_isDisposed)
            {
                return;
            }

            var previousRef = _node.ListRef;
            _node = newNode ?? throw new ArgumentNullException(nameof(newNode));
            // A ref the list no longer names lets go of the handle, as React detaches a replaced ref.
            ReleaseRef(previousRef);
            newNode.ListRef?.Set(_handle);
            MeasureItems();
            if (_horizontal != newNode.Horizontal) FlipAxis();
            SizeSpacer();
            // Update runs during the host's reconcile (PatchNode), so the cursor is correct here: refresh the
            // snapshot in case the enclosing Provider / MotionContext value changed since the last render.
            _enclosingContext = MotionContext.OutlivingPass(_contextStack?.SnapshotTops());
            ForceRefresh();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            CancelScrollRequest();

            _scrollView.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _scrollView.verticalScroller.valueChanged -= OnVerticalScrollValueChanged;
            _scrollView.horizontalScroller.valueChanged -= OnHorizontalScrollValueChanged;
            _scrollView.contentContainer.UnregisterCallback<GeometryChangedEvent>(OnContentGeometryChanged);
            ReleaseRef(_node.ListRef);

            ClearRenderedItems();
        }

        // A list mounted in this one's place under a new key has set the ref already: the removal of this one
        // runs after that creation.
        private void ReleaseRef(Ref<VirtualListHandle>? listRef)
        {
            if (listRef != null && ReferenceEquals(listRef.Current, _handle)) listRef.Set(null);
        }

        private void OnContentGeometryChanged(GeometryChangedEvent evt)
        {
            if (_pendingScrollTarget == null) return;
            var target = _pendingScrollTarget.Value;
            _pendingScrollTarget = null;
            WriteScrollValue(target, _scrollRequestVersion);
        }

        private Scroller AxisScroller => _horizontal ? _scrollView.horizontalScroller : _scrollView.verticalScroller;

        private void MeasureViewport()
        {
            var viewport = _scrollView.contentViewport.resolvedStyle;
            _viewportHeight = _horizontal ? viewport.width : viewport.height;
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            MeasureViewport();
            if (_viewportHeight > 0)
            {
                UpdateVisibleRange(AxisScroller.value, _viewportHeight);
            }
        }

        private void OnVerticalScrollValueChanged(float scrollValue)
        {
            if (!_horizontal) OnScrollValueChanged(scrollValue);
        }

        private void OnHorizontalScrollValueChanged(float scrollValue)
        {
            if (_horizontal) OnScrollValueChanged(scrollValue);
        }

        private void OnScrollValueChanged(float scrollValue)
        {
            var ownScroll = _writingScroll;
            _writingScroll = false;
            var rangeClamp = ObserveAxisValue(scrollValue);
            if (!ownScroll && !rangeClamp) CancelScrollRequest();
            if (_viewportHeight > 0)
            {
                UpdateVisibleRange(scrollValue, _viewportHeight);
            }
        }

        private void SizeSpacer()
        {
            var length = (float)OffsetOf(_node.Items.Count);
            _spacer.style.height = _horizontal ? StyleKeyword.Null : length;
            _spacer.style.width = _horizontal ? length : StyleKeyword.Null;
        }

        // The rows keep the length the last rebuild wrote along the old axis, which nothing writes again.
        private void FlipAxis()
        {
            CancelScrollRequest();
            for (var i = 0; i < _renderedElements.Length; i++)
            {
                var row = _renderedElements[i];
                if (row == null) continue;
                if (_horizontal) row.style.width = StyleKeyword.Null;
                else row.style.height = StyleKeyword.Null;
            }
            _horizontal = !_horizontal;
            _scrollView.mode = _horizontal ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical;
            RememberAxisState();
            MeasureViewport();
        }

        internal void UpdateVisibleRange(float scrollY, float viewportHeight)
        {
            if (_isDisposed || _node.Items.Count == 0)
            {
                // Not gated on the tracked range, which cannot say whether rows are held — the reason
                // _bufferFirstItemIndex exists.
                ClearRenderedItems();
                _firstRenderedIndex = -1;
                _lastRenderedIndex = -1;
                return;
            }

            var itemCount = _node.Items.Count;
            var firstVisible = ItemAt(scrollY);
            // react-window's getStartStopIndices: an item starting at the viewport's bottom edge is not in view.
            var lastVisible = Math.Max(firstVisible, LastItemBefore(scrollY + viewportHeight));

            var newFirst = Math.Max(0, firstVisible - _node.Overscan);
            var newLast = Math.Min(itemCount - 1, lastVisible + _node.Overscan);

            if (newFirst == _firstRenderedIndex && newLast == _lastRenderedIndex)
            {
                return;
            }

            RenderRange(newFirst, newLast);
        }

        private void RenderRange(int newFirst, int newLast)
        {
            // A row's layout effects read the row, which is in the list only once RebuildVisibleContainer places it.
            _reconciler.HoldRowLayoutEffects();
            try
            {
                PlaceRange(newFirst, newLast);
            }
            finally
            {
                try
                {
                    _reconciler.CommitHeldRowLayoutEffects();
                }
                finally
                {
                    _reconciler.CommitStrandedLayoutWorkForController();
                }
            }
        }

        private void PlaceRange(int newFirst, int newLast)
        {
            var newCount = newLast - newFirst + 1;

            IndexOldRenderedItems();

            AllocateRenderBuffers(newCount, out var newNodes, out var newKeys, out var newElements);

            // The loop below can throw — from the application's keySelector and renderer, and from
            // creating or patching the row the renderer describes — and it must not escape half-done:
            // AllocateRenderBuffers may have aliased the live buffers, leaving the controller pointing at a
            // part-filled array while the container still shows the rows it no longer names. Containing
            // the item instead was rejected — V.List, the mapping site this construct follows, releases
            // what its pass took and rethrows.
            try
            {
                PatchOrReplaceVisibleItems(newFirst, newCount, newNodes, newKeys, newElements);
            }
            catch
            {
                DiscardFailedPass(newElements);
                throw;
            }

            DisposeUntakenRows();

            RebuildVisibleContainer(newFirst, newCount, newElements);

            _renderedNodes = newNodes;
            _renderedKeys = newKeys;
            _renderedElements = newElements;
            _bufferFirstItemIndex = newFirst;
            _firstRenderedIndex = newFirst;
            _lastRenderedIndex = newLast;

            // A Dispose that reached the list during the loop — an error boundary above it catching a row's
            // render is one way — released the buffers as they stood then, and the rows this pass placed
            // need not have been among them.
            if (_isDisposed)
            {
                ClearRenderedItems();
                return;
            }

            // After DisposeUntakenRows, so a recycled item's ref cleanup runs before the setup of
            // whatever took its place, and after the container rebuild, so a setup reads an item that is
            // already in the list.
            _reconciler.DrainRefAttachesForController();
        }

        // Indexes the still-rendered items into the two old-row tables, for RenderRange's reuse/patch
        // lookup and recycle-cleanup pass.
        private void IndexOldRenderedItems()
        {
            // Index the still-rendered items BEFORE allocating the new buffers: when the window size is
            // unchanged the new buffers ALIAS _renderedNodes/_renderedElements (a zero-allocation reuse), so the
            // Array.Clear below would wipe the very entries this index reads. Building it first keeps the per-row
            // (node, element) references — the reuse/patch lookup and, critically, the recycle-cleanup pass that
            // disposes scrolled-out items. Skipping it (the aliased-and-cleared path) silently leaked every item's
            // fiber (effects, store subscriptions, nested inline children) on uniform same-size scrolling.
            _oldNodesByKey.Clear();
            _oldRepeatedKeyRows.Clear();
            _oldNodesByItemIndex.Clear();
            for (var i = 0; i < _renderedNodes.Length; i++)
            {
                if (_renderedNodes[i] != null)
                {
                    var key = _renderedKeys[i];
                    var itemIndex = _bufferFirstItemIndex + i;
                    if (key == null)
                    {
                        _oldNodesByItemIndex[itemIndex] = (_renderedNodes[i], _renderedElements[i]);
                    }
                    else if (!_oldNodesByKey.TryAdd(key, (_renderedNodes[i], _renderedElements[i], itemIndex)))
                    {
                        _oldRepeatedKeyRows[(key, itemIndex)] = (_renderedNodes[i], _renderedElements[i]);
                    }
                }
            }
        }

        // Aliases the existing _renderedNodes/_renderedKeys/_renderedElements buffers when the window size is
        // unchanged (zero-allocation reuse), else allocates fresh ones sized to newCount, and clears the node
        // and element buffers plus the per-render set the patch-or-replace pass detects a repeated key with.
        // The key buffer is left as it was: IndexOldRenderedItems reads a key only beside a node this pass
        // filled.
        private void AllocateRenderBuffers(
            int newCount, out VNode[] newNodes, out string?[] newKeys, out VisualElement[] newElements)
        {
            newNodes = _renderedNodes.Length == newCount ? _renderedNodes : new VNode[newCount];
            newKeys = _renderedKeys.Length == newCount ? _renderedKeys : new string?[newCount];
            newElements = _renderedElements.Length == newCount ? _renderedElements : new VisualElement[newCount];
            System.Array.Clear(newNodes, 0, newCount);
            System.Array.Clear(newElements, 0, newCount);

            _reusedKeys.Clear();
        }

        // Renders each visible-range item and either patches its reused element or creates a replacement,
        // filling newNodes/newKeys/newElements in place.
        private void PatchOrReplaceVisibleItems(
            int newFirst, int newCount, VNode[] newNodes, string?[] newKeys, VisualElement[] newElements)
        {
            // Render the items under the context that enclosed the V.VirtualList: the scope parents new item
            // fibers under the host (so they share its context) and restores the enclosing snapshot onto the
            // cursor for the item bodies, then stamps the new fibers for isolated-re-render reconstruction.
            var scopeToken = _reconciler.BeginDetachedItemScope(_hostFiber, _enclosingContext);
            try
            {
                for (var i = 0; i < newCount; i++)
                {
                    // Rows rendered for a disposed list would be released unseen, their bodies run for
                    // nothing.
                    if (_isDisposed)
                    {
                        break;
                    }

                    var itemIndex = newFirst + i;
                    var item = _node.Items[itemIndex];
                    // The selector's key is compared as a whole string and never becomes a VNode.Key or a
                    // scope segment, so it is not held to the delimiter rule VNode.Key enforces.
                    var key = _node.KeySelector(item);

                    // A null key is no key, the answer V.List gives the same selector: the row renders and
                    // reconciles by position, which here is its item index, and never through the
                    // string-keyed collections below. VirtualListKeyCollectionContractTests holds those
                    // collections to what each does with one. A key an earlier rendered item of this pass
                    // already claimed is reported, and the item still renders.
                    var claimed = key != null && _reusedKeys.Add(key);
                    if (key != null && !claimed)
                    {
                        FiberLogger.LogWarning("FiberVirtualListController", $"Duplicate key detected: \"{key}\". Items sharing a key are told apart by their item index; give each item a unique key.");
                    }

                    var vnode = _node.Renderer(item);
                    if (vnode == null)
                    {
                        if (claimed)
                        {
                            _reusedKeys.Remove(key!);
                        }
                        continue;
                    }
                    newNodes[i] = vnode;
                    newKeys[i] = key;

                    // Not routed through ChildReconciler.PatchOrReplaceAtSlot despite the same CanPatch
                    // gate: this controller lives in a separate class reached only through
                    // IReconcilerBridge (CreateElementForController / PatchNodeForController /
                    // CleanupElementForController), which has no access to ChildReconciler's private
                    // _factory/_patcher/_cleaner fields the helper closes over. It also disposes the old
                    // element AFTER creating the replacement (create-before-dispose, see below) rather
                    // than before, so it could not reuse that helper's eager remove-then-create order
                    // even if the class boundary were bridged.
                    (VNode node, VisualElement element, OldRowTable table) found = default;
                    if (key != null)
                    {
                        found = FindKeyedRow(key, itemIndex);
                    }
                    else if (_oldNodesByItemIndex.TryGetValue(itemIndex, out var unkeyedRow))
                    {
                        found = (unkeyedRow.node, unkeyedRow.element, OldRowTable.ByItemIndex);
                    }
                    var hasExisting = found.table != OldRowTable.None;
                    var existing = (node: found.node, element: found.element);
                    if (hasExisting && ReconcileKeying.CanPatch(existing.node, vnode))
                    {
                        // Store the patch's RETURN: a class-driven wrap/unwrap (shadow-*/clip-path-*)
                        // swaps the slot's top-level element, and re-mounting a stale reference would
                        // strip the wrapper (or re-mount an emptied one).
                        newElements[i] = _reconciler.PatchNodeForController(existing.element, existing.node, vnode);
                    }
                    else
                    {
                        // A same-key type flip (CanPatch=false) is a replacement, not a patch — mirroring
                        // the general keyed diff path's create-before-dispose ordering.
                        var replacement = _reconciler.CreateElementForController(vnode);
                        if (hasExisting)
                        {
                            _reconciler.CleanupElementForController(existing.element);
                        }
                        newElements[i] = replacement;
                    }

                    // The prior row leaves its table only once the slot holds an element, the order
                    // GeneralPathReconciler.CommitLeaf keeps for an old leaf it marks taken: a throw from the create
                    // or the patch above leaves it there for DiscardFailedPass to release.
                    switch (found.table)
                    {
                        case OldRowTable.ByKey:
                            _oldNodesByKey.Remove(key!);
                            break;
                        case OldRowTable.RepeatedKey:
                            _oldRepeatedKeyRows.Remove((key!, itemIndex));
                            break;
                        case OldRowTable.ByItemIndex:
                            _oldNodesByItemIndex.Remove(itemIndex);
                            break;
                    }

                    // Stamp this item's newly created fibers with its own vnode so an isolated re-render can
                    // rebuild a Provider the renderer placed above the item's consumer (reused items add none).
                    _reconciler.StampDetachedItemFibers(_hostFiber, _enclosingContext, vnode, scopeToken);
                }
            }
            finally
            {
                _reconciler.EndDetachedItemScope(_hostFiber, _enclosingContext, scopeToken);
            }
        }

        private enum OldRowTable : byte
        {
            None,
            ByKey,
            RepeatedKey,
            ByItemIndex,
        }

        // The row this key was rendered under for this item index first. Failing that, the row it was
        // rendered under for another index, so a moved item keeps its row — but only where the item at that
        // index no longer returns the key, whether or not it is still in the range: while it does, the row
        // is that item's, and two items sharing a key are told apart by their indices.
        private (VNode node, VisualElement element, OldRowTable table) FindKeyedRow(string key, int itemIndex)
        {
            var hasFirst = _oldNodesByKey.TryGetValue(key, out var first);
            if (hasFirst && first.itemIndex == itemIndex)
            {
                return (first.node, first.element, OldRowTable.ByKey);
            }
            if (_oldRepeatedKeyRows.TryGetValue((key, itemIndex), out var repeated))
            {
                return (repeated.node, repeated.element, OldRowTable.RepeatedKey);
            }
            if (hasFirst && !StillReturnsKey(first.itemIndex, key))
            {
                return (first.node, first.element, OldRowTable.ByKey);
            }
            return default;
        }

        private bool StillReturnsKey(int itemIndex, string key)
            => itemIndex < _node.Items.Count
               && string.Equals(_node.KeySelector(_node.Items[itemIndex]), key, StringComparison.Ordinal);

        private void DisposeUntakenRows()
        {
            foreach (var kvp in _oldNodesByKey)
            {
                _reconciler.CleanupElementForController(kvp.Value.element);
            }

            foreach (var kvp in _oldRepeatedKeyRows)
            {
                _reconciler.CleanupElementForController(kvp.Value.element);
            }

            foreach (var kvp in _oldNodesByItemIndex)
            {
                _reconciler.CleanupElementForController(kvp.Value.element);
            }
        }

        // Releases what a pass that threw was holding — the elements in its slots, and the prior rows
        // left in the tables — and leaves the range naming nothing, so the next range update renders it
        // from nothing. Putting the previous range back was rejected: a same-key type flip the pass
        // completed has already disposed the element that range showed, so what came back would not
        // always be that range.
        private void DiscardFailedPass(VisualElement[] newElements)
        {
            for (var i = 0; i < newElements.Length; i++)
            {
                if (newElements[i] != null)
                {
                    _reconciler.CleanupElementForController(newElements[i]);
                }
            }

            DisposeUntakenRows();

            _visibleContainer.Clear();
            _renderedNodes = Array.Empty<VNode>();
            _renderedKeys = Array.Empty<string?>();
            _renderedElements = Array.Empty<VisualElement>();
            _firstRenderedIndex = -1;
            _lastRenderedIndex = -1;
        }

        // Repopulates _visibleContainer from newElements at its new scroll offset.
        private void RebuildVisibleContainer(int newFirst, int newCount, VisualElement[] newElements)
        {
            _visibleContainer.Clear();
            var offset = (float)OffsetOf(newFirst);
            var container = _visibleContainer.style;
            container.flexDirection = _horizontal ? FlexDirection.Row : StyleKeyword.Null;
            container.top = _horizontal ? 0 : offset;
            container.left = _horizontal ? offset : 0;
            container.bottom = _horizontal ? 0 : StyleKeyword.Null;
            container.right = _horizontal ? StyleKeyword.Null : 0;

            for (var i = 0; i < newCount; i++)
            {
                if (newElements[i] != null)
                {
                    var length = (float)(OffsetOf(newFirst + i + 1) - OffsetOf(newFirst + i));
                    if (_horizontal) newElements[i].style.width = length;
                    else newElements[i].style.height = length;
                    _visibleContainer.Add(newElements[i]);
                }
            }
        }

        // The buffers go with the rows they held: IndexOldRenderedItems offers whatever they still name
        // for reuse, so a released row left there can be patched back in by the next pass.
        private void ClearRenderedItems()
        {
            for (var i = 0; i < _renderedElements.Length; i++)
            {
                if (_renderedElements[i] != null)
                {
                    _reconciler.CleanupElementForController(_renderedElements[i]);
                }
            }
            _visibleContainer.Clear();
            _renderedNodes = Array.Empty<VNode>();
            _renderedKeys = Array.Empty<string?>();
            _renderedElements = Array.Empty<VisualElement>();
        }

        // Every item's height is asked again on each render, so a height that render changed is read there.
        private void MeasureItems()
        {
            if (_node.ItemHeightAt == null) return;
            var count = _node.Items.Count;
            if (_offsets.Length != count + 1) _offsets = new double[count + 1];
            for (var i = 0; i < count; i++) _offsets[i + 1] = _offsets[i] + _node.ItemHeightAt(i);
        }

        private double OffsetOf(int index)
            => _node.ItemHeightAt == null ? (double)index * _node.ItemHeight : _offsets[index];

        // The item whose span holds offset, an item ending there excluded; the last item past the list's end.
        private int ItemAt(double offset)
        {
            var last = _node.Items.Count - 1;
            if (_node.ItemHeightAt == null)
            {
                return Math.Clamp((int)Math.Floor(offset / _node.ItemHeight), 0, last);
            }
            // Item i ends at _offsets[i + 1]: an end equal to offset belongs to the item before the one holding it,
            // and otherwise the first end past offset is the holding item's.
            var end = Array.BinarySearch(_offsets, 1, last + 1, offset);
            // MUTANT_SURVIVES(equivalent, boundary): a search starting at index 1 never answers 0.
            return Math.Min(end >= 0 ? end : ~end - 1, last);
        }

        // The last item starting before offset, clamped to the items.
        private int LastItemBefore(double offset)
        {
            var last = _node.Items.Count - 1;
            if (_node.ItemHeightAt == null)
            {
                return Math.Clamp((int)Math.Ceiling(offset / _node.ItemHeight) - 1, 0, last);
            }
            // An end equal to offset is the end of the last item starting before it.
            var end = Array.BinarySearch(_offsets, 1, last + 1, offset);
            // MUTANT_SURVIVES(equivalent, boundary): a search beginning at absolute index 1 cannot return index 0.
            return Math.Min((end >= 0 ? end : ~end) - 1, last);
        }

        // react-window's getOffsetForIndex, against the viewport the last GeometryChangedEvent measured.
        internal void ScrollToItem(int index, VirtualListAlign align, VirtualListScrollBehavior behavior)
        {
            if (_isDisposed) return;
            var count = _node.Items.Count;
            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index,
                    $"Index {index} is not within the range of 0 - {count - 1}.");
            }

            var start = OffsetOf(index);
            var size = OffsetOf(index + 1) - start;
            var total = OffsetOf(count);
            var viewport = (double)_viewportHeight;
            var current = (double)AxisScroller.value;
            var maxOffset = Math.Max(0, Math.Min(total - viewport, start));
            var minOffset = Math.Max(0, start - viewport + size);
            // An item taller than the viewport is in view while the viewport lies within it.
            // MUTANT_SURVIVES(equivalent, boundary): where size equals the viewport, minOffset is start give or
            // take one double rounding of start - viewport + size, so both arms ask whether current is start,
            // and the target either one picks differs from current by less than the float the scroller stores.
            var inView = size > viewport
                ? current >= start && current <= minOffset
                : current >= minOffset && current <= start;
            var center = Math.Max(0, Math.Min(total - viewport, start + size / 2 - viewport / 2));
            var target = align switch
            {
                VirtualListAlign.Start => maxOffset,
                VirtualListAlign.End => minOffset,
                VirtualListAlign.Center => center,
                // Smart is Auto for an item in view, where Auto stays put.
                VirtualListAlign.Smart => inView ? current : center,
                // MUTANT_SURVIVES(equivalent, boundary): current == minOffset is in view in either arm of inView.
                VirtualListAlign.Auto => inView ? current : current < minOffset ? minOffset : maxOffset,
                _ => throw new ArgumentOutOfRangeException(nameof(align), align, "not a VirtualListAlign member"),
            };
            var value = (float)target;
            CancelScrollRequest();
            var requestVersion = _scrollRequestVersion;
            if (behavior == VirtualListScrollBehavior.Smooth)
            {
                _smoothScroll = _scrollView.experimental.animation
                    .Start(AxisScroller.value, value, SmoothScrollDurationMs, (_, step) => WriteScrollValue(step, requestVersion))
                    .Ease(Easing.InOutQuad)
                    .KeepAlive();
                _smoothScroll.OnCompleted(() => EndSmoothScroll(value, requestVersion));
                return;
            }
            ScrollTo(value, requestVersion);
        }

        private const int SmoothScrollDurationMs = 300;

        private void ScrollTo(float value, int requestVersion)
        {
            WriteScrollValue(value, requestVersion);
            // A valueChanged handler may have issued a newer request before the write returns.
            if (!_isDisposed && requestVersion == _scrollRequestVersion)
            {
                _pendingScrollTarget = AxisScroller.value < value ? value : null;
            }
        }

        private void WriteScrollValue(float value, int requestVersion)
        {
            if (_isDisposed || requestVersion != _scrollRequestVersion) return;
            var wasWriting = _writingScroll;
            _writingScroll = true;
            try
            {
                AxisScroller.value = value;
            }
            finally
            {
                _writingScroll = wasWriting;
                RememberAxisState();
            }
        }

        private void EndSmoothScroll(float target, int requestVersion)
        {
            // MUTANT_SURVIVES(equivalent, guard removed): cancellation advances the request version before completion; the following guard rejects that old callback.
            if (_smoothScroll == null) return;
            // MUTANT_SURVIVES(equivalent, guard removed): cancellation clears the stored animation; the preceding guard covers its callback under the Stop ordering pinned by
            // VirtualListSmoothScrollTests.Given_ASmoothScrollInFlight_When_AnExternalChangeInterruptsIt_Then_TheOldAnimationLeavesTheListThere(DuringTick).
            if (requestVersion != _scrollRequestVersion) return;
            _smoothScroll = null;
            ScrollTo(target, requestVersion);
        }

        private void CancelScrollRequest()
        {
            _scrollRequestVersion++;
            _pendingScrollTarget = null;
            var running = _smoothScroll;
            _smoothScroll = null;
            running?.Stop();
        }

        private void RememberAxisState()
        {
            _axisValue = AxisScroller.value;
            _axisRange = (AxisScroller.lowValue, AxisScroller.highValue);
        }

        private bool ObserveAxisValue(float value)
        {
            var range = (low: AxisScroller.lowValue, high: AxisScroller.highValue);
            var clamped = Math.Clamp(_axisValue, Math.Min(range.low, range.high), Math.Max(range.low, range.high));
            var rangeClamp = !range.Equals(_axisRange) && value.Equals(clamped);
            _axisRange = range;
            _axisValue = value;
            return rangeClamp;
        }

        private void ForceRefresh()
        {
            _firstRenderedIndex = -1;
            _lastRenderedIndex = -1;

            if (_viewportHeight > 0)
            {
                UpdateVisibleRange(AxisScroller.value, _viewportHeight);
            }
        }
    }
}
