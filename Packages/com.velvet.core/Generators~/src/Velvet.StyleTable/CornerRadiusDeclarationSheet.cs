using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Velvet.StyleTable
{
    /// <summary>
    /// Derives the stylesheet that restates, as one custom property per corner, the radius each bundled rule
    /// declares.
    /// </summary>
    /// <remarks>
    /// <c>CornerRadiusFit</c> holds a fitted radius inline, and an inline value hides the declared one from every
    /// public reading, so the declared radius has to reach it through a channel inline style does not cover. A
    /// custom property is that channel. Each restating rule keeps its source rule's selector and the sheets'
    /// cascade order, so among themselves they are ranked as the rules they restate are.
    ///
    /// The restating rules declare custom properties and nothing else, so <see cref="StyleUtilityTableBuilder"/>
    /// reads them as token blocks and they add nothing to the property table — which is also why deriving this
    /// sheet from a directory that already holds it reproduces it unchanged.
    /// </remarks>
    internal static class CornerRadiusDeclarationSheet
    {
        public const string FileName = "_radius_declared.uss";

        private const string Shorthand = "border-radius";

        private const string Header = @"/*
 * Derived from the border-radius declarations in Runtime/Styles/*.uss by Generators~/src/Velvet.StyleTable.
 * Regenerate with Generators~/build.py after editing a stylesheet; edits made here are overwritten, and
 * BundledStyleSheetCensusTests fails if this file no longer matches the stylesheets.
 *
 * CornerRadiusFit reads these to learn the radius a rule declares while it holds a fitted one inline.
 */
";

        /// <summary>The corner longhands in the order a four-value shorthand lists them, with each one's restatement.</summary>
        private static readonly ImmutableArray<(string Longhand, string CustomProperty)> Corners = ImmutableArray.Create(
            ("border-top-left-radius", "--velvet-radius-top-left"),
            ("border-top-right-radius", "--velvet-radius-top-right"),
            ("border-bottom-right-radius", "--velvet-radius-bottom-right"),
            ("border-bottom-left-radius", "--velvet-radius-bottom-left"));

        public static CornerRadiusDeclarationResult Build(IReadOnlyList<UssSourceText> sheets)
        {
            var problems = ImmutableArray.CreateBuilder<UssProblem>();
            var text = new StringBuilder(Header);
            foreach (var source in sheets)
            {
                var sheet = UssStyleSheetParser.Parse(source.Path, source.Text);
                foreach (var rule in sheet.Rules)
                {
                    var corners = DeclaredCorners(sheet, rule, problems);
                    if (corners != null)
                    {
                        AppendRule(text, rule.Selector, corners);
                    }
                }
            }
            text.Append('\n');
            return new CornerRadiusDeclarationResult(text.ToString(), problems.ToImmutable());
        }

        /// <summary>
        /// The value each corner ends up declared with in <paramref name="rule"/>, a later declaration replacing
        /// an earlier one as it does in the cascade; null when the rule declares no radius.
        /// </summary>
        private static string?[]? DeclaredCorners(
            UssSheet sheet, UssRule rule, ImmutableArray<UssProblem>.Builder problems)
        {
            string?[]? corners = null;
            foreach (var declaration in rule.Declarations)
            {
                if (string.Equals(declaration.Property, Shorthand, StringComparison.Ordinal))
                {
                    corners ??= new string?[Corners.Length];
                    ExpandShorthand(sheet, declaration, corners, problems);
                    continue;
                }
                var corner = IndexOfLonghand(declaration.Property);
                if (corner >= 0)
                {
                    corners ??= new string?[Corners.Length];
                    corners[corner] = declaration.Value.Trim();
                }
            }
            return corners;
        }

        /// <summary>
        /// Only the one-value form is expanded. A multi-value or elliptical shorthand is reported instead, because
        /// the bundled sheets hold none, and an expansion nothing exercises is one nothing would notice breaking.
        /// </summary>
        private static void ExpandShorthand(
            UssSheet sheet, UssDeclaration declaration, string?[] corners, ImmutableArray<UssProblem>.Builder problems)
        {
            var value = declaration.Value.Trim();
            if (value.IndexOf(' ') >= 0 || value.IndexOf('/') >= 0)
            {
                problems.Add(sheet.ProblemAt(
                    UssProblemCode.UnsupportedRadiusShorthand,
                    $"'{Shorthand}: {value}' has more than one value. Only the one-value form is restated for " +
                    "CornerRadiusFit; write the corners as longhands.",
                    declaration.Offset));
                return;
            }
            for (var i = 0; i < corners.Length; i++)
            {
                corners[i] = value;
            }
        }

        private static int IndexOfLonghand(string property)
        {
            for (var i = 0; i < Corners.Length; i++)
            {
                if (string.Equals(Corners[i].Longhand, property, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        private static void AppendRule(StringBuilder text, string selector, string?[] corners)
        {
            text.Append('\n').Append(selector).Append(" {");
            for (var i = 0; i < corners.Length; i++)
            {
                if (corners[i] != null)
                {
                    text.Append(' ').Append(Corners[i].CustomProperty).Append(": ").Append(corners[i]).Append(';');
                }
            }
            text.Append(" }");
        }
    }

    internal readonly struct CornerRadiusDeclarationResult
    {
        public CornerRadiusDeclarationResult(string emittedSheet, ImmutableArray<UssProblem> problems)
        {
            EmittedSheet = emittedSheet;
            Problems = problems;
        }

        public string EmittedSheet { get; }

        public ImmutableArray<UssProblem> Problems { get; }
    }
}
