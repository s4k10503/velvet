using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the attribute variants — <c>data-[key=value]:</c> / <c>data-[key]:</c> and the
    /// <c>aria-</c> counterparts — an element styled by its OWN carried attribute values. UI Toolkit has no
    /// HTML attributes, so the element supplies them via the <c>Data</c> / <c>Aria</c> props, stored in the
    /// reconciler's per-element side-table and matched by the variant. The payload is asserted via the
    /// element's class list. Off-panel; reactivity to a changed prop comes through the props patch path.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class AttributeVariantTests
    {
        private static VisualElement El(ReconcilerScope scope) => scope.Root.Q<VisualElement>("el");

        private static FiberElementProps DataProps(params (string key, string value)[] pairs)
        {
            var map = new Dictionary<string, string>();
            foreach (var (key, value) in pairs)
            {
                map[key] = value;
            }
            return new FiberElementProps { Data = map };
        }

        [Test]
        public void Given_DataKeyValueVariant_When_AttributeMatches_Then_PayloadApplied()
        {
            // Arrange/Act — data-[state=open]:bg-mark on an element carrying state=open.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "data-[state=open]:bg-mark", name: "el", props: DataProps(("state", "open"))),
            });

            // Assert
            Assert.IsTrue(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_DataKeyValueVariant_When_AttributeValueDiffers_Then_PayloadAbsent()
        {
            // Arrange/Act — the element carries state=closed, so the open rule does not match.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "data-[state=open]:bg-mark", name: "el", props: DataProps(("state", "closed"))),
            });

            // Assert
            Assert.IsFalse(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_DataPresenceVariant_When_KeyPresent_Then_PayloadApplied()
        {
            // Arrange/Act — data-[loading]:bg-mark is a presence test; the element carries the loading key.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "data-[loading]:bg-mark", name: "el", props: DataProps(("loading", "true"))),
            });

            // Assert
            Assert.IsTrue(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_DataPresenceVariant_When_KeyAbsent_Then_PayloadAbsent()
        {
            // Arrange/Act — no loading key on the element, so the presence rule is off.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "data-[loading]:bg-mark", name: "el", props: DataProps(("other", "x"))),
            });

            // Assert
            Assert.IsFalse(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_AriaKeyValueVariant_When_AttributeMatches_Then_PayloadApplied()
        {
            // Arrange/Act — aria-[expanded=true]:bg-mark on an element carrying aria expanded=true.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "aria-[expanded=true]:bg-mark", name: "el",
                    props: new FiberElementProps { Aria = new Dictionary<string, string> { ["expanded"] = "true" } }),
            });

            // Assert
            Assert.IsTrue(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_DataAndAriaSameKey_When_OnlyAriaMatches_Then_DataRuleDoesNotLeakOntoAria()
        {
            // Arrange/Act — the data and aria namespaces are independent: an aria-[k=v] rule must not be
            // satisfied by a data attribute of the same key/value. Here only the data attribute is set.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "aria-[busy=true]:bg-mark", name: "el", props: DataProps(("busy", "true"))),
            });

            // Assert — the data attribute does not satisfy the aria rule (namespaces are distinct).
            Assert.IsFalse(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_DataVariantApplied_When_AttributeValueChanges_Then_PayloadReDerives()
        {
            // Arrange — payload on because state=open.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: "data-[state=open]:bg-mark", key: "x", name: "el", props: DataProps(("state", "open"))),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            Assume.That(El(scope).ClassListContains("bg-mark"), Is.True, "Precondition: payload on while state=open");

            // Act — the attribute value changes to closed via the props patch path.
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[]
            {
                V.Div(className: "data-[state=open]:bg-mark", key: "x", name: "el", props: DataProps(("state", "closed"))),
            });

            // Assert — the payload clears (reactivity to a controlled attribute change).
            Assert.IsFalse(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_AttributeVariantToken_When_Mounted_Then_TokenIsNotInClassList()
        {
            // Arrange/Act — the attribute token must never enter the USS class list (it is side-table-owned).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "data-[state=open]:bg-mark", name: "el", props: DataProps(("state", "open"))),
            });

            // Assert
            Assert.IsFalse(El(scope).ClassListContains("data-[state=open]:bg-mark"));
        }

        [Test]
        public void Given_EmptyValueEqualityVariant_When_PresentAttributeHasNoValue_Then_PayloadApplied()
        {
            // Arrange/Act — data-[state=]: tests for the empty-string value. The element carries the key with a
            // null value (a valueless attribute), which resolves to "" — so the equality rule matches.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "data-[state=]:bg-mark", name: "el", props: DataProps(("state", null))),
            });

            // Assert
            Assert.IsTrue(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_EmptyValueEqualityVariant_When_PresentAttributeHasNonEmptyValue_Then_PayloadAbsent()
        {
            // Arrange/Act — the empty-value rule is still an exact-equality test, so a non-empty value is off.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "data-[state=]:bg-mark", name: "el", props: DataProps(("state", "open"))),
            });

            // Assert
            Assert.IsFalse(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_TypedToggle_When_DataSuppliedViaParam_Then_PayloadApplied()
        {
            // Arrange/Act — a typed Toggle (not a Div/Span) declares the data-[...] variant in its className AND
            // supplies the matching attribute via the data: parameter the factory threads onto its props.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Toggle(className: "data-[state=open]:bg-mark", name: "el",
                    data: new Dictionary<string, string> { ["state"] = "open" }),
            });

            // Assert — the data- variant reaches the typed widget factories, not just Div/Span.
            Assert.IsTrue(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_TypedButton_When_AriaSuppliedViaParam_Then_PayloadApplied()
        {
            // Arrange/Act — a typed Button supplies an aria attribute via the aria: parameter and matches its
            // aria-[...] variant.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Button(className: "aria-[expanded=true]:bg-mark", name: "el",
                    aria: new Dictionary<string, string> { ["expanded"] = "true" }),
            });

            // Assert
            Assert.IsTrue(El(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_Div_When_DataSuppliedViaParam_Then_PayloadApplied()
        {
            // Arrange/Act — V.Div supplies a data-* attribute via its own data: convenience parameter
            // (rather than an explicit props: bag) and matches its data-[...] variant.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "data-[state=open]:bg-mark", name: "el",
                    data: new Dictionary<string, string> { ["state"] = "open" }),
            });

            // Assert — the data: parameter reaches Div, not just typed widget factories.
            Assert.IsTrue(El(scope).ClassListContains("bg-mark"));
        }
    }
}
