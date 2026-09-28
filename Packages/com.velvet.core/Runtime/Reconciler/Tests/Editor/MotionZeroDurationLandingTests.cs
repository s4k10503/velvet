using System.Collections.Generic;
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
            return V.Motion(name: "m", className: s_className, variants: s_poses, animate: label,
                transition: s_transition);
        }

        private readonly record struct Poses(StyleTransitionConfig Transition, string From, string To, string Land,
            int Frames = 3);

        // Mounts the box at From, plays the swap to To on Transition for Frames frames, then changes the label to
        // Land, whose pose has zero duration. Returns the box, what read gave just before Land, and the mount.
        private (VisualElement box, float before, MountedTree mounted) PlayThenLand(Poses poses,
            System.Func<VisualElement, float> read)
        {
            s_transition = poses.Transition;
            s_poses = new Dictionary<string, MotionVariant>
            {
                ["from"] = poses.From,
                ["to"] = poses.To,
                ["land"] = new MotionVariant(poses.Land, StyleTransitionConfig.None),
            };
            s_store = new LabelStore();
            var mounted = V.Mount(Root, V.Component(PoseBox, key: "root"));
            Tick();
            s_store.Set("to");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            for (var i = 0; i < poses.Frames; i++)
            {
                Tick();
            }
            var box = Root.Q<VisualElement>("m");
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
    }
}
