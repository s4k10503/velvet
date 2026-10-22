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
    // A class lands where a payload of it would (StyleVariantPayload.Apply): a field control's surface utility on
    // its input box, a utility both take on each, and a text-overflow utility on the box's text as well, with the
    // clip it implies. With the element's reconciler context, a gate token is told to the composed
    // class source as a variant payload is (ReconcilerContext.TrackPlayGate) and the class-driven passes re-run,
    // so a play's gap-*, shadow-* or skew-* applies while the play shows it. A bracket value a preset play shows
    // (realiseInline) is applied as an inline layer at the play's priority beside its list entry. A Motion's own
    // poses leave theirs to the reconciler's inline hold (FiberNodePatcher.HoldInlineForEnter, PlanInlineExit),
    // which writes them at the base priority.
    internal static class StyleAnimationClassUtils
    {
        // The bracket values each element shows as an inline layer for a play, so only a layer this applied
        // is cleared: a clear with no layer behind it rewrites the property from the element's other layers.
        private static readonly ConditionalWeakTable<VisualElement, Dictionary<string, long>> s_inlineShown = new();

        // priority is StyleLayerPriority.Animation for the classes a running play shows, Exit for an exit's, and the
        // base one for a class that rests on the element.
        internal static void AddClasses(VisualElement element, string[] classes, long priority = StyleLayerPriority.Base,
            ReconcilerContext? ctx = null, bool realiseInline = false)
        {
            var changes = new Changes(element);
            foreach (var cls in classes)
            {
                var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
                var dest = StyleVariantPayload.DestinationOf(ctx, element, core, on: true);
                CornerRadiusFit.TrackClass(dest, cls);
                Add(dest, cls, priority);
                if (realiseInline && priority != StyleLayerPriority.Base)
                {
                    ShowInline(dest, core, priority);
                }
                changes.Note(dest, ctx != null && ctx.TrackPlayGate(dest, core, priority, on: true), core);
                var also = Companions.Of(element, dest, core, important);
                if (also.Shared != null)
                {
                    Add(also.Shared, cls, priority);
                    if (realiseInline && priority != StyleLayerPriority.Base)
                    {
                        ShowInline(also.Shared, core, priority);
                    }
                    changes.Note(also.Shared, ctx != null && ctx.TrackPlayGate(also.Shared, core, priority, on: true),
                        core);
                }
                if (also.Text != null)
                {
                    Add(also.Text, cls, priority);
                    if (also.Clip != null)
                    {
                        Add(also.Text, also.Clip, priority);
                        Add(dest, also.Clip, priority);
                    }
                }
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
                var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
                var dest = StyleVariantPayload.DestinationOf(ctx, element, core, on: true);
                StyleClassProjection.RawRested(dest, cls);
                changes.Note(dest, ctx != null && ctx.RestPlayGate(dest, core), core);
                var also = Companions.Of(element, dest, core, important);
                if (also.Shared != null)
                {
                    StyleClassProjection.RawRested(also.Shared, cls);
                    changes.Note(also.Shared, ctx != null && ctx.RestPlayGate(also.Shared, core), core);
                }
                if (also.Text != null)
                {
                    StyleClassProjection.RawRested(also.Text, cls);
                    if (also.Clip != null)
                    {
                        StyleClassProjection.RawRested(also.Text, also.Clip);
                        StyleClassProjection.RawRested(dest, also.Clip);
                    }
                }
            }
            changes.ReSync(ctx);
        }

        // Removes each class, except one kept names, which is added instead: a play may have removed it
        // before the caller that keeps it recorded it as present. A class a variant payload in force still holds
        // stays too, as its variant rule still applies once the plain one is gone.
        internal static void RemoveClasses(VisualElement element, string[]? classes, string[]? kept = null,
            ReconcilerContext? ctx = null)
        {
            if (classes == null) return;
            var changes = new Changes(element);
            foreach (var cls in classes)
            {
                var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
                var keep = kept != null && System.Array.IndexOf(kept, cls) >= 0;
                var dest = StyleVariantPayload.DestinationOf(ctx, element, core, on: keep);
                var also = Companions.Of(element, dest, core, important);
                Settle(dest, cls, core, keep, ctx, ref changes);
                if (also.Shared != null)
                {
                    Settle(also.Shared, cls, core, keep, ctx, ref changes);
                }
                if (also.Text != null)
                {
                    Settle(also.Text, cls, keep);
                    // A clip the element's own classes still name or imply stays, as the class it came with would.
                    if (also.Clip != null)
                    {
                        var keepClip = keep || ClipStays(dest, also.Text, kept);
                        Settle(also.Text, also.Clip, keepClip);
                        Settle(dest, also.Clip, keepClip);
                    }
                }
            }
            changes.Settle(ctx);
        }

        // The text-overflow utilities: truncate carries the box's clip, the others imply it
        // (StyleInputBoxSurface.ImpliesBoxClip).
        private static readonly string[] s_clipping = { "truncate", "text-ellipsis", "text-clip" };

        // Whether the box's clip has a source besides the class being taken off: a class the element keeps that is
        // the clip or implies it, or a text-overflow utility still on the box or the text it draws, where a
        // reconciled one lands (StyleInputBoxSurface.TryGetBoxText).
        private static bool ClipStays(VisualElement box, VisualElement text, string[]? kept)
        {
            if (kept != null)
            {
                foreach (var cls in kept)
                {
                    var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
                    if (core == StyleInputBoxSurface.BoxClipCore || StyleInputBoxSurface.ImpliesBoxClip(core))
                    {
                        return true;
                    }
                }
            }
            foreach (var cls in s_clipping)
            {
                if (box.ClassListContains(cls) || text.ClassListContains(cls))
                {
                    return true;
                }
            }
            return false;
        }

        private static void Settle(VisualElement dest, string cls, string core, bool keep, ReconcilerContext? ctx,
            ref Changes changes)
        {
            Settle(dest, cls, keep);
            changes.Note(dest, ctx != null && ctx.TrackPlayGate(dest, core, StyleLayerPriority.Base, on: keep), core);
            HideInline(dest, core);
        }

        // Puts a kept class back at the base priority, else takes it off as the writer that put it on.
        private static void Settle(VisualElement dest, string cls, bool keep)
        {
            if (keep)
            {
                Add(dest, cls, StyleLayerPriority.Base);
            }
            else if (StyleClassProjection.HoldsUnderVariant(dest, cls))
            {
                StyleClassProjection.RawReleased(dest, cls);
            }
            else
            {
                dest.RemoveFromClassList(cls);
                StyleClassProjection.RawRemoved(dest, cls);
            }
        }

        // Drops the record of the play layers an element shows, which go with the element's layers
        // (StyleArbitraryValueResolver.ClearAll).
        internal static void ForgetShownLayers(VisualElement element) => s_inlineShown.Remove(element);

        // A class the projection took off the element is not put back here: the projection puts it back itself,
        // once the rank RawAdded gives it outranks what took it, or once that goes.
        private static void Add(VisualElement element, string cls, long priority)
        {
            if (!StyleClassProjection.Suppresses(element, cls))
            {
                element.AddToClassList(cls);
            }
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
            s_inlineShown.GetOrCreateValue(element)[core] = priority;
        }

        // A play's layer is keyed by the priority it was shown at, whichever priority the class leaves at.
        private static void HideInline(VisualElement element, string core)
        {
            if (!s_inlineShown.TryGetValue(element, out var shown) || !shown.Remove(core, out var priority)
                || !StyleArbitraryValueResolver.TryParseLayer(core, out var style))
            {
                return;
            }
            StyleArbitraryValueResolver.Clear(element, in style, priority);
        }

        // Where else a class lands beside its destination, as a payload of it does (StyleVariantPayload.Apply).
        private readonly struct Companions
        {
            public readonly VisualElement? Shared;
            public readonly VisualElement? Text;
            public readonly string? Clip;

            private Companions(VisualElement? shared, VisualElement? text, string? clip)
            {
                Shared = shared;
                Text = text;
                Clip = clip;
            }

            private static readonly string s_importantClip = "!" + StyleInputBoxSurface.BoxClipCore;

            public static Companions Of(VisualElement control, VisualElement dest, string core, bool important)
            {
                var shared = ReferenceEquals(dest, control) ? SharedBoxOf(control, core) : null;
                var text = StyleInputBoxSurface.TryGetBoxText(dest, core, out var found) ? found : null;
                var clip = text != null && StyleInputBoxSurface.ImpliesBoxClip(core)
                    ? important ? s_importantClip : StyleInputBoxSurface.BoxClipCore
                    : null;
                return new Companions(shared, text, clip);
            }

            private static VisualElement? SharedBoxOf(VisualElement control, string core)
                => StyleInputBoxSurface.TryGetSharedBox(control, core, out var box) ? box : null;
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
