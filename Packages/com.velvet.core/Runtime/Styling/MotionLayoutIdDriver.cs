#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Shared-element layout animation via FLIP (First-Last-Invert-Play). When a V.Motion(layoutId:)
    // patches at a box different from the one the SAME id stood at — including across a DIFFERENT
    // physical element entirely, e.g. after a same-key type flip or a move to a different parent — it
    // tweens from the old box to the new one instead of jump-cutting: capture the OLD box, let this
    // frame's layout settle at the NEW one, and draw the element over the old box with an inline translate
    // and uniform scale that a spring then carries back to its layout.
    //
    // A projection draws the element at its natural box divided by the scale its projected ancestors are
    // drawn at, about the parent's corner, so a layoutId Motion inside a growing or shrinking one keeps its
    // own size and its offset from that corner on every frame. Its natural box is lerped from the old box to
    // the layout by one progress spring. One frame per panel steps every projection and then writes them,
    // each after its ancestors, since a write reads the scale its ancestors are drawn at in that frame.
    //
    // A box is the rect an element is drawn at inside its parent, together with that parent, its centre and
    // size in panel space, and the scale its projected ancestors were drawn at. A box read under this same
    // parent is compared relative to the parent's drawn corner; any other is taken into the new parent
    // through the parent's panel transform. A box in LayoutIdRegistry forgets
    // its parent when the pool takes that parent back (ForgetParent), since the pool can hand it to another
    // Motion's element before the box is claimed.
    // Panel space throughout was rejected: an inner layoutId Motion that moves inside an outer one still
    // tweening then no longer tweens by its own move inside it
    // (Given_AnOuterLayoutIdMotionStillTweening_When_OnlyTheInnerMovesInsideIt_Then_TheInnerTweensOnlyItsOwnMove).
    // The centre is mapped as a point and the size through each parent's scale factors rather than as a
    // rect: a rect's panel mapping is the bounding box of its transformed corners, which a rotated ancestor
    // inflates
    // (Given_ALayoutIdMotionInARotatedBoard_When_ItMovesToTheOtherColumn_Then_ItTweensFromItsOldPlaceAtItsOwnSize).
    //
    // Scope: uniform scale only. A non-uniform rect change (width and height scale by different factors)
    // averages the two axis scale factors into one uniform factor instead of distorting the element on two
    // independent axes.
    internal static class MotionLayoutIdDriver
    {
        // An edge this close to its layout, in pixels, and moving this slowly ends a projection's spring.
        private const float RestPixels = 0.1f;

        private static int s_pass;
        private static readonly List<VisualElement> s_ended = new();

        // Called from FiberNodePatcher.PatchMotion for a MotionNode carrying a LayoutId, once the
        // patch's own class/style/children work is done. element.layout still holds the PRE-patch
        // resolved rect at this point (this frame's Yoga pass has not run yet) — capturing it now is the
        // same "read .layout synchronously before the mutation that invalidates it" pattern
        // GeneralPathReconciler.PinExitingChildOutOfFlow uses for a PopLayout exit. The NEW rect is not
        // trustworthy yet (a reparented/freshly-created element's .layout stays stale until the next
        // Yoga pass — see FiberWrapperElementAppliers's clip-wrapper comment on the same window), so it
        // is captured on this element's own first post-patch GeometryChangedEvent instead.
        internal static void OnPatched(VisualElement element, string layoutId, float stiffness, float damping, float mass, ReconcilerContext ctx)
        {
            // The old box is read off whichever element the id is registered to — this one, or the one it
            // replaces, which teardown has not reached yet — rather than stored at registration: a freshly
            // created element registers before its first layout, with no box to store, and a stored zero
            // rect reads as a real box at the parent's origin. The box an entry carries is the fallback for
            // an element not laid out yet, and the whole entry once teardown has taken its element.
            LayoutIdBox? oldBox = null;
            if (ctx.LayoutIdRegistry.TryGetValue(layoutId, out var previous))
            {
                oldBox = previous.Element != null && TryReadBox(previous.Element, ctx, out var live) ? live : previous.Box;
            }

            ctx.ElementToLayoutId[element] = layoutId;
            ctx.LayoutIdRegistry[layoutId] = (element, oldBox);

            // A second patch before a layout settles the first replaces its wait rather than adding one.
            CancelPendingSettle(element, ctx);
            if (oldBox is not { } fromBox) return;

            var pending = new LayoutIdPendingSettle(fromBox, element.layout, stiffness, damping, mass);
            pending.Callback = _ => Settle(element, ctx);
            element.RegisterCallback(pending.Callback);
            ctx.LayoutIdPendingSettles[element] = pending;
        }

        private static void Settle(VisualElement element, ReconcilerContext ctx)
        {
            if (!ctx.LayoutIdPendingSettles.TryGetValue(element, out var pending)) return;
            // An ancestor whose layout moved settles in this same layout pass, and this element's old box is
            // taken into the frame that ancestor is drawn in, so the ancestor goes first whichever event fires
            // first.
            SettleMovedAncestors(element, ctx);
            CancelPendingSettle(element, ctx);
            ForgetFallbackParent(element, ctx);
            Start(element, pending, ctx);
        }

        private static void SettleMovedAncestors(VisualElement element, ReconcilerContext ctx)
        {
            List<VisualElement>? moved = null;
            for (var ancestor = element.hierarchy.parent; ancestor != null; ancestor = ancestor.hierarchy.parent)
            {
                if (ctx.LayoutIdPendingSettles.TryGetValue(ancestor, out var pending)
                    && IsFiniteRect(ancestor.layout) && !SameRect(ancestor.layout, pending.PatchedLayout))
                {
                    (moved ??= new List<VisualElement>()).Add(ancestor);
                }
            }
            if (moved == null) return;
            for (var i = moved.Count - 1; i >= 0; i--)
            {
                Settle(moved[i], ctx);
            }
        }

        // Once the element is laid out, a read takes its own box for as long as it stays in the tree, and the
        // parent the fallback box names would keep that parent alive, removed or not
        // (Given_ALayoutIdMotionThatLeftARemovedParent_When_ItsTweenStarts_Then_ItsIdNoLongerHoldsThatParent).
        private static void ForgetFallbackParent(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.ElementToLayoutId.TryGetValue(element, out var layoutId)
                && ctx.LayoutIdRegistry.TryGetValue(layoutId, out var entry)
                && ReferenceEquals(entry.Element, element) && entry.Box is { } box)
            {
                ctx.LayoutIdRegistry[layoutId] = (element, box.Detached());
            }
        }

        private static void Start(VisualElement element, LayoutIdPendingSettle pending, ReconcilerContext ctx)
        {
            var host = element.panel?.visualTree;
            var parent = element.hierarchy.parent;
            var layout = element.layout;
            if (host == null || parent == null || !IsFiniteRect(layout)) return;

            var parentScale = AncestorScale(parent, ctx);
            // Taken to undistorted units: a box read under this parent was drawn at the scale its ancestors had
            // when it was read, and one mapped in from elsewhere at the scale they have now.
            var readScale = ReferenceEquals(pending.From.Parent, parent) ? pending.From.AncestorScale : parentScale;
            var drawnFrom = FromRect(element, pending.From, ctx);
            var from = new Rect(drawnFrom.position * readScale, drawnFrom.size * readScale);
            var moves = !ComputeDeltaPlan(from, layout, TransformOrigin(element)).IsEmpty;

            ctx.LayoutIdProjections.TryGetValue(element, out var projection);
            if (!moves && projection == null && IsUnit(parentScale)) return;
            projection ??= CreateProjection(element, ctx);
            projection.From = from;
            projection.Springing = moves;
            projection.Spring = new SpringIntegrator(1f);
            projection.Stiffness = pending.Stiffness;
            projection.Damping = pending.Damping;
            projection.Mass = pending.Mass;
            projection.Rest = RestPixels / Mathf.Max(EdgeTravel(from, layout), RestPixels);
            Project(host, ctx);
            EnsureFrame(host, ctx);
        }

        // The old box in the frame of the element's current parent.
        private static Rect FromRect(VisualElement element, LayoutIdBox fromBox, ReconcilerContext ctx)
        {
            var parent = element.hierarchy.parent!;
            if (ReferenceEquals(fromBox.Parent, parent)) return fromBox.Local;
            var world = parent.worldTransform;
            Vector2 centre = world.inverse.MultiplyPoint3x4(fromBox.PanelCentre);
            var size = fromBox.PanelSize / AxisLengths(world);
            return new Rect(centre - size / 2f, size);
        }

        private static LayoutIdProjection CreateProjection(VisualElement element, ReconcilerContext ctx)
        {
            var translate = element.style.translate;
            var scale = element.style.scale;
            var resolved = element.resolvedStyle;
            var projection = new LayoutIdProjection(translate, scale,
                translate.keyword == StyleKeyword.Undefined ? Pixels(translate.value, element.layout) : resolved.translate,
                scale.keyword == StyleKeyword.Undefined ? scale.value.value : resolved.scale.value)
            {
                From = element.layout,
            };
            ctx.LayoutIdProjections[element] = projection;
            return projection;
        }

        // Computes and writes every projection on the panel, first giving one to each registered layoutId
        // Motion under a scaling projection that has none, whether or not anything moved it.
        private static void Project(VisualElement host, ReconcilerContext ctx)
        {
            var pass = ++s_pass;
            foreach (var element in ctx.ElementToLayoutId.Keys)
            {
                if (!ctx.LayoutIdProjections.ContainsKey(element) && element.panel?.visualTree == host
                    && element.hierarchy.parent is { } parent && !IsUnit(ProjectedScale(parent, pass, ctx)))
                {
                    CreateProjection(element, ctx);
                }
            }
            foreach (var entry in ctx.LayoutIdProjections)
            {
                if (entry.Key.panel?.visualTree == host) Compute(entry.Key, entry.Value, pass, ctx);
            }
        }

        // The product of the scales the projections on this element and its ancestors are drawn at in this pass.
        private static float ProjectedScale(VisualElement element, int pass, ReconcilerContext ctx)
        {
            var scale = 1f;
            for (VisualElement? e = element; e != null; e = e.hierarchy.parent)
            {
                if (ctx.LayoutIdProjections.TryGetValue(e, out var projection)) scale *= Compute(e, projection, pass, ctx);
            }
            return scale;
        }

        // The same product, as last drawn.
        private static float AncestorScale(VisualElement element, ReconcilerContext ctx)
        {
            var scale = 1f;
            for (VisualElement? e = element; e != null; e = e.hierarchy.parent)
            {
                if (ctx.LayoutIdProjections.TryGetValue(e, out var projection)) scale *= projection.Scale;
            }
            return scale;
        }

        private static float Compute(VisualElement element, LayoutIdProjection projection, int pass, ReconcilerContext ctx)
        {
            if (projection.Pass == pass) return projection.Scale;
            projection.Pass = pass;
            var layout = element.layout;
            if (element.hierarchy.parent is not { } parent || !IsFiniteRect(layout)) return projection.Scale;

            projection.ParentScale = ProjectedScale(parent, pass, ctx);
            var plan = ComputeDeltaPlan(Drawn(projection, layout, projection.ParentScale), layout, TransformOrigin(element));
            projection.Scale = plan.Scale?.from ?? 1f;
            Write(element, projection, new Vector2(plan.TranslateX?.from ?? 0f, plan.TranslateY?.from ?? 0f));
            return projection.Scale;
        }

        private static Rect Drawn(LayoutIdProjection projection, Rect layout, float parentScale)
        {
            var t = projection.Springing ? projection.Spring.Value : 0f;
            var position = Vector2.LerpUnclamped(layout.position, projection.From.position, t);
            var size = Vector2.LerpUnclamped(layout.size, projection.From.size, t);
            return new Rect(position / parentScale, size / parentScale);
        }

        // The element's own translate adds to the projection's and its own scale multiplies the projection's.
        // A slot is left alone until the projection first needs a value there other than the element's own.
        private static void Write(VisualElement element, LayoutIdProjection projection, Vector2 translate)
        {
            AdoptForeignWrites(element, projection);
            if (projection.WritesTranslate || translate != Vector2.zero)
            {
                if (!projection.WritesTranslate)
                {
                    projection.WritesTranslate = true;
                    MotionNativeTransitionGuard.SuspendIfIntercepted(element, projection, MotionTransitionSlots.Translate);
                }
                var own = projection.OwnTranslate;
                element.style.translate = new Translate(new Length(own.x + translate.x), new Length(own.y + translate.y), own.z);
                projection.WrittenTranslate = element.style.translate;
            }
            if (projection.WritesScale || !IsUnit(projection.Scale))
            {
                if (!projection.WritesScale)
                {
                    projection.WritesScale = true;
                    MotionNativeTransitionGuard.SuspendIfIntercepted(element, projection, MotionTransitionSlots.Scale);
                }
                var own = projection.OwnScale;
                element.style.scale = new Scale(new Vector3(own.x * projection.Scale, own.y * projection.Scale, own.z));
                projection.WrittenScale = element.style.scale;
            }
        }

        // A slot that no longer holds the last value written here was written by someone else — the element's
        // own arbitrary-value class re-applied by a patch, a drag, another driver — and that value is the
        // element's own from then on: composed with, and handed back when the projection ends.
        private static void AdoptForeignWrites(VisualElement element, LayoutIdProjection projection)
        {
            var translate = element.style.translate;
            if (projection.WritesTranslate && translate != projection.WrittenTranslate)
            {
                projection.OwnInlineTranslate = translate;
                projection.OwnTranslate = translate.keyword == StyleKeyword.Undefined ? Pixels(translate.value, element.layout) : Vector3.zero;
            }
            var scale = element.style.scale;
            if (projection.WritesScale && scale != projection.WrittenScale)
            {
                projection.OwnInlineScale = scale;
                projection.OwnScale = scale.keyword == StyleKeyword.Undefined ? scale.value.value : Vector3.one;
            }
        }

        private static void EnsureFrame(VisualElement host, ReconcilerContext ctx)
        {
            if (ctx.LayoutIdFrames.ContainsKey(host)) return;
            ctx.LayoutIdFrames[host] = host.schedule.Execute((TimerState ts) =>
            {
                var dt = ts.deltaTime / 1000f;
                if (dt <= 0f) return;
                Frame(host, dt, ctx);
            }).Every(StyleAnimateDriver.TickMs);
        }

        private static void Frame(VisualElement host, float dt, ReconcilerContext ctx)
        {
            foreach (var entry in ctx.LayoutIdProjections)
            {
                var projection = entry.Value;
                if (!projection.Springing || entry.Key.panel?.visualTree != host) continue;
                projection.Spring.Step(dt, 0f, projection.Stiffness, projection.Damping, projection.Mass);
                if (projection.Spring.IsSettled(0f, projection.Rest, projection.Rest)) projection.Springing = false;
            }
            Project(host, ctx);

            var remaining = false;
            foreach (var entry in ctx.LayoutIdProjections)
            {
                if (entry.Key.panel?.visualTree != host) continue;
                if (!entry.Value.Springing && IsUnit(entry.Value.ParentScale)) s_ended.Add(entry.Key);
                else remaining = true;
            }
            foreach (var element in s_ended)
            {
                End(element, ctx);
            }
            s_ended.Clear();
            if (!remaining && ctx.LayoutIdFrames.Remove(host, out var frame)) frame.Pause();
        }

        // Hands the slots back to what held them before the projection, and the transition suspension with them.
        private static void End(VisualElement element, ReconcilerContext ctx)
        {
            if (!ctx.LayoutIdProjections.Remove(element, out var projection)) return;
            AdoptForeignWrites(element, projection);
            if (projection.WritesTranslate) element.style.translate = projection.OwnInlineTranslate;
            if (projection.WritesScale) element.style.scale = projection.OwnInlineScale;
            StyleArbitraryValueResolver.ReapplyLayeredValues(element);
            MotionNativeTransitionGuard.Release(element, projection);
        }

        private static Vector3 Pixels(Translate translate, Rect layout) => new(
            translate.x.unit == LengthUnit.Percent ? translate.x.value / 100f * layout.width : translate.x.value,
            translate.y.unit == LengthUnit.Percent ? translate.y.value / 100f * layout.height : translate.y.value,
            translate.z);

        // How long a unit step along each local axis is in panel space.
        private static Vector2 AxisLengths(Matrix4x4 world) =>
            new(world.MultiplyVector(Vector3.right).magnitude, world.MultiplyVector(Vector3.up).magnitude);

        private static Vector2 TransformOrigin(VisualElement element) => element.resolvedStyle.transformOrigin;

        private static bool TryReadBox(VisualElement element, ReconcilerContext ctx, out LayoutIdBox box)
        {
            box = default;
            var layout = element.layout;
            if (element.hierarchy.parent is not { } parent || !IsFiniteRect(layout)) return false;
            var parentScale = AncestorScale(parent, ctx);
            var drawn = ctx.LayoutIdProjections.TryGetValue(element, out var projection) ? Drawn(projection, layout, parentScale) : layout;
            var world = parent.worldTransform;
            box = new LayoutIdBox(parent, drawn, world.MultiplyPoint3x4(drawn.center), drawn.size * AxisLengths(world), parentScale);
            return true;
        }

        private static void CancelPendingSettle(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.LayoutIdPendingSettles.Remove(element, out var pending))
            {
                element.UnregisterCallback(pending.Callback);
            }
        }

        // Called from FiberElementCleaner before an element is pooled or disposed. The pending settle goes
        // too: a pooled element keeps its callbacks, so whatever the pool hands it to next would otherwise
        // play this element's tween. Torn down inside a pass, the element leaves its box for a replacement
        // later in that render — a same-key type flip creates it after this teardown — and ExpireSnapshots
        // drops what nobody claimed where the render ends.
        internal static void CancelForTeardown(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.ElementToLayoutId.TryGetValue(element, out var layoutId)
                && ctx.LayoutIdRegistry.TryGetValue(layoutId, out var current)
                && ReferenceEquals(current.Element, element))
            {
                if (ctx.CurrentPass != null)
                {
                    ctx.LayoutIdRegistry[layoutId] = (null, TryReadBox(element, ctx, out var live) ? live : current.Box);
                    ctx.LayoutIdSnapshots.Add(layoutId);
                }
                else
                {
                    ctx.LayoutIdRegistry.Remove(layoutId);
                }
            }
            ctx.LayoutIdProjections.Remove(element);
            CancelPendingSettle(element, ctx);
        }

        // Called from FiberElementCleaner as it returns an element to the pool.
        internal static void ForgetParent(VisualElement parent, ReconcilerContext ctx)
        {
            List<string>? forgotten = null;
            foreach (var entry in ctx.LayoutIdRegistry)
            {
                if (entry.Value.Box is { } box && ReferenceEquals(box.Parent, parent))
                {
                    (forgotten ??= new List<string>()).Add(entry.Key);
                }
            }
            if (forgotten == null) return;
            foreach (var layoutId in forgotten)
            {
                var entry = ctx.LayoutIdRegistry[layoutId];
                ctx.LayoutIdRegistry[layoutId] = (entry.Element, entry.Box!.Value.Detached());
            }
        }

        // Called where a render ends: the end of a batch drain, whose passes are one render, and the
        // boundary of a top-level pass outside a drain.
        internal static void ExpireSnapshots(ReconcilerContext ctx)
        {
            foreach (var layoutId in ctx.LayoutIdSnapshots)
            {
                if (ctx.LayoutIdRegistry.TryGetValue(layoutId, out var entry) && entry.Element == null)
                {
                    ctx.LayoutIdRegistry.Remove(layoutId);
                }
            }
            // MUTANT_SURVIVES(equivalent): CancelForTeardown lists every id it gives a null element, so an id
            // left over here is removed at a later boundary exactly when it would have been listed again.
            ctx.LayoutIdSnapshots.Clear();
        }

        // A power of two, so a rect off by exactly this much is representable and the boundary testable.
        internal const float PixelTolerance = 1f / 128f;

        // Pure(ish) mechanics, panel-free by design (mirrors MotionSpringDriverTests' own rationale for
        // testing the spring math directly): resolves an old→new rect pair into a SpringPlan whose Scale
        // channel animates the (averaged, uniform) size ratio back to 1 and whose TranslateX/Y channels
        // animate back to zero the offset between the new rect's transform origin (in its own pixels) and
        // the point at the same fraction of the old rect — the scale holds the origin still, so aligning
        // those two points is what starts the tween over the old rect. Empty (IsEmpty) when the rects differ
        // by no more than PixelTolerance, so the caller can skip building spring state for a patch that
        // didn't actually move/resize anything, and when either rect is not finite: NaN before a first
        // layout, infinite in a parent drawn at zero scale.
        internal static MotionSpringClassParser.SpringPlan ComputeDeltaPlan(Rect oldRect, Rect newRect, Vector2 origin)
        {
            if (!IsFiniteRect(oldRect) || !IsFiniteRect(newRect)) return default;

            var scaleX = newRect.width > 0.01f ? oldRect.width / newRect.width : 1f;
            var scaleY = newRect.height > 0.01f ? oldRect.height / newRect.height : 1f;
            var scale = (scaleX + scaleY) / 2f;
            var oldOrigin = oldRect.position + new Vector2(origin.x * scaleX, origin.y * scaleY);
            var delta = oldOrigin - (newRect.position + origin);

            // In pixels, not float equality: a box mapped through a rotated parent carries float noise.
            var translateChanged = Mathf.Abs(delta.x) > PixelTolerance || Mathf.Abs(delta.y) > PixelTolerance;
            var scaleChanged = Mathf.Abs(scale - 1f) * Mathf.Max(newRect.width, newRect.height) > PixelTolerance;

            return new MotionSpringClassParser.SpringPlan
            {
                TranslateX = translateChanged ? (delta.x, 0f) : null,
                TranslateY = translateChanged ? (delta.y, 0f) : null,
                Scale = scaleChanged ? (scale, 1f) : null,
            };
        }

        // The farthest any edge of one rect lies from the same edge of the other.
        private static float EdgeTravel(Rect a, Rect b) => Mathf.Max(
            Mathf.Max(Mathf.Abs(a.xMin - b.xMin), Mathf.Abs(a.xMax - b.xMax)),
            Mathf.Max(Mathf.Abs(a.yMin - b.yMin), Mathf.Abs(a.yMax - b.yMax)));

        private static bool SameRect(Rect a, Rect b) => IsFiniteRect(b) && EdgeTravel(a, b) <= PixelTolerance;

        private static bool IsUnit(float scale) => Mathf.Abs(scale - 1f) <= 1e-5f;

        private static bool IsFiniteRect(Rect r) =>
            float.IsFinite(r.x) && float.IsFinite(r.y) && float.IsFinite(r.width) && float.IsFinite(r.height);
    }

    internal readonly struct LayoutIdBox
    {
        public LayoutIdBox(VisualElement? parent, Rect local, Vector2 panelCentre, Vector2 panelSize, float ancestorScale)
        {
            Parent = parent;
            Local = local;
            PanelCentre = panelCentre;
            PanelSize = panelSize;
            AncestorScale = ancestorScale;
        }

        public VisualElement? Parent { get; }
        public Rect Local { get; }
        public Vector2 PanelCentre { get; }
        public Vector2 PanelSize { get; }
        public float AncestorScale { get; }

        // Without the parent it was read under, which the pool has taken back and can hand to another
        // Motion's element, where it would pass for that parent
        // (Given_TwoListComponentsHoldingTheCardInAPooledButton_When_TheSecondIsSelected_Then_TheCardTweensFromTheFirst).
        public LayoutIdBox Detached() => new(null, Local, PanelCentre, PanelSize, AncestorScale);
    }

    internal sealed class LayoutIdPendingSettle
    {
        public LayoutIdPendingSettle(LayoutIdBox from, Rect patchedLayout, float stiffness, float damping, float mass)
        {
            From = from;
            PatchedLayout = patchedLayout;
            Stiffness = stiffness;
            Damping = damping;
            Mass = mass;
        }

        public LayoutIdBox From { get; }
        public Rect PatchedLayout { get; }
        public float Stiffness { get; }
        public float Damping { get; }
        public float Mass { get; }
        public EventCallback<GeometryChangedEvent> Callback { get; set; } = null!;
    }

    internal sealed class LayoutIdProjection
    {
        public LayoutIdProjection(StyleTranslate ownInlineTranslate, StyleScale ownInlineScale, Vector3 ownTranslate, Vector3 ownScale)
        {
            OwnInlineTranslate = ownInlineTranslate;
            OwnInlineScale = ownInlineScale;
            OwnTranslate = ownTranslate;
            OwnScale = ownScale;
        }

        // The natural box at the spring's start, relative to the parent's drawn corner in undistorted units.
        public Rect From;
        // Progress from the layout (0) to From (1). SpringIntegrator is a mutable struct stepped in place, so
        // this is a field rather than a property.
        public SpringIntegrator Spring;
        public bool Springing;
        public float Stiffness;
        public float Damping;
        public float Mass;
        public float Rest;

        // The pass these were last computed in: the scale the projected ancestors are drawn at, and the
        // uniform scale this projection writes.
        public int Pass;
        public float ParentScale = 1f;
        public float Scale = 1f;

        public StyleTranslate OwnInlineTranslate;
        public StyleScale OwnInlineScale;
        public Vector3 OwnTranslate;
        public Vector3 OwnScale;

        public bool WritesTranslate;
        public bool WritesScale;
        public StyleTranslate WrittenTranslate;
        public StyleScale WrittenScale;
    }
}
