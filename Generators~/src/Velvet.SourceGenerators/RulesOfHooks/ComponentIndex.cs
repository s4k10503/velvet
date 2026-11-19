using System;
using System.Collections.Concurrent;
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
    /// to <c>V.Component</c> or <c>V.Memo</c> as the render body, which the runtime mounts as components with or
    /// without the attribute. Read syntactically, as eslint-plugin-react-hooks reads a file, in one pass over the
    /// compilation's trees the first time a question needs it, so a method mounted in one file is a component
    /// while another file is analysed.
    /// </summary>
    internal sealed class ComponentIndex
    {
        private readonly Compilation _compilation;
        private readonly object _gate = new();
        private readonly ConcurrentDictionary<IMethodSymbol, bool> _callsHooks = new(SymbolEqualityComparer.Default);
        private Index? _index;

        public ComponentIndex(Compilation compilation) => _compilation = compilation;

        /// <summary>
        /// Whether <paramref name="call"/> could name a component this compilation declares or mounts: a bare name
        /// looked up in a type enclosing the call, or a qualified one in the type its qualifier names. A syntactic
        /// answer, so a call it rules out costs no binding.
        /// </summary>
        public bool MayCallComponent(InvocationExpressionSyntax call, CancellationToken cancellationToken)
        {
            var keys = Read(cancellationToken).ComponentKeys;
            return call.Expression switch
            {
                MemberAccessExpressionSyntax member =>
                    keys.Contains((LastSegment(member.Expression) ?? string.Empty, member.Name.Identifier.ValueText)),
                SimpleNameSyntax name => call.Ancestors().OfType<BaseTypeDeclarationSyntax>()
                    .Any(type => keys.Contains((type.Identifier.ValueText, name.Identifier.ValueText))),
                _ => false,
            };
        }

        public bool IsComponent(IMethodSymbol method, CancellationToken cancellationToken) =>
            HasComponentAttribute(method)
            || Read(cancellationToken).Mounted.Contains((method.ContainingType?.Name ?? string.Empty, method.Name));

        /// <summary>
        /// Whether <paramref name="method"/> is a component this compilation declares with a hook call in its
        /// declaration, so that calling it directly runs that hook as part of the caller.
        /// </summary>
        public bool IsComponentCallingHooks(IMethodSymbol method, CancellationToken cancellationToken) =>
            IsComponent(method, cancellationToken)
            && _callsHooks.GetOrAdd(method.OriginalDefinition, definition =>
                definition.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .Any(call => RulesOfHooksAnalyzer.HookName(call, null, cancellationToken) != null)));

        /// <summary>
        /// Whether <paramref name="argument"/> is the render body of a <c>V.Component</c> or <c>V.Memo</c> call: its
        /// first argument, or the one named <c>body</c>. The comparer <c>V.Memo</c> takes is neither.
        /// </summary>
        public static bool IsComponentBodyArgument(ArgumentSyntax argument)
        {
            if (argument.Parent is not ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } list) return false;
            if (!IsMountCall(invocation)) return false;
            return argument.NameColon != null
                ? argument.NameColon.Name.Identifier.ValueText == BodyParameterName
                : list.Arguments.IndexOf(argument) == 0;
        }

        private const string BodyParameterName = "body";

        // `V.Component(...)` or `Velvet.V.Component(...)`, or `Component(...)` under `using static Velvet.V`.
        private static bool IsMountCall(InvocationExpressionSyntax invocation) =>
            RulesOfHooksAnalyzer.CalleeName(invocation) is { } name
            && (name == VelvetWellKnownNames.VComponentMethodName || name == VelvetWellKnownNames.VMemoMethodName)
            && invocation.Expression switch
            {
                MemberAccessExpressionSyntax member => LastSegment(member.Expression) == "V",
                _ => true,
            };

        private static string? LastSegment(ExpressionSyntax expression) => expression switch
        {
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            _ => null,
        };

        private static bool HasComponentAttribute(IMethodSymbol method) =>
            method.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == VelvetWellKnownNames.ComponentAttributeFullName);

        private static bool LooksLikeComponentAttribute(SyntaxList<AttributeListSyntax> lists) =>
            lists.SelectMany(list => list.Attributes).Any(attribute =>
                LastSegment(attribute.Name) is "Component" or "ComponentAttribute");

        // A build the token cancels leaves the index unset, so the next question builds it again.
        private Index Read(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _index) is { } built) return built;
            lock (_gate)
            {
                return _index ??= Build(cancellationToken);
            }
        }

        private Index Build(CancellationToken cancellationToken)
        {
            var components = ImmutableHashSet.CreateBuilder<(string Type, string Method)>();
            var mounted = ImmutableHashSet.CreateBuilder<(string Type, string Method)>();
            foreach (var tree in _compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var node in tree.GetRoot(cancellationToken).DescendantNodes())
                {
                    Collect(node, components, mounted);
                }
            }
            return new Index(components.ToImmutable(), mounted.ToImmutable());
        }

        private static void Collect(
            SyntaxNode node,
            ImmutableHashSet<(string Type, string Method)>.Builder components,
            ImmutableHashSet<(string Type, string Method)>.Builder mounted)
        {
            switch (node)
            {
                case MethodDeclarationSyntax method when LooksLikeComponentAttribute(method.AttributeLists):
                    components.Add((EnclosingTypeName(method), method.Identifier.ValueText));
                    break;
                case LocalFunctionStatementSyntax local when LooksLikeComponentAttribute(local.AttributeLists):
                    components.Add((EnclosingTypeName(local), local.Identifier.ValueText));
                    break;
                case InvocationExpressionSyntax invocation when IsMountCall(invocation):
                    foreach (var key in invocation.ArgumentList.Arguments.SelectMany(argument => MountedKeys(invocation, argument)))
                    {
                        mounted.Add(key);
                        components.Add(key);
                    }
                    break;
            }
        }

        // A method group handed as the render body, through any parentheses and casts, keyed by the type it is
        // looked up in: for a qualified name, the qualifier's last name; for a bare one, the innermost enclosing
        // type declaring a method of that name here, or every enclosing type where none does, since the method is
        // then a local function, declared in another part of a partial type, or inherited.
        private static (string Type, string Method)[] MountedKeys(InvocationExpressionSyntax invocation, ArgumentSyntax argument)
        {
            if (!IsComponentBodyArgument(argument)) return Array.Empty<(string, string)>();
            var expression = argument.Expression;
            while (expression is ParenthesizedExpressionSyntax or CastExpressionSyntax)
            {
                expression = expression is CastExpressionSyntax cast
                    ? cast.Expression
                    : ((ParenthesizedExpressionSyntax)expression).Expression;
            }
            switch (expression)
            {
                case MemberAccessExpressionSyntax member:
                    return new[] { (LastSegment(member.Expression) ?? string.Empty, member.Name.Identifier.ValueText) };
                case IdentifierNameSyntax id:
                    var name = id.Identifier.ValueText;
                    var types = invocation.Ancestors().OfType<TypeDeclarationSyntax>().ToArray();
                    var declaring = types.FirstOrDefault(type =>
                        type.Members.OfType<MethodDeclarationSyntax>().Any(method => method.Identifier.ValueText == name));
                    return declaring != null
                        ? new[] { (declaring.Identifier.ValueText, name) }
                        : types.Select(type => (type.Identifier.ValueText, name)).ToArray();
                default:
                    return Array.Empty<(string, string)>();
            }
        }

        private static string EnclosingTypeName(SyntaxNode node) =>
            node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? string.Empty;

        private sealed class Index
        {
            public Index(
                ImmutableHashSet<(string Type, string Method)> componentKeys,
                ImmutableHashSet<(string Type, string Method)> mounted)
            {
                ComponentKeys = componentKeys;
                Mounted = mounted;
            }

            /// <summary>Every component, marked or mounted, as its containing type's simple name and its own name.</summary>
            public ImmutableHashSet<(string Type, string Method)> ComponentKeys { get; }

            /// <summary>Each mounted method as its containing type's simple name and its own name.</summary>
            public ImmutableHashSet<(string Type, string Method)> Mounted { get; }
        }
    }
}
