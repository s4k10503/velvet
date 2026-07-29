using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when a type or member whose name marks it as test-only reappears in the runtime assembly. Three
    /// such batches have been removed: each was a drain or a probe only a test called, and leaving one in
    /// place gives production code a seam to start leaning on, at which point removing it stops being a
    /// refactor. The replacement is a reflection helper under <c>TestUtilities/</c>.
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
    }
}
