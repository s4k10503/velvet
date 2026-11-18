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
    /// Frames in which a class is toggled at the root of a mounted tree of 1,000 Divs, on a live runtime panel carrying
    /// Velvet's utility stylesheet and a stylesheet of its own with a rule that declares a border radius. Each sample is one
    /// such frame: the time the panel recorded under the markers <see cref="PanelFrameRecorder"/> reads, and the GC.Alloc
    /// blocks every thread charged in it.
    /// <para>
    /// Two cases, each over a fresh tree of the same shape. In the first no Div matches the radius rule, which is what an
    /// element costs without the radius fit's property. In the second every Div does, so each carries the property
    /// (<c>RadiusWatchSheets</c>) and gets a style event in each restyle. A case throws when its Divs do not resolve the
    /// property as it says, or when a frame ran none of a phase.
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
        public IEnumerator RestyleFrames_1000DivsUnderARadiusRule() => Run(watched: true);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green.
        [UnityTest, Performance]
        public IEnumerator RestyleFrames_1000DivsUnderNoRadiusRule() => Run(watched: false);

        // The class the sheet below gives a 10px radius, which a 40px Div fits unscaled.
        private const string RadiusClass = "layout-id-test-round";

        private static VNode RenderDivs(bool watched)
        {
            var children = new VNode[Divs];
            for (var i = 0; i < Divs; i++)
            {
                children[i] = V.Div(className: watched ? "w-[40px] h-[40px] " + RadiusClass : "w-[40px] h-[40px]", key: "div-" + i);
            }
            return V.Div("flex flex-col", children);
        }

        private IEnumerator Run(bool watched)
        {
#if UNITY_EDITOR
            _root.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdTestRules.uss"));
#endif
            _mounted = V.Mount(_root, RenderDivs(watched));
            _mounted.FlushStateForTest();
            for (var i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }
            var top = _root[0];
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
            for (var i = 0; i < top.childCount; i++)
            {
                var resolves = top[i].customStyle.TryGetValue(new CustomStyleProperty<string>("--velvet-radius-watch"), out _);
                if (resolves != watched)
                {
                    throw new InvalidOperationException(
                        $"Div {i} resolves the watch property {resolves}, where this arm says {watched}: the radius rule did "
                        + "not reach it, or the property was not written into it");
                }
            }
        }
    }
}
