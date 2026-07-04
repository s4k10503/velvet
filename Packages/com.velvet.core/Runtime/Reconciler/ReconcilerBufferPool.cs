#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Buffer pool used across the entire Reconciler.
    // Reuses buffers across reconcile passes to reduce GC allocations.
    // The Rent/Return pattern prevents buffer collisions during recursive calls.
    // Each pool has an upper bound of MaxPoolSize; returns beyond the
    // limit are discarded (preventing the pool from staying inflated at the peak size).
    internal sealed class ReconcilerBufferPool
    {
        // Maximum number of instances retained in a pool. Eight slots are practically
        // sufficient even for deeply nested recursion. Returns beyond the limit become GC targets.
        private const int MaxPoolSize = 8;

        // A type-agnostic Rent/Return pool. The clear-on-rent / clear-on-return discipline and the
        // capacity cap live here once; each buffer kind supplies only its element type and a Clear delegate
        // (Dictionary/HashSet/List have incompatible ICollection element types, so a delegate — not an
        // interface constraint — keeps the cap and reset in a single source of truth).
        private sealed class ClearablePool<T> where T : new()
        {
            private readonly Stack<T> _pool = new();
            private readonly Action<T> _clear;
            private readonly int _max;

            public ClearablePool(Action<T> clear, int max = MaxPoolSize)
            {
                _clear = clear;
                _max = max;
            }

            public T Rent()
            {
                var item = _pool.Count > 0 ? _pool.Pop() : new T();
                _clear(item);
                return item;
            }

            public void Return(T item)
            {
                _clear(item);
                if (_pool.Count >= _max) return;
                _pool.Push(item);
            }
        }

        #region ChildReconciler — for ReconcileKeyed

        private readonly ClearablePool<Dictionary<ChildKey, (int index, VNode? node)>> _oldKeyMapPool = new(m => m.Clear());
        private readonly ClearablePool<HashSet<ChildKey>> _usedKeysPool = new(s => s.Clear());
        private readonly ClearablePool<HashSet<ChildKey>> _replacedKeysPool = new(s => s.Clear());
        private readonly ClearablePool<List<(VisualElement? element, bool isExisting)>> _newElementsPool = new(l => l.Clear());

        public Dictionary<ChildKey, (int index, VNode? node)> RentOldKeyMap() => _oldKeyMapPool.Rent();
        public void Return(Dictionary<ChildKey, (int index, VNode? node)> map) => _oldKeyMapPool.Return(map);

        public HashSet<ChildKey> RentKeySet() => _usedKeysPool.Rent();
        public void ReturnKeySet(HashSet<ChildKey> set) => _usedKeysPool.Return(set);

        public HashSet<ChildKey> RentReplacedKeySet() => _replacedKeysPool.Rent();
        public void ReturnReplacedKeySet(HashSet<ChildKey> set) => _replacedKeysPool.Return(set);

        public List<(VisualElement? element, bool isExisting)> RentElementList() => _newElementsPool.Rent();
        public void Return(List<(VisualElement? element, bool isExisting)> list) => _newElementsPool.Return(list);

        #endregion

        #region For DOM-less AnimatePresence (ChildReconciler.ExpandAnimatePresenceInline) + BuildKeyedMapCopy

        private readonly ClearablePool<List<(string key, VNode node)>> _presenceListPool = new(l => l.Clear());
        private readonly ClearablePool<HashSet<string>> _presenceKeyPool = new(s => s.Clear());

        public List<(string key, VNode node)> RentKeyedList() => _presenceListPool.Rent();
        public void Return(List<(string key, VNode node)> list) => _presenceListPool.Return(list);

        public HashSet<string> RentPresenceKeySet() => _presenceKeyPool.Rent();
        public void ReturnPresenceKeySet(HashSet<string> set) => _presenceKeyPool.Return(set);

        #endregion

        #region For FlattenAndFilter

        private readonly ClearablePool<List<VNode>> _nodeListPool = new(l => l.Clear());

        public List<VNode> RentNodeList() => _nodeListPool.Rent();
        public void ReturnNodeList(List<VNode> list) => _nodeListPool.Return(list);

        #endregion

        #region ChildReconciler — for inline ContextProviderNode tracking

        private readonly ClearablePool<List<ContextProviderNode>> _providerListPool = new(l => l.Clear());

        public List<ContextProviderNode> RentProviderList() => _providerListPool.Rent();
        public void ReturnProviderList(List<ContextProviderNode> list) => _providerListPool.Return(list);

        #endregion

        #region ChildReconciler — for inline ComponentFiber tracking (per-Reconcile orphan diff)

        private readonly ClearablePool<List<ComponentFiber>> _fiberListPool = new(l => l.Clear());
        private readonly ClearablePool<HashSet<ComponentFiber>> _fiberSetPool = new(s => s.Clear());

        public List<ComponentFiber> RentFiberList() => _fiberListPool.Rent();
        public void ReturnFiberList(List<ComponentFiber> list) => _fiberListPool.Return(list);

        public HashSet<ComponentFiber> RentFiberSet() => _fiberSetPool.Rent();
        public void ReturnFiberSet(HashSet<ComponentFiber> set) => _fiberSetPool.Return(set);

        #endregion

        #region ChildReconciler — position counter for unkeyed inline components

        private readonly ClearablePool<Dictionary<object, int>> _positionCounterPool = new(m => m.Clear());

        public Dictionary<object, int> RentPositionCounter() => _positionCounterPool.Rent();
        public void ReturnPositionCounter(Dictionary<object, int> map) => _positionCounterPool.Return(map);

        #endregion

        #region ChildReconciler — for duplicate-key orphans (HashSet<int>)

        private readonly ClearablePool<HashSet<int>> _orphanedIndexSetPool = new(s => s.Clear());

        public HashSet<int> RentOrphanedIndexSet() => _orphanedIndexSetPool.Rent();
        public void ReturnOrphanedIndexSet(HashSet<int> set) => _orphanedIndexSetPool.Return(set);

        #endregion

        #region FiberNodeFactory.BuildKeyedMapCopy — indexByKey

        private readonly ClearablePool<Dictionary<string, int>> _indexByKeyMapPool = new(m => m.Clear());

        public Dictionary<string, int> RentIndexByKeyMap() => _indexByKeyMapPool.Rent();
        public void ReturnIndexByKeyMap(Dictionary<string, int> map) => _indexByKeyMapPool.Return(map);

        #endregion

        #region DiffClassList — HashSet<string>

        private readonly ClearablePool<HashSet<string>> _classSetPool = new(s => s.Clear());

        public HashSet<string> RentClassSet() => _classSetPool.Rent();
        public void ReturnClassSet(HashSet<string> set) => _classSetPool.Return(set);

        #endregion

        #region LIS computation — List<int> / HashSet<int>

        private readonly ClearablePool<List<int>> _intListPool = new(l => l.Clear());
        private readonly ClearablePool<HashSet<int>> _intSetPool = new(s => s.Clear());

        public List<int> RentIntList() => _intListPool.Rent();
        public void ReturnIntList(List<int> list) => _intListPool.Return(list);

        public HashSet<int> RentIntSet() => _intSetPool.Rent();
        public void ReturnIntSet(HashSet<int> set) => _intSetPool.Return(set);

        #endregion

        #region LIS computation — Dictionary<VisualElement, int>

        private readonly ClearablePool<Dictionary<VisualElement, int>> _elementIndexMapPool = new(m => m.Clear());

        public Dictionary<VisualElement, int> RentElementIndexMap() => _elementIndexMapPool.Rent();
        public void ReturnElementIndexMap(Dictionary<VisualElement, int> map) => _elementIndexMapPool.Return(map);

        #endregion
    }
}
