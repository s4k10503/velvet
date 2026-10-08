using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A layoutId move and a running <c>animate-bounce</c> or <c>animate-ping</c> on the same element, which both
    /// write translate or scale and each read the slot for the element's own value. The panel runs on a fake clock
    /// and the bundled stylesheet is attached, as in <see cref="AnimateSuspensionUnderVariantSwapTests"/>. GWT, one
    /// assert each.
    /// </summary>
    [TestFixture]
    internal sealed class AnimateLayoutIdCooperationTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";
        private const int MaxFrames = 1000;

        private double _now;

        private static readonly Dictionary<string, MotionVariant> s_variants = new() { ["visible"] = "opacity-100" };

        private static readonly StyleTransitionConfig s_transition = new()
        {
            DurationSec = 0.35f,
            Layout = new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f },
        };

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
            _now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, () => _now);
        }

        // A layoutId card after a spacer, so widening the spacer moves the card without touching its own classes.
        private static VNode Row(int spacerWidth, string className)
            => V.Div(className: "flex-row", children: new VNode[]
            {
                V.Div(className: $"w-[{spacerWidth}px] h-[40px]"),
                V.Motion(className: className, name: "card", variants: s_variants, animate: "visible", layoutId: "card",
                    transition: s_transition),
            });

        private (VisualElement element, StyleAnimateBinding binding) MountRow(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, Row(0, className));
            var element = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(element.panel);
            _mounted.Root.Reconciler.Context.AnimationBindings.TryGetValue(element, out var binding);
            return (element, binding);
        }

        private bool MoveRuns(VisualElement element)
            => _mounted!.Root.Reconciler.Context.LayoutIdProjections.ContainsKey(element);

        private void Patch(int toSpacer, string fromClassName, string toClassName) => _mounted!.Root.Reconciler.Reconcile(
            _window.rootVisualElement, new VNode[] { Row(0, fromClassName) }, new VNode[] { Row(toSpacer, toClassName) });

        // Plays the move out frame by frame; returns whether it started and whether it then ended.
        private (bool started, bool ended) PlayOutTheMove(VisualElement element)
        {
            ForcePanelUpdate(_window.rootVisualElement.panel);
            _now += 0.02;
            EditorPanelTestHelpers.DriveSchedulerOnce(_window.rootVisualElement.panel);
            var started = MoveRuns(element);
            for (var i = 0; i < MaxFrames && MoveRuns(element); i++)
            {
                _now += 0.02;
                EditorPanelTestHelpers.DriveSchedulerOnce(_window.rootVisualElement.panel);
                ForcePanelUpdate(_window.rootVisualElement.panel);
                EditorPanelTestHelpers.DriveAnimationsOnce(_window.rootVisualElement.panel);
            }
            return (started, !MoveRuns(element));
        }

        [Test]
        public void Given_ABounceLiftedWhenALayoutIdMoveStarts_When_TheMoveEnds_Then_TheNextBounceFrameIsAtTheElementsOwnTranslate()
        {
            // Arrange — the bounce is at the top of its lift as the move starts.
            const string classes = "w-[40px] h-[40px] bg-red-500 animate-bounce";
            var (element, binding) = MountRow(classes);
            binding.StartTime = Time.realtimeSinceStartupAsDouble;
            Patch(200, classes, classes);
            var (started, ended) = PlayOutTheMove(element);

            // Act — the bounce's frame at the bottom of its lift, where it adds nothing to the element's own.
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            // Assert
            var translate = element.style.translate.value;
            Assert.That((started, ended, Mathf.Abs(translate.x.value) + Mathf.Abs(translate.y.value) < 0.01f),
                Is.EqualTo((true, true, true)));
        }

        [Test]
        public void Given_APingScaledWhenALayoutIdMoveStarts_When_TheMoveEnds_Then_TheNextPingFrameIsAtTheElementsOwnScale()
        {
            // Arrange — the ping is about halfway through its growth as the move, which resizes the card, starts.
            const string before = "w-[40px] h-[40px] bg-red-500 animate-ping";
            const string after = "w-[80px] h-[40px] bg-red-500 animate-ping";
            var (element, binding) = MountRow(before);
            binding.StartTime = Time.realtimeSinceStartupAsDouble - 0.5;
            Patch(200, before, after);
            var (started, ended) = PlayOutTheMove(element);

            // Act — the ping's first frame, where it adds nothing to the element's own scale.
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Assert
            Assert.That((started, ended, Mathf.Abs(element.style.scale.value.value.x - 1f) < 0.01f),
                Is.EqualTo((true, true, true)));
        }
    }
}
