#nullable enable
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
        // Seq orders every push this cursor has taken, so a frame can tell what was pushed since it began.
        internal readonly struct Entry
        {
            internal readonly object? Value;
            internal readonly long Seq;

            internal Entry(object? value, long seq)
            {
                Value = value;
                Seq = seq;
            }
        }

        private Dictionary<object, Stack<Entry>> _stacks = new();

        private readonly Stack<Stack<Entry>> _emptied = new();

        private long _nextSeq;

        private ContextFrame _frame;

        public void Push<T>(ComponentContext<T> context, T? value) => PushRaw(context, value);

        public void Pop<T>(ComponentContext<T> context) => PopRaw(context);

        public T? Get<T>(ComponentContext<T> context)
        {
            object key = context;
            if (_stacks.TryGetValue(key, out var stack) && stack.Count > 0)
            {
                return (T)stack.Peek().Value!;
            }

            return context.DefaultValue;
        }

        internal bool TryGetTop(object key, out object? value)
        {
            if (_stacks.TryGetValue(key, out var stack) && stack.Count > 0)
            {
                value = stack.Peek().Value;
                return true;
            }
            value = null;
            return false;
        }

        // Captures the current top value of every active context as an untyped snapshot. Used to carry the
        // enclosing context of a deferred mount (a Portal's children reconcile after the main pass has unwound the
        // cursor) so it can be restored via PushRaw/PopRaw around that deferred reconcile, which belongs to the same
        // pass. Only the nearest (top) value per context is captured — that is all a read observes.
        internal List<KeyValuePair<object, object>> SnapshotTops()
        {
            var snapshot = new List<KeyValuePair<object, object>>(_stacks.Count);
            foreach (var entry in _stacks)
            {
                if (entry.Value.Count > 0)
                {
                    snapshot.Add(new KeyValuePair<object, object>(entry.Key, entry.Value.Peek().Value!));
                }
            }
            return snapshot;
        }

        // The frame a capture at a detached host reads (DetachedHost.Capture). Entered where a fiber's own output
        // starts to reconcile, after whatever stands for the Providers above that fiber is on the cursor, and where a
        // detached host's children start to reconcile, naming that host; the previous frame is handed back for
        // ExitFrame.
        internal ContextFrame EnterFrame(DetachedHost? enclosing)
        {
            var previous = _frame;
            _frame = new ContextFrame(_nextSeq, enclosing, enclosing?.Resolve());
            return previous;
        }

        internal void ExitFrame(ContextFrame previous) => _frame = previous;

        internal ContextFrame Frame => _frame;

        // Every entry the current frame pushed that is still on the cursor, each context's in the order it was pushed.
        internal void CollectPushedInFrame(List<KeyValuePair<object, object>> into)
        {
            var since = _frame.Since;
            foreach (var entry in _stacks)
            {
                var start = into.Count;
                foreach (var pushed in entry.Value)
                {
                    // Enumerated from the top down, and the sequence only grows upward.
                    if (pushed.Seq < since) break;
                    into.Add(new KeyValuePair<object, object>(entry.Key, pushed.Value!));
                }
                into.Reverse(start, into.Count - start);
            }
        }

        // Starts the cursor empty for a render placed somewhere other than where the values on it were pushed, and
        // hands those values back for Restore, which drops whatever the render pushed with them.
        internal Dictionary<object, Stack<Entry>> SetAside()
        {
            var setAside = _stacks;
            _stacks = new Dictionary<object, Stack<Entry>>();
            return setAside;
        }

        internal void Restore(Dictionary<object, Stack<Entry>> setAside) => _stacks = setAside;

        // Untyped push/pop over the per-context stacks. Backs both the typed Push/Pop (which simply box the
        // context key and value) and the restore of a SnapshotTops capture (a snapshot has erased the generic
        // context type). Reads (Get) peek the top, so a raw-pushed value is observed exactly like a typed one.
        internal void PushRaw(object key, object? value)
        {
            if (!_stacks.TryGetValue(key, out var stack))
            {
                stack = _emptied.Count > 0 ? _emptied.Pop() : new Stack<Entry>();
                _stacks[key] = stack;
            }
            stack.Push(new Entry(value, _nextSeq++));
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
                _emptied.Push(stack);
            }
        }
    }

    // Since is the sequence of the first push the frame counts as its own. The default frame counts every push, which
    // is right where no fiber's output is being reconciled (a bare Reconciler.Reconcile).
    internal readonly struct ContextFrame
    {
        internal readonly long Since;
        internal readonly DetachedHost? Enclosing;
        internal readonly DetachedHostCapture? EnclosingCapture;

        internal ContextFrame(long since, DetachedHost? enclosing, DetachedHostCapture? enclosingCapture)
        {
            Since = since;
            Enclosing = enclosing;
            EnclosingCapture = enclosingCapture;
        }
    }
}
