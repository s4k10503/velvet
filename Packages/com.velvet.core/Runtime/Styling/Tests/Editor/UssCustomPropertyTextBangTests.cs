using System.Reflection;
using NUnit.Framework;
using Velvet.Editor;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a value written as nothing but <c>!important</c> leaves no text behind it to be read for adjacent strings.
    /// </summary>
    internal sealed class UssCustomPropertyTextBangTests
    {
        // GREEN_ON_BASE(characterization): the base already strips an `!important` standing at the start of the value.
        // Change `bang >= 0` to `bang > 0` in WithoutComments and the text stays `!important`.
        [Test]
        public void Given_AValueThatIsOnlyImportant_When_ItsCommentsAreTakenOut_Then_NothingIsLeft()
        {
            // Arrange
            var withoutComments = typeof(UssCustomPropertyText).GetMethod("WithoutComments", BindingFlags.NonPublic | BindingFlags.Static)!;

            // Act
            var left = (string)withoutComments.Invoke(null, new object[] { "!important" })!;

            // Assert
            Assert.That(left, Is.Empty);
        }
    }
}
