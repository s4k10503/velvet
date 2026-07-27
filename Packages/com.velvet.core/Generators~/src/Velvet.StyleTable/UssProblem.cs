using System.Globalization;

namespace Velvet.StyleTable
{
    /// <summary>
    /// Stable identifiers for the ways deriving the table can fail.
    /// </summary>
    /// <remarks>
    /// Deliberately not in the <c>VEL###</c> space the Roslyn analyzers use. These are build-script failures,
    /// not compiler diagnostics: nothing can suppress one through an <c>.editorconfig</c> severity, and
    /// pretending otherwise by sharing the namespace would invite someone to try.
    /// </remarks>
    internal static class UssProblemCode
    {
        /// <summary>The stylesheet's rule and declaration structure could not be read.</summary>
        public const string MalformedUss = "USS001";

        /// <summary>A selector shape or at-rule the table cannot model.</summary>
        public const string UnsupportedConstruct = "USS002";

        /// <summary>A property name that is neither a UI Toolkit longhand nor a shorthand.</summary>
        public const string UnknownProperty = "USS003";

        /// <summary>A <c>:root</c> block declared something other than a custom property.</summary>
        public const string RootDeclaresNonCustomProperty = "USS004";

        /// <summary>A utility class declared a custom property.</summary>
        public const string UtilityDeclaresCustomProperty = "USS005";

        /// <summary>One utility class was defined under two different gates.</summary>
        public const string ClassSpansMultipleGates = "USS006";

        /// <summary>The longhand vocabulary outgrew the generated property set.</summary>
        public const string VocabularyExceedsCapacity = "USS007";

        /// <summary>The derivation was handed no stylesheet to read.</summary>
        public const string NoStyleSheets = "USS008";
    }

    /// <summary>One reason the table could not be derived, located in the stylesheet that caused it.</summary>
    internal readonly struct UssProblem
    {
        public UssProblem(string code, string message, string path = "", int line = 0, int column = 0)
        {
            Code = code;
            Message = message;
            Path = path;
            Line = line;
            Column = column;
        }

        public string Code { get; }

        public string Message { get; }

        public string Path { get; }

        /// <summary>One-based, so the text matches what an editor shows.</summary>
        public int Line { get; }

        /// <summary>One-based, so the text matches what an editor shows.</summary>
        public int Column { get; }

        /// <summary>Formatted the way compilers report, so an editor or CI log can jump to the rule.</summary>
        public override string ToString() =>
            Path.Length == 0
                ? Code + ": " + Message
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}({1},{2}): {3}: {4}",
                    Path,
                    Line,
                    Column,
                    Code,
                    Message);
    }
}
