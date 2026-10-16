using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // The add/remove helpers for a class its writer puts on and takes off the live list itself — the
    // animation scheduler's and the presence exit's — rather than as a payload. The element's class
    // projection ranks such a class without owning it (StyleClassProjection.RawAdded), and a container that
    // gives way to a class of the element's own (a divider, a space margin) re-applies after each add or
    // remove.
    //
    // With the element's reconciler context, a gate token is told to the composed class source as a variant
    // payload is (ReconcilerContext.TrackPlayGate) and the class-driven passes re-run, so a play's gap-*,
    // shadow-* or skew-* applies while the play shows it. A bracket value a preset play shows (realiseInline)
    // is applied as an inline layer at the play's priority beside its list entry. A Motion's own poses leave
    // theirs to the reconciler's inline hold (FiberNodePatcher.HoldInlineForEnter, PlanInlineExit), which
    // writes them at the base priority.
    internal static class StyleAnimationClassUtils
    {
        // The bracket values each element shows as an inline layer for a play, so only a layer this applied
        // is cleared: a clear with no layer behind it rewrites the property from the element's other layers.
        private static readonly ConditionalWeakTable<VisualElement, HashSet<string>> s_inlineShown = new();

        // priority is StyleLayerPriority.Animation for the classes a running play shows, and the base one for a
        // class that rests on the element.
        internal static void AddClasses(VisualElement element, string[] classes, long priority = StyleLayerPriority.Base,
            ReconcilerContext? ctx = null, bool realiseInline = false)
        {
            var gateChanged = false;
            foreach (var cls in classes)
            {
                CornerRadiusFit.TrackClass(element, cls);
                Add(element, cls, priority);
                var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
                if (realiseInline && priority != StyleLayerPriority.Base)
                {
                    ShowInline(element, core, priority);
                }
                gateChanged |= ctx != null && ctx.TrackPlayGate(element, core, priority, on: true);
            }
            Changed(element, ctx, gateChanged);
        }

        // Ranks classes a play leaves on the element as the element's own from now on.
        internal static void Rest(VisualElement element, string[]? classes, ReconcilerContext? ctx = null)
        {
            if (classes == null) return;
            var gateChanged = false;
            foreach (var cls in classes)
            {
                StyleClassProjection.RawRested(element, cls);
                gateChanged |= ctx != null
                    && ctx.RestPlayGate(element, StyleArbitraryValueResolver.StripImportant(cls, out _));
            }
            if (gateChanged)
            {
                ctx!.VariantGatedReSync?.Invoke(element);
            }
        }

        // Removes each class, except one kept names, which is added instead: a play may have removed it
        // before the caller that keeps it recorded it as present.
        internal static void RemoveClasses(VisualElement element, string[]? classes, string[]? kept = null,
            ReconcilerContext? ctx = null)
        {
            if (classes == null) return;
            var gateChanged = false;
            foreach (var cls in classes)
            {
                var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
                if (kept != null && System.Array.IndexOf(kept, cls) >= 0)
                {
                    Add(element, cls, StyleLayerPriority.Base);
                    gateChanged |= ctx != null && ctx.TrackPlayGate(element, core, StyleLayerPriority.Base, on: true);
                }
                else
                {
                    element.RemoveFromClassList(cls);
                    StyleClassProjection.RawRemoved(element, cls);
                    gateChanged |= ctx != null && ctx.TrackPlayGate(element, core, StyleLayerPriority.Animation, on: false);
                }
                HideInline(element, core);
            }
            Changed(element, ctx, gateChanged);
        }

        private static void Add(VisualElement element, string cls, long priority)
        {
            element.AddToClassList(cls);
            StyleClassProjection.RawAdded(element, cls, priority);
        }

        private static void ShowInline(VisualElement element, string core, long priority)
        {
            if (!StyleArbitraryValueResolver.IsInlineResolved(core)
                || !StyleArbitraryValueResolver.TryParseLayer(core, out var style))
            {
                return;
            }
            StyleArbitraryValueResolver.Apply(element, in style, priority);
            s_inlineShown.GetOrCreateValue(element).Add(core);
        }

        // A play's layer is keyed by its priority, Animation, whichever priority the class leaves at.
        private static void HideInline(VisualElement element, string core)
        {
            if (!s_inlineShown.TryGetValue(element, out var shown) || !shown.Remove(core)
                || !StyleArbitraryValueResolver.TryParseLayer(core, out var style))
            {
                return;
            }
            StyleArbitraryValueResolver.Clear(element, in style, StyleLayerPriority.Animation);
        }

        private static void Changed(VisualElement element, ReconcilerContext? ctx, bool gateChanged)
        {
            ClipPathLayoutBox.SyncClasses(element);
            StyleArbitraryValueResolver.NotifyClassesChanged(element);
            if (gateChanged)
            {
                ctx!.VariantGatedReSync?.Invoke(element);
            }
            SilhouetteFaceStash.NotifyClassesChanged(element);
        }
    }
}
