using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Velvet.SourceGenerators.Shared
{
    /// <summary>
    /// Which <c>_Impl</c> overloads the wrapper <c>[MemoizeMethod]</c> generates can call: it passes its own
    /// parameters in order and its own type parameters as type arguments.
    /// </summary>
    internal static class MemoizeImplSignature
    {
        internal const string Suffix = "_Impl";

        internal static bool Matches(IMethodSymbol wrapper, IMethodSymbol impl)
        {
            if (wrapper.TypeParameters.Length != impl.TypeParameters.Length)
            {
                return false;
            }
            var called = impl.TypeParameters.IsEmpty
                ? wrapper
                : wrapper.Construct(impl.TypeParameters.CastArray<ITypeSymbol>().ToArray());
            return called.Parameters.Select(p => p.Type)
                .SequenceEqual(impl.Parameters.Select(p => p.Type), SymbolEqualityComparer.Default);
        }
    }
}
