using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when the runtime looks a member or a type up by a name the compiler does not check, anywhere but in
    /// <c>EngineMember</c>. A member declared there is kept through stripping and resolved against each
    /// editor the suite runs in; one looked up elsewhere is neither.
    /// </summary>
    [TestFixture]
    internal sealed class EngineMemberRegistryTests
    {
        // A nameof argument is checked against the member it names when the runtime compiles, so it cannot drift
        // from a later engine unnoticed. An empty argument list is object.GetType().
        private static readonly Regex LookupByName = new(
            @"\.(?:GetField|GetProperty|GetMethod|GetEvent|GetMember|GetNestedType|GetType)\(\s*(?!nameof\s*\(|\))",
            RegexOptions.Compiled);

        // Strings stay, since a literal name is what the scan is after; comments go, since a comment that quotes a
        // lookup makes none.
        private static readonly Regex CommentOrString = new(
            @"(?<keep>@""(?:""""|[^""])*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])+')|//[^\n]*|/\*[\s\S]*?\*/",
            RegexOptions.Compiled);

        [Test]
        public void Given_TheRuntimeSources_When_ScannedForLookupsByName_Then_OnlyEngineMemberMakesThem()
        {
            // Arrange
            var runtime = Path.GetFullPath("Packages/com.velvet.core/Runtime");
            var sources = Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/Tests/"))
                .Where(path => Path.GetFileName(path) != "EngineMember.cs")
                .ToList();

            // Act
            var offenders = new List<string>();
            foreach (var path in sources)
            {
                var code = CommentOrString.Replace(File.ReadAllText(path),
                    match => match.Groups["keep"].Success ? match.Value : new string('\n', match.Value.Count(c => c == '\n')));
                foreach (Match lookup in LookupByName.Matches(code))
                {
                    var line = code.Take(lookup.Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetRelativePath(runtime, path).Replace('\\', '/')}:{line}");
                }
            }

            // Assert — the source count rides along because an empty scan reports no offender either.
            Assert.That((sources.Count > 100, string.Join("\n", offenders)), Is.EqualTo((true, string.Empty)),
                "declare the member in EngineMember and read it from there, so a player build keeps it and an "
                + "engine upgrade that changes it is reported");
        }
    }
}
