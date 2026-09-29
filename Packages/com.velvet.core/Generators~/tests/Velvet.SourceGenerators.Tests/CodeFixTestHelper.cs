using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Runs the fix through a real AdhocWorkspace + CodeFixProvider.RegisterCodeFixesAsync round-trip instead of
    /// diffing strings by hand, so the result reflects Roslyn's actual edit and formatting behavior.
    /// Stringifies the SyntaxRoot after applying the selected CodeAction.
    /// </summary>
    internal static class CodeFixTestHelper
    {
        public static async Task<string> ApplyCodeFixAsync(
            string userSource,
            DiagnosticAnalyzer analyzer,
            CodeFixProvider codeFixProvider,
            string codeActionTitle,
            string expectedDiagnosticId,
            bool formatDocument = true)
        {
            var (workspace, userDocId, actions) = await RegisterAsync(
                userSource, analyzer, codeFixProvider, expectedDiagnosticId).ConfigureAwait(false);

            var selectedAction = actions.FirstOrDefault(a => a.Title == codeActionTitle)
                ?? throw new InvalidOperationException(
                    $"CodeAction '{codeActionTitle}' not found. Available: {string.Join(", ", actions.Select(a => a.Title))}");

            var operations = await selectedAction.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (var op in operations)
            {
                op.Apply(workspace, CancellationToken.None);
            }

            var updatedDoc = workspace.CurrentSolution.GetDocument(userDocId)!;
            var formatted = formatDocument
                ? await Formatter.FormatAsync(updatedDoc, cancellationToken: CancellationToken.None).ConfigureAwait(false)
                : updatedDoc;
            var text = await formatted.GetTextAsync(CancellationToken.None).ConfigureAwait(false);
            return text.ToString();
        }

        /// <summary>
        /// The titles the provider registers against the first <paramref name="expectedDiagnosticId"/> the
        /// analyzer reports, which is how a case asserts that a diagnostic is left without a fix.
        /// </summary>
        public static async Task<IReadOnlyList<string>> RegisteredTitlesAsync(
            string userSource,
            DiagnosticAnalyzer analyzer,
            CodeFixProvider codeFixProvider,
            string expectedDiagnosticId)
        {
            var (_, _, actions) = await RegisterAsync(
                userSource, analyzer, codeFixProvider, expectedDiagnosticId).ConfigureAwait(false);
            return actions.Select(a => a.Title).ToList();
        }

        private static async Task<(AdhocWorkspace Workspace, DocumentId UserDocId, List<CodeAction> Actions)> RegisterAsync(
            string userSource,
            DiagnosticAnalyzer analyzer,
            CodeFixProvider codeFixProvider,
            string expectedDiagnosticId)
        {
            var workspace = new AdhocWorkspace(MefHostServices.Create(MefHostServices.DefaultAssemblies));
            var projectId = ProjectId.CreateNewId();
            var userDocId = DocumentId.CreateNewId(projectId);
            var stubDocId = DocumentId.CreateNewId(projectId);

            var solution = workspace.CurrentSolution
                .AddProject(projectId, "TestAssembly", "TestAssembly", LanguageNames.CSharp)
                .AddMetadataReferences(projectId, GetReferences())
                .WithProjectParseOptions(projectId, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))
                .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddDocument(userDocId, "User.cs", userSource)
                .AddDocument(stubDocId, "VelvetStub.cs", GeneratorTestHelper.VelvetStubSource);

            if (!workspace.TryApplyChanges(solution))
            {
                throw new InvalidOperationException("Failed to apply solution");
            }

            var document = workspace.CurrentSolution.GetDocument(userDocId)!;
            var compilation = await document.Project.GetCompilationAsync(CancellationToken.None).ConfigureAwait(false);
            var withAnalyzers = compilation!.WithAnalyzers(ImmutableArray.Create(analyzer));
            var analyzerDiags = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None).ConfigureAwait(false);
            var targetDiag = analyzerDiags.FirstOrDefault(d => d.Id == expectedDiagnosticId);
            if (targetDiag is null)
            {
                throw new InvalidOperationException(
                    $"No diagnostic with id {expectedDiagnosticId} was reported. Reported: {string.Join(", ", analyzerDiags.Select(d => d.Id))}");
            }

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(
                document,
                targetDiag,
                (action, _) => actions.Add(action),
                CancellationToken.None);
            await codeFixProvider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
            return (workspace, userDocId, actions);
        }

        private static IEnumerable<MetadataReference> GetReferences() =>
            GeneratorTestHelper.ReferenceAssemblies();
    }
}
