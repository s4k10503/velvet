using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Holds <c>scripts/test_quality/mutation_check.py</c>'s <c>line removed</c> verdict to what it says:
    /// every mutant carrying it leaves its line running nothing.
    /// </summary>
    /// <remarks>
    /// The verdict is what an author reasons about which mutants exist from, and the generator reads
    /// nothing into the word in front of the parenthesis — so a single-line <c>if</c> or <c>foreach</c>
    /// is taken whole, exactly as a discarded call is. Beside one of those, a verdict naming only the
    /// call reads as a harness that is confused.
    /// <para/>
    /// Tokens rather than statements: an <c>else</c> clause is not a statement, so a reading over statement
    /// nodes would report every removal this shape reaches through one, which the generator emits and
    /// wants. What holds of all of them is that nothing executable is left on the line.
    /// <para/>
    /// Several readings of each file, because a line no reading puts a token on cannot be answered for
    /// at all, and the assertion below is over every mutant rather than over the ones one reading happened
    /// to reach. The set is what lights every line of this package today; a line it leaves dark fails here
    /// rather than passing unread, and the answer is another reading.
    /// <para/>
    /// Its siblings <see cref="MutantParseabilityTests"/> and <see cref="MutantDeclarationRemovalTests"/>
    /// ask whether the mutant compiles at all; this asks whether the name it is filed under is true, which
    /// neither of them can see.
    /// </remarks>
    public sealed class MutantLineRemovalTests
    {
        private const string Operator = "line removed";

        [Fact]
        public void Given_EveryLineRemovalThisPackageGenerates_When_ItIsApplied_Then_NothingExecutableIsLeftOnTheLine()
        {
            // Arrange
            var repository = GeneratedMutants.RepositoryRoot();
            var mutants = GeneratedMutants.Generate(repository)
                .Where(mutant => mutant.Operator == Operator)
                .ToList();
            Assume.NotEmpty(mutants, "the generator emitted line removals to read");

            // Act
            var surviving = new List<string>();
            var examined = 0;
            foreach (var byFile in mutants.GroupBy(mutant => mutant.Path, StringComparer.Ordinal))
            {
                var lines = File.ReadAllLines(Path.Combine(repository, byFile.Key));
                var source = string.Join("\n", lines);
                var readings = MutantParseReadings.For(source).Select(options =>
                    (Options: options, Original: CSharpSyntaxTree.ParseText(source, options))).ToList();
                foreach (var mutant in byFile)
                {
                    if (mutant.Line < 1 || mutant.Line > lines.Length)
                    {
                        surviving.Add($"{byFile.Key}:{mutant.Line} is outside the file");
                        continue;
                    }
                    var swapped = (string[])lines.Clone();
                    swapped[mutant.Line - 1] = mutant.Text;
                    var mutated = string.Join("\n", swapped);
                    var read = false;
                    foreach (var reading in readings)
                    {
                        if (LeftOnLine(reading.Original, mutant.Line).Count == 0)
                        {
                            continue;
                        }
                        read = true;
                        var left = LeftOnLine(CSharpSyntaxTree.ParseText(mutated, reading.Options), mutant.Line);
                        if (left.Count == 1 && left[0].IsKind(SyntaxKind.SemicolonToken)
                            && left[0].Parent is EmptyStatementSyntax)
                        {
                            continue;
                        }
                        surviving.Add($"{byFile.Key}:{mutant.Line} keeps "
                                      + $"`{string.Join(" ", left.Select(token => token.ToString()))}`"
                                      + $"\n    {lines[mutant.Line - 1].Trim()}");
                    }
                    examined += read ? 1 : 0;
                }
            }

            // The whole list to a file: the assertion carries the count alone, and every entry is a
            // line somebody has to look at.
            if (surviving.Count > 0)
            {
                var listing = Path.Combine(Path.GetTempPath(), "velvet-incomplete-line-removals.txt");
                File.WriteAllLines(listing, surviving);
                Console.WriteLine($"{surviving.Count} line removal(s) leaving code behind; full list at {listing}");
                foreach (var entry in surviving)
                {
                    Console.WriteLine(entry);
                }
            }

            // Assert — the floor and the unread count ride along, because a run that reached no mutant,
            // or read only some of them, leaves nothing behind by arithmetic.
            Assert.Equal(
                (true, 0, 0),
                (examined > 1000, mutants.Count - examined, surviving.Count));
        }


        /// <summary>Every token starting on a one-based line, comments and whitespace aside.</summary>
        private static List<SyntaxToken> LeftOnLine(SyntaxTree tree, int line) =>
            tree.GetRoot().DescendantTokens()
                .Where(token => !token.IsKind(SyntaxKind.EndOfFileToken)
                                && tree.GetLineSpan(token.Span).StartLinePosition.Line + 1 == line)
                .ToList();
    }
}
