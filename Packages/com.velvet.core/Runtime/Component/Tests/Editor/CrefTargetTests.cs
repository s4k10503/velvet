using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins every XML-doc <c>cref</c> in the package against the stripped identifier corpus
    /// <see cref="DocumentationDriftTests"/> already builds, because a broken cref is CS1574 and nothing
    /// in the Unity compile or the pull-request path raises it: <c>csc.rsp</c> does not emit documentation,
    /// <c>docs/Velvet.Docs.csproj</c> excludes test assemblies, and the docs workflow runs on push to main
    /// only.
    /// </summary>
    [TestFixture]
    internal sealed class CrefTargetTests
    {
        private const string PackagePrefix = "Packages/com.velvet.core/";

        // Names that resolve nowhere in this repo's identifier corpus for a reason: external API the package
        // documents but never spells as code — BCL (<c>NullReferenceException</c>), UI Toolkit
        // (<c>enabledInHierarchy</c>), and Roslyn (<c>IncrementalValuesProvider</c>).
        private static readonly HashSet<string> CrefAllowlist = new()
        {
            "NullReferenceException",
            "enabledInHierarchy",
            "IncrementalValuesProvider",
        };

        private static readonly Regex CrefPattern = new(
            @"<see(?:also)?\s+cref\s*=\s*""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        [Test]
        public void Given_PackageSources_When_XmlDocCrefsAreScanned_Then_EveryTargetAppearsInTheIdentifierCorpus()
        {
            // Arrange
            var corpus = DocumentationDriftTests.SourceIdentifiers.Value;

            // Act
            var crefCount = 0;
            var unresolved = new List<string>();
            foreach (var entry in DocumentationCorpus.RepoEntries(includeClaude: false).Where(e =>
                         e.StartsWith(PackagePrefix, StringComparison.Ordinal)
                         && e.EndsWith(".cs", StringComparison.Ordinal)
                         && File.Exists(e)))
            {
                foreach (Match match in CrefPattern.Matches(File.ReadAllText(entry)))
                {
                    crefCount++;
                    var target = match.Groups[1].Value;
                    var name = ReduceCrefTarget(target);
                    if (!corpus.Contains(name) && !CrefAllowlist.Contains(name))
                    {
                        unresolved.Add($"{entry}: {name} (in cref=\"{target}\")");
                    }
                }
            }

            // Assert — that any cref was read rides along, because a walk that found none satisfies an
            // emptiness check on its own. The count itself is not pinned: adding one is ordinary.
            Assert.That(
                (crefCount > 0, string.Join(", ", unresolved.Distinct().OrderBy(s => s, StringComparer.Ordinal))),
                Is.EqualTo((true, string.Empty)));
        }

        private static string ReduceCrefTarget(string target)
        {
            var reduced = target.Trim();
            var paren = reduced.IndexOf('(');
            if (paren >= 0)
            {
                reduced = reduced[..paren];
            }

            if (reduced.StartsWith("T:", StringComparison.Ordinal)
                || reduced.StartsWith("M:", StringComparison.Ordinal)
                || reduced.StartsWith("P:", StringComparison.Ordinal)
                || reduced.StartsWith("F:", StringComparison.Ordinal)
                || reduced.StartsWith("E:", StringComparison.Ordinal)
                || reduced.StartsWith("N:", StringComparison.Ordinal))
            {
                reduced = reduced[2..];
            }

            var lastDot = reduced.LastIndexOf('.');
            reduced = lastDot < 0 ? reduced : reduced[(lastDot + 1)..];

            var generic = reduced.IndexOfAny(new[] { '{', '`' });
            if (generic >= 0)
            {
                reduced = reduced[..generic];
            }

            return reduced.Trim();
        }
    }
}
