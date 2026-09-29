using Microsoft.CodeAnalysis;

namespace Velvet.SourceGenerators.AutoDeps
{
    /// <summary>
    /// The instance fields and properties whose value can change between two renders that reach them through
    /// the same instance: VEL100 flags one captured by a hook lambda and missing from its deps, VEL012 one an
    /// instance <c>_Impl</c> reads.
    /// </summary>
    internal static class ReactiveInstanceMembers
    {
        internal static bool Contains(ISymbol? symbol) => symbol switch
        {
            // Static / const / readonly fields hold a value fixed after construction, so they are not reactive
            // between renders and must not be flagged (e.g. an injected `readonly IRepository _repo`).
            IFieldSymbol field => !field.IsStatic && !field.IsConst && !field.IsReadOnly,
            // Static and init-only properties hold a value fixed after construction and are excluded; a
            // getter-only property may be a computed reactive value, so it is still tracked.
            IPropertySymbol property => !property.IsStatic && !(property.SetMethod?.IsInitOnly ?? false),
            _ => false,
        };
    }
}
