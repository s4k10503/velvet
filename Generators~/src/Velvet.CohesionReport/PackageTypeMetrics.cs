using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Velvet.CohesionReport
{
    /// <summary>
    /// Syntax-level cohesion and coupling metrics over the package's non-test sources. Ca and Ce count
    /// simple-name matches across files, not semantic references — rankings are useful; the integers are not.
    /// </summary>
    internal static class PackageTypeMetrics
    {
        internal sealed class TypeRecord
        {
            public string Key { get; init; } = "";
            public string SimpleName { get; init; } = "";
            public string QualifiedName { get; init; } = "";
            public string File { get; init; } = "";
            public string SourceText { get; init; } = "";
            public int Lines { get; init; }
            public int Fields { get; init; }
            public int Methods { get; init; }
            public int Ce { get; init; }
            public int Ca { get; set; }
            public double Instability { get; set; }
            public int Rfc { get; init; }
            public int Lcom1 { get; init; }
            public double LcomHs { get; init; }
        }

        internal sealed class AssemblyRecord
        {
            public string Name { get; init; } = "";
            public string Path { get; init; } = "";
            public int Ce { get; init; }
            public int Ca { get; set; }
            public double Instability { get; set; }
        }

        internal static IReadOnlyList<TypeRecord> MeasureTypes()
        {
            var packageRoot = PackageRoot();
            var types = new Dictionary<string, TypeRecord>(StringComparer.Ordinal);

            foreach (var file in PackageSourceFiles(packageRoot))
            {
                var relative = file.Substring(packageRoot.Length + 1);
                var text = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(text,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
                CollectType(tree, relative, text, types);
            }

            foreach (var record in types.Values.ToList())
            {
                var consumers = new HashSet<string>(StringComparer.Ordinal);
                foreach (var other in types.Values)
                {
                    if (other.Key == record.Key) continue;
                    if (other.SourceText.Contains(record.SimpleName, StringComparison.Ordinal))
                        consumers.Add(other.Key);
                }
                record.Ca = consumers.Count;
                record.Instability = record.Ca + record.Ce == 0
                    ? 0
                    : (double)record.Ce / (record.Ca + record.Ce);
            }

            return types.Values.ToList();
        }

        internal static IReadOnlyList<AssemblyRecord> MeasureAssemblies()
        {
            var asmdefs = PackageAsmdefs();
            var byName = new Dictionary<string, AssemblyRecord>(StringComparer.Ordinal);
            var references = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var path in asmdefs)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var name = document.RootElement.GetProperty("name").GetString()!;
                var refs = document.RootElement.TryGetProperty("references", out var referencesElement)
                    ? referencesElement.EnumerateArray()
                        .Select(element => element.GetString()!)
                        .Where(reference => !string.IsNullOrEmpty(reference))
                        .ToList()
                    : new List<string>();

                var relative = Path.GetRelativePath(PackageRoot(), path).Replace('\\', '/');
                byName[name] = new AssemblyRecord
                {
                    Name = name,
                    Path = relative,
                    Ce = refs.Count,
                };
                references[name] = refs;
            }

            foreach (var (consumer, refs) in references)
            {
                foreach (var reference in refs)
                {
                    if (!byName.TryGetValue(reference, out var target)) continue;
                    target.Ca++;
                }
            }

            foreach (var record in byName.Values)
            {
                record.Instability = record.Ca + record.Ce == 0
                    ? 0
                    : (double)record.Ce / (record.Ca + record.Ce);
            }

            return byName.Values.OrderBy(record => record.Name, StringComparer.Ordinal).ToList();
        }

        internal static string FormatTypeOutliers(
            IReadOnlyList<TypeRecord> types,
            string metric,
            Func<TypeRecord, double> selector,
            int count)
        {
            var ordered = types
                .OrderByDescending(selector)
                .ThenBy(record => record.QualifiedName, StringComparer.Ordinal)
                .Take(count)
                .Select(record => $"{record.QualifiedName} ({selector(record):0}) in {record.File}")
                .ToList();
            return $"{metric}: {string.Join("; ", ordered)}";
        }

        internal static string FormatAssemblyTable(IReadOnlyList<AssemblyRecord> assemblies)
        {
            var lines = assemblies
                .OrderByDescending(record => record.Ca + record.Ce)
                .Select(record =>
                    $"{record.Name} Ca={record.Ca} Ce={record.Ce} I={record.Instability:F2} ({record.Path})")
                .ToList();
            return string.Join(Environment.NewLine, lines);
        }

        private static void CollectType(
            SyntaxTree tree,
            string relative,
            string text,
            Dictionary<string, TypeRecord> types)
        {
            foreach (var typeDecl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var qualified = QualifiedName(typeDecl, ".");
                var key = $"{relative}|{qualified}";
                if (types.ContainsKey(key)) continue;

                var startLine = tree.GetText().Lines.GetLineFromPosition(typeDecl.SpanStart).LineNumber + 1;
                var endLine = tree.GetText().Lines.GetLineFromPosition(typeDecl.Span.End).LineNumber + 1;

                var methods = typeDecl.Members.OfType<MethodDeclarationSyntax>().ToList();
                var instanceFields = CollectInstanceFields(typeDecl);
                var instanceMethodAccess = methods
                    .Where(method => !method.Modifiers.Any(SyntaxKind.StaticKeyword))
                    .Select(method => FieldsAccessedBy(method, instanceFields))
                    .ToList();

                var callCount = methods.Sum(method =>
                    method.DescendantNodes().OfType<InvocationExpressionSyntax>().Count());

                var externalTypes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var node in typeDecl.DescendantNodes())
                {
                    switch (node)
                    {
                        case IdentifierNameSyntax identifier
                            when identifier.Identifier.ValueText.Length > 0
                                 && char.IsUpper(identifier.Identifier.ValueText[0]):
                            externalTypes.Add(identifier.Identifier.ValueText);
                            break;
                        case MemberAccessExpressionSyntax memberAccess
                            when memberAccess.Expression is IdentifierNameSyntax expression
                                 && char.IsUpper(expression.Identifier.ValueText[0]):
                            externalTypes.Add(expression.Identifier.ValueText);
                            break;
                    }
                }

                types[key] = new TypeRecord
                {
                    Key = key,
                    SimpleName = typeDecl.Identifier.ValueText,
                    QualifiedName = qualified,
                    File = relative,
                    SourceText = text,
                    Lines = endLine - startLine + 1,
                    Fields = instanceFields.Count,
                    Methods = methods.Count,
                    Ce = externalTypes.Count,
                    Rfc = methods.Count + callCount,
                    Lcom1 = Lcom1(instanceMethodAccess),
                    LcomHs = LcomHs(instanceMethodAccess.Count, instanceFields.Count, instanceMethodAccess),
                };
            }
        }

        private static HashSet<string> CollectInstanceFields(TypeDeclarationSyntax typeDecl)
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in typeDecl.Members.OfType<FieldDeclarationSyntax>())
            {
                if (field.Modifiers.Any(SyntaxKind.StaticKeyword)) continue;
                foreach (var variable in field.Declaration.Variables)
                    fields.Add(variable.Identifier.ValueText);
            }
            return fields;
        }

        private static HashSet<string> FieldsAccessedBy(
            MethodDeclarationSyntax method,
            HashSet<string> instanceFields)
        {
            var accessed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var identifier in method.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (instanceFields.Contains(identifier.Identifier.ValueText))
                    accessed.Add(identifier.Identifier.ValueText);
            }
            foreach (var memberAccess in method.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                if (memberAccess.Expression is IdentifierNameSyntax expression
                    && instanceFields.Contains(expression.Identifier.ValueText))
                {
                    accessed.Add(expression.Identifier.ValueText);
                }
            }
            return accessed;
        }

        private static int Lcom1(IReadOnlyList<HashSet<string>> access)
        {
            if (access.Count <= 1) return 0;
            var disjoint = 0;
            for (var i = 0; i < access.Count; i++)
            for (var j = i + 1; j < access.Count; j++)
            {
                if (!access[i].Overlaps(access[j])) disjoint++;
            }
            return disjoint;
        }

        private static double LcomHs(
            int methodCount,
            int fieldCount,
            IReadOnlyList<HashSet<string>> access)
        {
            if (methodCount <= 1 || fieldCount == 0) return 0;
            var accessed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fields in access) accessed.UnionWith(fields);
            var sharedPairs = 0.0;
            for (var i = 0; i < access.Count; i++)
            for (var j = i + 1; j < access.Count; j++)
            {
                sharedPairs += access[i].Intersect(access[j]).Count();
            }
            var averageShared = 2.0 * sharedPairs / (methodCount * (methodCount - 1));
            var dissimilarity = 1.0 - (double)accessed.Count / fieldCount;
            return Math.Abs(1 - dissimilarity) < 1e-9 ? 0 : (averageShared - dissimilarity) / (1 - dissimilarity);
        }

        private static string QualifiedName(TypeDeclarationSyntax declaration, string separator)
        {
            var segments = new List<string> { declaration.Identifier.ValueText };
            var ns = string.Empty;
            foreach (var ancestor in declaration.Ancestors())
            {
                switch (ancestor)
                {
                    case TypeDeclarationSyntax outer:
                        segments.Insert(0, outer.Identifier.ValueText);
                        break;
                    case BaseNamespaceDeclarationSyntax namespaceDecl:
                        ns = namespaceDecl.Name.ToString() + (ns.Length == 0 ? "" : "." + ns);
                        break;
                }
            }
            var nested = string.Join(separator, segments);
            return ns.Length == 0 ? nested : ns + "." + nested;
        }

        private static string PackageRoot() =>
            Path.GetFullPath(Path.Combine(GeneratorsPaths.GeneratorsRoot(), ".."));

        private static List<string> PackageSourceFiles(string packageRoot) =>
            Directory.EnumerateFiles(packageRoot, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Generators~{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                .Where(file => !file.EndsWith(".g.cs", StringComparison.Ordinal))
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToList();

        private static List<string> PackageAsmdefs()
        {
            var packageRoot = PackageRoot();
            var sampleRoots = DeclaredSamplePaths(packageRoot)
                .Select(relative => Path.GetFullPath(Path.Combine(packageRoot, relative))
                    + Path.DirectorySeparatorChar)
                .ToList();

            return Directory.EnumerateFiles(packageRoot, "*.asmdef", SearchOption.AllDirectories)
                .Where(path => !sampleRoots.Any(root => path.StartsWith(root, StringComparison.Ordinal)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }

        private static List<string> DeclaredSamplePaths(string packageRoot)
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(packageRoot, "package.json")));
            return manifest.RootElement.TryGetProperty("samples", out var samples)
                ? samples.EnumerateArray()
                    .Where(sample => sample.TryGetProperty("path", out _))
                    .Select(sample => sample.GetProperty("path").GetString()!)
                    .ToList()
                : new List<string>();
        }
    }
}
