using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Velvet.SourceGenerators.Diagnostics;
using Velvet.SourceGenerators.Shared;

namespace Velvet.SourceGenerators.AutoDeps
{
    /// <summary>
    /// Reports <see cref="MemoizeDiagnostics.Vel012ImplReadsUnkeyedInstanceMember"/> where the <c>_Impl</c> of a
    /// <c>[MemoizeMethod]</c> method reads, through <c>this</c> and while it runs, an instance member
    /// <see cref="ReactiveInstanceMembers"/> counts: the generated wrapper keys on the instance, never on what the
    /// instance holds. A struct is exempt, since its instance is keyed by value.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MemoizeImplInstanceReadAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(MemoizeDiagnostics.Vel012ImplReadsUnkeyedInstanceMember);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationBlockAction(AnalyzeImpl);
        }

        private static void AnalyzeImpl(OperationBlockAnalysisContext ctx)
        {
            if (ctx.OwningSymbol is not IMethodSymbol impl) return;
            var wrapper = MemoizedWrapperOf(impl);
            if (wrapper is null) return;

            var operations = ctx.OperationBlocks.SelectMany(b => b.DescendantsAndSelf()).ToList();
            var duringCall = new DuringCall(operations);
            // One report per member, at its first read, as VEL100 reports each missing capture once.
            var reported = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var reference in operations.OfType<IMemberReferenceOperation>())
            {
                if (reference.Instance is not IInstanceReferenceOperation receiver) continue;
                // An object initializer's member is reached through the object being built (ImplicitReceiver).
                if (receiver.ReferenceKind != InstanceReferenceKind.ContainingTypeInstance) continue;
                if (!ReactiveInstanceMembers.Contains(reference.Member)) continue;
                if (!IsRead(reference)) continue;
                if (!duringCall.Runs(reference)) continue;
                if (!reported.Add(reference.Member)) continue;
                ctx.ReportDiagnostic(Diagnostic.Create(
                    MemoizeDiagnostics.Vel012ImplReadsUnkeyedInstanceMember,
                    reference.Syntax.GetLocation(),
                    impl.Name,
                    reference.Member.Name,
                    wrapper.Name));
            }
        }

        // The target of a plain assignment is written, not read, and nameof reads nothing.
        private static bool IsRead(IOperation reference) =>
            (reference.Parent as ISimpleAssignmentOperation)?.Target != reference &&
            !Ancestors(reference).OfType<INameOfOperation>().Any();

        private static IMethodSymbol? MemoizedWrapperOf(IMethodSymbol impl)
        {
            if (impl.ContainingType is not { IsReferenceType: true } type) return null;
            if (!impl.Name.EndsWith(MemoizeImplSignature.Suffix, StringComparison.Ordinal)) return null;
            var name = impl.Name.Substring(0, impl.Name.Length - MemoizeImplSignature.Suffix.Length);
            return type.GetMembers(name).OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.GetAttributes().Any(IsMemoizeMethod) && MemoizeImplSignature.Matches(m, impl));
        }

        private static bool IsMemoizeMethod(AttributeData attribute) =>
            attribute.AttributeClass?.ToDisplayString() == VelvetWellKnownNames.MemoizeMethodAttributeFullName;

        private static IEnumerable<IOperation> Ancestors(IOperation operation)
        {
            for (var current = operation.Parent; current is not null; current = current.Parent)
            {
                yield return current;
            }
        }

        /// <summary>
        /// Whether an operation inside <c>_Impl</c> runs before <c>_Impl</c> returns, so that what it reads can
        /// reach the node the memo caches. Code outside any lambda or local function runs. A local function runs
        /// where it is called from code that runs. A lambda assigned to a local runs where that local is invoked
        /// from code that runs. A lambda passed as an argument is taken to run when it returns a value, as
        /// <c>V.When</c>, <c>V.List</c> and LINQ run theirs, and to run later when it returns nothing, as an event
        /// handler does. Any other lambda, and a local function only converted to a delegate, is taken to run later.
        /// </summary>
        private sealed class DuringCall
        {
            private readonly IReadOnlyList<IOperation> _operations;
            private readonly Dictionary<IOperation, bool> _functions = new();

            public DuringCall(IReadOnlyList<IOperation> operations)
            {
                _operations = operations;
            }

            public bool Runs(IOperation operation)
            {
                var function = Ancestors(operation)
                    .FirstOrDefault(a => a is IAnonymousFunctionOperation || a is ILocalFunctionOperation);
                return function is null || FunctionRuns(function);
            }

            private bool FunctionRuns(IOperation function)
            {
                if (_functions.TryGetValue(function, out var known)) return known;
                // A function reached only through itself is not called from code that runs.
                _functions[function] = false;
                var runs = function is ILocalFunctionOperation local
                    ? Invocations(local.Symbol).Any(Runs)
                    : LambdaRuns((IAnonymousFunctionOperation)function);
                _functions[function] = runs;
                return runs;
            }

            private bool LambdaRuns(IAnonymousFunctionOperation lambda)
            {
                var use = lambda.Parent is IDelegateCreationOperation ? lambda.Parent.Parent : lambda.Parent;
                if (use is IArgumentOperation argument)
                {
                    return !lambda.Symbol.ReturnsVoid && Runs(argument);
                }
                var holder = AssignedLocal(use);
                return holder is not null && DelegateInvocations(holder).Any(Runs);
            }

            private static ILocalSymbol? AssignedLocal(IOperation? use)
            {
                if (use is IVariableInitializerOperation initializer)
                {
                    return (initializer.Parent as IVariableDeclaratorOperation)?.Symbol;
                }
                return ((use as ISimpleAssignmentOperation)?.Target as ILocalReferenceOperation)?.Local;
            }

            private IEnumerable<IInvocationOperation> Invocations(IMethodSymbol localFunction) =>
                _operations.OfType<IInvocationOperation>().Where(invocation =>
                    SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.OriginalDefinition, localFunction));

            private IEnumerable<IInvocationOperation> DelegateInvocations(ILocalSymbol holder) =>
                _operations.OfType<IInvocationOperation>().Where(invocation =>
                    SymbolEqualityComparer.Default.Equals((invocation.Instance as ILocalReferenceOperation)?.Local, holder));
        }
    }
}
