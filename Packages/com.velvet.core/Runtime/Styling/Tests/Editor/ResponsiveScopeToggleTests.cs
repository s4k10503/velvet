using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A re-render that adds or removes <c>@container</c> on an ancestor re-points the responsive variants of
    /// descendants that are already attached, the way a CSS container query re-evaluates when an ancestor
    /// gains or loses <c>container-type</c>. The toggled scope sits inside a wider marked one — wider than md
    /// where the toggled one is narrower — so which of the two a leaf reads is visible in whether its
    /// <c>md:</c> payload is on. The outer scope stands in for the panel, so no case depends on the size
    /// the host window is given.
    /// </summary>
    [TestFixture]
    internal sealed class ResponsiveScopeToggleTests : PanelTestBase
    {
        private const string OuterScope = "@container w-[900px] shrink-[0]";
        private const string NarrowScope = "w-[500px] shrink-[0]";
        private const string MarkedNarrowScope = "@container w-[500px] shrink-[0]";

        private readonly record struct ScopeState(string ScopeClass, string LeafClass);

        private sealed class ScopeStore : Store<ScopeState>
        {
            public ScopeStore(string scopeClass, string leafClass) : base(new ScopeState(scopeClass, leafClass)) { }
            public void SetScope(string scopeClass) => SetState(s => s with { ScopeClass = scopeClass });
            protected override void ResetCore() => SetState(_ => new ScopeState(string.Empty, string.Empty));
        }

        private static ScopeStore s_store;

        [Component]
        private static VNode Scope()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Div(OuterScope,
                V.Div(state.ScopeClass, V.Label(name: "leaf", className: state.LeafClass, text: "x")));
        }

        [TearDown]
        public void ResetTheme() => VelvetTheme.IsDark = false;

        // Mounts the scope, settles layout, and reports whether the leaf's payload is on before and after the
        // scope's className changes to next. Nothing is laid out or dispatched after the change, since the
        // scope's box does not move: the re-point itself has to re-evaluate the breakpoints.
        private (bool Before, bool After) PayloadAcrossToggle(string initialScope, string next, string leafClass)
        {
            using var store = new ScopeStore(initialScope, leafClass);
            s_store = store;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(Scope, key: "scope"));
            var leaf = _window.rootVisualElement.Q<Label>("leaf");
            Settle(leaf);
            var before = leaf.ClassListContains("bg-wide");

            store.SetScope(next);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            return (before, leaf.ClassListContains("bg-wide"));
        }

        // The width source the leaf is bound to gets a geometry event, so the leaf reads it.
        private void Settle(VisualElement leaf)
        {
            ForcePanelUpdate(leaf.panel);
            var source = leaf.parent.ClassListContains(VelvetResponsive.ContainerClass)
                ? leaf.parent
                : leaf.parent.parent;
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            source.SimulateEvent(evt);
        }

        [Test]
        public void Given_AnAttachedMdLeaf_When_ItsParentGainsTheMarker_Then_TheLeafReadsTheParentWidth()
        {
            // Act — on before the toggle is the 900px scope answering; off after it is the 500px one.
            var payload = PayloadAcrossToggle(NarrowScope, MarkedNarrowScope, "md:bg-wide");

            // Assert
            Assert.That(payload, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnAttachedMdLeaf_When_ItsParentLosesTheMarker_Then_TheLeafReadsTheOuterScopeWidth()
        {
            // Act
            var payload = PayloadAcrossToggle(MarkedNarrowScope, NarrowScope, "md:bg-wide");

            // Assert
            Assert.That(payload, Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AnAttachedStackedDarkMdLeaf_When_ItsParentGainsTheMarker_Then_TheLeafReadsTheParentWidth()
        {
            // Arrange — dark on, so the md: inner of the stack is the only gate left to decide.
            VelvetTheme.IsDark = true;

            // Act
            var payload = PayloadAcrossToggle(NarrowScope, MarkedNarrowScope, "dark:md:bg-wide");

            // Assert
            Assert.That(payload, Is.EqualTo((true, false)));
        }

        // A subscription left behind would hold the manipulator, and through it the element, for the life of
        // the domain. The event's backing field is read by name so that this file builds on a tree that lacks it.

        private static int ScopeSubscribers()
            => (typeof(StyleResponsiveScope)
                .GetField("ScopesChanged", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;

        private (bool Subscribed, bool Released) SubscriptionAcrossUnmount(string leafClass)
        {
            var before = ScopeSubscribers();
            _mounted = V.Mount(_window.rootVisualElement, V.Div(NarrowScope, V.Label(className: leafClass, text: "x")));
            var subscribed = ScopeSubscribers() > before;
            _mounted.Dispose();
            _mounted = null;
            return (subscribed, ScopeSubscribers() == before);
        }

        [Test]
        public void Given_AMountedMdLeaf_When_Unmounted_Then_ItsScopeSubscriptionIsReleased()
        {
            // Act
            var subscription = SubscriptionAcrossUnmount("md:bg-wide");

            // Assert
            Assert.That(subscription, Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AMountedStackedDarkMdLeaf_When_Unmounted_Then_ItsScopeSubscriptionIsReleased()
        {
            // Act
            var subscription = SubscriptionAcrossUnmount("dark:md:bg-wide");

            // Assert
            Assert.That(subscription, Is.EqualTo((true, true)));
        }
    }
}
