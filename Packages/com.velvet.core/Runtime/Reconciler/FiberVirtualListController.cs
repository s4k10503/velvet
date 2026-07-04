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
        private int _firstRenderedIndex = -1;
        private int _lastRenderedIndex = -1;
        private float _viewportHeight;
        private bool _isDisposed;

        private readonly Dictionary<string, (VNode node, VisualElement element)> _oldNodesByKey = new();
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
            // font-[…] skip rather than the old crude "add every token verbatim" loop. Variant manipulators
            // and the inline font layer are applied by FiberNodeFactory after the controller is created.
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
            _renderedNodes = Array.Empty<VNode>();
            _renderedElements = Array.Empty<VisualElement>();
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
                if (_firstRenderedIndex != -1)
                {
                    ClearRenderedItems();
                    _firstRenderedIndex = -1;
                    _lastRenderedIndex = -1;
                }
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

            // Index the still-rendered items by key BEFORE allocating the new buffers: when the window size is
            // unchanged the new buffers ALIAS _renderedNodes/_renderedElements (a zero-allocation reuse), so the
            // Array.Clear below would wipe the very entries this index reads. Building it first keeps the per-key
            // (node, element) references — the reuse/patch lookup and, critically, the recycle-cleanup pass that
            // disposes scrolled-out items. Skipping it (the aliased-and-cleared path) silently leaked every item's
            // fiber (effects, store subscriptions, nested inline children) on uniform same-size scrolling.
            _oldNodesByKey.Clear();
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
                }
            }

            var newNodes = _renderedNodes.Length == newCount ? _renderedNodes : new VNode[newCount];
            var newElements = _renderedElements.Length == newCount ? _renderedElements : new VisualElement[newCount];
            System.Array.Clear(newNodes, 0, newCount);
            System.Array.Clear(newElements, 0, newCount);

            _reusedKeys.Clear();

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

                    if (!_reusedKeys.Add(key))
                    {
                        FiberLogger.LogWarning("FiberVirtualListController", $"Duplicate key detected: \"{key}\". Skipping duplicate item to prevent tracking inconsistency.");
                        continue;
                    }

                    var vnode = _node.Renderer(item);
                    if (vnode == null)
                    {
                        _reusedKeys.Remove(key);
                        continue;
                    }
                    vnode.Key ??= key;
                    newNodes[i] = vnode;

                    if (_oldNodesByKey.TryGetValue(key, out var existing))
                    {
                        // Store the patch's RETURN: a class-driven wrap/unwrap (shadow-*/clip-path-*)
                        // swaps the slot's top-level element, and re-mounting a stale reference would
                        // strip the wrapper (or re-mount an emptied one).
                        newElements[i] = _reconciler.PatchNodeForController(existing.element, existing.node, vnode);
                    }
                    else
                    {
                        newElements[i] = _reconciler.CreateElementForController(vnode);
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

            foreach (var kvp in _oldNodesByKey)
            {
                if (!_reusedKeys.Contains(kvp.Key))
                {
                    _reconciler.CleanupElementForController(kvp.Value.element);
                }
            }

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

            _renderedNodes = newNodes;
            _renderedElements = newElements;
            _firstRenderedIndex = newFirst;
            _lastRenderedIndex = newLast;
        }

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
