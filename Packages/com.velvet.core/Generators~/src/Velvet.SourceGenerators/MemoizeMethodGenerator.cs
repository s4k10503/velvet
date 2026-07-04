using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Velvet.SourceGenerators.Diagnostics;
using Velvet.SourceGenerators.Shared;

namespace Velvet.SourceGenerators
{
    /// <summary>
    /// Incremental Source Generator that produces the body of a V.Memoized(...) wrapper from a partial method
    /// declaration annotated with [Memoize].
    /// </summary>
    /// <remarks>
    /// User writes:   [Memoize] private partial VNode BuildHeader(string title, int count);
    /// User writes:   private VNode BuildHeader_Impl(string title, int count) => V.Div(...);
    /// SG generates:  private partial VNode BuildHeader(string title, int count)
    ///                  => V.Memoized(() => BuildHeader_Impl(title, count), title, count);
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed class MemoizeMethodGenerator : IIncrementalGenerator
    {
        private const string MemoizeAttributeMetadataName = "Velvet.MemoizeAttribute";
        private const string ImplSuffix = "_Impl";
        internal const int MinArity = 0;
        internal const int MaxArity = 8;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var methodCandidates = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    MemoizeAttributeMetadataName,
                    predicate: static (node, _) => node is MethodDeclarationSyntax,
                    transform: static (ctx, ct) => BuildCandidate(ctx, ct))
                .Where(static c => c is not null)!
                .Select(static (c, _) => c!.Value);

