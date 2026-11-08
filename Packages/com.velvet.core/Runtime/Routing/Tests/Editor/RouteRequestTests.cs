using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a submission's encoding, <see cref="SubmitOptions.EncType"/>, as React Router's
    /// <c>normalizeNavigateOptions</c> and <c>createClientSideRequest</c> carry it.
    /// <list type="bullet">
    /// <item>The default encodes the body as URLSearchParams would into a <see cref="FormData"/>;
    /// <c>application/json</c> passes it through, parsing a string; <c>text/plain</c> writes it as text; a
    /// <c>get</c> takes neither of the last two.</item>
    /// <item>The action reads the body once, through <see cref="RouteRequest"/>, in the encoding it was sent
    /// with.</item>
    /// <item>The navigation in flight carries the encType and the body in the one member that encoding
    /// fills.</item>
    /// </list>
    /// </summary>
    [Timeout(30000)]
    [TestFixture]
    internal sealed class RouteRequestTests
    {
        private const string Json = "application/json";
        private const string PlainText = "text/plain";

        [TearDown]
        public void TearDown() => Router.Current?.Dispose();

        private static Router ItemsRouter(Func<RouteRequest, object> read)
            => BuildRouter("/other", Route("/", children: new[]
            {
                V.Route("items", V.Component(StubA), action: (context, _) => VelvetTask.FromResult(read(context.Request))),
                Route("other"),
            }));

        private static Router PendingItemsRouter()
            => BuildRouter("/other", Route("/", children: new[]
            {
                V.Route("items", V.Component(StubA), action: (_, _) => new VelvetTaskCompletionSource<object>().Task),
                Route("other"),
            }));

        private static object? ActionData(Func<RouteRequest, object> read, object? body, string encType, string action = "/items")
        {
            var router = ItemsRouter(read);
            router.SubmitAsync(body, new SubmitOptions { Method = "post", Action = action, EncType = encType })
                .GetAwaiter().GetResult();
            return router.CurrentActionData.GetValueOrDefault("0-0");
        }

        private static Exception? ActionError(Func<RouteRequest, object> read, object? body, string encType, string method = "post")
        {
            var router = ItemsRouter(read);
            router.SubmitAsync(body, new SubmitOptions { Method = method, Action = "/items", EncType = encType })
                .GetAwaiter().GetResult();
            return router.CurrentLoaderErrors.GetValueOrDefault("0");
        }

        private static string Entries(FormData formData)
            => string.Join("&", formData.Select(entry => entry.Key + "=" + entry.Value));

        #region The default encoding

        [Test]
        public void Given_AUrlEncodedPost_When_TheActionReadsFormData_Then_ItHoldsTheBodysPairsInOrder()
        {
            // Arrange
            var body = new { q = "a b", n = 2 };

            // Act
            var data = ActionData(request => Entries(request.FormData()), body, "application/x-www-form-urlencoded");

            // Assert
            Assert.That(data, Is.EqualTo("q=a b&n=2"));
        }

        [Test]
        public void Given_AUrlEncodedPost_When_TheActionReadsText_Then_ItIsTheBodyFormEncoded()
        {
            // Arrange
            var body = new { q = "a b", n = 2 };

            // Act
            var data = ActionData(request => request.Text(), body, null!);

            // Assert
            Assert.That(data, Is.EqualTo("q=a+b&n=2"));
        }

        [Test]
        public void Given_AUrlEncodedPost_When_TheActionReadsJson_Then_ItThrowsAFormatException()
        {
            // Arrange
            var body = new { q = "a" };

            // Act
            var error = ActionError(request => request.Json()!, body, "application/x-www-form-urlencoded");

            // Assert
            Assert.That(error, Is.InstanceOf<FormatException>());
        }

        [Test]
        public void Given_AUrlEncodedPost_When_TheActionReadsTheBodyTwice_Then_TheSecondReadThrows()
        {
            // Arrange
            var body = new { q = "a" };

            // Act
            var error = ActionError(request => request.Text() + request.Text(), body, "application/x-www-form-urlencoded");

            // Assert
            Assert.That(error?.Message, Is.EqualTo("Body has already been consumed."));
        }

        [Test]
        public void Given_AnActionWithAQueryAndAFragment_When_Posted_Then_TheRequestUrlIsThePathAndQuery()
        {
            // Arrange
            var body = new { q = "a" };

            // Act
            var data = ActionData(request => request.Url, body, "application/x-www-form-urlencoded", "/items?x=1#here");

            // Assert
            Assert.That(data, Is.EqualTo("/items?x=1"));
        }

        [Test]
        public void Given_AFormDataBody_When_PostedWhileTheActionRuns_Then_TheNavigationHoldsThatInstance()
        {
            // Arrange
            var router = PendingItemsRouter();
            var formData = new FormData();
            formData.Append("a", "1");

            // Act
            router.SubmitAsync(formData, new SubmitOptions { Method = "post", Action = "/items" }).Forget();

            // Assert
            Assert.That(router.Navigation.FormData, Is.SameAs(formData));
        }

        [Test]
        public void Given_ADefaultEncodedPost_When_TheActionRuns_Then_TheNavigationReportsTheUrlEncodedType()
        {
            // Arrange
            var router = PendingItemsRouter();

            // Act
            router.SubmitAsync("a=1", new SubmitOptions { Method = "post", Action = "/items" }).Forget();

            // Assert
            Assert.That(router.Navigation.FormEncType, Is.EqualTo("application/x-www-form-urlencoded"));
        }

        #endregion

        #region application/json

        [Test]
        public void Given_AJsonPostOfAString_When_TheActionReadsJson_Then_ItIsTheParsedValue()
        {
            // Arrange
            const string body = "{\"a\":[1,true,null,\"x\"]}";

            // Act
            var data = ActionData(
                request => request.Json() is Dictionary<string, object?> json && json["a"] is List<object?> list
                    ? string.Join(",", list.Select(item => item?.ToString() ?? "null"))
                    : "not an object",
                body, Json);

            // Assert
            Assert.That(data, Is.EqualTo("1,True,null,x"));
        }

        [Test]
        public void Given_AJsonPostOfAnObject_When_TheActionReadsText_Then_ItIsWhatJsonStringifyWrites()
        {
            // Arrange
            var body = new { b = 1.5, a = "x\"y", list = new[] { 1, 2 }, none = (string?)null, skipped = (Action)(() => { }) };

            // Act
            var data = ActionData(request => request.Text(), body, Json);

            // Assert
            Assert.That(data, Is.EqualTo("{\"b\":1.5,\"a\":\"x\\\"y\",\"list\":[1,2],\"none\":null}"));
        }

        [Test]
        public void Given_AJsonPostOfADictionaryWithArrayIndexKeys_When_TheActionReadsText_Then_ThoseKeysComeFirstAscending()
        {
            // Arrange
            var body = new Dictionary<string, int> { ["b"] = 1, ["10"] = 2, ["2"] = 3, ["01"] = 4 };

            // Act
            var data = ActionData(request => request.Text(), body, Json);

            // Assert
            Assert.That(data, Is.EqualTo("{\"2\":3,\"10\":2,\"b\":1,\"01\":4}"));
        }

        [Test]
        public void Given_AJsonPost_When_TheActionReadsFormData_Then_ItThrows()
        {
            // Arrange
            var body = new { a = 1 };

            // Act
            var error = ActionError(request => request.FormData(), body, Json);

            // Assert
            Assert.That(error, Is.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void Given_AJsonPost_When_TheActionReadsTheContentType_Then_ItIsApplicationJson()
        {
            // Arrange
            var body = new { a = 1 };

            // Act
            var data = ActionData(request => request.ContentType, body, Json);

            // Assert
            Assert.That(data, Is.EqualTo(Json));
        }

        [Test]
        public void Given_AJsonPostOfMalformedText_When_Submitted_Then_ItCommitsTheEncodingError()
        {
            // Arrange
            const string body = "{\"a\":";

            // Act
            var error = ActionError(_ => "ran", body, Json);

            // Assert
            Assert.That((error as ErrorResponse)?.Detail, Is.EqualTo("Error: Unable to encode submission body"));
        }

        [Test]
        public void Given_AJsonBodyHoldingItself_When_Posted_Then_TheSubmissionThrows()
        {
            // Arrange
            var router = ItemsRouter(_ => "ran");
            var body = new List<object>();
            body.Add(body);

            // Act
            TestDelegate submit = () => router.SubmitAsync(body, new SubmitOptions { Method = "post", Action = "/items", EncType = Json })
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(submit, Throws.InvalidOperationException);
        }

        [Test]
        public void Given_AJsonPostOfAString_When_TheActionRuns_Then_TheNavigationHoldsTheParsedValue()
        {
            // Arrange
            var router = PendingItemsRouter();

            // Act
            router.SubmitAsync("[7]", new SubmitOptions { Method = "post", Action = "/items", EncType = Json }).Forget();

            // Assert
            Assert.That((router.Navigation.Json as List<object?>)?[0], Is.EqualTo(7.0));
        }

        [Test]
        public void Given_AJsonPost_When_TheActionRuns_Then_TheNavigationHoldsNoFormData()
        {
            // Arrange
            var router = PendingItemsRouter();

            // Act
            router.SubmitAsync("[7]", new SubmitOptions { Method = "post", Action = "/items", EncType = Json }).Forget();

            // Assert
            Assert.That(router.Navigation.FormData, Is.Null);
        }

        #endregion

        #region text/plain

        [Test]
        public void Given_ATextPostOfAFormData_When_TheActionReadsText_Then_EachEntryIsALine()
        {
            // Arrange
            var body = new FormData();
            body.Append("a", "1");
            body.Append("b", "2");

            // Act
            var data = ActionData(request => request.Text(), body, PlainText);

            // Assert
            Assert.That(data, Is.EqualTo("a=1\nb=2\n"));
        }

        [Test]
        public void Given_ATextPostOfAString_When_TheActionRuns_Then_TheNavigationHoldsTheText()
        {
            // Arrange
            var router = PendingItemsRouter();

            // Act
            router.SubmitAsync("a b", new SubmitOptions { Method = "post", Action = "/items", EncType = PlainText }).Forget();

            // Assert
            Assert.That(router.Navigation.Text, Is.EqualTo("a b"));
        }

        [Test]
        public void Given_ATextPostOfAPlainObject_When_TheActionReadsText_Then_ItIsJavaScriptsStringOfAnObject()
        {
            // Arrange
            var body = new { a = 1 };

            // Act
            var data = ActionData(request => request.Text(), body, PlainText);

            // Assert
            Assert.That(data, Is.EqualTo("[object Object]"));
        }

        [TestCase(Json)]
        [TestCase(PlainText)]
        public void Given_AGetSubmissionInAnEncodingThatSendsABody_When_Submitted_Then_ItCommitsTheEncodingError(string encType)
        {
            // Arrange
            const string body = "a=1";

            // Act
            var error = ActionError(_ => "ran", body, encType, "get");

            // Assert
            Assert.That((error as ErrorResponse)?.Detail, Is.EqualTo("Error: Unable to encode submission body"));
        }

        #endregion

        #region Refusals and loaders

        [Test]
        public void Given_ARefusedSubmission_When_ItsErrorIsRead_Then_TheRouterMadeIt()
        {
            // Arrange
            const string body = "a=1";

            // Act
            var error = ActionError(_ => "ran", body, Json, "get");

            // Assert
            Assert.That((error as ErrorResponse)?.Internal, Is.True);
        }

        private static Router LoaderRouter(Func<RouteLoaderContext, string> read)
            => BuildRouter("/items?q=1#top", Route("/", children: new[]
            {
                Route("items", loader: (context, _) => VelvetTask.FromResult<object>(read(context))),
            }));

        [Test]
        public void Given_ALoader_When_ItReadsItsRequestUrl_Then_ItIsThePathAndSearch()
        {
            // Arrange / Act
            var router = LoaderRouter(context => context.Request.Url);

            // Assert
            Assert.That(router.CurrentLoaderData.GetValueOrDefault("0-0"), Is.EqualTo("/items?q=1"));
        }

        [Test]
        public void Given_ALoader_When_ItReadsItsRequestMethod_Then_ItIsGet()
        {
            // Arrange / Act
            var router = LoaderRouter(context => context.Request.Method);

            // Assert
            Assert.That(router.CurrentLoaderData.GetValueOrDefault("0-0"), Is.EqualTo("GET"));
        }

        [Test]
        public void Given_ALoader_When_ItReadsItsRequestText_Then_ItIsEmpty()
        {
            // Arrange / Act
            var router = LoaderRouter(context => "[" + context.Request.Text() + "]");

            // Assert
            Assert.That(router.CurrentLoaderData.GetValueOrDefault("0-0"), Is.EqualTo("[]"));
        }

        #endregion

        #region multipart/form-data

        [Test]
        public void Given_AMultipartPost_When_TheActionReadsFormData_Then_ItHoldsTheEntries()
        {
            // Arrange
            var body = new { a = "1", b = "2" };

            // Act
            var data = ActionData(request => Entries(request.FormData()), body, "multipart/form-data");

            // Assert
            Assert.That(data, Is.EqualTo("a=1&b=2"));
        }

        [Test]
        public void Given_AMultipartPost_When_TheActionReadsText_Then_EachEntryIsAPartUnderTheBoundary()
        {
            // Arrange
            var body = new { a = "1" };

            // Act
            var data = ActionData(
                request => request.Text() == "--" + Boundary(request) + "\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\n1\r\n--"
                    + Boundary(request) + "--\r\n",
                body, "multipart/form-data");

            // Assert
            Assert.That(data, Is.True);
        }

        private static string Boundary(RouteRequest request)
            => request.ContentType.Substring("multipart/form-data; boundary=".Length);

        #endregion
    }
}
