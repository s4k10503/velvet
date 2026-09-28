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
            // Arrange — a controls its own labels and hands its initial to a1; b, after it, inherits the parent's.
            var tree = new[]
            {
                Parent(new StyleTransitionConfig { DurationSec = 0.1f }, new[]
                {
                    V.Motion(key: "a", name: "a", variants: s_fromStart, initial: "start", animate: "visible", transition: s_child,
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
                    V.Motion(key: "a", name: "a", variants: s_fromStart, initial: "start", animate: "visible", transition: s_child,
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

        // q sits before the consumer and names labels of its own, so the spine walks past a Motion that does not
        // enclose the consumer.
        [Component]
        private static VNode AddingHost() => Parent(new StyleTransitionConfig { DurationSec = 0.1f }, new VNode[]
        {
            V.Motion(key: "q", name: "q", variants: s_fromStart, initial: "start", animate: "visible", transition: s_child),
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

        // GREEN_ON_BASE(characterization): a Motion's own label already stayed with its own children.
        [Test]
        public void Given_AChildDrivingItsChildrenWithItsOwnLabel_When_ASiblingMountsAfterIt_Then_TheSiblingTakesTheParentsLabel()
        {
            // Act
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new[]
            {
                V.Motion(key: "p", name: "p", animate: "visible", children: new[]
                {
                    V.Motion(key: "a", name: "a", animate: "hidden", children: new[] { Child("a1") }),
                    Child("b"),
                }),
            });

            // Assert
            Assert.That(Root.Q<VisualElement>("b").ClassListContains("opacity-100"), Is.True);
        }

        [Test]
        public void Given_AChildNamingItsOwnExit_When_TheParentsEnterStaggers_Then_ItTakesNoSlotFromTheNextChild()
        {
            // Arrange — Framer's isControllingVariants counts an own exit label, so own-exit is no variant child.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new[]
            {
                Parent(new StyleTransitionConfig { DurationSec = 0.1f, StaggerChildrenSec = 0.3f }, new[]
                {
                    V.Motion(key: "own-exit", name: "own-exit", variants: s_fade, exit: "hidden", transition: s_child),
                    Child("c1"),
                }),
            });
            var entered = Root.Q<VisualElement>("c1").ClassListContains("opacity-0");

            // Act
            Frames(6);

            // Assert — c1 entered, in slot 0.
            Assert.That((entered, EnteredSwapped("c1")), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AChildNamingItsOwnExit_When_ItsParentMounts_Then_ItTakesNoPoseFromTheParentsLabel()
        {
            // Act — a Motion naming a label of its own inherits none, as Framer's controlling variant nodes do.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new[]
            {
                V.Motion(key: "p", name: "p", animate: "visible", children: new VNode[]
                {
                    V.Motion(key: "own-exit", name: "own-exit", variants: s_fade, exit: "hidden", transition: s_child),
                }),
            });

            // Assert
            Assert.That(Root.Q<VisualElement>("own-exit").ClassListContains("opacity-100"), Is.False);
        }

        [Test]
        public void Given_AStaggeringParentAroundAPresence_When_ItsInheritingChildrenMountWithIt_Then_EachEntersOnItsOwnSlot()
        {
            // Arrange — Framer's staggered list: the presence's children are the parent's variant children.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new[]
            {
                Parent(new StyleTransitionConfig { DurationSec = 0.1f, StaggerChildrenSec = 0.3f }, new VNode[]
                {
                    V.AnimatePresence(key: "presence", children: new[] { Child("a"), Child("b") }),
                }),
            });
            var entered = Root.Q<VisualElement>("b").ClassListContains("opacity-0");

            // Act
            Frames(6);

            // Assert — b entered, and is still held in its 300ms slot while a has swapped.
            Assert.That((entered, EnteredSwapped("a"), EnteredSwapped("b")), Is.EqualTo((true, true, false)));
        }

        [Component]
        private static VNode Row(string id) => V.Motion(key: id, name: id, variants: s_fade, transition: s_child);

        private static VNode RowList() => V.VirtualList(new List<string> { "r0", "r1" }, id => id, itemHeight: 30f,
            renderer: id => V.Component(Row, id, key: id), className: "h-[200px]");

        [Component]
        private static VNode StaggeredListHost() => Parent(
            new StyleTransitionConfig { DurationSec = 0.1f, StaggerChildrenSec = 0.3f }, new[] { RowList() });

        // GREEN_ON_BASE(characterization): the base plays no enter for a row, so it staggers none.
        [Test]
        public void Given_AVirtualListUnderAnEnteringParent_When_ItsRowsMountAfterTheParent_Then_NoRowIsStaggered()
        {
            // Arrange — the rows render once the list is laid out, after the pass that mounted the parent, so
            // Framer would animate each on its own.
            using var mounted = V.Mount(Root, V.Component(StaggeredListHost, key: "host"));

            // Act
            Frames(8);

            // Assert
            Assert.That(EnteredSwapped("r1"), Is.True);
        }

        [Component]
        private static VNode LabelledListHost() => V.Motion(key: "p", name: "p", animate: "visible",
            children: new[] { RowList() });

        // GREEN_ON_BASE(characterization): every row of one range already inherited the label above the list.
        [Test]
        public void Given_AVirtualListUnderALabelledMotion_When_ItsRowsRenderInOneRange_Then_TheSecondRowTakesTheLabel()
        {
            // Arrange — each row component renders alone inside the list's item scope, reconstructing its context
            // over the one that scope pushed.
            using var mounted = V.Mount(Root, V.Component(LabelledListHost, key: "host"));

            // Act
            AdvancePast(0.2f);

            // Assert
            Assert.That(EnteredSwapped("r1"), Is.True);
        }

        [Test]
        public void Given_APresenceChildMountedUnderInitialFalse_When_ALaterRenderAddsAMotionInsideIt_Then_ThatMotionDoesNotEnter()
        {
            // Arrange — Framer's PresenceChild keeps the initial: false a first-render child was created with.
            VNode[] Tree(bool withInner) => new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false, children: new VNode[]
                {
                    V.Div(key: "a", children: new[]
                    {
                        withInner
                            ? V.Motion(key: "inner", name: "inner", variants: s_fade, initial: "hidden",
                                animate: "visible", transition: s_child)
                            : null,
                    }),
                }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree(false));

            // Act
            _reconciler.Reconcile(Root, Tree(false), Tree(true));

            // Assert — at rest from the start rather than at its initial pose.
            Assert.That(Root.Q<VisualElement>("inner").ClassListContains("opacity-100"), Is.True);
        }

        [Test]
        public void Given_APresenceChildMountedUnderInitialFalse_When_APortalInsideItMountsAMotion_Then_ThatMotionDoesNotEnter()
        {
            // Arrange — the portal's children mount after the presence's first render has returned.
            var target = new VisualElement();
            Root.Add(target);

            // Act
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false, children: new VNode[]
                {
                    V.Div(key: "a", children: new VNode[]
                    {
                        V.Portal(target, new VNode[]
                        {
                            V.Motion(key: "pm", name: "pm", variants: s_fade, initial: "hidden", animate: "visible",
                                transition: s_child),
                        }),
                    }),
                }),
            });

            // Assert
            Assert.That(target.Q<VisualElement>("pm").ClassListContains("opacity-100"), Is.True);
        }

        // GREEN_ON_BASE(characterization): a presence's initial: false already left alone a Motion mounting
        // outside it.
        [Test]
        public void Given_APresenceUnderInitialFalse_When_AMotionAfterItMountsOutsideIt_Then_ThatMotionEnters()
        {
            // Act
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false, children: new VNode[] { V.Div(key: "a") }),
                V.Motion(key: "after", name: "after", variants: s_fade, initial: "hidden", animate: "visible",
                    transition: s_child),
            });

            // Assert
            Assert.That(Root.Q<VisualElement>("after").ClassListContains("opacity-0"), Is.True);
        }

        // GREEN_ON_BASE(characterization): a key coming back after its removal already entered.
        [Test]
        public void Given_AFirstRenderChildUnderInitialFalse_When_ItIsRemovedAndComesBackWithAMotion_Then_ThatMotionEnters()
        {
            // Arrange — Framer creates the child that comes back with no initial: false.
            VNode[] Tree(bool present, bool withInner) => new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false, children: new VNode[]
                {
                    present
                        ? V.Div(key: "a", children: new[]
                        {
                            withInner
                                ? V.Motion(key: "inner", name: "inner", variants: s_fade, initial: "hidden",
                                    animate: "visible", transition: s_child)
                                : null,
                        })
                        : null,
                }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree(true, false));
            _reconciler.Reconcile(Root, Tree(true, false), Tree(false, false));

            // Act
            _reconciler.Reconcile(Root, Tree(false, false), Tree(true, true));

            // Assert
            Assert.That(Root.Q<VisualElement>("inner").ClassListContains("opacity-0"), Is.True);
        }

        [Component]
        private static VNode BlockedConsumer()
        {
            var (show, setShow) = Hooks.UseState(false);
            s_setShow = setShow;
            return V.Div(name: "slot", children: show
                ? new[]
                {
                    V.Motion(key: "m", name: "m", variants: s_fade, initial: "hidden", animate: "visible",
                        transition: s_child),
                }
                : Array.Empty<VNode>());
        }

        [Component]
        private static VNode BlockedPresenceHost() => V.AnimatePresence(key: "presence", initial: false,
            children: new[] { V.Component(BlockedConsumer, key: "a") });

        [Test]
        public void Given_AComponentInAPresenceChildMountedUnderInitialFalse_When_ItReRendersAloneAndMountsAMotion_Then_ThatMotionDoesNotEnter()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(BlockedPresenceHost, key: "host"));

            // Act
            s_setShow.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Root.Q<VisualElement>("m").ClassListContains("opacity-100"), Is.True);
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
