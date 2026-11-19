using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a custom property reader holds when it is built without saying it reads a theme class.
    /// </summary>
    internal sealed class CustomPropertyConsumerDefaultsTests
    {
        // GREEN_ON_BASE(characterization): the base's reader of a shadow bracket carries layers when built with the
        // parameter's default. Change `bool themed = false` to `true` in the Consumer constructor and it carries none.
        [Test]
        public void Given_AShadowBracketReadingAVariable_When_ItsReaderIsBuiltWithTheDefaultThemedArgument_Then_ItCarriesShadowLayers()
        {
            // Arrange
            var consumer = typeof(StyleCustomProperties).GetNestedType("Consumer", BindingFlags.NonPublic)!;
            var constructor = consumer.GetConstructors()[0];
            var parameters = constructor.GetParameters();

            // Act
            var built = constructor.Invoke(new object[]
            {
                "shadow-[0_1px_2px_var(--x)]", 0L, 0, 0, null, parameters[parameters.Length - 1].DefaultValue,
            });

            // Assert
            Assert.That((bool)consumer.GetField("ShadowLayers")!.GetValue(built)!, Is.True);
        }
    }
}
