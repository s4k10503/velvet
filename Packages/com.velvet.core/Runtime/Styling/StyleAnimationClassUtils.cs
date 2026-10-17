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
    // A class lands where a payload of it would (StyleVariantPayload.DestinationOf): a field control's surface
    // utility on its input box. With the element's reconciler context, a gate token is told to the composed
    // class source as a variant payload is (ReconcilerContext.TrackPlayGate) and the class-driven passes re-run,
    // so a play's gap-*, shadow-* or skew-* applies while the play shows it. A bracket value a preset play shows
    // (realiseInline) is applied as an inline layer at the play's priority beside its list entry. A Motion's own
    // poses leave theirs to the reconciler's inline hold (FiberNodePatcher.HoldInlineForEnter, PlanInlineExit),
    // which writes them at the base priority.
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
            var changes = new Changes(element);
            foreach (var cls in classes)
            {
                var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
                var dest = StyleVariantPayload.DestinationOf(ctx, element, core, on: true);
                CornerRadiusFit.TrackClass(dest, cls);
                Add(dest, cls, priority);
                if (realiseInline && priority != StyleLayerPriority.Base)
                {
                    ShowInline(dest, core, priority);
                }
                changes.Note(dest, ctx != null && ctx.TrackPlayGate(dest, core, priority, on: true), core);
            }
            changes.Settle(ctx);
        }

        // Ranks classes a play leaves on the element as the element's own from now on.
        internal static void Rest(VisualElement element, string[]? classes, ReconcilerContext? ctx = null)
        {
            if (classes == null) return;
            var changes = new Changes(element);
            foreach (var cls in classes)
            {
                var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
                var dest = StyleVariantPayload.DestinationOf(ctx, element, core, on: true);
                StyleClassProjection.RawRested(dest, cls);
                changes.Note(dest, ctx != null && ctx.RestPlayGate(dest, core), core);
            }
            changes.ReSync(ctx);
        }

        // Removes each class, except one kept names, which is added instead: a play may have removed it
        // before the caller that keeps it recorded it as present.
        internal static void RemoveClasses(VisualElement element, string[]? classes, string[]? kept = null,
            ReconcilerContext? ctx = null)
        {
            if (classes == null) return;
            var changes = new Changes(element);
            foreach (var cls in classes)
            {
                var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
                var keep = kept != null && System.Array.IndexOf(kept, cls) >= 0;
                var dest = StyleVariantPayload.DestinationOf(ctx, element, core, on: keep);
                if (keep)
                {
                    Add(dest, cls, StyleLayerPriority.Base);
                    changes.Note(dest, ctx != null && ctx.TrackPlayGate(dest, core, StyleLayerPriority.Base, on: true),
                        core);
                }
                else
                {
                    dest.RemoveFromClassList(cls);
                    StyleClassProjection.RawRemoved(dest, cls);
                    changes.Note(dest,
                        ctx != null && ctx.TrackPlayGate(dest, core, StyleLayerPriority.Animation, on: false), core);
                }
                HideInline(dest, core);
            }
            changes.Settle(ctx);
        }

        // Drops the record of the play layers an element shows, which go with the element's layers
        // (StyleArbitraryValueResolver.ClearAll).
        internal static void ForgetShownLayers(VisualElement element) => s_inlineShown.Remove(element);

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

        // The element a call wrote to and, at most, its input box, each with whether a gate token moved there.
        // A clip-path class moving re-resolves the mask of a clip wrapper the element already has, as a payload's
        // does (StyleVariantPayload.Apply); a play builds none.
        private struct Changes
        {
            private readonly VisualElement _control;
            private VisualElement? _box;
            private bool _controlGate;
            private bool _boxGate;
            private bool _clip;

            public Changes(VisualElement control)
            {
                _control = control;
                _box = null;
                _controlGate = false;
                _boxGate = false;
                _clip = false;
            }

            public void Note(VisualElement dest, bool gateChanged, string core)
            {
                _clip |= ReferenceEquals(dest, _control) && StyleClipPathClass.IsClipPathClass(core);
                if (ReferenceEquals(dest, _control))
                {
                    _controlGate |= gateChanged;
                    return;
                }
                _box = dest;
                _boxGate |= gateChanged;
            }

            public void Settle(ReconcilerContext? ctx)
            {
                Settle(_control, ctx, _controlGate);
                if (_clip)
                {
                    ctx?.ClipPathReResolve?.Invoke(_control);
                }
                if (_box != null)
                {
                    Settle(_box, ctx, _boxGate);
                }
            }

            public void ReSync(ReconcilerContext? ctx)
            {
                if (_controlGate)
                {
                    ctx!.VariantGatedReSync?.Invoke(_control);
                }
                if (_boxGate)
                {
                    ctx!.VariantGatedReSync?.Invoke(_box!);
                }
            }

            private static void Settle(VisualElement element, ReconcilerContext? ctx, bool gateChanged)
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
}
