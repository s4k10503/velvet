using System;

namespace Velvet
{
    // React's two hook-count errors, kept per hook kind because each kind has its own slot list and cursor.
    // A call past the count the committed render made throws from that call, as React's
    // updateWorkInProgressHook does; a body that returns having made fewer throws once it settles, as
    // finishRenderingHooks does. A mount (no committed render yet) is not compared.
    internal static class HookCountSentinel
    {
        private static readonly string[] s_kindNames = { "UseState / UseReducer", "UseStore", "Use" };

        private static int CountOf(in HookIndexTable cursors, int asyncCount, int kind) => kind switch
        {
            0 => cursors.StateHookIndex,
            1 => cursors.StoreHookIndex,
            2 => asyncCount,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        // A hook calls this right after advancing its cursor and before it reads or appends its slot, so a
        // call the committed render did not make appends nothing.
        internal static void ThrowIfPastCommittedCount(ComponentFiber fiber)
        {
            if (!fiber.HasCommittedHookCounts) return;
            for (var kind = 0; kind < s_kindNames.Length; kind++)
            {
                var committed = CountOf(fiber.CommittedHookCounts, fiber.CommittedAsyncSlotCount, kind);
                var current = CountOf(fiber.Indices, fiber.AsyncSlotCursor, kind);
                if (current > committed)
                {
                    throw new InvalidOperationException(FormatMismatch(fiber, s_kindNames[kind], committed, current));
                }
            }
        }

        // Runs once the render-phase loop settles. The counts advance only past this check, so a body that
        // throws or suspends leaves the next render compared against the last one that settled.
        internal static void ValidateAndCommit(ComponentFiber fiber)
        {
            if (fiber.HasCommittedHookCounts)
            {
                for (var kind = 0; kind < s_kindNames.Length; kind++)
                {
                    var committed = CountOf(fiber.CommittedHookCounts, fiber.CommittedAsyncSlotCount, kind);
                    var current = CountOf(fiber.Indices, fiber.AsyncSlotCursor, kind);
                    if (current != committed)
                    {
                        throw new InvalidOperationException(FormatMismatch(fiber, s_kindNames[kind], committed, current));
                    }
                }
            }
            fiber.CommittedHookCounts = fiber.Indices;
            fiber.CommittedAsyncSlotCount = fiber.AsyncSlotCursor;
            fiber.HasCommittedHookCounts = true;
        }

        internal static string FormatMismatch(ComponentFiber fiber, string kindName, int committed, int current)
            => current > committed
                ? $"{Hooks.ComponentName(fiber)}: Rendered more hooks than during the previous render" +
                  $" ({kindName}: {committed} before, {current} now). Hooks must be called in the same order on" +
                  " every render, never inside a condition, a loop or a helper method called conditionally" +
                  " (Rules of Hooks)."
                : $"{Hooks.ComponentName(fiber)}: Rendered fewer hooks than expected" +
                  $" ({kindName}: {committed} before, {current} now). This may be caused by an accidental early" +
                  " return statement, or by a hook inside a condition, a loop or a helper method called" +
                  " conditionally (Rules of Hooks).";
    }
}
