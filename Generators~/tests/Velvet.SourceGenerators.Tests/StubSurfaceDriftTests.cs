using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins <see cref="GeneratorTestHelper.VelvetStubSource"/> to the runtime it stands in for. Analyzer and
    /// generator tests compile their sample user code against that stub, so a stubbed signature that disagrees
    /// with the real one makes the whole suite green against a surface nobody can call: a diagnostic gets
    /// "verified" for an argument list, a return shape or a constraint set no user could ever write. This
    /// fixture re-derives the real signatures by parsing the runtime sources with Roslyn — syntax only, the
    /// same parse the hook-name guard uses, since this solution cannot reference the Unity assemblies — and
    /// fails on any divergence, naming the member.
    /// </summary>
    /// <remarks>
    /// The stub is a subset, not a variant: leaving a runtime type or member out is allowed and unchecked,
    /// because the stub only has to model what the analyzers look at. What is checked is everything it does
    /// declare — its modifiers, parameter types and names, return type, generic parameters, constraints,
    /// optionality and <c>params</c> form — plus, for any method or constructor name it declares, that it
    /// models every runtime overload of that name. That last rule is what stops a fixture from picking a call
    /// shape that only resolves because the narrower overloads are missing.
    /// <para>
    /// Comparison limits, each chosen because the stub cannot model the thing at all:
    /// </para>
    /// <list type="bullet">
    /// <item>Types are compared by simple name: the parse is syntactic, so <c>Velvet.Ref</c> and any other
    /// <c>Ref</c> in scope are indistinguishable. Nothing on this surface collides today.</item>
    /// <item>Nullable reference annotations are erased on both sides. The stub compiles in a
    /// nullable-oblivious compilation, so it cannot carry them, and no analyzer reads them.</item>
    /// <item>Base types and implemented interfaces are not compared. The runtime's are Unity- or
    /// runtime-internal types (<c>IHookRefSetter</c>, <c>VisualElement</c>) that cannot cross this
    /// boundary.</item>
    /// <item>Attributes, accessor bodies and member bodies are not compared — the stub is deliberately inert.</item>
    /// <item>Operators, indexers and nested types are not extracted. The stub declares none, so a lookup for
    /// one would find nothing on either side rather than mismatch.</item>
    /// </list>
    /// </remarks>
    public sealed class StubSurfaceDriftTests
    {
        /// <summary>
        /// Divergences the stub keeps on purpose, each with the reason it cannot or need not be modelled
        /// faithfully. Recorded so an intentional simplification is a decision in code rather than an absence
        /// nobody can distinguish from an oversight. A type key covers that type's own declaration; a
        /// <c>Type.Member</c> key covers every overload of that member name.
        /// </summary>
        private static readonly Dictionary<string, string> StubSimplifications = new()
        {
            ["Cysharp.Threading.Tasks.UniTask"] =
                "UniTask lives in an external package the netstandard test project has no reference to, and " +
                "UseBlocker's async overload is part of the deps-comparison surface, so a placeholder of the " +
                "same name and arity stands in — that keeps the overload's true parameter types, and therefore " +
                "its predicate arity, exercised end to end",
            ["Velvet.VNode"] =
                "the runtime's VNode is abstract and every concrete node carries required members; the stub " +
                "makes it instantiable so a fixture can hand a VNode-shaped value to a memoized factory " +
                "without modelling the node hierarchy. No analyzer inspects the type's abstractness",
        };

        private static readonly Lazy<StubComparison> Comparison = new(Compare);

        [Fact]
        public void Given_StubTypes_When_ResolvedAgainstRuntimeSource_Then_EachNamesADeclaredType()
        {
            // Arrange
            var comparison = Comparison.Value;
            Assume.NotEmpty(comparison.StubTypeNames, "no type declarations parsed off the Velvet stub");
            Assume.NotEmpty(RuntimeSourceIndex.Shared.DeclaredTypeFullNames, "no types parsed off the runtime sources");

            // Act
            var unresolved = Reportable(comparison, DivergenceKind.UnknownType);

            // Assert
            Assert.True(unresolved.Count == 0,
                "The Velvet stub declares types the runtime sources do not: " +
                $"[{string.Join("; ", unresolved)}]. A fixture compiling against one of them is exercising a " +
                $"type that does not exist. Correct the stub, or record it in {nameof(StubSimplifications)} " +
                "with the reason it cannot be modelled.");
        }

        [Fact]
        public void Given_StubDeclarations_When_ComparedAgainstRuntimeSource_Then_EachMatchesTheRuntimeSignature()
        {
            // Arrange
            var comparison = Comparison.Value;
            Assume.NotEmpty(comparison.StubMemberNames, "no public members parsed off the Velvet stub");
            Assume.NotEmpty(comparison.RuntimeMemberNames, "no public members parsed off the stubbed runtime types");

            // Act
            var mismatched = Reportable(comparison, DivergenceKind.Signature);

            // Assert
            Assert.True(mismatched.Count == 0,
                "These Velvet stub declarations do not match the runtime declaration they stand in for: " +
                $"[{string.Join("; ", mismatched)}]. Every analyzer and generator test compiles its sample " +
                "user code against the stub, so a diagnostic verified against a divergent signature is " +
                $"verified for a call shape no user can write. Correct the stub, or record it in " +
                $"{nameof(StubSimplifications)} with the reason.");
        }

        [Fact]
        public void Given_RuntimeOverloads_When_TheStubDeclaresTheirName_Then_EachIsModelled()
        {
            // Arrange
            var comparison = Comparison.Value;
            Assume.NotEmpty(comparison.OverloadableRuntimeMemberNames,
                "no overloadable runtime members matched a name the stub declares");

            // Act
            var unmodelled = Reportable(comparison, DivergenceKind.MissingOverload);

            // Assert
            Assert.True(unmodelled.Count == 0,
                "The runtime declares these overloads of a member the Velvet stub already models by name, but " +
                $"the stub does not model them: [{string.Join("; ", unmodelled)}]. With a narrower overload " +
                "missing, overload resolution in a fixture's sample source lands on a candidate that would " +
                "lose — or be ambiguous — against the real surface. Add it, or record it in " +
                $"{nameof(StubSimplifications)} with the reason.");
        }

        [Fact]
        public void Given_RecordedStubSimplifications_When_ComparedAgainstRuntimeSource_Then_EachStillDiverges()
        {
            // Arrange
            var comparison = Comparison.Value;
            Assume.NotEmpty(comparison.StubTypeNames, "no type declarations parsed off the Velvet stub");

            // Act
            var settled = StubSimplifications
                .Where(entry => comparison.Divergences.All(d => d.Key != entry.Key))
                .Select(entry => $"{entry.Key} (recorded as: {entry.Value})")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.True(settled.Count == 0,
                $"{nameof(StubSimplifications)} records divergences the stub no longer has: " +
                $"[{string.Join("; ", settled)}]. A record that no longer applies suppresses the next real " +
                "divergence to appear under the same name.");
        }

        private static List<string> Reportable(StubComparison comparison, DivergenceKind kind) =>
            comparison.Divergences
                .Where(d => d.Kind == kind && !StubSimplifications.ContainsKey(d.Key))
                .Select(d => d.Description)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// Every way the stub and the runtime disagree, before the recorded simplifications are subtracted, so
        /// the staleness fact can tell a record that still applies from one that has been fixed.
        /// </summary>
        private static StubComparison Compare()
        {
            var runtime = RuntimeSourceIndex.Shared;
            var divergences = new List<Divergence>();
            var stubTypeNames = new List<string>();
            var stubMemberNames = new List<string>();
            var runtimeMemberNames = new List<string>();
            var overloadableRuntimeMemberNames = new List<string>();

            foreach (var (fullName, stubDeclaration) in StubTypeDeclarations())
            {
                stubTypeNames.Add(fullName);

                var runtimeDeclarations = runtime.TypeDeclarationsOf(fullName).ToList();
                if (runtimeDeclarations.Count == 0)
                {
                    divergences.Add(new Divergence(fullName, DivergenceKind.UnknownType,
                        $"{fullName} is declared by the stub but by no runtime source file"));
                    continue;
                }

                var runtimeTypeForms = runtimeDeclarations.Select(CanonicalType).Distinct(StringComparer.Ordinal).ToList();
                var stubTypeForm = CanonicalType(stubDeclaration);
                if (!runtimeTypeForms.Contains(stubTypeForm, StringComparer.Ordinal))
                {
                    divergences.Add(new Divergence(fullName, DivergenceKind.Signature,
                        $"{fullName}: stub declares '{stubTypeForm}', runtime declares " +
                        $"[{string.Join(" | ", runtimeTypeForms)}]"));
                }

                var runtimeMembers = runtime.PublicMembersOf(fullName).SelectMany(Signatures).Distinct().ToList();
                var stubMembers = PublicMembersOf(stubDeclaration).SelectMany(Signatures).ToList();
                runtimeMemberNames.AddRange(runtimeMembers.Select(m => $"{fullName}.{m.Name}"));
                stubMemberNames.AddRange(stubMembers.Select(m => $"{fullName}.{m.Name}"));

                foreach (var stubMember in stubMembers)
                {
                    var key = $"{fullName}.{stubMember.Name}";
                    var candidates = runtimeMembers.Where(m => m.Name == stubMember.Name).ToList();
                    if (candidates.Count == 0)
                    {
                        divergences.Add(new Divergence(key, DivergenceKind.Signature,
                            $"{key}: stub declares '{stubMember.Form}', the runtime declares no public member " +
                            "of that name"));
                    }
                    else if (!candidates.Any(c => c.Form == stubMember.Form))
                    {
                        divergences.Add(new Divergence(key, DivergenceKind.Signature,
                            $"{key}: stub declares '{stubMember.Form}', runtime declares " +
                            $"[{string.Join(" | ", candidates.Select(c => c.Form))}]"));
                    }
                }

                foreach (var name in stubMembers.Where(m => m.IsOverloadable).Select(m => m.Name).Distinct())
                {
                    var stubForms = stubMembers.Where(m => m.Name == name).Select(m => m.Form).ToHashSet(StringComparer.Ordinal);
                    var runtimeOverloads = runtimeMembers.Where(m => m.IsOverloadable && m.Name == name).ToList();
                    overloadableRuntimeMemberNames.AddRange(runtimeOverloads.Select(_ => $"{fullName}.{name}"));
                    foreach (var missing in runtimeOverloads.Where(m => !stubForms.Contains(m.Form)))
                    {
                        divergences.Add(new Divergence($"{fullName}.{name}", DivergenceKind.MissingOverload,
                            $"{fullName}.{name}: the runtime also declares '{missing.Form}'"));
                    }
                }
            }

            return new StubComparison(
                divergences, stubTypeNames, stubMemberNames, runtimeMemberNames, overloadableRuntimeMemberNames);
        }

        /// <summary>
        /// The stub's type declarations, keyed the way <see cref="RuntimeSourceIndex"/> keys the runtime's. A
        /// stub that no longer parses would otherwise present as a surface with nothing to compare.
        /// </summary>
        private static List<(string FullName, TypeDeclarationSyntax Declaration)> StubTypeDeclarations()
        {
            var tree = CSharpSyntaxTree.ParseText(
                GeneratorTestHelper.VelvetStubSource,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));

            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(GeneratorTestHelper.VelvetStubSource)} does not parse: " +
                    string.Join("; ", errors.Select(e => e.ToString())));
            }

            return tree.GetRoot()
                .DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Select(declaration => (
                    RuntimeSourceIndex.QualifiedName(declaration, declaration.Identifier.ValueText, nestingSeparator: "."),
                    declaration))
                .ToList();
        }

        private static IEnumerable<MemberDeclarationSyntax> PublicMembersOf(TypeDeclarationSyntax declaration) =>
            declaration.Members.Where(member => member.Modifiers.Any(SyntaxKind.PublicKeyword));

        private static string CanonicalType(TypeDeclarationSyntax declaration) =>
            $"{Modifiers(declaration.Modifiers)}{declaration.Keyword.ValueText} {declaration.Identifier.ValueText}" +
            $"{TypeParameters(declaration.TypeParameterList)}{Constraints(declaration.ConstraintClauses)}";

        private static IEnumerable<MemberSignature> Signatures(MemberDeclarationSyntax member)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    yield return new MemberSignature(
                        method.Identifier.ValueText,
                        IsOverloadable: true,
                        $"{Modifiers(method.Modifiers)}{TypeName(method.ReturnType)} " +
                        $"{method.Identifier.ValueText}{TypeParameters(method.TypeParameterList)}" +
                        $"({Parameters(method.ParameterList)}){Constraints(method.ConstraintClauses)}");
                    break;
                case ConstructorDeclarationSyntax constructor:
                    yield return new MemberSignature(
                        constructor.Identifier.ValueText,
                        IsOverloadable: true,
                        $"{Modifiers(constructor.Modifiers)}{constructor.Identifier.ValueText}" +
                        $"({Parameters(constructor.ParameterList)})");
                    break;
                case PropertyDeclarationSyntax property:
                    yield return new MemberSignature(
                        property.Identifier.ValueText,
                        IsOverloadable: false,
                        $"{Modifiers(property.Modifiers)}{TypeName(property.Type)} " +
                        $"{property.Identifier.ValueText} {{ {Accessors(property)} }}");
                    break;
                case FieldDeclarationSyntax field:
                    foreach (var variable in field.Declaration.Variables)
                    {
                        yield return new MemberSignature(
                            variable.Identifier.ValueText,
                            IsOverloadable: false,
                            $"{Modifiers(field.Modifiers)}{TypeName(field.Declaration.Type)} " +
                            $"{variable.Identifier.ValueText}");
                    }
                    break;
                case EventFieldDeclarationSyntax @event:
                    foreach (var variable in @event.Declaration.Variables)
                    {
                        yield return new MemberSignature(
                            variable.Identifier.ValueText,
                            IsOverloadable: false,
                            $"{Modifiers(@event.Modifiers)}event {TypeName(@event.Declaration.Type)} " +
                            $"{variable.Identifier.ValueText}");
                    }
                    break;
            }
        }

        // `partial` splits a declaration across files rather than describing its surface, so it is dropped;
        // every other modifier (static / sealed / abstract / readonly / required / accessibility) is compared.
        private static string Modifiers(SyntaxTokenList modifiers)
        {
            var kept = modifiers
                .Where(modifier => !modifier.IsKind(SyntaxKind.PartialKeyword))
                .Select(modifier => modifier.ValueText)
                .ToList();
            return kept.Count == 0 ? string.Empty : string.Join(" ", kept) + " ";
        }

        // An expression-bodied property is a getter; the stub declares none, but reading one as no accessors
        // at all would silently equal a stub property that declares none either.
        private static string Accessors(PropertyDeclarationSyntax property) =>
            property.AccessorList is null
                ? "get;"
                : string.Concat(property.AccessorList.Accessors
                    .Select(accessor => $"{Modifiers(accessor.Modifiers)}{accessor.Keyword.ValueText};"));

        private static string TypeParameters(TypeParameterListSyntax? list) =>
            list is null
                ? string.Empty
                : "<" + string.Join(", ", list.Parameters.Select(p =>
                    (p.VarianceKeyword.IsKind(SyntaxKind.None) ? string.Empty : p.VarianceKeyword.ValueText + " ")
                    + p.Identifier.ValueText)) + ">";

        private static string Constraints(SyntaxList<TypeParameterConstraintClauseSyntax> clauses) =>
            clauses.Count == 0
                ? string.Empty
                : " " + string.Join(" ", clauses.Select(clause =>
                    $"where {clause.Name.Identifier.ValueText} : " +
                    string.Join(", ", clause.Constraints.Select(ConstraintText))));

        private static string ConstraintText(TypeParameterConstraintSyntax constraint) =>
            constraint switch
            {
                TypeConstraintSyntax type => TypeName(type.Type),
                ClassOrStructConstraintSyntax classOrStruct => classOrStruct.ClassOrStructKeyword.ValueText,
                ConstructorConstraintSyntax => "new()",
                DefaultConstraintSyntax => "default",
                _ => Collapse(constraint.ToString()),
            };

        private static string Parameters(ParameterListSyntax list) =>
            string.Join(", ", list.Parameters.Select(parameter =>
                $"{Modifiers(parameter.Modifiers)}{TypeName(parameter.Type)} {parameter.Identifier.ValueText}"
                + (parameter.Default is null ? string.Empty : $" = {Collapse(parameter.Default.Value.ToString())}")));

        /// <summary>
        /// A type reduced to what both sides can express: namespace qualification dropped (the stub writes
        /// <c>global::</c>-rooted names where the runtime writes imported ones) and nullable reference
        /// annotations erased (the stub's compilation is nullable-oblivious).
        /// </summary>
        private static string TypeName(TypeSyntax? type) =>
            type switch
            {
                null => "<none>",
                NullableTypeSyntax nullable => TypeName(nullable.ElementType),
                ArrayTypeSyntax array => TypeName(array.ElementType) + string.Concat(
                    array.RankSpecifiers.Select(rank => "[" + new string(',', rank.Rank - 1) + "]")),
                PredefinedTypeSyntax predefined => predefined.Keyword.ValueText,
                QualifiedNameSyntax qualified => TypeName(qualified.Right),
                AliasQualifiedNameSyntax alias => TypeName(alias.Name),
                GenericNameSyntax generic => generic.Identifier.ValueText + "<"
                    + string.Join(", ", generic.TypeArgumentList.Arguments.Select(TypeName)) + ">",
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                TupleTypeSyntax tuple => "(" + string.Join(", ", tuple.Elements.Select(element =>
                    TypeName(element.Type)
                    + (element.Identifier.IsKind(SyntaxKind.None) ? string.Empty : " " + element.Identifier.ValueText)))
                    + ")",
                RefTypeSyntax reference => "ref " + TypeName(reference.Type),
                _ => Collapse(type.ToString()),
            };

        private static string Collapse(string text) =>
            string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        private enum DivergenceKind
        {
            UnknownType,
            Signature,
            MissingOverload,
        }

        /// <summary><paramref name="Key"/> is what <see cref="StubSimplifications"/> records against.</summary>
        private readonly record struct Divergence(string Key, DivergenceKind Kind, string Description);

        /// <summary>One declared member reduced to the form both sides are compared in.</summary>
        private readonly record struct MemberSignature(string Name, bool IsOverloadable, string Form);

        /// <summary>
        /// The comparison plus the populations it ran over. Every fact here asserts that a filtered list is
        /// empty, which an empty population satisfies for the wrong reason, so the counts are carried out of
        /// the single pass rather than recomputed per fact.
        /// </summary>
        private sealed record StubComparison(
            IReadOnlyList<Divergence> Divergences,
            IReadOnlyList<string> StubTypeNames,
            IReadOnlyList<string> StubMemberNames,
            IReadOnlyList<string> RuntimeMemberNames,
            IReadOnlyList<string> OverloadableRuntimeMemberNames);
    }
}
