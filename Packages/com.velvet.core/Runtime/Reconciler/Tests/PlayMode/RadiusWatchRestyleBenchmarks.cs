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
    /// the first no Div has had a class of its own added or removed since it was built, which is the state of an
    /// element before the radius fit watches style passes, and in the second each Div has, so it carries
    /// <see cref="CornerRadiusFit.WatchClass"/>. A case throws when the arm's elements do not hold what the arm says,
    /// or when a frame ran none of a phase.
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
        private static StateUpdater<bool> s_setOwnClass;

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
        public IEnumerator RestyleFrames_1000DivsAfterAClassOfTheirOwnChanged() => Run(watched: true);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green.
        [UnityTest, Performance]
        public IEnumerator RestyleFrames_1000DivsNeverClassChanged() => Run(watched: false);

        [Component]
        private static VNode RenderDivs()
        {
            var (own, setOwn) = Hooks.UseState(false);
            s_setOwnClass = setOwn;
            var children = new VNode[Divs];
            for (var i = 0; i < Divs; i++)
            {
                children[i] = V.Div(className: own ? "w-[10px] h-[10px] own-class" : "w-[10px] h-[10px]", key: "div-" + i);
            }
            return V.Div("flex flex-col", children);
        }

        private IEnumerator Run(bool watched)
        {
            _mounted = V.Mount(_root, V.Component(RenderDivs));
            _mounted.FlushStateForTest();
            for (var i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }
            var top = _root[0];
            if (watched)
            {
                s_setOwnClass.Invoke(true);
                _mounted.FlushStateForTest();
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
            RequireWatch(top, false, "the top Div");
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
