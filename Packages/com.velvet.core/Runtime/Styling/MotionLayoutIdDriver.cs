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
    // the layout by one progress value, driven by the transition LayoutIdTiming resolves. One frame per panel steps every projection and then writes them,
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
    internal static class MotionLayoutIdDriver
    {
        private static int s_pass;
        // How long the frame running the current pass stepped the tweens by, 0 for a pass outside one.
        private static float s_passDt;
        private static readonly List<VisualElement> s_ended = new();
        private static readonly List<VisualElement> s_none = new();
        // The owner of the transition suspension a member holds while another leads its id.
        private static readonly object s_followOwner = new();
        private static readonly StyleLonghandSet s_visibility = StyleLonghandSet.Of(StyleLonghand.Visibility);
        // The panel no member is on, for SyncFollows while the lead is not moving from a box it took from another holder.
        private static readonly VisualElement s_nowhere = new();

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
            ctx.ElementToLayoutId.TryGetValue(element, out var heldId);
            var joins = heldId != layoutId;
            var previous = ctx.LayoutIdRegistry.GetValueOrDefault(layoutId);
            // The lead is the member that joined last, as Framer's NodeStack promotes the node mounted last, so a
            // patch of any other member leaves it following the lead wherever that patch moved it.
            if (!joins && !ReferenceEquals(previous.Element, element)) return;

            // The old box is read off whichever element the id is registered to — this one, or the lead it
            // takes over from, which may be live — rather than stored at registration: a freshly created
            // element registers before its first layout, with no box to store, and a stored zero rect reads as
            // a real box at the parent's origin. The box an entry carries is the fallback for an element not
            // laid out yet, and the whole entry once teardown has taken its element.
            LayoutIdBox live = default;
            var readLive = previous.Element != null && TryReadBox(previous.Element, previous.Element.layout, ctx, out live);
            var oldBox = readLive ? live : previous.Box;

            if (joins)
            {
                if (heldId != null) RemoveMember(element, heldId, ctx);
                Unfollow(element, ctx);
                if (!ctx.LayoutIdMembers.TryGetValue(layoutId, out var members))
                {
                    ctx.LayoutIdMembers[layoutId] = members = new List<VisualElement>();
                }
                members.Add(element);
            }
            ctx.ElementToLayoutId[element] = layoutId;
            ctx.LayoutIdRegistry[layoutId] = (element, oldBox);
            // The lead this one took over from is hidden until this one's tween draws it again.
            if (joins) SyncFollows(layoutId, ctx);

            // A second patch before a layout settles the first replaces its wait rather than adding one.
            CancelPendingSettle(element, ctx);
            // One with no box to start from and not laid out yet waits for its first layout all the same, so that
            // a scaling Motion it mounted inside corrects it before that frame draws it.
            if (oldBox is { } fromBox)
            {
                Wait(element, new LayoutIdPendingSettle(fromBox, element.layout, timing)
                {
                    ReadOffItself = readLive && ReferenceEquals(previous.Element, element),
                }, ctx);
            }
            else if (!IsFiniteRect(element.layout))
            {
                Wait(element, new LayoutIdPendingSettle(null, element.layout, timing), ctx);
            }
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
            // An ancestor whose layout moved settles in this same layout pass, and this element's old box is
            // taken into the frame that ancestor is drawn in, so the ancestor goes first whichever event fires
            // first.
            SettleMovedAncestors(element, ctx);
            CancelPendingSettle(element, ctx);
            ForgetFallbackParent(element, ctx);
            var host = element.panel?.visualTree;
            if (pending.From != null) Start(element, pending, ctx);
            // MUTANT_SURVIVES(unreachable): the frame check differs only on a panel holding a projection it has no frame for.
            // Every projection is created on its element's panel with a frame there, so only an element moved to another
            // panel without a teardown carries one to a panel without a frame, and Velvet moves none between panels: a
            // portal given another target remounts its children (portals.md). A first-layout wait settles on an element in a panel.
            else if (host != null && ctx.LayoutIdFrames.ContainsKey(host)) Project(host, ctx);
        }

        private static void SettleMovedAncestors(VisualElement element, ReconcilerContext ctx)
        {
            List<VisualElement>? moved = null;
            for (var ancestor = element.hierarchy.parent; ancestor != null; ancestor = ancestor.hierarchy.parent)
            {
                if (ctx.LayoutIdPendingSettles.TryGetValue(ancestor, out var pending)
                    && IsFiniteRect(ancestor.layout) && ancestor.layout != pending.PatchedLayout)
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
            // Promote starts only a member laid out in a panel.
            // MUTANT_SURVIVES(unreachable): a settling element is in a panel, under a parent, and laid out.
            // A settle runs from the element's own GeometryChangedEvent or from a descendant's, which settles
            // only an ancestor laid out.
            if (host == null || parent == null || !IsFiniteRect(layout)) return;

            // A box the patch read off this element is read again where that patch left it unless a move of its
            // own is still drawing it: a patch that moved nothing leaves a wait that a later layout change can
            // fire after the move that drew the box has ended
            // (Given_ALayoutIdMotionPatchedMidTweenWithoutMoving_When_ALaterLayoutChangeMovesIt_Then_ItDoesNotStartFromWhereTheTweenDrewItThen).
            var fromBox = pending.From!.Value;
            if (pending.ReadOffItself && !IsMoving(element, ctx) && TryReadBox(element, pending.PatchedLayout, ctx, out var redrawn))
            {
                fromBox = redrawn;
            }

            var parentScale = AncestorScale(parent, ctx);
            // Taken to undistorted units: a box read under this parent was drawn at the scale its ancestors had
            // when it was read, and one mapped in from elsewhere at the scale they have now.
            var readScale = ReferenceEquals(fromBox.Parent, parent) ? fromBox.AncestorScale : parentScale;
            var drawnFrom = FromRect(element, fromBox, ctx);
            var from = new Rect(drawnFrom.position * readScale, drawnFrom.size * readScale);
            // A lead that took its box from another holder draws the others over its box while it moves. It
            // crossfades with them where more than one holds the id and no ancestor is crossfading already
            // (setAnimationOrigin's shouldCrossfadeOpacity), and animates however little it moves, as Framer starts
            // a layout animation for a node resuming from another (resumeFrom) whether or not its layout changed.
            var layoutId = ctx.ElementToLayoutId[element];
            var shared = !pending.ReadOffItself;
            var crossfades = shared && ctx.LayoutIdMembers[layoutId].Count > 1 && !Crossfading(parent, ctx);
            var moves = pending.Timing.Animates() && (shared || !ComputeDelta(from, layout, TransformOrigin(element)).IsEmpty);

            ctx.LayoutIdProjections.TryGetValue(element, out var projection);
            if (!moves && projection == null && IsUnit(parentScale)) return;
            projection ??= CreateProjection(element, host, ctx);
            Share(projection, shared, crossfades, moves, fromBox.Look);
            projection.From = from;
            projection.Moving = moves;
            projection.Progress = pending.Timing.Start();
            SyncFollows(layoutId, ctx);
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

        private static LayoutIdProjection CreateProjection(VisualElement element, VisualElement host, ReconcilerContext ctx)
        {
            var translate = element.style.translate;
            var scale = element.style.scale;
            var resolved = element.resolvedStyle;
            var ownTranslate = translate.keyword == StyleKeyword.Undefined ? Pixels(translate.value, element.layout) : resolved.translate;
            var ownScale = scale.keyword == StyleKeyword.Undefined ? scale.value.value : resolved.scale.value;
            // A running bounce or ping has its frame in the slot, and the frame is the loop's rather than the element's.
            if (StyleAnimateDriver.TryReadBounceOwn(element, translate, out var bounceInline, out var bounceOwn))
            {
                (translate, ownTranslate) = (bounceInline, Pixels(bounceOwn, element.layout));
            }
            if (StyleAnimateDriver.TryReadPingOwn(element, scale, out var pingInline, out var pingOwn))
            {
                (scale, ownScale) = (pingInline, pingOwn);
            }
            var projection = new LayoutIdProjection(host, translate, scale, ownTranslate, ownScale)
            {
                From = element.layout,
            };
            ctx.LayoutIdProjections[element] = projection;
            return projection;
        }

        // Computes and writes every projection on the panel, first giving one to each registered layoutId
        // Motion laid out under a scaling projection that has none, whether or not anything moved it. One not laid
        // out yet is given one when its first layout settles its wait (OnPatched), not here: a pass run inside a
        // render reads its own transform before the style pass has resolved its classes.
        private static void Project(VisualElement host, ReconcilerContext ctx)
        {
            var pass = ++s_pass;
            foreach (var element in ctx.ElementToLayoutId.Keys)
            {
                if (!ctx.LayoutIdProjections.ContainsKey(element) && element.panel?.visualTree == host
                    && IsFiniteRect(element.layout)
                    && element.hierarchy.parent is { } parent && !IsUnit(ProjectedScale(parent, pass, ctx)))
                {
                    CreateProjection(element, host, ctx);
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
            // MUTANT_SURVIVES(equivalent): a second computation in one pass reads the same layouts, progress and
            // ancestor scales as the first, and writes back the values the first wrote.
            if (projection.Pass == pass) return projection.Scale;
            projection.Pass = pass;
            var layout = element.layout;
            if (element.hierarchy.parent is not { } parent || !IsFiniteRect(layout)) return projection.Scale;

            projection.ParentScale = ProjectedScale(parent, pass, ctx);
            var drawn = projection.Leader != null ? Followed(element, projection.Leader, ctx) : Drawn(projection, layout, projection.ParentScale);
            var delta = ComputeDelta(drawn, layout, TransformOrigin(element));
            projection.Scale = delta.Scale;
            Write(element, projection, delta.Translate);
            WriteOpacity(element, projection, Fade(element, projection, ctx), ctx);
            if (projection.Leader != null) LayoutIdPicking.Ignore(element, projection);
            return projection.Scale;
        }

        // Where the lead is drawn, in the frame of this member's parent: Framer projects a member behind the lead
        // onto the lead's box. Read as the lead was last computed, in this pass or the one before.
        private static Rect Followed(VisualElement element, VisualElement leader, ReconcilerContext ctx) =>
            TryReadBox(leader, leader.layout, ctx, out var box) ? FromRect(element, box, ctx) : element.layout;

        // Framer's crossfade and its only-member mix (mix-values.ts): the lead fades in over the first half of its move,
        // on circOut, and the members behind it are drawn at the previous lead's opacity fading out between halfway
        // and 95% of the way, linearly; a lead alone under its id mixes from its previous holder's opacity to its own.
        private static LayoutIdFade? Fade(VisualElement element, LayoutIdProjection projection, ReconcilerContext ctx)
        {
            var layoutId = ctx.ElementToLayoutId.GetValueOrDefault(element);
            if (projection.Leader != null) return Behind(ctx.LayoutIdProjections.GetValueOrDefault(projection.Leader), layoutId, ctx);
            if (projection.Crossfade && projection.Moving) return new LayoutIdFade(0f, CrossfadeIn(CrossfadeProgress(projection)), 1f);
            if (projection is not { Shared: true, Moving: true, FromLook: { } look }) return null;
            if (layoutId == null || ctx.LayoutIdMembers.GetValueOrDefault(layoutId)?.Count != 1) return null;
            return new LayoutIdFade(PreviousOpacity(look, layoutId, ctx), CrossfadeProgress(projection), 1f);
        }

        private static LayoutIdFade? Behind(LayoutIdProjection? leading, string? layoutId, ReconcilerContext ctx)
        {
            if (!IsCrossfading(leading)) return null;
            return leading!.FromLook is { } look
                ? new LayoutIdFade(PreviousOpacity(look, layoutId, ctx), 0f, 1f - CrossfadeOut(CrossfadeProgress(leading)))
                : null;
        }

        // A Motion whose id a render has just taken away is still drawn by the pass that render runs, with none.
        private static float PreviousOpacity(LayoutIdLook look, string? layoutId, ReconcilerContext ctx) =>
            layoutId != null && ctx.ElementToLayoutId.GetValueOrDefault(look.Source) == layoutId ? MotionOpacity.Own(look.Source) : look.Opacity;

        // The slot is left alone until the projection first draws a fade, and handed back when it stops.
        private static void WriteOpacity(VisualElement element, LayoutIdProjection projection, LayoutIdFade? fade,
            ReconcilerContext ctx)
        {
            if (fade is { } drawn)
            {
                projection.WritesOpacity = true;
                MotionOpacity.Draw(element, drawn, s_passDt);
            }
            else if (projection.WritesOpacity)
            {
                projection.WritesOpacity = false;
                MotionOpacity.End(element, ctx.StyleAnimationScheduler.Clock);
            }
        }

        // Read before the projection takes its new tween. A move of the lead's own that interrupts its crossfade holds
        // the opacities the crossfade had reached until that move lands, as Framer's stop() leaves the mixed values
        // in place.
        private static void Share(LayoutIdProjection projection, bool shared, bool crossfades, bool moves, LayoutIdLook? from)
        {
            var carries = !shared && IsCrossfading(projection);
            projection.FadeAt = carries ? CrossfadeProgress(projection) : float.NaN;
            // MUTANT_SURVIVES(equivalent): the look is read only while Shared is set, which a start reaching this line
            // leaves clear unless it is shared and has a tween.
            if (!carries) projection.FromLook = shared && moves ? from : null;
            // MUTANT_SURVIVES(equivalent): without a tween the projection is not moving, and a flag set without one is
            // read only with one moving, or by the frame that clears it and hides the other members.
            projection.Shared = (shared || carries) && moves;
            // Read only while the projection moves, which only a start with a tween sets.
            projection.Crossfade = crossfades || carries;
        }

        private static bool IsCrossfading(LayoutIdProjection? projection) => projection is { Crossfade: true, Moving: true };

        // How far a lead's crossfade has got: held where a move of its own interrupted it, else its tween's.
        private static float CrossfadeProgress(LayoutIdProjection projection) =>
            float.IsNaN(projection.FadeAt) ? 1f - projection.Progress.Value : projection.FadeAt;

        // Whether a projection on this element or an ancestor is crossfading.
        private static bool Crossfading(VisualElement? element, ReconcilerContext ctx)
        {
            for (var e = element; e != null; e = e.hierarchy.parent)
            {
                if (IsCrossfading(ctx.LayoutIdProjections.GetValueOrDefault(e))) return true;
            }
            return false;
        }

        private static float CrossfadeIn(float progress) => Mathf.Sin(Mathf.Acos(1f - Mathf.Clamp01(progress / 0.5f)));

        private static float CrossfadeOut(float progress) => Mathf.Clamp01((progress - 0.5f) / 0.45f);

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
                    StyleAnimateDriver.YieldSlots(element, projection, WrittenSlots(projection));
                }
                var own = projection.OwnTranslate;
                // A running bounce's lift is in the element's own frame, so the projection's scale carries it too.
                var lift = StyleAnimateDriver.CurrentLiftPx(element) * projection.Scale.y;
                element.style.translate = new Translate(
                    new Length(own.x + translate.x + lift.x), new Length(own.y + translate.y + lift.y), own.z);
                projection.WrittenTranslate = element.style.translate;
            }
            if (projection.WritesScale || !IsUnit(projection.Scale))
            {
                if (!projection.WritesScale)
                {
                    projection.WritesScale = true;
                    MotionNativeTransitionGuard.SuspendIfIntercepted(element, projection, MotionTransitionSlots.Scale);
                    StyleAnimateDriver.YieldSlots(element, projection, WrittenSlots(projection));
                }
                var own = projection.OwnScale;
                var ping = StyleAnimateDriver.CurrentPingFactor(element);
                element.style.scale = new Scale(
                    new Vector3(own.x * projection.Scale.x * ping, own.y * projection.Scale.y * ping, own.z));
                projection.WrittenScale = element.style.scale;
            }
        }

        // The slots a running animate-* loop leaves to the projection, which adds the loop's share to its own frame.
        private static MotionTransitionSlots WrittenSlots(LayoutIdProjection projection)
            => (projection.WritesTranslate ? MotionTransitionSlots.Translate : MotionTransitionSlots.None)
                | (projection.WritesScale ? MotionTransitionSlots.Scale : MotionTransitionSlots.None);

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
            var clock = ctx.StyleAnimationScheduler.Clock;
            var lastSec = clock.NowSec;
            ctx.LayoutIdFrames[host] = host.schedule.Execute((TimerState ts) =>
            {
                var dt = clock.StepSince(ref lastSec, ts);
                if (dt <= 0f) return;
                Frame(host, dt, ctx);
            }).Every(StyleAnimateDriver.TickMs);
        }

        private static void Frame(VisualElement host, float dt, ReconcilerContext ctx)
        {
            foreach (var entry in ctx.LayoutIdProjections)
            {
                var projection = entry.Value;
                if (!projection.Moving || entry.Key.panel?.visualTree != host) continue;
                if (projection.Progress.Step(dt)) projection.Moving = false;
            }
            // Before the pass, so that no pass draws a lead whose shared tween has landed as still crossfading.
            EndSharedTweens(ctx);
            s_passDt = dt;
            Project(host, ctx);
            s_passDt = 0f;
            SettleLandings(ctx);

            var remaining = false;
            foreach (var entry in ctx.LayoutIdProjections)
            {
                var projection = entry.Value;
                if (projection.Host != host) continue;
                // A member drawn over a lead lasts as long as the lead's shared tween, which EndSharedTweens ends above,
                // and the lead's projection keeps the frame running until then.
                if (projection.Leader != null) continue;
                // One whose element left the panel with no teardown is ended too, since no pass projects it there.
                if (entry.Key.panel?.visualTree != host || !projection.Moving && IsUnit(projection.ParentScale)) s_ended.Add(entry.Key);
                else remaining = true;
            }
            foreach (var element in s_ended)
            {
                End(element, ctx);
            }
            s_ended.Clear();
            if (!remaining && ctx.LayoutIdFrames.Remove(host, out var frame)) frame.Pause();
        }

        // A lead's shared tween ends when it lands, and hides the members drawn over it. Collected first, since
        // SyncFollows ends projections, and each looked up again, since it can end one collected here.
        private static void EndSharedTweens(ReconcilerContext ctx)
        {
            List<VisualElement>? landed = null;
            foreach (var entry in ctx.LayoutIdProjections)
            {
                if (entry.Value.Shared && !entry.Value.Moving) (landed ??= new List<VisualElement>()).Add(entry.Key);
            }
            foreach (var element in landed ?? s_none)
            {
                if (!ctx.LayoutIdProjections.TryGetValue(element, out var projection)) continue;
                // MUTANT_SURVIVES(equivalent): left set, the flags bring this element here again on the next frame.
                // There SyncFollows over an id whose lead is not moving leaves its other members hidden, and every
                // other read of them asks for a tween moving.
                projection.Shared = projection.Crossfade = false;
                SyncFollows(ctx.ElementToLayoutId[element], ctx);
            }
        }

        // A landing is over once its id has no lead moving, which the frame after the move ends sees whatever ended
        // it: the lead's frame runs until then.
        private static void SettleLandings(ReconcilerContext ctx)
        {
            for (var i = ctx.LayoutIdLandings.Count - 1; i >= 0; i--)
            {
                var landing = ctx.LayoutIdLandings[i];
                var lead = ctx.LayoutIdRegistry.GetValueOrDefault(landing.LayoutId).Element;
                if (lead != null && IsMoving(lead, ctx)) continue;
                ctx.LayoutIdLandings.RemoveAt(i);
                landing.Landed();
            }
        }

        // Hands the slots back to what held them before the projection, and the transition suspension with them.
        private static void End(VisualElement element, ReconcilerContext ctx)
        {
            if (!ctx.LayoutIdProjections.Remove(element, out var projection)) return;
            AdoptForeignWrites(element, projection);
            if (projection.WritesTranslate) element.style.translate = projection.OwnInlineTranslate;
            if (projection.WritesScale) element.style.scale = projection.OwnInlineScale;
            StyleAnimateDriver.YieldSlots(element, projection, MotionTransitionSlots.None);
            // The loop's frame follows at once, rather than the element standing at its own value until its next tick.
            StyleAnimateDriver.ReassertLoop(element, MotionTransitionSlots.None);
            WriteOpacity(element, projection, null, ctx);
            LayoutIdPicking.Restore(projection);
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

        private static bool IsMoving(VisualElement element, ReconcilerContext ctx) =>
            ctx.LayoutIdProjections.TryGetValue(element, out var projection) && projection.Moving;

        // The box the element is drawn at while its layout is the given one.
        private static bool TryReadBox(VisualElement element, Rect layout, ReconcilerContext ctx, out LayoutIdBox box)
        {
            box = default;
            if (element.hierarchy.parent is not { } parent || !IsFiniteRect(layout)) return false;
            var parentScale = AncestorScale(parent, ctx);
            var drawn = ctx.LayoutIdProjections.TryGetValue(element, out var projection) ? Drawn(projection, layout, parentScale) : layout;
            var world = parent.worldTransform;
            box = new LayoutIdBox(parent, drawn, world.MultiplyPoint3x4(drawn.center), drawn.size * AxisLengths(world), parentScale,
                LayoutIdLook.Read(element, projection));
            return true;
        }

        private static void CancelPendingSettle(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.LayoutIdPendingSettles.Remove(element, out var pending))
            {
                // MUTANT_SURVIVES(equivalent): a callback left registered finds its element's wait gone, or finds
                // the wait that replaced it, which that wait's own callback settles on the same event.
                element.UnregisterCallback(pending.Callback);
            }
        }

        // Called from FiberElementCleaner before an element is pooled or disposed. The pending settle goes
        // too: a pooled element keeps its callbacks, so whatever the pool hands it to next would otherwise
        // play this element's tween.
        internal static void CancelForTeardown(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.ElementToLayoutId.TryGetValue(element, out var layoutId)) RemoveMember(element, layoutId, ctx);
            if (ctx.LayoutIdProjections.Remove(element, out var projection)) LayoutIdPicking.Restore(projection);
            LayoutIdPicking.Release(element, ctx);
            MotionOpacity.Forget(element);
            CancelPendingSettle(element, ctx);
        }

        // Called from FiberNodePatcher.PatchMotion for a Motion patched with no layoutId: one that held an id stops
        // holding it, shown again, and its tween and its wait end.
        internal static void Forget(VisualElement element, ReconcilerContext ctx)
        {
            if (!ctx.ElementToLayoutId.Remove(element, out var layoutId)) return;
            RemoveMember(element, layoutId, ctx);
            Unfollow(element, ctx);
            End(element, ctx);
            CancelPendingSettle(element, ctx);
        }

        // Called from GeneralPathReconciler as a V.AnimatePresence child starts its exit. For each layoutId Motion in
        // it, the latest member that joined before it and is not exiting takes the lead, as Framer's NodeStack.relegate
        // promotes one when a node stops being present, whether or not that node led; with none, the lead stays.
        // Returns how many of those leads are moving, each of which calls landed once it has landed: Framer leaves
        // removing a relegated node to the lead's layout animation (MeasureLayout), through onExitComplete.
        internal static int Relegate(VisualElement root, ReconcilerContext ctx, System.Action landed)
        {
            var moving = 0;
            foreach (var element in RegisteredWithin(root, ctx))
            {
                ctx.LayoutIdExiting[element] = root;
                var layoutId = ctx.ElementToLayoutId[element];
                var members = ctx.LayoutIdMembers[layoutId];
                for (var i = members.IndexOf(element) - 1; i >= 0; i--)
                {
                    if (ctx.LayoutIdExiting.ContainsKey(members[i])) continue;
                    Resume(members[i], layoutId, ctx);
                    var lead = ctx.LayoutIdRegistry[layoutId].Element!;
                    if (IsMoving(lead, ctx))
                    {
                        ctx.LayoutIdLandings.Add(new LayoutIdLanding(root, layoutId, landed));
                        moving++;
                    }
                    break;
                }
            }
            return moving;
        }

        // Called from GeneralPathReconciler as a V.AnimatePresence child's exit is cancelled by its key coming back.
        // Each layoutId Motion in it takes its id's lead again, as Framer promotes a node that is present again, and
        // the landings the cancelled exit waited on are dropped, so that none settles an exit started later.
        internal static void Present(VisualElement root, ReconcilerContext ctx)
        {
            ctx.LayoutIdLandings.RemoveAll(landing => ReferenceEquals(landing.Root, root));
            foreach (var element in RegisteredWithin(root, ctx))
            {
                if (ctx.LayoutIdExiting.Remove(element)) Resume(element, ctx.ElementToLayoutId[element], ctx);
            }
        }

        // In tree order, an ancestor before its descendants, as Framer notifies them: a descendant that takes a lead
        // checks whether an ancestor is crossfading already.
        private static List<VisualElement> RegisteredWithin(VisualElement root, ReconcilerContext ctx)
        {
            var within = new List<VisualElement>();
            CollectRegistered(root, within, ctx);
            return within;
        }

        private static void CollectRegistered(VisualElement element, List<VisualElement> into, ReconcilerContext ctx)
        {
            if (ctx.ElementToLayoutId.ContainsKey(element)) into.Add(element);
            for (var i = 0; i < element.hierarchy.childCount; i++)
            {
                CollectRegistered(element.hierarchy[i], into, ctx);
            }
        }

        private static LayoutIdBox? ReadBox(VisualElement element, ReconcilerContext ctx) =>
            TryReadBox(element, element.layout, ctx, out var box) ? box : null;

        // Takes an element out of an id's members, at its teardown or as it joins another id. A lead that leaves
        // hands the lead to the member that joined last, as Framer's NodeStack.remove promotes the last remaining
        // member. With none left, a lead that leaves inside a pass leaves its box for a replacement later in
        // that render — a same-key type flip creates it after this teardown — and ExpireSnapshots drops what nobody
        // claimed where the render ends.
        private static void RemoveMember(VisualElement element, string layoutId, ReconcilerContext ctx)
        {
            var members = ctx.LayoutIdMembers[layoutId];
            members.Remove(element);
            if (members.Count == 0) ctx.LayoutIdMembers.Remove(layoutId);
            var current = ctx.LayoutIdRegistry[layoutId];
            if (!ReferenceEquals(current.Element, element)) return;

            var box = (ReadBox(element, ctx) ?? current.Box)?.Released();
            if (members.Count > 0)
            {
                Resume(members[members.Count - 1], layoutId, box, ctx);
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

        // Hands the id's lead to a member, which tweens from where the lead before it was drawn, as Framer's
        // NodeStack.promote resumes the promoted node from the previous lead. Started at once rather than on a
        // layout, since the member's own need not change; a patch later in the render that moves it moves the box
        // the tween runs to, which each pass reads off its layout. One not laid out yet has not been drawn anywhere,
        // and appears where it is first laid out.
        private static void Resume(VisualElement element, string layoutId, LayoutIdBox? from, ReconcilerContext ctx)
        {
            Unfollow(element, ctx);
            ctx.LayoutIdRegistry[layoutId] = (element, null);
            if (from is { } box)
            {
                // MUTANT_SURVIVES(equivalent): Start's own guard ends a member not laid out in a panel, as this does.
                if (element.panel != null && IsFiniteRect(element.layout))
                {
                    Start(element, new LayoutIdPendingSettle(box, element.layout, ctx.LayoutIdTimings[element]), ctx);
                    // Its patch earlier in this render may have moved it, and the layout that shows the move comes after
                    // the next frame's pass
                    // (Given_TwoLiveLayoutIdMotionsAtOneBox_When_TheLeadLeavesAndTheOtherMovesInOneRender_Then_TheOtherTweens).
                    if (!ctx.LayoutIdPendingSettles.ContainsKey(element))
                    {
                        Wait(element, new LayoutIdPendingSettle(null, element.layout, ctx.LayoutIdTimings[element]), ctx);
                    }
                }
            }
            // Start has drawn the rest already unless it did not start a tween.
            SyncFollows(layoutId, ctx);
        }

        // Hands the lead to a member over the live lead, from where that lead is drawn; a no-op for the lead itself,
        // as NodeStack.promote returns for the node that already leads.
        private static void Resume(VisualElement element, string layoutId, ReconcilerContext ctx)
        {
            // A live member holds the id, so the id has a live lead.
            var lead = ctx.LayoutIdRegistry[layoutId].Element!;
            if (!ReferenceEquals(lead, element)) Resume(element, layoutId, ReadBox(lead, ctx), ctx);
        }

        // Draws every member but the lead over the lead's box while the lead moves from a box it took from another
        // holder, fading out if the lead crossfades, as Framer projects a node behind its stack's lead onto the lead.
        // Otherwise hides them with `visibility: hidden`, where Framer draws them at opacity 0. The lead is the one
        // LayoutIdRegistry names.
        private static void SyncFollows(string layoutId, ReconcilerContext ctx)
        {
            if (!ctx.LayoutIdMembers.TryGetValue(layoutId, out var members)) return;
            var lead = ctx.LayoutIdRegistry[layoutId].Element;
            var leading = lead != null ? ctx.LayoutIdProjections.GetValueOrDefault(lead) : null;
            // A member on another panel than the lead, or on none, has no box of the lead's to be drawn over.
            var fadeHost = leading is { Shared: true, Moving: true } ? leading.Host : s_nowhere;
            foreach (var member in members)
            {
                if (ReferenceEquals(member, lead)) continue;
                Follow(member, ctx);
                CancelPendingSettle(member, ctx);
                if (ReferenceEquals(member.panel?.visualTree, fadeHost))
                {
                    member.style.visibility = ctx.LayoutIdFollows[member];
                    var projection = ctx.LayoutIdProjections.GetValueOrDefault(member) ?? CreateProjection(member, fadeHost, ctx);
                    projection.Leader = lead;
                    projection.Moving = false;
                }
                else
                {
                    // Its own tween, if one is running, carries on out of sight.
                    if (ctx.LayoutIdProjections.GetValueOrDefault(member)?.Leader != null) End(member, ctx);
                    member.style.visibility = Visibility.Hidden;
                }
            }
        }

        // Marks a member as following another's lead, keeping the inline visibility it held for when it leads again
        // and suspending a transition that would carry its visibility across frames.
        private static void Follow(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.LayoutIdFollows.ContainsKey(element)) return;
            ctx.LayoutIdFollows[element] = element.style.visibility;
            MotionNativeTransitionGuard.SuspendIfIntercepted(element, s_followOwner, MotionTransitionSlots.Visibility);
        }

        private static void Unfollow(VisualElement element, ReconcilerContext ctx)
        {
            if (!ctx.LayoutIdFollows.Remove(element, out var visibility)) return;
            // Taken out of a list a variant swap wrote since Follow, whose `all` would carry the write below
            // (Given_AClosingModalWithATitle_When_ItComesBackMidExit_Then_ItsTitleIsShownAgain).
            MotionNativeTransitionGuard.ExcludeFromHeldList(element, s_visibility);
            if (ctx.LayoutIdProjections.GetValueOrDefault(element)?.Leader != null) End(element, ctx);
            element.style.visibility = visibility;
            MotionNativeTransitionGuard.Release(element, s_followOwner);
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

        // MUTANT_SURVIVES(equivalent): no float s puts |s - 1| at exactly 1e-5f. Near 1, s - 1 is exact and a
        // whole number of s's spacing, 2^-23 or 2^-24, and 1e-5f is a whole number of neither.
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
        public LayoutIdBox(VisualElement? parent, Rect local, Vector2 panelCentre, Vector2 panelSize, Vector2 ancestorScale,
            LayoutIdLook? look)
        {
            Look = look;
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
        public LayoutIdLook? Look { get; }

        // Without the parent it was read under, which the pool has taken back and can hand to another
        // Motion's element, where it would pass for that parent
        // (Given_TwoListComponentsHoldingTheCardInAPooledButton_When_TheSecondIsSelected_Then_TheCardTweensFromTheFirst).
        public LayoutIdBox Detached() => new(null, Local, PanelCentre, PanelSize, AncestorScale, Look?.Released());

        // Without the holder the look was read off, which has let go of the id.
        public LayoutIdBox Released() => new(Parent, Local, PanelCentre, PanelSize, AncestorScale, Look?.Released());
    }

    // A relegated presence child waiting for the lead its id passed to.
    internal sealed class LayoutIdLanding
    {
        public LayoutIdLanding(VisualElement root, string layoutId, System.Action landed)
        {
            Root = root;
            LayoutId = layoutId;
            Landed = landed;
        }

        // The element the child's exit started on.
        public VisualElement Root { get; }
        public string LayoutId { get; }
        public System.Action Landed { get; }
    }

    internal sealed class LayoutIdPendingSettle
    {
        public LayoutIdPendingSettle(LayoutIdBox? from, Rect patchedLayout, LayoutIdTiming timing)
        {
            From = from;
            PatchedLayout = patchedLayout;
            Timing = timing;
        }

        // Null for a wait that only projects the element once it is first laid out.
        public LayoutIdBox? From { get; }
        // From was read off this same element as it was drawn, rather than off another holder of the id or a
        // fallback box.
        public bool ReadOffItself { get; init; }
        public Rect PatchedLayout { get; }
        public LayoutIdTiming Timing { get; }
        public EventCallback<GeometryChangedEvent> Callback { get; set; } = null!;
    }

    internal sealed class LayoutIdProjection
    {
        public LayoutIdProjection(VisualElement host, StyleTranslate ownInlineTranslate, StyleScale ownInlineScale,
            Vector3 ownTranslate, Vector3 ownScale)
        {
            Host = host;
            OwnInlineTranslate = ownInlineTranslate;
            OwnInlineScale = ownInlineScale;
            OwnTranslate = ownTranslate;
            OwnScale = ownScale;
        }

        // The panel whose frame steps and ends this projection.
        public readonly VisualElement Host;

        // The natural box where the progress starts, relative to the parent's drawn corner in undistorted units.
        public Rect From;
        public LayoutIdProgress Progress = null!;
        public bool Moving;

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

        // The lead this member is drawn over while the lead crossfades, or null for a projection of its own.
        public VisualElement? Leader;
        // This lead took its box from another holder, and the other members are drawn over it while it moves.
        public bool Shared;
        // This lead fades in and the members behind it fade out while it moves.
        public bool Crossfade;
        // The crossfade progress a move of the lead's own interrupted it at, or NaN while it runs with the tween.
        public float FadeAt = float.NaN;

        // What the holder this lead took its box from looked like, while it moves from that box.
        public LayoutIdLook? FromLook;

        // Each element of a member's subtree whose picking it holds off while drawn over the lead (LayoutIdPicking).
        public HashSet<VisualElement>? Picking;
        public bool WritesOpacity;
    }

    // The transition a layoutId move takes: the Motion's own `transition`, or its Layout in place of it when
    // set, as Framer reads `transition.layout`, with Framer's default layout transition where the caller gave
    // V.Motion no timing at all. Its type decides the curve, the way it does for a variant swap.
    internal readonly struct LayoutIdTiming
    {
        // Framer's defaultLayoutTransition: { duration: 0.45, ease: [0.4, 0, 0.1, 1] }.
        private static readonly StyleTransitionConfig s_default = new()
        {
            Type = TransitionType.Bezier, DurationSec = 0.45f, BezierX1 = 0.4f, BezierY1 = 0f, BezierX2 = 0.1f, BezierY2 = 1f,
        };

        private readonly StyleTransitionConfig _config;

        private LayoutIdTiming(StyleTransitionConfig config)
        {
            _config = config;
        }

        public bool IsSpring => _config.Type == TransitionType.Spring;
        public float Stiffness => _config.Stiffness;
        public float Damping => _config.Damping;
        public float Mass => _config.Mass;
        public float DurationSec => _config.DurationSec;
        public float DelaySec => Mathf.Max(_config.DelaySec, 0f);

        public static LayoutIdTiming From(StyleTransitionConfig? transition) => new(transition?.Layout ?? transition ?? s_default);

        // False for a zero duration or a configuration the scheduler rejects, which lands the move at once.
        // Asked once a move has settled rather than on every patch, since a rejected one warns.
        public bool Animates()
        {
            var t = _config;
            // Not a switch naming each type: its catch-all would have to throw, where LayoutIdProgress, Ease and
            // StyleAnimationScheduler time a type neither a spring nor a bezier as a tween.
            return t.Type == TransitionType.Spring
                ? StyleAnimationScheduler.ValidateSpringParameters(t.Stiffness, t.Damping, t.Mass)
                : t.Type == TransitionType.Bezier
                    ? StyleAnimationScheduler.ValidateBezierParameters(t.BezierX1, t.BezierY1, t.BezierX2, t.BezierY2, t.DurationSec)
                    : StyleAnimationScheduler.ValidateDuration(t.DurationSec, null);
        }

        public LayoutIdProgress Start() => new(this);

        public float Ease(float t) => _config.Type == TransitionType.Bezier
            ? CubicBezierEvaluator.Evaluate(_config.BezierX1, _config.BezierY1, _config.BezierX2, _config.BezierY2, t)
            : UssEasing.Evaluate(_config.Easing, t);
    }

    // How far a projection still has to go, from 1 at the old box to 0 at the layout.
    internal sealed class LayoutIdProgress
    {
        // Framer Motion's projection animates a progress from 0 to 1000, at rest and on its main thread, whatever
        // the boxes' distance, so a spring's rest and its end are measured on that travel.
        private const float ProgressTravel = 1000f;

        private readonly LayoutIdTiming _timing;
        private readonly float _springPassSec;
        private float _elapsedSec;

        public LayoutIdProgress(LayoutIdTiming timing)
        {
            _timing = timing;
            _springPassSec = timing.IsSpring
                ? MotionSpringDriver.PassDurationSec(ProgressTravel, timing.Stiffness, timing.Damping, timing.Mass)
                : 0f;
        }

        public float Value { get; private set; } = 1f;

        // Returns true once the progress has arrived.
        public bool Step(float dtSec)
        {
            _elapsedSec += dtSec;
            // Inside the delay active is not positive: the spring is sampled at its start, and the tween's curve
            // clamps a time below 0 to its start.
            var active = _elapsedSec - _timing.DelaySec;
            if (_timing.IsSpring)
            {
                // Ends as Framer's spring animation does, when its pass has run, and never for a spring that does
                // not rest within 20 s.
                var ended = active >= _springPassSec;
                Value = ended ? 0f : 1f - MotionSpringDriver.SampleSpring(0f, ProgressTravel, 1f, 0f,
                    System.Math.Max(active, 0f), (_timing.Stiffness, _timing.Damping, _timing.Mass)) / ProgressTravel;
                return ended;
            }
            var t = active / _timing.DurationSec;
            Value = 1f - _timing.Ease(t);
            return t >= 1f;
        }
    }
}
