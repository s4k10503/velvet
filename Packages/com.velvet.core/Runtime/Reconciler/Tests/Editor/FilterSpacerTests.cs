using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the filter bounds-spacer (<see cref="SilhouetteBoundsSpacer"/>) of the drop-shadow paint layer. An inline
    /// filter renders its subject into an offscreen texture sized to the layout rect, so anything a paint layer
    /// bleeds beyond that rect — a shadow's blur/spread — gets clipped unless a transparent trailing spacer widens
    /// the element's boundingBox to cover it. The reconciler-safety mechanics are pinned here:
    /// <list type="bullet">
    /// <item>Exactly one trailing spacer while filtered; the spacer is kept LAST so the positional child
    /// reconciler never mistakes it for a rendered child, and keyed child reconciliation stays correct alongside
    /// it (insert / remove / reorder).</item>
    /// <item>The spacer appears when the filter is added on patch and disappears when it is removed, including a
    /// filter carried only by a state variant (e.g. <c>hover:blur-sm</c>), since the spacer must exist whenever a
    /// filter COULD apply, not only while the variant's state is active.</item>
    /// </list>
    /// GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class FilterSpacerTests : PanelTestBase
    {
        private const string ContainerFilter = "w-[200px] shadow-lg hue-rotate-90 flex flex-col";
        private const string ContainerNoFilter = "w-[200px] shadow-lg flex flex-col";
        private const string ShadowFilter = "w-[200px] h-[80px] shadow-lg hue-rotate-90";
        private const string ShadowNoFilter = "w-[200px] h-[80px] shadow-lg";

        private static StateUpdater<string[]> s_setItems;
        private static StateUpdater<string> s_setClass;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_setItems = default;
            s_setClass = default;
        }

        [Component]
        private static VNode RenderContainer()
        {
            var (cls, setClass) = Hooks.UseState(ContainerFilter);
            var (items, setItems) = Hooks.UseState(new[] { "a", "b", "c" });
            s_setClass = setClass;
            s_setItems = setItems;
            return V.Div(className: cls, name: "box",
                children: items.Select(it => (VNode?)V.Div(key: it, name: it, className: "h-[20px]")).ToArray());
        }

        [Component]
        private static VNode RenderCard()
        {
            var (cls, setClass) = Hooks.UseState(ShadowFilter);
            s_setClass = setClass;
            return V.Div(className: cls, name: "card");
        }

        // Mirrors RenderCard but with the no-filter class as the INITIAL UseState value, so a mount-only
        // test can observe the first-render state instead of a filtered mount patched off — V.Mount's
        // initial render + FlushImmediate is one synchronous call, so there is no window to swap the class
        // via the state setter between "mounted" and "flushed" the way a later patch can.
        [Component]
        private static VNode RenderCardWithoutFilter()
        {
            var (cls, setClass) = Hooks.UseState(ShadowNoFilter);
            s_setClass = setClass;
            return V.Div(className: cls, name: "card");
        }

        private VisualElement Box => _window.rootVisualElement.Q<VisualElement>("box");
        private VisualElement Card => _window.rootVisualElement.Q<VisualElement>("card");

        // The rendered (non-spacer) children in order, by name.
        private string[] ChildNames() => Enumerable.Range(0, Box.childCount)
            .Select(i => Box[i])
            .Where(c => !SilhouetteBoundsSpacer.IsSpacer(c))
            .Select(c => c.name)
            .ToArray();

        private int SpacerCount() => Enumerable.Range(0, Box.childCount)
            .Count(i => SilhouetteBoundsSpacer.IsSpacer(Box[i]));

        // Same shape as SpacerCount, over the shadow layer's own container (Card instead of Box) — kept
        // as a distinct method since the two containers are different elements from different components.
        private int ShadowSpacerCount() => Enumerable.Range(0, Card.childCount)
            .Count(i => SilhouetteBoundsSpacer.IsSpacer(Card[i]));

        private bool SpacerIsLast() => Box.childCount > 0 && SilhouetteBoundsSpacer.IsSpacer(Box[Box.childCount - 1]);

        private void Mount()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderContainer));
        }

        // Mounts the card component (RenderCard) instead of the container one (RenderContainer).
        private void MountShadowCard()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCard));
        }

        // Mounts RenderCardWithoutFilter, whose first render already carries the no-filter class.
        private void MountShadowCardWithoutFilter()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCardWithoutFilter));
        }

        private void SetItems(string[] items)
        {
            s_setItems.Invoke(items);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private void SetClass(string cls)
        {
            s_setClass.Invoke(cls);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        [Test]
        public void Given_AShadowedFilteredContainer_When_Mounted_Then_OneSpacerIsAdded()
        {
            Mount();

            Assert.That(SpacerCount(), Is.EqualTo(1));
        }

        [Test]
        public void Given_AShadowedFilteredContainer_When_Mounted_Then_TheSpacerIsLast()
        {
            Mount();

            Assert.That(SpacerIsLast(), Is.True);
        }

        [Test]
        public void Given_AShadowedFilteredContainer_When_Mounted_Then_TheRenderedChildrenAreIntact()
        {
            Mount();

            Assert.That(ChildNames(), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Given_AChildRemoved_When_Drained_Then_TheRemainingChildrenReconcileCorrectly()
        {
            Mount();
            SetItems(new[] { "a", "c" });

            Assert.That(ChildNames(), Is.EqualTo(new[] { "a", "c" }));
        }

        [Test]
        public void Given_ChildrenReorderedAndAdded_When_Drained_Then_TheChildrenReconcileCorrectly()
        {
            Mount();
            SetItems(new[] { "c", "a", "b", "d" });

            Assert.That(ChildNames(), Is.EqualTo(new[] { "c", "a", "b", "d" }));
        }

        [Test]
        public void Given_ChildrenReorderedAndAdded_When_Drained_Then_TheSpacerStaysLastAndSingle()
        {
            Mount();
            SetItems(new[] { "c", "a", "b", "d" });

            Assert.That(SpacerIsLast() && SpacerCount() == 1, Is.True);
        }

        [Test]
        public void Given_AllChildrenRemoved_When_Drained_Then_OnlyTheSpacerRemains()
        {
            Mount();
            SetItems(Array.Empty<string>());

            Assert.That(Box.childCount == 1 && SpacerIsLast(), Is.True);
        }

        [Test]
        public void Given_TheFilterRemoved_When_Drained_Then_TheSpacerIsGone()
        {
            Mount();
            Assume.That(SpacerCount(), Is.EqualTo(1), "Precondition: the spacer was present under the filter");
            SetClass(ContainerNoFilter);

            Assert.That(SpacerCount(), Is.EqualTo(0));
        }

        [Test]
        public void Given_TheFilterRemoved_When_Drained_Then_TheChildrenSurvive()
        {
            Mount();
            SetClass(ContainerNoFilter);

            Assert.That(ChildNames(), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Given_TheFilterAddedOnPatch_When_Drained_Then_TheSpacerAppears()
        {
            Mount();
            SetClass(ContainerNoFilter);
            var countWithoutFilter = SpacerCount();
            SetClass(ContainerFilter);

            // The unfiltered count is asserted with the filtered one: the spacer mounted with the container,
            // so a patch that added nothing would still show one here.
            Assert.That((countWithoutFilter, SpacerCount()), Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_AShadowedElementWithAVariantOnlyFilter_When_Patched_Then_ASpacerIsAdded()
        {
            // A filter carried only by a state variant (hover:blur-sm) is applied by a manipulator at state
            // time, outside the reconcile — so the spacer must exist whenever a filter COULD apply, not only
            // while the state is active.
            Mount();
            SetClass("w-[200px] shadow-lg hover:blur-sm flex flex-col");

            Assert.That(SpacerCount(), Is.EqualTo(1));
        }

        [Test]
        public void Given_AShadowedFilteredElement_When_Mounted_Then_ASpacerIsAdded()
        {
            MountShadowCard();

            Assert.That(ShadowSpacerCount(), Is.EqualTo(1));
        }

        [Test]
        public void Given_AShadowedElementWithoutFilter_When_Mounted_Then_NoSpacerIsAdded()
        {
            MountShadowCardWithoutFilter();

            Assert.That(ShadowSpacerCount(), Is.EqualTo(0));
        }

        [Test]
        public void Given_TheFilterRemovedOnPatch_When_Drained_Then_TheSpacerIsGone()
        {
            MountShadowCard();
            Assume.That(ShadowSpacerCount(), Is.EqualTo(1), "Precondition: the spacer was present under the filter");
            SetClass(ShadowNoFilter);

            Assert.That(ShadowSpacerCount(), Is.EqualTo(0));
        }

        [Test]
        public void Given_TheShadowFilterAddedOnPatch_When_Drained_Then_TheSpacerAppears()
        {
            MountShadowCard();
            SetClass(ShadowNoFilter);
            var countWithoutFilter = ShadowSpacerCount();
            SetClass(ShadowFilter);

            // Same pairing as the container sibling above.
            Assert.That((countWithoutFilter, ShadowSpacerCount()), Is.EqualTo((0, 1)));
        }
    }
}
