using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds which class-driven mechanisms the neuter harness can disable against a checked-in record, so
    /// a mechanism added tomorrow is answered for rather than joining the uncovered majority in silence.
    /// <para>
    /// The harness disables a named mechanism and asks whether the tests named after it noticed. It can
    /// only ask that of a mechanism somebody wrote a cut for, so left alone it protects what it protected
    /// on the day it was written — and nothing said so: 26 of the 30 parsers and appliers here have no cut,
    /// and every one of them fails silently by design, since an unrecognised utility class is ignored.
    /// </para>
    /// <para>
    /// What this replaces asked instead whether a fixture was registered, and decided which fixtures had to
    /// be by matching four names. That could not catch a mechanism nobody had named. Deriving the answer
    /// was tried and does not work: a cut edits <c>FiberNodePatcher</c>, so every fixture mentioning it —
    /// ten of them, about gaps and portals and z-index — reads as required, which is the over-matching its
    /// own comment records having retreated from. What a fixture is about is not in its text.
    /// </para>
    /// <para>
    /// Coverage is, so coverage is what is recorded. <c>NeuterCutAnchorTests</c> keeps the cuts that exist
    /// honest; this keeps the set of cuts from quietly falling behind the code.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class NeuterCoverageTests
    {
        private const string CutsFile = "scripts/test_quality/neuter_cuts.json";
        private const string CoverageFile = "scripts/test_quality/neuter_coverage.txt";
        private const string PackageRoot = "Packages/com.velvet.core";

        [Serializable]
        private sealed class Edit
        {
#pragma warning disable CS0649
            public string file;
#pragma warning restore CS0649
        }

        [Serializable]
        private sealed class Cut
        {
#pragma warning disable CS0649
            public Edit[] edits;
#pragma warning restore CS0649
        }

        [Serializable]
        private sealed class CutMap
        {
#pragma warning disable CS0649
            public Cut[] cuts;
#pragma warning restore CS0649
        }

        /// <summary>Every class-driven mechanism, as its repo-relative path under the package.</summary>
        /// <remarks>
        /// A parser and its applier are the two halves a payload passes through, and both are named by
        /// convention — which is what lets this glob rather than list. Neither shape is a guess: the
        /// registered cuts are of exactly these two kinds.
        /// </remarks>
        private static IReadOnlyList<string> Mechanisms()
        {
            var package = Path.GetFullPath(PackageRoot);
            var found = Directory.GetFiles(Path.Combine(package, "Runtime/Styling"), "Style*Class.cs")
                .Concat(Directory.GetFiles(Path.Combine(package, "Runtime/Reconciler"), "Fiber*Applier.cs"))
                .Select(path => Path.GetRelativePath(package, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            return found;
        }

        private static HashSet<string> CutFiles()
        {
            var map = JsonUtility.FromJson<CutMap>(File.ReadAllText(Path.GetFullPath(CutsFile)));
            return map.cuts
                .SelectMany(cut => cut.edits)
                .Select(edit => edit.file.Replace(PackageRoot + "/", string.Empty))
                .ToHashSet(StringComparer.Ordinal);
        }

        private static HashSet<string> Recorded() =>
            File.ReadAllLines(Path.GetFullPath(CoverageFile))
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

        [Test]
        public void Given_EveryClassDrivenMechanism_When_ComparedToTheRecord_Then_TheUncoveredSetIsWhatWasRecorded()
        {
            // Arrange
            var mechanisms = Mechanisms();
            var cut = CutFiles();
            var recorded = Recorded();

            // Act
            var uncovered = mechanisms.Where(path => !cut.Contains(path)).ToHashSet(StringComparer.Ordinal);
            var arrived = uncovered.Except(recorded).OrderBy(path => path, StringComparer.Ordinal);
            var left = recorded.Except(uncovered).OrderBy(path => path, StringComparer.Ordinal);
            var drift = arrived.Select(path => "+ " + path)
                .Concat(left.Select(path => "- " + path))
                .ToList();

            // Assert — the mechanism count rides along because an empty glob agrees with an empty record.
            // Both directions are reported: an arrival is a mechanism nothing can disable, and a departure
            // is a cut somebody wrote, which the record must lose or it stops meaning anything.
            Assert.That((mechanisms.Count > 20, string.Join("\n", drift)), Is.EqualTo((true, string.Empty)),
                "a class-driven mechanism fails by being ignored, which reads exactly like a class nobody "
                + "wrote; either give it a cut in neuter_cuts.json or record it as uncovered");
        }

        [Test]
        public void Given_TheRecordedSet_When_ItIsReadBack_Then_EveryEntryNamesAFileThatExists()
        {
            // Arrange — a rename leaves an entry describing nothing, and the count keeps looking the same.
            var package = Path.GetFullPath(PackageRoot);
            var recorded = Recorded();

            // Act
            var missing = recorded
                .Where(path => !File.Exists(Path.Combine(package, path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.That((recorded.Count > 0, string.Join("\n", missing)), Is.EqualTo((true, string.Empty)));
        }
    }
}
