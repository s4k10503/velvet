#nullable enable
using System;

namespace Velvet
{
    /// <summary>
    /// An error a route renders with a status, as React Router's <c>ErrorResponse</c>: the 404 a navigation to a
    /// path no route matches commits. <c>Hooks.UseRouteError</c> returns it to the <c>errorElement</c> that
    /// renders it.
    /// </summary>
    public sealed class ErrorResponse : Exception
    {
        public ErrorResponse(int status, string statusText, object? detail = null)
            : base($"{status} {statusText}")
        {
            Status = status;
            StatusText = statusText;
            Detail = detail;
        }

        /// <summary>The HTTP-style status: 404 for a path no route matched.</summary>
        public int Status { get; }

        /// <summary>The status's reason phrase: <c>"Not Found"</c> for a 404.</summary>
        public string StatusText { get; }

        /// <summary>What the response carries; for a 404, the message naming the path that matched nothing.</summary>
        public object? Detail { get; }

        internal static ErrorResponse NotFound(string path) =>
            new(404, "Not Found", $"No route matches URL \"{path}\"");
    }
}
