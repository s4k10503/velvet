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
    // and scale that its layout transition then carries back to its layout.
    //
    // A projection draws the element at its natural box divided by the scale its projected ancestors are
    // drawn at, about the parent's corner, so a layoutId Motion inside a growing or shrinking one keeps its
    // own size and its offset from that corner on every frame. Its natural box is lerped from the old box to
    // the layout by one progress value, driven by the transition LayoutIdTiming resolves. One frame per panel
    // steps every projection and then writes them, each after its ancestors, since a write reads the scale
    // its ancestors are drawn at in that frame.
    //
    // A box is the rect an element is drawn at inside its parent, together with that parent, its centre and
    // size in panel space, and the scale its projected ancestors were drawn at. A box read under this same
    // parent is compared relative to the parent's drawn corner; any other is taken into the new parent
    // through the parent's panel transform. A box in LayoutIdRegistry forgets its parent when the pool takes
    // that parent back (ForgetParent), since the pool can hand it to another Motion's element before the box
    // is claimed.
    // Panel space throughout was rejected: an inner layoutId Motion that moves inside an outer one still
    // tweening then no longer tweens by its own move inside it
    // (Given_AnOuterLayoutIdMotionStillTweening_When_OnlyTheInnerMovesInsideIt_Then_TheInnerTweensOnlyItsOwnMove).
    // The centre is mapped as a point and the size through each parent's scale factors rather than as a
    // rect: a rect's panel mapping is the bounding box of its transformed corners, which a rotated ancestor
    // inflates
    // (Given_ALayoutIdMotionInARotatedBoard_When_ItMovesToTheOtherColumn_Then_ItTweensFromItsOldPlaceAtItsOwnSize).
    //
    // Several live Motions can hold one id, as members of Framer's NodeStack do: the newest to mount leads it
    // and the others follow it (Follow), and a lead leaving hands the id to the newest member left (Leave).
    internal static class MotionLayoutIdDriver
    {
        // An edge this close to its layout, in pixels, and moving this slowly ends a projection's spring.
        internal const float RestPixels = 0.1f;

        private static int s_pass;
        private static int s_frames;
        private static readonly List<VisualElement> s_ended = new();

        // Called from FiberNodePatcher.PatchMotion for a MotionNode carrying a LayoutId, once the
        // patch's own class/style/children work is done. element.layout still holds the PRE-patch
        // resolved rect at this point (this frame's Yoga pass has not run yet) — capturing it now is the
        // same "read .layout synchronously before the mutation that invalidates it" pattern
        // GeneralPathReconciler.PinExitingChildOutOfFlow uses for a PopLayout exit. The NEW rect is not
        // trustworthy yet (a reparented/freshly-created element's .layout stays stale until the next
        // Yoga pass — see FiberWrapperElementAppliers's clip-wrapper comment on the same window), so it
        // is captured on this element's own first post-patch GeometryChangedEvent instead.
        internal static void OnPatched(VisualElement element, string layoutId, LayoutIdTiming timing, ReconcilerContext ctx)
        {
            ctx.LayoutIdTimings[element] = timing;
            var joins = !ctx.ElementToLayoutId.TryGetValue(element, out var registeredId) || registeredId != layoutId;
            if (joins && registeredId != null) Leave(element, registeredId, ctx);
            // The newest Motion to mount under an id leads it, as the newest member of Framer's NodeStack does;
            // the others follow it, and a follower's own patch takes nothing from the lead
            // (Given_TwoLiveMotionsSharingALayoutId_When_TheLeadMovesInARenderThatAlsoPatchesTheFollower_Then_TheLeadTweensFromItsOwnBox).
            ctx.LayoutIdRegistry.TryGetValue(layoutId, out var previous);
            if (!joins && !ReferenceEquals(previous.Element, element)) return;
            if (joins) Members(layoutId, ctx).Add(element);

            // The old box is read off whichever element the id is registered to — this one, or the one it
            // replaces, which teardown has not reached yet — rather than stored at registration: a freshly
            // created element registers before its first layout, with no box to store, and a stored zero
            // rect reads as a real box at the parent's origin. The box an entry carries is the fallback for
            // an element not laid out yet, and the whole entry once teardown has taken its element.
            // A lead a teardown promoted stands at the box it was handed until its tween starts, having been
            // drawn nowhere else, as Framer's promote hands a promoted node's snapshot on to the next. Any
            // other element is read where it is: a wait a patch left can outlast a move of its ancestors
            // (Given_ALayoutIdMotionPatchedWithoutMoving_When_ItsParentMovesAndAnotherTakesTheId_Then_ItTweensFromWhereTheFirstIsNow).
            var promotion = previous.Element != null && ctx.LayoutIdPendingSettles.TryGetValue(previous.Element, out var wait) && wait.Promoted
                ? wait
                : null;
            var oldBox = promotion != null ? promotion.From : ReadBox(previous.Element, ctx) ?? previous.Box;

            ctx.ElementToLayoutId[element] = layoutId;
            ctx.LayoutIdRegistry[layoutId] = (element, oldBox);

            // A second patch before a layout settles the first replaces its wait rather than adding one.
            CancelPendingSettle(element, ctx);
            if (oldBox is not { } fromBox) return;
            var keepsPromotion = promotion != null && ReferenceEquals(previous.Element, element);
            Wait(element, new LayoutIdPendingSettle(fromBox, element.layout, timing, keepsPromotion,
                keepsPromotion || !ReferenceEquals(previous.Element, element)), ctx);
        }

        private static void Wait(VisualElement element, LayoutIdPendingSettle pending, ReconcilerContext ctx)
        {
            pending.Callback = _ => Settle(element, ctx);
            element.RegisterCallback(pending.Callback);
            ctx.LayoutIdPendingSettles[element] = pending;
        }

        private static void Settle(VisualElement element, ReconcilerContext ctx)
        {
            if (!ctx.LayoutIdPendingSettles.TryGetValue(element, out var pending)) return;
            // A follower's own layout change does not animate, as a Framer node that is not its stack's lead
            // does not.
            if (!IsLead(element, ctx)) return;
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
            Unhide(element, ctx);

            var parentScale = AncestorScale(parent, ctx);
            // Taken to undistorted units: a box read under this parent was drawn at the scale its ancestors had
            // when it was read, and one mapped in from elsewhere at the scale they have now.
            var readScale = ReferenceEquals(pending.From.Parent, parent) ? pending.From.AncestorScale : parentScale;
            var drawnFrom = FromRect(element, pending.From, ctx);
            var from = new Rect(drawnFrom.position * readScale, drawnFrom.size * readScale);
            // A handover with a follower crossfades even where the boxes match, as Framer animates a node
            // resuming from another whatever the delta.
            var followed = Follow(element, pending.Timing.Animates && pending.Handover, ctx);
            var moves = pending.Timing.Animates && (followed || !ComputeDelta(from, layout, TransformOrigin(element)).IsEmpty);

            ctx.LayoutIdProjections.TryGetValue(element, out var projection);
            if (!moves && projection == null) return;
            projection ??= CreateProjection(element, ctx);
            projection.FollowOf = null;
            projection.From = from;
            projection.Moving = moves;
            projection.Crossfade = followed;
            projection.Progress = pending.Timing.Start(EdgeTravel(from, layout));
            Project(host, ctx);
            EnsureFrame(host, ctx);
        }

        private static bool IsLead(VisualElement element, ReconcilerContext ctx) =>
            ctx.ElementToLayoutId.TryGetValue(element, out var layoutId)
            && ctx.LayoutIdRegistry.TryGetValue(layoutId, out var entry) && ReferenceEquals(entry.Element, element);

        private static List<VisualElement> Members(string layoutId, ReconcilerContext ctx)
        {
            if (!ctx.LayoutIdMembers.TryGetValue(layoutId, out var members))
            {
                ctx.LayoutIdMembers[layoutId] = members = new List<VisualElement>();
            }
            return members;
        }

        // The other live Motions under the lead's id follow it: while the lead moves from another Motion's box
        // they are drawn over the lead's box and fade out, as Framer crossfades a stack's previous lead into its
        // new one, and otherwise they are hidden from sight and from the pointer. Returns whether any follows.
        private static bool Follow(VisualElement lead, bool crossfades, ReconcilerContext ctx)
        {
            var followed = false;
            foreach (var member in Members(ctx.ElementToLayoutId[lead], ctx))
            {
                if (ReferenceEquals(member, lead) || member.panel == null) continue;
                followed = true;
                CancelPendingSettle(member, ctx);
                if (!crossfades)
                {
                    End(member, ctx);
                    Hide(member, ctx);
                    continue;
                }
                Unhide(member, ctx);
                if (!ctx.LayoutIdProjections.TryGetValue(member, out var projection)) projection = CreateProjection(member, ctx);
                projection.FollowOf = lead;
                projection.Moving = false;
            }
            return followed;
        }

        private static void Hide(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.LayoutIdHidden.ContainsKey(element)) return;
            ctx.LayoutIdHidden[element] = (element.style.opacity, element.pickingMode);
            element.style.opacity = 0f;
            element.pickingMode = PickingMode.Ignore;
        }

        private static void Unhide(VisualElement element, ReconcilerContext ctx)
        {
            if (!ctx.LayoutIdHidden.Remove(element, out var hidden)) return;
            element.style.opacity = hidden.Opacity;
            element.pickingMode = hidden.Picking;
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
            var opacity = element.style.opacity;
            var resolved = element.resolvedStyle;
            var projection = new LayoutIdProjection(translate, scale,
                translate.keyword == StyleKeyword.Undefined ? Pixels(translate.value, element.layout) : resolved.translate,
                scale.keyword == StyleKeyword.Undefined ? scale.value.value : resolved.scale.value)
            {
                From = element.layout,
                OwnInlineOpacity = opacity,
                OwnOpacity = opacity.keyword == StyleKeyword.Undefined ? opacity.value : resolved.opacity,
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
        private static Vector2 ProjectedScale(VisualElement element, int pass, ReconcilerContext ctx)
        {
            var scale = Vector2.one;
            for (VisualElement? e = element; e != null; e = e.hierarchy.parent)
            {
                if (ctx.LayoutIdProjections.TryGetValue(e, out var projection)) scale *= Compute(e, projection, pass, ctx);
            }
            return scale;
        }

        // The same product, as last drawn.
        private static Vector2 AncestorScale(VisualElement element, ReconcilerContext ctx)
        {
            var scale = Vector2.one;
            for (VisualElement? e = element; e != null; e = e.hierarchy.parent)
            {
                if (ctx.LayoutIdProjections.TryGetValue(e, out var projection)) scale *= projection.Scale;
            }
            return scale;
        }

        private static Vector2 Compute(VisualElement element, LayoutIdProjection projection, int pass, ReconcilerContext ctx)
        {
            if (projection.Pass == pass) return projection.Scale;
            projection.Pass = pass;
            var layout = element.layout;
            if (element.hierarchy.parent is not { } parent || !IsFiniteRect(layout)) return projection.Scale;

            projection.ParentScale = ProjectedScale(parent, pass, ctx);
            var drawn = Drawn(projection, layout, projection.ParentScale);
            var fade = 1f;
            if (projection.FollowOf is { } lead && ctx.LayoutIdProjections.TryGetValue(lead, out var leading)
                && lead.hierarchy.parent is { } leadParent)
            {
                Compute(lead, leading, pass, ctx);
                drawn = Followed(parent, leadParent, Drawn(leading, lead.layout, leading.ParentScale));
                fade = 1f - CrossfadeOut(leading.Moving ? 1f - leading.Progress.Value : 1f);
            }
            else if (projection.Crossfade && projection.Moving)
            {
                fade = CrossfadeIn(1f - projection.Progress.Value);
            }
            var delta = ComputeDelta(drawn, layout, TransformOrigin(element));
            projection.Scale = delta.Scale;
            Write(element, projection, delta.Translate);
            WriteOpacity(element, projection, fade);
            return projection.Scale;
        }

        // The lead's drawn box, in the follower's parent.
        private static Rect Followed(VisualElement parent, VisualElement leadParent, Rect leadDrawn)
        {
            var leadWorld = leadParent.worldTransform;
            var world = parent.worldTransform;
            Vector2 centre = world.inverse.MultiplyPoint3x4(leadWorld.MultiplyPoint3x4(leadDrawn.center));
            var size = leadDrawn.size * AxisLengths(leadWorld) / AxisLengths(world);
            return new Rect(centre - size / 2f, size);
        }

        // Framer's crossfade easings: the lead fades in over the first half of its move on a circular ease-out,
        // and a follower fades out linearly from the half to 95%.
        private static float CrossfadeIn(float p) => p <= 0f ? 0f : p >= 0.5f ? 1f : Mathf.Sin(Mathf.Acos(1f - p / 0.5f));

        private static float CrossfadeOut(float p) => p <= 0.5f ? 0f : p >= 0.95f ? 1f : (p - 0.5f) / 0.45f;

        private static void WriteOpacity(VisualElement element, LayoutIdProjection projection, float fade)
        {
            if (!projection.WritesOpacity && Mathf.Approximately(fade, 1f)) return;
            if (!projection.WritesOpacity)
            {
                projection.WritesOpacity = true;
                MotionNativeTransitionGuard.SuspendIfIntercepted(element, projection, MotionTransitionSlots.Opacity);
            }
            element.style.opacity = projection.OwnOpacity * fade;
            projection.WrittenOpacity = element.style.opacity;
        }

        private static Rect Drawn(LayoutIdProjection projection, Rect layout, Vector2 parentScale)
        {
            var t = projection.Moving ? projection.Progress.Value : 0f;
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
                element.style.scale = new Scale(new Vector3(own.x * projection.Scale.x, own.y * projection.Scale.y, own.z));
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
            var opacity = element.style.opacity;
            if (projection.WritesOpacity && opacity != projection.WrittenOpacity)
            {
                projection.OwnInlineOpacity = opacity;
                projection.OwnOpacity = opacity.keyword == StyleKeyword.Undefined ? opacity.value : 1f;
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
            s_frames++;
            StartPromotions(host, ctx);
            foreach (var entry in ctx.LayoutIdProjections)
            {
                var projection = entry.Value;
                if (!projection.Moving || entry.Key.panel?.visualTree != host) continue;
                if (projection.Progress.Step(dt)) projection.Moving = false;
            }
            Project(host, ctx);

            var remaining = ctx.LayoutIdPromotions.Count > 0;
            foreach (var entry in ctx.LayoutIdProjections)
            {
                if (entry.Key.panel?.visualTree != host) continue;
                var ended = entry.Value.FollowOf is { } lead
                    ? !(ctx.LayoutIdProjections.TryGetValue(lead, out var leading) && leading.Moving)
                    : !entry.Value.Moving && IsUnit(entry.Value.ParentScale);
                if (ended) s_ended.Add(entry.Key);
                else remaining = true;
            }
            foreach (var element in s_ended)
            {
                var following = ctx.LayoutIdProjections[element].FollowOf != null;
                End(element, ctx);
                if (following) Hide(element, ctx);
            }
            s_ended.Clear();
            if (!remaining && ctx.LayoutIdFrames.Remove(host, out var frame)) frame.Pause();
        }

        // A lead promoted by its predecessor's teardown starts once a layout pass has run since, unless its
        // own GeometryChangedEvent started it first; it stays hidden until then, since it has not taken the
        // predecessor's box yet.
        private static void StartPromotions(VisualElement host, ReconcilerContext ctx)
        {
            foreach (var entry in ctx.LayoutIdPromotions)
            {
                if (entry.Key.panel?.visualTree != host || s_frames < entry.Value + 2) continue;
                s_ended.Add(entry.Key);
            }
            foreach (var element in s_ended)
            {
                ctx.LayoutIdPromotions.Remove(element);
                Settle(element, ctx);
            }
            s_ended.Clear();
        }

        // Hands the slots back to what held them before the projection, and the transition suspension with them.
        private static void End(VisualElement element, ReconcilerContext ctx)
        {
            if (!ctx.LayoutIdProjections.Remove(element, out var projection)) return;
            AdoptForeignWrites(element, projection);
            if (projection.WritesTranslate) element.style.translate = projection.OwnInlineTranslate;
            if (projection.WritesScale) element.style.scale = projection.OwnInlineScale;
            if (projection.WritesOpacity) element.style.opacity = projection.OwnInlineOpacity;
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

        // Null for an element with no parent or not laid out yet.
        private static LayoutIdBox? ReadBox(VisualElement? element, ReconcilerContext ctx)
        {
            if (element?.hierarchy.parent is not { } parent || !IsFiniteRect(element.layout)) return null;
            var layout = element.layout;
            var parentScale = AncestorScale(parent, ctx);
            var drawn = ctx.LayoutIdProjections.TryGetValue(element, out var projection) ? Drawn(projection, layout, parentScale) : layout;
            var world = parent.worldTransform;
            return new LayoutIdBox(parent, drawn, world.MultiplyPoint3x4(drawn.center), drawn.size * AxisLengths(world), parentScale);
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
            if (ctx.ElementToLayoutId.TryGetValue(element, out var layoutId)) Leave(element, layoutId, ctx);
            ctx.LayoutIdProjections.Remove(element);
            CancelPendingSettle(element, ctx);
            ctx.LayoutIdTimings.Remove(element);
        }

        // Takes the element out of the id it was registered under. A lead leaving hands the id to the newest
        // follower left, as Framer's NodeStack.remove promotes its last member, which then tweens from the
        // box the lead left.
        private static void Leave(VisualElement element, string layoutId, ReconcilerContext ctx)
        {
            Unhide(element, ctx);
            ctx.LayoutIdPromotions.Remove(element);
            var members = Members(layoutId, ctx);
            members.Remove(element);
            if (members.Count == 0) ctx.LayoutIdMembers.Remove(layoutId);
            if (ctx.LayoutIdRegistry.TryGetValue(layoutId, out var current) && ReferenceEquals(current.Element, element))
            {
                var box = ctx.LayoutIdPendingSettles.TryGetValue(element, out var waiting) && waiting.Promoted
                    ? waiting.From
                    : ReadBox(element, ctx) ?? current.Box;
                if (members.Count > 0)
                {
                    Promote(members[members.Count - 1], layoutId, box, ctx);
                }
                else if (ctx.CurrentPass != null)
                {
                    ctx.LayoutIdRegistry[layoutId] = (null, box);
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

        private static void Promote(VisualElement next, string layoutId, LayoutIdBox? box, ReconcilerContext ctx)
        {
            ctx.LayoutIdRegistry[layoutId] = (next, box);
            End(next, ctx);
            CancelPendingSettle(next, ctx);
            if (box is not { } from || next.panel?.visualTree is not { } host)
            {
                Unhide(next, ctx);
                return;
            }
            Hide(next, ctx);
            var timing = ctx.LayoutIdTimings.TryGetValue(next, out var own) ? own : LayoutIdTiming.Default;
            Wait(next, new LayoutIdPendingSettle(from, next.layout, timing, promoted: true, handover: true), ctx);
            ctx.LayoutIdPromotions[next] = s_frames;
            EnsureFrame(host, ctx);
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

        // Pure mechanics, panel-free by design (mirrors MotionSpringDriverTests' own rationale for testing the
        // spring math directly): resolves an old→new rect pair into the scale on each axis that takes the new
        // rect's size to the old one's, and the translate that then moves the new rect's transform origin (in
        // its own pixels) onto the point at the same fraction of the old rect — the scale holds the origin
        // still, so aligning those two points is what starts the tween over the old rect. Empty (IsEmpty) when
        // the rects differ by no more than PixelTolerance, and when either rect is not finite: NaN before a
        // first layout, infinite in a parent drawn at zero scale.
        internal static LayoutIdDelta ComputeDelta(Rect oldRect, Rect newRect, Vector2 origin)
        {
            if (!IsFiniteRect(oldRect) || !IsFiniteRect(newRect)) return LayoutIdDelta.None;

            var scale = new Vector2(newRect.width > 0.01f ? oldRect.width / newRect.width : 1f,
                newRect.height > 0.01f ? oldRect.height / newRect.height : 1f);
            var oldOrigin = oldRect.position + Vector2.Scale(origin, scale);
            var delta = oldOrigin - (newRect.position + origin);

            // In pixels, not float equality: a box mapped through a rotated parent carries float noise.
            var translateChanged = Mathf.Abs(delta.x) > PixelTolerance || Mathf.Abs(delta.y) > PixelTolerance;
            var scaleChanged = Mathf.Abs(scale.x - 1f) * newRect.width > PixelTolerance
                || Mathf.Abs(scale.y - 1f) * newRect.height > PixelTolerance;
            return new LayoutIdDelta(translateChanged ? delta : Vector2.zero, scaleChanged ? scale : Vector2.one);
        }

        // The farthest any edge of one rect lies from the same edge of the other.
        private static float EdgeTravel(Rect a, Rect b) => Mathf.Max(
            Mathf.Max(Mathf.Abs(a.xMin - b.xMin), Mathf.Abs(a.xMax - b.xMax)),
            Mathf.Max(Mathf.Abs(a.yMin - b.yMin), Mathf.Abs(a.yMax - b.yMax)));

        private static bool SameRect(Rect a, Rect b) => IsFiniteRect(b) && EdgeTravel(a, b) <= PixelTolerance;

        private static bool IsUnit(Vector2 scale) => Mathf.Abs(scale.x - 1f) <= 1e-5f && Mathf.Abs(scale.y - 1f) <= 1e-5f;

        private static bool IsFiniteRect(Rect r) =>
            float.IsFinite(r.x) && float.IsFinite(r.y) && float.IsFinite(r.width) && float.IsFinite(r.height);
    }

    internal readonly struct LayoutIdDelta
    {
        public static readonly LayoutIdDelta None = new(Vector2.zero, Vector2.one);

        public LayoutIdDelta(Vector2 translate, Vector2 scale)
        {
            Translate = translate;
            Scale = scale;
        }

        public Vector2 Translate { get; }
        public Vector2 Scale { get; }
        public bool IsEmpty => Translate == Vector2.zero && Scale == Vector2.one;
    }

    internal readonly struct LayoutIdBox
    {
        public LayoutIdBox(VisualElement? parent, Rect local, Vector2 panelCentre, Vector2 panelSize, Vector2 ancestorScale)
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
        public Vector2 AncestorScale { get; }

        // Without the parent it was read under, which the pool has taken back and can hand to another
        // Motion's element, where it would pass for that parent
        // (Given_TwoListComponentsHoldingTheCardInAPooledButton_When_TheSecondIsSelected_Then_TheCardTweensFromTheFirst).
        public LayoutIdBox Detached() => new(null, Local, PanelCentre, PanelSize, AncestorScale);
    }

    internal sealed class LayoutIdPendingSettle
    {
        public LayoutIdPendingSettle(LayoutIdBox from, Rect patchedLayout, LayoutIdTiming timing, bool promoted, bool handover)
        {
            From = from;
            PatchedLayout = patchedLayout;
            Timing = timing;
            Promoted = promoted;
            Handover = handover;
        }

        // Handed the id by its predecessor's teardown rather than by a patch.
        public bool Promoted { get; }

        // From a box another Motion stood at rather than the element's own.
        public bool Handover { get; }

        public LayoutIdBox From { get; }
        public Rect PatchedLayout { get; }
        public LayoutIdTiming Timing { get; }
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

        // The natural box where the progress starts, relative to the parent's drawn corner in undistorted units.
        public Rect From;
        public LayoutIdProgress Progress = null!;
        public bool Moving;
        // The lead fades in over its move while another Motion under its id follows it.
        public bool Crossfade;
        // The lead this Motion follows, drawn over its box instead of its own.
        public VisualElement? FollowOf;

        // The pass these were last computed in: the scale the projected ancestors are drawn at, and the
        // scale this projection writes on each axis.
        public int Pass;
        public Vector2 ParentScale = Vector2.one;
        public Vector2 Scale = Vector2.one;

        public StyleTranslate OwnInlineTranslate;
        public StyleScale OwnInlineScale;
        public Vector3 OwnTranslate;
        public Vector3 OwnScale;

        public bool WritesTranslate;
        public bool WritesScale;
        public StyleTranslate WrittenTranslate;
        public StyleScale WrittenScale;

        public StyleFloat OwnInlineOpacity;
        public float OwnOpacity = 1f;
        public bool WritesOpacity;
        public StyleFloat WrittenOpacity;
    }

    // The transition a layoutId move takes: the Motion's own `transition`, or its Layout in place of it when
    // set, as Framer reads `transition.layout`, with Framer's default layout transition when the Motion has
    // none. Its type decides the curve, the way it does for a variant swap.
    internal readonly struct LayoutIdTiming
    {
        // Framer's defaultLayoutTransition: { duration: 0.45, ease: [0.4, 0, 0.1, 1] }.
        private static readonly StyleTransitionConfig s_default = new()
        {
            Type = TransitionType.Bezier, DurationSec = 0.45f, BezierX1 = 0.4f, BezierY1 = 0f, BezierX2 = 0.1f, BezierY2 = 1f,
        };

        private readonly StyleTransitionConfig _config;

        private LayoutIdTiming(StyleTransitionConfig config, bool animates)
        {
            _config = config;
            Animates = animates;
        }

        // False for a zero duration or a configuration the scheduler rejects, which lands the move at once.
        public bool Animates { get; }
        public bool IsSpring => _config.Type == TransitionType.Spring;
        public float Stiffness => _config.Stiffness;
        public float Damping => _config.Damping;
        public float Mass => _config.Mass;
        public float DurationSec => _config.DurationSec;
        public float DelaySec => Mathf.Max(_config.DelaySec, 0f);

        public static LayoutIdTiming Default => From(null);

        public static LayoutIdTiming From(StyleTransitionConfig? transition)
        {
            var t = transition?.Layout ?? transition ?? s_default;
            var animates = t.Type switch
            {
                TransitionType.Spring => StyleAnimationScheduler.ValidateSpringParameters(t.Stiffness, t.Damping, t.Mass),
                TransitionType.Bezier => StyleAnimationScheduler.ValidateBezierParameters(t.BezierX1, t.BezierY1, t.BezierX2, t.BezierY2, t.DurationSec),
                _ => StyleAnimationScheduler.ValidateDuration(t.DurationSec, null),
            };
            return new LayoutIdTiming(t, animates);
        }

        // travel: the farthest an edge moves, in pixels, which scales a spring's rest threshold.
        public LayoutIdProgress Start(float travel) => new(this, travel);

        public float Ease(float t) => _config.Type == TransitionType.Bezier
            ? CubicBezierEvaluator.Evaluate(_config.BezierX1, _config.BezierY1, _config.BezierX2, _config.BezierY2, t)
            : StyleFilterTransitionDriver.Ease(_config.Easing, t);
    }

    // How far a projection still has to go, from 1 at the old box to 0 at the layout.
    internal sealed class LayoutIdProgress
    {
        // The rest a crossfade that moves nothing settles at, on the scale opacity is measured in.
        private const float NormalizedRest = 0.001f;

        private readonly LayoutIdTiming _timing;
        private readonly float _rest;
        // A mutable struct stepped in place, so a field rather than a property.
        private SpringIntegrator _spring = new(1f);
        private float _elapsedSec;

        public LayoutIdProgress(LayoutIdTiming timing, float travel)
        {
            _timing = timing;
            _rest = Mathf.Min(MotionLayoutIdDriver.RestPixels / travel, NormalizedRest);
        }

        public float Value { get; private set; } = 1f;

        // Returns true once the progress has arrived.
        public bool Step(float dtSec)
        {
            _elapsedSec += dtSec;
            var active = _elapsedSec - _timing.DelaySec;
            if (active <= 0f) return false;
            if (_timing.IsSpring)
            {
                _spring.Step(Mathf.Min(dtSec, active), 0f, _timing.Stiffness, _timing.Damping, _timing.Mass);
                Value = _spring.Value;
                return _spring.IsSettled(0f, _rest, _rest);
            }
            var t = active / _timing.DurationSec;
            Value = 1f - _timing.Ease(t);
            return t >= 1f;
        }
    }
}
