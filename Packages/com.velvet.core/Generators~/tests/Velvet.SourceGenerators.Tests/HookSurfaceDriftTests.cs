using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Velvet.SourceGenerators.AutoDeps;
using Velvet.SourceGenerators.Shared;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins the analyzer's hook-name surface to the runtime's. Which methods are hooks is encoded on both
    /// sides of a compile boundary this solution cannot cross: the ILPP weaver uses <c>nameof</c> and breaks
    /// loudly on a rename, while the analyzer's strings would keep compiling against a name that no longer
    /// exists — exhaustive-deps would simply stop reporting, with nothing red. This fixture re-derives the
    /// runtime surface by parsing the runtime sources with Roslyn (syntax only: no assembly references, no
    /// build ordering, and no dependency on a Unity-produced artifact that CI does not have) and fails when
    /// the two no longer line up.
    /// </summary>
    /// <remarks>
    /// A syntax-only parse cannot see members inside a disabled preprocessor region. Both the symbol-free and
    /// the <c>UNITY_EDITOR</c> parse are unioned, so an editor-only declaration is still covered; a member
    /// gated on any other symbol's exclusive branch would stay invisible to this guard.
    /// </remarks>
    public sealed class HookSurfaceDriftTests
    {
        private const string DepsParameterName = "deps";

        // The two runtime types the analyzer binds hook calls to. Hook-name constants are resolved against
        // the union of their declared methods.
        private static readonly string[] HookHostTypes =
        {
            VelvetWellKnownNames.HooksTypeFullName,
            VelvetWellKnownNames.VTypeFullName,
        };

        /// <summary>
        /// Runtime methods that take a <c>deps</c> parameter yet are deliberately outside exhaustive-deps
        /// coverage, each with the reason it cannot be analyzed. Stated here so an intentional omission is a
        /// decision recorded in code rather than an absence nobody can tell from an oversight.
        /// </summary>
        private static readonly Dictionary<string, string> DepsMethodsOutsideAnalyzerCoverage = new()
        {
            ["Velvet.Hooks.UseAnimationSequence"] =
                "takes no delegate — its deps gate a declarative step list, so there are no closure captures to compare",
            ["Velvet.Hooks.TryGetMemoizedVNode"] =
                "auto-memoization plumbing whose deps array the ILPP weaver emits; never a hand-written call site",
            ["Velvet.Hooks.StoreMemoizedVNode"] =
                "auto-memoization plumbing whose deps array the ILPP weaver emits; never a hand-written call site",
        };

        private static readonly Lazy<RuntimeSourceIndex> Runtime = new(RuntimeSourceIndex.Build);

        [Fact]
        public void Given_WellKnownMethodNameConstants_When_ResolvedAgainstRuntimeSource_Then_EachNamesADeclaredMethod()
        {
            // Arrange
            var declared = HookHostTypes
                .SelectMany(type => Runtime.Value.PublicMethodNamesOf(type))
                .ToHashSet(StringComparer.Ordinal);
            Assume.NotEmpty(declared, "no public methods parsed off the runtime hook host types");

            // Act
            var unresolved = ConstantsWithSuffix("MethodName")
                .Where(c => !declared.Contains(c.Value))
                .Select(c => $"{c.Name} = \"{c.Value}\"")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(unresolved.Count == 0,
                $"{nameof(VelvetWellKnownNames)} names methods that no longer exist on " +
                $"[{string.Join(", ", HookHostTypes)}]: [{string.Join(", ", unresolved)}]. The analyzer matches " +
                "hooks by name, so it silently stops covering a renamed hook.");
        }

        [Fact]
        public void Given_WellKnownTypeNameConstants_When_ResolvedAgainstRuntimeSource_Then_EachNamesADeclaredType()
        {
            // Arrange
            var declared = Runtime.Value.DeclaredTypeFullNames;
            Assume.NotEmpty(declared, "no type declarations parsed off the runtime sources");

            // Act
            var unresolved = ConstantsWithSuffix("FullName")
                .Where(c => !declared.Contains(StripGlobalPrefix(c.Value)))
                .Select(c => $"{c.Name} = \"{c.Value}\"")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(unresolved.Count == 0,
                $"{nameof(VelvetWellKnownNames)} names types that are not declared under the runtime sources: " +
                $"[{string.Join(", ", unresolved)}]. Attribute and type lookups resolve by metadata name, so a " +
                "renamed or moved type turns the generator into a no-op instead of an error.");
        }

        [Fact]
        public void Given_RuntimeDepsComparingHooks_When_LookedUpInTheDescriptorTable_Then_EachHasADescriptor()
        {
            // Arrange
            var qualifying = QualifyingDepsHooks();
            Assume.NotEmpty(qualifying, "no deps-comparing hooks matched in the runtime sources");

            // Act
            var uncovered = qualifying
                .Where(hook => !HasDescriptorBoundTo(hook.MethodName, hook.ContainingTypeFullName))
                .Select(hook => hook.FullName)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(uncovered.Count == 0,
                $"These runtime hooks take a delegate plus a '{DepsParameterName}' parameter but have no " +
                $"{nameof(DepsHookDescriptor)} entry, so VEL100 exhaustive-deps does not analyze them: " +
                $"[{string.Join(", ", uncovered)}]. Add a descriptor, or record the reason in " +
                $"{nameof(DepsMethodsOutsideAnalyzerCoverage)}.");
        }

        [Fact]
        public void Given_RuntimeDepsTakingMethods_When_NotShapedForDepsComparison_Then_EachIsRecordedAsOutOfCoverage()
        {
            // Arrange
            var qualifying = QualifyingDepsHooks().Select(h => h.FullName).ToHashSet(StringComparer.Ordinal);

            // Act
            var unaccounted = DepsTakingMethodNames()
                .Where(name => !qualifying.Contains(name) && !DepsMethodsOutsideAnalyzerCoverage.ContainsKey(name))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(unaccounted.Count == 0,
                $"These runtime methods take a '{DepsParameterName}' parameter but do not match the " +
                "delegate-plus-deps shape the analyzer can inspect: " + $"[{string.Join(", ", unaccounted)}]. " +
                "The shape match is syntactic and can miss a custom delegate type, so each such method must " +
                $"either qualify or be recorded in {nameof(DepsMethodsOutsideAnalyzerCoverage)} with its reason.");
        }

        [Fact]
        public void Given_RecordedOutOfCoverageMethods_When_ComparedAgainstRuntimeSource_Then_EachStillExists()
        {
            // Arrange
            var depsTaking = DepsTakingMethodNames();
            Assume.NotEmpty(depsTaking, "no deps-taking methods matched in the runtime sources");

            // Act
            var stale = DepsMethodsOutsideAnalyzerCoverage
                .Where(entry => !depsTaking.Contains(entry.Key))
                .Select(entry => $"{entry.Key} (recorded as: {entry.Value})")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(stale.Count == 0,
                $"{nameof(DepsMethodsOutsideAnalyzerCoverage)} records methods that no longer take a " +
                $"'{DepsParameterName}' parameter on [{string.Join(", ", HookHostTypes)}]: " +
                $"[{string.Join("; ", stale)}]. A stale exclusion hides a real gap the next time that name " +
                "comes back.");
        }

        /// <summary>
        /// Every public method on the well-known host types that declares a <c>deps</c> parameter, whatever
        /// its shape. Overloads collapse to one entry — the analyzer matches by name.
        /// </summary>
        private static HashSet<string> DepsTakingMethodNames() =>
            HookHostTypes
                .SelectMany(type => Runtime.Value.PublicMethodsOf(type))
                .Where(m => m.Method.ParameterList.Parameters.Any(IsDepsParameter))
                .Select(m => $"{m.ContainingTypeFullName}.{m.Method.Identifier.ValueText}")
                .ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// Runtime methods the analyzer is expected to cover: a <c>deps</c> parameter (so there is a declared
        /// dependency list) plus a delegate parameter (so there are closure captures to compare against it),
        /// minus the recorded exclusions. Overloads collapse to one entry — the analyzer matches by name.
        /// </summary>
        private static List<HookDeclaration> QualifyingDepsHooks() =>
            HookHostTypes
                .SelectMany(type => Runtime.Value.PublicMethodsOf(type))
                .Where(m => m.Method.ParameterList.Parameters.Any(IsDepsParameter))
                .Where(m => m.Method.ParameterList.Parameters.Any(p => IsDelegateTyped(p, m.Method)))
                .Select(m => new HookDeclaration(m.ContainingTypeFullName, m.Method.Identifier.ValueText))
                .Where(hook => !DepsMethodsOutsideAnalyzerCoverage.ContainsKey(hook.FullName))
                .Distinct()
                .ToList();

        private static bool HasDescriptorBoundTo(string methodName, string containingTypeFullName) =>
            DepsHookDescriptor.TryGet(methodName, out var descriptor)
            && descriptor.ContainingTypeFullName == containingTypeFullName;

        private static bool IsDepsParameter(ParameterSyntax parameter) =>
            parameter.Identifier.ValueText == DepsParameterName;

        /// <summary>
        /// Syntactic delegate-parameter match: a <c>Func</c> / <c>Action</c> parameter, or a type parameter
        /// the method constrains to <c>Delegate</c> (<c>UseCallback&lt;T&gt;(T, params object[]) where T :
        /// Delegate</c>). A custom delegate type is indistinguishable from any other type name without
        /// symbols, which is why the non-matching remainder is asserted against a recorded exclusion list
        /// rather than ignored.
        /// </summary>
        private static bool IsDelegateTyped(ParameterSyntax parameter, MethodDeclarationSyntax method)
        {
            var typeName = SimpleTypeName(parameter.Type);
            if (typeName is null) return false;
            if (typeName is "Func" or "Action") return true;

            return method.ConstraintClauses.Any(clause =>
                clause.Name.Identifier.ValueText == typeName
                && clause.Constraints.OfType<TypeConstraintSyntax>()
                    .Any(constraint => SimpleTypeName(constraint.Type) == "Delegate"));
        }

        private static string? SimpleTypeName(TypeSyntax? type) =>
            type switch
            {
                NullableTypeSyntax nullable => SimpleTypeName(nullable.ElementType),
                QualifiedNameSyntax qualified => SimpleTypeName(qualified.Right),
                AliasQualifiedNameSyntax alias => SimpleTypeName(alias.Name),
                GenericNameSyntax generic => generic.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => null,
            };

        private static string StripGlobalPrefix(string fullName) =>
            fullName.StartsWith("global::", StringComparison.Ordinal) ? fullName.Substring("global::".Length) : fullName;

        // Reflects over the constants instead of restating them, so a constant added to the well-known-names
        // type is guarded without also editing this fixture.
        private static IEnumerable<(string Name, string Value)> ConstantsWithSuffix(string suffix) =>
            typeof(VelvetWellKnownNames)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.EndsWith(suffix, StringComparison.Ordinal))
                .Select(f => (f.Name, (string)f.GetRawConstantValue()!));

        private readonly record struct HookDeclaration(string ContainingTypeFullName, string MethodName)
        {
            public string FullName => $"{ContainingTypeFullName}.{MethodName}";
        }

        private readonly record struct MethodDeclaration(string ContainingTypeFullName, MethodDeclarationSyntax Method);

        /// <summary>
        /// Namespace-qualified index of the type and public-method declarations in the Velvet runtime sources.
        /// </summary>
        private sealed class RuntimeSourceIndex
        {
            // The Unity build defines this; parsing with and without it keeps editor-only declarations visible.
            private static readonly string[] EditorPreprocessorSymbols = { "UNITY_EDITOR" };

            private readonly List<MethodDeclaration> _methods;

            private RuntimeSourceIndex(HashSet<string> types, List<MethodDeclaration> methods)
            {
                DeclaredTypeFullNames = types;
                _methods = methods;
            }

            public IReadOnlySet<string> DeclaredTypeFullNames { get; }

            public IEnumerable<MethodDeclaration> PublicMethodsOf(string containingTypeFullName) =>
                _methods.Where(m => m.ContainingTypeFullName == containingTypeFullName);

            public IEnumerable<string> PublicMethodNamesOf(string containingTypeFullName) =>
                PublicMethodsOf(containingTypeFullName).Select(m => m.Method.Identifier.ValueText);

            public static RuntimeSourceIndex Build()
            {
                var files = RuntimeSourceFiles();
                var types = new HashSet<string>(StringComparer.Ordinal);
                var methods = new List<MethodDeclaration>();

                foreach (var file in files)
                {
                    var text = File.ReadAllText(file);
                    Collect(CSharpSyntaxTree.ParseText(text, DefaultParseOptions), types, methods);
                    Collect(CSharpSyntaxTree.ParseText(text, EditorParseOptions), types, methods);
                }

                return new RuntimeSourceIndex(types, methods);
            }

            private static CSharpParseOptions DefaultParseOptions =>
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

            private static CSharpParseOptions EditorParseOptions =>
                DefaultParseOptions.WithPreprocessorSymbols(EditorPreprocessorSymbols);

            private static void Collect(SyntaxTree tree, HashSet<string> types, List<MethodDeclaration> methods)
            {
                var root = tree.GetRoot();

                // Enums and delegates carry no methods but can still be named by a well-known-name constant.
                foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                {
                    AddNames(types, declaration, declaration.Identifier.ValueText);
                }
                foreach (var declaration in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
                {
                    AddNames(types, declaration, declaration.Identifier.ValueText);
                }

                foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    var displayName = QualifiedName(declaration, declaration.Identifier.ValueText, nestingSeparator: ".");
                    foreach (var method in declaration.Members.OfType<MethodDeclarationSyntax>())
                    {
                        if (!method.Modifiers.Any(SyntaxKind.PublicKeyword)) continue;
                        methods.Add(new MethodDeclaration(displayName, method));
                    }
                }
            }

            // A nested type is spelled Outer.Inner by the analyzer's display-string comparisons and Outer+Inner
            // by the attribute lookups' metadata names; both forms are indexed so either resolves.
            private static void AddNames(HashSet<string> types, SyntaxNode declaration, string identifier)
            {
                types.Add(QualifiedName(declaration, identifier, nestingSeparator: "."));
                types.Add(QualifiedName(declaration, identifier, nestingSeparator: "+"));
            }

            private static string QualifiedName(SyntaxNode declaration, string identifier, string nestingSeparator)
            {
                var typeSegments = new List<string> { identifier };
                var containingNamespace = string.Empty;
                foreach (var ancestor in declaration.Ancestors())
                {
                    switch (ancestor)
                    {
                        case BaseTypeDeclarationSyntax outer:
                            typeSegments.Insert(0, outer.Identifier.ValueText);
                            break;
                        case BaseNamespaceDeclarationSyntax ns:
                            containingNamespace = ns.Name.ToString() +
                                (containingNamespace.Length == 0 ? string.Empty : "." + containingNamespace);
                            break;
                    }
                }

                var nested = string.Join(nestingSeparator, typeSegments);
                return containingNamespace.Length == 0 ? nested : containingNamespace + "." + nested;
            }

            // Colocated tests declare their own fixtures and would let a stub stand in for a renamed runtime
            // type, so only production sources are indexed.
            private static List<string> RuntimeSourceFiles()
            {
                var root = RuntimeRoot();
                var files = Directory
                    .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                    .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("Tests"))
                    .ToList();
                if (files.Count == 0)
                {
                    throw new InvalidOperationException($"No runtime C# sources found under '{root}'.");
                }
                return files;
            }

            private static string RuntimeRoot()
            {
                var root = Path.GetFullPath(Path.Combine(GeneratorsRoot(), "..", "Runtime"));
                if (!Directory.Exists(root))
                {
                    throw new InvalidOperationException(
                        $"Velvet runtime sources not found at '{root}'. This guard re-derives the hook surface " +
                        "from them, so a moved runtime tree must break it rather than pass vacuously.");
                }
                return root;
            }

            // Walks up from the test host's output directory to the Generators~ root, identified by its .sln
            // file — robust to CI vs local build output layouts without hardcoding a depth.
            private static string GeneratorsRoot()
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Velvet.SourceGenerators.sln")))
                {
                    dir = dir.Parent;
                }
                if (dir == null)
                {
                    throw new InvalidOperationException(
                        "Could not locate Generators~ root (Velvet.SourceGenerators.sln) above " + AppContext.BaseDirectory);
                }
                return dir.FullName;
            }
        }

        private static class Assume
        {
            public static void NotEmpty<T>(IReadOnlyCollection<T> values, string what)
            {
                if (values.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Precondition failed: {what}. The runtime source parse produced nothing to compare " +
                        "against, which would make this guard pass vacuously.");
                }
            }
        }
    }
}
