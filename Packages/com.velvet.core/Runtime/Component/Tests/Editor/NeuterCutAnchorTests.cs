using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the source anchors <c>scripts/neuter-check.py</c> cuts at. The harness fails loudly when one
    /// stops matching, but it fails at next use — a rename can sit broken for as long as nobody sweeps,
    /// and the sweep is the thing that finds tests which stopped asking anything. Pinning the anchors
    /// here moves that failure to the pull request that renames the member.
    /// </summary>
    [TestFixture]
    internal sealed class NeuterCutAnchorTests
    {
        private const string CutsFile = "scripts/neuter-cuts.json";

        [Serializable]
        private sealed class Edit
        {
#pragma warning disable CS0649 // assigned by JsonUtility
            public string file;
            public string anchor;
            public string neuter;
#pragma warning restore CS0649
        }

        [Serializable]
        private sealed class Cut
        {
#pragma warning disable CS0649
            public string name;
            public string mechanism;
            public string summary;
            public Edit[] edits;
#pragma warning restore CS0649
        }

        [Serializable]
        private sealed class CaseScope
        {
#pragma warning disable CS0649
            public string mechanism;
            public string pattern;
#pragma warning restore CS0649
        }

        [Serializable]
        private sealed class FixtureCuts
        {
#pragma warning disable CS0649
            public string fixture;
            public string[] cuts;
            public CaseScope[] caseScopes;
#pragma warning restore CS0649
        }

        [Serializable]
        private sealed class CutMap
        {
#pragma warning disable CS0649
            public Cut[] cuts;
            public FixtureCuts[] fixtures;
#pragma warning restore CS0649
        }

        [Test]
        public void Given_EveryDeclaredCut_When_ItsAnchorIsSoughtInTheSource_Then_EachMatchesExactlyOnce()
        {
            // Arrange — an anchor matching twice is as broken as one matching zero times: the harness
            // neuters one of the two and reports the other's tests as holes.
            var map = ReadMap();

            // Act
            var wrong = (from cut in map.cuts
                         from edit in cut.edits
                         let path = Path.GetFullPath(edit.file)
                         let hits = File.Exists(path)
                             ? File.ReadAllLines(path).Count(line => line.Trim() == edit.anchor)
                             : -1
                         where hits != 1
                         select $"{cut.name}: {edit.file} matched {hits} times: {edit.anchor}").ToList();

            // Assert — the edit count is folded in because a map that parsed to nothing satisfies
            // "no anchor is wrong" exactly, and a renamed JSON key is what produces that.
            Assert.That((map.cuts.Sum(cut => cut.edits.Length), string.Join("\n", wrong)),
                Is.EqualTo((6, string.Empty)));
        }

        [Test]
        public void Given_EveryDeclaredCut_When_TheLineAfterItsAnchorIsRead_Then_ItIsABodyBrace()
        {
            // Arrange — the neuter is inserted after that brace, so an expression-bodied member or a
            // signature wrapped across lines would have it land outside the body and not compile.
            var map = ReadMap();

            // Act
            var wrong = (from cut in map.cuts
                         from edit in cut.edits
                         let lines = File.ReadAllLines(Path.GetFullPath(edit.file))
                         let anchor = Array.FindIndex(lines, line => line.Trim() == edit.anchor)
                         where anchor < 0 || FirstNonBlankAfter(lines, anchor) != "{"
                         select $"{cut.name}: {edit.file}: {edit.anchor}").ToList();

            // Assert
            Assert.That(wrong, Is.Empty, string.Join("\n", wrong));
        }

        [Test]
        public void Given_EveryFixtureInTheCutMap_When_ItsCutsAreResolved_Then_EachIsDeclared()
        {
            // Arrange
            var map = ReadMap();
            var declared = map.cuts.Select(cut => cut.name).ToHashSet(StringComparer.Ordinal);

            // Act
            var dangling = (from entry in map.fixtures
                            from name in entry.cuts
                            where !declared.Contains(name)
                            select $"{entry.fixture} names '{name}'").ToList();

            // Assert
            Assert.That((map.fixtures.Length, string.Join("\n", dangling)), Is.EqualTo((2, string.Empty)));
        }

        [Test]
        public void Given_EveryFixtureInTheCutMap_When_SoughtInTheTestSources_Then_EachIsDeclaredThere()
        {
            // Arrange — a fixture renamed out from under the map makes -testFilter select nothing, and a
            // run over no tests reports no holes.
            var map = ReadMap();
            var sources = Directory.EnumerateFiles(
                Path.GetFullPath("Packages/com.velvet.core"), "*Tests.cs", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension)
                .ToHashSet(StringComparer.Ordinal);

            // Act
            var missing = map.fixtures
                .Select(entry => entry.fixture)
                .Where(fixture => !sources.Contains(fixture.Substring(fixture.LastIndexOf('.') + 1)))
                .ToList();

            // Assert
            Assert.That(missing, Is.Empty, string.Join("\n", missing));
        }

        [Test]
        public void Given_AFixtureWhoseCutsSpanTwoMechanisms_When_ItsCasesAreClassified_Then_EachMatchesExactlyOneScope()
        {
            // Arrange — a case matching neither scope is never asked anything and reports no holes; a case
            // matching both is reported as a hole by whichever cut it does not belong to. The first run of
            // this harness handed back every ring case in the clip fixture as a hole under the clip applier
            // cut, which is what this case exists to make impossible to repeat.
            var map = ReadMap();

            // Act
            var wrong = (from entry in map.fixtures
                         where Mechanisms(map, entry).Count > 1
                         from name in CaseNames(entry.fixture)
                         let matched = entry.caseScopes.Count(scope => Regex.IsMatch(name, scope.pattern))
                         where matched != 1
                         select $"{entry.fixture}.{name} matched {matched} scopes").ToList();

            // Assert — the scoped-fixture count is folded in because a map whose fixtures all read as
            // single-mechanism satisfies this by never classifying anything.
            Assert.That(
                (map.fixtures.Count(entry => Mechanisms(map, entry).Count > 1), string.Join("\n", wrong)),
                Is.EqualTo((1, string.Empty)));
        }

        [Test]
        public void Given_AFixtureWhoseCutsAreOneMechanism_When_ItsScopesAreRead_Then_ItDeclaresNone()
        {
            // Arrange — every case in such a fixture is in scope for its only mechanism, so a pattern there
            // silently narrows the sweep instead of classifying it. One case in the ring parser fixture
            // carries neither "Ring" nor "Outline" in its name, so a pattern would drop it.
            var map = ReadMap();

            // Act
            var declaring = map.fixtures
                .Where(entry => Mechanisms(map, entry).Count == 1 && entry.caseScopes.Length > 0)
                .Select(entry => entry.fixture)
                .ToList();

            // Assert
            Assert.That(declaring, Is.Empty, string.Join("\n", declaring));
        }

        private static HashSet<string> Mechanisms(CutMap map, FixtureCuts entry) =>
            map.cuts.Where(cut => entry.cuts.Contains(cut.name))
                .Select(cut => cut.mechanism)
                .ToHashSet(StringComparer.Ordinal);

        private static IEnumerable<string> CaseNames(string fixture)
        {
            var shortName = fixture.Substring(fixture.LastIndexOf('.') + 1);
            var path = Directory.EnumerateFiles(
                Path.GetFullPath("Packages/com.velvet.core"), shortName + ".cs", SearchOption.AllDirectories)
                .FirstOrDefault();
            return path == null
                ? Enumerable.Empty<string>()
                : Regex.Matches(File.ReadAllText(path), @"public\s+(?:void|IEnumerator)\s+(Given_\w+)")
                    .Select(match => match.Groups[1].Value);
        }

        private static string FirstNonBlankAfter(IReadOnlyList<string> lines, int index)
        {
            for (var next = index + 1; next < lines.Count; next++)
            {
                if (lines[next].Trim().Length > 0)
                {
                    return lines[next].Trim();
                }
            }
            return string.Empty;
        }

        private static CutMap ReadMap() =>
            JsonUtility.FromJson<CutMap>(File.ReadAllText(Path.GetFullPath(CutsFile)));
    }
}
