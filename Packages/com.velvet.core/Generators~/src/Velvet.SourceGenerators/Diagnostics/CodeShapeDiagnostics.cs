using Microsoft.CodeAnalysis;

namespace Velvet.SourceGenerators.Diagnostics
{
    /// <summary>
    /// Diagnostic descriptors for the mechanical code-shape limits.
    /// </summary>
    internal static class CodeShapeDiagnostics
    {
        // Its own category so an assembly that has opted in can dial the shape rules down as a family
        // without touching the correctness rules. AnalyzerReleases.Unshipped.md records the two spellings
        // that make a category severity entry take effect.
        private const string Category = "Velvet.Shape";

        public static readonly DiagnosticDescriptor Vel500NestingDepthExceeded = new(
            "VEL500",
            "Member nesting depth exceeds the limit",
            "Member '{0}' nests {1} levels deep; the limit is {2}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            "The body nests control flow deeper than the limit. Extract the deepest block into its own member, invert a condition into a guard clause, or replace a nested branch with an else-if chain; wrapping the block in a nested function does not help, since its body is counted at the level it appears.");

        public static readonly DiagnosticDescriptor Vel501BranchCountExceeded = new(
            "VEL501",
            "Member branch count exceeds the limit",
            "Member '{0}' makes {1} branching decisions; the limit is {2}",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            "The body makes more branching decisions than the limit. Split it along the axis the branches already divide it by — one member per case group, or a table lookup in place of a chain of comparisons; flattening nesting into width does not help, since width is what this counts.");
    }
}
