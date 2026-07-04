using System;
using System.Collections.Generic;

namespace Velvet
{
    // Ambient context that holds the currently rendering ComponentFiber in a thread-static stack.
    // A component calls Push at the start of Render and Pop at the end.
    // The Hooks static class resolves the fiber via this stack's Current.
    // Distinct in role from ReconcilerContext.FiberStack (used for parent/child tracking); intentionally
    // separated for separation of concerns.
    internal static class FiberAmbientStack
    {
        [ThreadStatic]
        private static Stack<ComponentFiber> t_stack;

        public static ComponentFiber Current
        {
            get
            {
                var s = t_stack;
                return (s != null && s.Count > 0) ? s.Peek() : null;
            }
        }

        public static void Push(ComponentFiber fiber)
        {
            if (fiber == null) throw new ArgumentNullException(nameof(fiber));
            t_stack ??= new Stack<ComponentFiber>();
            t_stack.Push(fiber);
        }

        public static void Pop()
        {
            if (t_stack == null || t_stack.Count == 0) return;
            t_stack.Pop();
        }
    }
}
