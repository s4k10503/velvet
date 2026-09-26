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
    /// Pins that a Motion's mount enter, presence enter and presence exit move a pose written as inline style —
    /// translate, which has no USS form — on a real runtime panel: the enter shows <c>initial</c> and tweens to
    /// <c>animate</c>, the exit tweens to <c>exit</c> before removal, and an exit interrupted by the key coming
    /// back, or completed just before it does, leaves the element resting at <c>animate</c>, and a child removed
    /// before its enter swaps exits from <c>animate</c>, where the enter's cancel leaves its USS classes.
    /// </summary>
    internal sealed class MotionInlinePoseEnterExitPlaybackTests
    {
        // Away and gone sit on opposite sides of cover, so a sample's value alone says which play it came from.
        private static readonly Dictionary<string, MotionVariant> s_poses = new()
        {
            ["away"] = "translate-x-[300px]",
            ["cover"] = "translate-x-[0px]",
            ["gone"] = "translate-x-[-300px]",
        };

        private readonly record struct ShownState(bool Shown);

        private sealed class ShownStore : Store<ShownState>
        {
            public ShownStore(bool shown) : base(new ShownState(shown)) { }
            public void Set(bool shown) => SetState(_ => new ShownState(shown));
            protected override void ResetCore() => SetState(_ => new ShownState(false));
        }

        private static ShownStore s_store;
        private static bool s_declaresInitial;
        private static float s_delayChildrenSec;
        private static System.Action s_onExitComplete;

        private GameObject _go;
        private PanelSettings _settings;
        private MountedTree _mounted;
        private ShownStore _store;
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            s_store = null;
            s_declaresInitial = true;
            s_delayChildrenSec = 0f;
            s_onExitComplete = null;
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
        private static VNode MountEnterHost()
        {
            return V.Div(name: "wrap", children: new VNode[]
            {
                V.Motion(key: "m", name: "m", className: "absolute w-[50px] h-[50px]", variants: s_poses,
                    initial: "away", animate: "cover", transition: Linear(0.3f)),
            });
        }

        [Component]
        private static VNode PresenceHost()
        {
            var shown = Hooks.UseStore(s_store, s => s.Shown);
            return V.Div(name: "wrap", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", delayChildrenSec: s_delayChildrenSec,
                    onExitComplete: () => s_onExitComplete?.Invoke(),
                    children: new VNode[]
                    {
                        shown
                            ? V.Motion(key: "m", name: "m", className: "absolute w-[50px] h-[50px]",
                                variants: s_poses, initial: s_declaresInitial ? "away" : null, animate: "cover",
                                exit: "gone",
                                transition: Linear(0.3f))
                            : null,
                    }),
            });
        }

        private VisualElement CreateRuntimePanel(bool shown)
        {
            _go = new GameObject("MotionInlinePoseEnterExitPlayback");
            var doc = _go.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            _store = new ShownStore(shown);
            s_store = _store;
            return doc.rootVisualElement;
        }

        // Records the element's translate.x once a frame while it stays attached, for the given realtime.
        private static IEnumerator Sample(VisualElement element, double seconds, List<float> samples)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                if (element.panel != null)
                {
                    samples.Add(element.resolvedStyle.translate.x);
                }
                yield return null;
            }
        }

        private static bool AnyBetween(List<float> samples, float low, float high)
            => samples.Exists(x => x > low + 15f && x < high - 15f);

        [UnityTest]
        public IEnumerator Given_AStandaloneMotionWithTranslatePoses_When_ItMounts_Then_ItPassesThroughAnIntermediateTranslate()
        {
            // Arrange
            var root = CreateRuntimePanel(shown: false);
            yield return null;
            var samples = new List<float>();

            // Act
            _mounted = V.Mount(root, V.Component(MountEnterHost, key: "root"));
            yield return Sample(root.Q<VisualElement>("m"), 0.8, samples);

            // Assert
            Assert.That(AnyBetween(samples, 0f, 300f), Is.True, string.Join(", ", samples));
        }

        [UnityTest]
        public IEnumerator Given_APresenceChildWithTranslatePoses_When_ItIsAdded_Then_ItPassesThroughAnIntermediateTranslate()
        {
            // Arrange
            var root = CreateRuntimePanel(shown: false);
            yield return null;
            _mounted = V.Mount(root, V.Component(PresenceHost, key: "root"));
            yield return null;
            var samples = new List<float>();

            // Act
            _store.Set(true);
            _mounted.FlushStateForTest();
            yield return Sample(root.Q<VisualElement>("m"), 0.8, samples);

            // Assert
            Assert.That(AnyBetween(samples, 0f, 300f), Is.True, string.Join(", ", samples));
        }

        [UnityTest]
        public IEnumerator Given_APresenceChildWithTranslatePoses_When_ItIsRemoved_Then_ItPassesThroughAnIntermediateTranslateBeforeRemoval()
        {
            // Arrange
            var root = CreateRuntimePanel(shown: true);
            yield return null;
            _mounted = V.Mount(root, V.Component(PresenceHost, key: "root"));
            var motion = root.Q<VisualElement>("m");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.6);
            var samples = new List<float>();

            // Act
            _store.Set(false);
            yield return Sample(motion, 0.8, samples);

            // Assert
            Assert.That(AnyBetween(samples, -300f, 0f), Is.True, string.Join(", ", samples));
        }

        [UnityTest]
        public IEnumerator Given_APresenceChildAddedWithTranslatePoses_When_ItIsRemovedBeforeItsEnterSwaps_Then_ItExitsFromAnimateWithoutShowingInitial()
        {
            // Arrange — delayChildrenSec holds the enter's swap back, and the exit's with it, by 0.3s.
            s_delayChildrenSec = 0.3f;
            var root = CreateRuntimePanel(shown: false);
            yield return null;
            _mounted = V.Mount(root, V.Component(PresenceHost, key: "root"));
            yield return null;
            _store.Set(true);
            _mounted.FlushStateForTest();
            var motion = root.Q<VisualElement>("m");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.1);
            var samples = new List<float>();

            // Act — the enter is cancelled before its swap.
            _store.Set(false);
            _mounted.FlushStateForTest();
            yield return Sample(motion, 1.0, samples);

            // Assert
            Assert.That((AnyBetween(samples, -300f, 0f), samples.Exists(x => x > 15f)), Is.EqualTo((true, false)),
                string.Join(", ", samples));
        }

        [UnityTest]
        public IEnumerator Given_APresenceChildExitingBetweenTranslatePoses_When_ItIsAddedBackMidExit_Then_ItHadMovedAndReturnsToRest()
        {
            // Arrange
            var root = CreateRuntimePanel(shown: true);
            yield return null;
            _mounted = V.Mount(root, V.Component(PresenceHost, key: "root"));
            var motion = root.Q<VisualElement>("m");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.6);
            var exitSamples = new List<float>();
            _store.Set(false);
            yield return Sample(motion, 0.15, exitSamples);

            // Act
            _store.Set(true);
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.8);

            // Assert
            Assert.That((AnyBetween(exitSamples, -300f, 0f), motion.resolvedStyle.translate.x),
                Is.EqualTo((true, 0f)), string.Join(", ", exitSamples));
        }

        [UnityTest]
        public IEnumerator Given_APresenceChildExitingBetweenTranslatePoses_When_ItIsAddedBackAsTheExitCompletes_Then_ItHadMovedAndRestsAtAnimate()
        {
            // Arrange — the key comes back from onExitComplete, before the render that would drop the ghost.
            // No initial, so the re-entry plays no enter that would write the resting pose on its own.
            s_declaresInitial = false;
            var root = CreateRuntimePanel(shown: true);
            yield return null;
            _mounted = V.Mount(root, V.Component(PresenceHost, key: "root"));
            var motion = root.Q<VisualElement>("m");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.6);
            s_onExitComplete = () => _store.Set(true);
            var exitSamples = new List<float>();

            // Act
            _store.Set(false);
            yield return Sample(motion, 0.8, exitSamples);
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.3);

            // Assert
            Assert.That((AnyBetween(exitSamples, -300f, 0f), root.Q<VisualElement>("m")?.resolvedStyle.translate.x),
                Is.EqualTo((true, (float?)0f)), string.Join(", ", exitSamples));
        }
    }
}
#endif
