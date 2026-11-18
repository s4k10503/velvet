using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Velvet.SourceGenerators
{
    /// <summary>
    /// The bit-pattern comparison of the <c>float</c> and <c>double</c> fields a value type carries, which the
    /// runtime adds to that type's own equality. A struct declared in another assembly is not read, so a
    /// props type holding one declines.
    /// </summary>
    internal static class PropsFloatLeaves
    {
        // leaves is null where the struct carries no floating point.
        public static bool TryBuild(Compilation compilation, INamedTypeSymbol type, string a, string b, out string? leaves)
        {
            leaves = null;
            if (type.TypeKind == TypeKind.Enum || IsLeafFree(type))
            {
                return true;
            }

            if (!IsDeclaredHere(compilation, type))
            {
                return false;
            }

            var terms = new List<string>();
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
            {
                if (!TryAddFieldTerm(compilation, field, a, b, terms))
                {
                    return false;
                }
            }

            leaves = terms.Count == 0 ? null : string.Join(" && ", terms);
            return true;
        }

        private static bool IsLeafFree(ITypeSymbol type) =>
            type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char
                or SpecialType.System_SByte or SpecialType.System_Byte
                or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_IntPtr or SpecialType.System_UIntPtr
                or SpecialType.System_Decimal or SpecialType.System_DateTime;

        private static bool IsDeclaredHere(Compilation compilation, INamedTypeSymbol type) =>
            type.IsDefinition
            && type.DeclaringSyntaxReferences.Length != 0
            && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly);

        private static bool TryAddFieldTerm(
            Compilation compilation, IFieldSymbol field, string a, string b, List<string> terms)
        {
            var isNullable = field.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            var read = isNullable ? ((INamedTypeSymbol)field.Type).TypeArguments[0] : field.Type;
            if (!read.IsValueType)
            {
                return true;
            }

            var access = AccessName(compilation, field);
            var fieldA = access is null ? "_" : $"{a}.{access}";
            var fieldB = access is null ? "_" : $"{b}.{access}";
            var valueA = isNullable ? $"{fieldA}.Value" : fieldA;
            var valueB = isNullable ? $"{fieldB}.Value" : fieldB;
            if (!TryReadTerm((INamedTypeSymbol)read, compilation, valueA, valueB, out var term))
            {
                return false;
            }

            if (term is null)
            {
                return true;
            }

            if (access is null)
            {
                return false;
            }

            terms.Add(isNullable ? $"(!({fieldA}.HasValue && {fieldB}.HasValue) || {term})" : term);
            return true;
        }

        private static bool TryReadTerm(
            INamedTypeSymbol read, Compilation compilation, string a, string b, out string? term)
        {
            term = null;
            switch (read.SpecialType)
            {
                case SpecialType.System_Single:
                    term = $"(global::System.BitConverter.SingleToInt32Bits({a}) == global::System.BitConverter.SingleToInt32Bits({b}))";
                    return true;
                case SpecialType.System_Double:
                    term = $"(global::System.BitConverter.DoubleToInt64Bits({a}) == global::System.BitConverter.DoubleToInt64Bits({b}))";
                    return true;
                default:
                    return TryBuild(compilation, read, a, b, out term);
            }
        }

        // The name a field is read by from the generated class: its own where it can be written and reached,
        // and otherwise the auto-property whose backing field it is.
        private static string? AccessName(Compilation compilation, IFieldSymbol field)
        {
            if (field.CanBeReferencedByName && compilation.IsSymbolAccessibleWithin(field, compilation.Assembly))
            {
                return PropsEquality.Identifier(field.Name);
            }

            if (field.AssociatedSymbol is IPropertySymbol { GetMethod: { } getter } property
                && compilation.IsSymbolAccessibleWithin(property, compilation.Assembly)
                && compilation.IsSymbolAccessibleWithin(getter, compilation.Assembly)
                && IsAutoGetter(getter))
            {
                return PropsEquality.Identifier(property.Name);
            }

            return null;
        }

        private static bool IsAutoGetter(IMethodSymbol getter) =>
            getter.DeclaringSyntaxReferences.All(reference =>
                reference.GetSyntax() is not AccessorDeclarationSyntax { Body: not null } and not AccessorDeclarationSyntax { ExpressionBody: not null });
    }
}
