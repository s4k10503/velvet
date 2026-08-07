#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Reads back the nullable reference annotation the compiler emitted for a method's return type, so a
    /// fixture can pin a shipped signature's nullability the way <c>PublicAPI.txt</c> pins its shape.
    /// </summary>
    /// <remarks>
    /// <c>PublicAPI.txt</c> renders a return type without its annotation, so nothing else in the repository
    /// pins one. The compiler catches the disagreement when the member implements an interface (CS8766); it
    /// reported nothing for <see cref="Hooks.UseLocation"/>, whose null arrived through a null-forgiving
    /// operator one call away.
    /// </remarks>
    public static class NullableAnnotationProbe
    {
        /// <summary>Nullable reference states, valued as the compiler encodes them.</summary>
        public enum Annotation
        {
            /// <summary>Declared outside a nullable context.</summary>
            Oblivious = 0,

            /// <summary>Declared non-nullable (<c>T</c>).</summary>
            NotNullable = 1,

            /// <summary>Declared nullable (<c>T?</c>).</summary>
            Nullable = 2,
        }

        private const string NullableAttributeName = "System.Runtime.CompilerServices.NullableAttribute";

        private const string NullableContextAttributeName =
            "System.Runtime.CompilerServices.NullableContextAttribute";

        /// <summary>
        /// Returns the declared annotation of <paramref name="method"/>'s return type.
        /// </summary>
        /// <param name="method">Method whose return type is read. Must not be null.</param>
        public static Annotation ReturnAnnotation(MethodInfo method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));

            // A member agreeing with the enclosing NullableContext carries no attribute of its own, so an
            // absent attribute is a reading to be resolved outward rather than a missing one.
            var declared = ReadFlag(method.ReturnParameter.GetCustomAttributesData(), NullableAttributeName);
            if (declared.HasValue) return (Annotation)declared.Value;

            var onMethod = ReadFlag(method.GetCustomAttributesData(), NullableContextAttributeName);
            if (onMethod.HasValue) return (Annotation)onMethod.Value;

            for (var type = method.DeclaringType; type != null; type = type.DeclaringType)
            {
                var onType = ReadFlag(type.GetCustomAttributesData(), NullableContextAttributeName);
                if (onType.HasValue) return (Annotation)onType.Value;
            }

            var onModule = ReadFlag(method.Module.GetCustomAttributesData(), NullableContextAttributeName);
            return onModule.HasValue ? (Annotation)onModule.Value : Annotation.Oblivious;
        }

        private static byte? ReadFlag(IList<CustomAttributeData> attributes, string attributeFullName)
        {
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeType.FullName != attributeFullName) continue;
                if (attribute.ConstructorArguments.Count != 1) continue;

                var argument = attribute.ConstructorArguments[0];
                if (argument.Value is byte flag) return flag;

                // Which element of a constructed return type's flag array belongs to the returned reference
                // itself is pinned by the Match return-annotation case in RouteTreeTests.
                if (argument.Value is ReadOnlyCollection<CustomAttributeTypedArgument> flags
                    && flags.Count > 0
                    && flags[0].Value is byte outermost)
                {
                    return outermost;
                }
            }

            return null;
        }
    }
}
