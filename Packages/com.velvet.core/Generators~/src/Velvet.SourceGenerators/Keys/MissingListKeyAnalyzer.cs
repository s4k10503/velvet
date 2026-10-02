using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Velvet.SourceGenerators.Diagnostics;
using Velvet.SourceGenerators.Shared;

namespace Velvet.SourceGenerators.Keys
{
    /// <summary>
    /// Reports a <c>V.*</c> factory call that builds an element of a mapped list without a key: the element a
    /// <c>Select</c> selector returns, where <c>.ToArray()</c> hands the mapped array straight to a <c>V.*</c>
    /// factory as its children. React's <c>warnForMissingKey</c> is the counterpart, reporting a child of a
    /// mapped array that carries no key.
    /// </summary>
    /// <remarks>
    /// A C# array carries nothing that marks it as mapped, so the call shape is what is read: an array written
    /// out element by element is JSX's static children, which React does not ask a key of, and is not
    /// reported. A selector that returns something other than a <c>V.*</c> call — a method group, a local, a
    /// helper's result — is left alone, since what that returns is not visible here; so is a mapped array
    /// held in a local before it reaches the factory. A factory with no <c>key</c> parameter, such as
    /// <c>V.Text</c>, cannot be given one and is not reported.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal sealed class MissingListKeyAnalyzer : DiagnosticAnalyzer
    {
        private const string KeyParameterName = "key";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(KeyDiagnostics.Vel600MissingListKey);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeSelect, OperationKind.Invocation);
        }

        private static void AnalyzeSelect(OperationAnalysisContext ctx)
        {
            var select = (IInvocationOperation)ctx.Operation;
            if (!IsEnumerableMethod(select.TargetMethod, nameof(Enumerable.Select))) return;
            if (!ReachesFactoryChildren(select)) return;
            if (select.Arguments.Length != 2) return;
            if (Unwrap(select.Arguments[1].Value) is not IDelegateCreationOperation
                { Target: IAnonymousFunctionOperation selector }) return;

            foreach (var element in ReturnedElements(selector))
            {
                if (!IsUnkeyedFactoryCall(element, out var factory)) continue;
                ctx.ReportDiagnostic(Diagnostic.Create(
                    KeyDiagnostics.Vel600MissingListKey, element.Syntax.GetLocation(), factory));
            }
        }

        private static bool IsEnumerableMethod(IMethodSymbol method, string name) =>
            method.Name == name && method.ContainingType?.ToDisplayString() == "System.Linq.Enumerable";

        private static bool IsVFactory(IMethodSymbol method) =>
            method.ContainingType?.ToDisplayString() == VelvetWellKnownNames.VTypeFullName;

        // Select(...).ToArray() handed to a V.* factory as an argument, conversions aside.
        private static bool ReachesFactoryChildren(IInvocationOperation select)
        {
            if (select.Parent is not IArgumentOperation { Parent: IInvocationOperation toArray }) return false;
            if (!IsEnumerableMethod(toArray.TargetMethod, nameof(Enumerable.ToArray))) return false;
            var parent = toArray.Parent;
            while (parent is IConversionOperation) parent = parent.Parent;
            return parent is IArgumentOperation { Parent: IInvocationOperation factory } && IsVFactory(factory.TargetMethod);
        }

        // The values the selector's own returns hand back, read through a conditional's arms. A return inside
        // a nested lambda belongs to that lambda.
        private static IEnumerable<IOperation> ReturnedElements(IAnonymousFunctionOperation selector)
        {
            var returns = new List<IOperation>();
            CollectReturns(selector.Body, returns);
            return returns.SelectMany(Arms);
        }

        private static void CollectReturns(IOperation operation, List<IOperation> returns)
        {
            foreach (var child in operation.ChildOperations)
            {
                if (child is IAnonymousFunctionOperation or ILocalFunctionOperation) continue;
                if (child is IReturnOperation { ReturnedValue: { } value }) returns.Add(value);
                else CollectReturns(child, returns);
            }
        }

        private static IEnumerable<IOperation> Arms(IOperation value) => Unwrap(value) switch
        {
            IConditionalOperation conditional when conditional.WhenFalse != null =>
                Arms(conditional.WhenTrue).Concat(Arms(conditional.WhenFalse)),
            var other => new[] { other },
        };

        private static IOperation Unwrap(IOperation operation)
        {
            while (operation is IConversionOperation conversion) operation = conversion.Operand;
            return operation;
        }

        // A key given as the null literal is no key, as React reads a null key.
        private static bool IsUnkeyedFactoryCall(IOperation element, out string factory)
        {
            factory = string.Empty;
            if (element is not IInvocationOperation call || !IsVFactory(call.TargetMethod)) return false;
            var key = call.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == KeyParameterName);
            if (key == null) return false;
            factory = "V." + call.TargetMethod.Name;
            return key.ArgumentKind == ArgumentKind.DefaultValue
                || Unwrap(key.Value) is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } };
        }
    }
}
