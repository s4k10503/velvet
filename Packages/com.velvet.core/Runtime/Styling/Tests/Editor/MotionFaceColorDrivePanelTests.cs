using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A bezier play driving the background colour of a skewed V.Motion, whose skew layer suppresses the native
    /// background and repaints it sheared: the face shows the driven colour, as an unskewed twin playing the same
    /// play shows it inline, and the suppression stays in the slot under the drive.
    /// </summary>
    internal sealed class MotionFaceColorDrivePanelTests : PanelTestBase
    {
        private static readonly Dictionary<string, MotionVariant> s_poses = new()
        {
            ["hidden"] = "bg-[#ff0000]",
            ["visible"] = "bg-[#0000ff]",
        };

        private static VNode Driven(string name, string className) => V.Motion(name: name,
            className: "w-[40px] h-[40px] " + className, variants: s_poses, initial: "hidden", animate: "visible",
            transition: new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 4f });

        // Mounts the skewed Motion "b" beside its unskewed twin "c" on the fake clock, two seconds into their play.
        private (VisualElement Skewed, VisualElement Twin) MountPartWay()
        {
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: "flex flex-row", children: new VNode[]
            {
                Driven("b", "-skew-x-6 rotate-0"),
                Driven("c", string.Empty),
            }));
            var skewed = _window.rootVisualElement.Q("b");
            ForcePanelUpdate(skewed.panel);
            for (var i = 0; i < 2; i++)
            {
                UseFrameFakeClockHost.Ms += 1000;
                EditorPanelTestHelpers.DriveSchedulerOnce(skewed.panel);
                ForcePanelUpdate(skewed.panel);
            }
            return (skewed, _window.rootVisualElement.Q("c"));
        }

        // The twin's driven red, or NaN where its play is not part way, which no colour channel equals.
        private static float PartWay(VisualElement twin)
        {
            var red = twin.style.backgroundColor.value.r;
            return red > 0.05f && red < 0.95f ? red : float.NaN;
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; only a rotate class was added so it still reaches the painted skew layer.
        [Test]
        public void Given_ASkewedMotionDrivingItsBackground_When_ThePlayIsPartWay_Then_TheSkewFaceShowsTheDrivenColor()
        {
            // Arrange / Act
            var (skewed, twin) = MountPartWay();

            // Assert — -1 where no skew binding was built, which neither a channel nor PartWay's NaN equals.
            Assert.That(_mounted.Root.Reconciler.Context.SkewBindings.TryGetValue(skewed, out var binding)
                ? binding.BgColor.r
                : -1f, Is.EqualTo(PartWay(twin)).Within(1e-3f));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; only a rotate class was added so it still reaches the painted skew layer.
        [Test]
        public void Given_ASkewedMotionDrivingItsBackground_When_ThePlayIsPartWay_Then_TheSuppressionStaysInTheSlot()
        {
            // Arrange / Act
            var (skewed, twin) = MountPartWay();

            // Assert — read as false where the twin's play is not part way, so a play that never ran cannot pass.
            Assert.That(!float.IsNaN(PartWay(twin)) && SilhouetteFace.IsSentinel(skewed.style.backgroundColor.value),
                Is.True);
        }
    }
}
