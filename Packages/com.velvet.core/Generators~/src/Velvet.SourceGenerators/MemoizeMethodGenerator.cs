using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Velvet.SourceGenerators.Diagnostics;
using Velvet.SourceGenerators.Shared;

namespace Velvet.SourceGenerators
{
    /// <summary>
    /// Incremental Source Generator that produces the body of a V.Memoized(...) wrapper from a partial method
    /// declaration annotated with [MemoizeMethod].
    /// </summary>
    /// <remarks>
    /// User writes:   [MemoizeMethod] private partial VNode BuildHeader(string title, int count);
    /// User writes:   private VNode BuildHeader_Impl(string title, int count) => V.Div(...);
    /// SG generates:  private partial VNode BuildHeader(string title, int count)
    ///                  => V.Memoized(() => BuildHeader_Impl(title, count), title, count);
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed class MemoizeMethodGenerator : IIncrementalGenerator
    {
        private const string ImplSuffix = "_Impl";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var methodCandidates = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    VelvetWellKnownNames.MemoizeMethodAttributeFullName,
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

            var isPartial = decl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword));
            if (!isPartial)
            {
                return null;
            }

            var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
            var containingType = method.ContainingType;

            var declarationValid = ValidateDeclaration(decl, method, containingType, diagnostics, cancellationToken);
            var signatureValid = ValidateSignature(ctx.SemanticModel.Compilation, decl, method, diagnostics);
            var isValid = declarationValid && signatureValid;

            MethodInfo? info = isValid ? BuildMethodInfo(decl, method) : (MethodInfo?)null;

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

        /// <summary>
        /// The requirements on how the method and its enclosing types are written: an accessibility modifier,
        /// no body of its own, and <c>partial</c> the whole way out.
        /// </summary>
        private static bool ValidateDeclaration(
            MethodDeclarationSyntax decl,
            IMethodSymbol method,
            INamedTypeSymbol? containingType,
            ImmutableArray<DiagnosticInfo>.Builder diagnostics,
            CancellationToken cancellationToken)
        {
            var isValid = true;

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

            if (containingType is null || !IsAllContainingTypesPartial(containingType, cancellationToken))
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel007ContainingTypeNotPartial,
                    decl.Identifier.GetLocation(),
                    containingType?.ToDisplayString() ?? "<unknown>"));
                isValid = false;
            }

            return isValid;
        }

        /// <summary>
        /// Each of these leaves the V.Memoized wrapper unwritable rather than merely unusual.
        /// </summary>
        private static bool ValidateSignature(
            Compilation compilation,
            MethodDeclarationSyntax decl,
            IMethodSymbol method,
            ImmutableArray<DiagnosticInfo>.Builder diagnostics)
        {
            var isValid = true;

            var isAsyncOrTaskLike = method.IsAsync || IsTaskLikeReturnType(method.ReturnType);
            if (isAsyncOrTaskLike)
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel004AsyncMethodNotSupported,
                    decl.Identifier.GetLocation(),
                    method.Name));
                isValid = false;
            }

            // An in parameter is read-only, so the wrapper copies it and memoizes on the copy. A ref or out
            // parameter cannot be captured by the factory (CS1628), which runs later, during reconcile, after the
            // wrapper has returned to its caller, so no write through one could reach the caller in time.
            if (method.Parameters.Any(p => p.RefKind is RefKind.Ref or RefKind.Out))
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel005RefOutParameterNotSupported,
                    decl.Identifier.GetLocation(),
                    method.Name));
                isValid = false;
            }

            // Every argument is also a dependency, stored in the object?[] V.Memoized compares, and read by the
            // factory lambda. A ref struct can be neither (CS9108), a pointer cannot be stored, and a type holding
            // one needs an unsafe context the generated part does not open (CS0214).
            var unboxable = method.Parameters.FirstOrDefault(p => p.Type.IsRefLikeType || HoldsPointer(p.Type));
            if (unboxable is not null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel010UnboxableParameterNotSupported,
                    decl.Identifier.GetLocation(),
                    method.Name,
                    unboxable.Name,
                    unboxable.Type.ToDisplayString()));
                isValid = false;
            }

            // For async / Task-like cases, VEL004 already conveys the cause clearly, so suppress VEL008
            // (Task<VNode> is not a VNode-derived type, but it is clearer to surface VEL004 first).
            if (!isAsyncOrTaskLike &&
                (method.ReturnsByRef || method.ReturnsByRefReadonly ||
                 !IsMemoNodeAssignableTo(compilation, method.ReturnType)))
            {
                var byRef = method.ReturnsByRefReadonly ? "ref readonly " : method.ReturnsByRef ? "ref " : string.Empty;
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel008NonVNodeReturnType,
                    decl.Identifier.GetLocation(),
                    method.Name,
                    byRef + method.ReturnType.ToDisplayString()));
                isValid = false;
            }

            // The factory runs on a copy of the receiver, because a lambda in a struct cannot capture this
            // (CS1673); a write _Impl makes to that copy never reaches the struct. A ref struct's receiver
            // cannot be copied into anything a lambda may capture at all (CS8175).
            if (!method.IsStatic && method.ContainingType is { IsValueType: true } receiverType &&
                (receiverType.IsRefLikeType || !method.IsReadOnly))
            {
                diagnostics.Add(new DiagnosticInfo(
                    MemoizeDiagnostics.Vel011StructReceiverNotSupported,
                    decl.Identifier.GetLocation(),
                    method.Name,
                    receiverType.ToDisplayString()));
                isValid = false;
            }

            return isValid;
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
            sb.AppendLine("#nullable enable annotations");
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

        private static MethodInfo BuildMethodInfo(MethodDeclarationSyntax decl, IMethodSymbol method)
        {
            var taken = new HashSet<string>(
                method.Parameters.Select(p => p.Name).Concat(method.TypeParameters.Select(tp => tp.Name)),
                StringComparer.Ordinal);
            var typeArguments = method.TypeParameters.IsEmpty
                ? string.Empty
                : $"<{string.Join(", ", method.TypeParameters.Select(tp => EscapeKeyword(tp.Name)))}>";

            var lines = new List<string>
            {
                new StringBuilder()
                    .Append(RenderAccessibility(method.DeclaredAccessibility)).Append(' ')
                    .Append(method.IsStatic ? "static " : string.Empty)
                    .Append(MatchingModifiers(decl))
                    .Append("partial ")
                    .Append(method.ReturnType.ToDisplayString(FullyQualifiedFormat)).Append(' ')
                    .Append(EscapeKeyword(method.Name)).Append(typeArguments)
                    .Append('(')
                    .Append(string.Join(", ", method.Parameters.Select(p =>
                        $"{ParameterModifier(method, p)}{RenderType(p.Type)} {EscapeKeyword(p.Name)}")))
                    .Append(')')
                    .ToString(),
            };
            lines.AddRange(ConstraintClauses(decl, method).Select(clause => "    " + clause));

            var statements = new List<string>();

            // A readonly struct member reaches here (VEL011 rejects the rest); a lambda in a struct cannot
            // capture this (CS1673), so the factory calls _Impl on a copy.
            var target = string.Empty;
            if (!method.IsStatic && method.ContainingType is { IsValueType: true })
            {
                var receiver = UniqueName("self", taken);
                statements.Add($"var {receiver} = this;");
                target = receiver + ".";
            }

            // A lambda cannot capture an in parameter either, so each one is read into a local the factory
            // captures and the memo keys on.
            var arguments = new List<string>();
            foreach (var parameter in method.Parameters)
            {
                var name = EscapeKeyword(parameter.Name);
                if (parameter.RefKind == RefKind.In)
                {
                    var copy = UniqueName(parameter.Name + "Value", taken);
                    statements.Add($"var {copy} = {name};");
                    name = copy;
                }
                arguments.Add(name);
            }

            // The dependency array is always built here rather than left to overload resolution: with one
            // array-typed argument, V.Memoized's params object?[] would take that array as the whole list,
            // compared element by element, or a null one as no list at all. A method's type arguments are
            // part of the key, since the same position can call it with different ones, and so is an instance
            // member's receiver, since it can be called there on different instances.
            var keys = method.Parameters.Where(p => !p.IsParams).Select(p => arguments[p.Ordinal])
                .Concat(method.TypeParameters.Select(tp => $"typeof({EscapeKeyword(tp.Name)})"))
                .ToList();
            if (!method.IsStatic)
            {
                keys.Add("this");
            }
            string deps;
            var paramsParameter = method.Parameters.FirstOrDefault(p => p.IsParams);
            if (paramsParameter is not null)
            {
                // The compiler builds a params array afresh at every call, so its elements are the keys rather
                // than the array, behind its length, which also tells an empty array from a null one.
                var array = arguments[paramsParameter.Ordinal];
                deps = UniqueName("deps", taken);
                keys.Add($"{array}?.Length");
                statements.Add($"var {deps} = new object?[{keys.Count} + ({array}?.Length ?? 0)];");
                for (var i = 0; i < keys.Count; i++)
                {
                    statements.Add($"{deps}[{i}] = {keys[i]};");
                }
                statements.Add($"if ({array} != null) global::System.Array.Copy({array}, 0, {deps}, {keys.Count}, {array}.Length);");
            }
            else
            {
                // arity 0: there are no parameters to key on, and the whole point of the attribute is that the
                // result is computed once. Omitting the argument would instead ask V.Memoized for no dependency
                // array at all, which rebuilds every render; the empty array is what says "no dependencies".
                deps = keys.Count == 0
                    ? "global::System.Array.Empty<object>()"
                    : $"new object?[] {{ {string.Join(", ", keys)} }}";
            }

            var memoCall =
                $"global::Velvet.V.Memoized(() => {target}{EscapeKeyword(method.Name + ImplSuffix)}{typeArguments}({string.Join(", ", arguments)}), {deps})";
            if (statements.Count == 0)
            {
                lines.Add($"    => {memoCall};");
            }
            else
            {
                lines.Add("{");
                lines.AddRange(statements.Select(statement => "    " + statement));
                lines.Add($"    return {memoCall};");
                lines.Add("}");
            }

            return new MethodInfo(method.Name, string.Join("\n", lines));
        }

        private static void AppendMethod(SourceBuilder sb, MethodInfo info)
        {
            foreach (var line in info.Text.Split('\n'))
            {
                sb.AppendLine(line);
            }
        }

        private static string UniqueName(string candidate, HashSet<string> taken)
        {
            while (!taken.Add(candidate))
            {
                candidate += "_";
            }
            return candidate;
        }

        // CS0755 and CS0758 require the implementing declaration to repeat `this` and `params`.
        private static string ParameterModifier(IMethodSymbol method, IParameterSymbol parameter) =>
            (parameter.Ordinal == 0 && method.IsExtensionMethod ? "this " : string.Empty) +
            (parameter.IsParams ? "params " : string.Empty) +
            (parameter.RefKind == RefKind.In ? "in " : string.Empty);

        // CS8800, CS8663 and CS0764 require the implementing declaration to repeat these.
        private static string MatchingModifiers(MethodDeclarationSyntax decl)
        {
            var modifiers = new StringBuilder();
            foreach (var modifier in decl.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.NewKeyword) ||
                    modifier.IsKind(SyntaxKind.VirtualKeyword) ||
                    modifier.IsKind(SyntaxKind.OverrideKeyword) ||
                    modifier.IsKind(SyntaxKind.SealedKeyword) ||
                    modifier.IsKind(SyntaxKind.ReadOnlyKeyword) ||
                    modifier.IsKind(SyntaxKind.UnsafeKeyword))
                {
                    modifiers.Append(modifier.Text).Append(' ');
                }
            }
            return modifiers.ToString();
        }

        // CS0761 requires the implementing declaration to repeat each constraint. An override or an explicit
        // implementation may state only `class`, `struct` or `default` (CS0460), and needs the one its
        // declaration states where `T?` must mean a nullable reference rather than Nullable<T>, so its clauses
        // are copied as written; they name no type that could resolve differently in the generated file.
        private static ImmutableArray<string> ConstraintClauses(MethodDeclarationSyntax decl, IMethodSymbol method)
        {
            if (method.IsOverride || !method.ExplicitInterfaceImplementations.IsEmpty)
            {
                return decl.ConstraintClauses.Select(c => c.NormalizeWhitespace().ToFullString()).ToImmutableArray();
            }

            var clauses = ImmutableArray.CreateBuilder<string>();
            foreach (var typeParameter in method.TypeParameters)
            {
                var constraints = new List<string>();
                if (typeParameter.HasReferenceTypeConstraint)
                {
                    constraints.Add(
                        typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                            ? "class?"
                            : "class");
                }
                else if (typeParameter.HasUnmanagedTypeConstraint)
                {
                    constraints.Add("unmanaged");
                }
                else if (typeParameter.HasValueTypeConstraint)
                {
                    constraints.Add("struct");
                }
                else if (typeParameter.HasNotNullConstraint)
                {
                    constraints.Add("notnull");
                }

                constraints.AddRange(typeParameter.ConstraintTypes.Select(RenderType));

                if (typeParameter.HasConstructorConstraint)
                {
                    constraints.Add("new()");
                }

                if (constraints.Count > 0)
                {
                    clauses.Add($"where {EscapeKeyword(typeParameter.Name)} : {string.Join(", ", constraints)}");
                }
            }
            return clauses.ToImmutable();
        }

        // The display format escapes reserved keywords only, so a contextual one naming a type, a type parameter
        // or a namespace is escaped here. A keyword part is the keyword itself (`global`, `dynamic`) and stays.
        private static string RenderType(ITypeSymbol type) =>
            string.Concat(type.ToDisplayParts(FullyQualifiedFormat).Select(part =>
                part.Kind == SymbolDisplayPartKind.Keyword ? part.ToString() : EscapeKeyword(part.ToString())));

        private static string EscapeKeyword(string identifier) =>
            SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None &&
            SyntaxFacts.GetContextualKeywordKind(identifier) == SyntaxKind.None
                ? identifier
                : "@" + identifier;

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
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                                  SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
                                  SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

        private static bool HoldsPointer(ITypeSymbol type) => type switch
        {
            IPointerTypeSymbol or IFunctionPointerTypeSymbol => true,
            IArrayTypeSymbol array => HoldsPointer(array.ElementType),
            _ => false,
        };

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
            return (unbound is { Name: "Task" or "ValueTask" } &&
                    IsNamespace(unbound.ContainingNamespace, "System.Threading.Tasks")) ||
                   (unbound is { Name: "VelvetTask" } && IsNamespace(unbound.ContainingNamespace, "Velvet"));
        }

        // The wrapper returns the MemoNode V.Memoized builds, so the declared type has to be one MemoNode
        // converts to; another VNode subtype is CS0029 in the emitted body.
        private static bool IsMemoNodeAssignableTo(Compilation compilation, ITypeSymbol returnType)
        {
            if (!IsVNodeOrDerived(returnType))
            {
                return false;
            }
            for (ITypeSymbol? current = compilation.GetTypeByMetadataName("Velvet.MemoNode");
                 current is not null;
                 current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, returnType))
                {
                    return true;
                }
            }
            return false;
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
            var namespaceParts = new List<string>();
            for (var ns = type.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
            {
                namespaceParts.Insert(0, EscapeKeyword(ns.Name));
            }
            var namespaceName = string.Join(".", namespaceParts);

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
            return type.TypeKind == TypeKind.Interface ? "interface" : "class";
        }

        private static string BuildTypeDeclaration(INamedTypeSymbol type)
        {
            if (type.TypeParameters.Length == 0)
            {
                return EscapeKeyword(type.Name);
            }
            var parameters = string.Join(", ", type.TypeParameters.Select(tp => EscapeKeyword(tp.Name)));
            return $"{EscapeKeyword(type.Name)}<{parameters}>";
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
            foreach (var ch in baseName.Replace("@", string.Empty))
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
            // Build the suffix from the first 4 bytes (32 bits) of SHA256. The collision probability within the same trunk is 1/2^32,
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
            public MethodInfo(string name, string text)
            {
                Name = name;
                Text = text;
            }

            public string Name { get; }

            /// <summary>The emitted member, one line per <c>\n</c>, unindented.</summary>
            public string Text { get; }

            public bool Equals(MethodInfo other) =>
                string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                string.Equals(Text, other.Text, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is MethodInfo other && Equals(other);

            public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);
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
