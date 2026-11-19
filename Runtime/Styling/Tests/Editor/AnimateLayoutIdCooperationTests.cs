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
                    transition: s_transition, enterOnMount: false),
            });

        private StyleAnimateBinding _attached;
        private VisualElement _attachedTo;

        // Mounts the row and attaches the loop to the card directly, so the case names the mode it measures and the
        // reconciler, which does not know this loop, leaves it alone. Teardown detaches it.
        private (VisualElement element, StyleAnimateBinding binding) MountRow(string className, AnimateMode mode)
        {
            _mounted = V.Mount(_window.rootVisualElement, Row(0, className));
            var element = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(element.panel);
            _attachedTo = element;
            _attached = StyleAnimateDriver.Attach(element, new AnimateSpec(mode, 1f), panVertical: false);
            return (element, _attached);
        }

        public override void TearDown()
        {
            if (_attached != null)
            {
                StyleAnimateDriver.Detach(_attachedTo, _attached);
                _attached = null;
            }
            base.TearDown();
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
                Frame();
            }
            return (started, !MoveRuns(element));
        }

        [Test]
        public void Given_ABounceLiftedWhenALayoutIdMoveStarts_When_TheMoveEnds_Then_TheNextBounceFrameIsAtTheElementsOwnTranslate()
        {
            // Arrange — the bounce is at the top of its lift as the move starts.
            const string classes = "w-[40px] h-[40px] bg-red-500";
            var (element, binding) = MountRow(classes, AnimateMode.Bounce);
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
            const string before = "w-[40px] h-[40px] bg-red-500";
            const string after = "w-[80px] h-[40px] bg-red-500";
            var (element, binding) = MountRow(before, AnimateMode.Ping);
            binding.StartTime = Time.realtimeSinceStartupAsDouble - 0.5;
            Patch(200, before, after);
            var (started, ended) = PlayOutTheMove(element);

            // Act — the ping's first frame, where it adds nothing to the element's own scale.
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Assert
            Assert.That((started, ended, Mathf.Abs(element.style.scale.value.value.x - 1f) < 0.01f),
                Is.EqualTo((true, true, true)));
        }

        [Test]
        public void Given_ABounceOnACardMidMove_When_TheLoopIsAtTheTopOfItsLiftThenAtTheBottom_Then_TheCardIsHigherAtTheTop()
        {
            // Arrange — the move is a spring along the row, so the card's y is the bounce's alone.
            const string classes = "w-[40px] h-[40px] bg-red-500";
            var (element, binding) = MountRow(classes, AnimateMode.Bounce);
            Patch(200, classes, classes);
            for (var i = 0; i < 3; i++) Frame();

            // Act — the real ticks of the loop run at the phase each start time gives.
            binding.StartTime = Time.realtimeSinceStartupAsDouble;
            Frame();
            Frame();
            var atTheTop = element.style.translate.value.y.value;
            binding.StartTime = Time.realtimeSinceStartupAsDouble - 0.5;
            Frame();
            Frame();
            var atTheBottom = element.style.translate.value.y.value;

            // Assert
            Assert.That((MoveRuns(element), atTheTop - atTheBottom < -5f), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base hands a loop the own translate a layoutId move ends on.
        [Test]
        public void Given_ABounceUnderALayoutIdMove_When_TheCardTakesAnOwnTranslateMidMoveAndTheMoveEnds_Then_TheNextBounceFrameIsAtThatTranslate()
        {
            // Arrange — the move starts, and the card takes a translate of its own while it runs.
            const string before = "w-[40px] h-[40px] bg-red-500";
            const string after = "w-[40px] h-[40px] bg-red-500 translate-x-[20px]";
            var (element, binding) = MountRow(before, AnimateMode.Bounce);
            Patch(200, before, before);
            ForcePanelUpdate(_window.rootVisualElement.panel);
            Frame();
            var started = MoveRuns(element);
            _mounted!.Root.Reconciler.Reconcile(
                _window.rootVisualElement, new VNode[] { Row(200, before) }, new VNode[] { Row(200, after) });
            for (var i = 0; i < MaxFrames && MoveRuns(element); i++)
            {
                Frame();
            }
            var ended = !MoveRuns(element);

            // Act — the bounce's frame at the bottom of its lift, where it adds nothing to the element's own.
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            // Assert — NaN where the move never ran or never ended, which 20 never equals.
            Assert.That(started && ended ? element.style.translate.value.x.value : float.NaN, Is.EqualTo(20f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base lets a ping on a card a correction wrapper moves scale it mid-move.
        [Test]
        public void Given_APingOnACardItsWrapperMoves_When_TheLoopGrowsMidMove_Then_TheCardGrowsWithIt()
        {
            // Arrange — the clip path draws the card through a wrapper, whose slots are the move's alone; the move
            // resizes the card, so it scales the wrapper.
            const string before = "w-[40px] h-[40px] bg-red-500 clip-path-[circle(50%)]";
            const string after = "w-[80px] h-[40px] bg-red-500 clip-path-[circle(50%)]";
            var (element, binding) = MountRow(before, AnimateMode.Ping);
            Patch(200, before, after);
            for (var i = 0; i < 3; i++) Frame();
            var boxed = !ReferenceEquals(MotionLayoutIdDriver.Box(element), element);

            // Act — the loop at the start of its growth, then half way through it.
            binding.StartTime = Time.realtimeSinceStartupAsDouble;
            Frame();
            Frame();
            var atTheStart = element.style.scale.value.value.x;
            binding.StartTime = Time.realtimeSinceStartupAsDouble - 0.5;
            Frame();
            Frame();
            var halfWay = element.style.scale.value.value.x;

            // Assert — NaN where the card is not wrapped or not moving, which no growth exceeds.
            Assert.That(boxed && MoveRuns(element) ? halfWay - atTheStart : float.NaN, Is.GreaterThan(0.05f));
        }

        private void Frame()
        {
            _now += 0.02;
            EditorPanelTestHelpers.DriveSchedulerOnce(_window.rootVisualElement.panel);
            ForcePanelUpdate(_window.rootVisualElement.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(_window.rootVisualElement.panel);
        }
    }
}
