using Microsoft.CodeAnalysis;

namespace Velvet.SourceGenerators.Diagnostics
{
    /// <summary>
    /// Diagnostic descriptors for the identity a list's elements are reconciled by.
    /// </summary>
    internal static class KeyDiagnostics
    {
        public static readonly DiagnosticDescriptor Vel600MissingListKey = new(
            "VEL600",
            "Element of a mapped list built without a key",
            "'{0}' builds an element of a list mapped by Select without a key; pass key: so the element keeps its identity when the list changes",
            DiagnosticCategories.Keys,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            "React warns that each child in a list should have a unique key. A Select selector whose element reaches a V.* factory's children through .ToArray() is that list here; an unkeyed element is matched by its position, so an item inserted or removed ahead of it hands its element, and the state inside it, to the item that moves into its slot.");
    }
}
