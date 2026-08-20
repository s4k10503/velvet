using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the leading parameters of <see cref="V.TextField"/> to the order 2.1.0 shipped, so that a caller
    /// passing arguments positionally keeps binding each one to the parameter it bound to then. What the pin
    /// costs is that a parameter added later is appended after these rather than filed beside the one it
    /// belongs with by subject, and the four text-input parameters already are.
    /// <para/>
    /// A prefix rather than the whole list, because appending is the move that keeps those bindings. Position
    /// is held here, and the type standing at each position by <c>PublicApiSurfaceTests</c>: a positional
    /// argument rebinds on either moving.
    /// </summary>
    [TestFixture]
    internal sealed class TextFieldPositionalOrderTests
    {
        [Test]
        public void Given_TheParametersV210Declared_When_TextFieldIsDeclaredToday_Then_EachPositionNamesTheSameOne()
        {
            // Arrange
            var shipped = new[]
            {
                "className", "value", "onValueChanged", "key", "name", "label", "isPasswordField",
                "enabled", "refCallback", "whileHoverClass", "whileTapClass", "whileFocusClass",
                "data", "aria",
            };

            // Act
            var declared = typeof(V)
                .GetMethod(nameof(V.TextField), BindingFlags.Public | BindingFlags.Static)!
                .GetParameters()
                .Take(shipped.Length)
                .Select(parameter => parameter.Name);

            // Assert
            Assert.That(
                string.Join(", ", declared),
                Is.EqualTo(string.Join(", ", shipped)),
                "A parameter inserted among these rather than appended after them rebinds every positional "
                + "argument past it, and string? repeats here often enough that such a rebind need not fail "
                + "to compile.");
        }
    }
}
