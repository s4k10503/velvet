using Microsoft.CodeAnalysis;

namespace Velvet.SourceGenerators.Shared
{
    internal static class SymbolDisplayFormats
    {
        /// <summary>
        /// Format that includes the global:: prefix and fully-qualified type names so it can be embedded directly into generated code.
        /// </summary>
        public static readonly SymbolDisplayFormat FullyQualified = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                                   SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        /// <summary>
        /// Type display intended for embedding into expressions as an AccessPath. Excludes nullable annotations because they collide with ValueTuple type names.
        /// </summary>
        public static readonly SymbolDisplayFormat AccessPath = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
    }
}
