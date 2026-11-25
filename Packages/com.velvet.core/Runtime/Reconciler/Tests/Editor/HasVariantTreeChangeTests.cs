using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a <c>has-[…]:</c> element against changes to its subtree that its own post-children pass does not
    /// see: a relocated (<c>z-*</c>) descendant, which joins its parent only once its pass ends, and whose element
    /// a removal retires until then; a Portal's children leaving the target the element holds; and a Portal from
    /// another root, which registered none of the element's rules. Each holds once the change has rendered, as
    /// <c>:has()</c> matches the tree as it stands after any change.
    /// </summary>
    [TestFixture]
    internal sealed class HasVariantTreeChangeTests
    {
        private VisualElement _root;
        private static StateUpdater<int> s_setStep;
        private static StateUpdater<bool> s_setShown;
        private static VisualElement s_target;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            (s_setStep, s_setShown, s_target) = (default, default, null);
        }

        private VisualElement Parent => _root.Q<VisualElement>("parent");

        // The relocated child goes at step 2.
        [Component(Compiler = false)]
        private static VNode RelocatedChildHost()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(name: "wrap", children: new VNode[]
            {
                V.Div(name: "parent", className: "has-[.on]:bg-mark", children: step < 2
                    ? new VNode[] { V.Div(name: "z", className: "absolute z-10 on") }
                    : Array.Empty<VNode>()),
            });
        }

        [Component(Compiler = false)]
        private static VNode PortalHost()
            => V.Div(name: "host", children: new VNode[]
            {
                V.Div(name: "holder", className: "has-[.on]:bg-mark", children: new VNode[] { V.Div(name: "target") }),
                V.Component(PortalOwner, key: "owner"),
            });

        [Component(Compiler = false)]
        private static VNode PortalOwner()
        {
            var (shown, setShown) = Hooks.UseState(false);
            s_setShown = setShown;
            return shown ? V.Portal(s_target, new VNode[] { V.Div(className: "on") }) : V.Fragment();
        }

        private static VNode HolderTree()
            => V.Div(name: "holder", className: "has-[.on]:bg-mark", children: new VNode[] { V.Div(name: "target") });

        [Test]
        public void Given_AHasClassElementAboveARelocatedChildCarryingTheClass_When_Mounted_Then_ItMatches()
        {
            // Act
            using var mounted = V.Mount(_root, V.Div(name: "parent", className: "has-[.on]:bg-mark", children: new VNode[]
            {
                V.Div(name: "z", className: "absolute z-10 on"),
            }));

            // Assert
            Assert.That(Parent.ClassListContains("bg-mark"), Is.True);
        }

        [Test]
        public void Given_AHasCheckedElementAboveARelocatedChildHoldingACheckedToggle_When_Mounted_Then_ItMatches()
        {
            // Act
            using var mounted = V.Mount(_root, V.Div(name: "parent", className: "has-[:checked]:bg-mark", children: new VNode[]
            {
                V.Div(name: "z", className: "absolute z-10", children: new VNode[] { V.Toggle(name: "leaf", value: true) }),
            }));

            // Assert
            Assert.That(Parent.ClassListContains("bg-mark"), Is.True);
        }

        [Test]
        public void Given_AHasClassElementMatchingAPlacedRelocatedChild_When_ARenderOfTheElementRemovesTheChild_Then_ItStopsMatching()
        {
            // Arrange — a render of "parent" with the child placed, whose own post-children pass reads it there.
            using var mounted = V.Mount(_root, V.Component(RelocatedChildHost, key: "host"));
            s_setStep.Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var matchedWhilePlaced = Parent.ClassListContains("bg-mark");

            // Act — "parent" renders again, without the child.
            s_setStep.Invoke(2);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — null where the element did not match the placed child.
            Assert.That(matchedWhilePlaced ? Parent.ClassListContains("bg-mark") : (bool?)null, Is.False);
        }

        [Test]
        public void Given_AHasClassElementAboveAPortalTarget_When_ThePortalHoldingAChildCarryingTheClassUnmounts_Then_ItStopsMatching()
        {
            // Arrange — "owner" renders beside "holder", so neither the walk of its render nor the chain above it
            // reaches "holder"; its Portal mounts a child carrying the class into "target".
            using var mounted = V.Mount(_root, V.Component(PortalHost, key: "root"));
            s_target = _root.Q<VisualElement>("target");
            s_setShown.Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var holder = _root.Q<VisualElement>("holder");
            var matchedWhileMounted = holder.ClassListContains("bg-mark");

            // Act
            s_setShown.Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — null where the element did not match the Portal's child.
            Assert.That(matchedWhileMounted ? holder.ClassListContains("bg-mark") : (bool?)null, Is.False);
        }

        [Test]
        public void Given_AHasClassElementInOneRoot_When_ARenderOfAnotherRootPortalsAChildCarryingTheClassUnderIt_Then_ItMatches()
        {
            // Arrange
            var holderRoot = new VisualElement();
            using var holding = V.Mount(holderRoot, HolderTree());
            s_target = holderRoot.Q<VisualElement>("target");
            using var portalling = V.Mount(_root, V.Div(children: new VNode[] { V.Component(PortalOwner, key: "owner") }));

            // Act
            s_setShown.Invoke(true);
            portalling.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(holderRoot.Q<VisualElement>("holder").ClassListContains("bg-mark"), Is.True);
        }

        [Test]
        public void Given_AHasClassElementInOneRoot_When_AnotherRootMountsWithAPortalOfAChildCarryingTheClassUnderIt_Then_ItMatches()
        {
            // Arrange
            var holderRoot = new VisualElement();
            using var holding = V.Mount(holderRoot, HolderTree());
            var target = holderRoot.Q<VisualElement>("target");

            // Act
            using var portalling = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Portal(target, new VNode[] { V.Div(className: "on") }),
            }));

            // Assert
            Assert.That(holderRoot.Q<VisualElement>("holder").ClassListContains("bg-mark"), Is.True);
        }
    }
}
