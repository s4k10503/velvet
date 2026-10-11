using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a label change into a zero-duration pose does to the play still moving a Motion: a property the
    /// pose names lands within two frames, whether a swapped tween, a spring or a bezier is moving it, and a
    /// property it does not name keeps moving.
    /// </summary>
    [TestFixture]
    internal sealed class MotionZeroDurationLandingTests : MotionSimulatedPanelTestsBase
    {
        private readonly record struct LabelState(string Label);

        private sealed class LabelStore : Store<LabelState>
        {
            public LabelStore() : base(new LabelState("from")) { }
            public void Set(string label) => SetState(_ => new LabelState(label));
            protected override void ResetCore() => SetState(_ => new LabelState("from"));
        }

        private static readonly StyleTransitionConfig s_tween = new() { DurationSec = 0.35f };
        private static readonly StyleTransitionConfig s_spring = new() { Type = TransitionType.Spring };
        private static readonly StyleTransitionConfig s_bezier = new() { Type = TransitionType.Bezier, DurationSec = 0.6f };

        private static LabelStore s_store;
        private static Dictionary<string, MotionVariant> s_poses;
        private static StyleTransitionConfig s_transition;
        private static string s_className;

        public override void SetUp()
        {
            base.SetUp();
            // The poses' utility classes must resolve for resolvedStyle to show what each play leaves.
            VelvetStyleUtilities.AttachTo(Root);
            s_store = null;
            s_poses = null;
            s_transition = null;
            s_className = null;
        }

        [Component]
        private static VNode PoseBox()
        {
            var label = Hooks.UseStore(s_store, s => s.Label);
            // V.Motion gives a Motion with no transition the Fade preset's; one built directly carries none.
            return s_transition == null
                ? new MotionNode { Name = "m", Variants = s_poses, Animate = label }
                : V.Motion(name: "m", className: s_className, variants: s_poses, animate: label,
                    transition: s_transition);
        }

        // OnPoses puts Transition on the To pose and builds the Motion with none, so a Land with no LandTransition
        // has no transition at all.
        private readonly record struct Poses(StyleTransitionConfig Transition, string From, string To, string Land,
            int Frames = 3, string Next = null, StyleTransitionConfig NextTransition = null, string Mid = null,
            StyleTransitionConfig LandTransition = null, bool OnPoses = false);

        // Mounts the box at From, plays the swap to To on Transition for Frames frames, then changes the label to
        // Land, whose pose has zero duration. onPlay runs as the play starts. Returns the box, what read gave just
        // before Land, and the mount.
        private (VisualElement box, float before, MountedTree mounted) PlayThenLand(Poses poses,
            System.Func<VisualElement, float> read, System.Action<VisualElement> onPlay = null)
        {
            s_transition = poses.OnPoses ? null : poses.Transition;
            s_poses = new Dictionary<string, MotionVariant>
            {
                ["from"] = poses.From,
                ["to"] = new MotionVariant(poses.To, poses.OnPoses ? poses.Transition : null),
                ["land"] = new MotionVariant(poses.Land,
                    poses.LandTransition ?? (poses.OnPoses ? null : StyleTransitionConfig.None)),
                ["mid"] = new MotionVariant(poses.Mid ?? string.Empty, s_tween),
                ["next"] = new MotionVariant(poses.Next ?? string.Empty, poses.NextTransition ?? s_tween),
            };
            s_store = new LabelStore();
            var mounted = V.Mount(Root, V.Component(PoseBox, key: "root"));
            Tick();
            s_store.Set("to");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var box = Root.Q<VisualElement>("m");
            onPlay?.Invoke(box);
            for (var i = 0; i < poses.Frames; i++)
            {
                Tick();
            }
            var before = read(box);
            s_store.Set("land");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            return (box, before, mounted);
        }

        private static StyleTransitionConfig Driver(TransitionType type)
            => type == TransitionType.Spring ? s_spring : s_bezier;

        public override void TearDown()
        {
            s_store?.Dispose();
            base.TearDown();
        }

        private static float Opacity(VisualElement element) => element.resolvedStyle.opacity;

        private static float TranslateX(VisualElement element) => element.resolvedStyle.translate.x;

        private static float InlineTranslateX(VisualElement element)
            => element.style.translate.keyword == StyleKeyword.Undefined ? element.style.translate.value.x.value : float.NaN;

        private static bool Between(float value, float low, float high) => value > low && value < high;

        // Lands on the second frame after the landing render, where a spring's or a bezier's channel lands on the
        // first.
        [Test]
        public void Given_ATweenSwapThatHasSwapped_When_AZeroDurationPoseNamingOpacityFollows_Then_OpacityLandsByTheSecondFrame()
        {
            // Arrange
            var (box, before, mounted) = PlayThenLand(new Poses(s_tween, "opacity-0", "opacity-100", "opacity-50"),
                Opacity);
            using var _ = mounted;

            // Act
            Tick();
            Tick();

            // Assert — gated on the tween being mid-way, where landing and tweening read apart.
            var landed = Between(before, 0.05f, 0.45f) ? Opacity(box) : float.NaN;
            Assert.That(landed, Is.EqualTo(0.5f).Within(1e-3f));
        }

        [Test]
        public void Given_ATweenSwapWhoseDurationCodeRewrote_When_AZeroDurationPoseLandsAndTheTweenEnds_Then_ThatDurationIsTheElementsOwn()
        {
            // Arrange
            var (box, _, mounted) = PlayThenLand(new Poses(s_tween, "opacity-0", "opacity-100", "opacity-50"),
                Opacity, element => element.style.transitionDuration = new List<TimeValue> { new(0.25f, TimeUnit.Second) });
            using var owned = mounted;

            // Act
            AdvancePast(0.4f);

            // Assert
            var durations = box.style.transitionDuration;
            Assert.That((durations.keyword,
                    durations.value?.Count == 1 && durations.value[0].Equals(new TimeValue(0.25f, TimeUnit.Second))),
                Is.EqualTo((StyleKeyword.Undefined, true)));
        }

        // The tween's target writes no opacity, so the pose's opacity is a change the landing has to time rather than a
        // value it repeats: left out of the list, opacity would rest at the tween's old target
        // (HeldTransitionOverrideEngineTests).
        [Test]
        public void Given_ATweenSwapWhoseTargetLeavesOpacityUnnamed_When_AZeroDurationPoseNamingOpacityFollows_Then_OpacityLandsByTheSecondFrame()
        {
            // Arrange
            var (box, before, mounted) = PlayThenLand(new Poses(s_tween, "opacity-0 translate-x-[0px]",
                "translate-x-[40px]", "opacity-50"), Opacity);
            using var _ = mounted;

            // Act
            Tick();
            Tick();

            // Assert — gated on the tween being mid-way, where landing and resting at the old target read apart.
            var landed = Between(before, 0.05f, 0.45f) ? Opacity(box) : float.NaN;
            Assert.That(landed, Is.EqualTo(0.5f).Within(1e-3f));
        }

        // A tween with a delay writes a transition-delay list, which the landing extends beside the others.
        [Test]
        public void Given_ADelayedTweenSwapThatHasSwapped_When_AZeroDurationPoseNamingOpacityFollows_Then_OpacityLandsByTheSecondFrame()
        {
            // Arrange
            var delayed = new StyleTransitionConfig { DurationSec = 0.35f, DelaySec = 0.05f };
            var (box, before, mounted) = PlayThenLand(
                new Poses(delayed, "opacity-0", "opacity-100", "opacity-50", Frames: 8), Opacity);
            using var _ = mounted;

            // Act
            Tick();
            Tick();

            // Assert
            var landed = Between(before, 0.05f, 0.45f) ? Opacity(box) : float.NaN;
            Assert.That(landed, Is.EqualTo(0.5f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base leaves a swapped tween's transition lists as the play wrote them.
        // Each of them keeps an entry for every transition-property entry once the landing has extended them, which
        // is what lets MotionNativeTransitionGuard realign them when it narrows the list.
        [Test]
        public void Given_ATweenSwapThatHasSwapped_When_AZeroDurationPoseLandsOneOfItsProperties_Then_ItsTransitionListsStayPaired()
        {
            // Arrange
            var (box, _, mounted) = PlayThenLand(new Poses(s_tween, "opacity-0", "opacity-100", "opacity-50"),
                Opacity);
            using var __ = mounted;

            // Act
            var names = box.style.transitionProperty.value.Count;

            // Assert
            Assert.That(new[] { box.style.transitionDuration.value.Count, box.style.transitionTimingFunction.value.Count },
                Is.EqualTo(new[] { names, names }));
        }

        [Test]
        public void Given_ATweenSwapThatHasSwapped_When_AZeroDurationPoseNamingTranslateFollows_Then_TranslateLandsOnTheNextFrame()
        {
            // Arrange
            var (box, before, mounted) = PlayThenLand(
                new Poses(s_tween, "translate-x-[0px]", "translate-x-[40px]", "translate-x-[20px]"), TranslateX);
            using var _ = mounted;

            // Act
            Tick();

            // Assert
            var landed = Between(before, 0.5f, 18f) ? TranslateX(box) : float.NaN;
            Assert.That(landed, Is.EqualTo(20f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base's zero-duration swap leaves a swapped tween's transition whole.
        // So opacity, which the landing pose leaves unnamed, keeps tweening toward the value it now rests at.
        [Test]
        public void Given_ATweenSwapThatHasSwapped_When_AZeroDurationPoseNamingOnlyTranslateFollows_Then_OpacityKeepsMoving()
        {
            // Arrange
            var (box, before, mounted) = PlayThenLand(new Poses(s_tween, "opacity-0 translate-x-[0px]",
                "opacity-100 translate-x-[40px]", "translate-x-[20px]"), Opacity);
            using var _ = mounted;

            // Act
            Tick();

            // Assert
            var moving = Between(before, 0.05f, 0.45f) ? Opacity(box) : float.NaN;
            Assert.That(moving, Is.InRange(0.05f, 0.9f));
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ADrivenSwapRunning_When_AZeroDurationPoseNamingOpacityFollows_Then_OpacityLandsOnTheNextFrame(
            TransitionType type)
        {
            // Arrange
            var (box, before, mounted) = PlayThenLand(
                new Poses(Driver(type), "opacity-0", "opacity-100", "opacity-50"), Opacity);
            using var _ = mounted;

            // Act
            Tick();

            // Assert
            var landed = Between(before, 0.005f, 0.45f) ? Opacity(box) : float.NaN;
            Assert.That(landed, Is.EqualTo(0.5f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base's zero-duration swap leaves a running spring or bezier whole.
        // So its translate, which the landing pose leaves unnamed, ends at the play's own target.
        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ADrivenSwapRunning_When_AZeroDurationPoseNamingOnlyOpacityFollows_Then_TranslateEndsAtThePlaysTarget(
            TransitionType type)
        {
            // Arrange
            var (box, _, mounted) = PlayThenLand(new Poses(Driver(type), "opacity-0 translate-x-[0px]",
                "opacity-100 translate-x-[40px]", "opacity-50"), InlineTranslateX);
            using var __ = mounted;

            // Act
            AdvancePast(2f);

            // Assert
            Assert.That(InlineTranslateX(box), Is.EqualTo(40f));
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ADrivenSwapRunning_When_AZeroDurationPoseNamingTranslateFollows_Then_TheTranslateStaysAfterThePlayEnds(
            TransitionType type)
        {
            // Arrange
            var (box, _, mounted) = PlayThenLand(
                new Poses(Driver(type), "translate-x-[0px]", "translate-x-[40px]", "translate-x-[20px]"),
                InlineTranslateX);
            using var __ = mounted;

            // Act
            AdvancePast(2f);

            // Assert
            Assert.That(InlineTranslateX(box), Is.EqualTo(20f));
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ADrivenSwapRunning_When_AZeroDurationPoseRepeatingItsTranslateFollows_Then_TheTranslateLandsThere(
            TransitionType type)
        {
            // Arrange — the landing pose carries the running pose's own translate class, which its sync does not
            // write again.
            var (box, before, mounted) = PlayThenLand(new Poses(Driver(type), "opacity-0 translate-x-[0px]",
                "opacity-100 translate-x-[40px]", "opacity-50 translate-x-[40px]"), InlineTranslateX);
            using var _ = mounted;

            // Act
            Tick();

            // Assert
            var landed = Between(before, 0.1f, 39f) ? TranslateX(box) : float.NaN;
            Assert.That(landed, Is.EqualTo(40f).Within(0.01f));
        }

        private static float Read(VisualElement element, string property) => property switch
        {
            "opacity" => element.resolvedStyle.opacity,
            "translate" => element.resolvedStyle.translate.x,
            "scale" => element.resolvedStyle.scale.value.x,
            "rotate" => element.resolvedStyle.rotate.angle.ToDegrees(),
            "color" => element.resolvedStyle.backgroundColor.g,
            "font" => element.resolvedStyle.fontSize,
            _ => float.NaN,
        };

        public readonly record struct Channel
        {
            public TransitionType Type { get; init; }
            public string From { get; init; }
            public string To { get; init; }
            public string Land { get; init; }
            public string Property { get; init; }
            public float FromValue { get; init; }
            public float ToValue { get; init; }
            public float LandValue { get; init; }
        }

        // One case per kind of channel a spring or bezier drives, each moving only the property the landing pose
        // names, so the channel it releases is the one under test.
        private static IEnumerable<Channel> Channels()
        {
            foreach (var type in new[] { TransitionType.Spring, TransitionType.Bezier })
            {
                yield return new Channel
                {
                    Type = type, From = "translate-x-[0px]", To = "translate-x-[40px]", Land = "translate-x-[20px]",
                    Property = "translate", FromValue = 0f, ToValue = 40f, LandValue = 20f,
                };
                yield return new Channel
                {
                    Type = type, From = "scale-[0.5]", To = "scale-[1]", Land = "scale-[0.75]",
                    Property = "scale", FromValue = 0.5f, ToValue = 1f, LandValue = 0.75f,
                };
                yield return new Channel
                {
                    Type = type, From = "rotate-[0deg]", To = "rotate-[90deg]", Land = "rotate-[45deg]",
                    Property = "rotate", FromValue = 0f, ToValue = 90f, LandValue = 45f,
                };
                yield return new Channel
                {
                    Type = type, From = "bg-black", To = "bg-white", Land = "bg-black",
                    Property = "color", FromValue = 0f, ToValue = 1f, LandValue = 0f,
                };
                yield return new Channel
                {
                    Type = type, From = "text-[10px]", To = "text-[40px]", Land = "text-[30px]",
                    Property = "font", FromValue = 10f, ToValue = 40f, LandValue = 30f,
                };
            }
        }

        [TestCaseSource(nameof(Channels))]
        public void Given_ADrivenSwapRunning_When_AZeroDurationPoseNamingItsPropertyFollows_Then_ThePropertyLandsOnTheNextFrame(
            Channel channel)
        {
            // Arrange
            // s_bezier leaves its start more slowly than s_spring, so it runs longer before the landing.
            var frames = channel.Type == TransitionType.Bezier ? 8 : 3;
            var (box, before, mounted) = PlayThenLand(
                new Poses(Driver(channel.Type), channel.From, channel.To, channel.Land, frames),
                element => Read(element, channel.Property));
            using var _ = mounted;
            var span = Mathf.Abs(channel.ToValue - channel.FromValue);

            // Act
            Tick();

            // Assert — gated on the play having moved the property off both ends and off the landing value.
            var moving = Mathf.Abs(before - channel.FromValue) > span * 0.005f
                && Mathf.Abs(before - channel.ToValue) > span * 0.05f
                && Mathf.Abs(before - channel.LandValue) > span * 0.05f;
            var landed = moving ? Read(box, channel.Property) : float.NaN;
            Assert.That(landed, Is.EqualTo(channel.LandValue).Within(span * 1e-3f));
        }

        // One case per kind of channel a spring or bezier drives, each moving that property beside a translate or
        // an opacity the landing pose does name. No property's unstyled value lies between its from and to values,
        // so neither jumping to the play's target nor falling back to that value reads as still moving.
        private static IEnumerable<Channel> UnnamedChannels()
        {
            foreach (var type in new[] { TransitionType.Spring, TransitionType.Bezier })
            {
                yield return new Channel
                {
                    Type = type, From = "opacity-0 translate-x-[0px]", To = "opacity-100 translate-x-[40px]",
                    Land = "translate-x-[20px]", Property = "opacity", FromValue = 0f, ToValue = 1f,
                };
                yield return new Channel
                {
                    Type = type, From = "opacity-0 translate-x-[0px]", To = "opacity-100 translate-x-[40px]",
                    Land = "opacity-50", Property = "translate", FromValue = 0f, ToValue = 40f,
                };
                yield return new Channel
                {
                    Type = type, From = "scale-[1.5] translate-x-[0px]", To = "scale-[2] translate-x-[40px]",
                    Land = "translate-x-[20px]", Property = "scale", FromValue = 1.5f, ToValue = 2f,
                };
                // active-scale-95 writes scale only while the element is pressed, so it names no scale here.
                yield return new Channel
                {
                    Type = type, From = "scale-[1.5] translate-x-[0px]", To = "scale-[2] translate-x-[40px]",
                    Land = "translate-x-[20px] active-scale-95", Property = "scale", FromValue = 1.5f, ToValue = 2f,
                };
                yield return new Channel
                {
                    Type = type, From = "rotate-[0deg] translate-x-[0px]", To = "rotate-[90deg] translate-x-[40px]",
                    Land = "translate-x-[20px]", Property = "rotate", FromValue = 0f, ToValue = 90f,
                };
                yield return new Channel
                {
                    Type = type, From = "bg-black translate-x-[0px]", To = "bg-white translate-x-[40px]",
                    Land = "translate-x-[20px]", Property = "color", FromValue = 0f, ToValue = 1f,
                };
            }
        }

        // GREEN_ON_BASE(characterization): the base's zero-duration swap leaves a running spring or bezier whole.
        // So a property the landing pose leaves unnamed goes on moving toward the play's target.
        [TestCaseSource(nameof(UnnamedChannels))]
        public void Given_ADrivenSwapRunning_When_AZeroDurationPoseLeavesItsPropertyUnnamed_Then_ThePropertyKeepsMoving(
            Channel channel)
        {
            // Arrange
            var (box, before, mounted) = PlayThenLand(
                new Poses(Driver(channel.Type), channel.From, channel.To, channel.Land),
                element => Read(element, channel.Property));
            using var _ = mounted;
            var span = channel.ToValue - channel.FromValue;

            // Act
            Tick();

            // Assert — gated on the play having moved the property off both ends.
            var moving = before - channel.FromValue > span * 0.005f && channel.ToValue - before > span * 0.05f;
            var after = moving ? Read(box, channel.Property) : float.NaN;
            Assert.That(after, Is.InRange(before + span * 1e-4f, channel.ToValue - span * 0.01f));
        }

        public readonly record struct Undriven
        {
            public TransitionType Type { get; init; }
            public string Token { get; init; }
            public string Driven { get; init; }
            public string Property { get; init; }
            public float Value { get; init; }
        }

        // The Motion's own className carries a token for a property the play does not drive, and the landing pose
        // repeats that token, so the sync writes nothing for it.
        private static IEnumerable<Undriven> UndrivenProperties()
        {
            foreach (var type in new[] { TransitionType.Spring, TransitionType.Bezier })
            {
                yield return new Undriven { Type = type, Token = "opacity-[0.4]", Driven = "translate", Property = "opacity", Value = 0.4f };
                yield return new Undriven { Type = type, Token = "translate-x-[8px]", Driven = "opacity", Property = "translate", Value = 8f };
                yield return new Undriven { Type = type, Token = "scale-[0.8]", Driven = "translate", Property = "scale", Value = 0.8f };
                yield return new Undriven { Type = type, Token = "rotate-[30deg]", Driven = "translate", Property = "rotate", Value = 30f };
            }
        }

        // GREEN_ON_BASE(characterization): the base's zero-duration swap leaves a running spring or bezier whole.
        // So a property the play never drove keeps the value the Motion's own className gives it.
        [TestCaseSource(nameof(UndrivenProperties))]
        public void Given_ADrivenSwapRunning_When_AZeroDurationPoseNamesAPropertyItDoesNotDrive_Then_ThePropertyKeepsItsValue(
            Undriven undriven)
        {
            // Arrange
            s_className = undriven.Token;
            var poses = undriven.Driven == "translate"
                ? new Poses(Driver(undriven.Type), "translate-x-[0px]", "translate-x-[40px]",
                    "translate-x-[20px] " + undriven.Token)
                : new Poses(Driver(undriven.Type), "opacity-0", "opacity-100", "opacity-50 " + undriven.Token);
            var (box, _, mounted) = PlayThenLand(poses, Opacity);
            using var __ = mounted;

            // Act
            Tick();

            // Assert
            Assert.That(Read(box, undriven.Property), Is.EqualTo(undriven.Value).Within(1e-3f));
        }

        private static string TagClasses(VisualElement element)
            => string.Join(" ", element.GetClasses().Where(c => c.StartsWith("tag-")).OrderBy(c => c));

        // Each play still moves the translate the landing pose leaves unnamed when the next label arrives, so the
        // next pose's swap cancels it.
        [TestCase(TransitionType.Tween)]
        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_APlayStillRunningUnderALandedZeroDurationPose_When_ATimedPoseFollows_Then_OnlyThatPosesClassIsCarried(
            TransitionType type)
        {
            // Arrange
            var transition = type == TransitionType.Tween ? s_tween : Driver(type);
            var (box, _, mounted) = PlayThenLand(new Poses(transition, "opacity-0 translate-x-[0px] tag-a",
                "opacity-100 translate-x-[40px] tag-b", "opacity-50 tag-c", Next: "opacity-0 tag-d"), Opacity);
            using var __ = mounted;
            Tick();

            // Act
            s_store.Set("next");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(2f);

            // Assert
            Assert.That(TagClasses(box), Is.EqualTo("tag-d"));
        }

        [Test]
        public void Given_ATweenSwapLandedUnderAZeroDurationPoseLeavingTranslateUnnamed_When_ASpringPoseFollows_Then_NoTranslateComesBack()
        {
            // Arrange
            var (box, _, mounted) = PlayThenLand(new Poses(s_tween, "translate-x-[0px]", "translate-x-[40px]",
                "opacity-50", Next: "opacity-0", NextTransition: s_spring), Opacity);
            using var __ = mounted;
            AdvancePast(1f);

            // Act
            s_store.Set("next");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(2f);

            // Assert
            Assert.That(box.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base carried no class from a landing to the next label change.
        // Read at the spring's first frame rather than at its settle, where the case above reads.
        [Test]
        public void Given_ATweenSwapLandedUnderAZeroDurationPoseLeavingTranslateUnnamed_When_ASpringPoseFollows_Then_TheSpringStartsNoTranslate()
        {
            // Arrange
            var (box, _, mounted) = PlayThenLand(new Poses(s_tween, "translate-x-[0px]", "translate-x-[40px]",
                "opacity-50", Next: "opacity-0", NextTransition: s_spring), Opacity);
            using var __ = mounted;
            AdvancePast(1f);

            // Act
            s_store.Set("next");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Tick();

            // Assert
            Assert.That(TranslateX(box), Is.EqualTo(0f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base took no token off the class list at a label change.
        // So one the landing pose repeats from the pose before it stays there.
        [Test]
        public void Given_ATweenSwapThatHasSwapped_When_AZeroDurationPoseRepeatingItsTranslateFollows_Then_TheTranslateTokenStaysOnTheClassList()
        {
            // Arrange
            var (box, _, mounted) = PlayThenLand(new Poses(s_tween, "translate-x-[0px] opacity-0",
                "translate-x-[40px] opacity-100", "translate-x-[40px] opacity-50"), Opacity);
            using var __ = mounted;

            // Act
            AdvancePast(1f);

            // Assert
            Assert.That(box.ClassListContains("translate-x-[40px]"), Is.True);
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ADrivenSwapSettledUnderAZeroDurationPoseLeavingTranslateUnnamed_When_LaterPosesMoveTranslateAndLeaveIt_Then_TheDrivenTranslateDoesNotComeBack(
            TransitionType type)
        {
            // Arrange
            var (box, _, mounted) = PlayThenLand(new Poses(Driver(type), "translate-x-[0px]", "translate-x-[40px]",
                "opacity-50", Mid: "translate-x-[10px]", Next: "opacity-0", NextTransition: s_spring), InlineTranslateX);
            using var __ = mounted;
            AdvancePast(2f);
            s_store.Set("mid");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(1f);

            // Act
            s_store.Set("next");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(2f);

            // Assert
            Assert.That(box.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ATweenSwapThatHasSwapped_When_AZeroDurationPoseRepeatingItsOpacityFollows_Then_OpacityLandsByTheSecondFrame()
        {
            // Arrange
            var (box, before, mounted) = PlayThenLand(new Poses(s_tween, "opacity-0 translate-x-[0px]",
                "opacity-100 translate-x-[40px]", "opacity-100 translate-x-[20px]"), Opacity);
            using var _ = mounted;

            // Act
            Tick();
            Tick();

            // Assert — gated on the tween being mid-way, where landing and tweening read apart.
            var landed = Between(before, 0.05f, 0.45f) ? Opacity(box) : float.NaN;
            Assert.That(landed, Is.EqualTo(1f).Within(1e-3f));
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ADrivenSwapRunningUnderAHoverOpacityLayer_When_AZeroDurationPoseNamingOpacityFollows_Then_TheHoverOpacityShows(
            TransitionType type)
        {
            // Arrange — the landing pose repeats the play's opacity class, so the class sync leaves opacity alone, and
            // leaves translate to the play, so it does not settle on the next tick.
            var (box, _, mounted) = PlayThenLand(new Poses(Driver(type), "opacity-0 translate-x-[0px]",
                "opacity-100 translate-x-[40px]", "opacity-100 tag-c"),
                Opacity,
                element => StyleArbitraryValueResolver.ApplyClassToken(element, "opacity-[0.3]", StyleLayerPriority.Hover));
            using var _ = mounted;

            // Act
            Tick();

            // Assert
            var inline = box.style.opacity.keyword == StyleKeyword.Undefined ? box.style.opacity.value : float.NaN;
            Assert.That(inline, Is.EqualTo(0.3f).Within(1e-3f));
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ADrivenSwapRunning_When_AZeroDurationBezierPoseLeavesTranslateUnnamed_Then_TranslateKeepsMoving(
            TransitionType type)
        {
            // Arrange
            var (box, before, mounted) = PlayThenLand(new Poses(Driver(type), "opacity-0 translate-x-[0px]",
                "opacity-100 translate-x-[40px]", "opacity-50",
                LandTransition: new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 0f }),
                InlineTranslateX);
            using var _ = mounted;

            // Act
            Tick();

            // Assert — gated on the play having moved translate off both ends.
            var after = Between(before, 0.2f, 38f) ? InlineTranslateX(box) : float.NaN;
            Assert.That(after, Is.InRange(before + 1e-3f, 39.6f));
        }

        [Test]
        public void Given_ASpringSwapRunning_When_APoseWithNoTransitionLandsAndATimedPoseFollows_Then_OnlyThatPosesClassIsCarried()
        {
            // Arrange
            var (box, _, mounted) = PlayThenLand(new Poses(s_spring, "opacity-0 translate-x-[0px] tag-a",
                "opacity-100 translate-x-[40px] tag-b", "opacity-50 tag-c", Next: "opacity-0 tag-d", OnPoses: true),
                Opacity);
            using var __ = mounted;
            Tick();

            // Act
            s_store.Set("next");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(2f);

            // Assert
            Assert.That(TagClasses(box), Is.EqualTo("tag-d"));
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ADrivenSwapRunningUnderALandedZeroDurationPose_When_ASecondNamingTranslateFollows_Then_TheTranslateStaysAfterThePlayEnds(
            TransitionType type)
        {
            // Arrange — the first landing pose leaves translate unnamed, so the play goes on driving it.
            var (box, _, mounted) = PlayThenLand(new Poses(Driver(type), "opacity-0 translate-x-[0px]",
                "opacity-100 translate-x-[40px]", "opacity-50", Next: "opacity-50 translate-x-[20px]",
                NextTransition: StyleTransitionConfig.None), InlineTranslateX);
            using var __ = mounted;
            Tick();

            // Act
            s_store.Set("next");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(2f);

            // Assert
            Assert.That(InlineTranslateX(box), Is.EqualTo(20f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base left a swapped tween's `all` in place under a zero-duration pose.
        // So a class the same render changes outside the pose tweens on the play's timing.
        [Test]
        public void Given_ATweenSwapThatHasSwapped_When_AZeroDurationPoseRepeatingItsOpacityLandsBesideABackgroundChange_Then_TheBackgroundTweens()
        {
            // Arrange
            s_className = "bg-[#ff0000]";
            var (box, _, mounted) = PlayThenLand(new Poses(s_tween, "opacity-0 translate-x-[0px]",
                "opacity-100 translate-x-[40px]", "opacity-100 translate-x-[20px]"), Opacity,
                _ => s_className = "bg-[#0000ff]");
            using var __ = mounted;

            // Act
            Tick();

            // Assert
            Assert.That(box.resolvedStyle.backgroundColor.r, Is.GreaterThan(0.5f));
        }

        private static float InlineRotate(VisualElement element)
            => element.style.rotate.keyword == StyleKeyword.Undefined ? element.style.rotate.value.angle.value : float.NaN;

        private static readonly StyleTransitionConfig s_zeroBezier = new() { Type = TransitionType.Bezier, DurationSec = 0f };

        // GREEN_ON_BASE(characterization): the base's zero-duration bezier swap stopped the whole running play.
        // So the opacity its pose names showed at once there too; here only that property lands.
        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ADrivenSwapRunning_When_AZeroDurationBezierPoseNamingOpacityFollows_Then_OpacityLandsOnTheNextFrame(
            TransitionType type)
        {
            // Arrange
            var (box, before, mounted) = PlayThenLand(new Poses(Driver(type), "opacity-0", "opacity-100", "opacity-50",
                LandTransition: s_zeroBezier), Opacity);
            using var _ = mounted;

            // Act
            Tick();

            // Assert
            var landed = Between(before, 0.005f, 0.45f) ? Opacity(box) : float.NaN;
            Assert.That(landed, Is.EqualTo(0.5f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base started a spring or bezier swap from the pose it left.
        // So an opacity the settled first play took back to rest is where the second one starts it from.
        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ADrivenSwapThatSettled_When_ASecondDrivenSwapNamesAnOpacityTheFirstLeftBehind_Then_ItStartsFromRest(
            TransitionType type)
        {
            // Arrange
            var (box, _, mounted) = PlayThenLand(new Poses(Driver(type), "opacity-[0.2] translate-x-[0px]",
                "translate-x-[40px]", "opacity-[0.6] translate-x-[80px]", Frames: 200, LandTransition: Driver(type)),
                Opacity);
            using var __ = mounted;

            // Act
            Tick();

            // Assert
            Assert.That(Opacity(box), Is.GreaterThan(0.8f));
        }

        [Test]
        public void Given_ATweenSwapWithPropertyOverrides_When_AZeroDurationPoseRepeatingItsOpacityFollows_Then_OpacityLandsByTheSecondFrame()
        {
            // Arrange
            var overrides = new StyleTransitionConfig
            {
                DurationSec = 0.35f,
                PropertyOverrides = new[] { new StylePropertyTransition("opacity"), new StylePropertyTransition("translate") },
            };
            var (box, before, mounted) = PlayThenLand(new Poses(overrides, "opacity-0 translate-x-[0px]",
                "opacity-100 translate-x-[40px]", "opacity-100 translate-x-[20px]"), Opacity);
            using var _ = mounted;

            // Act
            Tick();
            Tick();

            // Assert — gated on the tween being mid-way, where landing and tweening read apart.
            var landed = Between(before, 0.05f, 0.45f) ? Opacity(box) : float.NaN;
            Assert.That(landed, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void Given_ATweenSwapTakingTranslateBackToRest_When_AZeroDurationPoseNamingTranslateFollows_Then_TranslateLandsByTheSecondFrame()
        {
            // Arrange — the play's target writes no translate, so the pose's is no repeat of it.
            var (box, before, mounted) = PlayThenLand(new Poses(s_tween, "opacity-0 translate-x-[40px]",
                "opacity-100", "opacity-50 translate-x-[20px]"), TranslateX);
            using var _ = mounted;

            // Act
            Tick();
            Tick();

            // Assert — gated on the tween being mid-way, where landing and tweening read apart.
            var landed = Between(before, 2f, 38f) ? TranslateX(box) : float.NaN;
            Assert.That(landed, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void Given_ATweenSwapThatHasSwapped_When_AZeroDurationPoseRepeatsItsTranslateInAnotherSpelling_Then_TranslateLandsByTheSecondFrame()
        {
            // Arrange — translate-x-4 and translate-x-[16px] write one value.
            var (box, before, mounted) = PlayThenLand(new Poses(s_tween, "opacity-0 translate-x-[0px]",
                "opacity-100 translate-x-4", "opacity-50 translate-x-[16px]"), TranslateX);
            using var _ = mounted;

            // Act
            Tick();
            Tick();

            // Assert — gated on the tween being mid-way, where landing and tweening read apart.
            var landed = Between(before, 1f, 15f) ? TranslateX(box) : float.NaN;
            Assert.That(landed, Is.EqualTo(16f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base's zero-duration swap re-resolved no variant layer.
        // So a rotate the play still drives kept the play's value through the landing.
        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ADrivenSwapRunningUnderAHoverRotateLayer_When_AZeroDurationPoseNamingOnlyOpacityFollows_Then_TheRotateStaysTheDriversOwn(
            TransitionType type)
        {
            // Arrange
            var (box, before, mounted) = PlayThenLand(new Poses(Driver(type), "opacity-0 rotate-0",
                "opacity-100 rotate-45", "opacity-50"), InlineRotate,
                element => StyleArbitraryValueResolver.ApplyClassToken(element, "rotate-[10deg]", StyleLayerPriority.Hover));
            using var _ = mounted;

            // Act
            var after = InlineRotate(box);

            // Assert — gated on the play's rotate reading apart from the layer's.
            var driven = Mathf.Abs(before - 10f) > 0.5f ? after : float.NaN;
            Assert.That(driven, Is.EqualTo(before).Within(1e-3f));
        }

        private static float Blur(VisualElement element)
        {
            var filter = element.resolvedStyle.filter.ToList();
            return filter.Count > 0 ? filter[0].GetParameter(0).floatValue : float.NaN;
        }

        [Test]
        public void Given_ATweenSwapThatHasSwapped_When_AZeroDurationPoseNamingABlurFollows_Then_TheBlurLandsByTheSecondFrame()
        {
            // Arrange
            var (box, _, mounted) = PlayThenLand(new Poses(s_tween, "opacity-0", "opacity-100", "blur-[8px]"), Opacity);
            using var __ = mounted;

            // Act
            Tick();
            Tick();

            // Assert
            Assert.That(Blur(box), Is.EqualTo(8f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): no filter tween of Velvet's runs on an element without transition-filter on the base.
        // Here it is the matching background-size entry the landing appends beside filter's that keeps the
        // landed blur the engine's.
        [Test]
        public void Given_ATweenSwapThatHasSwapped_When_AZeroDurationPoseNamingABlurFollows_Then_NoFilterTweenOfVelvetsTakesIt()
        {
            // Act
            var (box, _, mounted) = PlayThenLand(new Poses(s_tween, "opacity-0", "opacity-100", "blur-[8px]"), Opacity);
            using var __ = mounted;

            // Assert
            Assert.That(FilterTweenRuns(box), Is.False);
        }

        [Test]
        public void Given_ATweenSwapThatHasSwapped_When_AZeroDurationPoseRepeatingItsOpacityLandsBesideABlurChange_Then_TheBlurIsLeftToTheEngine()
        {
            // Arrange
            s_className = "blur-[2px]";
            var (box, _, mounted) = PlayThenLand(new Poses(s_tween, "opacity-0 translate-x-[0px]",
                "opacity-100 translate-x-[40px]", "opacity-100 translate-x-[20px]"), Opacity,
                _ => s_className = "blur-[6px]");
            using var __ = mounted;

            // Act
            Tick();

            // Assert — no filter tween of Velvet's runs, and the painted blur has not reached the 6 an instant write
            // would paint.
            Assert.That((FilterTweenRuns(box), Blur(box) < 5.99f), Is.EqualTo((false, true)));
        }

        private static bool FilterTweenRuns(VisualElement element)
        {
            var table = (ConditionalWeakTable<VisualElement, StyleFilterTransitionBinding>)typeof(StyleFilterTransitionDriver)
                .GetField("s_bindings", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            return table.TryGetValue(element, out var binding) && binding.Scheduled != null;
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ADrivenSwapSettledUnderAZeroDurationPoseLeavingOpacityUnnamed_When_ATweenPoseNamesAnOpacityClass_Then_ThatOpacityShows(
            TransitionType type)
        {
            // Arrange
            var (box, _, mounted) = PlayThenLand(new Poses(Driver(type), "opacity-[0.2]", "opacity-[0.8]",
                "translate-x-[20px]", Mid: "opacity-50"), Opacity);
            using var __ = mounted;
            AdvancePast(2f);

            // Act
            s_store.Set("mid");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(1f);

            // Assert
            Assert.That(Opacity(box), Is.EqualTo(0.5f).Within(1e-3f));
        }
    }
}
