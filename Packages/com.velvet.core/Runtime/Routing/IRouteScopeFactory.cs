namespace Velvet
{
    /// <summary>
    /// Factory abstraction that builds route scopes.
    /// Decouples DI-framework-specific implementations (such as VContainer) from the framework core.
    /// </summary>
    public interface IRouteScopeFactory
    {
        /// <summary>
        /// Creates a scope for the given route.
        /// </summary>
        /// <param name="route">Route definition the scope is created for.</param>
        /// <param name="parent">Parent route's scope. null for the root route.</param>
        IRouteScope CreateScope(RouteDefinition? route, IRouteScope? parent);
    }
}
