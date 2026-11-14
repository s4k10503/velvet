using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Mono.Cecil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Velvet.CodeGen
{
    // Mono.Cecil IAssemblyResolver that resolves references from
    // ICompiledAssembly.References — paths supplied by the Unity
    // ILPostProcessor runner. The runner runs in a separate process (Unity.ILPP.Runner)
    // without access to the Editor's AppDomain resolver, so references must be
    // resolved by hand against the file paths the runner provided.
    internal sealed class PostProcessorAssemblyResolver : IAssemblyResolver
    {
        private readonly Dictionary<string, HashSet<string>> _referenceToPathMap;
        private readonly HashSet<string> _referenceDirectories;
        private readonly Dictionary<string, AssemblyDefinition> _cache = new();
        private readonly ICompiledAssembly _compiledAssembly;
        private AssemblyDefinition? _selfAssembly;

        public PostProcessorAssemblyResolver(ICompiledAssembly compiledAssembly)
        {
            _compiledAssembly = compiledAssembly;
            _referenceToPathMap = new Dictionary<string, HashSet<string>>();
            _referenceDirectories = new HashSet<string>();
            foreach (var reference in compiledAssembly.References)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(reference);
                if (!_referenceToPathMap.TryGetValue(assemblyName, out var fileList))
                {
                    fileList = new HashSet<string>();
                    _referenceToPathMap.Add(assemblyName, fileList);
                }
                fileList.Add(reference);
                var directory = Path.GetDirectoryName(reference);
                if (!string.IsNullOrEmpty(directory))
                {
                    _referenceDirectories.Add(directory);
                }
            }
        }

        public void Dispose()
        {
            foreach (var cached in _cache.Values)
            {
                cached.Dispose();
            }
            _cache.Clear();
        }

        public AssemblyDefinition? Resolve(AssemblyNameReference name)
            => Resolve(name, new ReaderParameters(ReadingMode.Deferred));

        public AssemblyDefinition? Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (name.Name == _compiledAssembly.Name)
            {
                return _selfAssembly;
            }

            var fileName = FindFile(name);
            if (fileName == null)
            {
                return null;
            }

            if (_cache.TryGetValue(fileName, out var cached))
            {
                return cached;
            }

            parameters.AssemblyResolver = this;

            var peStream = MemoryStreamFor(fileName);
            var pdbPath = fileName + ".pdb";
            if (File.Exists(pdbPath))
            {
                parameters.SymbolStream = MemoryStreamFor(pdbPath);
            }

            var assemblyDefinition = AssemblyDefinition.ReadAssembly(peStream, parameters);
            _cache.Add(fileName, assemblyDefinition);
            return assemblyDefinition;
        }

        public void AddAssemblyDefinitionBeingOperatedOn(AssemblyDefinition assemblyDefinition)
        {
            _selfAssembly = assemblyDefinition;
        }

        private string? FindFile(AssemblyNameReference name)
        {
            if (_referenceToPathMap.TryGetValue(name.Name, out var paths))
            {
                if (paths.Count == 1)
                {
                    using var enumerator = paths.GetEnumerator();
                    enumerator.MoveNext();
                    return enumerator.Current;
                }
                foreach (var path in paths)
                {
                    var onDiskAssemblyName = AssemblyName.GetAssemblyName(path);
                    if (onDiskAssemblyName.FullName == name.FullName)
                    {
                        return path;
                    }
                }
                // Returning null lets the caller emit a clean diagnostic instead of leaking
                // the resolver's stack trace through the outer try/catch.
                return null;
            }

            // ICompiledAssembly.References only contains direct references. Indirect
            // references (e.g. types referenced by a field on a directly-referenced type)
            // are looked up by scanning the directories of direct references.
            foreach (var parentDir in _referenceDirectories)
            {
                var candidate = Path.Combine(parentDir, name.Name + ".dll");
                if (File.Exists(candidate))
                {
                    if (!_referenceToPathMap.TryGetValue(name.Name, out var referencePaths))
                    {
                        referencePaths = new HashSet<string>();
                        _referenceToPathMap.Add(name.Name, referencePaths);
                    }
                    referencePaths.Add(candidate);
                    return candidate;
                }
            }

            return null;
        }

        private static MemoryStream MemoryStreamFor(string fileName)
        {
            return Retry(10, TimeSpan.FromSeconds(1), () =>
            {
                byte[] byteArray;
                using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byteArray = new byte[fs.Length];
                    var readLength = fs.Read(byteArray, 0, (int)fs.Length);
                    if (readLength != fs.Length)
                    {
                        throw new InvalidOperationException(
                            $"Truncated read of '{fileName}': expected {fs.Length} bytes, got {readLength}.");
                    }
                }
                return new MemoryStream(byteArray);
            });
        }

        private static MemoryStream Retry(int retryCount, TimeSpan waitTime, Func<MemoryStream> func)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return func();
                }
                catch (IOException)
                {
                    if (attempt >= retryCount) throw;
                    Thread.Sleep(waitTime);
                }
            }
        }
    }

    // Redirects System.Private.CoreLib references back to the corlib actually
    // referenced by the assembly being processed (mscorlib or netstandard).
    // Without this, Cecil's reflection-based imports drag in the runner's CoreCLR corlib
    // and break type resolution against the target assembly.
    internal sealed class PostProcessorReflectionImporterProvider : IReflectionImporterProvider
    {
        public IReflectionImporter GetReflectionImporter(ModuleDefinition module)
            => new PostProcessorReflectionImporter(module);
    }

    internal sealed class PostProcessorReflectionImporter : DefaultReflectionImporter
    {
        private const string SystemPrivateCoreLib = "System.Private.CoreLib";
        private readonly AssemblyNameReference? _correctCorlib;

        public PostProcessorReflectionImporter(ModuleDefinition module) : base(module)
        {
            _correctCorlib = null;
            foreach (var a in module.AssemblyReferences)
            {
                if (a.Name is "mscorlib" or "netstandard" or SystemPrivateCoreLib)
                {
                    _correctCorlib = a;
                    break;
                }
            }
        }

        public override AssemblyNameReference ImportReference(AssemblyName reference)
        {
            if (_correctCorlib != null && reference.Name == SystemPrivateCoreLib)
            {
                return _correctCorlib;
            }
            return base.ImportReference(reference);
        }
    }
}
