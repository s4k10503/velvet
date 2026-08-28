using System;
using NUnit.Framework;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
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

        [Test]
        public void Given_RouteTreeMatch_When_ReturnAnnotationsRead_Then_TheChainIsNullableAndItsParamsAreNot()
        {
            // Arrange
            var match = typeof(RouteTree).GetMethod(nameof(RouteTree.Match))!;
            var matchParams = typeof(RouteMatch).GetProperty(nameof(RouteMatch.Params))!.GetMethod!;

            // Act
            var annotations = (
                chain: NullableAnnotationProbe.ReturnAnnotation(match),
                parameters: NullableAnnotationProbe.ReturnAnnotation(matchParams));

            // Assert
            Assert.That(
                annotations,
                Is.EqualTo((
                    NullableAnnotationProbe.Annotation.Nullable,
                    NullableAnnotationProbe.Annotation.NotNullable)),
                "Match declares the null it returns for a path no branch consumes, against a sibling "
                + "declared non-null. Match's constructed nullable return type is what makes the compiler "
                + "emit the annotation as a flag array, so this case also fixes which element of that "
                + "array the probe reads");
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
