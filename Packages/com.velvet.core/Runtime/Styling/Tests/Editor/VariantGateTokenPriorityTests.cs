using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Several variants naming one utility, for the families Velvet realises in C# rather than in USS
    /// (gap / divide / grid and the wrapper-less paint layers). <see cref="StyleClassProjection"/>
    /// decides which CLASSES may sit on the element and cannot decide which of two same-family tokens a
    /// class-driven pass reads, because that is settled by the order of the composed array: the tracked
    /// gate-token list is what orders it. It must rank by the priority each payload was applied at, and
    /// within one priority by where the className declares the rule — never by the order the signals fired.
    /// </summary>
    /// <remarks>
    /// Every signal here is driven off panel: <c>dark:</c> through <see cref="VelvetTheme"/>'s theme signal,
    /// which the conditional manipulator subscribes to on attach, <c>hover:</c> through the element's own
    /// callback registry, and <c>data-[…]:</c> through a props patch. <c>hover:</c> ranks above <c>dark:</c>
    /// in the precedence table and every <c>data-[…]:</c> rule shares one layer, so which payload each case
    /// owes its win to is a declared fact rather than a coincidence of the fixture. GWT, one assert per case.
    /// </remarks>
    [TestFixture]
    internal sealed class VariantGateTokenPriorityTests
    {
        // --space-4 == 16px, --space-8 == 32px (see _tokens.uss).
        private const float Space4 = 16f;
        private const float Space8 = 32f;

        // The blur reported for an element carrying no shadow at all, kept apart from every preset's blur so
        // a case where nothing painted cannot pass as a case where the right preset did.
        private const float NoShadow = -1f;

        [TearDown]
        public void TearDown() => VelvetTheme.IsDark = false;

        private static VisualElement Mount(ReconcilerScope scope, string className, int childCount = 0)
        {
            var children = new VNode[childCount];
            for (var i = 0; i < childCount; i++)
            {
                children[i] = V.Div(className: "child");
            }
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(className: className, name: "card", children: children) });
            return scope.Root.Q<VisualElement>("card");
        }

        private static void Hover(VisualElement element)
        {
            using var evt = PointerOverEvent.GetPooled();
            element.SimulateEvent(evt);
        }

        private static float BlurOf(ReconcilerScope scope, VisualElement element)
            => scope.Reconciler.Context.ShadowBindings.TryGetValue(element, out var binding)
                ? binding.Spec.Blur
                : NoShadow;

        // Lights both layers of "bg-[#FFFFFF] dark:shadow-lg hover:shadow-sm" in the given order and reports
        // the blur the shadow paint settled on. Each order gets its own reconciler and its own element, since
        // the theme signal is process-wide.
        private static float BlurAfterBothLit(bool hoverFirst)
        {
            VelvetTheme.IsDark = false;
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "bg-[#FFFFFF] dark:shadow-lg hover:shadow-sm");
            if (hoverFirst)
            {
                Hover(card);
                VelvetTheme.IsDark = true;
            }
            else
            {
                VelvetTheme.IsDark = true;
                Hover(card);
            }
            return BlurOf(scope, card);
        }

        // Sets both attributes of "data-[state=open]:shadow-lg data-[busy=true]:shadow-sm" one at a time, in
        // the given order, and reports the blur the shadow paint settled on. A data- rule re-evaluates on a
        // props patch, so each step is its own render.
        private static float BlurAfterBothAttributesSet(bool stateFirst)
        {
            using var scope = new ReconcilerScope();
            var none = AttributeCard(new Dictionary<string, string>());
            var one = AttributeCard(stateFirst
                ? new Dictionary<string, string> { ["state"] = "open" }
                : new Dictionary<string, string> { ["busy"] = "true" });
            var both = AttributeCard(new Dictionary<string, string>
            {
                ["state"] = "open",
                ["busy"] = "true",
            });
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), none);
            scope.Reconciler.Reconcile(scope.Root, none, one);
            scope.Reconciler.Reconcile(scope.Root, one, both);
            return BlurOf(scope, scope.Root.Q<VisualElement>("card"));
        }

        private static VNode[] AttributeCard(Dictionary<string, string> data)
            => new VNode[]
            {
                V.Div(className: "bg-[#FFFFFF] data-[state=open]:shadow-lg data-[busy=true]:shadow-sm",
                    name: "card", data: data),
            };

        [Test]
        public void Given_TwoVariantsAssertingOneGapTokenWithNoLiteralBase_When_OneTurnsOff_Then_TheGapKeepsDriving()
        {
            // Arrange — nothing declares gap-4 literally, so the token exists only while a variant asserts it.
            // Both do; the explicit flex-row fixes the axis the gap writes its margin on.
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "flex flex-row dark:gap-4 hover:gap-4", childCount: 2);
            VelvetTheme.IsDark = true;
            Hover(card);
            var spacedWhileBothLit = card[1].style.marginLeft.value.value;

            // Act — only the dark layer turns off. The hover layer still asserts gap-4, and the projection
            // duly leaves the class on the element.
            VelvetTheme.IsDark = false;

            // Assert — both edges, since a container that never got its gap would satisfy the second half on
            // its own. A token dropped with the layer that left stops driving the manipulator, and the
            // spacing disappears while the class sits there.
            Assert.That((spacedWhileBothLit, card[1].style.marginLeft.value.value),
                Is.EqualTo((Space4, Space4)));
        }

        [Test]
        public void Given_TwoPrioritiesOfTheShadowFamily_When_TheSameStateIsReachedByEitherSignalOrder_Then_TheSameShadowPaints()
        {
            // Arrange — the oracle is the payload the precedence table elects, painted from a literal token:
            // hover: outranks dark:, so shadow-sm is what both orders owe.
            using var oracleScope = new ReconcilerScope();
            var expected = BlurOf(oracleScope, Mount(oracleScope, "bg-[#FFFFFF] shadow-sm"));

            // Act — the same two signals, in both orders.
            var hoverThenDark = BlurAfterBothLit(hoverFirst: true);
            var darkThenHover = BlurAfterBothLit(hoverFirst: false);

            // Assert — one rendering, whichever path the window took to reach the state. Comparing the two
            // against the elected preset rather than only against each other is what keeps a pair that
            // agreed on the WRONG shadow (or on none) from passing.
            Assert.That((hoverThenDark, darkThenHover), Is.EqualTo((expected, expected)));
        }

        [Test]
        public void Given_TwoAttributeRulesOfTheShadowFamily_When_TheSameStateIsReachedByEitherRuleOrder_Then_TheSameShadowPaints()
        {
            // Arrange — both rules sit on the data- rank, so their values decide, as Tailwind emits them:
            // "busy=true" sorts before "state=open", so shadow-lg is what both orders owe.
            using var oracleScope = new ReconcilerScope();
            var expected = BlurOf(oracleScope, Mount(oracleScope, "bg-[#FFFFFF] shadow-lg"));

            // Act — the two attributes set one at a time, in both orders.
            var stateThenBusy = BlurAfterBothAttributesSet(stateFirst: true);
            var busyThenState = BlurAfterBothAttributesSet(stateFirst: false);

            // Assert
            Assert.That((stateThenBusy, busyThenState), Is.EqualTo((expected, expected)));
        }

        [Test]
        public void Given_OneValueClaimedByTwoAttributeRules_When_ItsLaterRuleOutranksTheOtherValue_Then_ThatValuePaints()
        {
            // Arrange — three rules of one family at one rank, emitted a=1, m=1, x=1. shadow-lg is claimed at a=1
            // and x=1 with shadow-sm between them, so ranking the token at its earlier rule would hand the
            // element to shadow-sm; ranked at its later one, it keeps it. shadow-sm is written last, so writing order
            // would hand it the element too.
            using var oracleScope = new ReconcilerScope();
            var expected = BlurOf(oracleScope, Mount(oracleScope, "bg-[#FFFFFF] shadow-lg"));
            using var scope = new ReconcilerScope();
            var lit = new Dictionary<string, string> { ["x"] = "1", ["m"] = "1", ["a"] = "1" };
            var tree = new VNode[]
            {
                V.Div(className: "bg-[#FFFFFF] data-[a=1]:shadow-lg data-[x=1]:shadow-lg data-[m=1]:shadow-sm",
                    name: "card", data: lit),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(BlurOf(scope, scope.Root.Q<VisualElement>("card")), Is.EqualTo(expected));
        }

        [Test]
        public void Given_AContainerBlanketHoverRule_When_TheChildDeclaresItsOwnHoverRule_Then_TheContainersRuleWins()
        {
            // Arrange — [&>*]:hover: is `.p > *:hover` on the child: hover's specificity, and an arbitrary variant,
            // which Tailwind emits after every named one, so it outranks the child's own hover:.
            using var oracleScope = new ReconcilerScope();
            var expected = BlurOf(oracleScope, Mount(oracleScope, "bg-[#FFFFFF] shadow-lg"));
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "[&>*]:hover:shadow-lg", children: new VNode[]
                {
                    V.Div(className: "bg-[#FFFFFF] hover:shadow-sm", name: "child"),
                }),
            });
            var child = scope.Root.Q<VisualElement>("child");

            // Act
            Hover(child);

            // Assert
            Assert.That(BlurOf(scope, child), Is.EqualTo(expected));
        }

        [Test]
        public void Given_AStackedVariantBesideThePlainOneItWraps_When_BothAreLit_Then_TheStackedOneWins()
        {
            // Arrange — dark:hover: carries hover's specificity and sorts after plain hover:, as Tailwind emits
            // it, so it wins while dark and hover are both on even though shadow-sm is written later.
            using var oracleScope = new ReconcilerScope();
            var expected = BlurOf(oracleScope, Mount(oracleScope, "bg-[#FFFFFF] shadow-lg"));
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "bg-[#FFFFFF] dark:hover:shadow-lg hover:shadow-sm");

            // Act
            VelvetTheme.IsDark = true;
            Hover(card);

            // Assert
            Assert.That(BlurOf(scope, card), Is.EqualTo(expected));
        }

        [Test]
        public void Given_AnEventDrivenHasRuleBesideAClassHasRule_When_BothAreLit_Then_TheOneTailwindEmitsLaterWins()
        {
            // Arrange — the has- rank is reached by two different suppliers: has-[:checked]: rides the
            // event-driven manipulator, has-[.class]: is a side table. Their arguments decide: ".error" sorts
            // before ":checked", so gap-4 wins though gap-8 is written later. A checked Toggle and an .error
            // child light both at once.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "flex flex-row has-[:checked]:gap-4 has-[.error]:gap-8", name: "row",
                    children: new VNode[]
                    {
                        V.Toggle(value: true),
                        V.Div(className: "error"),
                    }),
            });
            var row = scope.Root.Q<VisualElement>("row");

            // Act — the container's post-children pass has already derived both; re-derive so the checked
            // scan runs against the placed subtree.
            scope.Reconciler.Context.HasVariantManipulators[row].Rescan();

            // Assert — --space-4 == 16px.
            Assert.That(row[1].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_ATiedPairBesideARuleThatNeverApplies_When_BothOfThePairAreLit_Then_TheDeadRuleDoesNotRank()
        {
            // Arrange — first:hover: is on the structural config's own skip list, so it registers nothing and
            // can never apply a payload to anything. It still spells shadow-sm after a colon, which is all a
            // scan of the className would see, and it would outrank both data- rules if it ranked. Only the two
            // data- rules are real, and between them b=1 sorts later, so shadow-lg wins.
            using var oracleScope = new ReconcilerScope();
            var expected = BlurOf(oracleScope, Mount(oracleScope, "bg-[#FFFFFF] shadow-lg"));
            using var scope = new ReconcilerScope();
            var lit = new Dictionary<string, string> { ["b"] = "1", ["a"] = "1" };
            var tree = new VNode[]
            {
                V.Div(className: "bg-[#FFFFFF] data-[b=1]:shadow-lg data-[a=1]:shadow-sm first:hover:shadow-sm",
                    name: "card", data: lit),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(BlurOf(scope, scope.Root.Q<VisualElement>("card")), Is.EqualTo(expected));
        }

        [Test]
        public void Given_TwoTiedRulesResolved_When_TheClassNameSwapsTheirValues_Then_TheOtherValuePaints()
        {
            // Arrange — both rules lit on the data- rank, where b=1 sorts later, so shadow-lg paints.
            using var oracleScope = new ReconcilerScope();
            var expected = BlurOf(oracleScope, Mount(oracleScope, "bg-[#FFFFFF] shadow-sm"));
            using var scope = new ReconcilerScope();
            var lit = new Dictionary<string, string> { ["a"] = "1", ["b"] = "1" };
            var first = new VNode[]
            {
                V.Div(className: "bg-[#FFFFFF] data-[a=1]:shadow-sm data-[b=1]:shadow-lg", name: "card", data: lit),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);

            // Act — the two rules trade payloads; both stay lit and neither moves rank. What carries the change is
            // the attribute config pass clearing and re-applying both payloads on a class change, each with its
            // fresh place. A family that stopped cycling would keep the old payloads with nothing else to catch
            // it, which is the invariant this case pins.
            var second = new VNode[]
            {
                V.Div(className: "bg-[#FFFFFF] data-[b=1]:shadow-sm data-[a=1]:shadow-lg", name: "card", data: lit),
            };
            scope.Reconciler.Reconcile(scope.Root, first, second);

            // Assert — b=1 still sorts later and now carries shadow-sm, though shadow-lg is written last.
            Assert.That(BlurOf(scope, scope.Root.Q<VisualElement>("card")), Is.EqualTo(expected));
        }

        [Test]
        public void Given_ALiteralBaseTokenReassertedByTheStrongerVariant_When_AWeakerVariantNamesTheSameFamily_Then_TheStrongerPayloadPaints()
        {
            // Arrange — shadow-lg is declared literally AND behind hover:, with dark: naming a different
            // preset of the same family below it.
            using var oracleScope = new ReconcilerScope();
            var expected = BlurOf(oracleScope, Mount(oracleScope, "bg-[#FFFFFF] shadow-lg"));
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "bg-[#FFFFFF] shadow-lg dark:shadow-sm hover:shadow-lg");

            // Act
            VelvetTheme.IsDark = true;
            Hover(card);

            // Assert
            Assert.That(BlurOf(scope, card), Is.EqualTo(expected));
        }
    }
}
