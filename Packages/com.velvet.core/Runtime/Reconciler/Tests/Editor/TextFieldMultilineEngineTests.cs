using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the UI Toolkit behaviour <c>FiberPropApplier.ApplyTextField</c>'s multiline-after-delayed ordering
    /// and arrangements in <see cref="TextFieldMultilineKeyboardPropTests"/> are written against, so a case
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
    }
}
