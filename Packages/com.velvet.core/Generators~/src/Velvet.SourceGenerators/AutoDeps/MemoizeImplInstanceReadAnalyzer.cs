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
    /// <c>[MemoizeMethod]</c> method reads, through <c>this</c>, an instance member
    /// <see cref="ReactiveInstanceMembers"/> counts: the generated wrapper keys on the instance, never on what the
    /// instance holds. A struct is exempt, since its instance is keyed by value.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MemoizeImplInstanceReadAnalyzer : DiagnosticAnalyzer
    {
        private const string ImplSuffix = "_Impl";

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

            // One report per member, at its first read, as VEL100 reports each missing capture once.
            var reported = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var reference in ctx.OperationBlocks.SelectMany(b => b.DescendantsAndSelf()).OfType<IMemberReferenceOperation>())
            {
                if (reference.Instance is not IInstanceReferenceOperation receiver) continue;
                // An object initializer's member is reached through the object being built (ImplicitReceiver).
                if (receiver.ReferenceKind != InstanceReferenceKind.ContainingTypeInstance) continue;
                if (!ReactiveInstanceMembers.Contains(reference.Member)) continue;
                if (!reported.Add(reference.Member)) continue;
                ctx.ReportDiagnostic(Diagnostic.Create(
                    MemoizeDiagnostics.Vel012ImplReadsUnkeyedInstanceMember,
                    reference.Syntax.GetLocation(),
                    impl.Name,
                    reference.Member.Name,
                    wrapper.Name));
            }
        }

        private static IMethodSymbol? MemoizedWrapperOf(IMethodSymbol impl)
        {
            if (impl.ContainingType is not { IsReferenceType: true } type) return null;
            if (!impl.Name.EndsWith(ImplSuffix, StringComparison.Ordinal)) return null;
            var name = impl.Name.Substring(0, impl.Name.Length - ImplSuffix.Length);
            return type.GetMembers(name).OfType<IMethodSymbol>().FirstOrDefault(m => m.GetAttributes().Any(IsMemoizeMethod));
        }

        private static bool IsMemoizeMethod(AttributeData attribute) =>
            attribute.AttributeClass?.ToDisplayString() == VelvetWellKnownNames.MemoizeMethodAttributeFullName;
    }
}
