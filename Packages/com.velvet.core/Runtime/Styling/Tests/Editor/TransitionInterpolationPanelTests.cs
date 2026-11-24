using System;
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

        // Positions of the application's own, Velvet bundling no background-position utility.
        private const string PositionRules =
            ".app-left { background-position-x: left; } .app-right { background-position-x: right; }"
            + " .app-left-10 { background-position-x: left 10px; } .app-left-30 { background-position-x: left 30px; }"
            + " .app-right-10 { background-position-x: right 10px; } .app-right-30 { background-position-x: right 30px; }"
            + " .app-top { background-position-y: top; } .app-bottom { background-position-y: bottom; }";

        private StyleSheet _sheet;
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
            if (_sheet != null)
            {
                UnityEngine.Object.DestroyImmediate(_sheet);
                _sheet = null;
            }
            base.TearDown();
        }

        // An element classNames give, in a tree mounted on the window's root, styled on a clock this fixture moves.
        private VisualElement Mounted(string classNames)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "leaf", className: classNames));
            var element = _window.rootVisualElement.Q<VisualElement>("leaf");
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
    }
}
