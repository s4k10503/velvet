using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Benchmarks the text-effect pass for an element whose only effect is the line height a
    /// <c>text-&lt;size&gt;</c> class pairs with its size, the case every labelled text now falls into. Each
    /// shape runs against an inert-class control with the same elements, so the difference between the two
    /// is the size's effect.
    /// <para>
    /// Labels carry the class themselves in one shape; in the other a container carries it over classless
    /// labels, which is the path that walks the container's descendants on every patch. Each round
    /// reconciles between two trees whose texts differ, so every label's text is patched and re-applied.
    /// </para>
    /// </summary>
    internal sealed class TextSizeEffectReconcileBenchmarks
    {
        private const int k_Rows = 200;
        private static readonly int k_WarmupCount = BenchmarkRun.Warmup(5);
        private static readonly int k_MeasurementCount = BenchmarkRun.Measurements(20);

        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown() => _reconciler.Dispose();

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing, so the base runs it green.
        [Test, Performance]
        public void Reconcile_RetextLabels_200LabelsWithATextSize() => RunLabels("text-sm");

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing, so the base runs it green.
        [Test, Performance]
        public void Reconcile_RetextLabels_200LabelsWithAnInertClass() => RunLabels("p-1");

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing, so the base runs it green.
        [Test, Performance]
        public void Reconcile_RetextLabels_200LabelsUnderATextSizeContainer() => RunContainer("text-sm");

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing, so the base runs it green.
        [Test, Performance]
        public void Reconcile_RetextLabels_200LabelsUnderAnInertContainer() => RunContainer("p-1");

        private void RunLabels(string labelClass) =>
            Run(BuildLabels(labelClass, "a"), BuildLabels(labelClass, "b"));

        private void RunContainer(string containerClass) =>
            Run(BuildContainer(containerClass, "a"), BuildContainer(containerClass, "b"));

        private void Run(VNode[] first, VNode[] second)
        {
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), first);

            Measure.Method(() =>
                {
                    _reconciler.Reconcile(_root, first, second);
                    _reconciler.Reconcile(_root, second, first);
                })
                .GC()
                .WarmupCount(k_WarmupCount)
                .MeasurementCount(k_MeasurementCount)
                .Run();
        }

        private static VNode[] BuildLabels(string labelClass, string text)
        {
            var rows = new VNode[k_Rows];
            for (var i = 0; i < k_Rows; i++)
            {
                rows[i] = V.Label(className: labelClass, text: text + i);
            }
            return rows;
        }

        private static VNode[] BuildContainer(string containerClass, string text) =>
            new VNode[] { V.Div(className: containerClass, children: BuildLabels(string.Empty, text)) };
    }
}
