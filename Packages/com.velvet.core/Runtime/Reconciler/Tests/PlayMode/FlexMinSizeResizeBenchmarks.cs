using System;
using System.Collections;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Frames in which one flex container holding <see cref="Labels"/> text labels changes its width, on a live
    /// runtime panel carrying Velvet's utility stylesheet. Each sample is one such frame: the time the panel
    /// recorded under the markers <see cref="PanelFrameRecorder"/> reads, the GC.Alloc blocks every thread charged
    /// in it, and the number of times <see cref="StyleFlexMinSizeManipulator"/> derived in it, read off the
    /// profiler marker its derive runs under. Frames are paced as <see cref="IdleFrameBenchmarks"/> paces its own.
    /// <para>
    /// Two arms over the same labels: in a row they lie one after another along the container's width, in a column
    /// they stack and a resize changes the width each wraps its text at. A case throws when the container is not
    /// laid out by the sheet or its first or last label carries no automatic minimum on its parent's main axis.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class FlexMinSizeResizeBenchmarks
    {
        private const string PanelName = "VelvetFlexMinSizeResizeBenchmarkPanel";
        private const string ContainerName = "flex-min-container";
        private const string DeriveMarker = "Velvet.FlexMinSize.Derive";
        private const int Labels = 500;
        private const int SettleFrames = 5;
        private const int FramesPerSecond = 25;
        private const float NarrowWidth = 600f;
        private const float WideWidth = 640f;

        private static readonly int WarmupFrames = BenchmarkRun.Warmup(10);
        private static readonly int MeasuredFrames = BenchmarkRun.Measurements(60);

        private GameObject _gameObject = null!;
        private PanelSettings _panelSettings = null!;
        private VisualElement _root = null!;
        private MountedTree _mounted = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _gameObject = new GameObject("FlexMinSizeResizeBenchmarkRoot");
            var document = _gameObject.AddComponent<UIDocument>();
            _panelSettings = TestPanelSettings.Create();
            // As E2EBenchmarks names its panel, for the same reason.
            _panelSettings.name = PanelName;
            document.panelSettings = _panelSettings;
            yield return null;
            _root = document.rootVisualElement;
            BenchmarkPanelRoot.Prepare(_root);
            VelvetStyleUtilities.AttachTo(_root);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _mounted?.Dispose();
            if (_gameObject != null)
                UnityEngine.Object.Destroy(_gameObject);
            if (_panelSettings != null)
                UnityEngine.Object.Destroy(_panelSettings);
            yield return null;
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [UnityTest, Performance]
        public IEnumerator ResizeFrames_RowOf500Labels() => Run(horizontal: true);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [UnityTest, Performance]
        public IEnumerator ResizeFrames_ColumnOf500Labels() => Run(horizontal: false);

        private IEnumerator Run(bool horizontal)
        {
            var labels = new VNode?[Labels];
            for (var i = 0; i < Labels; i++)
            {
                labels[i] = V.Label(text: "item " + i + " label text");
            }
            _mounted = V.Mount(_root, V.Div(
                className: horizontal ? "flex flex-row" : "flex flex-col", name: ContainerName, children: labels));
            _mounted.FlushStateForTest();
            var container = _root.Q(ContainerName);
            container.style.width = NarrowWidth;

            // The frames that lay the mount out and paint it first, which are not resize ones.
            for (var i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }
            RequireMinimumsWritten(container, horizontal);

            var phases = new SampleGroup[PanelFrameRecorder.Phases.Length];
            for (var i = 0; i < phases.Length; i++)
            {
                phases[i] = new SampleGroup("Frame." + PanelFrameRecorder.Phases[i], SampleUnit.Millisecond);
            }
            var allocation = new SampleGroup("Frame.GC", SampleUnit.Undefined);
            var derives = new SampleGroup("Frame.Derive", SampleUnit.Undefined);
            var frame = new PanelFrameRecorder(PanelName);
            var derive = Recorder.Get(DeriveMarker);
            if (!derive.isValid)
            {
                throw new InvalidOperationException($"no profiler marker is named '{DeriveMarker}'");
            }

            using (new TargetFrameRateScope(FramesPerSecond))
            {
                frame.Start();
                derive.enabled = false;
                derive.enabled = true;
                // The frame Start ran in is only partly recorded.
                yield return null;
                for (var i = 0; i < WarmupFrames + MeasuredFrames; i++)
                {
                    container.style.width = i % 2 == 0 ? WideWidth : NarrowWidth;
                    yield return null;
                    frame.Capture();
                    if (!frame.RanEveryPhase)
                    {
                        frame.Stop();
                        derive.enabled = false;
                        throw new InvalidOperationException($"a resize frame ran none of a phase of {PanelName}");
                    }
                    if (i < WarmupFrames)
                    {
                        continue;
                    }
                    for (var p = 0; p < phases.Length; p++)
                    {
                        Measure.Custom(phases[p], frame.Milliseconds(p));
                    }
                    Measure.Custom(allocation, frame.AllocationBlocks);
                    Measure.Custom(derives, derive.sampleBlockCount);
                }
                frame.Stop();
                derive.enabled = false;
            }

            var mounted = container.childCount;
            if (mounted != Labels)
            {
                throw new InvalidOperationException($"expected {Labels} labels, the container holds {mounted}");
            }
        }

        // A container laid out as a column is a panel the sheet never reached, and a label with no minimum is one
        // the manipulator never reached, so neither frame would measure the work it is here to.
        private static void RequireMinimumsWritten(VisualElement container, bool horizontal)
        {
            var expected = horizontal ? FlexDirection.Row : FlexDirection.Column;
            if (container.resolvedStyle.flexDirection != expected)
            {
                throw new InvalidOperationException($"the container resolved {container.resolvedStyle.flexDirection}");
            }
            foreach (var label in new[] { container[0], container[Labels - 1] })
            {
                var minimum = horizontal ? label.style.minWidth : label.style.minHeight;
                if (!BenchmarkPanelRoot.LaidOut(label) || minimum.keyword == StyleKeyword.Null)
                {
                    throw new InvalidOperationException(
                        $"a label laid out at {label.layout} carries no automatic minimum on its parent's main axis");
                }
            }
        }
    }
}
