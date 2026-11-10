using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when the diff reads an old instance off the live child list after a container's entry, where it names
    /// one through <see cref="OldSlotOccupants"/> instead. The desync recovery reads the live list on purpose and is
    /// the method allowed to.
    /// </summary>
    [TestFixture]
    internal sealed class ContainerOldInstanceReadTests
    {
        private static readonly string[] DiffSources =
        {
            "Packages/com.velvet.core/Runtime/Reconciler/ChildReconciler.cs",
            "Packages/com.velvet.core/Runtime/Reconciler/GeneralPathReconciler.cs",
        };

        private static readonly HashSet<string> AllowedMethods = new() { "TryRebuildDesyncedSlotRange" };

        // ElementAt, and an indexer whose argument converts a logical slot — the two spellings a slot read takes in
        // the reconciler.
        private static readonly Regex SlotRead =
            new(@"\bElementAt\s*\(|\[[^\[\]\n]*ToPhysical\s*\(", RegexOptions.Compiled);

        private static readonly Regex MethodDeclaration =
            new(@"^\s*(?:(?:private|internal|public|protected|static|override|readonly)\s+)+[\w<>\[\]?,. ]+?\s(\w+)\s*\(",
                RegexOptions.Compiled | RegexOptions.Multiline);

        [Test]
        public void Given_TheDiffSources_When_ScannedForASlotRead_Then_OnlyTheDesyncRecoveryHasOne()
        {
            // Arrange
            var missing = DiffSources.Where(path => !File.Exists(Path.GetFullPath(path))).ToList();

            // Act
            var offenders = new List<string>();
            foreach (var path in DiffSources.Except(missing))
            {
                var code = BlankCommentsAndStrings(File.ReadAllText(Path.GetFullPath(path)));
                foreach (Match read in SlotRead.Matches(code))
                {
                    var method = EnclosingMethod(code, read.Index);
                    if (AllowedMethods.Contains(method)) continue;
                    var line = code.Take(read.Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetFileName(path)}:{line} in {method}");
                }
            }

            // Assert — a source that moved would scan as clean, so its absence is reported in the same text.
            Assert.That(
                string.Join("\n", missing.Select(path => "missing: " + path).Concat(offenders)),
                Is.Empty,
                "the diff names an old instance through OldSlotOccupants after the container's entry");
        }

        private static string EnclosingMethod(string code, int index)
        {
            var name = "<none>";
            foreach (Match declaration in MethodDeclaration.Matches(code))
            {
                if (declaration.Index > index) break;
                name = declaration.Groups[1].Value;
            }
            return name;
        }

        // Comments and string literals become spaces, newlines kept, so a name in prose or in a message is not a read
        // and line numbers still point at the source.
        private static string BlankCommentsAndStrings(string source)
        {
            var output = new StringBuilder(source.Length);
            var i = 0;
            while (i < source.Length)
            {
                if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') { output.Append(' '); i++; }
                }
                else if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    var end = source.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                    end = end < 0 ? source.Length : end + 2;
                    for (; i < end; i++) output.Append(source[i] == '\n' ? '\n' : ' ');
                }
                else if (source[i] == '"')
                {
                    output.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '"' && source[i] != '\n')
                    {
                        if (source[i] == '\\') { output.Append(' '); i++; }
                        if (i < source.Length) { output.Append(' '); i++; }
                    }
                    if (i < source.Length) { output.Append(' '); i++; }
                }
                else
                {
                    output.Append(source[i]);
                    i++;
                }
            }
            return output.ToString();
        }
    }
}
