using Mono.Cecil;

namespace Velvet.CodeGen
{
    // Reading a definition off a reference, where a reference into an assembly the resolver cannot load costs the
    // part of the analysis that wanted the definition rather than the weave of the whole method.
    internal static class ScopeResolution
    {
        public static TypeDefinition? Type(TypeReference type)
        {
            try
            {
                return type.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return null;
            }
        }

        public static FieldDefinition? Field(FieldReference field)
        {
            try
            {
                return field.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return null;
            }
        }

        public static MethodDefinition? Method(MethodReference method)
        {
            try
            {
                return method.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return null;
            }
        }
    }
}
