using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// A bracket typed as a length, with a numeric modifier, is not read as a colour: the modifier is no opacity there.
    /// </summary>
    internal sealed class RetypesColourLengthModifierTests
    {
        // GREEN_ON_BASE(characterization): the base already declines to read a length-typed bracket as a colour.
        [Test]
        public void Given_ATextBracketTypedAsALengthReadingAVariableWithANumericModifier_When_Asked_Then_ItIsNotReadAsAColour()
        {
            // Act
            var retypes = StyleCustomPropertyClass.RetypesColour("text-[length:var(--x)]/7");

            // Assert
            Assert.That(retypes, Is.False);
        }
    }
}
