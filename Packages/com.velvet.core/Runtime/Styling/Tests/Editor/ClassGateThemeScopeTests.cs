using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a class gate answers in the theme scope it is asked in. A breakpoint only a sheet names
    /// (<c>3xltest:</c>) is a variant inside a mount whose sheets declare it and is no variant outside, so a gate that
    /// peels variants reads one array differently in the two, and no reading taken in one may stand in the other.
    /// </summary>
    internal sealed class ClassGateThemeScopeTests : PanelTestBase
    {
        private const string TokenSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ThemeTokenSheet.uss";

        private static readonly string[] s_scopedPrefixes = { "3xltest:", "max-3xltest:", "@3xltest:", "md:", "sm:" };

        private static readonly string[] s_utilities =
        {
            "ease-in", "ease-out", "ease-in-out", "transition", "transition-colors", "transition-filter",
            "border", "border-2", "border-[3px]", "border-dashed", "skew-x-6", "shadow-lg", "inset-shadow-sm",
            "ring-2", "outline-2", "backdrop-blur-sm", "bg-linear-to-r", "animate-spin", "[&>*]:mt-2", "gap-4",
            "space-x-2", "grid", "grid-cols-3", "divide-y", "pointer-events-none", "absolute", "relative", "z-10",
            "order-2",
        };

        private StyleThemeTokens.Restore OpenSheetScope()
        {
            _window.rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(TokenSheet));
            _mounted = V.Mount(_window.rootVisualElement, V.Div());
            return StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context);
        }

        private static bool NamesFilterTransition(string[] classNames)
            => (bool)typeof(FiberFilterTransitionApplier)
                .GetMethod("HasFilterTransitionClass", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { classNames });

        // Whether noting classNames on a fresh element leaves it carrying a classed binding, which is what makes a
        // running transition take Tailwind's curve.
        private static bool NotesTimingKeyword(string[] classNames)
        {
            var element = new VisualElement();
            StyleEaseCurve.NoteClasses(element, classNames);
            var bindings = typeof(StyleEaseCurve).GetField("s_bindings", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null);
            var arguments = new object?[] { element, null };
            var found = (bool)bindings!.GetType().GetMethod("TryGetValue")!.Invoke(bindings, arguments);
            return found && (bool)arguments[1]!.GetType().GetField("Classed")!.GetValue(arguments[1]);
        }

        // The answer for an array asked first outside every scope and then inside the sheet's, or null where a fresh
        // array asked inside the sheet's scope does not answer true, which no reading equals.
        private bool? AskedAgainInTheSheetsScope(Func<string[], bool> gate, string[] classNames)
        {
            gate(classNames);
            using (OpenSheetScope())
            {
                var fresh = gate((string[])classNames.Clone());
                return fresh ? gate(classNames) : null;
            }
        }

        [Test]
        public void Given_AnArrayReadOutsideEveryScope_When_ItsTransitionFilterGateIsAskedInAScopeNamingItsBreakpoint_Then_ItAnswersAsAFreshArrayDoes()
        {
            // Act
            var answer = AskedAgainInTheSheetsScope(NamesFilterTransition, new[] { "3xltest:transition-filter" });

            // Assert
            Assert.That(answer, Is.EqualTo(true));
        }

        [Test]
        public void Given_AnArrayReadOutsideEveryScope_When_ItsTimingKeywordIsNotedInAScopeNamingItsBreakpoint_Then_ItAnswersAsAFreshArrayDoes()
        {
            // Act
            var answer = AskedAgainInTheSheetsScope(NotesTimingKeyword, new[] { "3xltest:ease-in" });

            // Assert
            Assert.That(answer, Is.EqualTo(true));
        }

        // A family the table keeps by array identity has to read a token the same in every scope, or the reading taken
        // first stands for every mount after it.
        [Test]
        public void Given_TokensBehindVariantsASheetNames_When_EveryTableFamilyReadsThemInAndOutOfTheSheetsScope_Then_NoReadingMoves()
        {
            // Arrange
            var tokens = s_scopedPrefixes.SelectMany(prefix => s_utilities.Select(utility => prefix + utility))
                .Concat(s_utilities).ToArray();
            var families = ((StyleClassFamily[])Enum.GetValues(typeof(StyleClassFamily)))
                .Where(family => family != StyleClassFamily.None).ToArray();
            string Reading(string token)
                => string.Concat(families.Select(family => StyleClassFamilies.Has(new[] { token }, family) ? '1' : '0'));
            var outside = tokens.Select(Reading).ToArray();

            // Act
            string[] inside;
            bool sheetNamesTheBreakpoint;
            using (OpenSheetScope())
            {
                inside = tokens.Select(Reading).ToArray();
                sheetNamesTheBreakpoint = StyleVariantClass.TryParse("3xltest:p-2", out _, out _, out _);
            }
            var moved = sheetNamesTheBreakpoint
                ? tokens.Where((token, i) => outside[i] != inside[i]).ToArray()
                : new[] { "the sheet's scope does not read 3xltest: as a variant" };

            // Assert
            Assert.That(moved, Is.Empty);
        }
    }
}
