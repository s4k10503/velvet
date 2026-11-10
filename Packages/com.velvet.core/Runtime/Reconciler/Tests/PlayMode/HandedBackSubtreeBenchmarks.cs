using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// An owner that renders again beside an element its compiler hands back by instance, holding a number of
    /// component fibers below it. The two cases differ only in that number, so a time that grows with it is a walk
    /// over the fibers the skip of the handed-back element was meant to spare. Time is the stopwatch over a run of
    /// the owner's renders, each flushed synchronously.
    /// </summary>
    [TestFixture]
    internal sealed class HandedBackSubtreeBenchmarks
    {
        private const int RendersPerMeasurement = 100;

        private static readonly int WarmupCount = BenchmarkRun.Warmup(3);
        private static readonly int MeasurementCount = BenchmarkRun.Measurements(10);

        private static int s_leaves;
        private static int s_leafRenders;
        private static StateUpdater<int> s_setTick;

        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_leafRenders = 0;
            s_setTick = default;
        }

        [Component(Compiler = false)]
        private static VNode Leaf()
        {
            s_leafRenders++;
            return V.Label(text: "leaf");
        }

        [Component(Compiler = false)]
        private static VNode Leaves()
        {
            var children = new VNode[s_leaves];
            for (var i = 0; i < children.Length; i++)
            {
                children[i] = V.Component(Leaf, key: "leaf" + i);
            }
            return V.Div(name: "leaves", children: children);
        }

        [Component]
        private static VNode Owner()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(name: "owner", children: new VNode[]
            {
                V.Label(name: "tick", text: "tick:" + tick),
                V.Div(name: "held", children: new VNode[] { V.Component(Leaves, key: "leaves") }),
            });
        }

        [Test, Performance]
        public void Rerender_OwnerBesideAHandedBackElementOver100Fibers() => Run(100);

        [Test, Performance]
        public void Rerender_OwnerBesideAHandedBackElementOver10000Fibers() => Run(10000);

        private void Run(int leaves)
        {
            s_leaves = leaves;
            using var mounted = V.Mount(_root, V.Component(Owner, key: "owner"));
            var tick = 0;

            Measure.Method(() =>
                {
                    for (var i = 0; i < RendersPerMeasurement; i++)
                    {
                        s_setTick.Invoke(++tick);
                        mounted.FlushStateForTest();
                    }
                })
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();

            if (s_leafRenders != leaves)
            {
                throw new InvalidOperationException(
                    $"The leaves rendered {s_leafRenders} times for {leaves} leaves, so the owner's renders reached them.");
            }
        }
    }
}
