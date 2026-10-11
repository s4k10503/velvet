using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.UIElements.TestFramework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when the size a percentage is taken of counts as definite, on boxes built by hand so that each
    /// declaration the answer turns on is the only one in the tree: a percentage of a definite size resolves, and
    /// of an indefinite one is handed back as the longhand's stand-in for it.
    /// </summary>
    internal sealed class RelativeLengthDefinitenessTests
    {
        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(400, 300) };
            _sim.ResetTimePerSimulatedFrameToDefault();
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private static VisualElement Add(VisualElement parent, Action<IStyle> declare)
        {
            var box = new VisualElement();
            declare(box.style);
            parent.Add(box);
            return box;
        }

        // Two wrappers declaring no height keep the box under test clear of the panel root, whose size the panel
        // decides.
        private VisualElement Wrapped()
        {
            var outer = Add(_sim.rootVisualElement, _ => { });
            return Add(outer, style =>
            {
                style.width = 300;
                style.minHeight = 200;
            });
        }

        private void Settle()
        {
            for (var i = 0; i < 3; i++)
            {
                _sim.FrameUpdateMs(16);
            }
        }

        // The keyword TryResolve hands back for a percentage plus pixels: Undefined for a length, Auto for the
        // indefinite stand-in.
        private static StyleKeyword KeywordOf(VisualElement element, string text, RelativeLengthAxis axis)
        {
            StyleLengthExpression.TryParse(text, out var expression);
            StyleRelativeLengths.TryResolve(element, expression!, axis, StyleKeyword.Auto, out var length);
            return length.keyword;
        }

        [Test]
        public void Given_AnAttachedElementNotYetLaidOut_When_ALengthIsResolved_Then_NothingIsResolved()
        {
            // Arrange
            var element = Add(_sim.rootVisualElement, _ => { });
            StyleLengthExpression.TryParse("calc(1em+1px)", out var expression);

            // Act
            var resolved = StyleRelativeLengths.TryResolve(element, expression!, RelativeLengthAxis.Width, null, out _);

            // Assert
            Assert.That((resolved, float.IsNaN(element.layout.width)), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AnEmHeightInAParentOfNoHeight_When_Resolved_Then_ItIsALengthRatherThanTheIndefiniteStandIn()
        {
            // Arrange
            var parent = Add(Wrapped(), style => style.fontSize = 10);
            var child = Add(parent, _ => { });
            Settle();

            // Act
            var keyword = KeywordOf(child, "calc(1em+1px)", RelativeLengthAxis.Height);

            // Assert
            Assert.That(keyword, Is.EqualTo(StyleKeyword.Undefined));
        }

        [Test]
        public void Given_AParentOfPixelHeightInAWrapperOfNoHeight_When_APercentageHeightIsResolved_Then_ItIsALength()
        {
            // Arrange
            var parent = Add(Wrapped(), style => style.height = 100);
            var child = Add(parent, _ => { });
            Settle();

            // Act
            var keyword = KeywordOf(child, "calc(50%+1px)", RelativeLengthAxis.Height);

            // Assert
            Assert.That(keyword, Is.EqualTo(StyleKeyword.Undefined));
        }

        [Test]
        public void Given_AParentOfPercentageHeightInAWrapperOfNoHeight_When_APercentageHeightIsResolved_Then_ItIsIndefinite()
        {
            // Arrange
            var parent = Add(Wrapped(), style => style.height = Length.Percent(50));
            var child = Add(parent, _ => { });
            Settle();

            // Act
            var keyword = KeywordOf(child, "calc(50%+1px)", RelativeLengthAxis.Height);

            // Assert
            Assert.That(keyword, Is.EqualTo(StyleKeyword.Auto));
        }

        [Test]
        public void Given_AnAbsoluteParentOfPercentageHeightInAWrapperOfNoHeight_When_APercentageHeightIsResolved_Then_ItIsALength()
        {
            // Arrange
            var parent = Add(Wrapped(), style =>
            {
                style.position = Position.Absolute;
                style.height = Length.Percent(50);
            });
            var child = Add(parent, _ => { });
            Settle();

            // Act
            var keyword = KeywordOf(child, "calc(50%+1px)", RelativeLengthAxis.Height);

            // Assert
            Assert.That(keyword, Is.EqualTo(StyleKeyword.Undefined));
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Given_AnAbsoluteParentOfNoSizePinnedOnOneSide_When_APercentageIsResolved_Then_ItIsIndefinite(
            bool vertical, bool leading)
        {
            // Arrange
            var parent = Add(Wrapped(), style =>
            {
                style.position = Position.Absolute;
                if (vertical && leading)
                {
                    style.top = 0;
                }
                else if (vertical)
                {
                    style.bottom = 0;
                }
                else if (leading)
                {
                    style.left = 0;
                }
                else
                {
                    style.right = 0;
                }
            });
            var child = Add(parent, _ => { });
            Settle();

            // Act
            var keyword = KeywordOf(child, "calc(50%+1px)", vertical ? RelativeLengthAxis.Height : RelativeLengthAxis.Width);

            // Assert
            Assert.That(keyword, Is.EqualTo(StyleKeyword.Auto));
        }

        [Test]
        public void Given_AGrownParentInAWrapperOfNoHeight_When_APercentageHeightIsResolved_Then_ItIsIndefinite()
        {
            // Arrange
            var parent = Add(Wrapped(), style => style.flexGrow = 1);
            var child = Add(parent, _ => { });
            Settle();

            // Act
            var keyword = KeywordOf(child, "calc(50%+1px)", RelativeLengthAxis.Height);

            // Assert
            Assert.That(keyword, Is.EqualTo(StyleKeyword.Auto));
        }

        // GREEN_ON_BASE(characterization): the base's generated table already gives each class a position of its own.
        [Test]
        public void Given_TheGeneratedUtilityTable_When_CascadePositionsAreRead_Then_NoTwoClassesShareOne()
        {
            // Arrange
            var table = (IDictionary)typeof(StyleUtilityProperties)
                .GetField("ByClassName", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

            // Act
            var positions = table.Values.Cast<ValueTuple<int, int>>().Select(entry => entry.Item2).ToList();

            // Assert
            Assert.That(positions.Count - positions.Distinct().Count(), Is.Zero);
        }
    }
}
