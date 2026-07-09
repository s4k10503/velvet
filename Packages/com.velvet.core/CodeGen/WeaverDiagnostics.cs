using Mono.Cecil;

namespace Velvet.CodeGen
{
    // Shared support for the two ILPP weavers (CompilerWeaver, MetadataRegistrationWeaver). Both hit the
    // identical resolution-failure shape when the Velvet runtime members they inject calls to cannot be
    // resolved from the processed module — a stale or duplicate copy of the Velvet assembly defeating the
    // post-processor's resolver — and both fell back to the same remediation sentence and the same
    // referenced-assembly type lookup before this was factored out.
    internal static class WeaverDiagnostics
    {
        // Formats the warning a weaver emits when its TryResolve fails: the assembly name (so the outcome
        // is distinguishable from "nothing needed weaving/registering"), featureLabel names what is
        // disabled (e.g. "auto-memoization"), resolutionFailure is the specific type/method that could not
        // be resolved, and whatBreaks is the weaver-specific consequence sentence (what silently stops
        // working as a result). The trailing remediation sentence is identical for every weaver.
        public static string FormatResolutionFailureWarning(
            ModuleDefinition module, string featureLabel, string resolutionFailure, string whatBreaks)
        {
            return $"Velvet {featureLabel} is disabled for assembly '{module.Assembly.Name.Name}': "
                + resolutionFailure
                + " " + whatBreaks
                + " Check for stale or duplicate copies of the Velvet assembly that prevent the IL"
                + " post-processor from resolving it, then recompile.";
        }

        // Resolves fullName from one of module's referenced assemblies. Used when the type is not declared
        // in module itself (the case for every assembly except Velvet, which declares its own runtime
        // types directly). Returns null when no referenced assembly resolves or none declares the type.
        public static TypeDefinition? ResolveExternal(ModuleDefinition module, string fullName)
        {
            foreach (var asmRef in module.AssemblyReferences)
            {
                var asm = module.AssemblyResolver.Resolve(asmRef);
                if (asm == null) continue;
                var t = asm.MainModule.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
