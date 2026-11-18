using System;
using System.Collections;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Frames in which a class change at the root restyles a mounted tree of 1,000 Divs, on a live runtime panel carrying
    /// Velvet's utility stylesheet. Each sample is one such frame: the time the panel recorded under the markers
    /// <see cref="PanelFrameRecorder"/> reads, and the GC.Alloc blocks every thread charged in it.
    /// <para>
    /// Two arms over the same elements in one run, so the difference between them does not depend on the machine: in
    /// the first every Div carries <see cref="CornerRadiusFit.WatchClass"/>, as the factory builds it, and in the second
    /// the class is taken off each one after the mount. A case throws when the arm's elements do not hold what the arm
    /// says, or when a frame ran none of a phase.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class RadiusWatchRestyleBenchmarks
    {
        private const string PanelName = "VelvetRadiusWatchRestyleBenchmarkPanel";
        private const string Toggled = "opacity-90";
        private const int Divs = 1000;
        private const int SettleFrames = 5;
        private const int FramesPerSecond = 25;

        private static readonly int WarmupFrames = BenchmarkRun.Warmup(10);
        private static readonly int MeasuredFrames = BenchmarkRun.Measurements(60);

        private GameObject _gameObject = null!;
        private PanelSettings _panelSettings = null!;
        private VisualElement _root = null!;
        private MountedTree _mounted = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _gameObject = new GameObject("RadiusWatchRestyleBenchmarkRoot");
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

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green.
        [UnityTest, Performance]
        public IEnumerator RestyleFrames_1000DivsCarryingTheWatchClass() => Run(watched: true);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green.
        [UnityTest, Performance]
        public IEnumerator RestyleFrames_1000DivsWithoutTheWatchClass() => Run(watched: false);

        private IEnumerator Run(bool watched)
        {
            var children = new VNode[Divs];
            for (var i = 0; i < Divs; i++)
            {
                children[i] = V.Div(className: "w-[10px] h-[10px]", key: "div-" + i);
            }
            _mounted = V.Mount(_root, V.Div("flex flex-col", children));
            _mounted.FlushStateForTest();
            var top = _root[0];
            if (!watched)
            {
                top.RemoveFromClassList(CornerRadiusFit.WatchClass);
                for (var i = 0; i < top.childCount; i++)
                {
                    top[i].RemoveFromClassList(CornerRadiusFit.WatchClass);
                }
            }
            for (var i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }
            RequireArm(top, watched);

            var phases = new SampleGroup[PanelFrameRecorder.Phases.Length];
            for (var i = 0; i < phases.Length; i++)
            {
                phases[i] = new SampleGroup("Frame." + PanelFrameRecorder.Phases[i], SampleUnit.Millisecond);
            }
            var allocation = new SampleGroup("Frame.GC", SampleUnit.Undefined);
            var frame = new PanelFrameRecorder(PanelName);

            using (new TargetFrameRateScope(FramesPerSecond))
            {
                frame.Start();
                // The frame Start ran in is only partly recorded.
                yield return null;
                for (var i = 0; i < WarmupFrames + MeasuredFrames; i++)
                {
                    top.ToggleInClassList(Toggled);
                    yield return null;
                    frame.Capture();
                    if (!frame.RanEveryPhase)
                    {
                        frame.Stop();
                        throw new InvalidOperationException($"a restyle frame ran none of a phase of {PanelName}");
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
                }
                frame.Stop();
            }
        }

        private static void RequireArm(VisualElement top, bool watched)
        {
            if (top.childCount != Divs)
            {
                throw new InvalidOperationException($"expected {Divs} mounted Divs, the root holds {top.childCount}");
            }
            RequireWatch(top, watched, "the top Div");
            for (var i = 0; i < top.childCount; i++)
            {
                RequireWatch(top[i], watched, $"Div {i}");
            }
        }

        // The class and the property it brings, so a sheet without the rule leaves the two arms alike and fails the run.
        private static void RequireWatch(VisualElement element, bool watched, string label)
        {
            var resolves = element.customStyle.TryGetValue(new CustomStyleProperty<float>("--velvet-radius-watch"), out _);
            if (element.ClassListContains(CornerRadiusFit.WatchClass) != watched || resolves != watched)
            {
                throw new InvalidOperationException(
                    $"{label} has the class {element.ClassListContains(CornerRadiusFit.WatchClass)} and resolves the property {resolves}, "
                    + $"where this arm says {watched} for both");
            }
        }
    }
}
