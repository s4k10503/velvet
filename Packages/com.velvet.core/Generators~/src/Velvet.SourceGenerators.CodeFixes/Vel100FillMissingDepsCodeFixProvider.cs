using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Velvet.SourceGenerators.AutoDeps;
using Velvet.SourceGenerators.Shared;

namespace Velvet.SourceGenerators.CodeFixes
{
    /// <summary>
    /// Quick fix for VEL100: appends the missing closure-captured local to the deps array literal of a
    /// deps-comparing hook (<c>UseEffect</c> / <c>UseLayoutEffect</c> / <c>UseCallback</c> / <c>UseMemo</c> /
    /// <c>UseImperativeHandle</c>, or the V DSL's <c>V.Memoized</c> / <c>V.MemoizedWithKey</c>). Only handles
    /// the simple <c>new[]</c> / <c>new T[] { ... }</c> deps initializer forms that the analyzer flags (loose
    /// <c>params</c> deps are left untouched). Preserves the leading trivia of the previous element so
    /// multi-line deps stay aligned.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Vel100FillMissingDepsCodeFixProvider))]
    [Shared]
    public sealed class Vel100FillMissingDepsCodeFixProvider : CodeFixProvider
    {
        private const string DiagnosticId = "VEL100";
        private const string Title = "Add missing local to hook deps array";

        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId);

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root is null) return;

            foreach (var diagnostic in context.Diagnostics)
            {
                var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
                var capturedIdentifier = node?.AncestorsAndSelf().OfType<IdentifierNameSyntax>().FirstOrDefault();
                if (capturedIdentifier is null) continue;

                var invocation = capturedIdentifier.AncestorsAndSelf().OfType<InvocationExpressionSyntax>()
                    .FirstOrDefault(inv => inv.Expression is MemberAccessExpressionSyntax m
                        && DepsHookDescriptor.TryGet(m.Name.Identifier.ValueText, out _));
                if (invocation is null) continue;
                if (invocation.Expression is not MemberAccessExpressionSyntax member) continue;
                DepsHookDescriptor.TryGet(member.Name.Identifier.ValueText, out var hook);

                var args = invocation.ArgumentList.Arguments;
                // Only the single array-literal deps form is fixable. Loose params deps have no single
                // initializer to append to, so the analyzer's diagnostic is left without a quick fix.
                if (args.Count != hook.DepsArgIndex + 1) continue;
                var depsExpr = args[hook.DepsArgIndex].Expression;
                if (UseEffectDepsSyntax.TryGetInitializer(depsExpr) is null) continue;

                var localName = capturedIdentifier.Identifier.ValueText;
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: Title,
                        createChangedDocument: ct => AppendDepAsync(context.Document, depsExpr, localName, ct),
                        equivalenceKey: $"{DiagnosticId}:{localName}"),
                    diagnostic);
            }
        }

        private static async Task<Document> AppendDepAsync(
            Document document,
            ExpressionSyntax depsExpr,
            string localName,
            CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null) return document;

            var initializer = UseEffectDepsSyntax.TryGetInitializer(depsExpr);
            if (initializer is null) return document;

            var newElement = SyntaxFactory.IdentifierName(localName);
            // Carry over the previous element's leading trivia so multi-line initializers keep their
            // newline + indentation. Single-line `new[] { a, b }` becomes `new[] { a, b, c }` because
            // the last element has no leading trivia to copy.
            if (initializer.Expressions.Count > 0)
            {
                var last = initializer.Expressions[initializer.Expressions.Count - 1];
                newElement = newElement.WithLeadingTrivia(last.GetLeadingTrivia());
            }
            var newInitializer = initializer.AddExpressions(newElement);
            var newRoot = root.ReplaceNode(initializer, newInitializer);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
