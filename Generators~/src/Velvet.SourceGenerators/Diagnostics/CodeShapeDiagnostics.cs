using Microsoft.CodeAnalysis;

namespace Velvet.SourceGenerators.Diagnostics
{
    /// <summary>
    /// Diagnostic descriptors for the mechanical code-shape rules.
    /// </summary>
    internal static class CodeShapeDiagnostics
    {
        // Its own category so an assembly that has opted in can dial the shape rules down as a family
        // without touching the correctness rules. AnalyzerReleases.Unshipped.md records the two spellings
        // that make a category severity entry take effect.
        private const string Category = DiagnosticCategories.Shape;

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
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            "The body makes more branching decisions than the limit. Split it along the axis the branches already divide it by — one member per case group, or a table lookup in place of a chain of comparisons; flattening nesting into width does not help, since width is what this counts.");

        public static readonly DiagnosticDescriptor Vel502ParameterCountExceeded = new(
            "VEL502",
            "Member parameter count exceeds the limit",
            "Member '{0}' demands {1} arguments from every caller; the limit is {2}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            "The declaration demands more arguments than the limit from every call site. Group the ones that travel together into a type the caller builds once, or split the member along the axis its parameters already divide it by; a parameter carrying a default value is not counted, since a call can leave it out.");

        // A warning rather than an error, unlike its siblings, because the package's own test assemblies still
        // carry sites it reports and they opt into this category; an error would stop them compiling, and a
        // test assembly that does not compile runs no tests.
        public static readonly DiagnosticDescriptor Vel503ToleranceNeverApplied = new(
            "VEL503",
            "Tolerance on a tuple comparison is never applied",
            "The tolerance on this comparison of '{0}' is never applied; its members are compared bit-exactly",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            "NUnit has no comparer for ValueTuple, so the pair falls through to the expected value's own IEquatable<T>, which is never handed the tolerance. The assertion is bit-exact equality wearing a tolerance suffix, and its failure message prints the tolerance it did not use, so nothing at run time tells the two apart. Round each member before comparing, or compare formatted strings. Three shapes this does not report. A tuple inside an expected collection: the tolerance descends into the collection and then dies at the element, so the assertion traps identically while the expected type itself is an array rather than a tuple. A constraint built in one statement and given its tolerance in another, since only a single chained expression is followed. And a tolerance dropped by another expected type reaching the same IEquatable<T> fall-through.");
    }
}
