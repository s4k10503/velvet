using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Velvet.SourceGenerators.Shared;

namespace Velvet.SourceGenerators
{
    /// <summary>
    /// Writes the equality a props member is decided by, picked from its declared type the way the runtime's
    /// own walk picks it from its runtime type. <see cref="TryBuild"/> is false for a type it cannot write
    /// that equality for, which makes the props type decline.
    /// </summary>
    internal static class PropsEquality
    {
        private const string Registry = "global::" + VelvetWellKnownNames.PropsComparerRegistryFullName;

        public static string Identifier(string name) =>
            SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
                ? "@" + name
                : name;

        public static bool ContainsTypeParameter(ITypeSymbol type)
        {
            switch (type)
            {
                case ITypeParameterSymbol:
                    return true;
                case IArrayTypeSymbol array:
                    return ContainsTypeParameter(array.ElementType);
                case INamedTypeSymbol named:
                    return (named.ContainingType is not null && ContainsTypeParameter(named.ContainingType))
                        || named.TypeArguments.Any(ContainsTypeParameter);
                default:
                    return false;
            }
        }

        public static bool TryBuild(Compilation compilation, ITypeSymbol type, string a, string b, out string equality)
        {
            equality = string.Empty;
            if (IsUnwritable(type))
            {
                return false;
            }

            if (IsDeclaredAsBoxed(type))
            {
                equality = $"{Registry}.ValueEquals({a}, {b})";
                return true;
            }

            if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return TryBuildNullable(compilation, (INamedTypeSymbol)type, a, b, out equality);
            }

            if (TryBuildBuiltIn(type, a, b, out var builtIn))
            {
                equality = builtIn;
                return true;
            }

            if (!type.IsValueType)
            {
                equality = $"global::System.Object.ReferenceEquals({a}, {b})";
                return true;
            }

            return TryBuildStruct(compilation, type, a, b, out equality);
        }

        private static bool IsUnwritable(ITypeSymbol type) =>
            type.IsRefLikeType
            || type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.TypeParameter or TypeKind.Error
            || ContainsTypeParameter(type);

        // The declared type fixes nothing about the value, so the runtime's own comparison reads its type.
        private static bool IsDeclaredAsBoxed(ITypeSymbol type) =>
            type.TypeKind is TypeKind.Interface or TypeKind.Dynamic
            || type.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType or SpecialType.System_Enum;

        private static bool TryBuildNullable(
            Compilation compilation, INamedTypeSymbol type, string a, string b, out string equality)
        {
            equality = string.Empty;
            if (!TryBuild(compilation, type.TypeArguments[0], $"{a}.Value", $"{b}.Value", out var values))
            {
                return false;
            }

            equality = $"({a}.HasValue == {b}.HasValue && (!{a}.HasValue || {values}))";
            return true;
        }

        private static bool TryBuildBuiltIn(ITypeSymbol type, string a, string b, out string equality)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Single:
                    equality = $"(global::System.BitConverter.SingleToInt32Bits({a}) == global::System.BitConverter.SingleToInt32Bits({b}))";
                    return true;
                case SpecialType.System_Double:
                    equality = $"(global::System.BitConverter.DoubleToInt64Bits({a}) == global::System.BitConverter.DoubleToInt64Bits({b}))";
                    return true;
                case SpecialType.System_String:
                    equality = $"global::System.String.Equals({a}, {b}, global::System.StringComparison.Ordinal)";
                    return true;
                default:
                    equality = $"({a} == {b})";
                    return IsOperatorComparable(type);
            }
        }

        private static bool IsOperatorComparable(ITypeSymbol type) =>
            type.TypeKind == TypeKind.Enum
            || type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char
                or SpecialType.System_SByte or SpecialType.System_Byte
                or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

        // A value type is decided by its own equality, and then by the floating-point fields it carries.
        private static bool TryBuildStruct(Compilation compilation, ITypeSymbol type, string a, string b, out string equality)
        {
            var own = $"global::System.Collections.Generic.EqualityComparer<{type.ToDisplayString(SymbolDisplayFormats.AccessPath)}>.Default.Equals({a}, {b})";
            equality = own;
            if (type.SpecialType is SpecialType.System_Decimal or SpecialType.System_DateTime)
            {
                return true;
            }

            if (!PropsFloatLeaves.TryBuild(compilation, (INamedTypeSymbol)type, a, b, out var leaves))
            {
                return false;
            }

            if (leaves is not null)
            {
                equality = $"({own} && {leaves})";
            }

            return true;
        }
    }
}
