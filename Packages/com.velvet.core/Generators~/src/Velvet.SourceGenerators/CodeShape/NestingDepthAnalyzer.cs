using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Velvet.SourceGenerators.Diagnostics;

namespace Velvet.SourceGenerators.CodeShape
{
    /// <summary>
    /// Reports a member body whose control flow nests deeper than <see cref="MaxDepth"/>, in assemblies that
    /// opt in. <c>Generators~/README.md</c> owns what counts as a level and why the rule is opt-in.
    /// </summary>
    /// <remarks>
    /// <see cref="OptsIntoCodeShapeRules"/> is the whole of the consumer-safety boundary: this diagnostic is
    /// an error, and every assembly referencing Velvet loads the analyzer, so a gate that admits an assembly
    /// it should not have breaks a build whose sources this package cannot edit. Both halves of the marker
    /// carry weight — a gate that matched the key and ignored the value would turn a future
    /// <c>("Velvet.CodeShape", "off")</c> into an opt-in.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal sealed class NestingDepthAnalyzer : DiagnosticAnalyzer
    {
        internal const int MaxDepth = 4;

        internal const string MarkerAttribute = "System.Reflection.AssemblyMetadataAttribute";

        internal const string MarkerKey = "Velvet.CodeShape";

        internal const string MarkerValue = "enforce";

        private static readonly SyntaxKind[] MemberKinds =
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

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(CodeShapeDiagnostics.Vel500NestingDepthExceeded);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            // Gating at compilation start rather than per node means an assembly that has not opted in pays
            // one attribute scan for the whole compile and registers no per-node work at all.
            context.RegisterCompilationStartAction(start =>
            {
                if (!OptsIntoCodeShapeRules(start.Compilation.Assembly)) return;
                start.RegisterSyntaxNodeAction(AnalyzeMember, MemberKinds);
            });
        }

        /// <summary>Height of the nesting tree below <paramref name="root"/>, which is itself not counted.</summary>
        internal static int Measure(SyntaxNode root)
        {
            var max = 0;
            var pending = new Stack<(SyntaxNode Node, int Depth)>();
            foreach (var child in root.ChildNodes())
            {
                pending.Push((child, 0));
            }

            while (pending.Count > 0)
            {
                var (node, depth) = pending.Pop();
                var here = OpensLevel(node) ? depth + 1 : depth;
                if (here > max)
                {
                    max = here;
                }
                foreach (var child in node.ChildNodes())
                {
                    pending.Push((child, here));
                }
            }

            return max;
        }

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

        private static void AnalyzeMember(SyntaxNodeAnalysisContext ctx)
        {
            foreach (var (body, nameToken, displayName) in BodiesOf(ctx.Node))
            {
                var depth = Measure(body);
                if (depth <= MaxDepth) continue;

                ctx.ReportDiagnostic(Diagnostic.Create(
                    CodeShapeDiagnostics.Vel500NestingDepthExceeded,
                    nameToken.GetLocation(),
                    displayName,
                    depth,
                    MaxDepth));
            }
        }

        private static bool OpensLevel(SyntaxNode node) => node switch
        {
            IfStatementSyntax => node.Parent is not ElseClauseSyntax,
            ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax =>
                true,
            SwitchStatementSyntax or TryStatementSyntax or LockStatementSyntax => true,
            // The block form only: `using var x = ...;` is a local declaration with no body and is the
            // flattening form of exactly this construct.
            UsingStatementSyntax => true,
            LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax => true,
            _ => false,
        };

        /// <summary>
        /// Every body the declaration carries, each with the token to report it on. A field declaration
        /// yields one entry per declarator so <c>A = ..., B = ...</c> names and points at the offending one
        /// rather than at whichever came first. A property's accessors are separate declarations reporting
        /// under their own names, so no body is counted twice.
        /// </summary>
        private static IEnumerable<(SyntaxNode Body, SyntaxToken Name, string Display)> BodiesOf(SyntaxNode node)
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
