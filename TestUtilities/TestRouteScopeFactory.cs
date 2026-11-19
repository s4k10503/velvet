using System;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Test double for <see cref="IRouteScopeFactory"/>. Records both the last scope it created and
    /// how many times it was invoked, so a single fixture can assert on either probe without needing
    /// its own copy of the double.
    /// </summary>
    public sealed class TestRouteScopeFactory : IRouteScopeFactory
    {
        public int CreateScopeCount { get; private set; }

        public TestRouteScope? LastScope { get; private set; }

        public IRouteScope CreateScope(RouteDefinition? route, IRouteScope? parent)
        {
            CreateScopeCount++;
            LastScope = new TestRouteScope();
            return LastScope;
        }
    }

    /// <summary>
    /// Test double for <see cref="IRouteScope"/>. Resolve always throws since no fixture exercises
    /// DI resolution through this scope; Dispose flips <see cref="IsDisposed"/> so tests can assert
    /// the scope was torn down.
    /// </summary>
    public sealed class TestRouteScope : IRouteScope
    {
        public bool IsDisposed { get; private set; }

        public T Resolve<T>() => throw new NotImplementedException();

        public void Dispose() => IsDisposed = true;
    }
}
