#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace Velvet
{
    /// <summary>
    /// The request a loader or an action is called with, React Router's <c>request</c>, built as React Router's
    /// <c>createClientSideRequest</c> builds it: a loader's is a <c>GET</c> of the location with no body, and an
    /// action's carries the submission's method, target and body in the encoding it was submitted with. As with a fetch <c>Request</c>, the body is read once:
    /// <see cref="FormData()"/>, <see cref="Json()"/> and <see cref="Text()"/> each consume a body, and a second read
    /// of one throws; a request with no body reads as empty every time.
    /// </summary>
    public sealed class RouteRequest
    {
        private const string UrlEncoded = "application/x-www-form-urlencoded";
        private const string JsonType = "application/json";
        private const string PlainText = "text/plain";

        private readonly string? _body;
        private readonly global::Velvet.FormData? _multipart;
        private readonly string? _essence;

        // Memory history's createURL resolves every href against this origin.
        private const string Origin = "http://localhost";

        // A loader's: a GET with no body.
        internal RouteRequest(string url)
        {
            Method = "GET";
            Url = Origin + url;
        }

        internal RouteRequest(string method, string url, string encType, global::Velvet.FormData? formData, object? json, string? text)
        {
            Method = method;
            Url = Origin + url;
            if (encType == JsonType)
            {
                _body = JsonText.Stringify(json);
                ContentType = JsonType;
            }
            else if (encType == PlainText)
            {
                _body = text;
                ContentType = "text/plain;charset=UTF-8";
            }
            else if (encType == UrlEncoded)
            {
                _body = RouteQuery.FormUrlEncode(formData!);
                ContentType = UrlEncoded + ";charset=UTF-8";
            }
            else
            {
                // Any other encType sends the FormData itself, which a fetch Request encodes as multipart.
                var boundary = "----formdata-velvet-" + Boundary();
                _multipart = new global::Velvet.FormData(formData!);
                _body = Multipart(formData!, boundary);
                ContentType = "multipart/form-data; boundary=" + boundary;
            }
            _essence = ContentType.Split(';')[0].Trim().ToLowerInvariant();
        }

        /// <summary>The method, upper-case: <c>GET</c> for a loader's, and <c>POST</c>, <c>PUT</c>, <c>PATCH</c> or
        /// <c>DELETE</c> for an action's.</summary>
        public string Method { get; }

        /// <summary>
        /// The request's URL, as memory history's <c>createURL</c> makes it: <c>http://localhost</c> and the path and
        /// query string, without the fragment.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// The body's <c>Content-Type</c> header: <c>application/json</c>, <c>text/plain;charset=UTF-8</c>,
        /// <c>application/x-www-form-urlencoded;charset=UTF-8</c>, or <c>multipart/form-data</c> with its
        /// boundary for any other encType; null for a request with no body.
        /// </summary>
        public string? ContentType { get; }

        /// <summary>Whether <see cref="FormData()"/>, <see cref="Json()"/> or <see cref="Text()"/> has read the body.</summary>
        public bool BodyUsed { get; private set; }

        /// <summary>The body as text, as <c>request.text()</c>.</summary>
        /// <exception cref="InvalidOperationException">The body has already been read.</exception>
        public string Text() => Consume();

        /// <summary>
        /// The body parsed as JSON, as <c>request.json()</c>: an object is a <c>Dictionary&lt;string, object?&gt;</c>,
        /// an array a <c>List&lt;object?&gt;</c> and a number a <c>double</c>.
        /// </summary>
        /// <exception cref="InvalidOperationException">The body has already been read.</exception>
        /// <exception cref="FormatException">The body is not JSON.</exception>
        public object? Json() => JsonText.Parse(Consume());

        /// <summary>The body's entries, as <c>request.formData()</c>.</summary>
        /// <exception cref="InvalidOperationException">The body has already been read, or the request has none or
        /// was submitted as <c>application/json</c> or <c>text/plain</c>.</exception>
        public global::Velvet.FormData FormData()
        {
            var body = Consume();
            if (_multipart != null)
            {
                return new global::Velvet.FormData(_multipart);
            }
            if (_essence != UrlEncoded)
            {
                throw new InvalidOperationException(
                    $"Could not parse content as FormData: its Content-Type is {ContentType ?? "absent"}.");
            }
            return new global::Velvet.FormData(RouteQuery.ParsePairs("?" + body));
        }

        private string Consume()
        {
            if (BodyUsed)
            {
                throw new InvalidOperationException("Body has already been consumed.");
            }
            // A body JSON.stringify left undefined is no body, which reads as empty and is never used up.
            if (_body == null)
            {
                return string.Empty;
            }
            BodyUsed = true;
            return _body;
        }

        // The multipart/form-data encoding algorithm: each entry a part named by its escaped name, its value's
        // line breaks as CRLF.
        private static string Multipart(global::Velvet.FormData formData, string boundary)
        {
            var text = new StringBuilder();
            foreach (var entry in formData)
            {
                text.Append("--").Append(boundary).Append("\r\n")
                    .Append("Content-Disposition: form-data; name=\"")
                    .Append(Crlf(entry.Key).Replace("\n", "%0A").Replace("\r", "%0D").Replace("\"", "%22"))
                    .Append("\"\r\n\r\n")
                    .Append(Crlf(entry.Value)).Append("\r\n");
            }
            return text.Append("--").Append(boundary).Append("--\r\n").ToString();
        }

        private static string Crlf(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

        private static string Boundary()
        {
            var random = new Random();
            var digits = new StringBuilder();
            for (var index = 0; index < 11; index++)
            {
                digits.Append(random.Next(10).ToString(CultureInfo.InvariantCulture));
            }
            return digits.ToString();
        }
    }
}
