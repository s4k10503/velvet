using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.UI;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the starter sample's two shipping facts: that Package Manager can offer it, and that what
    /// ships is what this project builds and tests.
    /// <para>
    /// Unity does not import a <c>~</c>-suffixed folder, so nothing under <c>Samples~</c> is compiled,
    /// opened or laid out here — no test can load that scene. The copy under <c>Assets/</c> is the one the
    /// project imports, the one the PlayMode scene fixture plays, and the one a player build carries; the
    /// byte-for-byte case below is what makes evidence gathered on it evidence about the shipped tree too.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class StarterSampleShippingTests
    {
        private const string PackageName = "com.velvet.core";
        private const string ImportedRoot = "Assets/VelvetStarterSample";
        private const string ShippedRoot = "Packages/com.velvet.core/Samples~/StarterApp";

        // Written by the OS beside real files and by nothing in this repo.
        private const string OsNoiseFile = ".DS_Store";

        [Test]
        public void Given_ThePackage_When_PackageManagerEnumeratesItsSamples_Then_EachIsNamedDescribedAndOnDisk()
        {
            // Arrange
            var version = PackageInfo.FindForPackageName(PackageName).version;

            // Act — the same call the Package Manager window's Samples section makes, so a manifest whose
            // `samples` array is absent or malformed yields nothing here rather than a parse error.
            var samples = Sample.FindByPackage(PackageName, version).ToList();
            var problems = samples
                .Select(Fault)
                .Where(fault => fault != null)
                .Concat(samples.Count == 0
                    ? new[] { $"{PackageName} declares no samples at all" }
                    : Array.Empty<string>())
                .ToList();

            // Assert
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void Given_TheShippedSampleTree_When_ComparedAgainstTheImportedCopy_Then_EveryFileMatchesByteForByte()
        {
            // Arrange
            var imported = RelativeFiles(ImportedRoot);
            var shipped = RelativeFiles(ShippedRoot);

            // Act — an empty imported tree is reported rather than passing quietly, because two absent
            // directories agree on everything.
            var differences = (imported.Count == 0
                    ? new[] { $"{ImportedRoot} holds no files" }
                    : Array.Empty<string>())
                .Concat(imported.Except(shipped).Select(file => $"missing from {ShippedRoot}: {file}"))
                .Concat(shipped.Except(imported).Select(file => $"only in {ShippedRoot}: {file}"))
                .Concat(imported.Intersect(shipped).Where(ContentsDiffer).Select(file => $"contents differ: {file}"))
                .OrderBy(message => message, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.That(differences, Is.Empty,
                "The shipped sample has drifted from the copy this project imports. "
                + "Run scripts/unity/sync_starter_sample.py.\n" + string.Join("\n", differences));
        }

        private static string Fault(Sample sample)
        {
            if (string.IsNullOrWhiteSpace(sample.displayName))
            {
                return "a sample is declared with no displayName";
            }

            if (string.IsNullOrWhiteSpace(sample.description))
            {
                return $"{sample.displayName}: no description";
            }

            return Directory.Exists(sample.resolvedPath)
                ? null
                : $"{sample.displayName}: path resolves to {sample.resolvedPath}, which does not exist";
        }

        // Unity's CWD during a test run is the project root, so a repo-relative root resolves the same way
        // from the Editor and from -runTests batchmode.
        private static IReadOnlyCollection<string> RelativeFiles(string root)
        {
            var full = Path.GetFullPath(root);
            if (!Directory.Exists(full))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(full, file).Replace('\\', '/'))
                .Where(file => Path.GetFileName(file) != OsNoiseFile)
                .ToList();
        }

        private static bool ContentsDiffer(string relative) =>
            !File.ReadAllBytes(Path.Combine(Path.GetFullPath(ImportedRoot), relative))
                .SequenceEqual(File.ReadAllBytes(Path.Combine(Path.GetFullPath(ShippedRoot), relative)));
    }
}
