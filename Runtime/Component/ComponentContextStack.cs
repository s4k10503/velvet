using System.Collections.Generic;

namespace Velvet
{
    // The live context cursor holding the current value per context. Maintained by the Reconciler
    // during tree traversal: a ContextProviderNode pushes on enter and pops on exit, so the
    // top of each per-context stack is the value the nearest enclosing Provider provides.
    // Hooks.UseContext<T> reads Get<T> directly. There is no per-fiber
    // snapshot — an isolated re-render reconstructs the enclosing Providers onto this cursor via
    // FiberContextSpine before the body runs.
    internal sealed class ComponentContextStack
    {
        private readonly Dictionary<object, Stack<object>> _stacks = new();

        public void Push<T>(ComponentContext<T> context, T value) => PushRaw(context, value);

        public void Pop<T>(ComponentContext<T> context) => PopRaw(context);

        public T Get<T>(ComponentContext<T> context)
        {
            object key = context;
            if (_stacks.TryGetValue(key, out var stack) && stack.Count > 0)
            {
                return (T)stack.Peek();
            }

            return context.DefaultValue;
        }

        // Captures the current top value of every active context as an untyped snapshot. Used to carry the
        // enclosing context of a deferred mount (a Portal's children reconcile after the main pass has
        // unwound the cursor) so it can be restored via PushRaw/PopRaw around that deferred reconcile.
        // Only the nearest (top) value per context is captured — that is all a read observes.
        internal List<KeyValuePair<object, object>> SnapshotTops()
        {
            var snapshot = new List<KeyValuePair<object, object>>(_stacks.Count);
            foreach (var entry in _stacks)
            {
                if (entry.Value.Count > 0)
                {
                    snapshot.Add(new KeyValuePair<object, object>(entry.Key, entry.Value.Peek()));
                }
            }
            return snapshot;
        }

        // Untyped push/pop over the per-context stacks. Backs both the typed Push/Pop (which simply box the
        // context key and value) and the restore of a SnapshotTops capture (a snapshot has erased the generic
        // context type). Reads (Get) peek the top, so a raw-pushed value is observed exactly like a typed one.
        internal void PushRaw(object key, object value)
        {
            if (!_stacks.TryGetValue(key, out var stack))
            {
                stack = new Stack<object>();
                _stacks[key] = stack;
            }
            stack.Push(value);
        }

        internal void PopRaw(object key)
        {
            if (!_stacks.TryGetValue(key, out var stack))
            {
                return;
            }
            stack.Pop();
            if (stack.Count == 0)
            {
                _stacks.Remove(key);
            }
        }
    }
}
