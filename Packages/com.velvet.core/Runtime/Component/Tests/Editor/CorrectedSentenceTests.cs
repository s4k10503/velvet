using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when a sentence CLAUDE.md records as false reappears anywhere else in the tree. Each entry in
    /// that list was copy-pasted into a comment, a skill, or prose, found wrong, and corrected — a
    /// reintroduction is the same failure mode with nothing to catch it today.
    /// </summary>
    [TestFixture]
    internal sealed class CorrectedSentenceTests
    {
        private const string ClaudePath = "CLAUDE.md";

        // The list entries are the only lines in CLAUDE.md that open with a hyphen, a space, and a quote.
        private static readonly Regex CorrectedSentenceEntryPattern =
            new(@"^- ""([^""]+)""", RegexOptions.Compiled);

        private static readonly string[] ScannedExtensions = { ".cs", ".md", ".py", ".sh" };

        [Test]
        public void Given_TheRepoSources_When_ScannedForCorrectedSentences_Then_NoneReappearOutsideTheList()
        {
            // Arrange — derived from CLAUDE.md so a sixth list entry needs no edit here.
            var sentences = ExtractCorrectedSentences();

            // Act
            var findings = ScanForSentences(sentences);

            // Assert — the count rides along so an extractor that found nothing cannot satisfy emptiness alone.
            Assert.That(
                (sentences.Count > 0, string.Join("\n", findings)),
                Is.EqualTo((true, string.Empty)),
                "A corrected sentence reappeared outside the CLAUDE.md list:\n" + string.Join("\n", findings));
        }

        private static List<string> ExtractCorrectedSentences()
        {
            var sentences = new List<string>();
            foreach (var line in File.ReadAllLines(ClaudePath))
            {
                var match = CorrectedSentenceEntryPattern.Match(line);
                if (match.Success)
                {
                    sentences.Add(match.Groups[1].Value);
                }
            }
            return sentences;
        }

        private static List<string> ScanForSentences(IReadOnlyList<string> sentences)
        {
            var findings = new List<string>();
            foreach (var entry in DocumentationCorpus.RepoEntries(includeClaude: true))
            {
                if (!ScannedExtensions.Any(extension => entry.EndsWith(extension, StringComparison.Ordinal))
                    || !File.Exists(entry))
                {
                    continue;
                }
                var lines = File.ReadAllLines(entry);
                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    var line = lines[lineIndex];
                    if (entry == ClaudePath && CorrectedSentenceEntryPattern.IsMatch(line))
                    {
                        continue;
                    }
                    foreach (var sentence in sentences)
                    {
                        if (line.Contains(sentence, StringComparison.Ordinal))
                        {
                            findings.Add($"{entry}:{lineIndex + 1}: {sentence}");
                        }
                    }
                }
            }
            return findings;
        }
    }
}
