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
        // The rank a payload carries when its caller supplies no rule place.
        //
        // Deliberately the WEAKEST value rather than the strongest, so an unrankable payload loses a tie
        // instead of winning it. Two unrankable payloads tie and fall back to arrival, which within one swept
        // array is that array's own order.
        public const int NoDeclaration = int.MinValue;

        // Applies (when on is true) or clears each payload on target.
        // A payload containing [ that parses as an arbitrary value is applied as an inline style at
        // priority; otherwise it is projected onto the class list at that same priority. Either way the
        // payload layers over the base and the lower-priority variants rather than tying with them, and
        // turning it off falls back to whatever is still active.
        //
        // declarations gives each payload, by position, its rule's place among the element's rules
        // (StyleRuleOrder), which the payload's layer key carries, so two payloads at ONE rank order the way Tailwind
        // emits the two rules (see StyleLayerPriority.WithRule). It rides
        // in from the caller rather than being read back out of the class array, because the array cannot say
        // whether a rule is LIT: a `lg:shadow-lg` below the breakpoint, a `peer-checked:` with no peer, a
        // `[&>*]:shadow-lg` that lands on the children, and a `first:hover:shadow-lg` the structural config
        // refuses to register all spell a payload that never applies here, and a scan would rank the element
        // by them anyway. A caller that supplies none leaves its payloads ranked behind every declared one in
        // their band, tied among themselves and so ordered by arrival.
        public static void Apply(VisualElement target, string?[] payloads, bool on,
            long priority = StyleLayerPriority.Base,
            ReconcilerContext? ctx = null, object? owner = null, int[]? declarations = null)
        {
            if (target == null || payloads == null)
            {
                return;
            }
            var control = target;
            VisualElement? box = null;
            var boxChanged = false;

            // Set when this call toggled a gate token (see IsVariantGateToken) on the live class list.
            // Signalled once after the whole payload array rather than per token, so `md:grid md:grid-cols-3`
            // re-derives the grid from its FINAL token set instead of first building a one-column grid from
            // the half-applied one.
            var gateChanged = false;

            for (var index = 0; index < payloads.Length; index++)
            {
                var payload = payloads[index];
                if (string.IsNullOrEmpty(payload))
                {
                    continue;
                }
                var declaration = declarations != null && index < declarations.Length
                    ? declarations[index]
                    : NoDeclaration;

                // Stacked variant (e.g. the `hover:bg-red` remainder of `dark:hover:bg-red`): the outer
                // manipulator's gate has flipped; defer to a nested manipulator that ANDs the inner variant's
                // own signal with this outer gate. Falls back to the plain leaf path when no registry is
                // available (the parameterless callers and the leaf-path unit tests).
                if (ctx != null && owner != null && StyleVariantClass.IsVariant(payload))
                {
                    ctx.GateStackedVariant(control, owner, payload, on, priority, declaration);
                    continue;
                }

                // The important modifier on a variant payload (hover:!bg-red, focus:bg-red!): strip the
                // bang and, when present, raise this payload into the important band so it wins conflicts.
                var core = StyleArbitraryValueResolver.StripImportant(payload, out var important);
                if (string.IsNullOrEmpty(core))
                {
                    continue;
                }
                // The condition belongs to the control and the paint to its input box (StyleInputBoxSurface).
                var dest = DestinationOf(ctx, control, core, on);
                var effectivePriority = important ? StyleLayerPriority.ImportantOf(priority) : priority;

                var payloadGated = ToggleResolvedPayload(dest, payload, on, effectivePriority, ctx, declaration);
                if (ReferenceEquals(dest, control))
                {
                    gateChanged |= payloadGated;
                }
                else
                {
                    box = dest;
                    boxChanged |= payloadGated;
                }

                // A clip-path payload (hover:clip-path-[…], dark:/first:clip-path-[…], …) was just toggled as a class,
                // but UITK has no clip-path property — the class alone does nothing. Re-resolve the element's
                // clip wrapper mask from its (now updated) live class list. The wrapper already exists (the
                // create/patch wrap gate sees the variant clip), so this only swaps the cached mask.
                if (ctx != null && StyleClipPathClass.IsClipPathClass(core))
                {
                    ctx.ClipPathReResolve?.Invoke(dest);
                }
            }

            if (gateChanged)
            {
                // A gate class just appeared on (or left) the live class list without passing through the
                // reconciler, so the layout manipulators and paint layers those tokens gate must be
                // re-derived here — nothing else will run until the element's next patch, which may never
                // come (a breakpoint crossing re-renders nothing).
                ctx?.VariantGatedReSync?.Invoke(control);
            }
            if (boxChanged)
            {
                ctx?.VariantGatedReSync?.Invoke(box!);
            }

            // A clipped element's wrapper lays the element out from the same classes, so it takes the toggle too.
            ClipPathLayoutBox.SyncClasses(control);
        }

        // Where core lands: the control's input box for a surface utility (StyleInputBoxSurface), else the
        // control. A box a gate token reaches is put on the record a paint re-sync reads first.
        private static VisualElement DestinationOf(ReconcilerContext? ctx, VisualElement control, string core,
            bool on)
        {
            var dest = StyleInputBoxSurface.DestinationFor(control, core);
            if (ctx != null && on && IsVariantGateToken(core) && StyleInputBoxSurface.IsBox(dest))
            {
                StyleInputBoxSurface.EnsurePaintState(ctx, dest);
            }
            return dest;
        }

        // Applies or clears one payload whose priority is already resolved, reporting back whether a gate token
        // moved, which the caller must re-sync once the whole array is through.
        private static bool ToggleResolvedPayload(
            VisualElement target, string payload, bool on, long effectivePriority,
            ReconcilerContext? ctx, int declaration)
        {
            var core = StyleArbitraryValueResolver.StripImportant(payload, out _);
            // A payload's layer, class slot and gate slot are keyed by the rule as well as its rank, so a rule
            // turning off clears its own value and not that of another rule on the same rank (nth-1: beside
            // nth-2:), and two that hold at once order by the place StyleRuleOrder gave the rule's declaration.
            var key = StyleLayerPriority.WithRule(effectivePriority, declaration);
            if (StyleArbitraryValueResolver.IsInlineResolved(core)
                && StyleArbitraryValueResolver.TryParse(core, out var style))
            {
                if (on)
                {
                    StyleArbitraryValueResolver.Apply(target, in style, key);
                }
                else
                {
                    StyleArbitraryValueResolver.Clear(target, in style, key);
                }
                return false;
            }

            // font-[…] / leading-[…]: no arbitrary property claims either prefix, so both fall past the
            // branch above, and their own resolvers read them back out of the composed class source instead.
            // Tracked but never projected, so the variant path leaves no bracket token on the USS class list
            // — the same exclusion the reconciled path applies at FiberNodePatcher.IsManipulatorOwnedClass.
            if (StyleFontClass.IsArbitraryFontClass(core)
                || StyleTextEffectClass.IsArbitraryLeadingClass(core))
            {
                return TrackVariantGate(ctx, target, payload, key, declaration, on);
            }

            // The off-toggle of a filter-[name:args] payload whose name was unregistered while the layer was
            // active — the shared clear resolves the name syntactically and removes the mirrored class (see
            // TryClearUnregisteredFilterToken).
            if (!on && StyleArbitraryValueResolver.TryClearUnregisteredFilterToken(target, core, key))
            {
                return false;
            }

            if (on)
            {
                StyleClassProjection.Add(target, core, key);
            }
            else
            {
                StyleClassProjection.Remove(target, core, key);
            }

            return TrackVariantGate(ctx, target, payload, key, declaration, on);
        }

        // True when any token in classNames is a VARIANT whose payload is a gate token — the question "could
        // a toggle on this element ever change what a class-driven pass builds", which the reconciler asks so
        // it can put the array aside for a re-sync that will have none of its own.
        //
        // Peel recognized variant layers with their own parsers, which preserve colons inside bracketed
        // payloads such as font-[weight:700] and font-[addr:Fonts/Body]. A bare arbitrary utility has no
        // variant layer, so its internal colon must not open a variant source entry.
        public static bool DeclaresGatePayload(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                var payload = cls;
                var variant = false;
                while (StyleVariantClass.TryParse(payload, out _, out var next)
                    || StyleHasVariantClass.TryParse(payload, out _, out _, out next)
                    || StyleAttributeVariantClass.TryParse(payload, out _, out _, out _, out next)
                    || StyleStructuralVariantClass.TryParse(payload, out _, out _, out next)
                    || StyleSupportsVariantClass.TryParse(payload, out _, out _, out next)
                    || StyleChildVariantClass.TryParse(payload, out next))
                {
                    variant = true;
                    payload = next ?? string.Empty;
                }
                var core = StyleArbitraryValueResolver.StripImportant(payload, out _);
                if (variant && IsVariantGateToken(core))
                {
                    return true;
                }
            }
            return false;
        }

        // Records a toggled payload that is one of the gate tokens, returning true when the tracked set
        // changed. Returns false without touching anything for the parameterless callers (no context to
        // record into) and for the overwhelmingly common non-gate payload. An important font, text-effect, gap,
        // divide or pointer-events token keeps its bang, which is what lets it outrank the element's own important
        // base token in the composed source; the other gate families read the bare core.
        private static bool TrackVariantGate(ReconcilerContext? ctx, VisualElement target, string payload,
            long priority, int declaration, bool on)
        {
            var core = StyleArbitraryValueResolver.StripImportant(payload, out _);
            return ctx != null && IsVariantGateToken(core)
                && ctx.TrackVariantGateClass(target, IsImportanceAware(core) ? payload : core,
                    priority, declaration, on);
        }

        private static bool IsImportanceAware(string core)
            => StyleFontClass.IsFontToken(core) || StyleTextEffectClass.IsTextEffectToken(core)
                || StylePointerEventsClass.IsPointerEventsToken(core)
                || (StyleGapClass.IsGapToken(core) && !StyleGapClass.IsSpaceToken(core))
                || StyleDivideClass.IsDivideToken(core);

        // The utility tokens whose mere PRESENCE in a class array decides what a class-driven pass builds:
        // the three layout manipulators and the pointer-events scope (FiberNodePatcher.ApplyLayoutManipulators),
        // the six wrapper-less paint layers (FiberNodePatcher.ApplyResolvedClassPasses), the inline font layer
        // (FiberNodePatcher.ApplyFontLayer) and the text-effect cascade (FiberNodePatcher.ApplyTextEffects).
        // Each family answers for its own prefix set so this gate cannot drift from the array scans those
        // passes run.
        //
        // Deliberately NOT the same set as the re-sync trigger: entering this one also routes the element
        // to the composed class source on every later patch, which costs an array per patch, and that is
        // only worth paying for a token a pass has to READ back out of the class array.
        private static bool IsVariantGateToken(string core)
            => StyleGapClass.IsGapToken(core)
                || StyleGridClass.IsGridToken(core)
                || StyleDivideClass.IsDivideToken(core)
                || StylePointerEventsClass.IsPointerEventsToken(core)
                || StyleSkewClass.IsSkewClass(core)
                || StyleShadowClass.IsShadowClass(core)
                || StyleGradientClass.IsGradientClass(core)
                || StyleAnimateClass.IsAnimateClass(core)
                || StyleBorderStyleClass.IsBorderStyleClass(core)
                || StyleRingClass.IsRingClass(core)
                || StyleFontClass.IsFontToken(core)
                || StyleTextEffectClass.IsTextEffectToken(core);
    }
}
