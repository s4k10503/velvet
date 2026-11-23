using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the append log a Portal's mount and patch open on the declaring fiber is closed again, so a
    /// fiber holds no log once the pass is over. A read answers -1 where the fiber type holds no such log.
    /// </summary>
    [TestFixture]
    internal sealed class PortalAppendLogReleaseTests
    {
        private static StateUpdater<int> s_setPortals;
        private static VisualElement s_target;

        [SetUp]
        public void SetUp()
        {
            s_target = new VisualElement();
            FiberStrictMode.Enabled = false;
        }

        [Component]
        private static VNode Child() => V.Label(text: "c");

        [Component]
        private static VNode Host()
        {
            var (count, setCount) = Hooks.UseState(1);
            s_setPortals = setCount;
            var portals = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                portals[i] = V.Portal(s_target, children: new VNode[] { V.Component(Child, key: "c") }, key: "p" + i);
            }
            return V.Div(children: portals);
        }

        private static int FibersHoldingALog(MountedTree mounted)
        {
            var field = typeof(ComponentFiber).GetField("_appendLog", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) return -1;
            var registry = mounted.Root.Reconciler.Context.ComponentRegistry;
            var table = (IDictionary)typeof(ComponentRegistry)
                .GetField("_inlineFiberToKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry);
            var holding = 0;
            foreach (ComponentFiber fiber in table.Keys)
            {
                if (field.GetValue(fiber) != null || (fiber.Parent != null && field.GetValue(fiber.Parent) != null)) holding++;
            }
            return holding;
        }

        [Test]
        public void Given_APortalDrainedOnMount_When_TheMountHasFinished_Then_NoFiberHoldsAnAppendLog()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(Host, key: "host"));

            // Act
            var holding = FibersHoldingALog(mounted);

            // Assert
            Assert.That(holding, Is.EqualTo(0));
        }

        [Test]
        public void Given_MountedPortals_When_AnotherPortalIsAddedAndPatched_Then_NoFiberHoldsAnAppendLog()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(Host, key: "host"));

            // Act
            s_setPortals.Invoke(3);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(FibersHoldingALog(mounted), Is.EqualTo(0));
        }
    }
}
