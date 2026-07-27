using System;
using System.Collections.Generic;
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
    /// Pins the analyzer's hook surface to the runtime's. Which methods are hooks is encoded on both sides of
    /// a compile boundary this solution cannot cross: the ILPP weaver uses <c>nameof</c> and breaks loudly on
    /// a rename, while the analyzer's strings would keep compiling against a name that no longer exists —
    /// exhaustive-deps would simply stop reporting, with nothing red. This fixture re-derives the runtime
    /// surface by parsing the runtime sources with Roslyn (syntax only: no assembly references, no build
    /// ordering, and no dependency on a Unity-produced artifact that CI does not have) and fails when the two
    /// no longer line up — on a missing hook, on a wrongly described one, and on an unpinned constant.
    /// </summary>
    /// <remarks>
    /// Known limits, none of which currently hide anything on this surface:
    /// <list type="bullet">
    /// <item>A dependency list is recognized in exactly two forms — a parameter named <c>deps</c>, or a
    /// trailing array written with the <c>object</c> keyword. A hook declaring one in any other shape (say
    /// <c>IReadOnlyList&lt;object&gt; dependencies</c>, or <c>System.Object[]</c> spelled with the type name)
    /// is not merely uncovered but invisible: it falls into neither half of the partition, so every fact here
    /// passes while the hook has no exhaustive-deps coverage. Widen the two forms rather than assume the
    /// remainder is caught.</item>
    /// <item>Members inside a disabled preprocessor region are invisible. The symbol-free and
    /// <c>UNITY_EDITOR</c> parses are unioned, so an editor-only declaration is covered, but another symbol's
    /// exclusive branch is not.</item>
    /// <item>A parameter whose type is a custom delegate cannot be told from any other named type, so the
    /// non-matching remainder is asserted against a recorded list instead of ignored.</item>
    /// <item>Where a hook declares more than one delegate parameter, the first is taken to be the factory.</item>
    /// <item>The generated <c>V.Memoized&lt;T1..T8&gt;</c> overloads are emitted by the overload generator
    /// rather than checked in under the runtime tree, so they are not parsed here: that descriptor is only
    /// ever validated against the non-generic form. The generator's own snapshot tests cover their shape.</item>
    /// </list>
    /// </remarks>
    public sealed class HookSurfaceDriftTests
    {
        private const string ConventionalDepsParameterName = "deps";
        private const string MethodNameSuffix = "MethodName";
        private const string TypeNameSuffix = "FullName";

        // The two runtime types the analyzer binds hook calls to. Hook-name constants are resolved against
        // the union of their declared methods.
        private static readonly string[] HookHostTypes =
        {
            VelvetWellKnownNames.HooksTypeFullName,
            VelvetWellKnownNames.VTypeFullName,
        };

        /// <summary>
        /// Runtime methods that declare a dependency list yet are deliberately outside exhaustive-deps
        /// coverage, each with the reason it cannot be analyzed. Recorded here so an intentional omission is a
        /// decision in code rather than an absence nobody can distinguish from an oversight.
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

        /// <summary>
        /// Well-known-name constants that name no declaration and so cannot be resolved against the runtime
        /// sources. Recorded so the suffix convention stays enforceable for everything else.
        /// </summary>
        private static readonly Dictionary<string, string> ConstantsNamingNoDeclaration = new()
        {
            [nameof(VelvetWellKnownNames.Namespace)] =
                "names the runtime namespace, which is not a type or method declaration",
        };

        private static RuntimeSourceIndex Runtime => RuntimeSourceIndex.Shared;

        [Fact]
        public void Given_WellKnownMethodNameConstants_When_ResolvedAgainstRuntimeSource_Then_EachNamesADeclaredMethod()
        {
            // Arrange
            var constants = ConstantsWithSuffix(MethodNameSuffix);
            var declared = HookHostTypes
                .SelectMany(type => Runtime.PublicMethodNamesOf(type))
                .ToHashSet(StringComparer.Ordinal);
            Assume.NotEmpty(constants, $"no '{MethodNameSuffix}' constants found on {nameof(VelvetWellKnownNames)}");
            Assume.NotEmpty(declared, "no public methods parsed off the runtime hook host types");

            // Act
            var unresolved = constants
                .Where(c => !declared.Contains(c.Value))
                .Select(Describe)
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
            var constants = ConstantsWithSuffix(TypeNameSuffix);
            var declared = Runtime.DeclaredTypeFullNames;
            Assume.NotEmpty(constants, $"no '{TypeNameSuffix}' constants found on {nameof(VelvetWellKnownNames)}");
            Assume.NotEmpty(declared, "no type declarations parsed off the runtime sources");

            // Act
            var unresolved = constants
                .Where(c => !declared.Contains(StripGlobalPrefix(c.Value)))
                .Select(Describe)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(unresolved.Count == 0,
                $"{nameof(VelvetWellKnownNames)} names types that are not declared under the runtime sources: " +
                $"[{string.Join(", ", unresolved)}]. Attribute and type lookups resolve by metadata name, so a " +
                "renamed or moved type turns the generator into a no-op instead of an error.");
        }

        [Fact]
        public void Given_WellKnownNameConstants_When_NamedWithoutARecognizedSuffix_Then_EachIsRecordedAsNamingNoDeclaration()
        {
            // Arrange
            var constants = AllStringConstants();
            Assume.NotEmpty(constants, $"no string constants found on {nameof(VelvetWellKnownNames)}");

            // Act
            var unpinned = constants
                .Where(c => !c.Name.EndsWith(MethodNameSuffix, StringComparison.Ordinal)
                    && !c.Name.EndsWith(TypeNameSuffix, StringComparison.Ordinal))
                .Where(c => !ConstantsNamingNoDeclaration.ContainsKey(c.Name))
                .Select(Describe)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(unpinned.Count == 0,
                $"These {nameof(VelvetWellKnownNames)} constants end in neither '{MethodNameSuffix}' nor " +
                $"'{TypeNameSuffix}', so nothing resolves them against the runtime: " +
                $"[{string.Join(", ", unpinned)}]. Rename to a pinned suffix, or record it in " +
                $"{nameof(ConstantsNamingNoDeclaration)} alongside " +
                $"[{string.Join("; ", ConstantsNamingNoDeclaration.Select(e => $"{e.Key} — {e.Value}"))}].");
        }

        [Fact]
        public void Given_RuntimeDepsComparingHooks_When_LookedUpInTheDescriptorTable_Then_EachHasADescriptor()
        {
            // Arrange
            var qualifying = QualifyingDepsHooks();
            Assume.NotEmpty(qualifying, "no deps-comparing hooks matched in the runtime sources");

            // Act
            var uncovered = qualifying.Keys
                .Where(hook => !HasDescriptorBoundTo(hook))
                .Select(hook => hook.FullName)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(uncovered.Count == 0,
                "These runtime hooks take a delegate plus a dependency list but have no " +
                $"{nameof(DepsHookDescriptor)} entry, so VEL100 exhaustive-deps does not analyze them: " +
                $"[{string.Join(", ", uncovered)}]. Add a descriptor, or record the reason in " +
                $"{nameof(DepsMethodsOutsideAnalyzerCoverage)}.");
        }

        [Fact]
        public void Given_DepsHookDescriptors_When_ComparedAgainstTheRuntimeSignature_Then_EachDescribesItAccurately()
        {
            // Arrange
            var qualifying = QualifyingDepsHooks();
            Assume.NotEmpty(qualifying, "no deps-comparing hooks matched in the runtime sources");

            // Act
            var wrong = new List<string>();
            foreach (var (hook, overloads) in qualifying)
            {
                if (!HasDescriptorBoundTo(hook)) continue;
                DepsHookDescriptor.TryGet(hook.MethodName, out var descriptor);
                wrong.AddRange(Mismatches(descriptor, overloads).Select(m => $"{hook.FullName}: {m}"));
            }
            wrong.Sort(StringComparer.Ordinal);

            // Assert
            Assert.True(wrong.Count == 0,
                $"These {nameof(DepsHookDescriptor)} entries do not describe the runtime signature they claim " +
                $"to: [{string.Join("; ", wrong)}]. A descriptor that exists but points at the wrong argument " +
                "or bounds the factory too tightly silently drops call sites the analyzer appears to cover.");
        }

        [Fact]
        public void Given_RuntimeDepsTakingMethods_When_NotShapedForDepsComparison_Then_EachIsRecordedAsOutOfCoverage()
        {
            // Arrange
            var qualifying = QualifyingDepsHooks().Keys.Select(h => h.FullName).ToHashSet(StringComparer.Ordinal);

            // Act
            var unaccounted = DepsTakingMethodNames()
                .Where(name => !qualifying.Contains(name) && !DepsMethodsOutsideAnalyzerCoverage.ContainsKey(name))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(unaccounted.Count == 0,
                "These runtime methods declare a dependency list but do not match the delegate-plus-deps shape " +
                $"the analyzer can inspect: [{string.Join(", ", unaccounted)}]. The shape match is syntactic " +
                "and can miss a custom delegate type, so each such method must either qualify or be recorded " +
                $"in {nameof(DepsMethodsOutsideAnalyzerCoverage)} with its reason.");
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
                $"{nameof(DepsMethodsOutsideAnalyzerCoverage)} records methods that no longer declare a " +
                $"dependency list on [{string.Join(", ", HookHostTypes)}]: [{string.Join("; ", stale)}]. " +
                "A stale exclusion hides a real gap the next time that name comes back.");
        }

        /// <summary>
        /// Each way the descriptor disagrees with every deps-taking overload of the hook it describes. The
        /// argument positions must hold for all of them (the analyzer has one descriptor per name), while the
        /// factory bound must admit the widest.
        /// </summary>
        private static IEnumerable<string> Mismatches(
            DepsHookDescriptor descriptor, IReadOnlyList<DepsOverload> overloads)
        {
            var factoryIndexes = overloads.Select(o => o.FactoryArgIndex).Distinct().ToList();
            if (factoryIndexes.Count > 1 || factoryIndexes[0] != descriptor.FactoryArgIndex)
            {
                yield return $"FactoryArgIndex is {descriptor.FactoryArgIndex}, runtime has " +
                    $"[{string.Join(", ", factoryIndexes)}]";
            }

            var depsIndexes = overloads.Select(o => o.DepsArgIndex).Distinct().ToList();
            if (depsIndexes.Count > 1 || depsIndexes[0] != descriptor.DepsArgIndex)
            {
                yield return $"DepsArgIndex is {descriptor.DepsArgIndex}, runtime has " +
                    $"[{string.Join(", ", depsIndexes)}]";
            }

            var paramsForms = overloads.Select(o => o.DepsAreParams).Distinct().ToList();
            if (paramsForms.Count > 1 || paramsForms[0] != descriptor.DepsAreParams)
            {
                yield return $"DepsAreParams is {descriptor.DepsAreParams}, runtime has " +
                    $"[{string.Join(", ", paramsForms)}]";
            }

            var widest = overloads.Max(o => o.FactoryParameterCount);
            if (widest != descriptor.MaxFactoryParameterCount)
            {
                yield return $"MaxFactoryParameterCount is {Arity(descriptor.MaxFactoryParameterCount)}, " +
                    $"runtime's widest factory takes {Arity(widest)}";
            }
        }

        private static string Arity(int value) =>
            value == DepsHookDescriptor.UnboundedFactoryParameterCount ? "unbounded" : value.ToString();

        /// <summary>
        /// Runtime hooks the analyzer is expected to cover — a dependency list (so there is a declared
        /// dependency set) plus a delegate parameter (so there are closure captures to compare against it) —
        /// mapped to every deps-taking overload of that name, minus the recorded exclusions.
        /// </summary>
        private static Dictionary<HookDeclaration, List<DepsOverload>> QualifyingDepsHooks()
        {
            var byHook = new Dictionary<HookDeclaration, List<DepsOverload>>();
            foreach (var overload in DepsOverloads().Where(o => o.FactoryArgIndex >= 0))
            {
                var hook = new HookDeclaration(overload.ContainingTypeFullName, overload.MethodName);
                if (DepsMethodsOutsideAnalyzerCoverage.ContainsKey(hook.FullName)) continue;
                if (!byHook.TryGetValue(hook, out var list))
                {
                    byHook[hook] = list = new List<DepsOverload>();
                }
                list.Add(overload);
            }
            return byHook;
        }

        /// <summary>Every public method on the host types that declares a dependency list, whatever its shape.</summary>
        private static HashSet<string> DepsTakingMethodNames() =>
            DepsOverloads()
                .Select(o => $"{o.ContainingTypeFullName}.{o.MethodName}")
                .ToHashSet(StringComparer.Ordinal);

        private static IEnumerable<DepsOverload> DepsOverloads()
        {
            foreach (var type in HookHostTypes)
            {
                foreach (var declared in Runtime.PublicMethodsOf(type))
                {
                    var overload = TryDescribe(declared);
                    if (overload is not null) yield return overload.Value;
                }
            }
        }

        private static DepsOverload? TryDescribe(MethodDeclaration declared)
        {
            var parameters = declared.Method.ParameterList.Parameters;
            var depsIndex = -1;
            for (var i = 0; i < parameters.Count; i++)
            {
                if (IsDependencyListParameter(parameters[i], isLast: i == parameters.Count - 1)) depsIndex = i;
            }
            if (depsIndex < 0) return null;

            var factoryIndex = -1;
            for (var i = 0; i < parameters.Count && factoryIndex < 0; i++)
            {
                if (IsDelegateTyped(parameters[i], declared.Method)) factoryIndex = i;
            }

            return new DepsOverload(
                declared.ContainingTypeFullName,
                declared.Method.Identifier.ValueText,
                factoryIndex,
                depsIndex,
                parameters[depsIndex].Modifiers.Any(SyntaxKind.ParamsKeyword),
                factoryIndex < 0 ? 0 : FactoryParameterCount(parameters[factoryIndex], declared.Method));
        }

        /// <summary>
        /// A declared dependency list: the conventional <c>deps</c> name, or a trailing <c>object[]</c> under
        /// any other name. Matching on shape rather than on the name alone is what keeps a hook that spells it
        /// <c>dependencies</c> inside the partition instead of invisible to every fact in this fixture.
        /// </summary>
        private static bool IsDependencyListParameter(ParameterSyntax parameter, bool isLast) =>
            parameter.Identifier.ValueText == ConventionalDepsParameterName
            || (isLast && IsObjectArray(parameter.Type));

        private static bool IsObjectArray(TypeSyntax? type) =>
            Unwrap(type) is ArrayTypeSyntax array && IsObjectType(Unwrap(array.ElementType));

        private static bool IsObjectType(TypeSyntax? type) =>
            type is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.ObjectKeyword);

        private static TypeSyntax? Unwrap(TypeSyntax? type) =>
            type is NullableTypeSyntax nullable ? Unwrap(nullable.ElementType) : type;

        /// <summary>
        /// Syntactic delegate-parameter match: a <c>Func</c> / <c>Action</c> parameter, or a type parameter
        /// the method constrains to <c>Delegate</c>.
        /// </summary>
        private static bool IsDelegateTyped(ParameterSyntax parameter, MethodDeclarationSyntax method)
        {
            var typeName = SimpleTypeName(parameter.Type);
            if (typeName is null) return false;
            return typeName is "Func" or "Action" || IsDelegateConstrained(typeName, method);
        }

        /// <summary>
        /// How many arguments the factory lambda receives: <c>Func</c>'s type arguments minus its return,
        /// <c>Action</c>'s type arguments, and unbounded for a <c>Delegate</c>-constrained type parameter,
        /// whose shape the signature does not pin down.
        /// </summary>
        private static int FactoryParameterCount(ParameterSyntax parameter, MethodDeclarationSyntax method)
        {
            var type = Unwrap(parameter.Type);
            var typeName = SimpleTypeName(type);
            if (typeName is not null && IsDelegateConstrained(typeName, method))
            {
                return DepsHookDescriptor.UnboundedFactoryParameterCount;
            }

            var typeArguments = AsGenericName(type)?.TypeArgumentList.Arguments.Count ?? 0;
            return typeName == "Func" ? Math.Max(0, typeArguments - 1) : typeArguments;
        }

        private static GenericNameSyntax? AsGenericName(TypeSyntax? type) =>
            Unwrap(type) switch
            {
                GenericNameSyntax generic => generic,
                QualifiedNameSyntax qualified => AsGenericName(qualified.Right),
                AliasQualifiedNameSyntax alias => AsGenericName(alias.Name),
                _ => null,
            };

        private static bool IsDelegateConstrained(string typeName, MethodDeclarationSyntax method) =>
            method.ConstraintClauses.Any(clause =>
                clause.Name.Identifier.ValueText == typeName
                && clause.Constraints.OfType<TypeConstraintSyntax>()
                    .Any(constraint => SimpleTypeName(constraint.Type) == "Delegate"));

        private static bool HasDescriptorBoundTo(HookDeclaration hook) =>
            DepsHookDescriptor.TryGet(hook.MethodName, out var descriptor)
            && descriptor.ContainingTypeFullName == hook.ContainingTypeFullName;

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

        private static string Describe((string Name, string Value) constant) =>
            $"{constant.Name} = \"{constant.Value}\"";

        // Reflects over the constants instead of restating them, so a constant added to the well-known-names
        // type is guarded without editing this fixture.
        private static List<(string Name, string Value)> AllStringConstants() =>
            typeof(VelvetWellKnownNames)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (f.Name, (string)f.GetRawConstantValue()!))
                .ToList();

        private static List<(string Name, string Value)> ConstantsWithSuffix(string suffix) =>
            AllStringConstants().Where(c => c.Name.EndsWith(suffix, StringComparison.Ordinal)).ToList();

        private readonly record struct HookDeclaration(string ContainingTypeFullName, string MethodName)
        {
            public string FullName => $"{ContainingTypeFullName}.{MethodName}";
        }

        /// <summary>One overload that declares a dependency list, as derived from the runtime signature.</summary>
        private readonly record struct DepsOverload(
            string ContainingTypeFullName,
            string MethodName,
            int FactoryArgIndex,
            int DepsArgIndex,
            bool DepsAreParams,
            int FactoryParameterCount);
    }
}
