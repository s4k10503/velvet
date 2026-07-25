using System;
using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="RouteTree.Match"/> over a route definition tree, from plain literal
    /// matching through the advanced segment kinds (splat, optional, dynamic) and specificity ranking.
    /// <list type="bullet">
    /// <item>A path matches the highest-specificity branch whose pattern consumes it fully; a leading slash on
    /// the queried path is optional and the root path <c>"/"</c> resolves to a declared <c>"/"</c> route.</item>
    /// <item>A null or empty queried path matches nothing.</item>
    /// <item>A path that no branch consumes returns null.</item>
    /// <item>A dynamic <c>:param</c> segment captures the corresponding path segment under the param name.</item>
    /// <item>Nested routes return the full parent-first chain, and every level shares the one cumulative
    /// parameter set captured across the whole branch.</item>
    /// <item>An index child (<c>path == ""</c>) matches its parent's path and forms a second chain entry, with
    /// a <see cref="RouteMatch.RouteId"/> disambiguated from its parent's id.</item>
    /// <item>Matching is case-insensitive by default and per-route; <c>caseSensitive: true</c> opts a single
    /// route into ordinal matching without inheriting to or from its relatives, and the flag participates in
    /// branch ranking so a case-rejected literal falls through to a dynamic sibling.</item>
    /// <item>The default case-insensitivity is the <see cref="RouteDefinition.CaseSensitive"/> init default, and
    /// it applies to splat and optional literal prefixes as well as plain literals.</item>
    /// <item>The constructor rejects a null route array.</item>
    /// <item>A splat (<c>*</c>) captures the remaining path tail under the <c>*</c> key, including the empty
    /// tail, and a same-length static segment outranks it.</item>
    /// <item>An optional parameter (<c>:id?</c>) matches with the value captured or, when absent, without the
    /// key present; an optional literal (<c>seg?</c>) matches both its present and absent forms.</item>
    /// <item>Ranking is by specificity, not declaration order: a static segment outranks a dynamic one for the
    /// same path, while a dynamic route still matches when no static route applies, and a deeper nested branch
    /// outranks a shallower one.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouteTreeTests
    {
        #region Basic matching

        [Test]
        public void Given_FlatRoutes_When_MatchingExactPath_Then_ReturnsThatRoute()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("/"),
                Route("about"),
            });

            // Act
            var result = tree.Match("/about");

            // Assert
            Assert.That(result[0].Route.Path, Is.EqualTo("about"));
        }

        [Test]
        public void Given_FlatRoutes_When_MatchingExactPath_Then_ChainHasSingleEntry()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("/"),
                Route("about"),
            });

            // Act
            var result = tree.Match("/about");

            // Assert
            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_RootRoute_When_MatchingRootPath_Then_ResolvesToRootRoute()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("/") });

            // Act
            var result = tree.Match("/");

            // Assert
            Assert.That(result[0].Route.Path, Is.EqualTo("/"));
        }

        [Test]
        public void Given_NonMatchingPath_When_Matching_Then_ReturnsNull()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("about") });

            // Act
            var result = tree.Match("/nonexistent");

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Given_AnyTree_When_MatchingNullPath_Then_ReturnsNull()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("about") });

            // Act
            var result = tree.Match(null);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Given_AnyTree_When_MatchingEmptyPath_Then_ReturnsNull()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("about") });

            // Act
            var result = tree.Match(string.Empty);

            // Assert
            Assert.That(result, Is.Null);
        }

        #endregion

        #region Parameters

        [Test]
        public void Given_ParameterRoute_When_MatchingConcretePath_Then_ExtractsParamValue()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("avatar/:id") });

            // Act
            var result = tree.Match("/avatar/123");

            // Assert
            Assert.That(result[0].Params["id"], Is.EqualTo("123"));
        }

        [Test]
        public void Given_MultiParameterRoute_When_MatchingConcretePath_Then_ExtractsAllParams()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("user/:userId/post/:postId") });

            // Act
            var result = tree.Match("/user/abc/post/456");

            // Assert
            Assert.That((result[0].Params["userId"], result[0].Params["postId"]), Is.EqualTo(("abc", "456")));
        }

        #endregion

        #region Nested routes

        [Test]
        public void Given_NestedRoutes_When_MatchingLeafPath_Then_ReturnsFullParentFirstChain()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("/", children: new[]
                {
                    Route("room", children: new[]
                    {
                        Route("edit"),
                    }),
                }),
            });

            // Act
            var result = tree.Match("/room/edit");

            // Assert
            Assert.That(
                new[] { result[0].Route.Path, result[1].Route.Path, result[2].Route.Path },
                Is.EqualTo(new[] { "/", "room", "edit" }));
        }

        [Test]
        public void Given_NestedRouteWithParam_When_MatchingLeafPath_Then_ParamIsVisibleAtLeafLevel()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("/", children: new[]
                {
                    Route("avatar/:id"),
                }),
            });

            // Act
            var result = tree.Match("/avatar/xyz");

            // Assert
            Assert.That(result[1].Params["id"], Is.EqualTo("xyz"));
        }

        [Test]
        public void Given_NestedRouteWithParam_When_MatchingLeafPath_Then_EveryLevelSharesTheCumulativeParamSet()
        {
            // The branch captures one parameter dictionary and exposes it at every chain level, so a param
            // captured by a descendant segment is visible from the parent entry too.
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("/", children: new[]
                {
                    Route("avatar/:id"),
                }),
            });

            // Act
            var result = tree.Match("/avatar/xyz");

            // Assert
            Assert.That(result[0].Params, Is.SameAs(result[1].Params));
        }

        #endregion

        #region Index routes

        [Test]
        public void Given_ParentWithIndexChild_When_MatchingParentPath_Then_IndexChildJoinsTheChain()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("room", children: new[]
                {
                    Route(""),
                    Route("edit"),
                }),
            });

            // Act
            var result = tree.Match("/room");

            // Assert
            Assert.That(
                new[] { result[0].Route.Path, result[1].Route.Path },
                Is.EqualTo(new[] { "room", "" }));
        }

        #endregion

        #region Case sensitivity

        [Test]
        public void Given_DefaultLiteralRoute_When_QueriedWithDifferentCase_Then_Matches()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("About") });

            // Act
            var result = tree.Match("/about");

            // Assert
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void Given_DefaultLiteralRoute_When_QueriedWithSameCase_Then_Matches()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("About") });

            // Act
            var result = tree.Match("/About");

            // Assert
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void Given_RouteDefinitionInitDefault_When_QueriedWithDifferentCase_Then_Matches()
        {
            // Bypass the RouteTestStubs.Route() helper (which explicitly assigns CaseSensitive) and exercise
            // RouteDefinition's init default directly, pinning the production sentinel: reverting
            // CaseSensitive's init default to true on the type fails this test even if the helper keeps false.
            // Arrange
            var tree = new RouteTree(new[] { new RouteDefinition { Path = "About" } });

            // Act
            var result = tree.Match("/about");

            // Assert
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void Given_DefaultParamRouteWithLiteralPrefix_When_PrefixCaseDiffers_Then_MatchesAndExtractsParam()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("users/:id") });

            // Act
            var result = tree.Match("/Users/5");

            // Assert
            Assert.That(result[0].Params["id"], Is.EqualTo("5"));
        }

        [Test]
        public void Given_CaseSensitiveRoute_When_QueriedWithDifferentCase_Then_DoesNotMatch()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("About", caseSensitive: true) });

            // Act
            var result = tree.Match("/about");

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Given_CaseSensitiveRoute_When_QueriedWithSameCase_Then_Matches()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("About", caseSensitive: true) });

            // Act
            var result = tree.Match("/About");

            // Assert
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void Given_DefaultSplatRoute_When_LiteralPrefixCaseDiffers_Then_MatchesAndCapturesTail()
        {
            // A splat's literal prefix segment follows the route's case-sensitivity flag (default insensitive),
            // so "Files/*" matches "/files/a.png" and the splat captures the remaining tail.
            // Arrange
            var tree = new RouteTree(new[] { Route("Files/*") });

            // Act
            var result = tree.Match("/files/a.png");

            // Assert
            Assert.That(result[0].Params["*"], Is.EqualTo("a.png"));
        }

        [Test]
        public void Given_DefaultOptionalLiteralRoute_When_QueriedWithDifferentCase_Then_Matches()
        {
            // Both the literal prefix and the optional literal segment honor the default-insensitive flag.
            // Arrange
            var tree = new RouteTree(new[] { Route("Docs/intro?") });

            // Act
            var result = tree.Match("/DOCS/INTRO");

            // Assert
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void Given_CaseSensitiveParentWithDefaultChild_When_ChildLiteralCaseDiffers_Then_ChildStaysInsensitive()
        {
            // The per-route flag is not inherited: a default-insensitive child literal matches a different case
            // even under a case-sensitive parent.
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("Files", caseSensitive: true, children: new[]
                {
                    Route("Photos"),
                }),
            });

            // Act
            var result = tree.Match("/Files/photos");

            // Assert
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void Given_CaseSensitiveParentWithDefaultChild_When_ParentLiteralCaseDiffers_Then_ParentStaysSensitive()
        {
            // The parent's case-sensitive flag is not relaxed by an insensitive child.
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("Files", caseSensitive: true, children: new[]
                {
                    Route("Photos"),
                }),
            });

            // Act
            var result = tree.Match("/files/photos");

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Given_CaseSensitiveLiteralAndDynamicSibling_When_LiteralCaseRejects_Then_FallsThroughToDynamic()
        {
            // "About" opts into case-sensitive matching, so "/ABOUT" cannot bind to it and falls through to the
            // dynamic ":slug" sibling, confirming the per-route flag participates in branch ranking.
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("About", caseSensitive: true),
                Route(":slug"),
            });

            // Act
            var result = tree.Match("/ABOUT");

            // Assert
            Assert.That(result[0].Route.Path, Is.EqualTo(":slug"));
        }

        [Test]
        public void Given_CaseSensitiveLiteralAndDynamicSibling_When_FallsThroughToDynamic_Then_CapturesOriginalCaseValue()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("About", caseSensitive: true),
                Route(":slug"),
            });

            // Act
            var result = tree.Match("/ABOUT");
            Assume.That(result[0].Route.Path, Is.EqualTo(":slug"), "Precondition: the dynamic sibling won the match");

            // Assert
            Assert.That(result[0].Params["slug"], Is.EqualTo("ABOUT"));
        }

        #endregion

        #region Validation

        [Test]
        public void Given_NullRouteArray_When_Constructing_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => new RouteTree(null));
        }

        [Test]
        public void Given_MidRouteSplat_When_Constructing_Then_ThrowsArgumentException()
        {
            // A splat is a tail-only catch-all; placing it before another segment is rejected at
            // definition time so the trailing segment can never be silently swallowed.
            // The complement (a terminal splat capturing the whole tail) is verified by
            // Given_SplatRoute_When_TailHasMultipleSegments_Then_CapturesWholeTail below.
            // Act + Assert
            Assert.Throws<ArgumentException>(() => new RouteTree(new[] { Route("files/*/download") }));
        }

        #endregion

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
