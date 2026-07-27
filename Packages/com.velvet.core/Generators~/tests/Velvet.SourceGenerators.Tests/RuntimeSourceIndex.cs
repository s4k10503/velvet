using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>One public method declaration parsed off the runtime sources, with its declaring type.</summary>
    internal readonly record struct MethodDeclaration(string ContainingTypeFullName, MethodDeclarationSyntax Method);

    /// <summary>
    /// Namespace-qualified index of the type and public-member declarations in the Velvet runtime sources.
    /// </summary>
    /// <remarks>
    /// The parse is syntax only: no assembly references, no build ordering, and no dependency on a
    /// Unity-produced artifact CI does not have. That is what lets a guard on this side of the compile
    /// boundary re-derive the runtime surface at all. The cost is that nothing here is semantically resolved —
    /// a type name is whatever the source spells, so two types sharing a simple name are indistinguishable to
    /// a caller comparing simple names.
    /// </remarks>
    internal sealed class RuntimeSourceIndex
    {
        // The Unity build defines this; parsing with and without it keeps editor-only declarations visible.
        private static readonly string[] EditorPreprocessorSymbols = { "UNITY_EDITOR" };

        private static readonly Lazy<RuntimeSourceIndex> Instance = new(Build);

        private readonly List<TypeDeclaration> _types;

        private RuntimeSourceIndex(HashSet<string> typeNames, List<TypeDeclaration> types)
        {
            DeclaredTypeFullNames = typeNames;
            _types = types;
        }

        /// <summary>
        /// Index shared by every fixture that re-derives the runtime surface. Parsing the whole runtime tree
        /// twice per file is slow enough that each fixture building its own is felt in the suite's runtime.
        /// </summary>
        public static RuntimeSourceIndex Shared => Instance.Value;

        /// <summary>Every declared type, in both the display (<c>Outer.Inner</c>) and metadata (<c>Outer+Inner</c>) spelling.</summary>
        public IReadOnlySet<string> DeclaredTypeFullNames { get; }

        /// <summary>
        /// Every declaration of a type, which is more than one when it is <c>partial</c> or when a
        /// preprocessor branch redeclares it. A caller comparing a declaration against the runtime must accept
        /// a match against any of them.
        /// </summary>
        public IEnumerable<TypeDeclarationSyntax> TypeDeclarationsOf(string containingTypeFullName) =>
            _types.Where(t => t.FullName == containingTypeFullName).Select(t => t.Declaration);

        public IEnumerable<MemberDeclarationSyntax> PublicMembersOf(string containingTypeFullName) =>
            TypeDeclarationsOf(containingTypeFullName)
                .SelectMany(declaration => declaration.Members)
                .Where(member => member.Modifiers.Any(SyntaxKind.PublicKeyword));

        public IEnumerable<MethodDeclaration> PublicMethodsOf(string containingTypeFullName) =>
            PublicMembersOf(containingTypeFullName)
                .OfType<MethodDeclarationSyntax>()
                .Select(method => new MethodDeclaration(containingTypeFullName, method));

        public IEnumerable<string> PublicMethodNamesOf(string containingTypeFullName) =>
            PublicMethodsOf(containingTypeFullName).Select(m => m.Method.Identifier.ValueText);

        public static RuntimeSourceIndex Build()
        {
            var typeNames = new HashSet<string>(StringComparer.Ordinal);
            var types = new List<TypeDeclaration>();

            foreach (var file in RuntimeSourceFiles())
            {
                var text = File.ReadAllText(file);
                Collect(CSharpSyntaxTree.ParseText(text, DefaultParseOptions), typeNames, types);
                Collect(CSharpSyntaxTree.ParseText(text, EditorParseOptions), typeNames, types);
            }

            return new RuntimeSourceIndex(typeNames, types);
        }

        private static CSharpParseOptions DefaultParseOptions =>
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

        private static CSharpParseOptions EditorParseOptions =>
            DefaultParseOptions.WithPreprocessorSymbols(EditorPreprocessorSymbols);

        private static void Collect(SyntaxTree tree, HashSet<string> typeNames, List<TypeDeclaration> types)
        {
            var root = tree.GetRoot();

            // Enums and delegates carry no members but can still be named by a well-known-name constant.
            foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                AddNames(typeNames, declaration, declaration.Identifier.ValueText);
            }
            foreach (var declaration in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
            {
                AddNames(typeNames, declaration, declaration.Identifier.ValueText);
            }

            foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var displayName = QualifiedName(declaration, declaration.Identifier.ValueText, nestingSeparator: ".");
                types.Add(new TypeDeclaration(displayName, declaration));
            }
        }

        // A nested type is spelled Outer.Inner by the analyzer's display-string comparisons and Outer+Inner
        // by the attribute lookups' metadata names; both forms are indexed so either resolves.
        private static void AddNames(HashSet<string> typeNames, SyntaxNode declaration, string identifier)
        {
            typeNames.Add(QualifiedName(declaration, identifier, nestingSeparator: "."));
            typeNames.Add(QualifiedName(declaration, identifier, nestingSeparator: "+"));
        }

        /// <summary>
        /// The namespace- and nesting-qualified name a declaration is indexed under. Public because a caller
        /// looking a declaration up must derive its name exactly this way; deriving it independently is how a
        /// lookup silently misses and a guard built on it passes with nothing compared.
        /// </summary>
        public static string QualifiedName(SyntaxNode declaration, string identifier, string nestingSeparator)
        {
            var typeSegments = new List<string> { identifier };
            var containingNamespace = string.Empty;
            foreach (var ancestor in declaration.Ancestors())
            {
                switch (ancestor)
                {
                    case BaseTypeDeclarationSyntax outer:
                        typeSegments.Insert(0, outer.Identifier.ValueText);
                        break;
                    case BaseNamespaceDeclarationSyntax ns:
                        containingNamespace = ns.Name.ToString() +
                            (containingNamespace.Length == 0 ? string.Empty : "." + containingNamespace);
                        break;
                }
            }

            var nested = string.Join(nestingSeparator, typeSegments);
            return containingNamespace.Length == 0 ? nested : containingNamespace + "." + nested;
        }

        // Colocated tests declare their own fixtures and would let a stub stand in for a renamed runtime
        // type, so only production sources are indexed.
        private static List<string> RuntimeSourceFiles()
        {
            var root = SolutionPaths.RuntimeRoot();
            var files = Directory
                .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("Tests"))
                .ToList();
            if (files.Count == 0)
            {
                throw new InvalidOperationException($"No runtime C# sources found under '{root}'.");
            }
            return files;
        }

        private readonly record struct TypeDeclaration(string FullName, TypeDeclarationSyntax Declaration);
    }
}
