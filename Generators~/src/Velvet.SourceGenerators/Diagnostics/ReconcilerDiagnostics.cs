using Microsoft.CodeAnalysis;

namespace Velvet.SourceGenerators.Diagnostics
{
    /// <summary>
    /// Diagnostic descriptors for how the reconciler matches the children a call site builds.
    /// </summary>
    internal static class ReconcilerDiagnostics
    {
        public static readonly DiagnosticDescriptor Vel600ListChildWithoutKey = new(
            "VEL600",
            "Each child in a list should have a unique key",
            "'V.{0}' builds a child of a list without a key; each child in a list should have a unique key",
            DiagnosticCategories.Reconciler,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            "An unkeyed child is matched by its index, so an item inserted, removed or reordered ahead of it hands its state and elements to whichever item then holds that index. React warns about a keyless element in an array at run time; a built list and children written out at the call site both reach Velvet's runtime as a VNode?[], so the warning is read off the source instead. Reported: a call of a Velvet.V factory building any node but a TextNode, without a key or with a null one, whose node reaches the selector's return of System.Linq.Enumerable.Select, a yield return, an element of an array (a local or a field) handed to a V factory or returned, or an item an explicit List<T>.Add, AddRange, Insert or InsertRange puts into a list made an array (a local, a field, or a parameter taken from a caller holding one). The node is followed through a cast, either branch of a conditional expression, the factory of a V.When, a local's assignments, the return of a method or local function this compilation declares or of a delegate's target, and from a parameter to the argument a call passes it; a trace stops 16 methods, lambdas and locals deep. Not reported: V.Text, whose node is text, as a string child is in React, and a collection initializer, whose children are written out as an array initializer's are.",
            helpLinkUri: null,
            WellKnownDiagnosticTags.CompilationEnd);
    }
}
