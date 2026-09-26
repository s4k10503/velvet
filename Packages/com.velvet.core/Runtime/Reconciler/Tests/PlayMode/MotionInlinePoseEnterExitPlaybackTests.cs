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
    /// back, or completed just before it does, leaves the element resting at the re-added node's values, and a
    /// child removed before its enter swaps leaves <c>initial</c> on the exit's transition, as its USS classes do.
    /// </summary>
    internal sealed class MotionInlinePoseEnterExitPlaybackTests
    {
        // Away and gone sit on opposite sides of cover, so a sample's value alone says which play it came from.
        private static readonly Dictionary<string, MotionVariant> s_poses = new()
        {
            ["away"] = "translate-x-[300px]",
            ["cover"] = "translate-x-[0px]",
            ["gone"] = "translate-x-[-300px]",
            ["cover2"] = "translate-x-[100px]",
            // Two resting poses differing in a USS utility; _effects.uss declares opacity-100 after opacity-50,
            // so an element carrying both resolves 1.
            ["lit"] = "opacity-100 translate-x-[0px]",
            ["dim"] = "opacity-50 translate-x-[0px]",
            // Initial names y alone and the resting and exit poses x alone, so y's resting value is the one no
            // pose names; the exit springs.
            ["awayY"] = "translate-y-[40px]",
            ["goneSpring"] = new MotionVariant("translate-x-[-300px]",
                new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 300f, Damping = 30f }),
        };

        private const string Box = "absolute w-[50px] h-[50px]";

        private readonly record struct ShownState(bool Shown, string ClassName, string Animate);

        private sealed class ShownStore : Store<ShownState>
        {
            public ShownStore(bool shown) : base(new ShownState(shown, Box, "cover")) { }
            public void Set(bool shown) => SetState(s => s with { Shown = shown });
            public void Set(bool shown, string className, string animate)
                => SetState(_ => new ShownState(shown, className, animate));
            protected override void ResetCore() => SetState(_ => new ShownState(false, Box, "cover"));
        }

        private static ShownStore s_store;
        private static bool s_declaresInitial;
        private static bool s_declaresExit;
        private static string s_initialLabel;
        private static string s_exitLabel;
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
            s_declaresExit = true;
            s_initialLabel = "away";
            s_exitLabel = "gone";
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
            var state = Hooks.UseStore(s_store, s => s);
            return V.Div(name: "wrap", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", delayChildrenSec: s_delayChildrenSec,
                    onExitComplete: () => s_onExitComplete?.Invoke(),
                    children: new VNode[]
                    {
                        state.Shown
                            ? V.Motion(key: "m", name: "m", className: state.ClassName,
                                variants: s_poses, initial: s_declaresInitial ? s_initialLabel : null,
                                animate: state.Animate, exit: s_declaresExit ? s_exitLabel : null,
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

        // Waits until the element has visibly moved toward the exit pose, or for a second when it never does.
        private static IEnumerator WaitUntilExitMoved(VisualElement element, List<float> samples)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 1.0;
            while (Time.realtimeSinceStartupAsDouble < deadline && !samples.Exists(x => x < -15f))
            {
                yield return null;
                samples.Add(element.resolvedStyle.translate.x);
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
        public IEnumerator Given_APresenceChildAddedWithTranslatePoses_When_ItIsRemovedBeforeItsEnterSwaps_Then_ItTweensFromInitialOnTheExitsTransition()
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
            var beforeExitSwap = new List<float>();
            var samples = new List<float>();

            // Act — the enter is cancelled before its swap; the first 0.2s fall before the exit's swap.
            _store.Set(false);
            _mounted.FlushStateForTest();
            yield return Sample(motion, 0.2, beforeExitSwap);
            yield return Sample(motion, 1.0, samples);

            // Assert — it leaves initial on the exit's transition, as the enter's USS classes do, rather than
            // jumping to animate, and the exit then moves it.
            Assert.That((AnyBetween(beforeExitSwap, 0f, 300f), AnyBetween(samples, -300f, 0f)),
                Is.EqualTo((true, true)), string.Join(", ", beforeExitSwap) + " | " + string.Join(", ", samples));
        }

        // GREEN_ON_BASE(characterization): the base's enter never writes initial's translate, so the element
        // sits at animate throughout. Measured red at the commit that landed the enter's pose only for an exit
        // with a pose of its own: the ghost stayed at 300.
        [UnityTest]
        public IEnumerator Given_APresenceChildWithoutAnExitPose_When_ItIsRemovedBeforeItsEnterSwaps_Then_ItReachesAnimateBeforeRemoval()
        {
            // Arrange — as above, with the Motion's own transition driving a classic exit.
            s_delayChildrenSec = 0.3f;
            s_declaresExit = false;
            var root = CreateRuntimePanel(shown: false);
            yield return null;
            _mounted = V.Mount(root, V.Component(PresenceHost, key: "root"));
            yield return null;
            _store.Set(true);
            _mounted.FlushStateForTest();
            var motion = root.Q<VisualElement>("m");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.1);
            var samples = new List<float>();

            // Act
            _store.Set(false);
            _mounted.FlushStateForTest();
            yield return Sample(motion, 1.2, samples);

            // Assert — the last sample taken while the ghost was still attached.
            Assert.That(samples.Count > 0 ? samples[samples.Count - 1] : float.NaN, Is.EqualTo(0f),
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
            yield return WaitUntilExitMoved(motion, exitSamples);

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

        // GREEN_ON_BASE(characterization): the base's exit never writes translate, so the re-add's own patch
        // is the last write and leaves the re-added node's values. Measured red at the commit that restored the
        // resting values recorded when the exit started: (50, 0).
        [UnityTest]
        public IEnumerator Given_APresenceChildExitingBetweenTranslatePoses_When_ItIsAddedBackMidExitWithANewClassNameAndLabel_Then_ItRestsAtTheNewValues()
        {
            // Arrange
            var root = CreateRuntimePanel(shown: true);
            yield return null;
            _mounted = V.Mount(root, V.Component(PresenceHost, key: "root"));
            var motion = root.Q<VisualElement>("m");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.6);
            _store.Set(false);
            yield return WaitUntilExitMoved(motion, new List<float>());

            // Act
            _store.Set(true, "absolute w-[80px] h-[50px]", "cover2");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.8);

            // Assert
            Assert.That((motion.resolvedStyle.width, motion.resolvedStyle.translate.x), Is.EqualTo((80f, 100f)));
        }

        // GREEN_ON_BASE(characterization): as the mid-exit case above; the re-add here lands after the exit
        // completed. The label stays, because a new one plays a runtime swap whose own write would cover the
        // restore. Measured red at the commit that restored the resting values recorded when the exit started:
        // (50, 0).
        [UnityTest]
        public IEnumerator Given_APresenceChildExitingBetweenTranslatePoses_When_ItIsAddedBackAsTheExitCompletesWithANewClassName_Then_ItRestsAtTheNewWidth()
        {
            // Arrange — no initial, so the re-entry plays no enter of its own.
            s_declaresInitial = false;
            var root = CreateRuntimePanel(shown: true);
            yield return null;
            _mounted = V.Mount(root, V.Component(PresenceHost, key: "root"));
            var motion = root.Q<VisualElement>("m");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.6);
            s_onExitComplete = () => _store.Set(true, "absolute w-[80px] h-[50px]", "cover");

            // Act
            _store.Set(false);
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(1.1);

            // Assert
            Assert.That((motion.resolvedStyle.width, motion.resolvedStyle.translate.x), Is.EqualTo((80f, 0f)));
        }

        [UnityTest]
        public IEnumerator Given_APresenceChildExitingFromAUssPose_When_ItIsAddedBackMidExitWithAnotherUssPose_Then_TheNewPoseHolds()
        {
            // Arrange — the bundled sheet is attached here alone, because this case reads a USS utility.
            var root = CreateRuntimePanel(shown: true);
            VelvetStyleUtilities.AttachTo(root);
            _store.Set(true, Box, "lit");
            yield return null;
            _mounted = V.Mount(root, V.Component(PresenceHost, key: "root"));
            var motion = root.Q<VisualElement>("m");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.6);
            _store.Set(false);
            yield return WaitUntilExitMoved(motion, new List<float>());

            // Act
            _store.Set(true, Box, "dim");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.8);

            // Assert
            Assert.That(motion.resolvedStyle.opacity, Is.EqualTo(0.5f).Within(1e-4f),
                string.Join(" ", motion.GetClasses()));
        }

        // GREEN_ON_BASE(characterization): the base's enter never writes initial's translate, so y is never
        // anything but its resting 0. Measured red at the commit that landed the enter's pose after every
        // exit's PlayExit: y held at 40 until the spring settled.
        [UnityTest]
        public IEnumerator Given_APresenceChildWithASpringExit_When_ItIsRemovedBeforeItsEnterSwaps_Then_AnAxisNoPoseNamesStaysAtRest()
        {
            // Arrange — delayChildrenSec holds the tween enter's swap back by 0.3s.
            s_delayChildrenSec = 0.3f;
            s_initialLabel = "awayY";
            s_exitLabel = "goneSpring";
            var root = CreateRuntimePanel(shown: false);
            yield return null;
            _mounted = V.Mount(root, V.Component(PresenceHost, key: "root"));
            yield return null;
            _store.Set(true);
            _mounted.FlushStateForTest();
            var motion = root.Q<VisualElement>("m");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.1);
            var ySamples = new List<float>();

            // Act
            _store.Set(false);
            _mounted.FlushStateForTest();
            var deadline = Time.realtimeSinceStartupAsDouble + 2.5;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                if (motion.panel != null)
                {
                    ySamples.Add(motion.resolvedStyle.translate.y);
                }
                yield return null;
            }

            // Assert — y stays at rest for as long as the ghost is sampled, which is several frames.
            Assert.That((ySamples.Count > 10, ySamples.Exists(y => y > 1f)), Is.EqualTo((true, false)),
                string.Join(", ", ySamples));
        }

        // GREEN_ON_BASE(characterization): the base's cancel puts back the resting classes the exit started
        // from, which include the one moved into className. Measured red at the commit that restored the
        // re-added resting variant alone: opacity 1.
        [UnityTest]
        public IEnumerator Given_APresenceChildExitingFromAUssPose_When_ItIsAddedBackMidExitWithThatClassMovedIntoClassName_Then_TheClassHolds()
        {
            // Arrange — the bundled sheet is attached here alone, because this case reads a USS utility.
            var root = CreateRuntimePanel(shown: true);
            VelvetStyleUtilities.AttachTo(root);
            _store.Set(true, Box, "dim");
            yield return null;
            _mounted = V.Mount(root, V.Component(PresenceHost, key: "root"));
            var motion = root.Q<VisualElement>("m");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.6);
            _store.Set(false);
            yield return WaitUntilExitMoved(motion, new List<float>());

            // Act
            _store.Set(true, Box + " opacity-50", "cover");
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.8);

            // Assert
            Assert.That(motion.resolvedStyle.opacity, Is.EqualTo(0.5f).Within(1e-4f),
                string.Join(" ", motion.GetClasses()));
        }
    }
}
#endif
