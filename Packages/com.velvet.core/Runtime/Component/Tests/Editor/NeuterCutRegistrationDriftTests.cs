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
    /// Pins that every neuter-eligible guard fixture is registered in <c>neuter_cuts.json</c> before the
    /// harness is next swept. <c>NeuterCutAnchorTests</c> holds anchors and declared fixtures honest once
    /// registered; this one catches a new guard left off the map.
    /// </summary>
    [TestFixture]
    internal sealed class NeuterCutRegistrationDriftTests
    {
        private const string CutsFile = "scripts/test_quality/neuter_cuts.json";

        // Tied to mechanisms that already carry cuts in neuter_cuts.json. A substring rule on Parity or
        // Inclusion alone fired on PaintOpacityParityPlaybackTests and HookWiringCoverageTests in the
        // measurement that led here; prefix-qualified suffixes keep the set to guards the harness names.
        private static readonly Regex[] RegistrationRequiredPatterns =
        {
            new(@"^ClipPathWrapTests$", RegexOptions.Compiled),
            new(@"^StyleRingClassTests$", RegexOptions.Compiled),
            new(@"^TextBalance(?:Parity|Playback)Tests$", RegexOptions.Compiled),
            new(@"^Bundled(?:StyleSheet|Shader)InclusionTests$", RegexOptions.Compiled),
        };

        // ParityPlayback is the shape the broader Parity keyword misclassified; one fixture carries it.
        private static readonly Regex RegistrationExemptPattern =
            new(@"^\w+ParityPlaybackTests$", RegexOptions.Compiled);

        [Serializable]
        private sealed class RegistrationAllowlistEntry
        {
#pragma warning disable CS0649
            public string fixture;
            public string reason;
#pragma warning restore CS0649
        }

        [Serializable]
        private sealed class FixtureCuts
        {
#pragma warning disable CS0649
            public string fixture;
#pragma warning restore CS0649
        }

        [Serializable]
        private sealed class CutMap
        {
#pragma warning disable CS0649
            public FixtureCuts[] fixtures;
            public RegistrationAllowlistEntry[] registrationAllowlist;
#pragma warning restore CS0649
        }

        [Test]
        public void Given_EveryNeuterEligibleGuardFixture_When_TheCutMapIsRead_Then_ItIsRegistered()
        {
            // Arrange
            var map = ReadMap();
            var registered = map.fixtures.Select(entry => entry.fixture).ToHashSet(StringComparer.Ordinal);

            // Act
            var required = GuardFixtures().Where(MatchesRequired).ToList();
            var missing = required
                .Select(FullyQualified)
                .Where(fixture => !registered.Contains(fixture))
                .ToList();

            // Assert — the match count is folded in because an empty scan satisfies "none missing".
            Assert.That((required.Count, string.Join("\n", missing)), Is.EqualTo((6, string.Empty)));
        }

        [Test]
        public void Given_EveryRegistrationExemptFixture_When_TheCutMapIsRead_Then_ItIsAllowlistedWithAReason()
        {
            // Arrange
            var map = ReadMap();
            var allowlisted = AllowlistByFixture(map);

            // Act
            var exempt = GuardFixtures().Where(name => RegistrationExemptPattern.IsMatch(name)).ToList();
            var wrong = (from name in exempt
                         let fixture = FullyQualified(name)
                         let entry = allowlisted.GetValueOrDefault(fixture)
                         where entry == null || string.IsNullOrWhiteSpace(entry.reason)
                         select fixture).ToList();

            // Assert — the exempt count is folded in because a tree with no exempt fixtures satisfies this vacuously.
            Assert.That((exempt.Count, string.Join("\n", wrong)), Is.EqualTo((1, string.Empty)));
        }

        [Test]
        public void Given_EveryRegistrationAllowlistEntry_When_ItsReasonIsRead_Then_ItIsNonEmpty()
        {
            // Arrange
            var map = ReadMap();

            // Act
            var blank = (map.registrationAllowlist ?? Array.Empty<RegistrationAllowlistEntry>())
                .Where(entry => string.IsNullOrWhiteSpace(entry.reason))
                .Select(entry => entry.fixture ?? "(null fixture)")
                .ToList();

            // Assert
            Assert.That(blank, Is.Empty);
        }

        [Test]
        public void Given_EveryRegistrationAllowlistEntry_When_ItIsSoughtInTheTestSources_Then_ItExists()
        {
            // Arrange
            var map = ReadMap();

            // Act
            var missing = (map.registrationAllowlist ?? Array.Empty<RegistrationAllowlistEntry>())
                .Select(entry => entry.fixture)
                .Where(fixture => !string.IsNullOrEmpty(fixture) && DeclaringSource(fixture) == null)
                .ToList();

            // Assert
            Assert.That(missing, Is.Empty);
        }

        [Test]
        public void Given_EveryRegistrationAllowlistEntry_When_ItIsComparedToGuardPatterns_Then_ItMatchesAtLeastOne()
        {
            // Arrange — an entry with no matching pattern is indistinguishable from one added to silence this test.
            var map = ReadMap();

            // Act
            var orphans = (from entry in map.registrationAllowlist ?? Array.Empty<RegistrationAllowlistEntry>()
                           let shortName = ShortName(entry.fixture)
                           where shortName.Length > 0
                           where !MatchesRequired(shortName) && !RegistrationExemptPattern.IsMatch(shortName)
                           select entry.fixture).ToList();

            // Assert
            Assert.That(orphans, Is.Empty);
        }

        private static Dictionary<string, RegistrationAllowlistEntry> AllowlistByFixture(CutMap map) =>
            (map.registrationAllowlist ?? Array.Empty<RegistrationAllowlistEntry>())
            .Where(entry => !string.IsNullOrEmpty(entry.fixture))
            .ToDictionary(entry => entry.fixture, StringComparer.Ordinal);

        private static bool MatchesRequired(string shortName) =>
            RegistrationRequiredPatterns.Any(pattern => pattern.IsMatch(shortName));

        private static IEnumerable<string> GuardFixtures()
        {
            var declaration = new Regex(@"\bclass\s+(\w+Tests)\b");
            return Directory
                .EnumerateFiles(Path.GetFullPath("Packages/com.velvet.core"), "*.cs", SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}"))
                .SelectMany(path => declaration.Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
                .Distinct(StringComparer.Ordinal);
        }

        private static string FullyQualified(string shortName) => $"Velvet.Tests.{shortName}";

        private static string ShortName(string fixture) =>
            fixture == null ? string.Empty : fixture.Substring(fixture.LastIndexOf('.') + 1);

        private static string DeclaringSource(string fixture)
        {
            var shortName = ShortName(fixture);
            var declaration = new Regex(@"\bclass\s+" + Regex.Escape(shortName) + @"\b");
            return Directory.EnumerateFiles(
                    Path.GetFullPath("Packages/com.velvet.core"), "*.cs", SearchOption.AllDirectories)
                .FirstOrDefault(path => declaration.IsMatch(File.ReadAllText(path)));
        }

        private static CutMap ReadMap() =>
            JsonUtility.FromJson<CutMap>(File.ReadAllText(Path.GetFullPath(CutsFile)));
    }
}
