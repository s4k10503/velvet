using System;

namespace Velvet
{
    internal static class HookGuard
    {
        // Throws when a hook is invoked outside of a Render() context.
        // Called from the Hooks static class.
        // fiber == null means there is no Current on FiberAmbientStack, i.e. we are outside Render().
        internal static void ThrowIfNotRendering(ComponentFiber fiber, string hookName)
        {
            if (fiber == null || !fiber.IsRendering)
            {
                var typeName = fiber?.Body?.Method?.Name ?? "[Component]";
                throw new InvalidOperationException(
                    $"{typeName}: {hookName}() may only be used inside Render().");
            }
        }
    }
}
