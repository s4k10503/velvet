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
    /// Pins that a <c>Hooks.UseAnimationSequence</c> driving a <c>V.Motion</c> between translate poses passes
    /// through intermediate values on a real runtime panel on two kinds of step: a 0.3s step that follows a
    /// near-instant one, and a step that follows one whose play has already finished.
    /// </summary>
    internal sealed class AnimationSequencePlaybackTests
    {
        // Cover sits between the other two with away and gone on opposite sides of it, so a sample's value
        // alone says which moving step it was taken on.
        private static readonly Dictionary<string, MotionVariant> s_poses = new()
        {
            ["away"] = "translate-x-[300px]",
            ["cover"] = "translate-x-[0px]",
            ["hold"] = "translate-x-[0px]",
            ["gone"] = "translate-x-[-300px]",
        };

        // Five percent of the 300px travel: a sample inside this margin of either end is not counted as
        // between them.
        private const float Margin = 15f;

        private static AnimationSequenceStep[] s_steps;

        private GameObject _go;
        private PanelSettings _settings;
        private MountedTree _mounted;
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            s_steps = null;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _frameRateScope.Dispose();
            _mounted?.Dispose();
            _mounted = null;
            if (_go != null) Object.Destroy(_go);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        [Component]
        private static VNode SequenceHost()
        {
            var (state, _) = Hooks.UseAnimationSequence(s_steps);
            return V.Div(name: "wrap", children: new VNode[]
            {
                V.Motion(key: "m", name: "m", className: "absolute w-[50px] h-[50px]", variants: s_poses,
                    animate: state.CurrentLabel, transition: state.CurrentTransition),
            });
        }

        private static StyleTransitionConfig Linear(float seconds)
            => new() { DurationSec = seconds, Easing = EasingMode.Linear };

        // Mounts the sequence on a real UIDocument panel and records the Motion's resolved translate.x once a
        // frame for the given realtime.
        private IEnumerator MountAndSample(AnimationSequenceStep[] steps, double seconds, List<float> samples)
        {
            s_steps = steps;
            _go = new GameObject("AnimationSequencePlayback");
            var doc = _go.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
            VelvetStyleUtilities.AttachTo(doc.rootVisualElement);
            _mounted = V.Mount(doc.rootVisualElement, V.Component(SequenceHost, key: "root"));
            var motion = doc.rootVisualElement.Q<VisualElement>("m");
            var deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                samples.Add(motion.resolvedStyle.translate.x);
                yield return null;
            }
        }

        private static bool AnyStrictlyBetween(List<float> samples, float low, float high)
            => samples.Exists(x => x > low + Margin && x < high - Margin);

        [UnityTest]
        public IEnumerator Given_ANearInstantStepThenATweenStep_When_FramesAdvance_Then_TheTweenStepPassesThroughAnIntermediateTranslate()
        {
            // Arrange
            var samples = new List<float>();
            var steps = new[]
            {
                AnimationSequenceStep.To("away", Linear(0.001f)),
                AnimationSequenceStep.To("cover", Linear(0.3f)),
                AnimationSequenceStep.Wait(0.5f),
            };

            // Act
            yield return MountAndSample(steps, 1.0, samples);

            // Assert
            Assert.That(AnyStrictlyBetween(samples, 0f, 300f), Is.True, string.Join(", ", samples));
        }

        [UnityTest]
        public IEnumerator Given_AnAwayCoverHoldGoneSequence_When_FramesAdvance_Then_BothMovingStepsPassThroughAnIntermediateTranslate()
        {
            // Arrange
            var samples = new List<float>();
            var steps = new[]
            {
                AnimationSequenceStep.To("away", Linear(0.001f)),
                AnimationSequenceStep.To("cover", Linear(0.3f)),
                AnimationSequenceStep.To("hold", Linear(0.3f)),
                AnimationSequenceStep.To("gone", Linear(0.3f)),
                AnimationSequenceStep.Wait(0.5f),
            };

            // Act
            yield return MountAndSample(steps, 1.8, samples);

            // Assert
            Assert.That((AnyStrictlyBetween(samples, 0f, 300f), AnyStrictlyBetween(samples, -300f, 0f)),
                Is.EqualTo((true, true)), string.Join(", ", samples));
        }
    }
}
#endif
