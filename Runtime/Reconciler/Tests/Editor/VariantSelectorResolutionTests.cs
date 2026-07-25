using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies three stateless variant-selector families, each matched off the reconciler's own
    /// bookkeeping rather than a live UI Toolkit pseudo-class or CSS feature query: the attribute
    /// variants (<c>data-[key=value]:</c> / <c>data-[key]:</c> and the <c>aria-</c> counterparts),
    /// matched against an element's own carried <c>Data</c>/<c>Aria</c> props (UI Toolkit has no HTML
    /// attributes, so the reconciler's per-element side-table stands in for them, and reactivity to a
    /// changed prop comes through the props patch path); the structural (child-position) variants
    /// (<c>first:</c> / <c>last:</c> / <c>odd:</c> / <c>even:</c> / <c>only:</c> and the arbitrary
    /// <c>[&amp;:nth-child(N)]:</c> form), declared on a child but resolved against its position among
    /// siblings by the reconciler's post-children pass, so the payload re-derives when the child set
    /// changes; and the feature-query variant <c>supports-[&lt;property&gt;:&lt;value&gt;]:</c>, which
    /// is STATIC on UI Toolkit's one fixed engine — a well-formed token always applies, a malformed one
    /// never does. All three families never leak their own token into the USS class list (side-table
    /// owned) and are asserted purely off-panel via the class list (or inline style for an
    /// arbitrary-value payload). GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class VariantSelectorResolutionTests
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

        // A container of `count` children, each carrying childClass + a stable key + name c{i}.
        private static VNode Container(int count, string childClass)
        {
            var children = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                children[i] = V.Div(className: childClass, key: i.ToString(), name: "c" + i);
            }
            return V.Div(className: "container", children: children);
        }

        private static VisualElement Child(ReconcilerScope scope, int i) => scope.Root.Q<VisualElement>("c" + i);

        [Test]
        public void Given_OddVariant_When_Mounted_Then_FirstChildHasPayload()
        {
            // Arrange/Act — odd: marks the 1st, 3rd, … children (1-based odd).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "odd:bg-mark") });

            // Assert
            Assert.IsTrue(Child(scope, 0).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_OddVariant_When_Mounted_Then_SecondChildLacksPayload()
        {
            // Arrange/Act — the 2nd child (1-based even) is not matched by odd:.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "odd:bg-mark") });

            // Assert
            Assert.IsFalse(Child(scope, 1).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_EvenVariant_When_Mounted_Then_SecondChildHasPayload()
        {
            // Arrange/Act — even: marks the 2nd, 4th, … children.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "even:bg-mark") });

            // Assert
            Assert.IsTrue(Child(scope, 1).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_FirstVariant_When_Mounted_Then_FirstChildHasPayload()
        {
            // Arrange/Act
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "first:bg-mark") });

            // Assert
            Assert.IsTrue(Child(scope, 0).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_FirstVariant_When_Mounted_Then_NonFirstChildLacksPayload()
        {
            // Arrange/Act — first: must not leak onto a middle child.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "first:bg-mark") });

            // Assert
            Assert.IsFalse(Child(scope, 1).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_LastVariant_When_Mounted_Then_LastChildHasPayload()
        {
            // Arrange/Act
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "last:bg-mark") });

            // Assert
            Assert.IsTrue(Child(scope, 2).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_ArbitraryNthChild_When_Mounted_Then_TargetedChildHasPayload()
        {
            // Arrange/Act — [&:nth-child(2)]: targets the 2nd child (1-based).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "[&:nth-child(2)]:bg-mark") });

            // Assert
            Assert.IsTrue(Child(scope, 1).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_OnlyVariant_When_SingleChild_Then_ChildHasPayload()
        {
            // Arrange/Act — only: matches a sole child.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(1, "only:bg-mark") });

            // Assert
            Assert.IsTrue(Child(scope, 0).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_LastVariant_When_AChildIsAppended_Then_PreviousLastClearsPayload()
        {
            // Arrange — three children, the 3rd is last.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { Container(3, "last:bg-mark") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            Assume.That(Child(scope, 2).ClassListContains("bg-mark"), Is.True, "Precondition: 3rd child is last");

            // Act — a 4th child is appended (the structural pass re-derives every position).
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { Container(4, "last:bg-mark") });

            // Assert — the previously-last child drops the payload (reactivity).
            Assert.IsFalse(Child(scope, 2).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_LastVariant_When_AChildIsAppended_Then_NewLastGainsPayload()
        {
            // Arrange — three children.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { Container(3, "last:bg-mark") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);

            // Act — a 4th child is appended.
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { Container(4, "last:bg-mark") });

            // Assert — the new last child gains the payload.
            Assert.IsTrue(Child(scope, 3).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_LastVariantOnShadowWrappedChild_When_Mounted_Then_InnerGetsPayload()
        {
            // Arrange — the last child carries both last: and shadow-md, so it is wrapped in a shadow wrapper;
            // the structural pass must resolve the wrapper back to the inner (the side-table is keyed by it).
            using var scope = new ReconcilerScope();
            var children = new VNode[]
            {
                V.Div(className: "last:bg-mark", key: "0", name: "c0"),
                V.Div(className: "last:bg-mark", key: "1", name: "c1"),
                V.Div(className: "last:bg-mark shadow-md", key: "2", name: "c2"),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(className: "container", children: children) });

            // Assert — the inner of the wrapped last child gets the payload.
            Assert.IsTrue(Child(scope, 2).ClassListContains("bg-mark"));
        }

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
