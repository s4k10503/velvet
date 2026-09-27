#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a runtime animate-label flip on a mounted Motion tweens a pose written as inline style —
    /// translate, which has no USS form — on a real runtime panel, on the transition of the pose it flips
    /// into: when nothing was animating before it, when it lands in the frame a near-instant flip did, and
    /// in a staggered child's own slot when the host renders again before that slot comes round.
    /// </summary>
    internal sealed class MotionInlinePoseSwapPlaybackTests
    {
        private static readonly Dictionary<string, MotionVariant> s_slide = new()
        {
            ["hidden"] = "translate-x-[0px]",
            ["visible"] = "translate-x-[300px]",
            ["cover"] = "translate-x-[0px]",
        };

        private readonly record struct SlideState(string Label, float DurationSec);

        private sealed class SlideStore : Store<SlideState>
        {
            public SlideStore() : base(new SlideState("hidden", 0.4f)) { }
            public void Set(string label, float durationSec) => SetState(_ => new SlideState(label, durationSec));
            protected override void ResetCore() => SetState(_ => new SlideState("hidden", 0.4f));
        }

        private static SlideStore s_store;
        private static StateUpdater<int> s_setTick;

        private GameObject _go;
        private PanelSettings _settings;
        private MountedTree _mounted;
        private SlideStore _store;
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            s_store = null;
            s_setTick = default;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _frameRateScope.Dispose();
            _mounted?.Dispose();
            _mounted = null;
            _store?.Dispose();
            _store = null;
            if (_go != null) Object.Destroy(_go);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        private static StyleTransitionConfig Linear(float seconds)
            => new() { DurationSec = seconds, Easing = EasingMode.Linear };

        [Component]
        private static VNode SlideHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Div(name: "wrap", children: new VNode[]
            {
                V.Motion(key: "m", name: "m", className: "absolute w-[50px] h-[50px]", variants: s_slide,
                    animate: state.Label, transition: Linear(state.DurationSec)),
            });
        }

        // Two children inheriting the coordinator's label, the second a 0.3s stagger slot behind the first,
        // plus a counter nothing reads, to render the host again without moving the label.
        [Component]
        private static VNode StaggerHost()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var state = Hooks.UseStore(s_store, s => s);
            return V.Motion(key: "coordinator", animate: state.Label,
                transition: new StyleTransitionConfig { StaggerChildrenSec = 0.3f },
                children: new VNode[]
                {
                    V.Motion(key: "c0", name: "c0", className: "absolute w-[50px] h-[50px]", variants: s_slide,
                        transition: Linear(0.2f)),
                    V.Motion(key: "c1", name: "c1", className: "absolute w-[50px] h-[50px]", variants: s_slide,
                        transition: Linear(0.2f)),
                });
        }

        private VisualElement CreateRuntimePanel()
        {
            _go = new GameObject("MotionInlinePoseSwapPlayback");
            var doc = _go.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            _store = new SlideStore();
            s_store = _store;
            return doc.rootVisualElement;
        }

        private static bool IsBetween(float x) => x > 15f && x < 285f;

        [UnityTest]
        public IEnumerator Given_ARuntimeFlipBetweenTranslatePoses_When_FramesAdvance_Then_TranslatePassesThroughAnIntermediateValue()
        {
            // Arrange — the Motion rests at the hidden pose with no transition on it.
            var root = CreateRuntimePanel();
            yield return null;
            _mounted = V.Mount(root, V.Component(SlideHost, key: "root"));
            var m = root.Q<VisualElement>("m");
            yield return null;

            // Act
            _store.Set("visible", 0.4f);
            var sawIntermediate = false;
            var deadline = Time.realtimeSinceStartupAsDouble + 1.0;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                sawIntermediate |= IsBetween(m.resolvedStyle.translate.x);
                yield return null;
            }

            // Assert
            Assert.That(sawIntermediate, Is.True);
        }

        [UnityTest]
        public IEnumerator Given_ANearInstantFlipAndATweenFlipRenderedInOneFrame_When_FramesAdvance_Then_TranslatePassesThroughAnIntermediateValue()
        {
            // Arrange
            var root = CreateRuntimePanel();
            yield return null;
            _mounted = V.Mount(root, V.Component(SlideHost, key: "root"));
            var m = root.Q<VisualElement>("m");
            yield return null;

            // Act — the second flip renders before the first one's swap, so it interrupts it.
            _store.Set("visible", 0.001f);
            _mounted.FlushStateForTest();
            _store.Set("cover", 0.3f);
            _mounted.FlushStateForTest();
            var sawIntermediate = false;
            var deadline = Time.realtimeSinceStartupAsDouble + 1.0;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                sawIntermediate |= IsBetween(m.resolvedStyle.translate.x);
                yield return null;
            }

            // Assert
            Assert.That(sawIntermediate, Is.True);
        }

        [UnityTest]
        public IEnumerator Given_StaggeredChildrenWithTranslatePoses_When_TheHostRendersAgainRightAfterTheFlip_Then_TheSecondChildIsStillAtRestWhileTheFirstIsPastHalfway()
        {
            // Arrange
            var root = CreateRuntimePanel();
            yield return null;
            _mounted = V.Mount(root, V.Component(StaggerHost, key: "root"));
            var first = root.Q<VisualElement>("c0");
            var second = root.Q<VisualElement>("c1");
            yield return null;

            // Act — the second render lands in the flip's frame, well inside the second child's slot.
            _store.Set("visible", 0.4f);
            _mounted.FlushStateForTest();
            s_setTick.Invoke(t => t + 1);
            _mounted.FlushStateForTest();
            var sawFirstAhead = false;
            var deadline = Time.realtimeSinceStartupAsDouble + 1.0;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                sawFirstAhead |= first.resolvedStyle.translate.x > 150f && second.resolvedStyle.translate.x < 1f;
                yield return null;
            }

            // Assert
            Assert.That(sawFirstAhead, Is.True);
        }
    }
}
#endif
