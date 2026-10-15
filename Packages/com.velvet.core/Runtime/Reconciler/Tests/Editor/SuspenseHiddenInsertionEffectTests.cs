using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which effects of a component a <c>V.Suspense</c> keeps hidden still run when its host renders it
    /// again, as React does: the insertion effects run in the mutation phase, which does not skip a hidden tree,
    /// while the layout effects wait for the reveal and the passive effects stay held.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseHiddenInsertionEffectTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            s_setBlocked = default;
            s_setTick = default;
            s_insertionSetups = 0;
            s_layoutSetups = 0;
            s_layoutCleanups = 0;
            s_passiveSetups = 0;
        }

        [Test]
        public void Given_AHiddenPrimary_When_ItsHostRendersItWithChangedDeps_Then_ItsInsertionEffectRuns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);

            // Act
            SetTick(mounted);

            // Assert
            Assert.That(s_insertionSetups, Is.EqualTo(2),
                "React runs the insertion effects of a tree a Suspense hides, as its mutation phase does not skip it");
        }

        // GREEN_ON_BASE(characterization): the base drops a hidden fiber's whole commit, so it runs no layout work.
        // What this pins is that the fix runs the insertion effects without running the layout ones beside them.
        [Test]
        public void Given_AHiddenPrimary_When_ItsHostRendersItWithChangedDeps_Then_ItsLayoutEffectStaysDown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);

            // Act
            SetTick(mounted);

            // Assert
            Assert.That($"{s_layoutSetups}/{s_layoutCleanups}", Is.EqualTo("1/1"),
                "React disconnects the layout effects of a tree a Suspense hides: one setup at mount, one cleanup at the hide");
        }

        // GREEN_ON_BASE(characterization): the base holds the passive work of a hidden fiber for the reveal.
        // What this pins is that the fix leaves the passive effects held.
        [Test]
        public void Given_AHiddenPrimary_When_ItsHostRendersItWithChangedDeps_Then_ItsPassiveEffectStaysHeld()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            mounted.FlushEffectsForTest();
            SetBlocked(mounted);

            // Act
            SetTick(mounted);
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_passiveSetups, Is.EqualTo(1),
                "A render made while a Suspense hides the tree commits no passive effects until the reveal");
        }

        private static void SetBlocked(MountedTree mounted)
        {
            s_setBlocked.Invoke(true);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static void SetTick(MountedTree mounted)
        {
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static StateUpdater<bool> s_setBlocked;
        private static StateUpdater<int> s_setTick;
        private static int s_insertionSetups;
        private static int s_layoutSetups;
        private static int s_layoutCleanups;
        private static int s_passiveSetups;

        [Component]
        private static VNode HostRender()
        {
            var (blocked, setBlocked) = Hooks.UseState(false);
            var (tick, setTick) = Hooks.UseState(0);
            s_setBlocked = setBlocked;
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Component(ProbeRender, tick, key: "probe"),
                        V.Component(ReaderRender, blocked, key: "reader"),
                    }),
            });
        }

        // Pending for good once blocked.
        [Component]
        private static VNode ReaderRender(bool blocked)
        {
            var value = Hooks.Use<int>(_ => blocked
                ? new VelvetTaskCompletionSource<int>().Task
                : VelvetTask.FromResult(0), blocked);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode ProbeRender(int tick)
        {
            Hooks.UseInsertionEffect(() =>
            {
                s_insertionSetups++;
                return (Action)null;
            }, new object[] { tick });
            Hooks.UseLayoutEffect(() =>
            {
                s_layoutSetups++;
                return (Action)(() => s_layoutCleanups++);
            }, new object[] { tick });
            Hooks.UseEffect(() =>
            {
                s_passiveSetups++;
                return (Action)null;
            }, new object[] { tick });
            return V.Label(text: "probe:" + tick);
        }
    }
}
