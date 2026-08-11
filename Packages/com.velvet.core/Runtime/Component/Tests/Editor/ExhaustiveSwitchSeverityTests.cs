using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds every production source that suppresses CS8524 — the discard-less classification switches — to
    /// an assembly whose <c>csc.rsp</c> raises CS8509 to an error. Suppressing CS8524 is how a site declares
    /// that its arms are meant to cover the enum; CS8509 is the only thing that then reports a member no arm
    /// covers, and measured on this project it is a warning nothing gates on: the same probe member compiled
    /// clean without the flag and failed the build with it.
    /// </summary>
    [TestFixture]
    internal sealed class ExhaustiveSwitchSeverityTests
    {
        private const string PackageRoot = "Packages/com.velvet.core";
        private const string Suppression = "#pragma warning disable CS8524";
        private const string Severity = "-warnaserror:CS8509";

        // Test assemblies are out of scope and deliberately carry no severity flag, and skipping them is also
        // what keeps this file — which names the token it searches for — from matching itself.
        private static bool IsProduction(string path) =>
            !path.Contains("/Tests/", StringComparison.Ordinal)
            && !path.Contains("Generators~", StringComparison.Ordinal)
            && !path.Contains("Samples~", StringComparison.Ordinal);

        private static string? AssemblyDirectoryOf(string file)
        {
            var package = Path.GetFullPath(PackageRoot);
            for (var dir = Directory.GetParent(file); dir != null; dir = dir.Parent)
            {
                if (Directory.EnumerateFiles(dir.FullName, "*.asmdef").Any())
                {
                    return dir.FullName;
                }
                if (string.Equals(dir.FullName, package, StringComparison.Ordinal))
                {
                    break;
                }
            }
            return null;
        }

        [Test]
        public void Given_EveryProductionSourceSuppressingCs8524_When_ItsAssemblyIsAsked_Then_Cs8509IsAnError()
        {
            // Arrange
            var suppressing = Directory
                .EnumerateFiles(Path.GetFullPath(PackageRoot), "*.cs", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/'))
                .Where(IsProduction)
                .Where(path => File.ReadAllText(path).Contains(Suppression, StringComparison.Ordinal))
                .ToList();
            Assume.That(suppressing, Is.Not.Empty);

            // Act
            var unguarded = new List<string>();
            foreach (var file in suppressing)
            {
                var assembly = AssemblyDirectoryOf(file);
                var rsp = assembly == null ? null : Path.Combine(assembly, "csc.rsp");
                if (rsp == null || !File.Exists(rsp)
                    || !File.ReadAllText(rsp).Contains(Severity, StringComparison.Ordinal))
                {
                    unguarded.Add(Path.GetFileName(file));
                }
            }

            // Assert
            Assert.That(unguarded, Is.Empty);
        }
    }
}
