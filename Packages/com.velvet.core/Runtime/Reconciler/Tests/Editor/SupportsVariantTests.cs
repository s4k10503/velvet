using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the feature-query variant <c>supports-[&lt;property&gt;:&lt;value&gt;]:</c>. UI Toolkit
    /// targets one fixed engine, so a feature query is STATIC: a well-formed token is always-applied (the
    /// property is, by construction, one the author is using), a malformed one never applies, and the token
    /// itself never enters the USS class list (it is side-table-owned). The payload is asserted via the
    /// element's class list (USS payload) or its inline style (arbitrary-value payload). Off-panel. GWT,
    /// one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class SupportsVariantTests
    {
        private static VisualElement El(ReconcilerScope scope) => scope.Root.Q<VisualElement>("el");

        [Test]
        public void Given_SupportsVariant_When_WellFormed_Then_PayloadApplied()
        {
            // Arrange/Act — supports-[display:flex]: is well-formed, so in UITK it is always-applied.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "supports-[display:flex]:bg-mark", name: "el"),
            });

            // Assert
            Assert.IsTrue(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_SupportsVariantWithArbitraryPayload_When_WellFormed_Then_InlineValueApplied()
        {
            // Arrange/Act — an arbitrary-value payload applies through the Supports layer as an inline style.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "supports-[display:flex]:w-[200px]", name: "el"),
            });

            // Assert
            Assert.That(El(scope).style.width.value.value, Is.EqualTo(200f));
        }

        [Test]
        public void Given_MalformedSupportsVariant_When_Mounted_Then_PayloadAbsent()
        {
            // Arrange/Act — the bracket has no property:value ':', so the token is malformed and never applies.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "supports-[flex]:bg-mark", name: "el"),
            });

            // Assert
            Assert.IsFalse(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_SupportsVariantToken_When_Mounted_Then_TokenIsNotInClassList()
        {
            // Arrange/Act — the feature-query token must never enter the USS class list (it is side-table-owned).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "supports-[display:flex]:bg-mark", name: "el"),
            });

            // Assert
            Assert.IsFalse(El(scope).ClassListContains("supports-[display:flex]:bg-mark"));
        }

        [Test]
        public void Given_SupportsVariantApplied_When_VariantRemovedOnPatch_Then_PayloadCleared()
        {
            // Arrange — payload on because the supports- token is present.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "supports-[display:flex]:bg-mark", key: "x", name: "el") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            Assume.That(El(scope).ClassListContains("bg-mark"), Is.True, "Precondition: payload on while token present");

            // Act — the class list drops the supports- token on a patch.
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[]
            {
                V.Div(className: "other", key: "x", name: "el"),
            });

            // Assert — the config pass clears the previously-applied payload.
            Assert.IsFalse(El(scope).ClassListContains("bg-mark"));
        }
    }
}
