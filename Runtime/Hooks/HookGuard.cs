using System;

namespace Velvet
{
    internal static class HookGuard
    {
        // fiber == null means there is no Current on FiberAmbientStack, i.e. we are outside Render().
        // The gate is the render-phase WINDOW (the body on the stack), not the whole flush: the
        // commit phase legitimately runs user code (callback refs) with the ambient fiber still
        // pushed, and a hook call from there must fail fast here — the cursor is parked past the
        // settled body's reads, so letting it through would append a slot and shift the next
        // render's positional pairing, surfacing one render later as a misleading count mismatch.
        internal static void ThrowIfNotRendering(ComponentFiber? fiber, string hookName)
        {
            if (fiber == null || !fiber.IsInRenderPhase)
            {
                var typeName = fiber?.Body?.Method?.Name ?? "[Component]";
                throw new InvalidOperationException(
                    $"{typeName}: {hookName}() may only be used inside Render().");
            }
        }
    }
}
