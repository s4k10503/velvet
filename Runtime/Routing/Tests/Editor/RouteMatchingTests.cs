using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the advanced matching features of <see cref="RouteTree"/> beyond plain literals.
    /// <list type="bullet">
    /// <item>A splat (<c>*</c>) captures the remaining path tail under the <c>*</c> key, including the empty
    /// tail, and a same-length static segment outranks it.</item>
    /// <item>An optional parameter (<c>:id?</c>) matches with the value captured or, when absent, without the
    /// key present; an optional literal (<c>seg?</c>) matches both its present and absent forms.</item>
    /// <item>Ranking is by specificity, not declaration order: a static segment outranks a dynamic one for the
    /// same path, while a dynamic route still matches when no static route applies, and a deeper nested branch
    /// outranks a shallower one.</item>
    /// <item>An index route's <see cref="RouteMatch.RouteId"/> is disambiguated so it never collides with its
    /// parent's id.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouteMatchingTests
    {
        #region Splat

        [Test]
        public void Given_SplatRoute_When_TailHasMultipleSegments_Then_CapturesWholeTail()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("files/*") });

            // Act
            var result = tree.Match("/files/a/b/c");

            // Assert
            Assert.That(result[0].Params["*"], Is.EqualTo("a/b/c"));
        }

        [Test]
        public void Given_SplatRoute_When_TailIsEmpty_Then_CapturesEmptyString()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("files/*") });

            // Act
            var result = tree.Match("/files");

            // Assert
            Assert.That(result[0].Params["*"], Is.EqualTo(string.Empty));
        }

        [Test]
        public void Given_SplatAndStaticSibling_When_PathMatchesStatic_Then_StaticOutranksSplat()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("*"),
                Route("about"),
            });

            // Act
            var result = tree.Match("/about");

            // Assert
            Assert.That(result[0].Route.Path, Is.EqualTo("about"),
                "A static segment outranks a splat for the same path");
        }

        #endregion

        #region Optional

        [Test]
        public void Given_OptionalParamRoute_When_ValuePresent_Then_CapturesValue()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("users/:id?") });

            // Act
            var result = tree.Match("/users/42");

            // Assert
            Assert.That(result[0].Params["id"], Is.EqualTo("42"));
        }

        [Test]
        public void Given_OptionalParamRoute_When_ValueAbsent_Then_KeyIsNotCaptured()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("users/:id?") });

            // Act
            var result = tree.Match("/users");

            // Assert
            Assert.That(result[0].Params.ContainsKey("id"), Is.False);
        }

        [Test]
        public void Given_OptionalLiteralRoute_When_SegmentPresent_Then_Matches()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("docs/intro?") });

            // Act
            var result = tree.Match("/docs/intro");

            // Assert
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void Given_OptionalLiteralRoute_When_SegmentAbsent_Then_Matches()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("docs/intro?") });

            // Act
            var result = tree.Match("/docs");

            // Assert
            Assert.That(result, Is.Not.Null);
        }

        #endregion

        #region Ranking by specificity

        [Test]
        public void Given_DynamicDeclaredBeforeStatic_When_PathMatchesStatic_Then_StaticWins()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("users/:id"),
                Route("users/new"),
            });

            // Act
            var result = tree.Match("/users/new");

            // Assert
            Assert.That(result[0].Route.Path, Is.EqualTo("users/new"));
        }

        [Test]
        public void Given_DynamicAndStaticSiblings_When_PathOnlyMatchesDynamic_Then_DynamicWins()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("users/:id"),
                Route("users/new"),
            });

            // Act
            var result = tree.Match("/users/42");

            // Assert
            Assert.That(result[0].Route.Path, Is.EqualTo("users/:id"));
        }

        [Test]
        public void Given_DynamicAndStaticSiblings_When_PathOnlyMatchesDynamic_Then_CapturesParam()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("users/:id"),
                Route("users/new"),
            });

            // Act
            var result = tree.Match("/users/42");
            Assume.That(result[0].Route.Path, Is.EqualTo("users/:id"), "Precondition: the dynamic route matched");

            // Assert
            Assert.That(result[0].Params["id"], Is.EqualTo("42"));
        }

        [Test]
        public void Given_NestedBranch_When_MatchingDeepPath_Then_DeeperLeafEndsTheChain()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("shop", children: new[]
                {
                    Route("cart"),
                }),
            });

            // Act
            var result = tree.Match("/shop/cart");

            // Assert
            Assert.That(result[result.Count - 1].Route.Path, Is.EqualTo("cart"));
        }

        #endregion

        #region RouteId

        [Test]
        public void Given_ParentWithIndexChild_When_Matching_Then_IndexRouteIdDoesNotCollideWithParent()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("room", children: new[]
                {
                    Route(""),
                }),
            });

            // Act
            var result = tree.Match("/room");

            // Assert
            Assert.That(
                (result[0].RouteId, result[1].RouteId),
                Is.EqualTo(("/room", "/room/?index")),
                "The index route id is disambiguated from its parent's id");
        }

        #endregion
    }
}
