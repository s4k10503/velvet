using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Fails when a mutant <c>scripts/test_quality/mutation_check.py</c> generates takes a variable
    /// designation away from a line while the code around it still reads the name.
    /// </summary>
    /// <remarks>
    /// Its sibling <see cref="MutantParseabilityTests"/> cannot see this one: an empty statement parses,
    /// so what the generator produced is well-formed C# that reads a name nothing declares any more.
    /// The campaign scores such a mutant unmeasured and fails the run, and the pull-request hook then
    /// refuses a branch that has no receipt to show.
    /// <para/>
    /// Designations rather than declarators: <c>foreach (var f in drop) Remove(f);</c> and
    /// <c>using (var x = Open()) Read(x);</c> take their variable away with the line that declared it,
    /// so refusing those would cost mutants that strand nothing.
    /// <para/>
    /// The generator approximates this with a pattern over the removed text, which is a spelling of C#
    /// rather than a reading of it. This is the reading, and what it costs the generator to miss a
    /// spelling is one red line here.
    /// <para/>
    /// The name and the block's line span, not the symbol: binding it would want the whole compilation
    /// with its references, which is the Unity build rather than this. So a second declaration of the
    /// same name inside the block reads as a surviving use of the first, and a widening that would let
    /// such a line through has to answer this before it can be measured.
    /// </remarks>
    public sealed class MutantDeclarationRemovalTests
    {
        [Fact]
        public void Given_EveryMutantThisPackageGenerates_When_ItTakesADesignationAway_Then_NothingStillReadsTheName()
        {
            // Arrange
            var repository = GeneratedMutants.RepositoryRoot();
            var mutants = GeneratedMutants.Generate(repository);
            Assume.NotEmpty(mutants, "the generator emitted mutants to read");

            // Act
            var stranded = new List<string>();
            var examined = 0;
            foreach (var byFile in mutants.GroupBy(mutant => mutant.Path, StringComparer.Ordinal))
            {
                var lines = File.ReadAllLines(Path.Combine(repository, byFile.Key));
                var original = CSharpSyntaxTree.ParseText(string.Join("\n", lines));
                var declared = DesignationsByLine(original);
                foreach (var mutant in byFile)
                {
                    if (mutant.Line < 1 || mutant.Line > lines.Length
                        || !declared.TryGetValue(mutant.Line, out var onThisLine))
                    {
                        continue;
                    }
                    examined++;
                    var swapped = (string[])lines.Clone();
                    swapped[mutant.Line - 1] = mutant.Text;
                    var mutated = CSharpSyntaxTree.ParseText(string.Join("\n", swapped));
                    var survivors = DesignationsByLine(mutated).TryGetValue(mutant.Line, out var after)
                        ? after.Select(designation => designation.Identifier.ValueText).ToHashSet(StringComparer.Ordinal)
                        : new HashSet<string>(StringComparer.Ordinal);
                    foreach (var lost in onThisLine.Where(d => !survivors.Contains(d.Identifier.ValueText)))
                    {
                        var name = lost.Identifier.ValueText;
                        if (ReadsWithin(mutated, Scope(lost), name))
                        {
                            stranded.Add($"{byFile.Key}:{mutant.Line} ({mutant.Operator}) strands `{name}`"
                                         + $"\n    {lines[mutant.Line - 1].Trim()}");
                        }
                    }
                }
            }

            // The whole list to a file: the assertion carries the count alone, and every entry is a
            // line somebody has to look at.
            if (stranded.Count > 0)
            {
                var listing = Path.Combine(Path.GetTempPath(), "velvet-stranded-mutants.txt");
                File.WriteAllLines(listing, stranded);
                Console.WriteLine($"{stranded.Count} mutant(s) stranding a name; full list at {listing}");
                foreach (var entry in stranded)
                {
                    Console.WriteLine(entry);
                }
            }

            // Assert — the examined count rides along, because a run that reached no designation at all
            // strands nothing by arithmetic. Measured at 309 over this package.
            Assert.Equal(
                (true, 0),
                (examined > 50, stranded.Count));
        }

        private static Dictionary<int, List<SingleVariableDesignationSyntax>> DesignationsByLine(SyntaxTree tree)
        {
            var found = new Dictionary<int, List<SingleVariableDesignationSyntax>>();
            foreach (var designation in tree.GetRoot().DescendantNodes().OfType<SingleVariableDesignationSyntax>())
            {
                var line = tree.GetLineSpan(designation.Span).StartLinePosition.Line + 1;
                if (!found.TryGetValue(line, out var onThisLine))
                {
                    found[line] = onThisLine = new List<SingleVariableDesignationSyntax>();
                }
                onThisLine.Add(designation);
            }
            return found;
        }

        /// <summary>The lines a designation's name is resolvable on, as line numbers in the unmutated file.</summary>
        private static (int First, int Last) Scope(SingleVariableDesignationSyntax designation)
        {
            SyntaxNode holder = designation.Ancestors().FirstOrDefault(node => node is BlockSyntax)
                                ?? designation.Ancestors().FirstOrDefault(node => node is MemberDeclarationSyntax)
                                ?? designation.SyntaxTree.GetRoot();
            var span = designation.SyntaxTree.GetLineSpan(holder.Span);
            return (span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1);
        }

        private static bool ReadsWithin(SyntaxTree tree, (int First, int Last) scope, string name) =>
            tree.GetRoot().DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(identifier => identifier.Identifier.ValueText == name)
                .Select(identifier => tree.GetLineSpan(identifier.Span).StartLinePosition.Line + 1)
                .Any(line => line >= scope.First && line <= scope.Last);
    }
}
