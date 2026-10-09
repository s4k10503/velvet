using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the UI Toolkit behaviour <c>FiberPropApplier.ApplyTextField</c>'s multiline-after-delayed ordering,
    /// <c>FiberPropApplier.ShowsItsValue</c>'s forms and arrangements in
    /// <see cref="TextFieldMultilineKeyboardPropTests"/> are written against, so a case
    /// here fails, rather than the code going quietly wrong or unneeded, if the engine stops doing it.
    /// </summary>
    internal sealed class TextFieldMultilineEngineTests
    {
        // GREEN_ON_BASE(characterization): the engine dropping line breaks from the shown text as multiline
        // turns off, which UI Toolkit did before this change as after it.
        [Test]
        public void Given_AMultilineFieldShowingALineBreak_When_MultilineIsTurnedOff_Then_TheShownTextLosesIt()
        {
            // Arrange
            var field = new TextField { multiline = true };
            field.value = "first\nsecond";
            var before = ((TextElement)field.textEdition).text;

            // Act
            field.multiline = false;

            // Assert
            Assert.That(
                (before, ((TextElement)field.textEdition).text),
                Is.EqualTo(("first\nsecond", "firstsecond")));
        }

        // GREEN_ON_BASE(characterization): a limit written after the value cuts the value with its break
        // still in it, which UI Toolkit did before this change as after it.
        [Test]
        public void Given_ASingleLineFieldHoldingAValueWithALineBreak_When_ItsLimitIsWrittenAfterTheValue_Then_TheShownTextIsTheValueCutWithItsBreak()
        {
            // Arrange — the silent setter, because that is the one the reconciler writes a value through.
            var field = new TextField();
            field.SetValueWithoutNotify("a\nbcdef");
            var before = ((TextElement)field.textEdition).text;

            // Act
            field.maxLength = 3;

            // Assert — the reading before the limit is folded in because it is the form the limit replaces.
            Assert.That(
                (before, ((TextElement)field.textEdition).text),
                Is.EqualTo(("abcdef", "a\nb")));
        }

        // GREEN_ON_BASE(characterization): a value written under a limit drops the break before the cut.
        // UI Toolkit did so before this change as after it.
        [Test]
        public void Given_ASingleLineFieldWithALimit_When_AValueWithALineBreakIsWrittenSilently_Then_TheShownTextIsTheValueWithoutItsBreakCut()
        {
            // Arrange
            var field = new TextField { maxLength = 3 };

            // Act
            field.SetValueWithoutNotify("a\nbcdef");

            // Assert
            Assert.That(((TextElement)field.textEdition).text, Is.EqualTo("abc"));
        }

        // GREEN_ON_BASE(characterization): a multi-line field shows a value written under a limit cut with its break.
        // UI Toolkit did so before this change as after it.
        [Test]
        public void Given_AMultilineFieldWithALimit_When_AValueWithALineBreakIsWrittenSilently_Then_TheShownTextIsTheValueCutWithItsBreak()
        {
            // Arrange
            var field = new TextField { multiline = true, maxLength = 3 };

            // Act
            field.SetValueWithoutNotify("a\nbcdef");

            // Assert
            Assert.That(((TextElement)field.textEdition).text, Is.EqualTo("a\nb"));
        }
    }
}
