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

        /// <summary>
        /// The submission as a request, React Router's <c>request</c>: its body in the encoding
        /// <see cref="SubmitOptions.EncType"/> named, read through <see cref="RouteRequest.FormData()"/>,
        /// <see cref="RouteRequest.Json()"/> or <see cref="RouteRequest.Text()"/>.
        /// </summary>
        public RouteRequest Request { get; init; } = null!;
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

        /// <summary>
        /// How the body is encoded, React Router's <c>encType</c>. Null or empty is
        /// <c>application/x-www-form-urlencoded</c>, which reads the body as the URLSearchParams constructor
        /// does: the query string of a <c>get</c>, and a <see cref="FormData"/> for any other method.
        /// <c>application/json</c> passes the body through, a string parsed as JSON; <c>text/plain</c> sends it
        /// as text. A <c>get</c> takes neither of those two: it commits "Unable to encode submission body".
        /// Any other value sends the <see cref="FormData"/> as <c>multipart/form-data</c>.
        /// </summary>
        public string EncType { get; init; } = "application/x-www-form-urlencoded";
    }

    /// <summary>The function <c>Hooks.UseSubmit</c> returns: React Router's <c>SubmitFunction</c>.</summary>
    /// <param name="formData">What the submission sends, encoded as <see cref="SubmitOptions.EncType"/> says and
    /// <c>routing.md</c> lists; a body that cannot be encoded commits an error in the navigation.</param>
    /// <param name="options">How to submit; null takes every default.</param>
    public delegate VelvetTask<NavigationResult> SubmitFunction(object? formData, SubmitOptions? options = null);
}
