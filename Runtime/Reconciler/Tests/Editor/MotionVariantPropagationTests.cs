using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies V.Motion variant propagation through MotionContext: a descendant Motion that
    /// supplies <c>variants</c> but no explicit <c>animate</c> inherits the NEAREST ANCESTOR Motion's active
    /// label and resolves it against its own variants — so setting <c>animate</c> on a parent drives the whole
    /// subtree. The self-case (a node's own <c>animate</c>) is already merged into ClassNames at construction and
    /// keeps overriding any inherited label. Assertions read the mounted VisualElement's class list (the resolved
    /// state), not the VNode. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class MotionVariantPropagationTests
    {
        private static readonly Dictionary<string, string> Fade = new()
        {
            ["visible"] = "opacity-100",
            ["hidden"] = "opacity-0",
        };

        private static Action<int> s_consumerBump;
        private static Action<int> s_wrapperConsumerBump;
        private static Action<string> s_memoSetAnimate;
        private VisualElement _mountRoot;

        [SetUp]
        public void SetUp()
        {
            _mountRoot = new VisualElement();
            s_consumerBump = null;
            s_wrapperConsumerBump = null;
            s_memoSetAnimate = null;
        }

        // A MEMOIZED intermediate component between an animated parent Motion and an inheriting child Motion.
        // It bails (its body does not re-run) on the parent's re-render, yet context propagation must still
        // hold: the child follows the parent's label change. Velvet's inline expansion re-patches the bailed
        // component's committed output under the live ancestor label, so no UseContext subscription is needed —
        // the Motion consumes the label at the element level, not in a component body.
        [Component]
        private static VNode MemoParentHostRender()
        {
            var (label, setLabel) = Hooks.UseState("hidden");
            s_memoSetAnimate = setLabel;
            return V.Motion(key: "p", animate: label, children: new VNode[]
            {
                V.Component(MemoizedIntermediateRender, key: "mid"),
            });
        }

        private static int s_memoRenderCount;

        [Component(Memoize = true)]
        private static VNode MemoizedIntermediateRender()
        {
            s_memoRenderCount++;
            return V.Motion(name: "memo-leaf", key: "c", variants: Fade);
        }

        // A stateful intermediate component under an animated Motion that renders a child Motion. Its
        // self-setState triggers an ISOLATED re-render (not driven from the animated parent) — the case where
        // the inherited context survives because the consumer re-reads the provider value.
        [Component]
        private static VNode MotionConsumerRender()
        {
            var (_, bump) = Hooks.UseState(0);
            s_consumerBump = bump;
            return V.Motion(name: "animated-child", key: "c", variants: Fade);
        }

        [Component]
        private static VNode MotionHostRender()
            => V.Motion(key: "p", animate: "visible", children: new VNode[]
            {
                V.Component(MotionConsumerRender, key: "consumer"),
            });

        // Same as MotionConsumerRender, but WRAPPER-mounted: it sits under a DOM-less AnimatePresence (which
        // inline-expands its keyed children into the parent's slot range). Its self-setState exercises the
        // isolated re-render of a wrapper-hosted descendant — the FiberContextSpine edge for wrapper-hosted fibers.
        [Component]
        private static VNode WrapperMountedConsumerRender()
        {
            var (_, bump) = Hooks.UseState(0);
            s_wrapperConsumerBump = bump;
            return V.Motion(name: "wrapper-child", key: "c", variants: Fade);
        }

        [Component]
        private static VNode AnimatePresenceMotionHostRender()
            => V.Motion(key: "p", animate: "visible", children: new VNode[]
            {
                V.AnimatePresence(children: new VNode[]
                {
                    V.Component(WrapperMountedConsumerRender, key: "consumer"),
                }),
            });

        private static void Mount(ReconcilerScope scope, VNode[] tree)
            => scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

        // A plain (non-Motion) component sitting between an animated parent and a child Motion.
        // MotionContext is an ambient context, so it flows THROUGH intervening components — this is the consumer.
        [Component]
        private static VNode PassThroughChild(Dictionary<string, string> childVariants)
            => V.Motion(key: "c", variants: childVariants);

        [Test]
        public void Given_AMemoizedComponentBetweenParentAndChildMotion_When_TheParentAnimateChanges_Then_TheChildStillFollows()
        {
            // Arrange — child Motion inherits "hidden" through a MEMOIZED intermediate that bails on equal props.
            using var mounted = V.Mount(_mountRoot, V.Component(MemoParentHostRender, key: "host"));
            Assume.That(_mountRoot.Q("memo-leaf").ClassListContains("opacity-0"), Is.True,
                "Precondition: the child inherits the parent's initial 'hidden' label");

            // Act — flip the parent's animate. The intermediate is memoized; its body must NOT re-run (bail),
            // proving the child follows via inline-expansion re-patch under the live label, not a body re-render.
            s_memoRenderCount = 0;
            s_memoSetAnimate.Invoke("visible");
            mounted.FlushStateForTest();
            Assume.That(s_memoRenderCount, Is.EqualTo(0),
                "Precondition: the memoized intermediate bailed (body did not re-run), so this exercises memo-boundary propagation");

            // Assert — the child follows the parent across the memo boundary (context propagation).
            Assert.That(
                (_mountRoot.Q("memo-leaf").ClassListContains("opacity-100"), _mountRoot.Q("memo-leaf").ClassListContains("opacity-0")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AMotionWithInitial_When_MountedUnderAnimatePresence_Then_ItStartsAtTheInitialVariant()
        {
            // Arrange / Act — a direct-child Motion with an `initial` label enters under AnimatePresence.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.AnimatePresence(children: new VNode[]
                {
                    V.Motion(key: "a", variants: Fade, initial: "hidden", animate: "visible",
                        transition: new StyleTransitionConfig { DurationSec = 0.1f }),
                }),
            });

            // Assert — the enter from-state is variants[initial]=opacity-0, and the resting variants[animate]=opacity-100
            // is stripped during the from-frame (swapped back + kept on completion — see the PlayMode test).
            Assert.That(
                (scope.Root[0].ClassListContains("opacity-0"), scope.Root[0].ClassListContains("opacity-100")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AMotionWithExit_When_ResolvingExitTransition_Then_ItAnimatesFromAnimateToExitVariant()
        {
            // Arrange — a direct Motion child with an `exit` label.
            MotionNode motion = V.Motion(variants: Fade, animate: "visible", exit: "hidden");

            // Act — resolve the exit transition (node == motion: the direct-child case).
            var cfg = GeneralPathReconciler.TryResolveVariantExit(motion, motion);

            // Assert — it leaves the resting variants[animate] and animates to variants[exit].
            Assert.That((cfg.ExitFromClass, cfg.ExitToClass), Is.EqualTo(("opacity-100", "opacity-0")));
        }

        [Test]
        public void Given_AVariantMotionExiting_When_TheSameKeyIsReAddedMidExit_Then_TheRestingAnimateVariantSurvives()
        {
            // Arrange — a variant Motion (no `initial`) resting at variants[animate]=opacity-100 under AnimatePresence.
            using var scope = new ReconcilerScope();
            VNode[] Tree(bool present) => new VNode[]
            {
                V.AnimatePresence(children: present
                    ? new VNode[]
                    {
                        V.Motion(key: "a", variants: Fade, animate: "visible", exit: "hidden",
                            transition: new StyleTransitionConfig { DurationSec = 0.1f }),
                    }
                    : Array.Empty<VNode>()),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), Tree(true));
            Assume.That(scope.Root[0].ClassListContains("opacity-100"), Is.True,
                "Precondition: the Motion rests at variants[animate]=opacity-100");

            // Act — remove the key (exit begins; the ghost stays mounted) then re-add it BEFORE the exit
            // completes (exit-cancel / interrupt). The EditMode scheduler does not tick, so the exit stays
            // pending and the re-add triggers CancelExit synchronously.
            scope.Reconciler.Reconcile(scope.Root, Tree(true), Tree(false));
            Assume.That(scope.Root[0].ClassListContains("opacity-100"), Is.True,
                "Precondition: while exiting (pre-swap), the resting class is still present");
            scope.Reconciler.Reconcile(scope.Root, Tree(false), Tree(true));

            // Assert — cancelling the exit returns to the resting variant; opacity-100 must survive
            // (it must NOT be stripped by CancelExit and left unrecoverable due to a stale MotionAppliedClasses cache).
            Assert.That(scope.Root[0].ClassListContains("opacity-100"), Is.True,
                "Exit-cancel must restore the resting variants[animate], not leave the element without it");
        }

        [Test]
        public void Given_AVariantMotionWithoutExplicitTransition_When_ExitIsCancelled_Then_NoPresetEnterIsReplayed()
        {
            // Arrange — a variant Motion with NO explicit transition, so it defaults to StyleTransition.Fade (which
            // carries preset enter classes anim-fade-enter-*). It rests at variants[animate]=opacity-100.
            using var scope = new ReconcilerScope();
            VNode[] Tree(bool present) => new VNode[]
            {
                V.AnimatePresence(children: present
                    ? new VNode[] { V.Motion(key: "a", variants: Fade, animate: "visible", exit: "hidden") }
                    : Array.Empty<VNode>()),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), Tree(true));
            Assume.That(scope.Root[0].ClassListContains("opacity-100"), Is.True,
                "Precondition: rests at variants[animate]");

            // Act — exit, then re-add the key mid-exit (interrupt / exit-cancel).
            scope.Reconciler.Reconcile(scope.Root, Tree(true), Tree(false));
            scope.Reconciler.Reconcile(scope.Root, Tree(false), Tree(true));

            // Assert — a variant Motion is variant-managed (resting restored by CancelExit); cancelling its exit must
            // NOT replay the classic preset (Fade) enter on top of the restored resting variant.
            Assert.That(scope.Root[0].ClassListContains("anim-fade-enter-from"), Is.False,
                "No classic preset enter should fire for a variant Motion on exit-cancel");
        }

        [Test]
        public void Given_RemovedChildWithNoExitAnimation_When_Removed_Then_OnExitCompleteFires()
        {
            // Arrange — an AnimatePresence child whose Motion has a zero-duration transition (no exit animation).
            using var scope = new ReconcilerScope();
            var fired = 0;
            VNode[] Tree(bool present) => new VNode[]
            {
                V.AnimatePresence(onExitComplete: () => fired++, children: present
                    ? new VNode[]
                    {
                        V.Motion(key: "a", transition: new StyleTransitionConfig { DurationSec = 0f },
                            children: new VNode[] { V.Label(text: "x") }),
                    }
                    : Array.Empty<VNode>()),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), Tree(true));
            Assume.That(fired, Is.EqualTo(0), "Precondition: no exit yet");

            // Act — remove the child (instant removal, no exit animation to drive a PlayExit callback).
            scope.Reconciler.Reconcile(scope.Root, Tree(true), Tree(false));

            // Assert — onExitComplete still fires even though the removed child had no exit animation.
            Assert.That(fired, Is.EqualTo(1),
                "onExitComplete must fire even when all removed children exit instantly (no animation)");
        }

        [Test]
        public void Given_AVariantMotionEnter_When_Mounted_Then_TransitionPropertyIsSetSoTheSwapTweens()
        {
            // Arrange / Act — a variant Motion enters (initial → animate). The variant utility classes
            // (opacity-0 ↔ opacity-100) carry no transition-* of their own.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.AnimatePresence(children: new VNode[]
                {
                    V.Motion(key: "a", variants: Fade, initial: "hidden", animate: "visible",
                        transition: new StyleTransitionConfig { DurationSec = 0.1f }),
                }),
            });

            // Assert — transition-property: all is applied so the class swap TWEENS rather than snapping (a
            // duration-only transition tweens; without this the variant change would be instant).
            var tp = scope.Root[0].style.transitionProperty;
            Assert.That(tp.keyword == StyleKeyword.Null ? "(unset)" : tp.value[0].ToString(), Is.EqualTo("all"));
        }

        [Test]
        public void Given_DelayChildren_When_StaggerDelayComputed_Then_AddsTheFixedDelayToEachChild()
        {
            // delayChildren=0.5 + stagger=0.1 → child 2 of 5 is delayed 0.5 + 0.1*2 = 0.7.
            var presence = (AnimatePresenceNode)V.AnimatePresence(staggerSec: 0.1f, delayChildrenSec: 0.5f);
            Assert.That(presence.StaggerDelaySec(2, 5), Is.EqualTo(0.7f).Within(1e-5f));
        }

        [Test]
        public void Given_StaggerDirectionReverse_When_StaggerDelayComputed_Then_TheLastChildAnimatesFirst()
        {
            // direction=-1 over 3 children: index 0 → 0.1*(3-1-0)=0.2 (last), index 2 → 0.1*(3-1-2)=0 (first).
            var presence = (AnimatePresenceNode)V.AnimatePresence(staggerSec: 0.1f, staggerDirection: -1);
            Assert.That(
                (presence.StaggerDelaySec(0, 3), presence.StaggerDelaySec(2, 3)),
                Is.EqualTo((0.2f, 0f)).Within(1e-5f));
        }

        [Test]
        public void Given_DefaultDirection_When_StaggerDelayComputed_Then_FirstChildHasNoDelay()
        {
            // Forward (default): index 0 → 0, index 2 → 0.1*2 = 0.2 (unchanged from the original stagger).
            var presence = (AnimatePresenceNode)V.AnimatePresence(staggerSec: 0.1f);
            Assert.That(
                (presence.StaggerDelaySec(0, 5), presence.StaggerDelaySec(2, 5)),
                Is.EqualTo((0f, 0.2f)).Within(1e-5f));
        }

        [Test]
        public void Given_AMotionWithItsOwnAnimate_When_Mounted_Then_ItsElementCarriesTheResolvedVariantClass()
        {
            // Arrange / Act — a standalone Motion resolving its own label (no longer baked at construction).
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Motion("item", key: "m", variants: Fade, animate: "visible") });

            // Assert — the variant is resolved onto the element at reconcile time.
            Assert.That(scope.Root[0].ClassListContains("opacity-100"), Is.True);
        }

        [Test]
        public void Given_AParentMotionWithAnimateVisible_When_AChildMotionHasVariantsButNoAnimate_Then_TheChildResolvesTheParentsVisibleVariant()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act — parent drives the subtree; child only declares its variants.
            Mount(scope, new VNode[]
            {
                V.Motion(key: "p", animate: "visible", children: new VNode[]
                {
                    V.Motion(key: "c", variants: Fade),
                }),
            });

            // Assert
            Assert.That(scope.Root[0][0].ClassListContains("opacity-100"), Is.True);
        }

        [Test]
        public void Given_AChildInheritingHidden_When_TheParentAnimateChangesToVisible_Then_TheChildVariantClassSwitches()
        {
            // Arrange — mounted with the parent showing "hidden".
            using var scope = new ReconcilerScope();
            var hidden = new VNode[]
            {
                V.Motion(key: "p", animate: "hidden", children: new VNode[] { V.Motion(key: "c", variants: Fade) }),
            };
            var visible = new VNode[]
            {
                V.Motion(key: "p", animate: "visible", children: new VNode[] { V.Motion(key: "c", variants: Fade) }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), hidden);
            Assume.That(scope.Root[0][0].ClassListContains("opacity-0"), Is.True);

            // Act — the parent's active label changes.
            scope.Reconciler.Reconcile(scope.Root, hidden, visible);

            // Assert — the child swaps to the new label's class and drops the old one.
            Assert.That(
                (scope.Root[0][0].ClassListContains("opacity-100"), scope.Root[0][0].ClassListContains("opacity-0")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AChildWithItsOwnAnimate_When_NestedUnderADifferentlyAnimatedParent_Then_TheChildSelfLabelWins()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act — child explicitly animates "hidden" under a parent animating "visible".
            Mount(scope, new VNode[]
            {
                V.Motion(key: "p", animate: "visible", children: new VNode[]
                {
                    V.Motion(key: "c", variants: Fade, animate: "hidden"),
                }),
            });

            // Assert — the self label overrides the inherited one.
            Assert.That(
                (scope.Root[0][0].ClassListContains("opacity-0"), scope.Root[0][0].ClassListContains("opacity-100")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AChildWithoutVariants_When_NestedUnderAnAnimatedParent_Then_NoVariantClassIsPropagated()
        {
            // Arrange — parent has variants + animate; child declares none.
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[]
            {
                V.Motion(key: "p", variants: Fade, animate: "visible", children: new VNode[]
                {
                    V.Motion("p-4", key: "c"),
                }),
            });

            // Assert — a variant-less child keeps only its base classes; nothing is propagated onto it.
            Assert.That(scope.Root[0][0].ClassListContains("opacity-100"), Is.False);
        }

        [Test]
        public void Given_AGrandchildMotion_When_OnlyTheTopAncestorSetsAnimate_Then_ItResolvesTheTopAncestorsLabel()
        {
            // Arrange — a middle Motion with variants but no animate must pass the label through to the grandchild.
            using var scope = new ReconcilerScope();
            var scale = new Dictionary<string, string> { ["visible"] = "scale-100", ["hidden"] = "scale-0" };

            // Act
            Mount(scope, new VNode[]
            {
                V.Motion(key: "top", animate: "visible", children: new VNode[]
                {
                    V.Motion(key: "mid", variants: Fade, children: new VNode[]
                    {
                        V.Motion(key: "leaf", variants: scale),
                    }),
                }),
            });

            // Assert — the label flows through the middle level down to the grandchild.
            Assert.That(scope.Root[0][0][0].ClassListContains("scale-100"), Is.True);
        }

        [Test]
        public void Given_APlainComponentBetweenAnAnimatedParentAndAChildMotion_When_Mounted_Then_TheLabelFlowsThroughTheComponent()
        {
            // Arrange — the label must cross a non-Motion component boundary (ambient-context semantics).
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[]
            {
                V.Motion(key: "p", animate: "visible", children: new VNode[]
                {
                    V.Component<Dictionary<string, string>>(PassThroughChild, Fade, key: "mid"),
                }),
            });

            // Assert — the component's child Motion still inherits the ancestor Motion's active label.
            Assert.That(scope.Root[0][0].ClassListContains("opacity-100"), Is.True);
        }

        [Test]
        public void Given_AnInheritedMotion_When_AnIntermediateComponentReRendersInIsolation_Then_TheInheritedVariantSurvives()
        {
            // Arrange — child Motion inherits "visible" through a stateful intermediate component.
            using var mounted = V.Mount(_mountRoot, V.Component(MotionHostRender, key: "host"));
            Assume.That(_mountRoot.Q("animated-child").ClassListContains("opacity-100"), Is.True,
                "Precondition: the child inherits the ancestor Motion's label on mount");

            // Act — the intermediate component re-renders on its OWN setState (not from the animated parent).
            s_consumerBump.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — ambient-context semantics: the inherited label survives the isolated re-render.
            Assert.That(_mountRoot.Q("animated-child").ClassListContains("opacity-100"), Is.True,
                "An isolated re-render must reconstruct the ancestor MotionContext, not drop to the default");
        }

        [Test]
        public void Given_AnInheritedMotionUnderAnimatePresence_When_AWrapperHostedComponentReRendersInIsolation_Then_TheInheritedVariantSurvives()
        {
            // Arrange — a child Motion inherits "visible" through a DOM-less AnimatePresence wrapper.
            using var mounted = V.Mount(_mountRoot, V.Component(AnimatePresenceMotionHostRender, key: "host"));
            Assume.That(_mountRoot.Q("wrapper-child").ClassListContains("opacity-100"), Is.True,
                "Precondition: the wrapper-mounted child inherits the ancestor Motion's label on mount");

            // Act — the wrapper-hosted component re-renders on its OWN setState (an isolated re-render whose
            // spine reconstruction must descend THROUGH the AnimatePresenceNode to reach this fiber).
            s_wrapperConsumerBump.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the inherited label survives the isolated re-render of an AnimatePresence-hosted descendant.
            Assert.That(_mountRoot.Q("wrapper-child").ClassListContains("opacity-100"), Is.True,
                "An isolated re-render of an AnimatePresence-hosted descendant must reconstruct the ancestor MotionContext");
        }

        [Test]
        public void Given_APropagatedMotionOnAPooledWidget_When_ItUnmountsAndThePooledWidgetIsReused_Then_NoStaleVariantClassRemains()
        {
            // Arrange — a pooled Button Motion that inherits "opacity-100" by propagation.
            using var scope = new ReconcilerScope();
            var withButton = new VNode[]
            {
                V.Motion(key: "p", animate: "visible", children: new VNode[]
                {
                    V.Motion(key: "c", variants: Fade, elementType: typeof(Button)),
                }),
            };
            var plain = new VNode[] { V.Button("plain", key: "b") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), withButton);
            Assume.That(scope.Root[0][0].ClassListContains("opacity-100"), Is.True);

            // Act — unmount (the Button returns to the pool), then mount a plain Button that re-rents it.
            scope.Reconciler.Reconcile(scope.Root, withButton, Array.Empty<VNode>());
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), plain);

            // Assert — the reused widget carries no ghost of the propagated variant class.
            Assert.That(scope.Root[0].ClassListContains("opacity-100"), Is.False);
        }
    }
}
