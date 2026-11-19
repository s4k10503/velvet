using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A bezier play driving the background colour of a skewed V.Motion: the face shows the driven colour, as an
    /// unskewed twin playing the same play shows it.
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
                Driven("b", "-skew-x-6"),
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

        [Test]
        public void Given_ASkewedMotionDrivingItsBackground_When_ThePlayIsPartWay_Then_ItShowsTheColourItsUnskewedTwinShows()
        {
            // Arrange
            var (skewed, twin) = MountPartWay();

            // Act
            var shown = skewed.style.backgroundColor.value.r;

            // Assert
            Assert.That(shown, Is.EqualTo(PartWay(twin)).Within(1e-3f));
        }
    }
}
