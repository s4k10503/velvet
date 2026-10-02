// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class RouteErrorResponseTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        private static string StatusOf(Exception? error)
            => error is RouteErrorResponse response ? $"{response.Status} {response.StatusText}" : "not a response";

        private static Exception? OnlyError(Router router) => router.CurrentLoaderErrors.Values.SingleOrDefault();

        #region What the router records

        [Test]
        public void Given_AnUnmatchedPath_When_Navigating_Then_ItsErrorIsA404Response()
        {
            // Arrange
            var router = BuildRouter("/home", Route("/", children: new[] { Route("home") }));

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(StatusOf(OnlyError(router)), Is.EqualTo("404 Not Found"));
        }

        [Test]
        public void Given_AMethodNoFormTakes_When_Submitted_Then_ItsErrorIsA405Response()
        {
            // Arrange
            var router = BuildRouter("/items", Route("items"));

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "head" }).GetAwaiter().GetResult();

            // Assert
            Assert.That(StatusOf(OnlyError(router)), Is.EqualTo("405 Method Not Allowed"));
        }

        [Test]
        public void Given_ARouteWithoutAnAction_When_ItIsPosted_Then_ItsErrorIsA405Response()
        {
            // Arrange
            var router = BuildRouter("/items", Route("items"));

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }).GetAwaiter().GetResult();

            // Assert
            Assert.That(StatusOf(OnlyError(router)), Is.EqualTo("405 Method Not Allowed"));
        }

        #endregion

        #region The message

        [Test]
        public void Given_StringData_When_Constructed_Then_TheMessageIsTheString()
        {
            // Arrange
            const string data = "Gone for good";

            // Act
            var response = new RouteErrorResponse(410, "Gone", data);

            // Assert
            Assert.That(response.Message, Is.EqualTo(data));
        }

        [Test]
        public void Given_AnExceptionAsData_When_Constructed_Then_TheMessageIsItsMessage()
        {
            // Arrange
            var data = new InvalidOperationException("described");

            // Act
            var response = new RouteErrorResponse(400, "Bad Request", data);

            // Assert
            Assert.That(response.Message, Is.EqualTo("described"));
        }

        [Test]
        public void Given_NoData_When_Constructed_Then_TheMessageIsTheStatusAndItsReasonPhrase()
        {
            // Arrange
            const int status = 401;

            // Act
            var response = new RouteErrorResponse(status, "Unauthorized");

            // Assert
            Assert.That(response.Message, Is.EqualTo("401 Unauthorized"));
        }

        #endregion

        #region The default error element

        // Thrown, so unlike a response the router records it carries a stack trace that names this fixture.
        private static Router ThrowingResponseRouter()
        {
            var router = BuildRouter("/home", Route("/", children: new[]
            {
                Route("home"),
                Route("denied", loader: (_, _) => throw new RouteErrorResponse(401, "Unauthorized", "Sign in first")),
            }));
            router.NavigateSync("/denied");
            return router;
        }

        [Test]
        public void Given_AThrownResponse_When_TheDefaultErrorElementRendersIt_Then_ItShowsTheStatus()
        {
            // Arrange
            var router = ThrowingResponseRouter();
            LogAssert.Expect(LogType.Exception, new Regex(nameof(RouteErrorResponse)));

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(_root.FindLabelByText("401 Unauthorized") != null, Is.True);
        }

        [Test]
        public void Given_AThrownResponse_When_TheDefaultErrorElementRendersIt_Then_ItShowsNoStackTrace()
        {
            // Arrange
            var router = ThrowingResponseRouter();
            LogAssert.Expect(LogType.Exception, new Regex(nameof(RouteErrorResponse)));

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();

            // Assert — the heading rides along because an element that rendered nothing shows no stack either.
            var labels = _root.Query<Label>().ToList();
            Assert.That(
                (_root.FindLabelByText("Unexpected Application Error!") != null,
                    labels.Exists(label => label.text.Contains(nameof(RouteErrorResponseTests)))),
                Is.EqualTo((true, false)));
        }

        #endregion
    }
}
