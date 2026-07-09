using UnityEngine.UIElements;

namespace Velvet
{
    // Shared payload toggling for the variant manipulators (StyleVariantManipulator,
    // StyleConditionalVariantManipulator, StyleRelationalVariantManipulator).
    // A variant payload is an ordinary utility: a USS class (bg-blue-500) toggled on the class
    // list, or an arbitrary value (w-[200px]) applied as an inline style.
    internal static class StyleVariantPayload
    {
        // Applies (when on is true) or clears each payload on target.
        // A payload containing [ that parses as an arbitrary value is applied as an inline style at
        // priority (so a state variant layers over the base / lower-priority variants
        // rather than wiping the property when it turns off); otherwise it is toggled as a USS class.
        public static void Apply(VisualElement target, string?[] payloads, bool on,
            int priority = StyleLayerPriority.Base,
            ReconcilerContext? ctx = null, object? owner = null)
        {
            if (target == null || payloads == null)
            {
                return;
            }

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
                // bang and, when present, raise this payload to the Important layer so it wins conflicts.
                var core = StyleArbitraryValueResolver.StripImportant(payload, out var important);
                if (string.IsNullOrEmpty(core))
                {
                    continue;
                }
                var effectivePriority = important ? StyleLayerPriority.Important : priority;

                if (StyleArbitraryValueResolver.IsInlineResolved(core)
                    && StyleArbitraryValueResolver.TryParse(core, out var style))
                {
                    if (on)
                    {
                        StyleArbitraryValueResolver.Apply(target, in style, effectivePriority);
                    }
                    else
                    {
                        StyleArbitraryValueResolver.Clear(target, style.Property, effectivePriority);
                    }
                }
                else if (on)
                {
                    target.AddToClassList(core);
                }
                else
                {
                    target.RemoveFromClassList(core);
                }

                // A clip-path payload (hover:clip-path-[…], dark:clip-path-[…], …) was just toggled as a class,
                // but UITK has no clip-path property — the class alone does nothing. Re-resolve the element's
                // clip wrapper mask from its (now updated) live class list. The wrapper already exists (the
                // create/patch wrap gate sees the variant clip), so this only swaps the cached mask.
                if (ctx != null && StyleClipPathClass.IsClipPathClass(core))
                {
                    ctx.ClipPathReResolve?.Invoke(target);
                }
            }
        }
    }
}
