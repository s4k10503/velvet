using System;
using System.Collections.Generic;

namespace Velvet
{
    // LIFO stack representing "the currently rendering ComponentFiber" during reconcile.
    // Pushed each time a component's Render produces a child Component, and popped when children traversal completes.
    // Placed symmetrically with ComponentContextStack (per-context value stack); passes the parent Fiber reference to
    // ComponentFiber.AppendChild.
    // Since the Fiber itself owns Parent/Child/Sibling pointers, this stack only tracks "the fiber currently
    // being traversed".
    internal sealed class FiberStack
    {
        private readonly Stack<ComponentFiber> _stack = new();

        public ComponentFiber Current => _stack.TryPeek(out var top) ? top : null;

        public void Push(ComponentFiber fiber)
        {
            if (fiber == null)
            {
                throw new ArgumentNullException(nameof(fiber));
            }
            _stack.Push(fiber);
        }

        public void Pop()
        {
            if (_stack.Count == 0)
            {
                throw new InvalidOperationException("FiberStack.Pop called on empty stack.");
            }
            _stack.Pop();
        }
    }
}
