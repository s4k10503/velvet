using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that an error boundary that has caught keeps its fallback until it remounts, as React's does.
    /// <list type="bullet">
    /// <item>A later render of it — its parent's, with nothing throwing any more — renders the fallback
    /// again, handed the error it caught and the props of that render, and not its children.</item>
    /// <item>Given a new key, it remounts and renders its children.</item>
    /// <item>An error its fallback's content throws on such a render goes to the boundary above, whether its
    /// parent's render or its own update renders it.</item>
    /// <item>With the StrictMode double render on, rendering its fallback again reports no impure render.</item>
    /// <item>What its body renders on those renders goes back to the pool.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class StickyBoundaryTests
    {
        private static bool s_throws;
        private static bool s_fallbackContentThrows;
        private static Action<int> s_setTick;
        private static Action<int> s_setKey;
        private static Action<int> s_setOwnTick;
        private static int s_innerFactoryRuns;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_throws = false;
            s_fallbackContentThrows = false;
            s_setTick = null;
            s_setKey = null;
            s_setOwnTick = null;
            s_innerFactoryRuns = 0;
            FiberStrictMode.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            FiberStrictMode.Enabled = false;
        }

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        [Test]
        public void Given_ABoundaryThatCaught_When_ItsParentRendersAgainWithNothingThrowing_Then_ItShowsItsFallbackWithTheCaughtErrorAndTheNewProps()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = false;

            // Act
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("fallback:2:Sticky boundary throw"));
        }

        // GREEN_ON_BASE(characterization): the merge base renders the children of a boundary given a new key too.
        // A boundary keeps its fallback until it remounts now, and a new key remounting it is what this pins.
        [Test]
        public void Given_ABoundaryThatCaught_When_ItsParentGivesItANewKey_Then_ItRendersItsChildren()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);
            var caught = Texts();
            s_throws = false;

            // Act
            s_setKey.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the catch is folded in, since a boundary that never caught renders its children as well
            Assert.That(caught + "|" + Texts(), Is.EqualTo("fallback:0:Sticky boundary throw|child"));
        }

        [Test]
        public void Given_ABoundaryShowingItsFallback_When_ItsParentsRenderMakesTheFallbacksContentThrow_Then_TheBoundaryAboveCatchesIt()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(OuterHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = false;
            s_fallbackContentThrows = true;
            s_innerFactoryRuns = 0;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the inner factory's runs are read with it: one that caught the content's error again runs
            // it a second time before the boundary above takes it
            Assert.That(Texts() + ", inner factory ran " + s_innerFactoryRuns, Is.EqualTo("outer-fallback, inner factory ran 1"));
        }

        [Test]
        public void Given_ABoundaryShowingItsFallback_When_ItsOwnUpdateMakesTheFallbacksContentThrow_Then_TheBoundaryAboveCatchesIt()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(OuterHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = false;
            s_fallbackContentThrows = true;
            s_innerFactoryRuns = 0;

            // Act
            s_setOwnTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the inner factory's runs are read with it: one that caught the content's error again runs
            // it a second time before the boundary above takes it
            Assert.That(Texts() + ", inner factory ran " + s_innerFactoryRuns, Is.EqualTo("outer-fallback, inner factory ran 1"));
        }

        // GREEN_ON_BASE(characterization): the merge base double-renders a boundary's body, which matches its commit.
        // A boundary showing its fallback commits the fallback instead, and no impure report for that is what this pins.
        [Test]
        public void Given_StrictModeAndABoundaryThatCaught_When_ItsParentRendersAgain_Then_NoImpureRenderIsReported()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);
            FiberStrictMode.Enabled = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — an error log fails the case as well
            LogAssert.NoUnexpectedReceived();
        }

        // GREEN_ON_BASE(characterization): the merge base commits and later retires what this body renders.
        // A boundary showing its fallback discards it at each render instead, and handing it back is what this pins.
        [Test]
        public void Given_ABoundaryShowingItsFallbackWhoseBodyRentsPooledNodes_When_ItRendersAgainAndIsDisposed_Then_ThePoolCountsNothingRentedOut()
        {
            // Arrange
            s_throws = true;
            var before = Rented();
            var mounted = V.Mount(_root, V.Component(PooledHostRender, key: "host"), CaughtErrors.Unlogged);
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            mounted.Dispose();

            // Assert
            var after = Rented();
            Assert.That(
                (after.Props - before.Props, after.EventArrays - before.EventArrays, after.NodeArrays - before.NodeArrays),
                Is.EqualTo((0, 0, 0)));
        }

        // Read by reflection for the reason MemoCacheCollisionTests reads the same sets.
        private static (int Props, int EventArrays, int NodeArrays) Rented()
        {
            int Count(string field)
            {
                var set = typeof(VNodePool).GetField(field, System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static).GetValue(null);
                return (int)set.GetType().GetProperty("Count").GetValue(set);
            }
            return (Count("s_ownedProps"), Count("s_ownedSingleEventArrays"), Count("s_ownedNodeArrays"));
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode PooledBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(new VNode[]
            {
                V.Div(children: V.List(new[] { "open" }, id => id, id => V.Button(text: id + tick, onClick: () => { }))),
                V.Component(ThrowerRender, key: "thrower"),
            });
        }

        [Component(Compiler = false)]
        private static VNode PooledHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[] { V.Component(PooledBoundaryRender, tick, key: "boundary") });
        }

        [Component(Compiler = false)]
        private static VNode ThrowerRender()
        {
            if (s_throws) throw new InvalidOperationException("Sticky boundary throw");
            return V.Label(text: "child");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode BoundaryRender(int tick)
        {
            Hooks.UseFallback(ex => V.Label(text: "fallback:" + tick + ":" + ex.Message));
            return V.Component(ThrowerRender, key: "thrower");
        }

        [Component(Compiler = false)]
        private static VNode HostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            var (key, setKey) = Hooks.UseState(0);
            s_setTick = setTick;
            s_setKey = setKey;
            return V.Div(children: new VNode[] { V.Component(BoundaryRender, tick, key: "boundary-" + key) });
        }

        [Component(Compiler = false)]
        private static VNode FallbackContentRender()
        {
            if (s_fallbackContentThrows) throw new InvalidOperationException("Fallback content throw");
            return V.Label(text: "inner-fallback");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode InnerBoundaryRender(int tick)
        {
            var (_, setOwnTick) = Hooks.UseState(0);
            s_setOwnTick = setOwnTick;
            Hooks.UseFallback(_ =>
            {
                s_innerFactoryRuns++;
                return V.Component(FallbackContentRender, key: "content");
            });
            return V.Component(ThrowerRender, key: "thrower");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OuterBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "outer-fallback"));
            return V.Component(InnerBoundaryRender, tick, key: "inner");
        }

        [Component(Compiler = false)]
        private static VNode OuterHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[] { V.Component(OuterBoundaryRender, tick, key: "outer") });
        }
    }
}
