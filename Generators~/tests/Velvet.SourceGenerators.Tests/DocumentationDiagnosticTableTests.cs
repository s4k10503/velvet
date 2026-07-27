using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Doc-drift guard for the VEL### diagnostic IDs: memoization.md's table and Generators~/README.md's
    /// summary line must not talk about an ID the generator/analyzer assembly does not actually define —
    /// the class of bug that once left the docs describing a contiguous "VEL001-011" range while the real
    /// IDs are the non-contiguous VEL001-009 / VEL100-101.
    /// </summary>
    public sealed class DocumentationDiagnosticTableTests
    {
        private static readonly Regex DiagnosticIdPattern = new(@"VEL\d{3}", RegexOptions.Compiled);

        // "Velvet.Memoize" is the [MemoizeMethod]-attribute diagnostic category (VEL001-009). memoization.md's
        // table intentionally documents only this category and points elsewhere (AnalyzerReleases.Unshipped.md)
        // for the separate "Velvet.Hooks" rules-of-hooks diagnostics (VEL100/101), so the table's exact-match
        // comparison set is scoped to this category rather than to every diagnostic the assembly defines.
        private const string MemoizeCategory = "Velvet.Memoize";

        [Fact]
        public void MemoizationDocTable_ComparedAgainstMemoizeCategoryDescriptors_MatchesExactly()
        {
            var definedIds = AllDiagnosticDescriptors()
                .Where(d => d.Category == MemoizeCategory)
                .Select(d => d.Id)
                .ToHashSet();
            var documentedIds = ExtractIds(File.ReadAllText(MemoizationDocPath()));

            var missingFromDoc = definedIds.Except(documentedIds).OrderBy(x => x).ToList();
            var undefinedInDoc = documentedIds.Except(definedIds).OrderBy(x => x).ToList();

            Assert.True(missingFromDoc.Count == 0 && undefinedInDoc.Count == 0,
                $"Documentation~/memoization.md's diagnostic table is out of sync with the {MemoizeCategory} " +
                $"category descriptors.\nDefined but missing from the doc table: [{string.Join(", ", missingFromDoc)}]\n" +
                $"In the doc table but not a defined {MemoizeCategory} descriptor: [{string.Join(", ", undefinedInDoc)}]");
        }

        [Fact]
        public void GeneratorsReadme_ComparedAgainstAllDescriptors_MentionsOnlyRealIds()
        {
            var allDefinedIds = AllDiagnosticDescriptors().Select(d => d.Id).ToHashSet();
            var mentionedIds = ExtractIds(File.ReadAllText(GeneratorsReadmePath()));

            var undefinedInReadme = mentionedIds.Except(allDefinedIds).OrderBy(x => x).ToList();

            Assert.True(undefinedInReadme.Count == 0,
                "Generators~/README.md mentions VEL IDs with no matching DiagnosticDescriptor: " +
                $"[{string.Join(", ", undefinedInReadme)}]");
        }

        private static HashSet<string> ExtractIds(string text) =>
            DiagnosticIdPattern.Matches(text).Select(m => m.Value).ToHashSet();

        // Reflects over every type in the generator/analyzer assembly — not just MemoizeDiagnostics — so a
        // future diagnostic-definition class is picked up automatically instead of needing this test updated.
        private static List<DiagnosticDescriptor> AllDiagnosticDescriptors()
        {
            var assembly = typeof(MemoizeMethodGenerator).Assembly;
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            var descriptors = new List<DiagnosticDescriptor>();
            foreach (var type in types)
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (field.FieldType == typeof(DiagnosticDescriptor) &&
                        field.GetValue(null) is DiagnosticDescriptor descriptor)
                    {
                        descriptors.Add(descriptor);
                    }
                }
            }
            return descriptors;
        }

        private static string MemoizationDocPath() =>
            Path.Combine(SolutionPaths.GeneratorsRoot(), "..", "Documentation~", "memoization.md");

        private static string GeneratorsReadmePath() =>
            Path.Combine(SolutionPaths.GeneratorsRoot(), "README.md");
    }
}
