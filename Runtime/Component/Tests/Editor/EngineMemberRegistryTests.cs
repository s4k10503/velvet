using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>Guards the direct reflection, dynamic enum, delegate and fluent-selection spellings covered below.</summary>
    [TestFixture]
    internal sealed class EngineMemberRegistryTests
    {
        private static readonly Regex LookupByName = new(
            @"(?:(?<enum>\bEnum\s*\.\s*(?:TryParse|Parse))|(?<delegate>\bDelegate\s*\.\s*CreateDelegate)|"
            + @"\.\s*(?<member>GetField|GetProperty|GetMethod|GetEvent|GetMember|GetNestedType|GetType|GetInterface|InvokeMember|"
            + @"GetDeclaredField|GetDeclaredProperty|GetDeclaredMethod|GetDeclaredEvent|GetRuntimeField|GetRuntimeProperty|GetRuntimeMethod|GetRuntimeEvent)|"
            + @"\.\s*(?<plural>GetMethods|GetFields|GetProperties|GetEvents|GetMembers|GetNestedTypes|GetInterfaces))(?=\s*\()",
            RegexOptions.Compiled);

        private static readonly Regex WholeNameof = new(
            @"^nameof\s*\(\s*[^()]+\s*\)$",
            RegexOptions.Compiled);

        private static readonly Regex LiteralNameSelection = new(
            @"\.\s*(?:First|FirstOrDefault|Single|SingleOrDefault|Where|Find|FindAll)\s*\([^;]*?"
            + @"\.\s*Name\s*(?:==|\.\s*Equals\s*\()\s*@?""",
            RegexOptions.Compiled | RegexOptions.Singleline);

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
                var source = File.ReadAllText(path);
                foreach (var index in LookupOffsets(source))
                {
                    var line = source.Take(index).Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetRelativePath(runtime, path).Replace('\\', '/')}:{line}");
                }
            }

            // Assert — the source count rides along because an empty scan reports no offender either.
            Assert.That((sources.Count > 100, string.Join("\n", offenders)), Is.EqualTo((true, string.Empty)),
                "declare the member in EngineMember and read it from there, so a player build keeps it and an "
                + "engine upgrade that changes it is reported");
        }

        // GREEN_ON_BASE(construction): this source guard's scanner and examples are carried together; the
        // old `LookupByName` pattern is the cut that misses the added reflection spellings.
        [TestCase("type.GetField (\"x\")")]
        [TestCase("type.GetField\n(\"x\")")]
        [TestCase("type.GetDeclaredField(\"x\")")]
        [TestCase("type.GetRuntimeProperty(\"x\")")]
        [TestCase("type.GetInterface(\"IFoo\")")]
        [TestCase("type.InvokeMember(\"Foo\", flags, null, target, null)")]
        [TestCase("type.GetProperty(nameof(Foo) + \"suffix\")")]
        [TestCase("Enum.TryParse(type, \"Focus\", out var focus)")]
        [TestCase("Enum.Parse(type, \"Focus\")")]
        [TestCase("Delegate.CreateDelegate(type, target, \"Foo\")")]
        [TestCase("type.GetMethods(flags).First(m => m.Name == \"Foo\")")]
        public void Given_ANamedLookupSpelling_When_Scanned_Then_ItIsReported(string code)
        {
            // Arrange
            var source = code;

            // Act
            var offsets = LookupOffsets(source).ToArray();

            // Assert
            Assert.That(offsets.Length, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(construction): these controls exercise the carried scanner; removing the `WholeNameof` exclusion reports compiler-checked names.
        [TestCase("target.GetType()")]
        [TestCase("type.GetField(nameof(Foo), flags)")]
        [TestCase("type.GetProperty ( nameof(Foo) )")]
        [TestCase("type.GetProperty(nameof(Nullable<int>.HasValue))")]
        [TestCase("Enum.TryParse<State>(text, out var state)")]
        [TestCase("Delegate.CreateDelegate(type, method)")]
        [TestCase("type.GetMethods(flags).First(m => m.Name == nameof(Foo))")]
        [TestCase("// type.GetField(\"x\")\nvar value = 1;")]
        [TestCase("var text = \"type.GetField(\\\"x\\\")\";")]
        public void Given_ACheckedOrInertLookupSpelling_When_Scanned_Then_ItIsNotReported(string code)
        {
            // Arrange
            var source = code;

            // Act
            var offsets = LookupOffsets(source).ToArray();

            // Assert
            Assert.That(offsets, Is.Empty);
        }

        private static IEnumerable<int> LookupOffsets(string source)
        {
            var code = CommentOrString.Replace(source, match => match.Groups["keep"].Success ? match.Value
                : new string(' ', match.Length));
            var literals = CommentOrString.Matches(code).Cast<Match>().Where(m => m.Groups["keep"].Success).ToArray();
            foreach (Match lookup in LookupByName.Matches(code))
            {
                if (literals.Any(literal => lookup.Index >= literal.Index && lookup.Index < literal.Index + literal.Length))
                {
                    continue;
                }
                var start = lookup.Index + lookup.Length;
                while (start < code.Length && char.IsWhiteSpace(code[start])) start++;
                if (start >= code.Length || code[start] != '(') continue;
                var arguments = Arguments(code, start, literals, out var end);
                if (lookup.Groups["plural"].Success)
                {
                    var stop = code.IndexOf(';', end);
                    var tail = code.Substring(end, (stop < 0 ? code.Length : stop) - end);
                    if (LiteralNameSelection.IsMatch(tail)) yield return lookup.Index;
                    continue;
                }
                if (lookup.Groups["delegate"].Success)
                {
                    if (arguments.Any(argument => argument.StartsWith("\"", System.StringComparison.Ordinal)
                        || argument.StartsWith("@\"", System.StringComparison.Ordinal))) yield return lookup.Index;
                    continue;
                }
                var nameIndex = lookup.Groups["enum"].Success ? 1 : 0;
                if (arguments.Count <= nameIndex || WholeNameof.IsMatch(arguments[nameIndex])) continue;
                yield return lookup.Index;
            }
        }

        private static List<string> Arguments(string code, int opening, Match[] literals, out int end)
        {
            var arguments = new List<string>();
            var depth = 0;
            var from = opening + 1;
            for (var i = from; i < code.Length; i++)
            {
                var literal = literals.FirstOrDefault(span => span.Index == i);
                if (literal != null) { i += literal.Length - 1; continue; }
                var ch = code[i];
                if (ch is '(' or '[' or '{') depth++;
                if (ch == ')' && depth == 0)
                {
                    var last = code.Substring(from, i - from).Trim();
                    if (last.Length > 0) arguments.Add(last);
                    end = i + 1;
                    return arguments;
                }
                if (ch is ')' or ']' or '}') depth--;
                if (ch != ',' || depth != 0) continue;
                arguments.Add(code.Substring(from, i - from).Trim());
                from = i + 1;
            }
            end = code.Length;
            return arguments;
        }
    }
}
