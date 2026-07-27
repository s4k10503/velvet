using UnityEngine.UIElements;

namespace Velvet
{
    // Shared payload toggling for every variant family: the manipulators (StyleVariantManipulator,
    // StyleConditionalVariantManipulator, StyleRelationalVariantManipulator, the child / stacked / has-
    // ones) and the side-table passes the reconciler drives itself (structural, has-[.class]:,
    // data-/aria-, supports-).
    // A variant payload is an ordinary utility: a USS class (bg-blue-500) toggled on the class
    // list, or an arbitrary value (w-[200px]) applied as an inline style.
    internal static class StyleVariantPayload
    {
        // Applies (when on is true) or clears each payload on target.
        // A payload containing [ that parses as an arbitrary value is applied as an inline style at
        // priority; otherwise it is projected onto the class list at that same priority. Either way the
        // payload layers over the base and the lower-priority variants rather than tying with them, and
        // turning it off falls back to whatever is still active.
        public static void Apply(VisualElement target, string?[] payloads, bool on,
            int priority = StyleLayerPriority.Base,
            ReconcilerContext? ctx = null, object? owner = null)
        {
            if (target == null || payloads == null)
            {
                return;
            }

            // Set when this call toggled a layout gate token (gap / grid / divide / text-balance) on the
            // live class list. Signalled once after the whole payload array rather than per token, so
            // `md:grid md:grid-cols-3` re-derives the grid from its FINAL token set instead of first
            // building a one-column grid from the half-applied one.
            var layoutGateChanged = false;
            // A USS-spelled width payload has to re-sync the layout manipulators: it decides whether
            // text-balance stands down, and it loses to balance's own inline width, so it moves nothing the
            // engine would raise a geometry event for. Kept out of the gate set above because it needs only
            // the trigger, not the per-patch live-list routing that set also turns on. The inline-resolved
            // spellings need nothing here — the manipulator's own layer probe sees both of their edges.
            var reSyncOnly = false;

            foreach (var payload in payloads)
            {
                if (string.IsNullOrEmpty(payload))
                {
                    continue;
                }

                // Stacked variant (e.g. the `hover:bg-red` remainder of `dark:hover:bg-red`): the outer
                // manipulator's gate has flipped; defer to a nested manipulator that ANDs the inner variant's
                // own signal with this outer gate. Falls back to the plain leaf path when no registry is
                // available (the parameterless callers and the leaf-path unit tests).
                if (ctx != null && owner != null && StyleVariantClass.IsVariant(payload))
                {
                    ctx.GateStackedVariant(target, owner, payload, on, priority);
                    continue;
                }

                // The important modifier on a variant payload (hover:!bg-red, focus:bg-red!): strip the
                // bang and, when present, raise this payload into the important band so it wins conflicts.
                var core = StyleArbitraryValueResolver.StripImportant(payload, out var important);
                if (string.IsNullOrEmpty(core))
                {
                    continue;
                }
                var effectivePriority = important ? StyleLayerPriority.ImportantOf(priority) : priority;

                if (StyleArbitraryValueResolver.IsInlineResolved(core)
                    && StyleArbitraryValueResolver.TryParse(core, out var style))
                {
                    if (on)
                    {
                        StyleArbitraryValueResolver.Apply(target, in style, effectivePriority);
                    }
                    else
                    {
                        StyleArbitraryValueResolver.Clear(target, in style, effectivePriority);
                    }
                }
                else if (!on && StyleArbitraryValueResolver.TryClearUnregisteredFilterToken(target, core, effectivePriority))
                {
                    // The off-toggle of a filter-[name:args] payload whose name was unregistered while
                    // the layer was active — the shared clear resolves the name syntactically and
                    // removes the mirrored class (see TryClearUnregisteredFilterToken).
                }
                else if (on)
                {
                    StyleClassProjection.Add(target, core, effectivePriority);
                    layoutGateChanged |= TrackLayoutGate(ctx, target, core, true);
                    reSyncOnly |= StyleTextBalanceClass.IsWidthDeclaringToken(core);
                }
                else
                {
                    StyleClassProjection.Remove(target, core, effectivePriority);
                    layoutGateChanged |= TrackLayoutGate(ctx, target, core, false);
                    reSyncOnly |= StyleTextBalanceClass.IsWidthDeclaringToken(core);
                }

                // A clip-path payload (hover:clip-path-[…], dark:/first:clip-path-[…], …) was just toggled as a class,
                // but UITK has no clip-path property — the class alone does nothing. Re-resolve the element's
                // clip wrapper mask from its (now updated) live class list. The wrapper already exists (the
                // create/patch wrap gate sees the variant clip), so this only swaps the cached mask.
                if (ctx != null && StyleClipPathClass.IsClipPathClass(core))
                {
                    ctx.ClipPathReResolve?.Invoke(target);
                }
            }

            if (layoutGateChanged || reSyncOnly)
            {
                // A gap / grid / divide / text-balance class just appeared on (or left) the live class list
                // without passing through the reconciler, so the manipulators those tokens gate must be
                // re-derived here — nothing else will run until the element's next patch, which may never
                // come (a breakpoint crossing re-renders nothing).
                ctx?.LayoutManipulatorReSync?.Invoke(target);
            }
        }

        // Records a toggled payload that is one of the layout gate tokens, returning true when the tracked
        // set changed. Returns false without touching anything for the parameterless callers (no context to
        // record into) and for the overwhelmingly common non-layout payload.
        private static bool TrackLayoutGate(ReconcilerContext? ctx, VisualElement target, string core, bool on)
            => ctx != null && IsLayoutGateToken(core) && ctx.TrackVariantLayoutClass(target, core, on);

        // The utility tokens whose mere PRESENCE decides whether a layout manipulator exists on an element
        // (FiberNodePatcher.ApplyLayoutManipulators). Each family answers for its own prefix set so this
        // gate cannot drift from the array scans the manipulator passes run.
        //
        // Deliberately NOT the same set as the re-sync trigger: entering this one also routes the element
        // to the live-class-list path on every later patch, which costs an array per patch, and that is
        // only worth paying for a token a manipulator pass has to READ back out of the class array. A width
        // payload needs the trigger and nothing else — text-balance reads the live list directly.
        private static bool IsLayoutGateToken(string core)
            => StyleGapClass.IsGapToken(core)
                || StyleGridClass.IsGridToken(core)
                || StyleDivideClass.IsDivideToken(core)
                || StyleTextBalanceClass.IsTextBalanceToken(core);
    }
}
