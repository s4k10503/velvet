using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the leading parameters of <c>V.TextField</c> to the order and types 2.1.0 shipped, so that a
    /// caller passing arguments positionally keeps binding each one to the parameter it bound to then. What
    /// the pin costs is that a parameter added later is appended after these rather than filed beside the one
    /// it belongs with by subject, and the four text-input parameters already are.
    /// <para/>
    /// A prefix rather than the whole list, because appending is the move that keeps those bindings. Both the
    /// name and the type at each position, because a positional argument rebinds on either moving, and
    /// swapping two positions of the same type changes the names while leaving the type list identical.
    /// <para/>
    /// One factory, hence <c>Single</c>: a second overload would decide by argument type which one a
    /// positional call reaches, and this prefix would no longer be the whole answer.
    /// </summary>
    [TestFixture]
    internal sealed class TextFieldPositionalOrderTests
    {
        [Test]
        public void Given_TheParametersV210Declared_When_TextFieldIsDeclaredToday_Then_EachPositionIsUnmoved()
        {
            // Arrange
            var shipped = new (string Name, Type Type)[]
            {
                ("className", typeof(string)),
                ("value", typeof(string)),
                ("onValueChanged", typeof(Action<string>)),
                ("key", typeof(string)),
                ("name", typeof(string)),
                ("label", typeof(string)),
                ("isPasswordField", typeof(bool?)),
                ("enabled", typeof(bool?)),
                ("refCallback", typeof(Func<VisualElement, Action>)),
                ("whileHoverClass", typeof(string)),
                ("whileTapClass", typeof(string)),
                ("whileFocusClass", typeof(string)),
                ("data", typeof(IReadOnlyDictionary<string, string>)),
                ("aria", typeof(IReadOnlyDictionary<string, string>)),
            };

            // Act
            var declared = typeof(V)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == nameof(V.TextField))
                .GetParameters()
                .Take(shipped.Length)
                .Select(parameter => parameter.Name + ": " + parameter.ParameterType);

            // Assert
            Assert.That(
                string.Join("\n", declared),
                Is.EqualTo(string.Join("\n", shipped.Select(parameter => parameter.Name + ": " + parameter.Type))),
                "A parameter inserted among these rather than appended after them rebinds every positional "
                + "argument past it, and string repeats here often enough that such a rebind need not fail "
                + "to compile.");
        }
    }
}
