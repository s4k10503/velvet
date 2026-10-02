#nullable enable
using System;

namespace Velvet
{
    /// <summary>
    /// An error carrying an HTTP status: React Router's <c>ErrorResponse</c>, which
    /// <c>isRouteErrorResponse</c> tests for. The router records one for a path no route matches (404) and for a
    /// submission no action can take (405), and a loader or an action may throw one of its own.
    /// <c>Hooks.UseRouteError</c> returns it like any other error.
    /// </summary>
    public sealed class RouteErrorResponse : Exception
    {
        /// <param name="status">The HTTP status, such as <c>404</c>.</param>
        /// <param name="statusText">The status's reason phrase, such as <c>Not Found</c>.</param>
        /// <param name="data">What the response carries. A string or an exception there is also the
        /// <see cref="Exception.Message"/>, which is otherwise the status and its reason phrase.</param>
        public RouteErrorResponse(int status, string statusText, object? data = null)
            : base(MessageOf(status, statusText, data), data as Exception)
        {
            Status = status;
            StatusText = statusText;
            Data = data;
        }

        /// <summary>The HTTP status.</summary>
        public int Status { get; }

        /// <summary>The status's reason phrase.</summary>
        public string StatusText { get; }

        /// <summary>What the response carries: for one the router records, the exception that describes it.</summary>
        public new object? Data { get; }

        private static string MessageOf(int status, string statusText, object? data) => data switch
        {
            string text => text,
            Exception exception => exception.Message,
            _ => $"{status} {statusText}",
        };
    }
}