            var grouped = methodCandidates.Collect();
            context.RegisterSourceOutput(grouped, static (spc, candidates) => Emit(spc, candidates));
        }

        private static MemoizeCandidate? BuildCandidate(
            GeneratorAttributeSyntaxContext ctx,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ctx.TargetSymbol is not IMethodSymbol method)
            {
                return null;
            }

            if (ctx.TargetNode is not MethodDeclarationSyntax decl)
            {
                return null;
            }

            var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
            var isValid = true;

            var isPartial = decl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword));
            if (!isPartial)
            {
                return null;
            }

            var hasAccessibility = decl.Modifiers.Any(m =>
                m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword) ||
                m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword) ||
                m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InternalKeyword) ||
                m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ProtectedKeyword));
            if (!hasAccessibility)
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel006MissingAccessibilityModifier,
                    decl.Identifier.GetLocation(),
                    method.Name));
                isValid = false;
            }

            if (decl.Body is not null || decl.ExpressionBody is not null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel009PartialMethodAlreadyHasBody,
                    decl.Identifier.GetLocation(),
                    method.Name));
                isValid = false;
            }

            var containingType = method.ContainingType;
            if (containingType is null || !IsAllContainingTypesPartial(containingType, cancellationToken))
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel007ContainingTypeNotPartial,
                    decl.Identifier.GetLocation(),
                    containingType?.ToDisplayString() ?? "<unknown>"));
                isValid = false;
            }

            var parameters = method.Parameters;
            var arity = parameters.Length;
            if (arity == 0 && isValid)
            {
                // Arity 0 is the deps-less memoization shape: generation always proceeds, but a warning is emitted
                // when the corresponding _Impl method is not provably Pure. The deps-less V.Memoized cache returns the
                // same VNode forever, so impure factories silently leak stale state.
                // Skipped when isValid is already false (VEL006 / VEL007 / VEL009 etc.) to avoid spurious double
                // warnings for shapes that are rejected for unrelated reasons.
                if (!IsImplMethodPure(ctx.SemanticModel.Compilation, containingType, method.Name, cancellationToken))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        MemoizeDiagnostics.Vel001ArityZeroCannotProvePurity,
                        decl.Identifier.GetLocation(),
                        method.Name));
                }
            }
            else if (arity > MaxArity)
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel002ArityExceedsLimit,
                    decl.Identifier.GetLocation(),
                    method.Name,
                    arity.ToString(CultureInfo.InvariantCulture)));
                isValid = false;
            }

            if (method.TypeParameters.Length > 0)
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel003GenericMethodNotSupported,
                    decl.Identifier.GetLocation(),
                    method.Name));
                isValid = false;
            }

            var isAsyncOrTaskLike = method.IsAsync || IsTaskLikeReturnType(method.ReturnType);
            if (isAsyncOrTaskLike)
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel004AsyncMethodNotSupported,
                    decl.Identifier.GetLocation(),
                    method.Name));
                isValid = false;
            }

            if (parameters.Any(p => p.RefKind is RefKind.Ref or RefKind.Out or RefKind.RefReadOnly or RefKind.In))
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel005RefOutParameterNotSupported,
                    decl.Identifier.GetLocation(),
                    method.Name));
                isValid = false;
            }

            // For async / Task-like cases, VEL004 already conveys the cause clearly, so suppress VEL008
            // (Task<VNode> is not a VNode-derived type, but it is clearer to surface VEL004 first).
            if (!isAsyncOrTaskLike && !IsVNodeOrDerived(method.ReturnType))
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel008NonVNodeReturnType,
                    decl.Identifier.GetLocation(),
                    method.Name,
                    method.ReturnType.ToDisplayString()));
                isValid = false;
            }

            MethodInfo? info = isValid
                ? new MethodInfo(
                    name: method.Name,
                    accessibility: RenderAccessibility(method.DeclaredAccessibility),
                    isStatic: method.IsStatic,
                    returnTypeDisplay: method.ReturnType.ToDisplayString(FullyQualifiedFormat),
                    parameters: parameters
                        .Select(p => new ParameterInfo(p.Name, p.Type.ToDisplayString(FullyQualifiedFormat)))
                        .ToImmutableArray())
                : (MethodInfo?)null;

            if (containingType is null)
            {
                return new MemoizeCandidate(
                    typeKey: new TypeKey(string.Empty, ImmutableArray<TypeKey.TypeSegment>.Empty),
                    hintName: $"{method.Name}.Memoize.g.cs",
                    method: null,
                    diagnostics: diagnostics.ToImmutable());
            }

            return new MemoizeCandidate(
                typeKey: BuildTypeKey(containingType),
                hintName: BuildHintName(containingType),
                method: info,
                diagnostics: diagnostics.ToImmutable());
        }

        private static void Emit(SourceProductionContext spc, ImmutableArray<MemoizeCandidate> candidates)
        {
            foreach (var candidate in candidates)
            {
                foreach (var info in candidate.Diagnostics)
                {
                    spc.ReportDiagnostic(info.ToDiagnostic());
                }
            }

            var validByType = candidates
                .Where(c => c.Method is not null)
                .GroupBy(c => c.TypeKey);

            var emittedHints = new HashSet<string>(StringComparer.Ordinal);

            foreach (var group in validByType)
            {
                var first = group.First();
                var source = GenerateSourceForType(first.TypeKey, group.Select(c => c.Method!.Value).ToImmutableArray());
                var hintName = UniqueHintName(emittedHints, first.HintName, source);
                spc.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
            }
        }

        internal static string GenerateSourceForType(TypeKey typeKey, ImmutableArray<MethodInfo> methods)
        {
            var sb = new SourceBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable disable");
            sb.AppendLine();

            if (typeKey.NamespaceName is { Length: > 0 } ns)
            {
                sb.AppendLine($"namespace {ns}");
                using (sb.Block())
                {
                    EmitTypeChainInner(sb, typeKey.TypeChain, 0, methods);
                }
            }
            else
            {
                EmitTypeChainInner(sb, typeKey.TypeChain, 0, methods);
            }

            return sb.ToString();
        }

        private static void EmitTypeChainInner(
            SourceBuilder sb,
            ImmutableArray<TypeKey.TypeSegment> chain,
            int index,
            ImmutableArray<MethodInfo> methods)
        {
            var segment = chain[index];
            sb.AppendLine($"partial {segment.Keyword} {segment.Declaration}");
            using (sb.Block())
            {
                if (index + 1 < chain.Length)
                {
                    EmitTypeChainInner(sb, chain, index + 1, methods);
                }
                else
                {
                    for (var i = 0; i < methods.Length; i++)
                    {
                        AppendMethod(sb, methods[i]);
                        if (i < methods.Length - 1)
                        {
                            sb.AppendLine();
                        }
                    }
                }
            }
        }

        private static void AppendMethod(SourceBuilder sb, MethodInfo info)
        {
            var paramList = string.Join(", ", info.Parameters.Select(p => $"{p.TypeDisplay} {p.Name}"));
            var nameList = string.Join(", ", info.Parameters.Select(p => p.Name));
            var staticModifier = info.IsStatic ? "static " : string.Empty;
            sb.AppendLine($"{info.Accessibility} {staticModifier}partial {info.ReturnTypeDisplay} {info.Name}({paramList})");
            if (info.Parameters.Length == 0)
            {
                // arity 0: no deps to forward; V.Memoized's params object[] is omitted so the cache key is the empty deps array.
                sb.AppendLine($"    => global::Velvet.V.Memoized(() => {info.Name}{ImplSuffix}());");
            }
            else
            {
                sb.AppendLine($"    => global::Velvet.V.Memoized(() => {info.Name}{ImplSuffix}({nameList}), {nameList});");
            }
        }

        /// <summary>
        /// Returns true when the &lt;methodName&gt;_Impl member exists on <paramref name="containingType"/> and
        /// <see cref="PurityAnalysis.PurityAnalyzer"/> classifies it as Pure. Unknown / Impure / missing yields false
        /// (caller treats this as "cannot relax the arity 0 restriction").
        /// </summary>
        private static bool IsImplMethodPure(
            Compilation compilation,
            INamedTypeSymbol? containingType,
            string methodName,
            CancellationToken cancellationToken)
        {
            if (containingType is null)
            {
                return false;
            }

            var implName = methodName + ImplSuffix;
            foreach (var member in containingType.GetMembers(implName))
            {
                if (member is not IMethodSymbol implMethod || implMethod.Parameters.Length != 0)
                {
                    continue;
                }

                var purity = PurityAnalysis.PurityAnalyzer.Analyze(implMethod, compilation, cancellationToken);
                if (purity.Purity == PurityAnalysis.Purity.Pure)
                {
                    return true;
                }
            }

            return false;
        }

        private static string RenderAccessibility(Accessibility accessibility) => accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.Private => "private",
            _ => "private",
        };

        private static readonly SymbolDisplayFormat FullyQualifiedFormat = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        private static bool IsAllContainingTypesPartial(INamedTypeSymbol type, CancellationToken cancellationToken)
        {
            for (var current = type; current is not null; current = current.ContainingType)
            {
                var anyPartial = false;
                foreach (var reference in current.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax typeDecl &&
                        typeDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
                    {
                        anyPartial = true;
                        break;
                    }
                }
                if (!anyPartial)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsTaskLikeReturnType(ITypeSymbol returnType)
        {
            if (returnType is not INamedTypeSymbol named)
            {
                return false;
            }
            var unbound = named.IsGenericType ? named.ConstructedFrom : named;
            return unbound is { Name: "Task" or "ValueTask" } &&
                   IsNamespace(unbound.ContainingNamespace, "System.Threading.Tasks");
        }

        private static bool IsVNodeOrDerived(ITypeSymbol type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (current is INamedTypeSymbol named &&
                    named.Name == "VNode" &&
                    IsNamespace(named.ContainingNamespace, "Velvet"))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsNamespace(INamespaceSymbol ns, string dottedName)
        {
            if (ns is null)
            {
                return false;
            }
            var parts = dottedName.Split('.');
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                if (ns is null || ns.IsGlobalNamespace || ns.Name != parts[i])
                {
                    return false;
                }
                ns = ns.ContainingNamespace;
            }
            return ns is { IsGlobalNamespace: true };
        }

        private static TypeKey BuildTypeKey(INamedTypeSymbol type)
        {
            var namespaceName = type.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? ns.ToDisplayString()
                : string.Empty;

            var chain = ImmutableArray.CreateBuilder<TypeKey.TypeSegment>();
            for (var current = type; current is not null; current = current.ContainingType)
            {
                chain.Insert(0, new TypeKey.TypeSegment(
                    keyword: GetTypeKeyword(current),
                    declaration: BuildTypeDeclaration(current)));
            }

            return new TypeKey(namespaceName, chain.ToImmutable());
        }

        private static string GetTypeKeyword(INamedTypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Struct)
            {
                return type.IsRecord ? "record struct" : "struct";
            }
            if (type.IsRecord)
            {
                return "record";
            }
            return "class";
        }

        private static string BuildTypeDeclaration(INamedTypeSymbol type)
        {
            if (type.TypeParameters.Length == 0)
            {
                return type.Name;
            }
            var parameters = string.Join(", ", type.TypeParameters.Select(tp => tp.Name));
            return $"{type.Name}<{parameters}>";
        }

        private static string BuildHintName(INamedTypeSymbol type)
        {
            var ns = type.ContainingNamespace is { IsGlobalNamespace: false } n
                ? n.ToDisplayString()
                : string.Empty;

            var chain = new List<string>();
            for (var current = type; current is not null; current = current.ContainingType)
            {
                var name = current.TypeParameters.Length > 0
                    ? $"{current.Name}_T{current.TypeParameters.Length}"
                    : current.Name;
                chain.Insert(0, name);
            }

            var baseName = ns.Length > 0
                ? $"{ns}.{string.Join("_", chain)}"
                : string.Join("_", chain);

            var safe = new StringBuilder(baseName.Length);
            foreach (var ch in baseName)
            {
                safe.Append(ch switch
                {
                    '<' or '>' or ' ' or ',' or '`' => '_',
                    _ => ch,
                });
            }

            return $"{safe}.Memoize.g.cs";
        }

        private static string UniqueHintName(HashSet<string> emitted, string hintName, string content)
        {
            if (emitted.Add(hintName))
            {
                return hintName;
            }
            // Build the suffix from the upper 4 bytes (32 bits) of SHA256. The collision probability within the same trunk is 1/2^32,
            // which is negligible in practice. If a collision still happens, the Add failure has no effect on the outcome, so we ignore the return value.
            var hash = ComputeShortHash(content);
            var dotIndex = hintName.IndexOf('.');
            var trunk = dotIndex > 0 ? hintName.Substring(0, dotIndex) : hintName;
            var suffix = dotIndex > 0 ? hintName.Substring(dotIndex) : string.Empty;
            var unique = $"{trunk}__{hash}{suffix}";
            emitted.Add(unique);
            return unique;
        }

        private static string ComputeShortHash(string content)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
            var sb = new StringBuilder(8);
            for (var i = 0; i < 4; i++)
            {
                sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        internal readonly struct TypeKey : IEquatable<TypeKey>
        {
            public TypeKey(string namespaceName, ImmutableArray<TypeSegment> typeChain)
            {
                NamespaceName = namespaceName ?? string.Empty;
                TypeChain = typeChain;
            }

            public string NamespaceName { get; }
            public ImmutableArray<TypeSegment> TypeChain { get; }

            public bool Equals(TypeKey other)
            {
                if (!string.Equals(NamespaceName, other.NamespaceName, StringComparison.Ordinal))
                {
                    return false;
                }
                if (TypeChain.Length != other.TypeChain.Length)
                {
                    return false;
                }
                for (var i = 0; i < TypeChain.Length; i++)
                {
                    if (!TypeChain[i].Equals(other.TypeChain[i]))
                    {
                        return false;
                    }
                }
                return true;
            }

            public override bool Equals(object? obj) => obj is TypeKey other && Equals(other);

            public override int GetHashCode()
            {
                var hash = StringComparer.Ordinal.GetHashCode(NamespaceName);
                foreach (var seg in TypeChain)
                {
                    hash = unchecked(hash * 31 + seg.GetHashCode());
                }
                return hash;
            }

            internal readonly struct TypeSegment : IEquatable<TypeSegment>
            {
                public TypeSegment(string keyword, string declaration)
                {
                    Keyword = keyword;
                    Declaration = declaration;
                }

                public string Keyword { get; }
                public string Declaration { get; }

                public bool Equals(TypeSegment other) =>
                    string.Equals(Keyword, other.Keyword, StringComparison.Ordinal) &&
                    string.Equals(Declaration, other.Declaration, StringComparison.Ordinal);

                public override bool Equals(object? obj) => obj is TypeSegment other && Equals(other);

                public override int GetHashCode() =>
                    unchecked(StringComparer.Ordinal.GetHashCode(Keyword) * 31 +
                              StringComparer.Ordinal.GetHashCode(Declaration));
            }
        }

        internal readonly struct MemoizeCandidate : IEquatable<MemoizeCandidate>
        {
            public MemoizeCandidate(
                TypeKey typeKey,
                string hintName,
                MethodInfo? method,
                ImmutableArray<DiagnosticInfo> diagnostics)
            {
                TypeKey = typeKey;
                HintName = hintName;
                Method = method;
                Diagnostics = diagnostics;
            }

            public TypeKey TypeKey { get; }
            public string HintName { get; }
            public MethodInfo? Method { get; }
            public ImmutableArray<DiagnosticInfo> Diagnostics { get; }

            public bool Equals(MemoizeCandidate other) =>
                TypeKey.Equals(other.TypeKey) &&
                string.Equals(HintName, other.HintName, StringComparison.Ordinal) &&
                Method.HasValue == other.Method.HasValue &&
                (!Method.HasValue || (other.Method.HasValue && Method.Value.Equals(other.Method.Value))) &&
                Diagnostics.SequenceEqual(other.Diagnostics);

            public override bool Equals(object? obj) => obj is MemoizeCandidate other && Equals(other);

            public override int GetHashCode() =>
                unchecked(TypeKey.GetHashCode() * 31 +
                          (HintName is null ? 0 : StringComparer.Ordinal.GetHashCode(HintName)));
        }

        internal readonly struct MethodInfo : IEquatable<MethodInfo>
        {
            public MethodInfo(
                string name,
                string accessibility,
                bool isStatic,
                string returnTypeDisplay,
                ImmutableArray<ParameterInfo> parameters)
            {
                Name = name;
                Accessibility = accessibility;
                IsStatic = isStatic;
                ReturnTypeDisplay = returnTypeDisplay;
                Parameters = parameters;
            }

            public string Name { get; }
            public string Accessibility { get; }
            public bool IsStatic { get; }
            public string ReturnTypeDisplay { get; }
            public ImmutableArray<ParameterInfo> Parameters { get; }

            public bool Equals(MethodInfo other) =>
                string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                string.Equals(Accessibility, other.Accessibility, StringComparison.Ordinal) &&
                IsStatic == other.IsStatic &&
                string.Equals(ReturnTypeDisplay, other.ReturnTypeDisplay, StringComparison.Ordinal) &&
                Parameters.SequenceEqual(other.Parameters);

            public override bool Equals(object? obj) => obj is MethodInfo other && Equals(other);

            public override int GetHashCode() =>
                unchecked(StringComparer.Ordinal.GetHashCode(Name) * 31 + Parameters.Length);
        }

        internal readonly struct ParameterInfo : IEquatable<ParameterInfo>
        {
            public ParameterInfo(string name, string typeDisplay)
            {
                Name = name;
                TypeDisplay = typeDisplay;
            }

            public string Name { get; }
            public string TypeDisplay { get; }

            public bool Equals(ParameterInfo other) =>
                string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                string.Equals(TypeDisplay, other.TypeDisplay, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is ParameterInfo other && Equals(other);

            public override int GetHashCode() =>
                unchecked(StringComparer.Ordinal.GetHashCode(Name) * 31 + StringComparer.Ordinal.GetHashCode(TypeDisplay));
        }

        /// <summary>
        /// Struct that holds diagnostic information in a form safe for the IIncrementalGenerator cache.
        /// Holding <see cref="Location"/> directly would pin <see cref="SyntaxTree"/> in the incremental cache,
        /// causing equality checks to break on unrelated trivia changes; instead, hold (filePath, TextSpan, LinePositionSpan)
        /// as a value-type tuple and reconstruct Location when emitting the diagnostic.
        /// </summary>
        internal readonly struct DiagnosticInfo : IEquatable<DiagnosticInfo>
        {
            private readonly DiagnosticDescriptor _descriptor;
            private readonly string _filePath;
            private readonly TextSpan _textSpan;
            private readonly LinePositionSpan _lineSpan;
            private readonly ImmutableArray<string> _messageArgs;

            public DiagnosticInfo(DiagnosticDescriptor descriptor, Location location, params string[] messageArgs)
            {
                _descriptor = descriptor;
                var fileSpan = location?.GetLineSpan() ?? default;
                _filePath = fileSpan.Path ?? string.Empty;
                _textSpan = location?.SourceSpan ?? default;
                _lineSpan = fileSpan.Span;
                _messageArgs = messageArgs?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
            }

            public Diagnostic ToDiagnostic()
            {
                var location = string.IsNullOrEmpty(_filePath)
                    ? Location.None
                    : Location.Create(_filePath, _textSpan, _lineSpan);
                return Diagnostic.Create(_descriptor, location, _messageArgs.Cast<object?>().ToArray());
            }

            public bool Equals(DiagnosticInfo other) =>
                ReferenceEquals(_descriptor, other._descriptor) &&
                string.Equals(_filePath, other._filePath, StringComparison.Ordinal) &&
                _textSpan == other._textSpan &&
                _lineSpan.Equals(other._lineSpan) &&
                _messageArgs.SequenceEqual(other._messageArgs);

            public override bool Equals(object? obj) => obj is DiagnosticInfo other && Equals(other);

            public override int GetHashCode() =>
                unchecked(StringComparer.Ordinal.GetHashCode(_descriptor.Id) * 31 + _textSpan.GetHashCode());
        }
    }
}
