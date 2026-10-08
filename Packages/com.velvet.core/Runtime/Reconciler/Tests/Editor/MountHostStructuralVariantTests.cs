using System;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that structural variants (<c>first:</c>, <c>last:</c>, <c>only:</c>) are evaluated for children
    /// reconciled directly under an element no VNode created: the element a tree is mounted into, and a portal's
    /// target. They are likewise re-derived for the children of a context-provider wrapper, when a Portal's
    /// teardown leaves others on its target, and when a time-sliced reconcile finishes. As in CSS, the parent's other element children are siblings too, those this tree did not put
    /// there among them.
    /// </summary>
    /// <remarks>
    /// Each payload is an inline width, so a variant that did not apply reads back as the unset width.
    /// </remarks>
    [TestFixture]
    internal sealed class MountHostStructuralVariantTests
    {
        private MountedTree? _mounted;
        private static StateUpdater<int> s_setCount;
        private static int s_initialCount;
        private static VisualElement? s_portalTarget;
        private static readonly ComponentContext<string> s_context = ComponentContext<string>.Create("light");

        [SetUp]
        public void SetUp()
        {
            s_setCount = default;
            s_initialCount = 2;
            s_portalTarget = null;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        private static string Width(VisualElement element)
            => element.style.width.keyword == StyleKeyword.Undefined
                ? element.style.width.value.value.ToString(CultureInfo.InvariantCulture)
                : "-";

        private static string Widths(VisualElement parent) => string.Join("|", parent.Children().Select(Width));

        // What CSS gives a row named "row…" under <parent>, read off the live children rather than assumed:
        // the unrelated child does not carry a payload, and it still takes a place in the sibling order.
        private static string WidthsByCss(VisualElement parent)
            => string.Join("|", parent.Children().Select((child, index) =>
                !child.name.StartsWith("row") ? "-"
                : index == 0 ? "10"
                : index == parent.childCount - 1 ? "30"
                : "-"));

        private static VNode?[] Rows(int count, string className)
            => Enumerable.Range(0, count)
                .Select(index => (VNode?)V.Div(name: "row" + index, className: className)).ToArray();

        [Component]
        private static VNode GrowingRows()
        {
            var (count, setCount) = Hooks.UseState(s_initialCount);
            s_setCount = setCount;
            return V.Fragment(children: Rows(count, "last:w-[30px]"));
        }

        [Component]
        private static VNode GrowingPortalRows()
        {
            var (count, setCount) = Hooks.UseState(1);
            s_setCount = setCount;
            return V.Portal(s_portalTarget!, children: Rows(count, "last:w-[30px]"));
        }

        [Test]
        public void Given_ALoneDivAtTheMountRoot_When_Mounted_Then_ItsOnlyVariantApplies()
        {
            // Arrange
            var host = new VisualElement();

            // Act
            _mounted = V.Mount(host, V.Div(name: "row0", className: "only:w-[20px]"));

            // Assert
            Assert.That(Widths(host), Is.EqualTo("20"));
        }

        [Test]
        public void Given_AFragmentAtTheMountRoot_When_Mounted_Then_FirstAndLastVariantsLandOnItsEnds()
        {
            // Arrange
            var host = new VisualElement();

            // Act
            _mounted = V.Mount(host,
                V.Fragment(children: Rows(3, "first:w-[10px] last:w-[30px]")));

            // Assert
            Assert.That(Widths(host), Is.EqualTo("10|-|30"));
        }

        [Test]
        public void Given_RowsAtTheMountRoot_When_ARowIsAdded_Then_LastMovesToTheNewRow()
        {
            // Arrange
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(GrowingRows, key: "rows"));

            // Act
            s_setCount.Invoke(3);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Widths(host), Is.EqualTo("-|-|30"));
        }

        [Test]
        public void Given_RowsAtTheMountRoot_When_TheLastRowIsRemoved_Then_LastMovesToTheNewEnd()
        {
            // Arrange
            s_initialCount = 3;
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(GrowingRows, key: "rows"));

            // Act
            s_setCount.Invoke(2);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Widths(host), Is.EqualTo("-|30"));
        }

        [Test]
        public void Given_AComponentWithinADiv_When_ItAddsARow_Then_LastMovesToTheNewRow()
        {
            // Arrange
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Div(name: "list", children: new VNode?[]
            {
                V.Component(GrowingRows, key: "rows"),
            }));
            var list = host.Q<VisualElement>("list");

            // Act — only the component re-renders, so nothing patches the div around it.
            s_setCount.Invoke(3);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Widths(list), Is.EqualTo("-|-|30"));
        }

        [Test]
        public void Given_AHostHoldingAnUnrelatedChild_When_RowsAreMounted_Then_FirstAndLastFollowTheWholeChildList()
        {
            // Arrange
            var host = new VisualElement();
            host.Add(new VisualElement { name = "unrelated" });

            // Act
            _mounted = V.Mount(host, V.Fragment(children: Rows(2, "first:w-[10px] last:w-[30px]")));

            // Assert
            Assert.That(Widths(host), Is.EqualTo(WidthsByCss(host)));
        }

        [Test]
        public void Given_ATargetHoldingAnUnrelatedChild_When_APortalMountsRows_Then_FirstAndLastFollowTheWholeChildList()
        {
            // Arrange
            var target = new VisualElement();
            target.Add(new VisualElement { name = "unrelated" });

            // Act
            _mounted = V.Mount(new VisualElement(),
                V.Portal(target, children: Rows(2, "first:w-[10px] last:w-[30px]")));

            // Assert
            Assert.That(Widths(target), Is.EqualTo(WidthsByCss(target)));
        }

        [Component]
        private static VNode RemovablePortals()
        {
            var (count, setCount) = Hooks.UseState(2);
            s_setCount = setCount;
            return V.Fragment(children: new VNode?[]
            {
                V.Portal(s_portalTarget!, key: "first",
                    children: new VNode?[] { V.Div(name: "row0", className: "last:w-[30px]") }),
                count == 2
                    ? V.Portal(s_portalTarget!, key: "second",
                        children: new VNode?[] { V.Div(name: "row1", className: "last:w-[30px]") })
                    : null,
            });
        }

        [Test]
        public void Given_TwoPortalsOnOneTarget_When_TheSecondUnmounts_Then_LastMovesToTheRemainingRow()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            _mounted = V.Mount(new VisualElement(), V.Component(RemovablePortals, key: "portals"));

            // Act — the first portal's own children are unchanged, so only the teardown touches the target.
            s_setCount.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Widths(s_portalTarget!), Is.EqualTo("30"));
        }

        [Test]
        public void Given_AProviderWrapperBuiltForAController_When_ItHoldsRows_Then_FirstAndLastLandOnItsEnds()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var bridge = (IReconcilerBridge)reconciler;

            // Act
            var wrapper = bridge.CreateElementForController(
                V.Provider(s_context, "value", Rows(3, "first:w-[10px] last:w-[30px]")));

            // Assert
            Assert.That(Widths(wrapper), Is.EqualTo("10|-|30"));
        }

        [Test]
        public void Given_AProviderWrapperBuiltForAController_When_ItIsPatchedWithAnotherRow_Then_LastMovesToIt()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var bridge = (IReconcilerBridge)reconciler;
            var before = V.Provider(s_context, "value", Rows(2, "last:w-[30px]"));
            var wrapper = bridge.CreateElementForController(before);

            // Act
            bridge.PatchNodeForController(wrapper, before, V.Provider(s_context, "value", Rows(3, "last:w-[30px]")));

            // Assert
            Assert.That(Widths(wrapper), Is.EqualTo("-|-|30"));
        }

        [Test]
        public void Given_ABudgetedReconcileUnderAHost_When_ItIsResumedToTheEnd_Then_FirstAndLastLandOnTheEnds()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var host = new VisualElement();
            reconciler.Reconcile(host, Array.Empty<VNode>(), Rows(3, "first:w-[10px] last:w-[30px]"),
                frameBudgetMs: 0.001);
            Assume.That(reconciler.HasPendingWork, Is.True, "Precondition: the budget parked the diff");

            // Act
            reconciler.ContinueReconcile();

            // Assert
            Assert.That(Widths(host), Is.EqualTo("10|-|30"));
        }

        [Test]
        public void Given_APortalWithOneRow_When_ItsDeclarerAddsARow_Then_LastMovesToTheNewRow()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            _mounted = V.Mount(new VisualElement(), V.Component(GrowingPortalRows, key: "portal"));

            // Act
            s_setCount.Invoke(2);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Widths(s_portalTarget!), Is.EqualTo("-|30"));
        }
    }
}
