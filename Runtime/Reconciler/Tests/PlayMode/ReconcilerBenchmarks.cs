using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.UIElements;

namespace Velvet.Tests.Performance
{
    [TestFixture]
    public class ReconcilerBenchmarks
    {
        private Reconciler _reconciler;
        private VisualElement _root;

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

        #region B-1-a: Initial mount (long-lived Reconciler)

        [Test, Performance]
        public void InitialMount_10Elements() => RunInitialMountBenchmark(10, 10);

        [Test, Performance]
        public void InitialMount_100Elements() => RunInitialMountBenchmark(100, 10);

        [Test, Performance]
        public void InitialMount_1000Elements() => RunInitialMountBenchmark(1000, 10);

        #endregion

        #region B-1-b: Re-reconcile — no changes

        [Test, Performance]
        public void Reconcile_NoChange_100Elements()
        {
            var nodes = BenchmarkHelpers.BuildLabelNodes(100);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), nodes);

            Measure.Method(() =>
            {
                _reconciler.Reconcile(_root, nodes, nodes);
            })
            .GC()
            .WarmupCount(20)
            .MeasurementCount(20)
            .Run();
        }

        #endregion

        #region B-1-c: Re-reconcile — text change for all elements

        [Test, Performance]
        public void Reconcile_AllChange_100Elements()
        {
            var oldNodes = BenchmarkHelpers.BuildLabelNodes(100, prefix: "old-");
            var newNodes = BenchmarkHelpers.BuildLabelNodes(100, prefix: "new-");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldNodes);

            Measure.Method(() =>
            {
                _reconciler.Reconcile(_root, oldNodes, newNodes);
            })
            .GC()
            .WarmupCount(5)
            .MeasurementCount(20)
            .Run();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Initial mount on the long-lived Reconciler path. Production reuses one Reconciler per Page/Component,
        /// so this is the realistic metric. Reconciler ctor/Dispose is hoisted out of the measured region
        /// (via <c>[SetUp]</c>/<c>[TearDown]</c>) so the numbers reflect mount cost only. See
        /// <see cref="InitialMountAllocBreakdown"/> for the detailed breakdown.
        /// </summary>
        private void RunInitialMountBenchmark(int count, int measurements, int warmup = 3)
        {
            var nodes = BenchmarkHelpers.BuildLabelNodes(count);

            Measure.Method(() =>
            {
                var root = new VisualElement();
                _reconciler.Reconcile(root, Array.Empty<VNode>(), nodes);
            })
            .GC()
            .WarmupCount(warmup)
            .MeasurementCount(measurements)
            .Run();
        }

        #endregion
    }
}
