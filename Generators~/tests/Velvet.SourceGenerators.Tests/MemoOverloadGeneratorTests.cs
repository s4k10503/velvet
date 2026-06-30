using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Compares the MemoOverloadGenerator output against 16 golden snapshots for arity 1-8 x {Memoized, MemoizedWithKey}.
    /// </summary>
    public sealed class MemoOverloadGeneratorTests
    {
        // Same indentation as method declarations inside the V.cs partial class (namespace + class = 4 + 4 = 8 spaces).
        // Must be updated alongside any spec change to the generator's SourceBuilder.Block().
        private const string MemberIndent = "        ";

        private static readonly string s_generated = MemoOverloadGenerator.GenerateOverloads();

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void Memo_Arity_MatchesSnapshot(int arity)
        {
            AssertMethodMatchesSnapshot("Memoized", arity);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void MemoWithKey_Arity_MatchesSnapshot(int arity)
        {
            AssertMethodMatchesSnapshot("MemoizedWithKey", arity);
        }

        private static void AssertMethodMatchesSnapshot(string methodName, int arity)
        {
            var typeArgs = string.Join(", ", Enumerable.Range(1, arity).Select(i => $"T{i}"));
            var signature = $"public static MemoNode {methodName}<{typeArgs}>";
            var actual = ExtractMethodBlock(s_generated, signature);
            var snapshotPath = Path.Combine(
                AppContext.BaseDirectory,
                "Snapshots",
                $"{methodName}_Arity{arity}.verified.cs");
            Assert.True(File.Exists(snapshotPath), $"Snapshot not found: {snapshotPath}");
            var expected = File.ReadAllText(snapshotPath).Replace("\r\n", "\n").TrimEnd();
            Assert.Equal(expected, actual.TrimEnd());
        }

        /// <summary>
        /// Extracts the section from the doc comment to the closing brace of the method matching the given signature
        /// from the generated source.
        /// </summary>
        private static string ExtractMethodBlock(string source, string signature)
        {
            var normalized = source.Replace("\r\n", "\n");
            var sigIndex = normalized.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(sigIndex >= 0, $"Signature '{signature}' not found in generated source.");

            var summaryMarker = MemberIndent + "/// <summary>";
            var summaryStart = normalized.LastIndexOf(summaryMarker, sigIndex, StringComparison.Ordinal);
            Assert.True(summaryStart >= 0,
                $"Could not locate '{summaryMarker}' preceding the method (member indent mismatch?).");

            var closeMarker = "\n" + MemberIndent + "}\n";
            var closeIndex = normalized.IndexOf(closeMarker, sigIndex, StringComparison.Ordinal);
            Assert.True(closeIndex >= 0,
                $"Could not locate method body close marker '\\n{MemberIndent}}}\\n' (member indent mismatch?).");

            var endIndex = closeIndex + closeMarker.Length;
            return normalized.Substring(summaryStart, endIndex - summaryStart);
        }
    }
}
