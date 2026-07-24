using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Velvet.CodeGen
{
    // ILPostProcessor host that delegates to CompilerWeaver (inner auto-memoization of analyzable
    // [Component] bodies) and MetadataRegistrationWeaver (Error Boundary / props-bail / DisplayName
    // registration calls). The assembly is rewritten only when either weaver reports a change;
    // otherwise the original assembly is passed through untouched. The ILPostProcessor runs in the
    // external Unity.ILPP.Runner process, so UnityEngine.Debug.Log is unavailable. All diagnostics
    // are surfaced through DiagnosticMessage.
    internal sealed class VelvetCompilerILPostProcessor : ILPostProcessor
    {
        private const string VelvetAssemblyName = "Velvet";

        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            if (compiledAssembly.Name.EndsWith(".CodeGen.Tests", StringComparison.Ordinal))
            {
                return false;
            }
            if (compiledAssembly.InMemoryAssembly.PeData == null
                || compiledAssembly.InMemoryAssembly.PdbData == null)
            {
                return false;
            }
            if (compiledAssembly.Name == VelvetAssemblyName)
            {
                return true;
            }
            foreach (var reference in compiledAssembly.References)
            {
                if (Path.GetFileNameWithoutExtension(reference) == VelvetAssemblyName)
                {
                    return true;
                }
            }
            return false;
        }

        public override ILPostProcessResult? Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();
            AssemblyDefinition assemblyDefinition;
            try
            {
                assemblyDefinition = AssemblyDefinitionFor(compiledAssembly);
            }
            catch (BadImageFormatException)
            {
                return new ILPostProcessResult(null, diagnostics);
            }

            using (assemblyDefinition)
            {
                bool madeAnyChange;
                try
                {
                    var memoWoven = CompilerWeaver.Weave(assemblyDefinition.MainModule, diagnostics);
                    var metadataWoven = MetadataRegistrationWeaver.Weave(assemblyDefinition.MainModule, diagnostics);
                    madeAnyChange = memoWoven || metadataWoven;
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new DiagnosticMessage
                    {
                        DiagnosticType = DiagnosticType.Error,
                        MessageData = $"VelvetCompilerILPostProcessor threw: {ex}",
                    });
                    return new ILPostProcessResult(null, diagnostics);
                }

                if (!madeAnyChange)
                {
                    return new ILPostProcessResult(null, diagnostics);
                }

                foreach (var d in diagnostics)
                {
                    if (d.DiagnosticType == DiagnosticType.Error)
                    {
                        return new ILPostProcessResult(null, diagnostics);
                    }
                }

                using var pe = new MemoryStream();
                using var pdb = new MemoryStream();
                var writerParameters = new WriterParameters
                {
                    SymbolWriterProvider = new Mono.Cecil.Cil.PortablePdbWriterProvider(),
                    SymbolStream = pdb,
                    WriteSymbols = true,
                };
                assemblyDefinition.Write(pe, writerParameters);
                return new ILPostProcessResult(
                    new InMemoryAssembly(pe.ToArray(), pdb.ToArray()),
                    diagnostics);
            }
        }

        private static AssemblyDefinition AssemblyDefinitionFor(ICompiledAssembly compiledAssembly)
        {
            var resolver = new PostProcessorAssemblyResolver(compiledAssembly);
            var readerParameters = new ReaderParameters
            {
                SymbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData),
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                AssemblyResolver = resolver,
                ReflectionImporterProvider = new PostProcessorReflectionImporterProvider(),
                ReadingMode = ReadingMode.Immediate,
                ReadSymbols = true,
            };
            var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData);
            var assemblyDefinition = AssemblyDefinition.ReadAssembly(peStream, readerParameters);
            resolver.AddAssemblyDefinitionBeingOperatedOn(assemblyDefinition);
            return assemblyDefinition;
        }
    }
}
