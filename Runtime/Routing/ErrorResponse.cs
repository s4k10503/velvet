#nullable enable
using System;

namespace Velvet
{
    /// <summary>
    /// An error a route renders with a status, as React Router's <c>ErrorResponse</c>. The router makes one, as
    /// <c>getInternalRouterError</c> does, for a path no route matches (404), a submission whose method no form
    /// takes (405), a submission to a route with no action (405) and a body it cannot encode (400).
    /// <c>Hooks.UseRouteError</c> returns it to the <c>errorElement</c> that renders it.
    /// </summary>
    public sealed class ErrorResponse : Exception
    {
        public ErrorResponse(int status, string? statusText, object? detail = null)
            : this(status, statusText, detail, null)
        {
        }

        private ErrorResponse(int status, string? statusText, object? detail, Exception? error)
            : base($"{status} {statusText ?? string.Empty}", error)
        {
            Status = status;
            StatusText = statusText ?? string.Empty;
            Detail = detail;
        }

        /// <summary>The HTTP-style status: 404, 405 or 400 for the ones the router makes.</summary>
        public int Status { get; }

        /// <summary>The status's reason phrase: <c>"Not Found"</c>, <c>"Method Not Allowed"</c> or
        /// <c>"Bad Request"</c> for the ones the router makes, and empty when none was given.</summary>
        public string StatusText { get; }

        /// <summary>
        /// What the response carries, React Router's <c>data</c>. For one the router makes, the message as
        /// JavaScript writes an <c>Error</c>, <c>"Error: "</c> and the message, whose exception is the
        /// <see cref="Exception.InnerException"/>.
        /// </summary>
        public object? Detail { get; }

        /// <summary>Whether the router made this response, React Router's <c>internal</c>.</summary>
        public bool Internal { get; private set; }

        // React Router's getInternalRouterError, for the statuses this router raises.
        private static ErrorResponse FromRouter(int status, string statusText, string message) =>
            new(status, statusText, "Error: " + message, new Exception(message)) { Internal = true };

        internal static ErrorResponse NotFound(string pathname) =>
            FromRouter(404, "Not Found", $"No route matches URL \"{pathname}\"");

        internal static ErrorResponse InvalidMethod(string method) =>
            FromRouter(405, "Method Not Allowed", $"Invalid request method \"{method.ToUpperInvariant()}\"");

        internal static ErrorResponse NoAction(string method, string pathname, string? routeId) =>
            FromRouter(405, "Method Not Allowed",
                $"You made a {method.ToUpperInvariant()} request to \"{pathname}\" but did not provide an "
                + $"`action` for route \"{routeId}\", so there is no way to handle the request.");

        internal static ErrorResponse InvalidBody() =>
            FromRouter(400, "Bad Request", "Unable to encode submission body");
    }
}
