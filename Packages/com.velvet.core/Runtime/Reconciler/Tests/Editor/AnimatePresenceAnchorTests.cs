using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which Motions a keyed AnimatePresence child's removal plays an exit on, and what its first mount
    /// suppresses, for a keyed child that is the Motion itself or puts a Provider, a Fragment, a component, a memo, a
    /// Suspense, an element or another Motion around it: the Motions in the removed child's committed subtree
    /// exit and hold the child until they have, a coordinator's exit label and stagger reach the Motions that
    /// inherit its labels, an inner AnimatePresence's child is that presence's, and the presence's
    /// <c>initial: false</c> reaches the Motions its first render creates.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresenceAnchorTests
    {
        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        // A coordinator whose exit pose carries only its children's stagger.
        private static readonly Dictionary<string, MotionVariant> s_coordinator = new()
        {
            ["visible"] = "",
            ["hidden"] = new MotionVariant("", new StyleTransitionConfig { StaggerChildrenSec = 0.1f }),
        };

        // A coordinator whose exit pose springs its opacity 1 → 0, which Framer's default spring ends at 1.05 s on
        // the browser, and holds its inheriting children until then. DurationSec, which a spring does not read,
        // declares 0.05 s.
        private static readonly Dictionary<string, MotionVariant> s_springCoordinator = new()
        {
            ["visible"] = "opacity-100",
            ["hidden"] = new MotionVariant("opacity-0", new StyleTransitionConfig
            {
                Type = TransitionType.Spring, DurationSec = 0.05f, When = TransitionWhen.BeforeChildren,
            }),
        };

        private static readonly ComponentContext<int> s_context = ComponentContext<int>.Create(0);

        private readonly record struct KeySetState(string Keys);

        private sealed class KeySetStore : Store<KeySetState>
        {
            public KeySetStore(string initial) : base(new KeySetState(initial)) { }
            public void Set(string keys) => SetState(_ => new KeySetState(keys));
            protected override void ResetCore() => SetState(_ => new KeySetState("a"));
        }

        private readonly record struct FlagState(bool On);

        private sealed class FlagStore : Store<FlagState>
        {
            public FlagStore() : base(new FlagState(true)) { }
            public void Set(bool on) => SetState(_ => new FlagState(on));
            protected override void ResetCore() => SetState(_ => new FlagState(true));
        }

        private static KeySetStore s_keyStore;
        private static FlagStore s_flagStore;
        private static FlagStore s_showStore;
        private static string s_wrapper;
        private static int s_exitsCompleted;
        private static bool s_presenceInitial;
        private static VisualElement s_portalTarget;
        private static Dictionary<string, MotionVariant> s_inheritingVariants;
        private static string s_hostLabel;
        private static bool s_nestInner;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_keyStore = null;
            s_flagStore = null;
            s_showStore = null;
            s_wrapper = null;
            s_exitsCompleted = 0;
            s_presenceInitial = false;
            s_portalTarget = null;
            s_inheritingVariants = s_fade;
            s_hostLabel = "visible";
            s_nestInner = false;
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private int HostChildCount => Root.Q<VisualElement>("host").childCount;

        // Declares an enter and an exit of its own, so either one playing is readable off its element.
        private static VNode TimedMotion(string key) => V.Motion(name: "item", key: key, variants: s_fade,
            initial: "hidden", animate: "visible", exit: "hidden",
            transition: new StyleTransitionConfig { DurationSec = 0.3f });

        // Inherits its label from the coordinator above it, with no exit of its own.
        private static VNode InheritingMotion(int index) => V.Motion(name: "child" + index, variants: s_fade,
            transition: new StyleTransitionConfig { DurationSec = 0.05f });

        [Component]
        private static VNode MotionRender() => TimedMotion(null);

        // Exits more slowly than TimedMotion, so the two exiting together settle at different times.
        private static VNode SlowMotion() => V.Motion(name: "slow", variants: s_fade, animate: "visible",
            exit: "hidden", transition: new StyleTransitionConfig { DurationSec = 0.6f });

        // The timed Motion while s_flagStore is on, a plain element once it is off.
        [Component]
        private static VNode ToggledMotionRender()
        {
            var on = Hooks.UseStore(s_flagStore, s => s.On);
            return on ? TimedMotion(null) : V.Div(name: "placeholder");
        }

        // A Motion with no variants, whose exit label therefore plays the classic exit of its transition.
        [Component]
        private static VNode ClassicRender() => V.Motion(name: "item", exit: "gone", transition: StyleTransition.Fade);

        [Component]
        private static VNode InnerPresenceRender() => V.AnimatePresence(key: "inner",
            children: new[] { TimedMotion("inner-item") });

        [Component]
        private static VNode CoordinatorRender() => V.Motion(variants: s_coordinator, animate: "visible",
            exit: "hidden", children: new[] { InheritingMotion(0), InheritingMotion(1), InheritingMotion(2) });

        [Component]
        private static VNode SpringCoordinatorRender() => V.Motion(name: "spring-coordinator",
            variants: s_springCoordinator, animate: "visible", exit: "hidden", children: new[] { InheritingMotion(0) });

        // child0 has an inheriting child of its own, which is its to number rather than the coordinator's.
        [Component]
        private static VNode NestedCoordinatorRender() => V.Motion(variants: s_coordinator, animate: "visible",
            exit: "hidden", children: new[]
            {
                V.Motion(name: "child0", variants: s_fade, transition: new StyleTransitionConfig { DurationSec = 0.05f },
                    children: new[] { InheritingMotion(3) }),
                InheritingMotion(1),
            });

        // Ahead of the two inheriting children: a Motion naming its own exit, one naming its own animate, and a
        // variant child whose variants name no pose for the exit label.
        [Component]
        private static VNode MixedCoordinatorRender() => V.Motion(variants: s_coordinator, animate: "visible",
            exit: "hidden", children: new[]
            {
                V.Motion(name: "own-exit", variants: s_fade, exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                V.Motion(name: "own-animate", variants: s_fade, animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                V.Motion(name: "no-pose", variants: new Dictionary<string, MotionVariant> { ["visible"] = "opacity-100" },
                    transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                InheritingMotion(1),
                InheritingMotion(2),
            });

        // The keyed child s_wrapper names: the same TimedMotion, or a wrapper around it. "nested" puts it inside
        // a Motion that plays no exit of its own.
        private static VNode KeyedChild(string key) => s_wrapper switch
        {
            "direct" => TimedMotion(key),
            "provider" => V.Provider(s_context, 1, new[] { TimedMotion(null) }, key: key),
            "component" => V.Component(MotionRender, key: key),
            "memo" => V.MemoizedWithKey(key, () => TimedMotion(null)),
            "suspense" => V.Suspense(V.Label(text: "loading"), new[] { TimedMotion(null) }, key: key),
            "element" => V.Div(key: key, children: new[] { TimedMotion(null) }),
            "nested" => V.Motion(key: key, variants: s_fade, animate: "visible",
                children: new[] { TimedMotion(null) }),
            "toggled" => V.Component(ToggledMotionRender, key: key),
            "coordinator" => V.Component(CoordinatorRender, key: key),
            "coordinator-nested" => V.Component(NestedCoordinatorRender, key: key),
            "coordinator-spring" => V.Component(SpringCoordinatorRender, key: key),
            "coordinator-mixed" => V.Component(MixedCoordinatorRender, key: key),
            "inner-presence" => V.Div(key: key, children: new VNode[]
            {
                V.AnimatePresence(key: "inner", children: new[] { TimedMotion("inner-item") }),
            }),
            "inner-presence-top" => V.Component(InnerPresenceRender, key: key),
            "anchor-and-descendant" => V.Motion(name: "item", key: key, variants: s_fade, initial: "hidden",
                animate: "visible", exit: "hidden", transition: new StyleTransitionConfig { DurationSec = 0.3f },
                children: new[] { SlowMotion() }),
            "classic" => V.Component(ClassicRender, key: key),
            "named" => V.Motion(name: "item-" + key, key: key, variants: s_fade, initial: "hidden",
                animate: "visible", exit: "hidden", transition: new StyleTransitionConfig { DurationSec = 0.3f }),
            "fragment" => V.Fragment(new[] { TimedMotion(null) }, key: key),
            "fragment-pair" => V.Fragment(new[] { TimedMotion(null), V.Div(name: "sibling") }, key: key),
            "z-nested" => V.Div(key: key, children: new[]
            {
                V.Div(className: "absolute z-10", children: new[] { TimedMotion(null) }),
            }),
            "portal" => V.Div(key: key, children: new VNode[] { V.Portal(s_portalTarget, new[] { TimedMotion(null) }) }),
            _ => throw new System.ArgumentOutOfRangeException(nameof(s_wrapper), s_wrapper, null),
        };

        private static VNode Presence(string keys)
        {
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(KeyedChild(key.ToString()));
            }
            return V.AnimatePresence(key: "presence", initial: s_presenceInitial, children: children.ToArray(),
                onExitComplete: () => s_exitsCompleted++);
        }

        // A Motion outside the presence, portalled into the same target after the presence child's own portal,
        // so its content sits in the slot right after that portal's. It declares an exit, so a walk that strayed
        // into that slot would play one.
        private static VNode OutsidePortal() => s_portalTarget == null ? null : V.Portal(s_portalTarget, new[]
        {
            V.Motion(name: "other", variants: s_fade, animate: "visible", exit: "hidden",
                transition: new StyleTransitionConfig { DurationSec = 0.3f }),
        });

        [Component]
        private static VNode PresenceHost()
        {
            var keys = Hooks.UseStore(s_keyStore, s => s.Keys);
            return V.Div(name: "host", children: new[] { Presence(keys), OutsidePortal() });
        }

        // Keyed children whose element type flips with s_flagStore while their keys stay, so the reconciler creates
        // their Motions again rather than patching them. Under s_nestInner each Motion holds an inner presence whose
        // only child carries no Motion.
        [Component]
        private static VNode TypeFlipPresenceHost()
        {
            var keys = Hooks.UseStore(s_keyStore, s => s.Keys);
            var on = Hooks.UseStore(s_flagStore, s => s.On);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item", key: key.ToString(), elementType: on ? null : typeof(Box),
                    variants: s_fade, initial: "hidden", animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f },
                    children: s_nestInner
                        ? new VNode[] { V.AnimatePresence(key: "inner", children: new VNode[] { V.Div(key: "x") }) }
                        : null));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: s_presenceInitial, children: children.ToArray()),
            });
        }

        // Keyed children that inherit the label of the Motion above the presence, s_hostLabel, and play the classic
        // Fade preset's exit on removal.
        [Component]
        private static VNode InheritingPresenceHost()
        {
            var keys = Hooks.UseStore(s_keyStore, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item", key: key.ToString(), variants: s_inheritingVariants,
                    transition: StyleTransition.Fade));
            }
            return V.Motion(name: "host", animate: s_hostLabel, children: new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: s_presenceInitial, children: children.ToArray()),
            });
        }

        // Keyed Fragments placing a Label ahead of a Motion that plays the Fade preset's enter and exit.
        [Component]
        private static VNode LabelFirstFragmentHost()
        {
            var keys = Hooks.UseStore(s_keyStore, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Fragment(new VNode[]
                {
                    V.Label(name: "caption", text: "x"),
                    V.Motion(name: "item", transition: StyleTransition.Fade),
                }, key: key.ToString()));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: s_presenceInitial, children: children.ToArray()),
            });
        }

        // PresenceHost's presence, taken out of the tree while s_showStore is off.
        [Component]
        private static VNode ShowablePresenceHost()
        {
            var keys = Hooks.UseStore(s_keyStore, s => s.Keys);
            var shown = Hooks.UseStore(s_showStore, s => s.On);
            return V.Div(name: "host", children: new[] { shown ? Presence(keys) : null });
        }

        private float ItemDurationMs() => DurationMs("item");

        // The inline transition-duration the scheduler wrote on the named element, in milliseconds, or NaN when
        // nothing holds the slot or the element is gone — the same reading MotionVariantTransitionTests takes.
        private float DurationMs(string name)
        {
            var element = Root.Q<VisualElement>(name);
            if (element == null) return float.NaN;
            var duration = element.style.transitionDuration;
            return duration.keyword != StyleKeyword.Null && duration.value != null && duration.value.Count > 0
                ? duration.value[0].value
                : float.NaN;
        }

        private MountedTree MountSettled(string wrapper, string keys)
        {
            s_wrapper = wrapper;
            s_keyStore = new KeySetStore(keys);
            var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));
            Frames(40);
            return mounted;
        }

        private bool HasClass(string name, string className)
            => Root.Q<VisualElement>(name)?.ClassListContains(className) == true;

        private static void Drain(MountedTree mounted) => mounted.GetSchedulerForTest().DrainImmediateForTest();

        private void Frames(int count)
        {
            for (var i = 0; i < count; i++) _sim.FrameUpdateMs(16);
        }

        // GREEN_ON_BASE(characterization): an anchor's removal already waited for its exit.
        [TestCase("direct")]
        [TestCase("provider")]
        public void Given_AnAnchorDeclaringExit_When_TheKeyIsRemoved_Then_TheChildIsHeldForItsExit(string wrapper)
        {
            // Arrange — settled first, so the removal is from rest.
            using var mounted = MountSettled(wrapper, "a");
            using var keys = s_keyStore;

            // Act
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — an exiting ghost still holds the host's slot.
            Assert.That(HostChildCount, Is.EqualTo(1));
        }

        [TestCase("component")]
        [TestCase("memo")]
        [TestCase("suspense")]
        [TestCase("element")]
        [TestCase("nested")]
        public void Given_ATimedMotionBehindAWrapper_When_TheKeyIsRemoved_Then_TheChildIsHeldForItsExit(string wrapper)
        {
            // Arrange — settled first, so the removal is from rest.
            using var mounted = MountSettled(wrapper, "a");
            using var keys = s_keyStore;

            // Act
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the child is held, and the Motion's own 300ms exit is what is playing on its element.
            Assert.That((HostChildCount, ItemDurationMs()), Is.EqualTo((1, 300f)));
        }

        [Test]
        public void Given_AComponentChildExiting_When_ItsMotionsExitCompletes_Then_TheChildIsRemoved()
        {
            // Arrange
            using var mounted = MountSettled("component", "a");
            using var keys = s_keyStore;
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var heldWhileExiting = HostChildCount;

            // Act — well past the 300ms exit.
            Frames(40);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — held while the exit played, then gone, with onExitComplete fired once.
            Assert.That((heldWhileExiting, HostChildCount, s_exitsCompleted), Is.EqualTo((1, 0, 1)));
        }

        // "element" puts the Motion below the ghost's first element, which the removal's own cancel reaches.
        [TestCase("component")]
        [TestCase("element")]
        public void Given_AChildExiting_When_TheKeyReturnsBeforeTheExitCompletes_Then_ItsMotionRestsVisible(string wrapper)
        {
            // Arrange
            using var mounted = MountSettled(wrapper, "a");
            using var keys = s_keyStore;
            var before = Root.Q<VisualElement>("item");
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Frames(3);

            // Act — back mid-exit, then past where the exit would have ended.
            keys.Set("a");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Frames(40);

            // Assert — the same element, back at its resting pose with the exit pose gone.
            var item = Root.Q<VisualElement>("item");
            Assert.That(
                (ReferenceEquals(item, before), item.ClassListContains("opacity-100"), item.ClassListContains("opacity-0")),
                Is.EqualTo((true, true, false)));
        }

        [Test]
        public void Given_AnAnchorInheritingItsLabelExiting_When_TheKeyReturnsBeforeTheExitCompletes_Then_ItRestsWithNoPresetEnter()
        {
            // Arrange
            using var keys = new KeySetStore("a");
            s_keyStore = keys;
            using var mounted = V.Mount(Root, V.Component(InheritingPresenceHost, key: "root"));
            Frames(40);
            keys.Set(string.Empty);
            Drain(mounted);
            Frames(3);

            // Act — back mid-exit.
            keys.Set("a");
            Drain(mounted);

            // Assert — resting at the inherited pose, with the Fade preset's enter not started over it.
            var item = Root.Q<VisualElement>("item");
            Assert.That((item.ClassListContains("anim-fade-enter-from"), item.ClassListContains("opacity-100")),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AnAnchorInheritingItsLabelWithNoInitial_When_ItsKeyIsAdded_Then_ItRestsWithNoPresetEnter()
        {
            // Arrange — no initial label anywhere, so no variant enter resolves.
            using var keys = new KeySetStore(string.Empty);
            s_keyStore = keys;
            using var mounted = V.Mount(Root, V.Component(InheritingPresenceHost, key: "root"));
            Frames(40);

            // Act
            keys.Set("a");
            Drain(mounted);

            // Assert — resting at the inherited pose, with the Fade preset's enter not started over it.
            var item = Root.Q<VisualElement>("item");
            Assert.That((item.ClassListContains("anim-fade-enter-from"), item.ClassListContains("opacity-100")),
                Is.EqualTo((false, true)));
        }

        // GREEN_ON_BASE(characterization): a Motion no animate label reaches plays its preset's enter, as before.
        [Test]
        public void Given_AMotionWithVariantsThatNoLabelReaches_When_ItsKeyIsAdded_Then_ItsPresetEnterPlays()
        {
            // Arrange
            s_hostLabel = null;
            using var keys = new KeySetStore(string.Empty);
            s_keyStore = keys;
            using var mounted = V.Mount(Root, V.Component(InheritingPresenceHost, key: "root"));
            Frames(40);

            // Act
            keys.Set("a");
            Drain(mounted);

            // Assert — the Fade preset's enter has started on it.
            Assert.That(HasClass("item", "anim-fade-enter-from"), Is.True);
        }

        // GREEN_ON_BASE(characterization): a Motion with no variants plays its preset's enter under a label, as before.
        [Test]
        public void Given_AMotionWithNoVariantsUnderAnInheritedLabel_When_ItsKeyIsAdded_Then_ItsPresetEnterPlays()
        {
            // Arrange
            s_inheritingVariants = null;
            using var keys = new KeySetStore(string.Empty);
            s_keyStore = keys;
            using var mounted = V.Mount(Root, V.Component(InheritingPresenceHost, key: "root"));
            Frames(40);

            // Act
            keys.Set("a");
            Drain(mounted);

            // Assert — the Fade preset's enter has started on it.
            Assert.That(HasClass("item", "anim-fade-enter-from"), Is.True);
        }

        [Test]
        public void Given_AComponentChildExiting_When_ItsMotionUnmountsBeforeTheExitCompletes_Then_TheChildIsRemoved()
        {
            // Arrange
            using var flag = new FlagStore();
            s_flagStore = flag;
            using var mounted = MountSettled("toggled", "a");
            using var keys = s_keyStore;
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var heldWhileExiting = HostChildCount;

            // Act — the only exiting Motion leaves the tree, well inside its 300ms.
            flag.Set(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Frames(2);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — held while it exited, then nothing was left to wait for.
            Assert.That((heldWhileExiting, s_exitsCompleted), Is.EqualTo((1, 1)));
        }

        [Test]
        public void Given_ACoordinatorStaggeringItsExit_When_TheKeyIsRemoved_Then_ItsInheritingChildrenExitInTurn()
        {
            // Arrange
            using var mounted = MountSettled("coordinator", "a");
            using var keys = s_keyStore;

            // Act — past the first child's turn, short of the second's 100ms.
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Frames(4);

            // Assert — the exit label has reached the first child only.
            var swapped = new List<bool>();
            for (var i = 0; i < 3; i++)
            {
                swapped.Add(Root.Q<VisualElement>("child" + i)?.ClassListContains("opacity-0") == true);
            }
            Assert.That(string.Join(",", swapped), Is.EqualTo("True,False,False"));
        }

        [Test]
        public void Given_ACoordinatorWhoseExitSpringsBeforeChildren_When_TheKeyIsRemoved_Then_ItsChildWaitsForTheSpringToRest()
        {
            // Arrange
            using var mounted = MountSettled("coordinator-spring", "a");
            using var keys = s_keyStore;

            // Act — past the 0.05 s the pose declares, well short of the spring's 1.05 s.
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Frames(20);

            // Assert — the coordinator's own exit has swapped and its inheriting child has not started its own.
            Assert.That((HasClass("spring-coordinator", "opacity-0"), HasClass("child0", "opacity-0")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ACoordinatorStaggeringItsExit_When_AChildOfItsInheritingChildExits_Then_ItExitsWithItsOwnParent()
        {
            // Arrange — Framer numbers each variant parent's own children: child3 belongs to child0, so child1 is
            // the coordinator's second child.
            using var mounted = MountSettled("coordinator-nested", "a");
            using var keys = s_keyStore;

            // Act — past the first slot, short of the second's 100ms.
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Frames(4);

            // Assert
            Assert.That((HasClass("child0", "opacity-0"), HasClass("child3", "opacity-0"), HasClass("child1", "opacity-0")),
                Is.EqualTo((true, true, false)));
        }

        [Test]
        public void Given_ACoordinatorStaggeringItsExit_When_ItsChildrenMixOwnLabelsAndAPoselessVariantChild_Then_OnlyTheVariantChildrenTakeSlots()
        {
            // Arrange — own-exit and own-animate carry labels of their own and take no slot; no-pose is a variant
            // child with nothing to play and still takes slot 0, as Framer counts every variant child.
            using var mounted = MountSettled("coordinator-mixed", "a");
            using var keys = s_keyStore;

            // Act — past child1's 100ms slot, short of child2's 200ms one.
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Frames(9);

            // Assert
            Assert.That((HasClass("child1", "opacity-0"), HasClass("child2", "opacity-0")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AKeyedFragmentAroundATimedMotion_When_TheKeyIsRemoved_Then_TheChildIsHeldForItsExit()
        {
            // Arrange — settled first, so the removal is from rest.
            using var mounted = MountSettled("fragment", "a");
            using var keys = s_keyStore;

            // Act
            keys.Set(string.Empty);
            Drain(mounted);

            // Assert — the Fragment's element is held, and the Motion's own 300ms exit is what is playing on it.
            Assert.That((HostChildCount, ItemDurationMs()), Is.EqualTo((1, 300f)));
        }

        [Test]
        public void Given_AKeyedFragmentOfTwoElementsExiting_When_ItsMotionsExitCompletes_Then_BothLeaveTogether()
        {
            // Arrange
            using var mounted = MountSettled("fragment-pair", "a");
            using var keys = s_keyStore;
            keys.Set(string.Empty);
            Drain(mounted);
            var heldWhileExiting = HostChildCount;

            // Act — well past the 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert — both of the Fragment's elements held while the exit played, then both gone, with
            // onExitComplete fired once.
            Assert.That((heldWhileExiting, HostChildCount, s_exitsCompleted), Is.EqualTo((2, 0, 1)));
        }

        [Test]
        public void Given_AKeyedMotionSettled_When_ATypeFlipCreatesItAgainUnderItsKey_Then_TheNewElementEnters()
        {
            // Arrange — initial: true, so the key withholds nothing; settled well past the mount enter.
            s_presenceInitial = true;
            using var keys = new KeySetStore("a");
            s_keyStore = keys;
            using var flag = new FlagStore();
            s_flagStore = flag;
            using var mounted = V.Mount(Root, V.Component(TypeFlipPresenceHost, key: "root"));
            Frames(40);

            // Act
            flag.Set(false);
            Drain(mounted);

            // Assert — a new element, with the 300ms enter timing written on it.
            Assert.That((Root.Q<VisualElement>("item") is Box, ItemDurationMs()), Is.EqualTo((true, 300f)));
        }

        [Test]
        public void Given_AKeyedMotionExiting_When_ItsKeyReturnsWithATypeFlip_Then_TheNewElementEnters()
        {
            // Arrange — settled, then removed, and still exiting.
            s_presenceInitial = true;
            using var keys = new KeySetStore("a");
            s_keyStore = keys;
            using var flag = new FlagStore();
            s_flagStore = flag;
            using var mounted = V.Mount(Root, V.Component(TypeFlipPresenceHost, key: "root"));
            Frames(40);
            keys.Set(string.Empty);
            Drain(mounted);
            Frames(3);

            // Act — the key returns in the same render that flips its element type.
            flag.Set(false);
            keys.Set("a");
            Drain(mounted);

            // Assert — a new element, with the 300ms enter timing written on it.
            Assert.That((Root.Q<VisualElement>("item") is Box, ItemDurationMs()), Is.EqualTo((true, 300f)));
        }

        [Test]
        public void Given_AKeyedMotionHoldingAnInnerPresence_When_ATypeFlipCreatesItAgain_Then_TheNewElementEnters()
        {
            // Arrange — the inner presence's only child carries no Motion, so its own emission reports none created.
            s_presenceInitial = true;
            s_nestInner = true;
            using var keys = new KeySetStore("a");
            s_keyStore = keys;
            using var flag = new FlagStore();
            s_flagStore = flag;
            using var mounted = V.Mount(Root, V.Component(TypeFlipPresenceHost, key: "root"));
            Frames(40);

            // Act
            flag.Set(false);
            Drain(mounted);

            // Assert — a new element, with the 300ms enter timing written on it.
            Assert.That((Root.Q<VisualElement>("item") is Box, ItemDurationMs()), Is.EqualTo((true, 300f)));
        }

        // GREEN_ON_BASE(characterization): the base plays no enter on a persisting key after a new one.
        [Test]
        public void Given_ANewKeyAheadOfASettledOne_When_BothAreEmitted_Then_OnlyTheNewOneEnters()
        {
            // Arrange — "a" settled well past its mount enter.
            s_presenceInitial = true;
            using var mounted = MountSettled("named", "a");
            using var keys = s_keyStore;

            // Act — "b" is created ahead of "a", which the same pass then patches.
            keys.Set("ba");
            Drain(mounted);

            // Assert — the enter timing on "b" and none on "a".
            Assert.That((DurationMs("item-b"), float.IsNaN(DurationMs("item-a"))), Is.EqualTo((300f, true)));
        }

        // GREEN_ON_BASE(characterization): the base plays no enter when a Motion naming its own animate type-flips.
        [Test]
        public void Given_AKeyedMotionTheFirstRenderMountedUnderInitialFalse_When_ATypeFlipCreatesItAgain_Then_NoEnterPlays()
        {
            // Arrange — initial: false, which the key keeps withholding for as long as it stays.
            using var keys = new KeySetStore("a");
            s_keyStore = keys;
            using var flag = new FlagStore();
            s_flagStore = flag;
            using var mounted = V.Mount(Root, V.Component(TypeFlipPresenceHost, key: "root"));
            Frames(40);

            // Act
            flag.Set(false);
            Drain(mounted);

            // Assert — the new element, with no enter timing on it.
            Assert.That((Root.Q<VisualElement>("item") is Box, float.IsNaN(ItemDurationMs())), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AKeyedFragmentPlacingALabelAheadOfItsMotion_When_ItsKeyIsAdded_Then_TheMotionPlaysTheEnter()
        {
            // Arrange
            using var keys = new KeySetStore(string.Empty);
            s_keyStore = keys;
            using var mounted = V.Mount(Root, V.Component(LabelFirstFragmentHost, key: "root"));
            Frames(40);

            // Act
            keys.Set("a");
            Drain(mounted);

            // Assert — the Fade preset's enter has started on the Motion and not on the Label.
            Assert.That((HasClass("caption", "anim-fade-enter-from"), HasClass("item", "anim-fade-enter-from")),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AKeyedFragmentPlacingALabelAheadOfItsMotion_When_ItsKeyIsRemoved_Then_TheMotionPlaysTheExit()
        {
            // Arrange
            using var keys = new KeySetStore("a");
            s_keyStore = keys;
            using var mounted = V.Mount(Root, V.Component(LabelFirstFragmentHost, key: "root"));
            Frames(40);

            // Act
            keys.Set(string.Empty);
            Drain(mounted);

            // Assert — the Fade preset's exit has started on the Motion and not on the Label.
            Assert.That((HasClass("caption", "anim-fade-exit-from"), HasClass("item", "anim-fade-exit-from")),
                Is.EqualTo((false, true)));
        }

        // GREEN_ON_BASE(characterization): an inner presence's child never held the outer removal.
        [Test]
        public void Given_AnInnerAnimatePresence_When_TheOuterKeyIsRemoved_Then_TheInnerChildDoesNotHoldIt()
        {
            // Arrange
            using var mounted = MountSettled("inner-presence", "a");
            using var keys = s_keyStore;

            // Act
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the outer child leaves at once.
            Assert.That(HostChildCount, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): initial: false already suppressed an anchor's mount enter.
        [TestCase("direct")]
        [TestCase("provider")]
        public void Given_InitialFalseOnThePresence_When_AnAnchorMounts_Then_ItsEnterIsSuppressed(string wrapper)
        {
            // Arrange
            s_wrapper = wrapper;
            using var keys = new KeySetStore("a");
            s_keyStore = keys;

            // Act — an enter that plays writes its inline timing inside this reconcile.
            using var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));

            // Assert — mounted, with no enter timing on it.
            Assert.That((Root.Q<VisualElement>("item") != null, float.IsNaN(ItemDurationMs())), Is.EqualTo((true, true)));
        }

        [TestCase("component")]
        [TestCase("memo")]
        [TestCase("suspense")]
        [TestCase("element")]
        public void Given_InitialFalseOnThePresence_When_ATimedMotionBehindAWrapperMounts_Then_ItsEnterIsSuppressed(
            string wrapper)
        {
            // Arrange
            s_wrapper = wrapper;
            using var keys = new KeySetStore("a");
            s_keyStore = keys;

            // Act — an enter that plays writes its inline timing inside this reconcile.
            using var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));

            // Assert — mounted, with no enter timing on it.
            Assert.That((Root.Q<VisualElement>("item") != null, float.IsNaN(ItemDurationMs())), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AMotionInsideAZManagedElementExiting_When_ItsExitCompletes_Then_TheChildIsRemoved()
        {
            // Arrange
            using var mounted = MountSettled("z-nested", "a");
            using var keys = s_keyStore;
            keys.Set(string.Empty);
            Drain(mounted);
            var heldWhileExiting = HostChildCount;

            // Act — well past the 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert — held while the exit played, then gone, with onExitComplete fired once.
            Assert.That((heldWhileExiting, HostChildCount, s_exitsCompleted), Is.EqualTo((1, 0, 1)));
        }

        [Test]
        public void Given_AComponentChildRenderedAgain_When_TheKeyIsRemoved_Then_ItIsHeldUntilItsExitCompletes()
        {
            // Arrange — the reorder emits "a" a second time before its removal.
            using var mounted = MountSettled("component", "ab");
            using var keys = s_keyStore;
            keys.Set("ba");
            Drain(mounted);
            keys.Set("b");
            Drain(mounted);
            var heldWhileExiting = HostChildCount;

            // Act — well past the 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert — "a" held beside "b", then gone once, with onExitComplete fired once.
            Assert.That((heldWhileExiting, HostChildCount, s_exitsCompleted), Is.EqualTo((2, 1, 1)));
        }

        [Test]
        public void Given_AnAnchorAndADescendantExitingTogether_When_TheAnchorsExitCompletes_Then_TheChildIsHeldForTheSlowerOne()
        {
            // Arrange
            using var mounted = MountSettled("anchor-and-descendant", "a");
            using var keys = s_keyStore;
            keys.Set(string.Empty);
            Drain(mounted);

            // Act — past the anchor's 300ms, short of the descendant's 600ms; then past both.
            Frames(25);
            Drain(mounted);
            var heldPastTheAnchor = HostChildCount;
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((heldPastTheAnchor, HostChildCount), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_AMotionWithNoVariantsDeclaringExit_When_TheKeyIsRemoved_Then_ItPlaysItsClassicExit()
        {
            // Arrange
            using var mounted = MountSettled("classic", "a");
            using var keys = s_keyStore;

            // Act
            keys.Set(string.Empty);
            Drain(mounted);
            Frames(2);

            // Assert — held, with the transition's exit class swapped in.
            Assert.That((HostChildCount, HasClass("item", "anim-fade-exit-to")), Is.EqualTo((1, true)));
        }

        [Test]
        public void Given_APortalInsideTheChild_When_TheKeyIsRemoved_Then_ItsMotionExitsAndTheNextPortalsDoesNot()
        {
            // Arrange — a second portal into the same target holds a Motion outside the presence.
            s_portalTarget = new VisualElement { name = "target" };
            Root.Add(s_portalTarget);
            using var mounted = MountSettled("portal", "a");
            using var keys = s_keyStore;

            // Act
            keys.Set(string.Empty);
            Drain(mounted);
            Frames(2);

            // Assert — the portalled Motion swapped to its exit pose; the one in the next slot kept its own.
            Assert.That((HasClass("item", "opacity-0"), HasClass("other", "opacity-0")), Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): no onExitComplete follows the whole presence leaving the tree.
        [Test]
        public void Given_AComponentChildExiting_When_TheWholePresenceUnmounts_Then_NoExitCompletionFollows()
        {
            // Arrange
            s_wrapper = "component";
            using var keys = new KeySetStore("a");
            s_keyStore = keys;
            using var show = new FlagStore();
            s_showStore = show;
            using var mounted = V.Mount(Root, V.Component(ShowablePresenceHost, key: "root"));
            Frames(40);
            keys.Set(string.Empty);
            Drain(mounted);
            var completedBefore = s_exitsCompleted;

            // Act — the presence leaves mid-exit, taking the exiting Motion with it.
            show.Set(false);
            Drain(mounted);
            Frames(3);
            Drain(mounted);

            // Assert
            Assert.That(s_exitsCompleted, Is.EqualTo(completedBefore));
        }

        [Test]
        public void Given_AnExitingMotionUnmounted_When_TheKeyReturnsInTheSameFrame_Then_NoExitCompletionFollows()
        {
            // Arrange
            using var flag = new FlagStore();
            s_flagStore = flag;
            using var mounted = MountSettled("toggled", "a");
            using var keys = s_keyStore;
            keys.Set(string.Empty);
            Drain(mounted);
            flag.Set(false);
            Drain(mounted);

            // Act — back before a frame passes, then several frames on.
            keys.Set("a");
            Drain(mounted);
            Frames(3);
            Drain(mounted);

            // Assert — the key stayed, so its removal never completed.
            Assert.That((HostChildCount, s_exitsCompleted), Is.EqualTo((1, 0)));
        }

        // GREEN_ON_BASE(characterization): a child left with no Motion to exit was removed at once on the base too.
        [Test]
        public void Given_AChildsOnlyMotionReplacedInTheRenderRemovingIt_When_ThatRenderDrains_Then_TheChildIsRemoved()
        {
            // Arrange
            using var flag = new FlagStore();
            s_flagStore = flag;
            using var mounted = MountSettled("toggled", "a");
            using var keys = s_keyStore;

            // Act — the removal and the Motion's replacement land in one drain.
            keys.Set(string.Empty);
            flag.Set(false);
            Drain(mounted);
            Drain(mounted);

            // Assert — nothing plays, so the removal completes.
            Assert.That((HostChildCount, s_exitsCompleted), Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_AMotionInsideAnElementEntering_When_TheKeyIsRemovedBeforeTheEnterSwaps_Then_ItExitsFromItsRestingPose()
        {
            // Arrange — added after the first render, so its mount enter plays and strips it to its initial pose.
            // Inside an element, the Motion is not the ghost's first element, which the removal cancels anyway.
            using var mounted = MountSettled("element", string.Empty);
            using var keys = s_keyStore;
            keys.Set("a");
            Drain(mounted);

            // Act
            keys.Set(string.Empty);
            Drain(mounted);

            // Assert — the enter was cancelled back to the resting pose the exit starts from.
            Assert.That((HasClass("item", "opacity-0"), HasClass("item", "opacity-100")), Is.EqualTo((false, true)));
        }

        // GREEN_ON_BASE(characterization): a Motion behind a component added after the first render played its enter.
        [Test]
        public void Given_InitialFalseOnThePresence_When_AMotionBehindAComponentIsAddedLater_Then_ItsEnterPlays()
        {
            // Arrange
            using var mounted = MountSettled("component", string.Empty);
            using var keys = s_keyStore;

            // Act
            keys.Set("a");
            Drain(mounted);

            // Assert
            Assert.That(ItemDurationMs(), Is.EqualTo(300f));
        }

        // GREEN_ON_BASE(characterization): a presence left at initial: true never held back a descendant's enter.
        [Test]
        public void Given_InitialTrueOnThePresence_When_AMotionBehindAComponentMountsWithIt_Then_ItsEnterPlays()
        {
            // Arrange
            s_wrapper = "component";
            s_presenceInitial = true;
            using var keys = new KeySetStore("a");
            s_keyStore = keys;

            // Act
            using var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));

            // Assert
            Assert.That(ItemDurationMs(), Is.EqualTo(300f));
        }

        // GREEN_ON_BASE(characterization): an inner presence's child never held the outer removal.
        [Test]
        public void Given_AKeyedChildWhoseTopIsAnInnerAnimatePresence_When_TheOuterKeyIsRemoved_Then_TheInnerChildDoesNotHoldIt()
        {
            // Arrange
            using var mounted = MountSettled("inner-presence-top", "a");
            using var keys = s_keyStore;

            // Act
            keys.Set(string.Empty);
            Drain(mounted);

            // Assert — the outer child leaves at once.
            Assert.That(HostChildCount, Is.EqualTo(0));
        }
    }
}
