using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the filter bounds-spacer (<see cref="SilhouetteBoundsSpacer"/>) across the two paint layers that
    /// need it: skew and drop-shadow. An inline filter renders its subject into an offscreen texture sized to
    /// the layout rect, so anything a paint layer bleeds beyond that rect — a skewed silhouette's sheared
    /// corners, or a shadow's blur/spread — gets clipped unless a transparent trailing spacer widens the
    /// element's boundingBox to cover it. Both layers share the same reconciler-safety mechanics, pinned once
    /// here:
    /// <list type="bullet">
    /// <item>Skew: exactly one trailing spacer while filtered; the spacer is kept LAST so the positional
    /// child reconciler never mistakes it for a rendered child, and keyed child reconciliation stays correct
    /// alongside it (insert / remove / reorder).</item>
    /// <item>Shadow: a trailing spacer while filtered, using the same mechanism.</item>
    /// <item>Both: the spacer appears when the filter is added on patch and disappears when it is removed,
    /// including a filter carried only by a state variant (e.g. <c>hover:blur-sm</c>), since the spacer must
    /// exist whenever a filter COULD apply, not only while the variant's state is active. When both layers
    /// apply together, their two spacers coexist as trailing children without disturbing the rendered
    /// children.</item>
    /// </list>
    /// GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class FilterSpacerTests
    {
        private const string SkewFilter = "w-[200px] -skew-x-6 hue-rotate-90 flex flex-col";
        private const string SkewNoFilter = "w-[200px] -skew-x-6 flex flex-col";
        private const string ShadowFilter = "w-[200px] h-[80px] shadow-lg hue-rotate-90";
        private const string ShadowNoFilter = "w-[200px] h-[80px] shadow-lg";

        private EditorWindow _window;
        private MountedTree _mounted;
        private static StateUpdater<string[]> s_setItems;
        private static StateUpdater<string> s_setClass;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            s_setItems = default;
            s_setClass = default;
            _window = ScriptableObject.CreateInstance<TestHostWindow>();
            _window.position = new Rect(0, 0, 800, 600);
            _window.Show();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_window != null)
            {
                _window.Close();
                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }
        }

        [Component]
        private static VNode RenderContainer()
        {
            var (cls, setClass) = Hooks.UseState(SkewFilter);
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

        private FiberBatchScheduler Scheduler => _mounted.Root.Reconciler.Context.BatchScheduler;
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

        // Mounts the shadow layer's own component (RenderCard) instead of the skew layer's RenderContainer.
        private void MountShadowCard()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCard));
        }

        private void SetItems(string[] items)
        {
            s_setItems.Invoke(items);
            Scheduler.DrainImmediateForTest();
        }

        private void SetClass(string cls)
        {
            s_setClass.Invoke(cls);
            Scheduler.DrainImmediateForTest();
        }

        [Test]
        public void Given_ASkewedFilteredContainer_When_Mounted_Then_OneSpacerIsAdded()
        {
            Mount();

            Assert.That(SpacerCount(), Is.EqualTo(1));
        }

        [Test]
        public void Given_ASkewedFilteredContainer_When_Mounted_Then_TheSpacerIsLast()
        {
            Mount();

            Assert.That(SpacerIsLast(), Is.True);
        }

        [Test]
        public void Given_ASkewedFilteredContainer_When_Mounted_Then_TheRenderedChildrenAreIntact()
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
            SetClass(SkewNoFilter);

            Assert.That(SpacerCount(), Is.EqualTo(0));
        }

        [Test]
        public void Given_TheFilterRemoved_When_Drained_Then_TheChildrenSurvive()
        {
            Mount();
            SetClass(SkewNoFilter);

            Assert.That(ChildNames(), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Given_TheFilterAddedOnPatch_When_Drained_Then_TheSpacerAppears()
        {
            Mount();
            SetClass(SkewNoFilter);
            Assume.That(SpacerCount(), Is.EqualTo(0), "Precondition: no spacer without a filter");
            SetClass(SkewFilter);

            Assert.That(SpacerCount(), Is.EqualTo(1));
        }

        [Test]
        public void Given_ASkewedElementWithAVariantOnlyFilter_When_Patched_Then_ASpacerIsAdded()
        {
            // A filter carried only by a state variant (hover:blur-sm) is applied by a manipulator at state
            // time, outside the reconcile — so the spacer must exist whenever a filter COULD apply, not only
            // while the state is active.
            Mount();
            SetClass("w-[200px] -skew-x-6 hover:blur-sm flex flex-col");

            Assert.That(SpacerCount(), Is.EqualTo(1));
        }

        [Test]
        public void Given_SkewAndShadowAndFilter_When_Patched_Then_TwoSpacersAreAdded()
        {
            // Both paint layers reserve their own bounds; the two spacers coexist as trailing children.
            Mount();
            SetClass("w-[200px] -skew-x-6 shadow-lg hue-rotate-90 flex flex-col");

            Assert.That(SpacerCount(), Is.EqualTo(2));
        }

        [Test]
        public void Given_SkewAndShadowAndFilter_When_Patched_Then_TheRenderedChildrenAreStillIntact()
        {
            // Two trailing spacers must not disturb the reconciled children.
            Mount();
            SetClass("w-[200px] -skew-x-6 shadow-lg hue-rotate-90 flex flex-col");

            Assert.That(ChildNames(), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Given_SkewAndShadowAndFilter_When_Patched_Then_BothSpacersTrailAllRenderedChildren()
        {
            // The last two children are the spacers; every rendered child precedes them.
            Mount();
            SetClass("w-[200px] -skew-x-6 shadow-lg hue-rotate-90 flex flex-col");
            var trailingTwoAreSpacers = Box.childCount >= 2
                && SilhouetteBoundsSpacer.IsSpacer(Box[Box.childCount - 1])
                && SilhouetteBoundsSpacer.IsSpacer(Box[Box.childCount - 2]);

            Assert.That(trailingTwoAreSpacers, Is.True);
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
            MountShadowCard();
            SetClass(ShadowNoFilter);

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
            Assume.That(ShadowSpacerCount(), Is.EqualTo(0), "Precondition: no spacer without a filter");
            SetClass(ShadowFilter);

            Assert.That(ShadowSpacerCount(), Is.EqualTo(1));
        }

        private sealed class TestHostWindow : EditorWindow { }
    }
}
