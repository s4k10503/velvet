using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Velvet.SourceGenerators.AutoDeps;
using Velvet.SourceGenerators.Shared;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins <see cref="UseEffectExhaustiveDepsAnalyzer.StableHookSlots"/> to the runtime's own account of which
    /// hook returns are reference-stable. A name missing from the analyzer makes VEL100 demand a dependency
    /// React would not, and a name that stays after the runtime stops guaranteeing stability makes VEL100 stay
    /// quiet about a dependency that now changes. Neither shows up in any other guard, because the two live on
    /// opposite sides of a compile boundary this solution cannot cross.
    /// </summary>
    /// <remarks>
    /// The runtime side is the marker sentence <c>reference-stable across renders</c> inside a hook's
    /// <c>&lt;returns&gt;</c> documentation, re-derived by parsing the runtime sources — the same syntax-only
    /// parse the hook-name guard uses.
    /// <list type="bullet">
    /// <item>A hook's own <c>&lt;returns&gt;</c> cannot say which tuple slot it means, so this fixture checks
    /// membership only. The slot numbers stay the analyzer's and are covered by its behaviour tests.</item>
    /// <item>Only the <c>&lt;returns&gt;</c> element is read. A stability claim made in a summary, a remark or
    /// a body comment is invisible here, which is why the near-miss fact exists: it turns every other mention
    /// of stability in a <c>&lt;returns&gt;</c> into a recorded decision instead of a silent absence.</item>
    /// <item>A hook name with several overloads counts as declared when any one of them carries the marker,
    /// matching the analyzer, which binds by name.</item>
    /// </list>
    /// </remarks>
    public sealed class StableHookSlotDriftTests
    {
        private const string StabilityMarker = "reference-stable across renders";
        private const string StabilityWord = "stable";

        /// <summary>
        /// Hooks whose <c>&lt;returns&gt;</c> speaks of stability yet earns no exemption, each with the reason
        /// it does not. Recorded so a hook that gains a real guarantee cannot pass for one of these.
        /// </summary>
        private static readonly Dictionary<string, string> MentionsStabilityWithoutEarningASlot = new(StringComparer.Ordinal)
        {
            ["UseCallback"] =
                "stable only while its own deps are equal, which is the caller's argument rather than a property " +
                "of the hook",
            ["UseMemo"] =
                "stable only while its own deps are equal, same as UseCallback",
            ["UseNavigate"] =
                "a UseCallback keyed on the replace argument and the enclosing Outlet depth, so a call site " +
                "varying either gets a fresh delegate",
            ["UseId"] =
                "stable by value rather than by reference, so a deps array holding it compares equal every " +
                "render and asking for it costs nothing — which is what React's own rule does with useId",
        };

        [Fact]
        public void Given_RuntimeHooksDocumentingAStableReturn_When_LookedUpInTheAnalyzer_Then_EachHasAStableSlot()
        {
            // Arrange
            var documented = HooksDeclaringAStableReturn();
            Assume.NotEmpty(documented, $"no runtime <returns> carried '{StabilityMarker}'");

            // Act
            var unlisted = documented
                .Where(hook => !UseEffectExhaustiveDepsAnalyzer.StableHookSlots.ContainsKey(hook))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(unlisted.Count == 0,
                "The runtime documents these hooks as returning a reference-stable value, but " +
                $"{nameof(UseEffectExhaustiveDepsAnalyzer.StableHookSlots)} does not list them: " +
                $"[{string.Join(", ", unlisted)}]. VEL100 therefore asks callers to put them in a dependency " +
                "array, which React would not.");
        }

        [Fact]
        public void Given_TheAnalyzerStableSlots_When_ResolvedAgainstRuntimeSource_Then_EachHookDocumentsAStableReturn()
        {
            // Arrange
            var documented = HooksDeclaringAStableReturn();
            Assume.NotEmpty(UseEffectExhaustiveDepsAnalyzer.StableHookSlots,
                $"{nameof(UseEffectExhaustiveDepsAnalyzer.StableHookSlots)} is empty");

            // Act
            var unbacked = UseEffectExhaustiveDepsAnalyzer.StableHookSlots.Keys
                .Where(hook => !documented.Contains(hook))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(unbacked.Count == 0,
                $"{nameof(UseEffectExhaustiveDepsAnalyzer.StableHookSlots)} exempts these hooks from VEL100, but " +
                $"no runtime <returns> says '{StabilityMarker}' about them: [{string.Join(", ", unbacked)}]. An " +
                "exemption the runtime no longer backs hides a dependency that really does change.");
        }

        [Fact]
        public void Given_RuntimeReturnsDocsMentioningStability_When_TheyOmitTheMarker_Then_EachIsRecorded()
        {
            // Arrange
            var mentioning = HooksWhoseReturnsMentions(StabilityWord);
            Assume.NotEmpty(mentioning, $"no runtime <returns> mentioned '{StabilityWord}' at all");

            // Act
            var unaccounted = mentioning
                .Where(hook => !HooksDeclaringAStableReturn().Contains(hook))
                .Where(hook => !MentionsStabilityWithoutEarningASlot.ContainsKey(hook))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(unaccounted.Count == 0,
                "These hooks describe their return as stable in some wording other than the marker " +
                $"'{StabilityMarker}': [{string.Join(", ", unaccounted)}]. Nothing derives an exemption from a " +
                $"paraphrase, so either use the marker or record why the hook earns none in " +
                $"{nameof(MentionsStabilityWithoutEarningASlot)}.");
        }

        [Fact]
        public void Given_TheRecordedHooks_When_ComparedAgainstRuntimeSource_Then_EachStillMentionsStability()
        {
            // Arrange
            var mentioning = HooksWhoseReturnsMentions(StabilityWord);
            Assume.NotEmpty(mentioning, $"no runtime <returns> mentioned '{StabilityWord}' at all");

            // Act
            var stale = MentionsStabilityWithoutEarningASlot
                .Where(entry => !mentioning.Contains(entry.Key))
                .Select(entry => $"{entry.Key} (recorded as: {entry.Value})")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(stale.Count == 0,
                $"{nameof(MentionsStabilityWithoutEarningASlot)} records hooks whose <returns> no longer mentions " +
                $"stability: [{string.Join("; ", stale)}]. A stale record hides the real question the next time " +
                "that wording comes back.");
        }

        private static HashSet<string> HooksDeclaringAStableReturn() => HooksWhoseReturnsMentions(StabilityMarker);

        private static HashSet<string> HooksWhoseReturnsMentions(string text) =>
            RuntimeSourceIndex.Shared
                .PublicMethodsOf(VelvetWellKnownNames.HooksTypeFullName)
                .Where(declared => ReturnsDocumentationOf(declared.Method)
                    .IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(declared => declared.Method.Identifier.ValueText)
                .ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// The text of a declaration's <c>&lt;returns&gt;</c> element with the comment markers stripped, or the
        /// empty string when it declares none. Read off the raw trivia rather than the structured documentation
        /// node so the result does not depend on the parse's <c>DocumentationMode</c>.
        /// </summary>
        private static string ReturnsDocumentationOf(MethodDeclarationSyntax method)
        {
            var trivia = Regex.Replace(method.GetLeadingTrivia().ToFullString(), @"^\s*///", string.Empty,
                RegexOptions.Multiline);
            var match = Regex.Match(trivia, "<returns>(.*?)</returns>", RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }
    }
}
