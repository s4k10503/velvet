using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when a type or member declares itself test-only in the runtime assembly — by its name, or by
    /// saying so in its own comment. Three such batches have been removed: each was a drain or a probe only
    /// a test called, and leaving one in place gives production code a seam to start leaning on, at which
    /// point removing it stops being a refactor. The replacement is a reflection helper under
    /// <c>TestUtilities/</c>.
    /// <para>
    /// Neither axis reaches a member that is test-only and says nothing about it. What would is the
    /// property that actually matters — a member under <c>Runtime/</c> with no production caller — and the
    /// call graph for it is reachable, since <c>Mono.Cecil</c> is a reference of this assembly. What that
    /// needs first is a rule for the members reflection and domain-reload attributes reach, which have no
    /// call site to find and are the bulk of what a first pass reports.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class TestOnlyMemberConventionTests
    {
        // Deliberately wider than the names removed so far: a rename to ForTests or ForUnitTest would
        // otherwise re-open the convention with the guard still green.
        private static readonly Regex TestOnlyName =
            new(@"For(Unit)?Tests?(ing)?$|^TestOnly", RegexOptions.Compiled);

        [Test]
        public void Given_VelvetRuntimeAssembly_When_ScannedForTestOnlyNames_Then_NothingDeclaresOne()
        {
            // Arrange — DeclaredOnly keeps an inherited member from being reported once per subclass.
            const BindingFlags allDeclared = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            var types = typeof(V).Assembly.GetTypes();

            // Act — a property's accessors carry its name too, so reporting them would name one offender twice.
            // Type.Name, not FullName: a nested type's FullName carries its declaring type, which would let a
            // nested TestOnlyFoo match on its parent's name instead of its own.
            var offendingTypes = types
                .Where(type => TestOnlyName.IsMatch(type.Name))
                .Select(type => type.FullName);
            var offendingMembers = types
                .SelectMany(type => type.GetMembers(allDeclared).Select(member => (type, member)))
                .Where(pair => pair.member is not MethodBase { IsSpecialName: true })
                .Where(pair => TestOnlyName.IsMatch(pair.member.Name))
                .Select(pair => $"{pair.type.FullName}.{pair.member.Name}");
            var offenders = offendingTypes.Concat(offendingMembers).Distinct().OrderBy(name => name).ToList();

            // Assert
            Assert.That(offenders, Is.Empty,
                "Runtime declares test-only names; move each to a TestUtilities reflection helper:\n"
                + string.Join("\n", offenders));
        }

        // "test-only" and "test only", in a comment of any kind, on the line above a declaration or on it.
        // Wider than the two that were removed, because the phrase is what an author reaches for and the
        // punctuation between the words is not.
        private static readonly Regex TestOnlyProse =
            new(@"(?i)\btest[- ]only\b", RegexOptions.Compiled);

        private static readonly Regex Declaration =
            new(@"^\s*(?:\[.*\]\s*)?(?:public|internal|private|protected)\s", RegexOptions.Compiled);

        [Test]
        public void Given_TheRuntimeSources_When_ScannedForTestOnlyProse_Then_NoDeclarationClaimsIt()
        {
            // Arrange — the compiled assembly cannot answer this: a comment is not in it.
            var runtime = Path.GetFullPath("Packages/com.velvet.core/Runtime");
            var sources = Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/Tests/"))
                .ToList();

            // Act — a comment claims the declaration it precedes, so the match is reported with the line
            // below it rather than on its own; a trailing comment claims its own line.
            var offenders = new List<string>();
            foreach (var path in sources)
            {
                var lines = File.ReadAllLines(path);
                for (var index = 0; index < lines.Length; index++)
                {
                    if (!TestOnlyProse.IsMatch(lines[index]))
                    {
                        continue;
                    }

                    var declares = Declaration.IsMatch(lines[index])
                        || lines.Skip(index + 1).Take(6).Any(line => Declaration.IsMatch(line));
                    if (declares)
                    {
                        offenders.Add($"{Path.GetRelativePath(runtime, path).Replace('\\', '/')}:{index + 1}");
                    }
                }
            }

            // Assert — the source count rides along because an empty scan reports no offender either, and
            // the offenders are joined rather than compared as lists, which NUnit would settle by reference.
            Assert.That((sources.Count > 100, string.Join("\n", offenders)), Is.EqualTo((true, string.Empty)),
                "a member that says it is test-only is one production can start leaning on; move it to a "
                + "TestUtilities reflection helper");
        }
    }
}
