#nullable enable
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>What a route's action is called with: React Router's <c>ActionFunctionArgs</c>.</summary>
    public sealed class RouteActionContext
    {
        /// <summary>Parameters captured across the full matched route branch.</summary>
        public IReadOnlyDictionary<string, string> Params { get; init; } = null!;

        /// <summary>The matched route pattern, equal to <see cref="RouteMatch.MatchedPath"/>.</summary>
        public string? Path { get; init; }

        /// <summary>The submission's method, upper-case: <c>POST</c>, <c>PUT</c>, <c>PATCH</c> or <c>DELETE</c>.</summary>
        public string Method { get; init; } = null!;

        /// <summary>What the submission sent: the <c>formData</c> handed to <c>submit</c>.</summary>
        public object? FormData { get; init; }
    }

    /// <summary>How <see cref="SubmitFunction"/> and <see cref="Router.SubmitAsync(object, SubmitOptions, System.Threading.CancellationToken)"/> submit: React Router's <c>SubmitOptions</c>.</summary>
    public sealed class SubmitOptions
    {
        /// <summary>
        /// <c>get</c>, <c>post</c>, <c>put</c>, <c>patch</c> or <c>delete</c>, in any case. Null or empty is
        /// <c>get</c>, which navigates with the form data as the query string and runs no action. Any other
        /// method runs none either: the navigation commits React Router's 405 error in its place.
        /// </summary>
        public string Method { get; init; } = "get";

        /// <summary>
        /// The path to submit to, absolute or relative. Null or empty submits to the route the submitting
        /// component renders in, with the current query string, as <c>useSubmit</c> with no action does.
        /// </summary>
        public string? Action { get; init; }

        /// <summary>
        /// True replaces the current history entry and false pushes one. Null replaces for a mutation submitted
        /// to the current location whose action does not fail, and pushes otherwise.
        /// </summary>
        public bool? Replace { get; init; }
    }

    /// <summary>The function <c>Hooks.UseSubmit</c> returns: React Router's <c>SubmitFunction</c>.</summary>
    /// <param name="formData">What the submission sends. A <c>get</c> submission encodes null, an <see cref="ISearchParams"/>, a query
    /// string or name/value pairs as its query string; any other form data commits an error in the navigation.</param>
    /// <param name="options">How to submit; null takes every default.</param>
    public delegate VelvetTask<NavigationResult> SubmitFunction(object? formData, SubmitOptions? options = null);
}
