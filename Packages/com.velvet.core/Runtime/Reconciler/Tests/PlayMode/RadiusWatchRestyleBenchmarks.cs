using System;
using System.Collections;
using System.Linq;
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
    /// Two cases over a fresh tree of the same shape, which differ in one class token each Div carries: in the first it
    /// names no rule, and in the second it names the rule that declares the radius. In the second every Div matches a rule
    /// that has a derived counterpart with the radius watch property (<c>RadiusWatchSheets</c>), so each gets a style event
    /// in a restyle. A case throws when its Divs do not resolve the property as it says, or when a frame ran none of a
    /// phase. The sheet is an asset loaded through the editor's asset database, so the cases are editor-only and skip in a
    /// player.
    /// </para>
    /// <para>
    /// Two more cases run 1,000 Buttons under the engine's own theme, which the watch sheets cover like any other: in the
    /// first they carry the property, and in the second the watch sheets are taken off after the mount, so the pair is the
    /// cost of the property on built-in controls. They skip where the panel has no engine theme.
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

        // The buttons are matched by the engine theme's radius rules, which are watched like any other. In the second case
        // the watch sheets are taken off the panel's root and the window's after the mount, which leaves the same elements
        // and the same theme without the property.
        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green.
        [UnityTest, Performance]
        public IEnumerator RestyleFrames_1000ButtonsUnderTheEngineTheme() => Run(watched: true, buttons: true);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green.
        [UnityTest, Performance]
        public IEnumerator RestyleFrames_1000ButtonsUnderTheEngineThemeWithoutWatchSheets() => Run(watched: false, buttons: true);

        // The class the sheet below gives a 10px radius, which a 40px Div fits unscaled, and one of the same length that no
        // rule names.
        private const string RadiusClass = "layout-id-test-round";
        private const string NoRuleClass = "layout-id-test-rxxxx";

        private static VNode RenderDivs(bool watched)
        {
            var children = new VNode[Divs];
            for (var i = 0; i < Divs; i++)
            {
                children[i] = V.Div(className: "w-[40px] h-[40px] " + (watched ? RadiusClass : NoRuleClass), key: "div-" + i);
            }
            return V.Div("flex flex-col", children);
        }

        private static VNode RenderButtons()
        {
            var children = new VNode[Divs];
            for (var i = 0; i < Divs; i++)
            {
                children[i] = V.Button(className: "w-[80px] h-[24px]", text: "b", key: "button-" + i);
            }
            return V.Div("flex flex-col", children);
        }

        private IEnumerator Run(bool watched, bool buttons = false)
        {
            if (buttons)
            {
                var theme = Resources.FindObjectsOfTypeAll<ThemeStyleSheet>().FirstOrDefault(t => t.name.Contains("Default"));
                if (theme == null)
                {
                    Assert.Ignore("the panel has no engine theme to read");
                }
                _panelSettings.themeStyleSheet = theme;
                yield return null;
            }
#if UNITY_EDITOR
            _root.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdTestRules.uss"));
#else
            Assert.Ignore("the radius rule is an asset the editor loads");
#endif
            _mounted = V.Mount(_root, buttons ? RenderButtons() : RenderDivs(watched));
            _mounted.FlushStateForTest();
            for (var i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }
            if (buttons && !watched)
            {
                TakeOffWatchSheets(_root.panel.visualTree);
                TakeOffWatchSheets(_root);
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

        private static void TakeOffWatchSheets(VisualElement element)
        {
            for (var i = element.styleSheets.count - 1; i >= 0; i--)
            {
                if (RadiusWatchSheets.IsWatch(element.styleSheets[i]))
                {
                    element.styleSheets.Remove(element.styleSheets[i]);
                }
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
                var resolves = top[i].customStyle.TryGetValue(new CustomStyleProperty<float>(RadiusWatchSheets.Property), out _);
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
