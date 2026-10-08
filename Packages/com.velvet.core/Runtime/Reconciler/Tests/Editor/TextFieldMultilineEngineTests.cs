using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the UI Toolkit behaviour the multiline-after-delayed ordering in
    /// <c>FiberPropApplier.ApplyTextField</c> exists for, so this case fails, rather than the ordering going
    /// quietly unneeded, if the engine stops doing it.
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
    }
}
