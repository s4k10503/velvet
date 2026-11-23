using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the growth the ranges keep per target for a Portal patched around another: an entry stands while a
    /// Portal holds a range on the target and is gone once the last one leaves, so a tree whose rows each mount and
    /// unmount a Portal into an element of their own keeps none of them. A read answers -1 where the store keeps no
    /// such table.
    /// </summary>
    [TestFixture]
    internal sealed class PortalGrowthEntryTests
    {
        private const int Targets = 5;

        private static StateUpdater<int> s_setPortals;
        private static StateUpdater<int> s_setTick;
        private static readonly List<VisualElement> s_targets = new();

        [SetUp]
        public void SetUp()
        {
            s_targets.Clear();
            for (var i = 0; i < Targets; i++) s_targets.Add(new VisualElement());
            FiberStrictMode.Enabled = false;
        }

        [Component]
        private static VNode Host()
        {
            var (count, setCount) = Hooks.UseState(Targets);
            var (tick, setTick) = Hooks.UseState(0);
            s_setPortals = setCount;
            s_setTick = setTick;
            var portals = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                portals[i] = V.Portal(s_targets[i], key: "p" + i, children: new VNode[] { V.Label(text: "t" + tick, key: "l") });
            }
            return V.Div(children: portals);
        }

        private static int GrowthEntries(MountedTree mounted)
        {
            var store = mounted.Root.Reconciler.Context.PortalState.Stored;
            var table = (IDictionary)typeof(PortalRangeStore)
                .GetField("_growth", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(store);
            return table?.Count ?? -1;
        }

        private static void Render(MountedTree mounted)
        {
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        [Test]
        public void Given_PortalsIntoSeparateTargets_When_TheyAreMountedAndPatched_Then_EachTargetKeepsAGrowthEntry()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(Host, key: "host"));

            // Act
            s_setTick.Invoke(1);
            Render(mounted);

            // Assert
            Assert.That(GrowthEntries(mounted), Is.EqualTo(Targets));
        }

        [Test]
        public void Given_PortalsIntoSeparateTargetsThatWerePatched_When_TheyAreAllUnmounted_Then_NoGrowthEntryRemains()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(Host, key: "host"));
            s_setTick.Invoke(1);
            Render(mounted);

            // Act
            s_setPortals.Invoke(0);
            Render(mounted);

            // Assert
            Assert.That(GrowthEntries(mounted), Is.EqualTo(0));
        }
    }
}
