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
            "An unkeyed child is matched by its index, so an item inserted, removed or reordered ahead of it hands its state and elements to whichever item then holds that index. React warns about a keyless element in an array at run time; a list a Select built and children written out at the call site both reach Velvet's runtime as a VNode?[], so the warning is read off the source instead. Reported: a call of a Velvet.V factory building any node but a TextNode, without a key or with a null one, that the selector of System.Linq.Enumerable.Select returns directly, through a cast, from either branch of a conditional expression, or as the node a V.When's factory builds. Not reported: V.Text, whose node is text, as a string child is in React; a node a method of your own builds; a node handed back through a call; and a list built other than by Select.");
    }
}
