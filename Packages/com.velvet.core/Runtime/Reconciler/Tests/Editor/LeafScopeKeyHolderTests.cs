using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a key <c>FiberKeying.LeafScope</c> composes can end up: the only call in the runtime sources
    /// hands it to <c>ChildKey.Explicit</c>, whose hash reads every character (<see cref="ChildKeyHashTests"/>), so
    /// no <c>Dictionary&lt;string, …&gt;</c> or <c>HashSet&lt;string&gt;</c> holds one, and none needs a comparer
    /// that does. A second caller is the point to give whatever holds its result such a comparer, or to show it never
    /// holds one.
    /// </summary>
    [TestFixture]
    internal sealed class LeafScopeKeyHolderTests
    {
        private static readonly Regex Call = new(@"\bLeafScope\(", RegexOptions.Compiled);

        // GREEN_ON_BASE(construction): both sides here are the repository's own sources. Add a call such as `var scope = FiberKeying.LeafScope(parent, key, 0);` to another file under Runtime and this is what reddens.
        [Test]
        public void Given_TheRuntimeSources_When_ScannedForCallsOfLeafScope_Then_OnlyReconcileKeyingMakesOne()
        {
            // Arrange
            var runtime = Path.GetFullPath("Packages/com.velvet.core/Runtime");
            var sources = Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/Tests/"));

            // Act
            var callers = sources
                .Where(path => File.ReadLines(path)
                    .Select(line => line.TrimStart())
                    .Any(line => !line.StartsWith("//") && !line.Contains("static string LeafScope(") && Call.IsMatch(line)))
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToArray();

            // Assert
            Assert.That(callers, Is.EqualTo(new[] { "ReconcileKeying.cs" }));
        }
    }
}
