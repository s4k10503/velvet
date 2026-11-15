using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the registry's index of the inline fibers each Portal placeholder owns, which the hide of a
    /// Suspense, the cleanup of a Portal and the drain of a Portal's children read in place of a test of every
    /// registered fiber: it lists a fiber under the placeholder it is stamped with once mounted, and under none once
    /// the Portal leaves.
    /// </summary>
    [TestFixture]
    internal sealed class PortalOwnedFiberIndexTests
    {
        private VisualElement _root;
        private VisualElement _target;
        private StateUpdater<int> _setPortals;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _target = new VisualElement();
            FiberStrictMode.Enabled = false;
        }

        [Component]
        private static VNode Child() => V.Label(text: "c");

        [Component]
        private static VNode Host()
        {
            var (count, setCount) = Hooks.UseState(2);
            s_setPortals = setCount;
            var portals = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                portals[i] = V.Portal(s_target, children: new VNode[] { V.Component(Child, key: "c") }, key: "p" + i);
            }
            return V.Div(children: portals);
        }

        private static StateUpdater<int> s_setPortals;
        private static VisualElement s_target;

        private static string IndexSummary(MountedTree mounted)
        {
            var registry = mounted.Root.Reconciler.Context.ComponentRegistry;
            var index = (IDictionary)typeof(ComponentRegistry)
                .GetField("_inlineFibersOfPortal", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry);
            var fibers = 0;
            var agree = true;
            foreach (DictionaryEntry entry in index)
            {
                foreach (ComponentFiber fiber in (IEnumerable)entry.Value)
                {
                    fibers++;
                    agree &= ReferenceEquals(fiber.OwningPortalPlaceholder, entry.Key);
                }
            }
            return $"{index.Count} placeholders, {fibers} fibers, stamps agree: {agree}";
        }

        // GREEN_ON_BASE(construction): the base has no index, so the case cannot build there.
        [Test]
        public void Given_TwoPortalsEachRenderingAComponent_When_TheTreeMounts_Then_EachFiberIsIndexedUnderItsOwnPlaceholder()
        {
            // Arrange
            s_target = _target;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));

            // Act
            var summary = IndexSummary(mounted);

            // Assert
            Assert.That(summary, Is.EqualTo("2 placeholders, 2 fibers, stamps agree: True"));
        }

        // GREEN_ON_BASE(construction): the base has no index, so the case cannot build there.
        [Test]
        public void Given_TwoMountedPortals_When_OneIsRenderedAway_Then_OnlyTheOtherIsIndexed()
        {
            // Arrange
            s_target = _target;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));

            // Act
            s_setPortals.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(IndexSummary(mounted), Is.EqualTo("1 placeholders, 1 fibers, stamps agree: True"));
        }
    }
}
