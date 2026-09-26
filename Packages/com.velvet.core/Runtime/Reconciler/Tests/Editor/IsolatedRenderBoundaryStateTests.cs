using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a Suspense's committed branch and an AnimatePresence's committed children are found
    /// again by the component that renders them when it re-renders alone under a wrapper its parent put it
    /// in. The parent's walk reaches the component's body through the wrapper; the component's own render
    /// starts at the keying root.
    /// <list type="bullet">
    /// <item>A suspended boundary keeps the one fallback element it committed, under a keyed Fragment and
    /// inside a <c>V.Memoized</c>.</item>
    /// <item>A consumer inside that fallback, re-rendering alone, still reads the Provider the fallback
    /// places above it.</item>
    /// <item>An AnimatePresence keeps the child element it committed, under the same two wrappers.</item>
    /// <item>A suspended boundary rendered by a component inside another Suspense's children keeps its one
    /// fallback element too: a Suspense's branches establish a scope of their own.</item>
    /// <item>An AnimatePresence rendered by a component that is another AnimatePresence's child keeps its
    /// committed child the same way.</item>
    /// <item>Two boundaries in one body, the second under an unkeyed Fragment or Provider at the first's index,
    /// each keep their own record: a resolved Suspense does not take a suspended one's fallback away, and two
    /// AnimatePresence do not show each other's children.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class IsolatedRenderBoundaryStateTests
    {
        private static VelvetTaskCompletionSource<string> s_resource;
        private static StateUpdater<int> s_setOwnerTick;
        private static StateUpdater<int> s_setReaderTick;
        private static readonly ComponentContext<string> Theme = ComponentContext<string>.Create("default");
        private MountedTree? _mounted;

        [SetUp]
        public void SetUp()
        {
            s_resource = new VelvetTaskCompletionSource<string>();
            s_setOwnerTick = default;
            s_setReaderTick = default;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        [Component(Compiler = false)]
        private static VNode Pending() => V.Label(name: "primary", text: Hooks.Use(_ => s_resource.Task));

        // Each owner returns its boundary with no element above it: an element's children start their own walk
        // at the keying root, so a boundary under one is placed alike from both entry points.
        [Component(Compiler = false)]
        private static VNode SuspenseOwner()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnerTick = setTick;
            return V.Fragment(children: new VNode?[]
            {
                V.Label(name: "tick", text: tick.ToString()),
                V.Suspense(V.Label(name: "loading"), new VNode?[] { V.Component(Pending) }),
            });
        }

        [Component(Compiler = false)]
        private static VNode FallbackThemeReader()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setReaderTick = setTick;
            return V.Label(name: "theme", text: Hooks.UseContext(Theme) + tick);
        }

        [Component(Compiler = false)]
        private static VNode ProvidingFallbackOwner()
            => V.Suspense(
                V.Provider(Theme, "fallback", children: new VNode?[] { V.Component(FallbackThemeReader) }),
                new VNode?[] { V.Component(Pending) });

        [Component(Compiler = false)]
        private static VNode PresenceOwner()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnerTick = setTick;
            return V.Fragment(children: new VNode?[]
            {
                V.Label(name: "tick", text: tick.ToString()),
                V.AnimatePresence(initial: false, children: new VNode?[] { V.Div(name: "item", key: "a") }),
            });
        }

        private static VNode UnderKeyedFragment(Func<VNode> owner)
            => V.Div(children: new VNode?[]
            {
                V.Fragment(key: "wrapper", children: new VNode?[] { V.Component(owner) }),
            });

        private static VNode InsideMemo(Func<VNode> owner)
            => V.Div(children: new VNode?[] { V.Memoized(() => V.Component(owner), 0) });

        [Component(Compiler = false)]
        private static VNode SuspenseUnderKeyedFragmentHost() => UnderKeyedFragment(SuspenseOwner);

        [Component(Compiler = false)]
        private static VNode SuspenseInsideMemoHost() => InsideMemo(SuspenseOwner);

        [Component(Compiler = false)]
        private static VNode ProvidingFallbackUnderKeyedFragmentHost() => UnderKeyedFragment(ProvidingFallbackOwner);

        [Component(Compiler = false)]
        private static VNode ProvidingFallbackInsideMemoHost() => InsideMemo(ProvidingFallbackOwner);

        [Component(Compiler = false)]
        private static VNode PresenceUnderKeyedFragmentHost() => UnderKeyedFragment(PresenceOwner);

        [Component(Compiler = false)]
        private static VNode PresenceInsideMemoHost() => InsideMemo(PresenceOwner);

        [Component(Compiler = false)]
        private static VNode SuspenseInsideSuspenseHost()
            => V.Div(children: new VNode?[]
            {
                V.Suspense(V.Label(name: "outer-loading"), new VNode?[] { V.Component(SuspenseOwner) }),
            });

        // The second boundary sits at the first's index inside an unkeyed wrapper, which opens no scope where
        // none is open, so only a key that counts the wrapper tells the two apart.
        private static VNode ResolvedBesideSuspended(int tick, Func<VNode?[], VNode> wrap)
            => V.Fragment(children: new VNode?[]
            {
                V.Suspense(V.Label(name: "loading"), new VNode?[] { V.Component(Pending) }),
                wrap(new VNode?[] { V.Suspense(V.Label(name: "unused"), new VNode?[] { V.Label(name: "ready") }) }),
                V.Label(name: "tick", text: tick.ToString()),
            });

        [Component(Compiler = false)]
        private static VNode ResolvedUnderFragmentBesideSuspendedOwner()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnerTick = setTick;
            return ResolvedBesideSuspended(tick, children => V.Fragment(children: children));
        }

        [Component(Compiler = false)]
        private static VNode ResolvedUnderProviderBesideSuspendedOwner()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnerTick = setTick;
            return ResolvedBesideSuspended(tick, children => V.Provider(Theme, "inside", children: children));
        }

        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["visible"] = "opacity-100",
            ["hidden"] = "opacity-0",
        };

        // An exit that animates, so a child one presence does not name is held as a ghost rather than dropped.
        private static VNode FadingChild(string name, string key)
            => V.Motion(name: name, key: key, variants: s_fade, animate: "visible", exit: "hidden",
                transition: new StyleTransitionConfig { DurationSec = 0.3f });

        [Component(Compiler = false)]
        private static VNode TwoPresencesOwner()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnerTick = setTick;
            return V.Fragment(children: new VNode?[]
            {
                V.AnimatePresence(initial: false, children: new VNode?[] { FadingChild("first", "a") }),
                V.Fragment(children: new VNode?[]
                {
                    V.AnimatePresence(initial: false, children: new VNode?[] { FadingChild("second", "b") }),
                }),
                V.Label(name: "tick", text: tick.ToString()),
            });
        }

        [Component(Compiler = false)]
        private static VNode PresenceAsPresenceChildHost()
            => V.Div(children: new VNode?[]
            {
                V.AnimatePresence(initial: false, children: new VNode?[] { V.Component(PresenceOwner, key: "row") }),
            });

        private static IReadOnlyList<VisualElement> Named(VisualElement container, string name)
            => container.Query<VisualElement>(name: name).ToList();

        // The owner's tick is folded in so a render that never happened cannot read as the element surviving it.
        private (string Tick, int Count, bool SameInstance) OwnerReRendersAlone(
            Func<VNode> host, string committedName)
        {
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(host, key: "host"));
            var committed = Named(container, committedName).Single();

            s_setOwnerTick.Invoke(1);
            _mounted.FlushStateForTest();

            var after = Named(container, committedName);
            return (container.Q<Label>("tick")?.text, after.Count,
                after.Count == 1 && ReferenceEquals(after[0], committed));
        }

        [Test]
        public void Given_SuspendedBoundaryUnderKeyedFragment_When_ItsOwnerReRendersAlone_Then_ItKeepsItsOneFallbackElement()
        {
            // Arrange
            var host = (Func<VNode>)SuspenseUnderKeyedFragmentHost;

            // Act
            var observed = OwnerReRendersAlone(host, "loading");

            // Assert
            Assert.That(observed, Is.EqualTo(("1", 1, true)));
        }

        [Test]
        public void Given_SuspendedBoundaryInsideMemo_When_ItsOwnerReRendersAlone_Then_ItKeepsItsOneFallbackElement()
        {
            // Arrange
            var host = (Func<VNode>)SuspenseInsideMemoHost;

            // Act
            var observed = OwnerReRendersAlone(host, "loading");

            // Assert
            Assert.That(observed, Is.EqualTo(("1", 1, true)));
        }

        [Test]
        public void Given_ConsumerInFallbackUnderKeyedFragment_When_TheConsumerReRendersAlone_Then_ItStillReadsTheFallbackProvider()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(ProvidingFallbackUnderKeyedFragmentHost, key: "host"));

            // Act
            s_setReaderTick.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(container.Q<Label>("theme")?.text, Is.EqualTo("fallback1"));
        }

        [Test]
        public void Given_ConsumerInFallbackInsideMemo_When_TheConsumerReRendersAlone_Then_ItStillReadsTheFallbackProvider()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(ProvidingFallbackInsideMemoHost, key: "host"));

            // Act
            s_setReaderTick.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(container.Q<Label>("theme")?.text, Is.EqualTo("fallback1"));
        }

        [Test]
        public void Given_AnimatePresenceUnderKeyedFragment_When_ItsOwnerReRendersAlone_Then_ItKeepsItsCommittedChild()
        {
            // Arrange
            var host = (Func<VNode>)PresenceUnderKeyedFragmentHost;

            // Act
            var observed = OwnerReRendersAlone(host, "item");

            // Assert
            Assert.That(observed, Is.EqualTo(("1", 1, true)));
        }

        [Test]
        public void Given_SuspendedBoundaryInsideAnotherSuspense_When_ItsOwnerReRendersAlone_Then_ItKeepsItsOneFallbackElement()
        {
            // Arrange
            var host = (Func<VNode>)SuspenseInsideSuspenseHost;

            // Act
            var observed = OwnerReRendersAlone(host, "loading");

            // Assert
            Assert.That(observed, Is.EqualTo(("1", 1, true)));
        }

        [Test]
        public void Given_ResolvedBoundaryUnderUnkeyedFragmentBesideSuspendedOne_When_TheOwnerReRenders_Then_TheSuspendedOneKeepsItsOneFallbackElement()
        {
            // Arrange
            var host = (Func<VNode>)ResolvedUnderFragmentBesideSuspendedOwner;

            // Act
            var observed = OwnerReRendersAlone(host, "loading");

            // Assert
            Assert.That(observed, Is.EqualTo(("1", 1, true)));
        }

        [Test]
        public void Given_ResolvedBoundaryUnderUnkeyedProviderBesideSuspendedOne_When_TheOwnerReRenders_Then_TheSuspendedOneKeepsItsOneFallbackElement()
        {
            // Arrange
            var host = (Func<VNode>)ResolvedUnderProviderBesideSuspendedOwner;

            // Act
            var observed = OwnerReRendersAlone(host, "loading");

            // Assert
            Assert.That(observed, Is.EqualTo(("1", 1, true)));
        }

        [Test]
        public void Given_AnimatePresenceRenderedAsAnotherPresencesChild_When_ItsOwnerReRendersAlone_Then_ItKeepsItsCommittedChild()
        {
            // Arrange
            var host = (Func<VNode>)PresenceAsPresenceChildHost;

            // Act
            var observed = OwnerReRendersAlone(host, "item");

            // Assert
            Assert.That(observed, Is.EqualTo(("1", 1, true)));
        }

        [Test]
        public void Given_SecondAnimatePresenceUnderUnkeyedFragment_When_TheOwnerReRenders_Then_EachShowsOnlyItsOwnChild()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(TwoPresencesOwner, key: "host"));

            // Act
            s_setOwnerTick.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            var shown = string.Join("|", container.Query<VisualElement>()
                .Where(element => element.name is "first" or "second").ToList().Select(element => element.name));
            Assert.That((container.Q<Label>("tick")?.text, shown), Is.EqualTo(("1", "first|second")));
        }

        [Test]
        public void Given_AnimatePresenceInsideMemo_When_ItsOwnerReRendersAlone_Then_ItKeepsItsCommittedChild()
        {
            // Arrange
            var host = (Func<VNode>)PresenceInsideMemoHost;

            // Act
            var observed = OwnerReRendersAlone(host, "item");

            // Assert
            Assert.That(observed, Is.EqualTo(("1", 1, true)));
        }
    }
}
