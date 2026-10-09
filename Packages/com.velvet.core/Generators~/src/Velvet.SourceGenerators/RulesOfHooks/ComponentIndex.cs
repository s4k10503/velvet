using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Velvet.SourceGenerators.Shared;

namespace Velvet.SourceGenerators.RulesOfHooks
{
    /// <summary>
    /// What one compilation declares as components: methods marked <c>[Component]</c>, and methods handed by name
    /// to <c>V.Component</c> or <c>V.Memo</c>, which the runtime mounts as components with or without the
    /// attribute. Built once per compilation on first use, so a method mounted in one file is a component while
    /// another file is analysed.
    /// </summary>
    internal sealed class ComponentIndex
    {
        private readonly Compilation _compilation;
        private readonly Lazy<ImmutableHashSet<IMethodSymbol>> _mounted;

        public ComponentIndex(Compilation compilation)
        {
            _compilation = compilation;
            _mounted = new Lazy<ImmutableHashSet<IMethodSymbol>>(CollectMounted, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public bool IsComponent(IMethodSymbol method) =>
            HasComponentAttribute(method) || _mounted.Value.Contains(method.OriginalDefinition);

        /// <summary>
        /// Whether <paramref name="method"/> is a component this compilation declares with a hook call in its
        /// body, so that calling it directly runs that hook as part of the caller.
        /// </summary>
        public bool IsComponentCallingHooks(IMethodSymbol method) =>
            IsComponent(method)
            && method.OriginalDefinition.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax().DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(call => RulesOfHooksAnalyzer.IsHookLikeName(RulesOfHooksAnalyzer.CalleeName(call))));

        /// <summary>
        /// Whether <paramref name="argument"/> is the render body of a <c>V.Component</c> or <c>V.Memo</c> call:
        /// the parameter it binds to takes a delegate returning <c>Velvet.VNode</c>, which separates the body
        /// from <c>V.Memo</c>'s comparer.
        /// </summary>
        public static bool IsComponentBodyArgument(ArgumentSyntax argument, SemanticModel model, CancellationToken cancellationToken)
        {
            if (argument.Parent is not ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } list) return false;
            if (!IsMountingName(RulesOfHooksAnalyzer.CalleeName(invocation))) return false;
            if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol mount) return false;
            if (mount.ContainingType?.ToDisplayString() != VelvetWellKnownNames.VTypeFullName) return false;

            var parameter = argument.NameColon != null
                ? mount.Parameters.FirstOrDefault(p => p.Name == argument.NameColon.Name.Identifier.ValueText)
                : list.Arguments.IndexOf(argument) is var index && index < mount.Parameters.Length
                    ? mount.Parameters[index]
                    : null;
            return parameter?.Type is INamedTypeSymbol { DelegateInvokeMethod: { } invoke }
                && invoke.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == VelvetWellKnownNames.VNodeFullName;
        }

        private static bool IsMountingName(string? name) =>
            name == VelvetWellKnownNames.VComponentMethodName || name == VelvetWellKnownNames.VMemoMethodName;

        private static bool HasComponentAttribute(IMethodSymbol method) =>
            method.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == VelvetWellKnownNames.ComponentAttributeFullName);

        private ImmutableHashSet<IMethodSymbol> CollectMounted()
        {
            var mounted = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var tree in _compilation.SyntaxTrees)
            {
                SemanticModel? model = null;
                foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (!IsMountingName(RulesOfHooksAnalyzer.CalleeName(invocation))) continue;
                    foreach (var argument in invocation.ArgumentList.Arguments)
                    {
                        if (argument.Expression is AnonymousFunctionExpressionSyntax) continue;
#pragma warning disable RS1030 // The index answers for every tree, so it binds trees other than the one being analysed.
                        model ??= _compilation.GetSemanticModel(tree);
#pragma warning restore RS1030
                        if (!IsComponentBodyArgument(argument, model, CancellationToken.None)) continue;
                        var info = model.GetSymbolInfo(argument.Expression);
                        if ((info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is IMethodSymbol method)
                        {
                            mounted.Add(method.OriginalDefinition);
                        }
                    }
                }
            }
            return mounted.ToImmutable();
        }
    }
}
