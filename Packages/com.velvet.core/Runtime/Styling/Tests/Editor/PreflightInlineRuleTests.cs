using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the inline rule border preflight shows its colours through, and the engine behaviours it relies on:
    /// a colour shown through it survives a restyle, it is not an inline value, an inline value stands over it
    /// whatever the value, and a side taken out of it gives the property back to the rules.
    /// </summary>
    internal sealed class PreflightInlineRuleTests : PanelTestBase
    {
        private PreflightInlineRule _rule;
        private Label _label;

        public override void SetUp()
        {
            base.SetUp();
            VelvetStyleUtilities.AttachTo(_window.rootVisualElement);
            _label = new Label { name = "tag" };
            _window.rootVisualElement.Add(_label);
            ForcePanelUpdate(_label.panel);
            _rule = PreflightInlineRule.Attach(_label);
        }

        public override void TearDown()
        {
            _rule.Release(_label);
            base.TearDown();
        }

        [Test]
        public void Given_AShownSide_When_TheElementIsRestyledByAClass_Then_TheSideStaysShown()
        {
            // Arrange
            _rule.Show(_label, 0, Color.magenta);

            // Act
            _label.AddToClassList("text-red-500");
            ForcePanelUpdate(_label.panel);

            // Assert
            Assert.That(_label.resolvedStyle.borderTopColor, Is.EqualTo(Color.magenta));
        }

        [Test]
        public void Given_AShownSide_When_CodeWritesTheSameColourInline_Then_TheWriteIsStored()
        {
            // Arrange
            _rule.Show(_label, 0, Color.magenta);

            // Act
            _label.style.borderTopColor = new StyleColor(Color.magenta);

            // Assert
            Assert.That(_label.style.borderTopColor.keyword, Is.EqualTo(StyleKeyword.Undefined));
        }

        [Test]
        public void Given_AShownSide_When_CodeWritesAnotherColourInline_Then_TheInlineColourStands()
        {
            // Arrange
            _rule.Show(_label, 0, Color.magenta);

            // Act
            _label.style.borderTopColor = new StyleColor(Color.green);
            _label.AddToClassList("text-red-500");
            ForcePanelUpdate(_label.panel);

            // Assert
            Assert.That(_label.resolvedStyle.borderTopColor, Is.EqualTo(Color.green));
        }

        [Test]
        public void Given_AShownSide_When_ItIsShownAgain_Then_NoInlineValueIsStored()
        {
            // Arrange
            _rule.Show(_label, 0, Color.magenta);

            // Act
            _rule.Show(_label, 0, Color.cyan);

            // Assert
            Assert.That(_label.style.borderTopColor.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AShownSide_When_ItIsHidden_Then_TheRulesGiveItBack()
        {
            // Arrange
            _rule.Show(_label, 0, Color.magenta);

            // Act
            _rule.Hide(_label, 0);

            // Assert
            Assert.That(_label.resolvedStyle.borderTopColor, Is.EqualTo(Color.clear));
        }

        [Test]
        public void Given_AShownSide_When_ItIsHidden_Then_TheOtherSidesStayShown()
        {
            // Arrange
            _rule.Show(_label, 0, Color.magenta);
            _rule.Show(_label, 1, Color.cyan);

            // Act
            _rule.Hide(_label, 0);
            _label.AddToClassList("text-red-500");
            ForcePanelUpdate(_label.panel);

            // Assert
            Assert.That(_label.resolvedStyle.borderRightColor, Is.EqualTo(Color.cyan));
        }

        [Test]
        public void Given_AShownSide_When_AnInlineValueWrittenOverItIsCleared_Then_TheSideIsLost()
        {
            // Arrange
            _rule.Show(_label, 0, Color.magenta);
            _label.style.borderTopColor = new StyleColor(Color.green);

            // Act
            _label.style.borderTopColor = new StyleColor(StyleKeyword.Null);

            // Assert
            Assert.That(_rule.Lost(_label), Is.True);
        }

        [Test]
        public void Given_ALostSide_When_Reasserted_Then_TheColourIsShownAgain()
        {
            // Arrange
            _rule.Show(_label, 0, Color.magenta);
            _label.style.borderTopColor = new StyleColor(Color.green);
            _label.style.borderTopColor = new StyleColor(StyleKeyword.Null);

            // Act
            _rule.Reassert(_label);

            // Assert
            Assert.That(_label.resolvedStyle.borderTopColor, Is.EqualTo(Color.magenta));
        }

        [Test]
        public void Given_AShownSide_When_TheRuleIsReleased_Then_TheRulesGiveItBack()
        {
            // Arrange
            _rule.Show(_label, 0, Color.magenta);

            // Act
            _rule.Release(_label);

            // Assert
            Assert.That(_label.resolvedStyle.borderTopColor, Is.EqualTo(Color.clear));
        }
    }
}
