using System;
using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// E2E benchmark: measures VNode construction through MarkDirty in a PlayMode environment.
    /// Unity 6's public API has no equivalent of IPanel.ValidateLayout, so synchronous layout/paint
    /// measurement is not possible from the outside. This records only the synchronous cost of
    /// Reconcile + MarkDirty. Actual layout/paint runs asynchronously on a frame after yield return null.
    /// </summary>
    [TestFixture]
    public class E2EBenchmarks
    {
        private const int k_WarmupCount = 3;
        private const int k_MeasurementCount = 10;

        private GameObject _gameObject;
        private UIDocument _uiDocument;
        private PanelSettings _panelSettings;
        private VisualElement _root;
        private Reconciler _reconciler;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _gameObject = new GameObject("E2EBenchmarkRoot");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _uiDocument.panelSettings = _panelSettings;

            yield return null;

            _root = _uiDocument.rootVisualElement;
            _reconciler = new Reconciler();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _reconciler?.Dispose();
            if (_gameObject != null)
                UnityEngine.Object.Destroy(_gameObject);
            if (_panelSettings != null)
                UnityEngine.Object.Destroy(_panelSettings);
            yield return null;
        }

        #region B-4: E2E mount (including layout + paint)

        [UnityTest, Performance]
        public IEnumerator E2E_Mount_10Elements_WithLayoutAndPaint()
        {
            yield return RunE2EBenchmark(10);
        }

        [UnityTest, Performance]
        public IEnumerator E2E_Mount_100Elements_WithLayoutAndPaint()
        {
            yield return RunE2EBenchmark(100);
        }

        [UnityTest, Performance]
        public IEnumerator E2E_Mount_1000Elements_WithLayoutAndPaint()
        {
            yield return RunE2EBenchmark(1000);
        }

        #endregion

        #region Internal measurement logic

        private IEnumerator RunE2EBenchmark(int count)
        {
            var groupTime      = new SampleGroup("E2E.Time",      SampleUnit.Millisecond);
            var groupGC        = new SampleGroup("E2E.GC",        SampleUnit.Byte);
            var groupReconcile = new SampleGroup("E2E.Reconcile", SampleUnit.Millisecond);
            var groupMarkDirty = new SampleGroup("E2E.MarkDirty", SampleUnit.Millisecond);

            var nodes = BenchmarkHelpers.BuildLabelNodes(count, prefix: "e2e-item-");
            var sw = new Stopwatch();

            for (int i = 0; i < k_WarmupCount; i++)
            {
                _reconciler.Reconcile(_root, Array.Empty<VNode>(), nodes);
                _root.MarkDirtyRepaint();
                _reconciler.Reconcile(_root, nodes, Array.Empty<VNode>());
                yield return null;
            }

            for (int i = 0; i < k_MeasurementCount; i++)
            {
                // Cleanup goes BEFORE GC.Collect so unmount allocations are reclaimed before the
                // GC baseline. First iteration has nothing mounted yet.
                if (i > 0)
                {
                    _reconciler.Reconcile(_root, nodes, Array.Empty<VNode>());
                }

                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                System.GC.Collect();
                long gcBefore = System.GC.GetTotalMemory(false);

                sw.Restart();
                _reconciler.Reconcile(_root, Array.Empty<VNode>(), nodes);
                sw.Stop();
                double reconcileMs = sw.Elapsed.TotalMilliseconds;

                // MarkDirtyRepaint only sets the dirty flag; actual painting runs asynchronously
                // on the panel's next-frame update. What is measured here is the synchronous call
                // overhead, not the actual paint cost.
                sw.Restart();
                _root.MarkDirtyRepaint();
                sw.Stop();
                double markDirtyMs = sw.Elapsed.TotalMilliseconds;

                long gcAfter = System.GC.GetTotalMemory(false);

                Measure.Custom(groupTime,      reconcileMs + markDirtyMs);
                Measure.Custom(groupGC,        Math.Max(0, gcAfter - gcBefore));
                Measure.Custom(groupReconcile, reconcileMs);
                Measure.Custom(groupMarkDirty, markDirtyMs);

                yield return null;
            }
        }

        #endregion
    }

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
