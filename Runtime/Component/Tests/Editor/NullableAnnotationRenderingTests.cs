using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins how the compiler flattens nullable reference flags onto a type tree, since that encoding belongs
    /// to Roslyn rather than to this repository and the surface file is rendered from it: a position read one
    /// off spells every annotation after it onto the wrong type.
    /// </summary>
    [TestFixture]
    internal sealed class NullableAnnotationRenderingTests
    {
        [Test]
        public void Given_KnownlyAnnotatedSignatures_When_Rendered_Then_EachSpellingMatchesItsDeclaration()
        {
            // Arrange
            var expected = string.Join("\n", new[]
            {
                "ctor Velvet.Tests.NullableShapes.ctor(): System.Void",
                "method Velvet.Tests.NullableShapes.Exchange(System.String, System.String?&): "
                + "System.Collections.Generic.List`1[System.String]?",
                "property Velvet.Tests.NullableShapes.ArrayOfNullable: System.String?[]",
                "property Velvet.Tests.NullableShapes.GenericValueTypeArgument: "
                + "System.Collections.Generic.KeyValuePair`2[System.Int32, System.String?]",
                "property Velvet.Tests.NullableShapes.ListOfGenericValueType: "
                + "System.Collections.Generic.List`1[System.Collections.Generic.KeyValuePair`2"
                + "[System.Int32, System.String?]]",
                "property Velvet.Tests.NullableShapes.ListOfNullable: "
                + "System.Collections.Generic.List`1[System.String?]",
                "property Velvet.Tests.NullableShapes.NullableArray: System.String[]?",
                "property Velvet.Tests.NullableShapes.NullableList: "
                + "System.Collections.Generic.List`1[System.String]?",
                "property Velvet.Tests.NullableShapes.NullableText: System.String?",
                "property Velvet.Tests.NullableShapes.NullableValueType: System.Nullable`1[System.Int32]",
                "property Velvet.Tests.NullableShapes.Text: System.String",
                "property Velvet.Tests.NullableShapes.ValueTypeArgument: "
                + "System.Collections.Generic.Dictionary`2[System.Int32, System.String?]",
            });

            // Act
            var rendered = string.Join("\n", PublicApiSurface.RenderMembersOf(typeof(NullableShapes)));

            // Assert
            Assert.That(rendered, Is.EqualTo(expected));
        }

        [Test]
        public void Given_SignaturesOutsideANullableContext_When_Rendered_Then_TheyAreSpeltObliviousRatherThanNonNull()
        {
            // Arrange
            var expected = "ctor Velvet.Tests.ObliviousShapes.ctor(): System.Void\n"
                           + "property Velvet.Tests.ObliviousShapes.Listed: "
                           + "System.Collections.Generic.List`1[System.String~]~\n"
                           + "property Velvet.Tests.ObliviousShapes.Text: System.String~";

            // Act
            var rendered = string.Join("\n", PublicApiSurface.RenderMembersOf(typeof(ObliviousShapes)));

            // Assert
            Assert.That(rendered, Is.EqualTo(expected));
        }
    }

    /// <summary>
    /// Signatures whose declarations are the expectations of
    /// <see cref="NullableAnnotationRenderingTests"/>. Each separates one rule of the encoding from the next.
    /// </summary>
    internal sealed class NullableShapes
    {
        public string Text => string.Empty;

        public string? NullableText => null;

        public int? NullableValueType => null;

        public List<string?> ListOfNullable => new();

        public List<string>? NullableList => null;

        public string?[] ArrayOfNullable => Array.Empty<string?>();

        public string[]? NullableArray => null;

        public Dictionary<int, string?> ValueTypeArgument => new();

        public KeyValuePair<int, string?> GenericValueTypeArgument => default;

        public List<KeyValuePair<int, string?>> ListOfGenericValueType => new();

        public List<string>? Exchange(string required, out string? optional)
        {
            optional = null;
            return required.Length == 0 ? null : new List<string>();
        }
    }

#nullable disable

    /// <summary>Signatures the compiler leaves unannotated, for the oblivious case.</summary>
    internal sealed class ObliviousShapes
    {
        public string Text => string.Empty;

        public List<string> Listed => new();
    }

#nullable restore
}
