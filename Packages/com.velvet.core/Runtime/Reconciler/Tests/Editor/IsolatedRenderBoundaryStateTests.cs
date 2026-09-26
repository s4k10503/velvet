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
