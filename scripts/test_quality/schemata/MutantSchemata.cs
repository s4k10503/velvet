// Rewrites a campaign's mutants into one compilable tree, each guarded by a switch read at run time.
//
// Built and run by schemata_tool.py with the editor's own .NET runtime and Roslyn, so the parse and
// the compile check are the ones Unity's build uses. Input and output are JSON; mutation_check.py's
// schemata_request writes the input and measure_in_session reads the output.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Velvet.MutantSchemata
{
    internal sealed class MutantInput
    {
        public int id { get; set; }
        public int line { get; set; }
        public int start { get; set; }
        public int length { get; set; }
        public string replacement { get; set; } = "";
        public string @operator { get; set; } = "";
        public string before { get; set; } = "";
    }

    internal sealed class FileInput
    {
        public string path { get; set; } = "";
        public string assembly { get; set; } = "";
        public string text { get; set; } = "";
        public List<MutantInput> mutants { get; set; } = new();
    }

    internal sealed class Input
    {
        public string project { get; set; } = "";
        public string env { get; set; } = "";
        public Dictionary<string, string> assemblies { get; set; } = new();
        public List<FileInput> files { get; set; } = new();
        public int rounds { get; set; } = 8;
        public bool shapeRulesOff { get; set; } = true;
    }

    internal sealed class Site
    {
        public MutantInput Mutant = null!;
        public FileInput File = null!;
        public SyntaxNode Node = null!;
        public bool Statement;
        public string Mutated = "";
    }

    internal static class Program
    {
        private const string SwitchPrefix = "__VelvetMutantSwitch_";
        private static readonly HashSet<string> PostProcessorAssemblies = new() { "Unity.Velvet.CodeGen" };
        private const string Prologue = "#pragma warning disable VEL500, VEL501, VEL502\n#line 1\n";

        private static int Main(string[] argv)
        {
            var input = JsonSerializer.Deserialize<Input>(File.ReadAllText(argv[0]))!;
            var declined = new SortedDictionary<int, string>();
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var switches = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var roundsTaken = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var fatal = new List<string>();
            var clock = System.Diagnostics.Stopwatch.StartNew();

            foreach (var group in input.files.GroupBy(f => f.assembly))
            {
                if (!input.assemblies.TryGetValue(group.Key, out var rsp))
                {
                    DeclineEach(group, declined, "no compiler response file for assembly " + group.Key);
                    continue;
                }
                var result = RewriteAssembly(input, group.Key, rsp, group.ToList(), declined);
                if (result.Fatal != null)
                {
                    fatal.Add(group.Key + ": " + result.Fatal);
                    DeclineEach(group, declined, "the assembly could not be rewritten: " + result.Fatal);
                    continue;
                }
                foreach (var pair in result.Files) files[pair.Key] = pair.Value;
                if (result.Switch != null) switches[group.Key] = result.Switch;
                roundsTaken[group.Key] = result.Rounds;
            }

            var placed = input.files.SelectMany(f => f.mutants).Select(m => m.id)
                .Where(id => !declined.ContainsKey(id)).OrderBy(id => id).ToList();
            var output = new Dictionary<string, object>
            {
                ["placed"] = placed,
                ["declined"] = declined.ToDictionary(p => p.Key.ToString(), p => p.Value),
                ["files"] = files,
                ["switches"] = switches,
                ["rounds"] = roundsTaken,
                ["fatal"] = fatal,
                ["seconds"] = clock.Elapsed.TotalSeconds,
            };
            File.WriteAllText(argv[1], JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
            return fatal.Count == 0 ? 0 : 1;
        }

        private static void DeclineEach(IEnumerable<FileInput> files, SortedDictionary<int, string> declined, string reason)
        {
            foreach (var mutant in files.SelectMany(file => file.mutants).Where(mutant => !declined.ContainsKey(mutant.id)))
                declined[mutant.id] = reason;
        }

        internal sealed class AssemblyResult
        {
            public string? Fatal;
            public Dictionary<string, string> Files = new();
            public string? Switch;
            public int Rounds;
        }

        internal sealed class CompileContext
        {
            public CSharpParseOptions ParseOptions = null!;
            public List<MetadataReference> References = new();
            public CSharpCompilationOptions Options = null!;
            public List<ISourceGenerator> Generators = new();
            public ImmutableArray<DiagnosticAnalyzer> Analyzers = ImmutableArray<DiagnosticAnalyzer>.Empty;
            public ImmutableArray<AdditionalText> Additional = ImmutableArray<AdditionalText>.Empty;
            public List<(string path, string text)> Sources = new();
        }

        private static AssemblyResult RewriteAssembly(Input input, string assembly, string rsp,
                                                      List<FileInput> targets, SortedDictionary<int, string> declined)
        {
            var arguments = CSharpCommandLineParser.Default.Parse(
                new[] { "@" + Path.GetFullPath(Path.Combine(input.project, rsp)) }, input.project, null);
            var parseErrors = arguments.Errors.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (parseErrors.Count > 0)
                return new AssemblyResult { Fatal = "the response file did not parse: " + parseErrors[0].GetMessage() };
            var loader = new Loader();
            var analyzerReferences = arguments.AnalyzerReferences
                .Select(a => new AnalyzerFileReference(Full(input.project, a.FilePath), loader)).ToList();
            var context = new CompileContext
            {
                ParseOptions = arguments.ParseOptions,
                References = arguments.MetadataReferences.Select(r =>
                    (MetadataReference)MetadataReference.CreateFromFile(Full(input.project, r.Reference), r.Properties)).ToList(),
                Options = arguments.CompilationOptions,
                Generators = analyzerReferences.SelectMany(a => a.GetGenerators(LanguageNames.CSharp)).ToList(),
                Analyzers = analyzerReferences.SelectMany(a => a.GetAnalyzers(LanguageNames.CSharp)).ToImmutableArray(),
                Additional = arguments.AdditionalFiles
                    .Select(f => (AdditionalText)new FileText(Full(input.project, f.Path))).ToImmutableArray(),
            };
            var byPath = targets.ToDictionary(t => Norm(Full(input.project, t.path)), t => t);
            foreach (var source in arguments.SourceFiles)
            {
                var full = Norm(source.Path);
                context.Sources.Add((full, byPath.TryGetValue(full, out var target) ? target.text : File.ReadAllText(full)));
            }
            foreach (var target in byPath.Keys)
            {
                if (!context.Sources.Any(s => s.path == target))
                    return new AssemblyResult { Fatal = "the response file does not compile " + target + "; run the baseline first" };
            }
            return RewriteSources(input, assembly, context, targets, declined);
        }

        // Everything after the response file: the pristine compile, placing each site, and the rounds that
        // decline what a guard breaks. Separate so the placement can be run over sources a test hands it.
        internal static AssemblyResult RewriteSources(Input input, string assembly, CompileContext context,
                                                      List<FileInput> targets, SortedDictionary<int, string> declined)
        {
            var result = new AssemblyResult();
            var parseOptions = context.ParseOptions;
            var sources = context.Sources;

            // The unmutated tree has to compile clean here, or an error below cannot be blamed on a mutant.
            var pristine = Compile(sources, context, assembly);
            var pristineErrors = Errors(pristine.diagnostics).ToList();
            if (pristineErrors.Count > 0)
            {
                result.Fatal = "the unmutated assembly does not compile here: " + pristineErrors[0];
                return result;
            }

            // Locate every site on the unmutated trees, with the unmutated semantic model for the type check.
            var sites = new List<Site>();
            foreach (var target in targets)
            {
                var full = Norm(Full(input.project, target.path));
                var tree = pristine.compilation.SyntaxTrees.First(t => Norm(t.FilePath) == full);
                foreach (var mutant in target.mutants)
                {
                    if (PostProcessorAssemblies.Contains(assembly))
                    {
                        declined[mutant.id] = "the assembly runs inside the compiler's post-processor, not the editor";
                        continue;
                    }
                    var reason = Locate(target, mutant, tree, pristine.compilation, parseOptions, out var site);
                    if (reason != null) declined[mutant.id] = reason;
                    else sites.Add(site!);
                }
            }

            for (var round = 1; round <= input.rounds; round++)
            {
                result.Rounds = round;
                var live = sites.Where(s => !declined.ContainsKey(s.Mutant.id)).ToList();
                var rewritten = new Dictionary<string, (string text, List<(int id, TextSpan wrap, TextSpan mutated, TextSpan member)> spans)>();
                string? switchName = null;
                string? host = null;
                foreach (var target in targets.OrderBy(t => t.path, StringComparer.Ordinal))
                {
                    var full = Norm(Full(input.project, target.path));
                    var mine = live.Where(s => s.File == target).ToList();
                    if (mine.Count == 0) continue;
                    if (host == null)
                    {
                        host = full;
                        switchName = SwitchName(target.text, parseOptions, assembly);
                    }
                    rewritten[full] = Rewrite(target.text, parseOptions, mine, switchName!,
                                              host == full ? SwitchDeclaration(assembly, input.env) : null, input.shapeRulesOff);
                }
                if (rewritten.Count == 0)
                {
                    return result;
                }
                var mutatedSources = sources.Select(s => rewritten.TryGetValue(s.path, out var r) ? (s.path, r.text) : s).ToList();
                var compiled = Compile(mutatedSources, context, assembly);
                var errors = Errors(compiled.diagnostics).ToList();
                if (errors.Count == 0)
                {
                    foreach (var pair in rewritten) result.Files[Relative(input.project, pair.Key)] = pair.Value.text;
                    result.Switch = switchName + ".Active";
                    return result;
                }
                var fatal = Blame(errors, rewritten, declined);
                if (fatal != null)
                {
                    result.Fatal = fatal;
                    return result;
                }
            }
            result.Fatal = "still not compiling after " + input.rounds + " rounds";
            return result;
        }

        // What placing one mutant reads: the unmutated tree and its model, and where the edit lands.
        private sealed class Placing
        {
            public FileInput File = null!;
            public MutantInput Mutant = null!;
            public SyntaxTree Tree = null!;
            public Compilation Compilation = null!;
            public SemanticModel Model = null!;
            public CSharpParseOptions Options = null!;
            public SourceText Text = null!;
            public int EditStart;
            public TextSpan Edit => new(EditStart, Mutant.length);
        }

        // Declines the mutant each error lands in: its mutated branch, else the innermost guard holding it,
        // else every mutant of the member holding it. Returns why the assembly cannot go on, or null.
        private static string? Blame(List<Diagnostic> errors,
                                     Dictionary<string, (string text, List<(int id, TextSpan wrap, TextSpan mutated, TextSpan member)> spans)> rewritten,
                                     SortedDictionary<int, string> declined)
        {
            foreach (var error in errors)
            {
                var location = error.Location;
                if (!rewritten.TryGetValue(Norm(location.SourceTree?.FilePath ?? ""), out var r))
                    return "an error outside every mutated file: " + error;
                var ids = Blamed(r.spans, location.SourceSpan);
                if (ids.Count == 0) return "an error no mutant accounts for: " + error;
                foreach (var id in ids.Where(id => !declined.ContainsKey(id)))
                    declined[id] = "the guarded form does not compile: " + error.Id + " " + error.GetMessage();
            }
            return null;
        }

        private static List<int> Blamed(List<(int id, TextSpan wrap, TextSpan mutated, TextSpan member)> spans, TextSpan at)
        {
            var inMutated = spans.Where(s => s.mutated.Contains(at)).ToList();
            if (inMutated.Count > 0) return inMutated.OrderBy(s => s.mutated.Length).Take(1).Select(s => s.id).ToList();
            var inWrap = spans.Where(s => s.wrap.Contains(at)).ToList();
            if (inWrap.Count > 0)
            {
                var innermost = inWrap.Min(s => s.wrap.Length);
                return inWrap.Where(s => s.wrap.Length == innermost).Select(s => s.id).ToList();
            }
            return spans.Where(s => s.member.Contains(at)).Select(s => s.id).ToList();
        }

        private static string? Locate(FileInput file, MutantInput mutant, SyntaxTree tree, Compilation compilation,
                                      CSharpParseOptions parseOptions, out Site? site)
        {
            site = null;
            var text = tree.GetText();
            if (mutant.line < 1 || mutant.line > text.Lines.Count) return "the line is outside the file";
            var placing = new Placing
            {
                File = file, Mutant = mutant, Tree = tree, Compilation = compilation, Model = compilation.GetSemanticModel(tree),
                Options = parseOptions, Text = text, EditStart = text.Lines[mutant.line - 1].Start + mutant.start,
            };
            if (mutant.@operator is "line removed" or "guard removed") return LocateStatement(placing, out site);
            var start = StartOf(placing, out var missing);
            if (missing != null) return missing;
            if (start == null) return "the mutated operator is not an expression of its own here";
            if (InComponentBody(start)) return "the expression is in a [Component] body the weaver reads";
            return Climb(placing, start, out site);
        }

        private static string? LocateStatement(Placing placing, out Site? site)
        {
            site = null;
            var statement = placing.Tree.GetRoot().FindNode(placing.Edit, getInnermostNodeForTie: true)
                .AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault(s => s.Span == placing.Edit);
            if (statement == null) return "the removed text is not one whole statement";
            if (statement is LocalDeclarationStatementSyntax or LocalFunctionStatementSyntax or LabeledStatementSyntax)
                return "the removed statement declares a name";
            if (InComponentBody(statement)) return "the statement is in a [Component] body the weaver reads";
            site = new Site { Mutant = placing.Mutant, File = placing.File, Node = statement, Statement = true };
            return null;
        }

        // The expression the edit is made in: the one holding a removed clause, the literal, or the binary
        // expression whose operator token is the one mutated.
        private static SyntaxNode? StartOf(Placing placing, out string? missing)
        {
            missing = null;
            var root = placing.Tree.GetRoot();
            var mutant = placing.Mutant;
            if (mutant.@operator is "clause removed")
                return root.FindNode(placing.Edit).AncestorsAndSelf().OfType<ExpressionSyntax>()
                    .FirstOrDefault(e => e.Span.Contains(placing.Edit));
            var literal = mutant.@operator == "literal";
            var at = literal ? placing.EditStart : placing.EditStart + 1;
            var token = root.FindToken(at);
            if (token.SpanStart != at || token.Text != mutant.before)
            {
                missing = "no '" + mutant.before + "' token at the mutant";
                return null;
            }
            if (literal) return token.Parent as LiteralExpressionSyntax;
            return token.Parent is BinaryExpressionSyntax binary && binary.OperatorToken == token ? binary : null;
        }

        // Climbs from the edited expression until substituting the edit there parses as the direct mutant
        // does, so the guarded branch is the textual mutant rather than a node swap of different precedence.
        private static string? Climb(Placing placing, SyntaxNode start, out Site? site)
        {
            site = null;
            var mutant = placing.Mutant;
            var text = placing.Text.ToString();
            var direct = text.Remove(placing.EditStart, mutant.length).Insert(placing.EditStart, mutant.replacement);
            var canonicalDirect = Canonical(CSharpSyntaxTree.ParseText(direct, placing.Options).GetRoot());
            for (var node = start; node is BinaryExpressionSyntax or ParenthesizedExpressionSyntax or LiteralExpressionSyntax
                     or PrefixUnaryExpressionSyntax; node = node.Parent!)
            {
                var offset = placing.EditStart - node.SpanStart;
                if (offset < 0 || offset + mutant.length > node.Span.Length) continue;
                var mutated = node.ToString().Remove(offset, mutant.length).Insert(offset, mutant.replacement);
                var candidate = text.Remove(node.SpanStart, node.Span.Length).Insert(node.SpanStart, "(" + mutated + ")");
                var candidateTree = CSharpSyntaxTree.ParseText(candidate, placing.Options, placing.Tree.FilePath);
                if (Canonical(candidateTree.GetRoot()) != canonicalDirect) continue;
                var why = TypeMismatch(node, placing.Model, placing.Compilation, placing.Tree, candidateTree)
                          ?? DirectMutantError(placing.Compilation, placing.Tree, candidateTree)
                          ?? ConstantRebinds(node, mutated, placing.Model, placing.Compilation, placing.Tree, placing.Options);
                if (why != null) return why;
                if (InExpressionTree(node, placing.Model)) return "the expression is inside an expression tree";
                if (mutated.Contains('\n') && !SingleLine(mutated, placing.Options)) return "the mutated expression spans lines it cannot be folded onto";
                site = new Site { Mutant = mutant, File = placing.File, Node = node, Mutated = Fold(mutated, placing.Options) };
                return null;
            }
            return "no enclosing expression parses as the direct mutant does";
        }

        // The weaver reads a [Component] body's own IL to decide whether to memoize it, and a guard is IL it
        // did not see in the unmutated build; a lambda or local function inside one is a body of its own.
        private static bool InComponentBody(SyntaxNode node)
        {
            foreach (var ancestor in node.Ancestors())
            {
                if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax) return false;
                if (ancestor is MethodDeclarationSyntax method)
                    return method.AttributeLists.SelectMany(list => list.Attributes)
                        .Any(attribute => attribute.Name.ToString().Split('.').Last() is "Component" or "ComponentAttribute");
            }
            return false;
        }

        private static string? TypeMismatch(SyntaxNode node, SemanticModel model, Compilation compilation,
                                            SyntaxTree tree, SyntaxTree candidateTree)
        {
            var before = model.GetTypeInfo(node).Type;
            var replaced = compilation.ReplaceSyntaxTree(tree, candidateTree);
            var after = replaced.GetSemanticModel(candidateTree);
            var parenthesis = candidateTree.GetRoot().FindToken(node.SpanStart).Parent as ParenthesizedExpressionSyntax;
            if (parenthesis == null) return "the mutated expression could not be found again";
            var mutatedType = after.GetTypeInfo(parenthesis).Type;
            if (before == null || mutatedType == null || before.TypeKind == TypeKind.Error || mutatedType.TypeKind == TypeKind.Error)
                return "the mutated expression has no type";
            if (!SymbolEqualityComparer.Default.Equals(before, mutatedType))
                return "the mutated expression's type differs: " + before.ToDisplayString() + " -> " + mutatedType.ToDisplayString();
            return null;
        }

        // The guarded file is what gets compiled, so a direct mutant that would not compile -- a constant
        // narrowed out of range once the operator flips -- would otherwise be measured rather than named
        // uncompilable by its own launch.
        private static string? DirectMutantError(Compilation compilation, SyntaxTree tree, SyntaxTree candidateTree)
        {
            var replaced = compilation.ReplaceSyntaxTree(tree, candidateTree);
            var error = replaced.GetSemanticModel(candidateTree).GetDiagnostics()
                .FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            return error == null ? null : "the direct mutant does not compile: " + error.Id + " " + error.GetMessage();
        }

        // A guard makes a constant operand non-constant, which takes away the implicit constant conversion
        // an overload may have been chosen by: both the unmutated and the mutated operand are bound with and
        // without a guard around them, and the site is declined where the call they sit in changes.
        private static string? ConstantRebinds(SyntaxNode node, string mutated, SemanticModel model,
                                               Compilation compilation, SyntaxTree tree, CSharpParseOptions options)
        {
            var constant = model.GetConstantValue(node);
            if (!constant.HasValue || constant.Value is null or bool or string) return null;
            var call = node.Ancestors().TakeWhile(a => a is not StatementSyntax)
                .OfType<ArgumentSyntax>().FirstOrDefault()?.Parent?.Parent;
            if (call == null) return null;
            foreach (var operand in new[] { node.ToString(), mutated })
            {
                var plain = Bound(call, node, "(" + operand + ")", compilation, tree, options);
                var guarded = Bound(call, node, "(System.Environment.TickCount == int.MinValue ? (" + operand + ") : (" +
                                                operand + "))", compilation, tree, options);
                if (plain != guarded)
                    return "a guard makes the constant operand non-constant, and the call binds " + guarded + " rather than " + plain;
            }
            return null;
        }

        private static string Bound(SyntaxNode call, SyntaxNode node, string replacement, Compilation compilation,
                                    SyntaxTree tree, CSharpParseOptions options)
        {
            var text = tree.GetText().ToString();
            var candidate = text.Remove(node.SpanStart, node.Span.Length).Insert(node.SpanStart, replacement);
            var candidateTree = CSharpSyntaxTree.ParseText(candidate, options, tree.FilePath);
            var replaced = compilation.ReplaceSyntaxTree(tree, candidateTree);
            var found = candidateTree.GetRoot().FindToken(call.SpanStart).Parent?.AncestorsAndSelf()
                .FirstOrDefault(n => n.RawKind == call.RawKind && n.SpanStart == call.SpanStart);
            if (found == null) return "nothing";
            return replaced.GetSemanticModel(candidateTree).GetSymbolInfo(found).Symbol?.ToDisplayString() ?? "nothing";
        }

        private static bool InExpressionTree(SyntaxNode node, SemanticModel model)
        {
            foreach (var lambda in node.Ancestors().OfType<AnonymousFunctionExpressionSyntax>())
            {
                var converted = model.GetTypeInfo(lambda).ConvertedType;
                if (converted != null && converted.Name == "Expression" &&
                    converted.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions") return true;
            }
            return false;
        }

        // The expression with every line break and comment between its tokens made a space, so a
        // multi-line original adds no line to the file. Tokens are kept whole: joining them with spaces
        // would put spaces inside an interpolated string's text.
        private static string Fold(string expression, CSharpParseOptions options)
        {
            if (!expression.Contains('\n')) return expression;
            var parsed = SyntaxFactory.ParseExpression(expression, 0, options);
            var builder = new StringBuilder();
            foreach (var token in parsed.DescendantTokens())
            {
                Trivia(token.LeadingTrivia, builder);
                builder.Append(token.Text);
                Trivia(token.TrailingTrivia, builder);
            }
            return builder.ToString();

            static void Trivia(SyntaxTriviaList list, StringBuilder into)
            {
                foreach (var trivia in list)
                {
                    if (trivia.IsKind(SyntaxKind.WhitespaceTrivia)) into.Append(trivia.ToFullString());
                    else if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)) into.Append(' ');
                }
            }
        }

        private static bool SingleLine(string expression, CSharpParseOptions options)
        {
            var parsed = SyntaxFactory.ParseExpression(expression, 0, options);
            return parsed.DescendantTokens().All(t => !t.Text.Contains('\n'))
                && !parsed.DescendantTrivia().Any(t => t.IsDirective);
        }

        private static (string text, List<(int id, TextSpan wrap, TextSpan mutated, TextSpan member)> spans) Rewrite(
            string text, CSharpParseOptions options, List<Site> sites, string switchName, string? declaration,
            bool shapeRulesOff)
        {
            var tree = CSharpSyntaxTree.ParseText(text, options);
            var root = tree.GetRoot();
            // Sites were located on the pristine compilation's tree, which is this text parsed the same way.
            var byNode = new Dictionary<SyntaxNode, List<Site>>();
            foreach (var site in sites)
            {
                var node = root.FindNode(site.Node.Span, getInnermostNodeForTie: true)
                    .AncestorsAndSelf().First(n => n.Span == site.Node.Span && n.RawKind == site.Node.RawKind);
                if (!byNode.TryGetValue(node, out var list)) byNode[node] = list = new List<Site>();
                list.Add(site);
            }
            var rewritten = root.ReplaceNodes(byNode.Keys, (original, current) =>
            {
                var inner = current.WithoutTrivia().ToFullString();
                foreach (var site in byNode[original].OrderBy(s => s.Mutant.id))
                {
                    inner = site.Statement
                        ? "{ if (" + switchName + ".Active != " + site.Mutant.id + ") " + inner + " }"
                        : "(" + switchName + ".Active == " + site.Mutant.id + " ? (" + site.Mutated + ") : (" + inner + "))";
                }
                var replacement = site0Statement(byNode[original])
                    ? (SyntaxNode)SyntaxFactory.ParseStatement(inner, 0, options)
                    : SyntaxFactory.ParseExpression(inner, 0, options);
                return replacement.WithLeadingTrivia(original.GetLeadingTrivia()).WithTrailingTrivia(original.GetTrailingTrivia());
            });
            // The code-shape analyzers count every guard as a branch and every statement guard as a level,
            // so they are off in a rewritten file; `#line 1` keeps each line's reported number its own.
            var result = (shapeRulesOff ? Prologue : "") + rewritten.ToFullString();
            if (declaration != null) result = result + declaration;
            var spans = FindSpans(result, options, switchName, sites);
            return (result, spans);

            static bool site0Statement(List<Site> list) => list[0].Statement;
        }

        // Where each mutant's guard and mutated branch landed, read back off the text by the guard's spelling.
        private static List<(int id, TextSpan wrap, TextSpan mutated, TextSpan member)> FindSpans(
            string text, CSharpParseOptions options, string switchName, List<Site> sites)
        {
            var tree = CSharpSyntaxTree.ParseText(text, options);
            var root = tree.GetRoot();
            var found = new List<(int, TextSpan, TextSpan, TextSpan)>();
            foreach (var site in sites)
            {
                var id = site.Mutant.id;
                SyntaxNode? wrap = null;
                TextSpan mutated = default;
                if (site.Statement)
                {
                    var guard = root.DescendantNodes().OfType<IfStatementSyntax>().FirstOrDefault(i =>
                        i.Condition is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.NotEqualsExpression)
                        && b.Left.ToString() == switchName + ".Active" && b.Right.ToString() == id.ToString());
                    wrap = guard?.Parent;
                    mutated = guard == null ? default : new TextSpan(guard.SpanStart, 0);
                }
                else
                {
                    var conditional = root.DescendantNodes().OfType<ConditionalExpressionSyntax>().FirstOrDefault(c =>
                        c.Condition is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.EqualsExpression)
                        && b.Left.ToString() == switchName + ".Active" && b.Right.ToString() == id.ToString());
                    wrap = conditional?.Parent;
                    mutated = conditional?.WhenTrue.Span ?? default;
                }
                if (wrap == null) continue;
                var member = wrap.AncestorsAndSelf().FirstOrDefault(n => n is MemberDeclarationSyntax or AccessorDeclarationSyntax
                    or LocalFunctionStatementSyntax);
                found.Add((id, wrap.Span, mutated, member?.Span ?? wrap.Span));
            }
            return found;
        }

        private static string SwitchName(string text, CSharpParseOptions options, string assembly)
        {
            var root = CSharpSyntaxTree.ParseText(text, options).GetRoot();
            var scoped = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
            var type = SwitchPrefix + Sanitize(assembly);
            return scoped == null ? "global::" + type : "global::" + scoped.Name + "." + type;
        }

        private static string SwitchDeclaration(string assembly, string env) =>
            "\ninternal static class " + SwitchPrefix + Sanitize(assembly) + "\n{\n" +
            "    internal static int Active = Read();\n" +
            "    private static int Read() => int.TryParse(System.Environment.GetEnvironmentVariable(\"" + env +
            "\"), out var id) ? id : 0;\n}\n";

        private static string Sanitize(string name) => new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        internal static string Canonical(SyntaxNode root)
        {
            var builder = new StringBuilder();
            void Walk(SyntaxNodeOrToken item)
            {
                if (item.IsToken)
                {
                    builder.Append(item.AsToken().Text).Append(' ');
                    return;
                }
                var node = item.AsNode()!;
                if (node is ParenthesizedExpressionSyntax parenthesized)
                {
                    Walk(parenthesized.Expression);
                    return;
                }
                builder.Append('<').Append(node.Kind()).Append(' ');
                foreach (var child in node.ChildNodesAndTokens()) Walk(child);
                builder.Append('>');
            }
            Walk(root);
            return builder.ToString();
        }

        private static (CSharpCompilation compilation, ImmutableArray<Diagnostic> diagnostics) Compile(
            List<(string path, string text)> sources, CompileContext context, string assembly)
        {
            var trees = sources.Select(s => CSharpSyntaxTree.ParseText(SourceText.From(s.text, Encoding.UTF8), context.ParseOptions, s.path)).ToList();
            var compilation = CSharpCompilation.Create(assembly, trees, context.References, context.Options);
            var provider = new EmptyOptionsProvider();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(context.Generators, context.Additional, context.ParseOptions, provider);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var generatorDiagnostics);
            var diagnostics = (context.Analyzers.IsEmpty
                ? generated.GetDiagnostics()
                : generated.WithAnalyzers(context.Analyzers, new AnalyzerOptions(context.Additional, provider))
                    .GetAllDiagnosticsAsync(CancellationToken.None).Result).AddRange(generatorDiagnostics);
            return ((CSharpCompilation)generated, diagnostics);
        }

        private static IEnumerable<Diagnostic> Errors(ImmutableArray<Diagnostic> diagnostics) =>
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && !d.IsSuppressed);

        private static string Full(string project, string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(project, path));

        private static string Norm(string path) => Path.GetFullPath(path).Replace('\\', '/');

        private static string Relative(string project, string full) => Path.GetRelativePath(project, full).Replace('\\', '/');

        private sealed class Loader : IAnalyzerAssemblyLoader
        {
            private readonly List<string> directories = new();

            public Loader()
            {
                AssemblyLoadContext.Default.Resolving += (context, name) =>
                {
                    foreach (var directory in directories)
                    {
                        var candidate = Path.Combine(directory, name.Name + ".dll");
                        if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
                    }
                    return null;
                };
            }

            public void AddDependencyLocation(string fullPath)
            {
                var directory = Path.GetDirectoryName(fullPath)!;
                if (!directories.Contains(directory)) directories.Add(directory);
            }

            public Assembly LoadFromPath(string fullPath)
            {
                AddDependencyLocation(fullPath);
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
            }
        }

        private sealed class FileText : AdditionalText
        {
            private readonly string path;
            public FileText(string path) => this.path = path;
            public override string Path => path;
            public override SourceText GetText(CancellationToken cancellationToken = default) =>
                SourceText.From(File.Exists(path) ? File.ReadAllText(path) : "", Encoding.UTF8);
        }

        private sealed class EmptyOptionsProvider : AnalyzerConfigOptionsProvider
        {
            private static readonly EmptyOptions Empty = new();
            public override AnalyzerConfigOptions GlobalOptions => Empty;
            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;
            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;

            private sealed class EmptyOptions : AnalyzerConfigOptions
            {
                public override bool TryGetValue(string key, out string value)
                {
                    value = null!;
                    return false;
                }
            }
        }
    }
}
