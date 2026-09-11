#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Virtualization controller that places only visible-range items into the DOM inside a ScrollView.
    // Watches scroll position and GeometryChangedEvent, and updates the DOM directly when the visible range changes.
    internal sealed class FiberVirtualListController : IDisposable
    {
        private readonly ScrollView _scrollView;
        private readonly VisualElement _totalHeightSpacer;
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
        private VisualElement[] _renderedElements;
        // _firstRenderedIndex cannot stand in for this: ForceRefresh drops it to -1 while the buffers
        // above still hold the rows it named.
        private int _bufferFirstItemIndex = -1;
        private int _firstRenderedIndex = -1;
        private int _lastRenderedIndex = -1;
        private float _viewportHeight;
        private bool _isDisposed;

        // The prior pass's rows, split by what the next pass may find them under: a keyed row by its key,
        // an unkeyed one by the item index it was rendered for. What a pass leaves in them, whether it
        // finishes or throws, is what DisposeUntakenRows releases.
        private readonly Dictionary<string, (VNode node, VisualElement element)> _oldNodesByKey = new();
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
            _enclosingContext = contextStack?.SnapshotTops();

            if (node.Name != null)
            {
                scrollView.name = node.Name;
            }

            // Route class application through the same canonical entry point ElementNode uses, so the
            // ScrollView honours variant-token skipping, arbitrary values (w-[120px], bg-[addr:…]) and the
            // font-[…] skip. Variant manipulators and the inline font layer are applied by FiberNodeFactory
            // after the controller is created.
            FiberElementFactory.ApplyClassNames(scrollView, node.ClassNames);

            _totalHeightSpacer = new VisualElement
            {
                style =
                {
                    height = node.ItemHeight * node.Items.Count,
                    flexShrink = 0
                }
            };

            _visibleContainer = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    right = 0
                }
            };

            _renderedNodes = Array.Empty<VNode>();
            _renderedElements = Array.Empty<VisualElement>();

            _scrollView.contentContainer.Add(_totalHeightSpacer);
            _scrollView.contentContainer.Add(_visibleContainer);

            _scrollView.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _scrollView.verticalScroller.valueChanged += OnScrollValueChanged;
        }

        public void Update(VirtualListNode newNode)
        {
            if (_isDisposed)
            {
                return;
            }

            _node = newNode ?? throw new ArgumentNullException(nameof(newNode));
            _totalHeightSpacer.style.height = newNode.ItemHeight * newNode.Items.Count;
            // Update runs during the host's reconcile (PatchNode), so the cursor is correct here: refresh the
            // snapshot in case the enclosing Provider / MotionContext value changed since the last render.
            _enclosingContext = _contextStack?.SnapshotTops();
            ForceRefresh();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            _scrollView.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _scrollView.verticalScroller.valueChanged -= OnScrollValueChanged;

            ClearRenderedItems();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            _viewportHeight = _scrollView.contentViewport.resolvedStyle.height;
            if (_viewportHeight > 0)
            {
                UpdateVisibleRange(_scrollView.verticalScroller.value, _viewportHeight);
            }
        }

        private void OnScrollValueChanged(float scrollValue)
        {
            if (_viewportHeight > 0)
            {
                UpdateVisibleRange(scrollValue, _viewportHeight);
            }
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

            var itemHeight = _node.ItemHeight;
            var itemCount = _node.Items.Count;

            var firstVisible = Math.Max(0, (int)(scrollY / itemHeight));
            var lastVisible = Math.Min(itemCount - 1, (int)((scrollY + viewportHeight) / itemHeight));

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
            var newCount = newLast - newFirst + 1;

            IndexOldRenderedItems();

            AllocateRenderBuffers(newCount, out var newNodes, out var newElements);

            // The loop below can throw — from the application's keySelector and renderer, and from
            // creating or patching the row the renderer describes — and it must not escape half-done:
            // AllocateRenderBuffers may have aliased the live buffers, leaving the controller pointing at a
            // part-filled array while the container still shows the rows it no longer names. Containing
            // the item instead was rejected — V.List, the mapping site this construct follows, releases
            // what its pass took and rethrows.
            try
            {
                PatchOrReplaceVisibleItems(newFirst, newCount, newNodes, newElements);
            }
            catch
            {
                DiscardFailedPass(newElements);
                throw;
            }

            DisposeUntakenRows();

            RebuildVisibleContainer(newFirst, newCount, newElements);

            _renderedNodes = newNodes;
            _renderedElements = newElements;
            _bufferFirstItemIndex = newFirst;
            _firstRenderedIndex = newFirst;
            _lastRenderedIndex = newLast;

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
            _oldNodesByItemIndex.Clear();
            for (var i = 0; i < _renderedNodes.Length; i++)
            {
                if (_renderedNodes[i] != null)
                {
                    var key = _renderedNodes[i].Key;
                    if (key != null)
                    {
                        if (!_oldNodesByKey.TryAdd(key, (_renderedNodes[i], _renderedElements[i])))
                        {
                            FiberLogger.LogWarning("FiberVirtualListController", $"Duplicate key detected in rendered items: \"{key}\". Later element overwrites earlier one.");
                            _oldNodesByKey[key] = (_renderedNodes[i], _renderedElements[i]);
                        }
                    }
                    else
                    {
                        _oldNodesByItemIndex[_bufferFirstItemIndex + i] = (_renderedNodes[i], _renderedElements[i]);
                    }
                }
            }
        }

        // Aliases the existing _renderedNodes/_renderedElements buffers when the window size is unchanged
        // (zero-allocation reuse), else allocates fresh ones sized to newCount, and clears both plus the
        // per-render set the patch-or-replace pass detects a repeated key with.
        private void AllocateRenderBuffers(int newCount, out VNode[] newNodes, out VisualElement[] newElements)
        {
            newNodes = _renderedNodes.Length == newCount ? _renderedNodes : new VNode[newCount];
            newElements = _renderedElements.Length == newCount ? _renderedElements : new VisualElement[newCount];
            System.Array.Clear(newNodes, 0, newCount);
            System.Array.Clear(newElements, 0, newCount);

            _reusedKeys.Clear();
        }

        // Renders each visible-range item and either patches its reused element or creates a replacement,
        // filling newNodes/newElements in place.
        private void PatchOrReplaceVisibleItems(int newFirst, int newCount, VNode[] newNodes, VisualElement[] newElements)
        {
            // Render the items under the context that enclosed the V.VirtualList: the scope parents new item
            // fibers under the host (so they share its context) and restores the enclosing snapshot onto the
            // cursor for the item bodies, then stamps the new fibers for isolated-re-render reconstruction.
            var scopeToken = _reconciler.BeginDetachedItemScope(_hostFiber, _enclosingContext);
            try
            {
                for (var i = 0; i < newCount; i++)
                {
                    var itemIndex = newFirst + i;
                    var item = _node.Items[itemIndex];
                    var key = _node.KeySelector(item);

                    // Taken out of the range here rather than left to VNode.Key's refusal below, which
                    // would end the pass and blank the list over one item's key.
                    if (VNode.KeyHoldsDelimiter(key))
                    {
                        FiberLogger.LogWarning("FiberVirtualListController", $"Key holding a NUL (U+0000) detected: \"{key}\". Skipping the item; NUL is reserved as the internal scope delimiter.");
                        continue;
                    }

                    // A null key is no key, the answer V.List gives the same selector: the row renders and
                    // reconciles by position, which here is its item index, and never through the two
                    // string-keyed collections below. VirtualListKeyCollectionContractTests holds those
                    // two to what each does with one.
                    if (key != null && !_reusedKeys.Add(key))
                    {
                        FiberLogger.LogWarning("FiberVirtualListController", $"Duplicate key detected: \"{key}\". Skipping duplicate item to prevent tracking inconsistency.");
                        continue;
                    }

                    var vnode = _node.Renderer(item);
                    if (vnode == null)
                    {
                        if (key != null)
                        {
                            _reusedKeys.Remove(key);
                        }
                        continue;
                    }
                    // IndexOldRenderedItems keys the next pass's reuse table off VNode.Key, and that table
                    // is read back by the selector's key, so the two sides answer each other only while
                    // the selector's key is the one that lands on the node — the same overwrite V.List
                    // makes at its own mapping site.
                    vnode.Key = key;
                    newNodes[i] = vnode;

                    // Not routed through ChildReconciler.PatchOrReplaceAtSlot despite the same CanPatch
                    // gate: this controller lives in a separate class reached only through
                    // IReconcilerBridge (CreateElementForController / PatchNodeForController /
                    // CleanupElementForController), which has no access to ChildReconciler's private
                    // _factory/_patcher/_cleaner fields the helper closes over. It also disposes the old
                    // element AFTER creating the replacement (create-before-dispose, see below) rather
                    // than before, so it could not reuse that helper's eager remove-then-create order
                    // even if the class boundary were bridged.
                    var hasExisting = key != null
                        ? _oldNodesByKey.TryGetValue(key, out var existing)
                        : _oldNodesByItemIndex.TryGetValue(itemIndex, out existing);
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
                    // GeneralPathReconciler.CommitLeaf keeps for a key it marks used: a throw from the create
                    // or the patch above leaves it there for DiscardFailedPass to release.
                    if (key != null)
                    {
                        _oldNodesByKey.Remove(key);
                    }
                    else
                    {
                        _oldNodesByItemIndex.Remove(itemIndex);
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

        private void DisposeUntakenRows()
        {
            foreach (var kvp in _oldNodesByKey)
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
            _renderedElements = Array.Empty<VisualElement>();
            _firstRenderedIndex = -1;
            _lastRenderedIndex = -1;
        }

        // Repopulates _visibleContainer from newElements at its new scroll offset.
        private void RebuildVisibleContainer(int newFirst, int newCount, VisualElement[] newElements)
        {
            _visibleContainer.Clear();
            _visibleContainer.style.top = newFirst * _node.ItemHeight;

            for (var i = 0; i < newCount; i++)
            {
                if (newElements[i] != null)
                {
                    newElements[i].style.height = _node.ItemHeight;
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
            _renderedElements = Array.Empty<VisualElement>();
        }

        private void ForceRefresh()
        {
            _firstRenderedIndex = -1;
            _lastRenderedIndex = -1;

            if (_viewportHeight > 0)
            {
                UpdateVisibleRange(_scrollView.verticalScroller.value, _viewportHeight);
            }
        }
    }
}
