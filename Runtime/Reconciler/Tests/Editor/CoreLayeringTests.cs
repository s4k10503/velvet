using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when the reconciler or the hook slots name a type Routing declares, or Async names the work loop.
    /// Routing registers with the core through the seams the core declares (<c>HookBlockerSlot</c>, the
    /// disposable an Outlet's scope is held as), and Async owns <c>AwaitContinuationGate</c> rather than
    /// reading the work loop, so each of the two assemblies a split makes can be built without the other
    /// side. Read from the sources with comments and string literals blanked, because namespaces do not track
    /// folders here and a declared name is only found by what a file spells.
    /// </summary>
    [TestFixture]
    internal sealed class CoreLayeringTests
    {
        private static readonly string Runtime = Path.GetFullPath("Packages/com.velvet.core/Runtime");

        // One alternation scanned left to right, so whichever of the five opens first at a position claims
        // the run: a `//` inside a string belongs to the string, and a quote inside a comment to the comment.
        private static readonly Regex CommentOrLiteral = new(
            @"//[^\r\n]*|/\*.*?\*/|@""(?:[^""]|"""")*""|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex TypeDeclaration = new(
            @"\b(?:class|struct|interface|enum|record)\s+(\w+)|\bdelegate\s+[^;(]*?\b(\w+)\s*\(",
            RegexOptions.Compiled);

        private static string Redacted(string path)
        {
            var source = File.ReadAllText(path);
            var blanked = new StringBuilder(source);
            foreach (Match run in CommentOrLiteral.Matches(source))
            {
                for (var i = run.Index; i < run.Index + run.Length; i++)
                {
                    blanked[i] = source[i] == '\n' || source[i] == '\r' ? source[i] : ' ';
                }
            }
            return blanked.ToString();
        }

        private static IEnumerable<string> Sources(string folder) =>
            Directory.EnumerateFiles(Path.Combine(Runtime, folder), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/Tests/", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal);

        // Namespace-level types only: a name one type nests inside another is no seam, and reads as a common
        // word (State, Props, Visit) wherever the reconciler spells it.
        private static SortedSet<string> TypesDeclaredIn(string folder)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in Sources(folder))
            {
                var source = Redacted(path);
                var depth = 0;
                var from = 0;
                foreach (Match declaration in TypeDeclaration.Matches(source))
                {
                    for (; from < declaration.Index; from++)
                    {
                        depth += source[from] == '{' ? 1 : source[from] == '}' ? -1 : 0;
                    }
                    if (depth == 1)
                    {
                        names.Add(declaration.Groups[1].Success ? declaration.Groups[1].Value : declaration.Groups[2].Value);
                    }
                }
            }
            return names;
        }

        private static IEnumerable<string> Spelling(string path, IEnumerable<string> names)
        {
            var source = Redacted(path);
            return names.Where(name => Regex.IsMatch(source, @"\b" + Regex.Escape(name) + @"\b"))
                .Select(name => $"{Path.GetRelativePath(Runtime, path).Replace('\\', '/')} names {name}");
        }

        [Test]
        public void Given_TheReconcilerAndTheHookSlots_When_TheirSourcesAreRead_Then_NoneNamesATypeRoutingDeclares()
        {
            // Arrange
            var routing = TypesDeclaredIn("Routing");
            var core = Sources("Reconciler").Append(Path.Combine(Runtime, "Hooks", "HookSlots.cs")).ToList();

            // Act — an empty Routing would scan every file clean, so its absence is reported in the same text.
            var empty = routing.Count > 20 ? Enumerable.Empty<string>() : new[] { "Routing declares too few types" };
            var offenders = core.SelectMany(path => Spelling(path, routing));

            // Assert
            Assert.That(string.Join("\n", empty.Concat(offenders)), Is.Empty,
                "Routing registers with the core through a seam the core declares; the core names no Routing type");
        }

        [Test]
        public void Given_Async_When_ItsSourcesAreRead_Then_NoneNamesTheWorkLoop()
        {
            // Arrange
            var async = Sources("Async").ToList();

            // Act
            var empty = async.Count > 10 ? Enumerable.Empty<string>() : new[] { "Async holds too few sources" };
            var offenders = async.SelectMany(path => Spelling(path, new[] { nameof(FiberWorkLoop) }));

            // Assert
            Assert.That(string.Join("\n", empty.Concat(offenders)), Is.Empty,
                "Async declares the gate the work loop sets; it names no member of the work loop");
        }
    }
}
