using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the mount enter of a Motion that inherits its labels — Framer Motion's variant child, which mounts
    /// at the initial label its variant parent declares and animates to the parent's animate label — and the
    /// orchestration a mounting parent's transition applies to those enters: each variant parent numbers its own
    /// variant children, and a child mounting under an already-mounted parent enters from the inherited initial
    /// on its own. Enters are read off the class list: a variant enter strips the resting pose and applies the
    /// initial one synchronously, and swaps back once its slot elapses on the simulated panel.
    /// </summary>
    [TestFixture]
    internal sealed class MotionVariantChildEnterTests : MotionSimulatedPanelTestsBase
    {
        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        // Names no "hidden" pose, so an inherited "hidden" initial resolves nothing on it.
        private static readonly Dictionary<string, MotionVariant> s_fromStart = new()
        {
            ["start"] = "opacity-50",
            ["visible"] = "opacity-100",
        };

        private static readonly StyleTransitionConfig s_child = new() { DurationSec = 0.05f };

        private static StateUpdater<bool> s_setShow;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_setShow = default;
        }

        private static VNode Child(string name, VNode[] children = null)
            => V.Motion(key: name, name: name, variants: s_fade, transition: s_child, children: children);

        private static VNode Parent(StyleTransitionConfig transition, VNode[] children)
            => V.Motion(key: "p", name: "p", variants: s_fade, initial: "hidden", animate: "visible",
                transition: transition, children: children);

        private bool EnteredSwapped(string name) => Root.Q<VisualElement>(name).ClassListContains("opacity-100");

        private void Frames(int count)
        {
            for (var i = 0; i < count; i++) Tick();
        }

        [Test]
        public void Given_AParentWithInitialAndAnimate_When_AnInheritingChildMountsWithIt_Then_TheChildStartsAtItsInitialPose()
        {
            // Act
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new[]
            {
                Parent(new StyleTransitionConfig { DurationSec = 0.1f }, new[] { Child("c") }),
            });

            // Assert — the child's own variants[hidden], the label its parent enters from.
            Assert.That(Root.Q<VisualElement>("c").ClassListContains("opacity-0"), Is.True);
        }

        [Test]
        public void Given_AParentEnterStaggeringItsChildren_When_TwoInheritingChildrenMountWithIt_Then_EachEntersOnItsOwnSlot()
        {
            // Arrange
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new[]
            {
                Parent(new StyleTransitionConfig { DurationSec = 0.1f, StaggerChildrenSec = 0.3f },
                    new[] { Child("c0"), Child("c1") }),
            });

            // Act — past the first slot, short of the second's 300ms.
            Frames(6);

            // Assert — c0 has swapped to its resting pose; c1 is still held at its initial one.
            Assert.That((EnteredSwapped("c0"), EnteredSwapped("c1")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnInheritingChildWithItsOwnChild_When_TheParentsEnterStaggers_Then_TheGrandchildEntersWithItAndTheNextChildKeepsItsSlot()
        {
            // Arrange — Framer numbers each variant parent's own children: a1 belongs to a, so b is the parent's
            // second child and takes the 300ms slot, while a1 starts with a in slot 0.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new[]
            {
                Parent(new StyleTransitionConfig { DurationSec = 0.1f, StaggerChildrenSec = 0.3f },
                    new[] { Child("a", new[] { Child("a1") }), Child("b") }),
            });

            // Act
            Frames(6);

            // Assert
            Assert.That((EnteredSwapped("a1"), EnteredSwapped("b")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AChildEnteringFromItsOwnInitial_When_ASiblingMountsAfterIt_Then_TheSiblingEntersFromTheInheritedInitial()
        {
            // Arrange — a names its own initial and hands it to a1; b, after it, inherits the parent's.
            var tree = new[]
            {
                Parent(new StyleTransitionConfig { DurationSec = 0.1f }, new[]
                {
                    V.Motion(key: "a", name: "a", variants: s_fromStart, initial: "start", transition: s_child,
                        children: new[] { Child("a1") }),
                    Child("b"),
                }),
            };

            // Act
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(Root.Q<VisualElement>("b").ClassListContains("opacity-0"), Is.True);
        }

        [Test]
        public void Given_AMountedParent_When_AnInheritingChildIsAddedAfterAChildWithItsOwnInitial_Then_TheAddedChildEntersFromTheParentsInitial()
        {
            // Arrange — Framer animates a variant child that mounts under an already-mounted parent from the
            // initial label it inherits.
            VNode[] Tree(bool withB) => new[]
            {
                Parent(new StyleTransitionConfig { DurationSec = 0.1f }, new[]
                {
                    V.Motion(key: "a", name: "a", variants: s_fromStart, initial: "start", transition: s_child,
                        children: new[] { Child("a1") }),
                    withB ? Child("b") : null,
                }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree(false));
            AdvancePast(0.2f);

            // Act
            _reconciler.Reconcile(Root, Tree(false), Tree(true));

            // Assert
            Assert.That(Root.Q<VisualElement>("b").ClassListContains("opacity-0"), Is.True);
        }

        [Component]
        private static VNode AddingConsumer()
        {
            var (show, setShow) = Hooks.UseState(false);
            s_setShow = setShow;
            return V.Div(name: "slot", children: show ? new[] { Child("m") } : Array.Empty<VNode>());
        }

        // q sits before the consumer and names its own initial, so the spine walks past a Motion that does not
        // enclose the consumer.
        [Component]
        private static VNode AddingHost() => Parent(new StyleTransitionConfig { DurationSec = 0.1f }, new VNode[]
        {
            V.Motion(key: "q", name: "q", variants: s_fromStart, initial: "start", transition: s_child),
            V.Component(AddingConsumer, key: "consumer"),
        });

        [Test]
        public void Given_AComponentUnderAParentWithInitial_When_ItReRendersAloneAndMountsAnInheritingMotion_Then_TheMotionEntersFromTheParentsInitial()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(AddingHost, key: "host"));
            AdvancePast(0.2f);

            // Act — the consumer's own state change re-renders it in isolation.
            s_setShow.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Root.Q<VisualElement>("m").ClassListContains("opacity-0"), Is.True);
        }

        // The Motion before the consumer is the first context the spine pushes, and it encloses nothing.
        [Component]
        private static VNode SideBySideHost() => V.Div(children: new VNode[]
        {
            V.Motion(key: "q", name: "q", variants: s_fade, animate: "visible", transition: s_child),
            V.Component(AddingConsumer, key: "consumer"),
        });

        // GREEN_ON_BASE(characterization): the spine already walked past a Motion beside the component it
        // rebuilt the context for.
        [Test]
        public void Given_AComponentBesideAMotion_When_ItReRendersAloneAndMountsAMotion_Then_TheMotionMounts()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(SideBySideHost, key: "host"));

            // Act
            s_setShow.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Root.Q<VisualElement>("m"), Is.Not.Null);
        }

        [Test]
        public void Given_APresenceChildStaggeringItsChildren_When_ItEntersWithThePresence_Then_ItsInheritingChildrenEnterOnTheirSlots()
        {
            // Arrange — the presence plays the anchor's own enter; the anchor's children are its variant
            // children as they are anywhere else.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.AnimatePresence(key: "presence", children: new[]
                {
                    Parent(new StyleTransitionConfig { DurationSec = 0.1f, StaggerChildrenSec = 0.3f },
                        new[] { Child("c0"), Child("c1") }),
                }),
            });

            // Act
            Frames(6);

            // Assert
            Assert.That((EnteredSwapped("c0"), EnteredSwapped("c1")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APresenceStaggeringItsChildren_When_TheSecondEntersWithAnInheritingChild_Then_ThatChildEntersInItsParentsSlot()
        {
            // Arrange — the presence plays b's enter in its 300ms slot, and b's child starts with it.
            MotionNode Keyed(string key) => V.Motion(key: key, name: key, variants: s_fade, initial: "hidden",
                animate: "visible", transition: new StyleTransitionConfig { DurationSec = 0.1f },
                children: new[] { Child(key + "-child") });
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.AnimatePresence(key: "presence", staggerSec: 0.3f, children: new VNode[] { Keyed("a"), Keyed("b") }),
            });

            // Act
            Frames(6);

            // Assert
            Assert.That((EnteredSwapped("a-child"), EnteredSwapped("b-child")), Is.EqualTo((true, false)));
        }
    }
}
