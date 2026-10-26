using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Rows that are themselves pooled Buttons with a click handler. The keyed benchmark rows are plain containers
    /// holding their Buttons, and a container is discarded rather than pooled, so what a pooled Button's teardown
    /// costs, and what renting one back costs, is measured nowhere else. Time is the stopwatch over one reconcile
    /// of the case's operation; the arrangement that puts the rows in the state it starts from runs outside it.
    /// </summary>
    [TestFixture]
    internal sealed class PooledButtonRowBenchmarks
    {
        private const int Rows = 1000;

        private static readonly int WarmupCount = BenchmarkRun.Warmup(3);
        private static readonly int MeasurementCount = BenchmarkRun.Measurements(10);

        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; the tree check holds behaviour the base already has.
        [Test, Performance]
        public void Clear_1000PooledButtonRows()
        {
            var rows = BuildRows();

            Run(
                () => _reconciler.Reconcile(_root, Array.Empty<VNode>(), rows),
                () => _reconciler.Reconcile(_root, rows, Array.Empty<VNode>()));

            Assert.AreEqual(0, _root.childCount);
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; the tree check holds behaviour the base already has.
        [Test, Performance]
        public void Create_1000PooledButtonRows()
        {
            var rows = BuildRows();

            Run(
                () =>
                {
                    if (_root.childCount > 0) _reconciler.Reconcile(_root, rows, Array.Empty<VNode>());
                },
                () => _reconciler.Reconcile(_root, Array.Empty<VNode>(), rows));

            Assert.AreEqual(Rows, _root.childCount);
        }

        // The arrangement of the first iteration meets an empty pool and a later one a full one, which is the
        // path a Button rented back takes, so one more arrangement and operation run after the loops.
        private static void Run(Action arrange, Action operate)
        {
            Measure.Method(operate)
                .SetUp(arrange)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();
            arrange();
            operate();
        }

        private static VNode[] BuildRows()
        {
            var handler = new Action(() => { });
            var rows = new VNode[Rows];
            for (var i = 0; i < Rows; i++)
            {
                rows[i] = V.Button(text: "row " + i, onClick: handler, key: "r" + i);
            }
            return rows;
        }
    }
}
