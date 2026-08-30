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
    /// Fails when a mutant <c>scripts/test_quality/mutation_check.py</c> generates carries off the text a
    /// name depends on: the designation that declares it, or the <c>out</c> argument that assigns it.
    /// </summary>
    /// <remarks>
    /// Its sibling <see cref="MutantParseabilityTests"/> cannot see either: what a removal leaves is
    /// well-formed C#, which is the fact that fixture measures, and what refuses the mutant is the
    /// compiler, over a name. The campaign scores such a mutant unmeasured and fails the run, and the
    /// pull-request hook then refuses a branch that has no receipt to show.
    /// <para/>
    /// Designations rather than declarators: <c>foreach (var f in drop) Remove(f);</c> and
    /// <c>using (var x = Open()) Read(x);</c> take their variable away with the line that declared it,
    /// so refusing those would cost mutants that strand nothing.
    /// <para/>
    /// The generator approximates both with a pattern over the removed text, which is a spelling of C#
    /// rather than a reading of it. This is the reading, and what it costs the generator to miss a
    /// spelling is one red line here.
    /// <para/>
    /// The name and the block's line span, not the symbol: binding it would want the whole compilation
    /// with its references, which is the Unity build rather than this. So a second declaration of the
    /// same name inside the block reads as a surviving use of the first, and a widening that would let
    /// such a line through has to answer this before it can be measured. The syntax-only narrowing that
    /// would isolate that case — require the mutated tree to declare the name nowhere in the scope — was
    /// rejected for the direction it trades in: a strand a later sibling block redeclares would read as
    /// fine, which buys one false positive back with a false negative in a guard whose failure is silence.
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
                var text = string.Join("\n", lines);
                // Read under the configuration that lights the most of the file. Under the default
                // one a line inside an `#if` region nothing defines is trivia: there is no
                // designation on it to lose, so a mutant there was examined by nothing.
                var reading = MutantParseReadings.Widest(text);
                var original = CSharpSyntaxTree.ParseText(text, reading);
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
                    var mutated = CSharpSyntaxTree.ParseText(string.Join("\n", swapped), reading);
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

        /// <summary>
        /// Fails when a removal carries off an <c>out</c> argument naming a variable declared elsewhere.
        /// </summary>
        /// <remarks>
        /// The name outlives the removal here, so the fact above sees nothing: what the removal takes away
        /// is the assignment rather than the declaration. Whether the reads that survive it still have a
        /// writer is definite assignment, which wants a bound compilation; this refuses the shape instead,
        /// the way the generator's own <c>out</c> arm does, so a narrowing of that arm has to move this
        /// line before it can be measured.
        /// <para/>
        /// Only an <c>out</c> argument spelled as a bare name reaches this, which is narrower than the
        /// generator's arm: its two exemptions fall outside, and so does every spelling that is not an
        /// identifier.
        /// </remarks>
        [Fact]
        public void Given_EveryMutantThisPackageGenerates_When_ItTakesAnOutArgumentAway_Then_TheArgumentWasNotAnUnqualifiedName()
        {
            // Arrange
            var repository = GeneratedMutants.RepositoryRoot();
            var mutants = GeneratedMutants.Generate(repository);
            Assume.NotEmpty(mutants, "the generator emitted mutants to read");

            // Act
            var carried = new List<string>();
            var examined = 0;
            foreach (var byFile in mutants.GroupBy(mutant => mutant.Path, StringComparer.Ordinal))
            {
                var lines = File.ReadAllLines(Path.Combine(repository, byFile.Key));
                var text = string.Join("\n", lines);
                // Same reading as the case above, for the reason stated there.
                var reading = MutantParseReadings.Widest(text);
                var original = CSharpSyntaxTree.ParseText(text, reading);
                var assigned = AssignedOutArgumentsByLine(original);
                foreach (var mutant in byFile)
                {
                    if (mutant.Line < 1 || mutant.Line > lines.Length
                        || !assigned.TryGetValue(mutant.Line, out var onThisLine))
                    {
                        continue;
                    }
                    examined++;
                    var swapped = (string[])lines.Clone();
                    swapped[mutant.Line - 1] = mutant.Text;
                    var mutated = CSharpSyntaxTree.ParseText(string.Join("\n", swapped), reading);
                    var survivors = AssignedOutArgumentsByLine(mutated).TryGetValue(mutant.Line, out var after)
                        ? new List<string>(after)
                        : new List<string>();
                    // Removed one at a time rather than compared as sets, so a line naming the same
                    // variable in two `out` positions still reports when only one of them goes.
                    foreach (var lost in onThisLine)
                    {
                        if (survivors.Remove(lost))
                        {
                            continue;
                        }
                        carried.Add($"{byFile.Key}:{mutant.Line} ({mutant.Operator}) carries off `out {lost}`"
                                    + $"\n    {lines[mutant.Line - 1].Trim()}");
                    }
                }
            }

            // The whole list to a file: the assertion carries the count alone, and every entry is a
            // line somebody has to look at.
            if (carried.Count > 0)
            {
                var listing = Path.Combine(Path.GetTempPath(), "velvet-carried-out-arguments.txt");
                File.WriteAllLines(listing, carried);
                Console.WriteLine($"{carried.Count} mutant(s) carrying off an `out` assignment; full list at {listing}");
                foreach (var entry in carried)
                {
                    Console.WriteLine(entry);
                }
            }

            // Assert — the examined count rides along, because a run that reached no `out` argument at
            // all carries nothing off by arithmetic. Measured at 45 over this package, against the
            // sibling fact's 309.
            Assert.Equal(
                (true, 0),
                (examined > 20, carried.Count));
        }

        /// <summary>The unqualified names each line's <c>out</c> arguments assign, discards aside.</summary>
        private static Dictionary<int, List<string>> AssignedOutArgumentsByLine(SyntaxTree tree)
        {
            var found = new Dictionary<int, List<string>>();
            foreach (var argument in tree.GetRoot().DescendantNodes().OfType<ArgumentSyntax>())
            {
                if (!argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)
                    || argument.Expression is not IdentifierNameSyntax named
                    || named.Identifier.ValueText == "_")
                {
                    continue;
                }
                var line = tree.GetLineSpan(argument.Span).StartLinePosition.Line + 1;
                if (!found.TryGetValue(line, out var onThisLine))
                {
                    found[line] = onThisLine = new List<string>();
                }
                onThisLine.Add(named.Identifier.ValueText);
            }
            return found;
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
