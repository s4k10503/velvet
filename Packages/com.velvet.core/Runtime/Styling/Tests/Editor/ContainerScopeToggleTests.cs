using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A re-render that adds or removes <c>@container</c> on an ancestor, renames the container it names, or
    /// rewrites the <c>className</c> of a leaf that is already attached, re-points or re-reads the container
    /// variants without any geometry event after it. The toggled container sits inside a wider one — wider than
    /// <c>@md</c> where the toggled one is narrower — so which of the two a leaf reads is visible in whether its
    /// <c>@md:</c> payload is on. The leaf's callbacks and its container's are counted where a registration that
    /// outlived its manipulator would show.
    /// </summary>
    [TestFixture]
    internal sealed class ContainerScopeToggleTests : PanelTestBase
    {
        private const string OuterScope = "@container w-[900px] shrink-[0]";
        private const string NamedOuterScope = "@container/a w-[900px] shrink-[0]";
        private const string MainOuterScope = "@container/main w-[900px] shrink-[0]";
        private const string PlainNarrow = "w-[300px] shrink-[0]";
        private const string MarkedNarrow = "@container w-[300px] shrink-[0]";
        private const string OtherNamedNarrow = "@container/b w-[300px] shrink-[0]";
        private const string NamedNarrow = "@container/a w-[300px] shrink-[0]";

        private readonly record struct ScopeState(string OuterClass, string ScopeClass, string LeafClass);

        private sealed class ScopeStore : Store<ScopeState>
        {
            public ScopeStore(string outerClass, string scopeClass, string leafClass)
                : base(new ScopeState(outerClass, scopeClass, leafClass)) { }
            public void SetScope(string scopeClass) => SetState(s => s with { ScopeClass = scopeClass });
            public void SetLeaf(string leafClass) => SetState(s => s with { LeafClass = leafClass });
            protected override void ResetCore() => SetState(_ => new ScopeState(string.Empty, string.Empty, string.Empty));
        }

        private static ScopeStore s_store;

        [Component]
        private static VNode Scope()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Div(state.OuterClass,
                V.Div(state.ScopeClass, V.Label(name: "leaf", className: state.LeafClass, text: "x")));
        }

        [TearDown]
        public void ResetTheme() => VelvetTheme.IsDark = false;

        private static readonly FieldInfo CallbackRegistry =
            typeof(CallbackEventHandler).GetField("m_CallbackRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Mounts the scope and settles layout, so the leaf has read the container it is bound to.
        private Label MountScope(ScopeStore store, string sourceClass)
        {
            s_store = store;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(Scope, key: "scope"));
            var leaf = _window.rootVisualElement.Q<Label>("leaf");
            Settle(leaf, sourceClass);
            return leaf;
        }

        private void Drain() => _mounted.GetSchedulerForTest().DrainImmediateForTest();

        // The container the leaf is bound to gets a geometry event, so the leaf reads it. A leaf bound to none has
        // nothing registered on its container, which the simulator refuses.
        private static void Settle(VisualElement leaf, string sourceClass)
        {
            ForcePanelUpdate(leaf.panel);
            var source = leaf.parent;
            while (!source.ClassListContains(sourceClass))
            {
                source = source.parent;
            }
            if (CallbackRegistry.GetValue(source) == null)
            {
                return;
            }
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            source.SimulateEvent(evt);
        }

        // Whether the leaf's payload is on before and after the inner scope's className changes to next. Nothing is
        // laid out or dispatched after the change, since the scope's box does not move: the re-point itself has to
        // re-evaluate the query.
        private (bool Before, bool After) PayloadAcrossToggle(
            string outerClass, string initialScope, string next, string leafClass, string sourceClass)
        {
            using var store = new ScopeStore(outerClass, initialScope, leafClass);
            var leaf = MountScope(store, sourceClass);
            var before = leaf.ClassListContains("bg-wide");

            store.SetScope(next);
            Drain();
            return (before, leaf.ClassListContains("bg-wide"));
        }

        [Test]
        public void Given_AnAttachedMdLeaf_When_ItsParentGainsTheMarker_Then_TheLeafReadsTheParentWidth()
        {
            // Act — on before the toggle is the 900px container answering; off after it is the 300px one.
            var payload = PayloadAcrossToggle(OuterScope, PlainNarrow, MarkedNarrow, "@md:bg-wide", "@container");

            // Assert
            Assert.That(payload, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnAttachedMdLeaf_When_ItsParentLosesTheMarker_Then_TheLeafReadsTheOuterContainerWidth()
        {
            // Act
            var payload = PayloadAcrossToggle(OuterScope, MarkedNarrow, PlainNarrow, "@md:bg-wide", "@container");

            // Assert
            Assert.That(payload, Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AnAttachedMdLeaf_When_ItsParentGainsTheMarkerImperativelyAndReconciles_Then_TheLeafReadsTheParentWidth()
        {
            // Arrange — the 900px outer container answers until the parent carries the marker.
            using var store = new ScopeStore(OuterScope, PlainNarrow, "@md:bg-wide");
            var leaf = MountScope(store, "@container");
            var before = leaf.ClassListContains("bg-wide");
            leaf.parent.AddToClassList("@container");

            // Act — a re-render that leaves the className's markers alone.
            store.SetScope(PlainNarrow + " p-1");
            Drain();

            // Assert
            Assert.That((before, leaf.ClassListContains("bg-wide")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnAttachedStackedDarkMdLeaf_When_ItsParentGainsTheMarker_Then_TheLeafReadsTheParentWidth()
        {
            // Arrange — dark on, so the @md: inner of the stack is the only gate left to decide.
            VelvetTheme.IsDark = true;

            // Act
            var payload = PayloadAcrossToggle(OuterScope, PlainNarrow, MarkedNarrow, "dark:@md:bg-wide", "@container");

            // Assert
            Assert.That(payload, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnAttachedNamedMdLeaf_When_ItsParentIsRenamedToTheNameItAsks_Then_TheLeafReadsTheParentWidth()
        {
            // Act — the 900px outer container is the one named a until the 300px parent takes the name.
            var payload = PayloadAcrossToggle(NamedOuterScope, OtherNamedNarrow, NamedNarrow, "@md/a:bg-wide",
                "@container/a");

            // Assert
            Assert.That(payload, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnAttachedLeafWithoutAContainerVariant_When_ItsClassNameGainsOne_Then_ItTakesEffectAtOnce()
        {
            // Arrange
            using var store = new ScopeStore(OuterScope, PlainNarrow, "p-2");
            var leaf = MountScope(store, "@container");
            var before = leaf.ClassListContains("bg-wide");

            // Act
            store.SetLeaf("@md:bg-wide");
            Drain();

            // Assert
            Assert.That((before, leaf.ClassListContains("bg-wide")), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AnAttachedMdLeaf_When_ItsClassNameLosesTheVariant_Then_ThePayloadGoes()
        {
            // Arrange
            using var store = new ScopeStore(OuterScope, PlainNarrow, "@md:bg-wide");
            var leaf = MountScope(store, "@container");
            var before = leaf.ClassListContains("bg-wide");

            // Act
            store.SetLeaf("p-2");
            Drain();

            // Assert
            Assert.That((before, leaf.ClassListContains("bg-wide")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnAttachedMdLeaf_When_ItsPayloadIsSwapped_Then_TheOldPayloadGoesAndTheNewOneComes()
        {
            // Arrange
            using var store = new ScopeStore(OuterScope, PlainNarrow, "@md:bg-old");
            var leaf = MountScope(store, "@container");
            var before = (leaf.ClassListContains("bg-old"), leaf.ClassListContains("bg-new"));

            // Act
            store.SetLeaf("@md:bg-new");
            Drain();

            // Assert
            Assert.That((before, (leaf.ClassListContains("bg-old"), leaf.ClassListContains("bg-new"))),
                Is.EqualTo(((true, false), (false, true))));
        }

        [Test]
        public void Given_AnAttachedMdLeaf_When_ItsClassNameGainsASecondQuery_Then_BothAreOn()
        {
            // Arrange — 900px is above @md and @lg.
            using var store = new ScopeStore(OuterScope, PlainNarrow, "@md:bg-old");
            var leaf = MountScope(store, "@container");

            // Act
            store.SetLeaf("@md:bg-old @lg:bg-new");
            Drain();

            // Assert
            Assert.That((leaf.ClassListContains("bg-old"), leaf.ClassListContains("bg-new")), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AnAttachedUnnamedMdLeaf_When_ItsVariantNamesTheContainer_Then_ItStaysOn()
        {
            // Arrange — the 900px outer container is the one named main, and the nearest of any name.
            using var store = new ScopeStore(MainOuterScope, PlainNarrow, "@md:bg-wide");
            var leaf = MountScope(store, "@container/main");
            var before = leaf.ClassListContains("bg-wide");

            // Act
            store.SetLeaf("@md/main:bg-wide");
            Drain();

            // Assert
            Assert.That((before, leaf.ClassListContains("bg-wide")), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AnAttachedNamedMdLeaf_When_ItsVariantDropsTheName_Then_TheContainerIsStillWatchedOnce()
        {
            // Arrange
            using var store = new ScopeStore(MainOuterScope, PlainNarrow, "@md/main:bg-wide");
            var leaf = MountScope(store, "@container/main");
            var container = leaf.parent.parent;
            var named = CallbackRegistryProbe.BubbleUpCallbackCount(container);

            // Act
            store.SetLeaf("@md:bg-wide");
            Drain();

            // Assert
            Assert.That((CallbackRegistryProbe.BubbleUpCallbackCount(container) - named, leaf.ClassListContains("bg-wide")),
                Is.EqualTo((0, true)));
        }

        [Test]
        public void Given_AContainerWithAnMdLeaf_When_TheLeafGainsAndLosesTheVariant_Then_TheContainerIsWatchedOnlyWhileItHolds()
        {
            // Arrange
            using var store = new ScopeStore(OuterScope, PlainNarrow, "p-2");
            var leaf = MountScope(store, "@container");
            var container = leaf.parent.parent;
            var baseline = CallbackRegistryProbe.BubbleUpCallbackCount(container);

            // Act
            store.SetLeaf("@md:bg-wide");
            Drain();
            var watched = CallbackRegistryProbe.BubbleUpCallbackCount(container);
            store.SetLeaf("p-2");
            Drain();

            // Assert
            Assert.That((watched - baseline, CallbackRegistryProbe.BubbleUpCallbackCount(container) - baseline),
                Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_AnAttachedLeaf_When_ItGainsAndLosesAContainerVariant_Then_ItsAttachAndDetachCallbacksComeAndGo()
        {
            // Arrange
            using var store = new ScopeStore(OuterScope, PlainNarrow, "p-2");
            var leaf = MountScope(store, "@container");
            var baseline = CallbackRegistryProbe.BubbleUpCallbackCount(leaf);

            // Act
            store.SetLeaf("@md:bg-wide");
            Drain();
            var held = CallbackRegistryProbe.BubbleUpCallbackCount(leaf);
            store.SetLeaf("p-2");
            Drain();

            // Assert
            Assert.That((held - baseline, CallbackRegistryProbe.BubbleUpCallbackCount(leaf) - baseline),
                Is.EqualTo((2, 0)));
        }

        [Test]
        public void Given_AnOnMdLeaf_When_ItIsRemovedFromThePanelAndAddedBack_Then_ItOffsWhileOutAndReadsTheContainerAgain()
        {
            // Arrange
            using var store = new ScopeStore(OuterScope, PlainNarrow, "@md:bg-wide");
            var leaf = MountScope(store, "@container");
            var container = leaf.parent.parent;
            var parent = leaf.parent;
            var onBefore = leaf.ClassListContains("bg-wide");
            var watchedBefore = CallbackRegistryProbe.BubbleUpCallbackCount(container);

            // Act
            leaf.RemoveFromHierarchy();
            var onWhileOut = leaf.ClassListContains("bg-wide");
            var released = watchedBefore - CallbackRegistryProbe.BubbleUpCallbackCount(container);
            parent.Add(leaf);

            // Assert
            Assert.That((onBefore, onWhileOut, released, leaf.ClassListContains("bg-wide")),
                Is.EqualTo((true, false, 1, true)));
        }

        // A subscription left behind would hold the manipulator, and through it the element, for the life of
        // the domain. The event's backing field is read by name so that this file builds on a tree that lacks it.

        private static int ScopeSubscribers()
            => (typeof(StyleResponsiveScope)
                .GetField("ScopesChanged", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;

        private (int Mounted, int Released) SubscribersAcrossUnmount(string leafClass)
        {
            var before = ScopeSubscribers();
            _mounted = V.Mount(_window.rootVisualElement, V.Div(MarkedNarrow, V.Label(className: leafClass, text: "x")));
            var mounted = ScopeSubscribers() - before;
            _mounted.Dispose();
            _mounted = null;
            return (mounted, ScopeSubscribers() - before);
        }

        [Test]
        public void Given_AMountedMdLeaf_When_Unmounted_Then_ItsScopeSubscriptionIsReleased()
        {
            // Act
            var subscribers = SubscribersAcrossUnmount("@md:bg-wide");

            // Assert
            Assert.That(subscribers, Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_AMountedStackedDarkMdLeaf_When_Unmounted_Then_BothScopeSubscriptionsAreReleased()
        {
            // Arrange — dark on, so the stack's @md: inner exists beside the dark: manipulator that owns it.
            VelvetTheme.IsDark = true;

            // Act
            var subscribers = SubscribersAcrossUnmount("dark:@md:bg-wide");

            // Assert
            Assert.That(subscribers, Is.EqualTo((2, 0)));
        }

        [Test]
        public void Given_AHoverStackedContainerLeaf_When_ItsClassNameLosesTheVariant_Then_NoStackedManipulatorIsLeftOnIt()
        {
            // Arrange — a 600px container is above @md, so the stacked hover: inner is created and, being
            // edge-based, kept when its gate closes.
            using var store = new ScopeStore(string.Empty, "@container w-[600px] shrink-[0]", "@md:hover:bg-wide");
            var leaf = MountScope(store, "@container");
            var context = _mounted.Root.Reconciler.Context;
            var stackedWhileDeclared = context.HasStackedVariantsOn(leaf);

            // Act
            store.SetLeaf("p-2");
            Drain();

            // Assert
            Assert.That((stackedWhileDeclared, context.HasStackedVariantsOn(leaf)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AContainerManipulatorTheUnmountNeverReached_When_TheTreeIsDisposed_Then_ItIsReleased()
        {
            // Arrange — a manipulator the unmount never reached: its element belongs to no fiber, as one an aborted
            // teardown skipped does not.
            var before = ScopeSubscribers();
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "anchor"));
            var context = _mounted.Root.Reconciler.Context;
            var stray = new Label("x");
            StyleContainerQuery.TryParse("@md", out var query);
            var manipulator = new StyleContainerVariantManipulator(context,
                new[] { new ContainerBinding("@md", query, new[] { "bg-wide" }, new[] { 0 }) });
            stray.AddManipulator(manipulator);
            context.ContainerVariantManipulators[stray] = manipulator;
            var held = (ScopeSubscribers() - before, context.ContainerVariantManipulators.Count);

            // Act
            _mounted.Dispose();
            _mounted = null;

            // Assert
            Assert.That((held, (ScopeSubscribers() - before, context.ContainerVariantManipulators.Count)),
                Is.EqualTo(((1, 1), (0, 0))));
        }
    }
}
