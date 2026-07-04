using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Velvet.SourceGenerators.CodeFixes
{
    /// <summary>
    /// Shared logic for attribute-removal CodeFixes.
    /// Locates the <see cref="AttributeSyntax"/> at the diagnostic position and returns a new Document with it removed from the list.
    /// </summary>
    internal static class RemoveAttributeHelper
    {
        public static async Task RegisterRemoveAttributeFixAsync(
            CodeFixContext context,
            string title)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                return;
            }

            foreach (var diagnostic in context.Diagnostics)
            {
                var attribute = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                    ?.AncestorsAndSelf()
                    .OfType<AttributeSyntax>()
                    .FirstOrDefault();
                if (attribute is null)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: title,
                        createChangedDocument: ct => RemoveAttributeAsync(context.Document, attribute, ct),
                        equivalenceKey: title),
                    diagnostic);
            }
        }

        private static async Task<Document> RemoveAttributeAsync(
            Document document,
            AttributeSyntax attribute,
            CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                return document;
            }

            var list = attribute.Parent as AttributeListSyntax;
            if (list is null)
            {
                return document;
            }

            SyntaxNode? newRoot;
            if (list.Attributes.Count == 1)
            {
                newRoot = root.RemoveNode(list, SyntaxRemoveOptions.KeepExteriorTrivia);
            }
            else
            {
                var newAttributes = SyntaxFactory.SeparatedList(list.Attributes.Where(a => a != attribute));
                newRoot = root.ReplaceNode(list, list.WithAttributes(newAttributes));
            }

            return newRoot is null ? document : document.WithSyntaxRoot(newRoot);
        }
    }
}
