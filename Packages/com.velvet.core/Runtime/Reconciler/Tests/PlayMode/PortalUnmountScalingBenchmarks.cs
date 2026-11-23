using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Unmounting many Portals at two sizes, so a cost that grows with the count shows as a median per Portal that
    /// grows from the 250 case to the 1000 one. Each sample is one update that removes every Portal and the drain
    /// that commits it, after an arrangement outside the window that mounts them: its time, and the GC.Alloc blocks
    /// it charged (<see cref="WindowCost"/>).
    /// <para>
    /// <c>SiblingPortals</c> unmounts that many Portals beside one another, <c>OnePortalWithInlineChildren</c> one
    /// Portal holding that many components, and <c>NoPortals</c> the same number of labels where no Portal is
    /// recorded, which is the cost an unmount has with nothing of the Portal's to find. Each is read as the median
    /// divided by the count, and a Portal case is read against its control's change between the two sizes.
    /// </para>
    /// <para>
    /// After every update, measured or not, the target and the root are read back, and a difference from what the
    /// update should leave throws.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class PortalUnmountScalingBenchmarks
    {
        private static readonly int WarmupCount = BenchmarkRun.Warmup(3);
        private static readonly int MeasurementCount = BenchmarkRun.Measurements(10);

        private VisualElement _root = null!;
        private MountedTree _mounted = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            PortalUnmountApp.Target = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null!;
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void SiblingPortals_250() => Run(PortalUnmountApp.Siblings, 250, PortalUnmountApp.PortalRows);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void SiblingPortals_1000() => Run(PortalUnmountApp.Siblings, 1000, PortalUnmountApp.PortalRows);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void OnePortalWithInlineChildren_250() => Run(PortalUnmountApp.InlineChildren, 250, PortalUnmountApp.PortalRows);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void OnePortalWithInlineChildren_1000() => Run(PortalUnmountApp.InlineChildren, 1000, PortalUnmountApp.PortalRows);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void NoPortals_250() => Run(PortalUnmountApp.Control, 250, PortalUnmountApp.HostRows);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void NoPortals_1000() => Run(PortalUnmountApp.Control, 1000, PortalUnmountApp.HostRows);

        private void Run(Func<VNode> host, int count, Func<VisualElement, int> rows)
        {
            _mounted = V.Mount(_root, V.Component(host));
            var time = new SampleGroup("Unmount.Time", SampleUnit.Millisecond);
            var allocation = new SampleGroup("Unmount.GC", SampleUnit.Undefined);

            for (var i = 0; i < WarmupCount; i++)
            {
                Arrange(count, rows);
                Unmount();
                Verify(rows);
            }

            for (var i = 0; i < MeasurementCount; i++)
            {
                Arrange(count, rows);
                var cost = WindowCost.Of(Unmount);
                Verify(rows);
                Measure.Custom(time, cost.Milliseconds);
                Measure.Custom(allocation, cost.AllocationBlocks);
            }
        }

        private void Arrange(int count, Func<VisualElement, int> rows)
        {
            PortalUnmountApp.Set.Invoke(count);
            _mounted.DrainForTest();
            if (rows(_root) != count)
            {
                throw new InvalidOperationException($"expected {count} rows, saw {rows(_root)}");
            }
        }

        private void Unmount()
        {
            PortalUnmountApp.Set.Invoke(0);
            _mounted.DrainForTest();
        }

        private void Verify(Func<VisualElement, int> rows)
        {
            if (rows(_root) != 0)
            {
                throw new InvalidOperationException($"expected no rows after the unmount, saw {rows(_root)}");
            }
        }
    }

    // A host whose one state is how many Portals, Portal children or labels it renders.
    internal static class PortalUnmountApp
    {
        internal static VisualElement Target = null!;
        internal static StateUpdater<int> Set;

        // Each Portal case's rows are the ones in the target, and the control's the ones in the host's own element.
        internal static int PortalRows(VisualElement root) => Target.childCount;

        internal static int HostRows(VisualElement root) => root.childCount == 0 ? 0 : root[0].childCount;

        [Component]
        private static VNode Child() => V.Label(text: "c");

        [Component]
        internal static VNode Siblings()
        {
            var (count, set) = Hooks.UseState(0);
            Set = set;
            var portals = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                portals[i] = V.Portal(Target, children: new VNode[] { V.Label(text: "p") }, key: "p" + i);
            }
            return V.Div(children: portals);
        }

        [Component]
        internal static VNode InlineChildren()
        {
            var (count, set) = Hooks.UseState(0);
            Set = set;
            var children = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                children[i] = V.Component(Child, key: "c" + i);
            }
            return V.Div(children: new VNode[] { V.Portal(Target, children: children, key: "p") });
        }

        [Component]
        internal static VNode Control()
        {
            var (count, set) = Hooks.UseState(0);
            Set = set;
            var labels = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                labels[i] = V.Label(text: "l", key: "l" + i);
            }
            return V.Div(children: labels);
        }
    }
}
