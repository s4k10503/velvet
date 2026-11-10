using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the names a bracketed custom property declaration (<c>[--name:value]</c>) takes: CSS allows a custom
    /// property name upper-case letters, digits and underscores, where a standard property name takes none.
    /// </summary>
    internal sealed class CustomPropertyNameTests
    {
        // GREEN_ON_BASE(characterization): the base takes a capital, a digit and an underscore in a custom property name.
        [Test]
        public void Given_ACustomPropertyNameWithACapitalADigitAndAnUnderscore_When_Read_Then_ItIsADeclaration()
        {
            // Act
            var declared = StyleCustomPropertyClass.TryDeclaration("[--Brand_2:#ff0000]", out var name, out _);

            // Assert — the name where it declares, which the expected one is not where it does not.
            Assert.That(declared ? name : null, Is.EqualTo("--Brand_2"));
        }
    }
}
