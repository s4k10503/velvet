using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Velvet.SourceGenerators.CodeShape
{
    /// <summary>
    /// What the code-shape analyzers agree on before either measures anything: which declarations are
    /// members, which of their parts carry code, what to call each part in a report, and whether the
    /// assembly opted in.
    /// </summary>
    /// <remarks>
    /// <see cref="OptsIntoCodeShapeRules"/> is the whole of the consumer-safety boundary: these diagnostics
    /// are errors, and every assembly referencing Velvet loads the analyzers, so a gate that admits an
    /// assembly it should not have breaks a build whose sources this package cannot edit. Both halves of the
    /// marker carry weight — a gate that matched the key and ignored the value would turn a future
    /// <c>("Velvet.CodeShape", "off")</c> into an opt-in — and so does the attribute type, or any attribute
    /// taking a name/value pair would opt an assembly in by coincidence.
    /// </remarks>
    internal static class CodeShapeMembers
    {
        internal const string MarkerAttribute = "System.Reflection.AssemblyMetadataAttribute";

        internal const string MarkerKey = "Velvet.CodeShape";

        internal const string MarkerValue = "enforce";

        internal static readonly SyntaxKind[] MemberKinds =
        {
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.DestructorDeclaration,
            SyntaxKind.OperatorDeclaration,
            SyntaxKind.ConversionOperatorDeclaration,
            SyntaxKind.PropertyDeclaration,
            SyntaxKind.IndexerDeclaration,
            SyntaxKind.FieldDeclaration,
            SyntaxKind.EventFieldDeclaration,
            SyntaxKind.GetAccessorDeclaration,
            SyntaxKind.SetAccessorDeclaration,
            SyntaxKind.InitAccessorDeclaration,
            SyntaxKind.AddAccessorDeclaration,
            SyntaxKind.RemoveAccessorDeclaration,
        };

        internal static bool OptsIntoCodeShapeRules(IAssemblySymbol assembly)
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != MarkerAttribute) continue;
                if (attribute.ConstructorArguments.Length != 2) continue;
                if (attribute.ConstructorArguments[0].Value as string != MarkerKey) continue;
                if (attribute.ConstructorArguments[1].Value as string == MarkerValue) return true;
            }

            return false;
        }

        /// <summary>
        /// Every body the declaration carries, each with the token to report it on. A field declaration
        /// yields one entry per declarator so <c>A = ..., B = ...</c> names and points at the offending one
        /// rather than at whichever came first. A property's accessors are separate declarations reporting
        /// under their own names, so no body is counted twice.
        /// </summary>
        internal static IEnumerable<(SyntaxNode Body, SyntaxToken Name, string Display)> BodiesOf(SyntaxNode node)
        {
            switch (node)
            {
                case MethodDeclarationSyntax method:
                    foreach (var entry in MethodBodies(method, method.Identifier, method.Identifier.ValueText))
                        yield return entry;
                    break;
                case ConstructorDeclarationSyntax constructor:
                    foreach (var entry in MethodBodies(constructor, constructor.Identifier,
                                 constructor.Identifier.ValueText))
                        yield return entry;
                    break;
                case DestructorDeclarationSyntax destructor:
                    foreach (var entry in MethodBodies(destructor, destructor.Identifier,
                                 "~" + destructor.Identifier.ValueText))
                        yield return entry;
                    break;
                case OperatorDeclarationSyntax op:
                    foreach (var entry in MethodBodies(op, op.OperatorToken,
                                 "operator " + op.OperatorToken.ValueText))
                        yield return entry;
                    break;
                case ConversionOperatorDeclarationSyntax conversion:
                    foreach (var entry in MethodBodies(conversion, conversion.ImplicitOrExplicitKeyword,
                                 "operator " + conversion.Type))
                        yield return entry;
                    break;
                case AccessorDeclarationSyntax accessor:
                    if (accessor.Body != null)
                        yield return (accessor.Body, accessor.Keyword, AccessorDisplayName(accessor));
                    else if (accessor.ExpressionBody != null)
                        yield return (accessor.ExpressionBody, accessor.Keyword, AccessorDisplayName(accessor));
                    break;
                case PropertyDeclarationSyntax property:
                    if (property.ExpressionBody != null)
                        yield return (property.ExpressionBody, property.Identifier, property.Identifier.ValueText);
                    if (property.Initializer != null)
                        yield return (property.Initializer, property.Identifier, property.Identifier.ValueText);
                    break;
                case IndexerDeclarationSyntax indexer:
                    if (indexer.ExpressionBody != null)
                        yield return (indexer.ExpressionBody, indexer.ThisKeyword, "this[]");
                    break;
                case BaseFieldDeclarationSyntax field:
                    foreach (var declarator in field.Declaration.Variables)
                    {
                        if (declarator.Initializer != null)
                            yield return (declarator.Initializer, declarator.Identifier,
                                declarator.Identifier.ValueText);
                    }
                    break;
            }
        }

        private static IEnumerable<(SyntaxNode Body, SyntaxToken Name, string Display)> MethodBodies(
            BaseMethodDeclarationSyntax method, SyntaxToken name, string display)
        {
            if (method.Body != null) yield return (method.Body, name, display);
            else if (method.ExpressionBody != null) yield return (method.ExpressionBody, name, display);
        }

        private static string AccessorDisplayName(AccessorDeclarationSyntax accessor)
        {
            var owner = accessor.Parent?.Parent switch
            {
                PropertyDeclarationSyntax property => property.Identifier.ValueText,
                IndexerDeclarationSyntax => "this[]",
                EventDeclarationSyntax @event => @event.Identifier.ValueText,
                _ => "?",
            };
            return owner + "." + accessor.Keyword.ValueText;
        }
    }
}
