using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Velvet.SourceGenerators.Shared;

namespace Velvet.SourceGenerators
{
    /// <summary>
    /// Decides whether a props type gets a comparer and, if it does, writes one equality per member.
    /// </summary>
    internal static class PropsComparerBody
    {
        public static PropsComparerModel? TryBuild(Compilation compilation, INamedTypeSymbol propsType)
        {
            if (!IsComparablePropsBag(compilation, propsType))
            {
                return null;
            }

            if (!PropsMembers.TryRead(propsType, out var members))
            {
                return null;
            }

            var checks = ImmutableArray.CreateBuilder<MemberCheck>(members.Count);
            for (var i = 0; i < members.Count; i++)
            {
                if (!PropsEquality.TryBuild(compilation, members[i].Type, $"a{i}", $"b{i}", out var equality))
                {
                    return null;
                }

                checks.Add(new MemberCheck(PropsEquality.Identifier(members[i].Name), equality));
            }

            return new PropsComparerModel(
                propsType.ToDisplayString(SymbolDisplayFormats.AccessPath), checks.ToImmutable());
        }

        // The runtime reaches a props type through its exact runtime type, and walks only a reference type
        // that is not a collection: any other props value is decided as a value.
        private static bool IsComparablePropsBag(Compilation compilation, INamedTypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic)
            {
                return false;
            }

            if (PropsEquality.ContainsTypeParameter(type) || !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
            {
                return false;
            }

            return !type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable);
        }
    }

    internal readonly struct PropsMember
    {
        public PropsMember(string name, ITypeSymbol type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }
        public ITypeSymbol Type { get; }
    }

    /// <summary>
    /// The public instance properties and fields of a props type, as the runtime's reflected member set
    /// reads them: inherited ones included, an indexer, a write-only property and the record's
    /// <c>EqualityContract</c> not.
    /// </summary>
    internal static class PropsMembers
    {
        public static bool TryRead(INamedTypeSymbol type, out List<PropsMember> members)
        {
            members = new List<PropsMember>();
            var latest = new Dictionary<string, ISymbol>();
            for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                foreach (var symbol in current.GetMembers())
                {
                    if (!TryTake(symbol, latest, members))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // False for a member the generator cannot read as the runtime does, which makes the whole type
        // decline rather than compare fewer members.
        private static bool TryTake(ISymbol symbol, Dictionary<string, ISymbol> latest, List<PropsMember> members)
        {
            switch (symbol)
            {
                case IPropertySymbol property when IsReflectedProperty(property):
                    return property.ReturnsByRef == false
                        && property.GetMethod!.DeclaredAccessibility == Accessibility.Public
                        && Record(property, property.Type, latest, members);
                case IFieldSymbol field when IsReflectedField(field):
                    return !field.IsFixedSizeBuffer && Record(field, field.Type, latest, members);
                default:
                    return true;
            }
        }

        private static bool IsReflectedProperty(IPropertySymbol property) =>
            property.DeclaredAccessibility == Accessibility.Public
            && !property.IsStatic
            && !property.IsIndexer
            && property.GetMethod is not null
            && property.Name != "EqualityContract";

        private static bool IsReflectedField(IFieldSymbol field) =>
            field.DeclaredAccessibility == Accessibility.Public && !field.IsStatic && !field.IsConst;

        // Walked from the most derived type up, so a name met twice is either an override the derived
        // declaration already stands for or a member a derived one hides, which reflection lists twice.
        private static bool Record(ISymbol symbol, ITypeSymbol type, Dictionary<string, ISymbol> latest, List<PropsMember> members)
        {
            if (latest.TryGetValue(symbol.Name, out var earlier))
            {
                latest[symbol.Name] = symbol;
                return IsOverrideOf(earlier, symbol);
            }

            latest[symbol.Name] = symbol;
            members.Add(new PropsMember(symbol.Name, type));
            return true;
        }

        private static bool IsOverrideOf(ISymbol derived, ISymbol baseSymbol) =>
            derived is IPropertySymbol { IsOverride: true } property
            && baseSymbol is IPropertySymbol
            && SymbolEqualityComparer.Default.Equals(
                property.OverriddenProperty?.OriginalDefinition, baseSymbol.OriginalDefinition);
    }
}
