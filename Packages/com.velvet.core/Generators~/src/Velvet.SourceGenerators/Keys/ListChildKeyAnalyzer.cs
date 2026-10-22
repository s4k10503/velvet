using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Velvet.SourceGenerators.Diagnostics;
using Velvet.SourceGenerators.Shared;

namespace Velvet.SourceGenerators.Keys
{
    /// <summary>
    /// Reports <see cref="ReconcilerDiagnostics.Vel600ListChildWithoutKey"/>: React's missing-key warning for a
    /// list built with <c>Select</c>, given at compile time because the runtime receives that list as the same
    /// array type as a children array written out at the call site.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ListChildKeyAnalyzer : DiagnosticAnalyzer
    {
        private const string KeyParameterName = "key";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(ReconcilerDiagnostics.Vel600ListChildWithoutKey);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(OperationAnalysisContext ctx)
        {
            var invocation = (IInvocationOperation)ctx.Operation;
            if (!IsLinqSelect(invocation.TargetMethod)) return;
            var selector = LambdaArgumentOf(invocation);
            if (selector is null) return;
            foreach (var returned in ReturnedValues(selector.Body))
            {
                foreach (var factory in FactoryCalls(returned))
                {
                    if (!BuildsKeyableNode(factory.TargetMethod) || HasKey(factory)) continue;
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        ReconcilerDiagnostics.Vel600ListChildWithoutKey,
                        factory.Syntax.GetLocation(),
                        factory.TargetMethod.Name));
                }
            }
        }

        private static bool IsLinqSelect(IMethodSymbol method) =>
            method.Name == nameof(System.Linq.Enumerable.Select)
            && method.ContainingType.ToDisplayString() == typeof(System.Linq.Enumerable).FullName;

        private static IAnonymousFunctionOperation? LambdaArgumentOf(IInvocationOperation invocation)
        {
            foreach (var argument in invocation.Arguments)
            {
                if (argument.Value is IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda })
                    return lambda;
            }
            return null;
        }

        // A nested lambda is read only as V.When's factory (NodesBuiltBy): what any other returns goes to whatever
        // runs it, which is not followed.
        private static IEnumerable<IOperation> ReturnedValues(IOperation operation)
        {
            foreach (var child in operation.ChildOperations)
            {
                if (child is IAnonymousFunctionOperation or ILocalFunctionOperation) continue;
                if (child is IReturnOperation { ReturnedValue: { } value }) yield return value;
                foreach (var nested in ReturnedValues(child)) yield return nested;
            }
        }

        private static IEnumerable<IInvocationOperation> FactoryCalls(IOperation? value)
        {
            switch (value)
            {
                case IConversionOperation conversion:
                    foreach (var call in FactoryCalls(conversion.Operand)) yield return call;
                    break;
                case IConditionalOperation conditional:
                    foreach (var call in FactoryCalls(conditional.WhenTrue)) yield return call;
                    foreach (var call in FactoryCalls(conditional.WhenFalse)) yield return call;
                    break;
                case IInvocationOperation call
                    when call.TargetMethod.ContainingType.ToDisplayString() == VelvetWellKnownNames.VTypeFullName:
                    foreach (var built in NodesBuiltBy(call)) yield return built;
                    break;
            }
        }

        // V.When is declared to return VNode itself: the node it hands back is the one its factory returns.
        private static IEnumerable<IInvocationOperation> NodesBuiltBy(IInvocationOperation call)
        {
            if (call.TargetMethod.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                != VelvetWellKnownNames.VNodeFullName)
            {
                yield return call;
                yield break;
            }
            var factory = LambdaArgumentOf(call);
            if (factory is null) yield break;
            foreach (var returned in ReturnedValues(factory.Body))
            {
                foreach (var built in FactoryCalls(returned)) yield return built;
            }
        }

        private static bool BuildsKeyableNode(IMethodSymbol factory) =>
            factory.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                != VelvetWellKnownNames.TextNodeFullName
            && IsVNode(factory.ReturnType);

        private static bool IsVNode(ITypeSymbol type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == VelvetWellKnownNames.VNodeFullName)
                    return true;
            }
            return false;
        }

        // An omitted key reaches the call as its default, a constant null, as a written null does.
        private static bool HasKey(IInvocationOperation factory)
        {
            foreach (var argument in factory.Arguments)
            {
                if (argument.Parameter?.Name != KeyParameterName) continue;
                var constant = argument.Value.ConstantValue;
                return !constant.HasValue || constant.Value is not null;
            }
            return false;
        }
    }
}
