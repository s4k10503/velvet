using System;

namespace Velvet
{
    /// <summary>
    /// DI container abstraction for a route scope. Resolves services on a per-route basis;
    /// the scope is destroyed by Dispose when navigation leaves the route.
    /// </summary>
    public interface IRouteScope : IDisposable
    {
        /// <summary>Resolves a service of type <typeparamref name="T"/> from this scope.</summary>
        T Resolve<T>();
    }
}
