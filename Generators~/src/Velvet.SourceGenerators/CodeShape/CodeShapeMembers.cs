using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Velvet.SourceGenerators.CodeShape
{
    /// <summary>
    /// What the code-shape analyzers agree on before any of them measures anything: which declarations are
    /// members, which of their parts carry code or parameters, what to call each part in a report, and
    /// whether the assembly opted in.
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

        /// <summary>
        /// Declarations carrying a parameter list that can exceed a limit. Disjoint from
        /// <see cref="MemberKinds"/> in both directions: a field or accessor takes no parameters, while a
        /// delegate, a local function and a primary constructor declare one without owning a body to measure.
        /// The operator forms are absent because the language caps them at two parameters, so no limit worth
        /// setting could ever reach them.
        /// </summary>
        internal static readonly SyntaxKind[] ParameterizedKinds =
        {
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.IndexerDeclaration,
            SyntaxKind.DelegateDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration,
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
                case BaseMethodDeclarationSyntax method:
                    var (name, display) = ReportedName(method);
                    foreach (var entry in MethodBodies(method, name, display))
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

        /// <summary>
        /// The declaration's parameters, with the token to report them on, or <c>null</c> where the
        /// declaration kind can carry a list but this one does not — a class without a primary constructor.
        /// </summary>
        internal static (SeparatedSyntaxList<ParameterSyntax> Parameters, SyntaxToken Name, string Display)?
            ParametersOf(SyntaxNode node)
        {
            switch (node)
            {
                case MethodDeclarationSyntax method:
                    return (method.ParameterList.Parameters, method.Identifier, method.Identifier.ValueText);
                case ConstructorDeclarationSyntax constructor:
                    return (constructor.ParameterList.Parameters, constructor.Identifier,
                        constructor.Identifier.ValueText);
                case IndexerDeclarationSyntax indexer:
                    return (indexer.ParameterList.Parameters, indexer.ThisKeyword, "this[]");
                case DelegateDeclarationSyntax @delegate:
                    return (@delegate.ParameterList.Parameters, @delegate.Identifier,
                        @delegate.Identifier.ValueText);
                case LocalFunctionStatementSyntax local:
                    return (local.ParameterList.Parameters, local.Identifier, local.Identifier.ValueText);
                case TypeDeclarationSyntax type:
                    // Reached through the child node rather than a property: the pinned Roslyn reference
                    // predates primary constructors on a class or struct and exposes `ParameterList` on the
                    // record node alone, while the host compiler this analyzer loads into parses all three
                    // into the same shape. Raising the reference is not an option — 4.8 breaks the
                    // generators with CS9057.
                    var primary = type.ChildNodes().OfType<ParameterListSyntax>().FirstOrDefault();
                    return primary == null
                        ? null
                        : ((SeparatedSyntaxList<ParameterSyntax>, SyntaxToken, string)?)
                        (primary.Parameters, type.Identifier, type.Identifier.ValueText);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Where a report on a method-like declaration points, and what it calls it — the only thing the five
        /// forms differ in, since all of them carry their body the same way. The two operator forms have no
        /// identifier at all, so they are located and named by the token that does tell them apart.
        /// </summary>
        private static (SyntaxToken Name, string Display) ReportedName(BaseMethodDeclarationSyntax method) =>
            method switch
            {
                MethodDeclarationSyntax m => (m.Identifier, m.Identifier.ValueText),
                ConstructorDeclarationSyntax c => (c.Identifier, c.Identifier.ValueText),
                DestructorDeclarationSyntax d => (d.Identifier, "~" + d.Identifier.ValueText),
                OperatorDeclarationSyntax o => (o.OperatorToken, "operator " + o.OperatorToken.ValueText),
                ConversionOperatorDeclarationSyntax c =>
                    (c.ImplicitOrExplicitKeyword, "operator " + c.Type),
                _ => (method.GetFirstToken(), "?"),
            };

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
