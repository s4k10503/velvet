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
}
