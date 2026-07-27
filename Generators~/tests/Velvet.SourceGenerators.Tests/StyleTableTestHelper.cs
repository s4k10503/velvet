using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Velvet.StyleTable;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Derives the utility property table from stylesheet text, and compiles and loads what it emits so the
    /// table can be queried instead of pattern-matched in its source form.
    /// </summary>
    /// <remarks>
    /// Querying the loaded table is what makes the bit packing part of the assertion: a set whose bit indices
    /// disagreed with the emitted vocabulary would still produce plausible-looking source. Compiling it also
    /// proves the emitted C# is valid, which is the one thing a build-time derivation cannot find out on its
    /// own the way a source generator would.
    /// </remarks>
    internal static class StyleTableTestHelper
    {
        public static StyleTableRun Derive(params StyleSheetInput[] sheets)
        {
            var result = StyleUtilityTableBuilder.Build(
                sheets.Select(s => new UssSourceText(s.Path, s.Text)).ToList());
            return new StyleTableRun(
                result.Problems.IsEmpty ? StyleUtilityTableEmitter.Emit(result.Table) : null,
                result.Problems);
        }

        /// <summary>Compiles and loads the emitted table so its lookups can be called.</summary>
        public static StyleTableProbe Load(StyleTableRun run)
        {
            if (run.EmittedSource == null)
            {
                throw new InvalidOperationException(
                    "The derivation reported problems and emitted no table:\n" +
                    string.Join("\n", run.Problems.Select(p => p.ToString())));
            }

            var compilation = CSharpCompilation.Create(
                assemblyName: "VelvetStyleTableProbe",
                syntaxTrees: new[]
                {
                    CSharpSyntaxTree.ParseText(
                        run.EmittedSource,
                        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)),
                },
                references: GeneratorTestHelper.ReferenceAssemblies(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var peStream = new MemoryStream();
            var emit = compilation.Emit(peStream);
            if (!emit.Success)
            {
                var errors = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
                throw new InvalidOperationException(
                    "The emitted table does not compile:\n" + string.Join("\n", errors));
            }
            return new StyleTableProbe(Assembly.Load(peStream.ToArray()));
        }
    }

    /// <summary>A stylesheet handed to the derivation.</summary>
    internal readonly struct StyleSheetInput
    {
        public StyleSheetInput(string path, string text)
        {
            Path = path;
            Text = text;
        }

        public string Path { get; }

        public string Text { get; }

        public static StyleSheetInput Uss(string text) => new StyleSheetInput("/styles/_test.uss", text);
    }

    internal readonly struct StyleTableRun
    {
        public StyleTableRun(string? emittedSource, ImmutableArray<UssProblem> problems)
        {
            EmittedSource = emittedSource;
            Problems = problems;
        }

        /// <summary>Null when the derivation reported a problem, which is when nothing may be written.</summary>
        public string? EmittedSource { get; }

        public ImmutableArray<UssProblem> Problems { get; }

        public IReadOnlyList<string> ProblemCodes =>
            Problems.Select(p => p.Code).OrderBy(code => code, StringComparer.Ordinal).ToList();
    }

    /// <summary>The emitted table, loaded and queryable by class name.</summary>
    internal sealed class StyleTableProbe
    {
        private readonly Type _properties;
        private readonly Type _longhand;
        private readonly Type _rule;
        private readonly Type _transitions;

        public StyleTableProbe(Assembly assembly)
        {
            _properties = Required(assembly, "Velvet.StyleUtilityProperties");
            _longhand = Required(assembly, "Velvet.StyleLonghand");
            _rule = Required(assembly, "Velvet.StyleUtilityRule");
            _transitions = Required(assembly, "Velvet.StyleTransitionUtilities");
        }

        public int Count => (int)_properties.GetProperty("Count", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        public int LonghandCount => (int)_properties.GetField("LonghandCount", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        public bool Defines(string className) => TryGet(className, out _);

        /// <summary>The USS longhand names <paramref name="className"/> writes, sorted.</summary>
        public IReadOnlyList<string> PropertiesOf(string className)
        {
            if (!TryGet(className, out var rule))
            {
                throw new InvalidOperationException($"The table defines no rule for '{className}'.");
            }

            return LonghandNames(_rule.GetProperty("Properties")!.GetValue(rule)!);
        }

        /// <summary>The USS longhand names a <c>StyleLonghandSet</c> holds, sorted.</summary>
        private IReadOnlyList<string> LonghandNames(object set)
        {
            var contains = set.GetType().GetMethod("Contains")!;
            var identifierToUss = UssPropertyVocabulary.Longhands
                .ToDictionary(l => l.Identifier, l => l.UssName, StringComparer.Ordinal);

            var written = new List<string>();
            foreach (var value in Enum.GetValues(_longhand))
            {
                if ((bool)contains.Invoke(set, new[] { value })!)
                {
                    written.Add(identifierToUss[value.ToString()!]);
                }
            }
            written.Sort(StringComparer.Ordinal);
            return written;
        }

        public string GateOf(string className)
        {
            if (!TryGet(className, out var rule))
            {
                throw new InvalidOperationException($"The table defines no rule for '{className}'.");
            }
            return _rule.GetProperty("Gate")!.GetValue(rule)!.ToString()!;
        }

        /// <summary>How many bundled utilities declare <c>transition-property</c>.</summary>
        public int TransitionCount =>
            (int)_transitions.GetProperty("Count", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        /// <summary>The utilities that declare <c>transition-property</c>, in cascade order.</summary>
        public IReadOnlyList<string> TransitionUtilitiesInCascadeOrder()
        {
            var byClassName = (IEnumerable)_transitions
                .GetField("ByClassName", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;
            var byPosition = new SortedDictionary<int, string>();
            foreach (var pair in byClassName)
            {
                var type = pair.GetType();
                byPosition.Add(
                    (int)type.GetProperty("Value")!.GetValue(pair)!,
                    (string)type.GetProperty("Key")!.GetValue(pair)!);
            }
            return byPosition.Values.ToList();
        }

        /// <summary>The USS longhand names <paramref name="className"/>'s transition-property value covers.</summary>
        public IReadOnlyList<string> TransitionPropertiesOf(string className)
        {
            if (!TryGetTransition(className, out _, out var set))
            {
                throw new InvalidOperationException($"'{className}' declares no transition-property.");
            }
            return LonghandNames(set!);
        }

        public bool DeclaresTransition(string className) => TryGetTransition(className, out _, out _);

        private bool TryGetTransition(string className, out int cascadePosition, out object? properties)
        {
            var tryGet = _transitions.GetMethod("TryGet", BindingFlags.Public | BindingFlags.Static)!;
            var arguments = new object?[] { className, null, null };
            var found = (bool)tryGet.Invoke(null, arguments)!;
            cascadePosition = (int)arguments[1]!;
            properties = arguments[2];
            return found;
        }

        private bool TryGet(string className, out object? rule)
        {
            var tryGet = _properties.GetMethod("TryGet", BindingFlags.Public | BindingFlags.Static)!;
            var arguments = new object?[] { className, null };
            var found = (bool)tryGet.Invoke(null, arguments)!;
            rule = arguments[1];
            return found;
        }

        private static Type Required(Assembly assembly, string fullName) =>
            assembly.GetType(fullName)
            ?? throw new InvalidOperationException($"The emitted table declares no '{fullName}'.");
    }
}
