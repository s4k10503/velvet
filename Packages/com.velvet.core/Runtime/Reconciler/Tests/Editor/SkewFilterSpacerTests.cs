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
    /// Pins the filter bounds-spacer: a skewed element under an inline filter (a filter clips the sheared
    /// silhouette to the layout rect, so a transparent last-child spacer widens the element's boundingBox to
    /// keep it) gets exactly one trailing spacer, the spacer is kept LAST so the positional child reconciler
    /// never mistakes it for a rendered child, keyed child reconciliation stays correct alongside it, and the
    /// spacer appears / vanishes with the filter. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class SkewFilterSpacerTests
    {
        private const string SkewFilter = "w-[200px] -skew-x-6 hue-rotate-90 flex flex-col";
        private const string SkewNoFilter = "w-[200px] -skew-x-6 flex flex-col";

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

        private FiberBatchScheduler Scheduler => _mounted.Root.Reconciler.Context.BatchScheduler;
        private VisualElement Box => _window.rootVisualElement.Q<VisualElement>("box");

        // The rendered (non-spacer) children in order, by name.
        private string[] ChildNames() => Enumerable.Range(0, Box.childCount)
            .Select(i => Box[i])
            .Where(c => !SilhouetteBoundsSpacer.IsSpacer(c))
            .Select(c => c.name)
            .ToArray();

        private int SpacerCount() => Enumerable.Range(0, Box.childCount)
            .Count(i => SilhouetteBoundsSpacer.IsSpacer(Box[i]));

        private bool SpacerIsLast() => Box.childCount > 0 && SilhouetteBoundsSpacer.IsSpacer(Box[Box.childCount - 1]);

        private void Mount()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderContainer));
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

        private sealed class TestHostWindow : EditorWindow { }
    }
}
