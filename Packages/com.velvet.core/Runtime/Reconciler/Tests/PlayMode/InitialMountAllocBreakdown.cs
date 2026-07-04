using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.UIElements;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Initial mount GC alloc breakdown benchmark.
    /// Introduced to isolate the source of "11 alloc/element"; demonstrated the following:
    ///
    ///   <list type="bullet">
    ///     <item>The per-element ~11 alloc originates from <b>Unity UIToolkit internals</b> (new Label / text setter / parent.Add)</item>
    ///     <item>Velvet's Reconcile path is <b>0 alloc</b> (long-lived Reconciler path; Bench_G matches Bench_E)</item>
    ///     <item>The F-G difference is the one-time <c>new Reconciler()</c> ctor cost</item>
    ///   </list>
    ///
    /// Expected relative relationships (environment-independent):
    ///   <code>
    ///   G ≈ E              (Velvet long-lived matches the Unity floor = 0 Velvet alloc)
    ///   F ≈ E + α          (α is the fixed overhead of one Reconciler ctor)
    ///   A ≤ B,C ≤ D ≤ E    (monotonically increasing as features are added)
    ///   </code>
    ///
    /// This file is for early detection of baseline regressions. If new alloc is introduced on Velvet's side,
    /// G drifts far from E → catchable via CI / manual review.
    /// </summary>
    [TestFixture]
    public class InitialMountAllocBreakdown
    {
        private const int Count = 1000;
        private const int WarmUp = 3;
        private const int Measurements = 10;

        [Test, Performance]
        public void Bench_A_NewLabelAlone()
        {
            Measure.Method(() =>
            {
                for (var i = 0; i < Count; i++)
                {
                    _ = new Label();
                }
            })
                .GC()
                .WarmupCount(WarmUp)
                .MeasurementCount(Measurements)
                .Run();
        }

        [Test, Performance]
        public void Bench_B_NewLabelWithText()
        {
            Measure.Method(() =>
            {
                for (var i = 0; i < Count; i++)
                {
                    _ = new Label("item");
                }
            })
                .GC()
                .WarmupCount(WarmUp)
                .MeasurementCount(Measurements)
                .Run();
        }

        [Test, Performance]
        public void Bench_C_NewLabelAndSetText()
        {
            Measure.Method(() =>
            {
                for (var i = 0; i < Count; i++)
                {
                    var l = new Label();
                    l.text = "item";
                }
            })
                .GC()
                .WarmupCount(WarmUp)
                .MeasurementCount(Measurements)
                .Run();
        }

        [Test, Performance]
        public void Bench_D_NewLabelAndAddToRoot()
        {
            Measure.Method(() =>
            {
                var root = new VisualElement();
                for (var i = 0; i < Count; i++)
                {
                    var l = new Label();
                    root.Add(l);
                }
            })
                .GC()
                .WarmupCount(WarmUp)
                .MeasurementCount(Measurements)
                .Run();
        }

        /// <summary>
        /// Hand-written equivalent benchmark. Reference implementation for the Unity floor; baseline for measuring Velvet overhead.
        /// </summary>
        [Test, Performance]
        public void Bench_E_FullPipelineParity()
        {
            Measure.Method(() =>
            {
                var root = new VisualElement();
                for (var i = 0; i < Count; i++)
                {
                    var l = new Label();
                    l.text = "item";
                    root.Add(l);
                }
            })
                .GC()
                .WarmupCount(WarmUp)
                .MeasurementCount(Measurements)
                .Run();
        }

        /// <summary>
        /// Path that creates a new Reconciler each iteration. Includes the one-time Reconciler ctor cost.
        /// Reference value, since production assumes a long-lived Reconciler.
        /// </summary>
        [Test, Performance]
        public void Bench_F_VelvetReconcile()
        {
            var nodes = BuildLabelVNodes();

            Measure.Method(() =>
            {
                var r = new Reconciler();
                var root = new VisualElement();
                r.Reconcile(root, Array.Empty<VNode>(), nodes);
                r.Dispose();
            })
                .GC()
                .WarmupCount(WarmUp)
                .MeasurementCount(Measurements)
                .Run();
        }

        /// <summary>
        /// Long-lived Reconciler path. Reflects the production usage pattern (the Reconciler is
        /// long-lived per Page/Component). Demonstrates that Velvet-attributable alloc is 0.
        /// Expected: matches Bench_E (differences within measurement noise).
        /// </summary>
        [Test, Performance]
        public void Bench_G_VelvetReconcileLongLived()
        {
            var nodes = BuildLabelVNodes();

            var reconciler = new Reconciler();
            try
            {
                Measure.Method(() =>
                {
                    var root = new VisualElement();
                    reconciler.Reconcile(root, Array.Empty<VNode>(), nodes);
                })
                    .GC()
                    .WarmupCount(WarmUp)
                    .MeasurementCount(Measurements)
                    .Run();
            }
            finally
            {
                reconciler.Dispose();
            }
        }

        private static VNode[] BuildLabelVNodes()
        {
            var nodes = new VNode[Count];
            for (var i = 0; i < Count; i++)
            {
                nodes[i] = V.Label(text: "item");
            }
            return nodes;
        }
    }
}
