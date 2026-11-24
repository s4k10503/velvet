using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins CSS's interpolation of visibility and background-position on what Velvet renders, both of which UI Toolkit
    /// runs a transition of as a switch partway through: a transition from or to visible shows visible while it runs,
    /// and a background-position slides.
    /// </summary>
    internal sealed class TransitionInterpolationPanelTests : PanelTestBase
    {
        private const string OneSecond = "duration-1000 ease-linear";

        // The box the slides between a length and a percentage run in.
        private const string Box = "w-[120px] h-[60px] ";

        // Positions of the application's own, Velvet bundling no background-position utility.
        private const string PositionRules =
            ".app-left { background-position-x: left; } .app-right { background-position-x: right; }"
            + " .app-left-10 { background-position-x: left 10px; } .app-left-30 { background-position-x: left 30px; }"
            + " .app-right-10 { background-position-x: right 10px; } .app-right-30 { background-position-x: right 30px; }"
            + " .app-top { background-position-y: top; } .app-bottom { background-position-y: bottom; }"
            + " .app-center { background-position-x: center; } .app-right-20p { background-position-x: right 20%; }"
            + " .app-top-10 { background-position-y: top 10px; }"
            + " .app-size-25-10 { background-size: 25px 10px; } .app-size-35-10 { background-size: 35px 10px; }"
            + " .app-contain { background-size: contain; } .app-cover { background-size: cover; }"
            + " .app-size-half { background-size: 50%; } .app-size-auto-15 { background-size: auto 15px; }"
            + " .app-size-auto { background-size: auto; } .app-size-35 { background-size: 35px; }"
            + " .app-round { background-repeat: round; } .app-round-y { background-repeat: no-repeat round; }"
            + " .app-round-x { background-repeat: round no-repeat; }";

        private StyleSheet _sheet;
        private readonly List<Texture2D> _images = new();
        private MountedTree _portalMount;
        private double _now;

        protected override void LoadStyleSheets()
        {
            VelvetStyleUtilities.AttachTo(_window.rootVisualElement);
            _sheet = ScriptableObject.CreateInstance<StyleSheet>();
            var impl = Activator.CreateInstance(StockStyleSheetImport.Impl!, true)!;
            try
            {
                StockStyleSheetImport.Import!.Invoke(impl, new object[] { _sheet, PositionRules });
            }
            catch (TargetInvocationException)
            {
                // As TransitionDiscretePanelTests.AttachSheet takes the sheet.
            }
            _window.rootVisualElement.styleSheets.Add(_sheet);
        }

        public override void TearDown()
        {
            _portalMount?.Dispose();
            _portalMount = null;
            if (_sheet != null)
            {
                UnityEngine.Object.DestroyImmediate(_sheet);
                _sheet = null;
            }
            try
            {
                base.TearDown();
            }
            finally
            {
                foreach (var image in _images)
                {
                    UnityEngine.Object.DestroyImmediate(image);
                }
                _images.Clear();
            }
        }

        // An element classNames give, in a tree mounted on the window's root, styled on a clock this fixture moves;
        // showing image where one is given, and with what inline writes before its first style pass.
        private VisualElement Mounted(string classNames, Texture2D image = null, Action<VisualElement> inline = null)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "leaf", className: classNames,
                styles: image == null ? null : new StyleOverrides { BackgroundImage = new StyleBackground(image) }));
            var element = _window.rootVisualElement.Q<VisualElement>("leaf");
            inline?.Invoke(element);
            return Attached(element);
        }

        private Texture2D Image(int width, int height)
        {
            var image = new Texture2D(width, height);
            _images.Add(image);
            return image;
        }

        // A slide from ten pixels from the left to the right edge, a quarter of the way in: CSS's value there is a
        // quarter of the room the image leaves plus 7.5px. The offset it reads is in pixels from the left edge.
        private float AQuarterIntoTenPixelsToRight(string classNames, Texture2D image = null,
            Action<VisualElement> inline = null)
        {
            var element = Mounted(Box + "transition-all app-left-10 " + classNames + " " + OneSecond, image, inline);
            Swap(element, "app-left-10", "app-right");
            RunFor(element, 0.25);
            return OffsetWhileItRuns(element, "background-position-x", BackgroundPositionKeyword.Left, LengthUnit.Pixel);
        }

        // The same, as a row a portal puts in an element of another mounted tree, so that two holds are above it.
        private VisualElement PortedUnderAMount(string classNames)
        {
            var outer = new VisualElement();
            _window.rootVisualElement.Add(outer);
            _mounted = V.Mount(outer, V.Div(name: "target"));
            var target = outer.Q<VisualElement>("target");
            var source = new VisualElement();
            _window.rootVisualElement.Add(source);
            _portalMount = V.Mount(source, V.Portal(target, new VNode[] { V.Div(name: "leaf", className: classNames) }));
            return Attached(target.Q<VisualElement>("leaf"));
        }

        private VisualElement Attached(VisualElement element)
        {
            _now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => _now);
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            return element;
        }

        // The style pass that starts the transitions a change asks for, and the panel's animation update seconds on.
        private void RunFor(VisualElement element, double seconds)
        {
            ForcePanelUpdate(element.panel);
            _now += seconds;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
        }

        private static void Swap(VisualElement element, string from, string to)
        {
            element.RemoveFromClassList(from);
            element.AddToClassList(to);
        }

        private static bool Runs(VisualElement element, string property)
        {
            var id = EngineMember.StylePropertyNameId.ResolveProperty()!.GetValue(new StylePropertyName(property));
            return (bool)EngineMember.HasRunningStyleAnimation.ResolveMethod()!.Invoke(element, new[] { id });
        }

        // The resolved visibility while its transition runs, so that a change nothing transitioned cannot pass for one
        // held visible; -1 where none runs.
        private static Visibility VisibilityWhileItRuns(VisualElement element)
            => Runs(element, "visibility") ? element.resolvedStyle.visibility : (Visibility)(-1);

        // The resolved position's offset from edge in unit while its transition runs; NaN where none runs or the
        // position is given otherwise.
        private static float OffsetWhileItRuns(VisualElement element, string property, BackgroundPositionKeyword edge,
            LengthUnit unit)
        {
            if (!Runs(element, property))
            {
                return float.NaN;
            }
            var position = property == "background-position-y"
                ? element.resolvedStyle.backgroundPositionY
                : element.resolvedStyle.backgroundPositionX;
            return position.keyword == edge && position.offset.unit == unit ? position.offset.value : float.NaN;
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_InvisibleIsAddedAndItsFirstFrameIsThreeQuartersIn_Then_ItIsVisible()
        {
            // Arrange
            var element = Mounted("transition-all " + OneSecond);
            element.AddToClassList("invisible");

            // Act
            RunFor(element, 0.75);

            // Assert
            Assert.That(VisibilityWhileItRuns(element), Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_InvisibleIsAddedAndAFrameFallsThreeQuartersIn_Then_ItIsVisible()
        {
            // Arrange
            var element = Mounted("transition-all " + OneSecond);
            element.AddToClassList("invisible");
            RunFor(element, 0.25);

            // Act
            RunFor(element, 0.5);

            // Assert
            Assert.That(VisibilityWhileItRuns(element), Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void Given_AnInvisibleElementTransitioningAll_When_VisibleReplacesInvisible_Then_AQuarterInItIsVisible()
        {
            // Arrange
            var element = Mounted("transition-all invisible " + OneSecond);
            Swap(element, "invisible", "visible");

            // Act
            RunFor(element, 0.25);

            // Assert
            Assert.That(VisibilityWhileItRuns(element), Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void Given_AFadeInAQuarterIn_When_ItIsReversed_Then_ItIsHiddenOnceAQuarterSecondHasPassed()
        {
            // Arrange — CSS shortens the reversal to the quarter of the fade-in that ran.
            var element = Mounted("transition-all invisible " + OneSecond);
            Swap(element, "invisible", "visible");
            RunFor(element, 0.25);
            var shown = element.resolvedStyle.visibility == Visibility.Visible;
            Swap(element, "visible", "invisible");

            // Act
            RunFor(element, 0.3);

            // Assert — the fade-in a reversal shortens is one that showed the element.
            Assert.That(shown ? element.resolvedStyle.visibility : (Visibility)(-1), Is.EqualTo(Visibility.Hidden));
        }

        [Test]
        public void Given_TransitionAllWithTransitionDiscrete_When_InvisibleIsAdded_Then_ThreeQuartersInItIsVisible()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete " + OneSecond);
            element.AddToClassList("invisible");

            // Act
            RunFor(element, 0.75);

            // Assert — on an element transition-discrete applies to.
            Assert.That(StyleTransitionBehavior.AllowsDiscrete(element) ? VisibilityWhileItRuns(element) : (Visibility)(-1),
                Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_LeftBecomesRight_Then_AQuarterInItIsAQuarterOfTheWay()
        {
            // Arrange
            var element = Mounted("transition-all app-left " + OneSecond);
            Swap(element, "app-left", "app-right");

            // Act
            RunFor(element, 0.25);

            // Assert
            Assert.That(OffsetWhileItRuns(element, "background-position-x", BackgroundPositionKeyword.Left,
                LengthUnit.Percent), Is.EqualTo(25f).Within(0.01f));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_LeftBecomesRightAndAFrameFallsThreeQuartersIn_Then_ItIsThreeQuartersOfTheWay()
        {
            // Arrange
            var element = Mounted("transition-all app-left " + OneSecond);
            Swap(element, "app-left", "app-right");
            RunFor(element, 0.25);

            // Act
            RunFor(element, 0.5);

            // Assert
            Assert.That(OffsetWhileItRuns(element, "background-position-x", BackgroundPositionKeyword.Left,
                LengthUnit.Percent), Is.EqualTo(75f).Within(0.01f));
        }

        [Test]
        public void Given_ASlideThreeQuartersIn_When_ItIsReversed_Then_HalfOfTheReversalInItIsHalfWayBack()
        {
            // Arrange — CSS shortens the reversal to the 0.75s of the slide that ran.
            var element = Mounted("transition-all app-left " + OneSecond);
            Swap(element, "app-left", "app-right");
            RunFor(element, 0.75);
            Swap(element, "app-right", "app-left");

            // Act
            RunFor(element, 0.375);

            // Assert
            Assert.That(OffsetWhileItRuns(element, "background-position-x", BackgroundPositionKeyword.Left,
                LengthUnit.Percent), Is.EqualTo(37.5f).Within(0.01f));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_TenPixelsFromTheLeftBecomesThirty_Then_AQuarterInItIsFifteen()
        {
            // Arrange
            var element = Mounted("transition-all app-left-10 " + OneSecond);
            Swap(element, "app-left-10", "app-left-30");

            // Act
            RunFor(element, 0.25);

            // Assert
            Assert.That(OffsetWhileItRuns(element, "background-position-x", BackgroundPositionKeyword.Left,
                LengthUnit.Pixel), Is.EqualTo(15f).Within(0.01f));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_TenPixelsFromTheRightBecomesThirty_Then_AQuarterInItIsFifteen()
        {
            // Arrange
            var element = Mounted("transition-all app-right-10 " + OneSecond);
            Swap(element, "app-right-10", "app-right-30");

            // Act
            RunFor(element, 0.25);

            // Assert
            Assert.That(OffsetWhileItRuns(element, "background-position-x", BackgroundPositionKeyword.Right,
                LengthUnit.Pixel), Is.EqualTo(15f).Within(0.01f));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_TopBecomesBottom_Then_AQuarterInItIsAQuarterOfTheWay()
        {
            // Arrange
            var element = Mounted("transition-all app-top " + OneSecond);
            Swap(element, "app-top", "app-bottom");

            // Act
            RunFor(element, 0.25);

            // Assert
            Assert.That(OffsetWhileItRuns(element, "background-position-y", BackgroundPositionKeyword.Top,
                LengthUnit.Percent), Is.EqualTo(25f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base shows a fade-in's start at its first frame, as CSS does at progress 0.
        // Changing `progress <= 0f` to `progress < 0f` in StyleTransitionInterpolation's VisibilityRun.Select reddens it.
        [Test]
        public void Given_AnInvisibleElementTransitioningAll_When_AnAnimationUpdateComesAtTheStartOfItsFadeIn_Then_ItIsHidden()
        {
            // Arrange
            var element = Mounted("transition-all invisible " + OneSecond);
            Swap(element, "invisible", "visible");

            // Act
            RunFor(element, 0.0);

            // Assert
            Assert.That(VisibilityWhileItRuns(element), Is.EqualTo(Visibility.Hidden));
        }

        [Test]
        public void Given_AFadeInOnAJumpStartStep_When_AnAnimationUpdateComesAtItsStart_Then_ItIsVisible()
        {
            // Arrange — the step jumps to a quarter at progress 0, which CSS shows as visible.
            var element = Mounted("transition-all invisible duration-1000 ease-[steps(4,jump-start)]");
            Swap(element, "invisible", "visible");

            // Act
            RunFor(element, 0.0);

            // Assert
            Assert.That(VisibilityWhileItRuns(element), Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void Given_AFadeInPastHalfWay_When_ItIsReversed_Then_ItIsVisibleUntilTheReversalItRanFor()
        {
            // Arrange — a frame a tenth in and one six tenths in; CSS shortens the reversal to 0.6s.
            var element = Mounted("transition-all invisible " + OneSecond);
            Swap(element, "invisible", "visible");
            RunFor(element, 0.1);
            RunFor(element, 0.5);
            Swap(element, "visible", "invisible");

            // Act
            RunFor(element, 0.4);

            // Assert
            Assert.That(VisibilityWhileItRuns(element), Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void Given_ASlideReversedHalfWayBack_When_ItIsReversedAgain_Then_HalfOfThatReversalInItIsHalfWayOut()
        {
            // Arrange — CSS runs the second reversal for 0.625s, from 37.5% back out to 100%.
            var element = Mounted("transition-all app-left " + OneSecond);
            Swap(element, "app-left", "app-right");
            RunFor(element, 0.75);
            Swap(element, "app-right", "app-left");
            RunFor(element, 0.375);
            Swap(element, "app-left", "app-right");

            // Act
            RunFor(element, 0.3125);

            // Assert
            Assert.That(OffsetWhileItRuns(element, "background-position-x", BackgroundPositionKeyword.Left,
                LengthUnit.Percent), Is.EqualTo(68.75f).Within(0.01f));
        }

        [Test]
        public void Given_ARowAPortalPutsUnderAMount_When_LeftBecomesRight_Then_ThreeQuartersInItIsThreeQuartersOfTheWay()
        {
            // Arrange
            var element = PortedUnderAMount("transition-all app-left " + OneSecond);
            Swap(element, "app-left", "app-right");

            // Act
            RunFor(element, 0.75);

            // Assert
            Assert.That(OffsetWhileItRuns(element, "background-position-x", BackgroundPositionKeyword.Left,
                LengthUnit.Percent), Is.EqualTo(75f).Within(0.01f));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_CenterBecomesTwentyPercentFromTheRight_Then_AQuarterInItIsAQuarterOfTheWay()
        {
            // Arrange — from 50% to 80%.
            var element = Mounted("transition-all app-center " + OneSecond);
            Swap(element, "app-center", "app-right-20p");

            // Act
            RunFor(element, 0.25);

            // Assert
            Assert.That(OffsetWhileItRuns(element, "background-position-x", BackgroundPositionKeyword.Left,
                LengthUnit.Percent), Is.EqualTo(57.5f).Within(0.01f));
        }

        [Test]
        public void Given_TheDefaultBackgroundSize_When_TenPixelsFromTheLeftBecomesTheRightEdge_Then_AQuarterInItIsAQuarterOfTheWay()
        {
            // Arrange — the image stretched over the box leaves no room, so the length alone moves: 7.5px.
            var image = Image(40, 20);

            // Act
            var offset = AQuarterIntoTenPixelsToRight("", image);

            // Assert
            Assert.That(offset, Is.EqualTo(7.5f).Within(0.01f));
        }

        [Test]
        public void Given_AnElementShowingNoImage_When_CenterBecomesTenPixelsFromTheLeft_Then_AQuarterInItIsAQuarterOfTheWay()
        {
            // Arrange — from 50% to 10px, with no image to leave room: a quarter of the 10px.
            var element = Mounted(Box + "transition-all app-center " + OneSecond);
            Swap(element, "app-center", "app-left-10");

            // Act
            RunFor(element, 0.25);

            // Assert
            Assert.That(OffsetWhileItRuns(element, "background-position-x", BackgroundPositionKeyword.Left,
                LengthUnit.Pixel), Is.EqualTo(2.5f).Within(0.01f));
        }

        [Test]
        public void Given_ABackgroundSizeInPixels_When_TenPixelsFromTheLeftBecomesTheRightEdge_Then_AQuarterInItIsAQuarterOfTheWay()
        {
            // Arrange — a 25px tile leaves 95px: 23.75px plus 7.5px.
            var image = Image(40, 20);

            // Act
            var offset = AQuarterIntoTenPixelsToRight("app-size-25-10", image);

            // Assert
            Assert.That(offset, Is.EqualTo(31.25f).Within(0.01f));
        }

        [Test]
        public void Given_AContainedImage_When_TenPixelsFromTheLeftBecomesTheRightEdge_Then_AQuarterInItIsAQuarterOfTheWay()
        {
            // Arrange — a square image contained in 120px by 60px is 60px wide, leaving 60px: 15px plus 7.5px.
            var image = Image(20, 20);

            // Act
            var offset = AQuarterIntoTenPixelsToRight("app-contain", image);

            // Assert
            Assert.That(offset, Is.EqualTo(22.5f).Within(0.01f));
        }

        [Test]
        public void Given_ACoveringImage_When_TenPixelsFromTheLeftBecomesTheRightEdge_Then_AQuarterInItIsAQuarterOfTheWay()
        {
            // Arrange — a 4:1 image covering 120px by 60px is 240px wide, leaving -120px: -30px plus 7.5px.
            var image = Image(40, 10);

            // Act
            var offset = AQuarterIntoTenPixelsToRight("app-cover", image);

            // Assert
            Assert.That(offset, Is.EqualTo(-22.5f).Within(0.01f));
        }

        [Test]
        public void Given_ABackgroundWidthInPercent_When_TenPixelsFromTheLeftBecomesTheRightEdge_Then_AQuarterInItIsAQuarterOfTheWay()
        {
            // Arrange — half of 120px leaves 60px: 15px plus 7.5px.
            var image = Image(40, 20);

            // Act
            var offset = AQuarterIntoTenPixelsToRight("app-size-half", image);

            // Assert
            Assert.That(offset, Is.EqualTo(22.5f).Within(0.01f));
        }

        [Test]
        public void Given_ABackgroundHeightAlone_When_TenPixelsFromTheLeftBecomesTheRightEdge_Then_AQuarterInItIsAQuarterOfTheWay()
        {
            // Arrange — a 2:1 image 15px high is 30px wide, leaving 90px: 22.5px plus 7.5px.
            var image = Image(40, 20);

            // Act
            var offset = AQuarterIntoTenPixelsToRight("app-size-auto-15", image);

            // Assert
            Assert.That(offset, Is.EqualTo(30f).Within(0.01f));
        }

        [Test]
        public void Given_ARoundRepeat_When_TenPixelsFromTheLeftBecomesTheRightEdge_Then_AQuarterInItIsAQuarterOfTheWay()
        {
            // Arrange — three 35px tiles round to 40px in 120px, leaving 80px: 20px plus 7.5px.
            var image = Image(40, 20);

            // Act
            var offset = AQuarterIntoTenPixelsToRight("app-size-35-10 app-round", image);

            // Assert
            Assert.That(offset, Is.EqualTo(27.5f).Within(0.01f));
        }

        [Test]
        public void Given_AVerticalRoundRepeatOfAnAutoWidth_When_TenPixelsFromTheLeftBecomesTheRightEdge_Then_TheWidthKeepsTheProportions()
        {
            // Arrange — a 40px by 25px image rounds to two 30px rows in 60px and widens to 48px, leaving 72px: 18px
            // plus 7.5px.
            var image = Image(40, 25);

            // Act
            var offset = AQuarterIntoTenPixelsToRight("app-size-auto app-round-y", image);

            // Assert
            Assert.That(offset, Is.EqualTo(25.5f).Within(0.01f));
        }

        [Test]
        public void Given_AHorizontalRoundRepeatOfAnAutoHeight_When_TenPixelsFromTheTopBecomesTheBottomEdge_Then_TheHeightKeepsTheProportions()
        {
            // Arrange — a 40px by 25px image 35px wide is 21.875px high; three columns round it to 40px, and 25px high,
            // leaving 35px of 60px: 8.75px plus 7.5px.
            var element = Mounted(Box + "transition-all app-top-10 app-size-35 app-round-x " + OneSecond, Image(40, 25));
            Swap(element, "app-top-10", "app-bottom");

            // Act
            RunFor(element, 0.25);

            // Assert
            Assert.That(OffsetWhileItRuns(element, "background-position-y", BackgroundPositionKeyword.Top,
                LengthUnit.Pixel), Is.EqualTo(16.25f).Within(0.01f));
        }

        [Test]
        public void Given_ABackgroundSizeWithANoneHeight_When_TenPixelsFromTheLeftBecomesTheRightEdge_Then_TheImageKeepsItsOwnSize()
        {
            // Arrange — a none length leaves the 40px image as it is, leaving 80px: 20px plus 7.5px.
            var image = Image(40, 20);

            // Act
            var offset = AQuarterIntoTenPixelsToRight("", image,
                element => element.style.backgroundSize = new BackgroundSize(Length.Pixels(25f), Length.None()));

            // Assert
            Assert.That(offset, Is.EqualTo(27.5f).Within(0.01f));
        }
    }
}
