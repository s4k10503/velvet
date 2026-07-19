using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Panel-backed contract for <see cref="StyleSkewChildTranslateManipulator"/>: the approximate descendant
    /// shear a skewed caster applies to its direct children. A real Yoga pass is required (the seat reads each
    /// child's laid-out centroid), so these mount into a genuine <see cref="EditorWindow"/> panel and force a
    /// layout pass. The fixture pins that (1) a positive-skewX column leans its top and bottom rows opposite
    /// ways, (2) removing skew resets the children's translate, (3) a child added to an already-skewed steady
    /// state is seated by the reconciler's re-run (not by a geometry event, which is deliberately withheld), (4)
    /// an out-of-flow <c>.absolute</c> child is skipped, (5) a child's own static <c>translate-x-*</c> survives
    /// the parent being unskewed, and (6) a translate a child acquires AFTER it transitions out-of-flow survives
    /// the parent being unskewed rather than being clobbered by the stale pre-shear capture. GWT, one assert per
    /// case.
    /// </summary>
    [TestFixture]
    internal sealed class SkewChildTranslateManipulatorTests : PanelTestBase
    {
        private const string StyleUtilitiesPath =
            "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        // Class-driven (skew ⇄ no-skew) and items-driven components, each with its own updater so a test drives
        // exactly one. Reset per test in SetUp so a prior test's captured setter never leaks.
        private static StateUpdater<string> s_setClass;
        private static StateUpdater<string> s_setTranslateClass;
        private static StateUpdater<string[]> s_setItems;
        private static StateUpdater<string> s_setCasterClass;
        private static StateUpdater<string> s_setChildClass;

        protected override void LoadStyleSheets()
        {
            // A real panel is required for .absolute to resolve to position:absolute (the out-of-flow signal
            // StyleOutOfFlowChild reads on panel); skew and translate-x-* carry no USS rule, so the sheet does
            // not otherwise touch these trees.
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleUtilitiesPath);
            Assert.That(styleSheet, Is.Not.Null,
                $"Could not load Velvet's StyleUtilities.uss at '{StyleUtilitiesPath}'.");
            _window.rootVisualElement.styleSheets.Add(styleSheet);
        }

        public override void SetUp()
        {
            base.SetUp();
            s_setClass = default;
            s_setTranslateClass = default;
            s_setItems = default;
            s_setCasterClass = default;
            s_setChildClass = default;
        }

        private void Drain() => _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

        // Resolves layout, then delivers a GeometryChangedEvent so the manipulator reads the now-real centroids
        // (the EditMode player loop delivers neither on its own).
        private static void SeatViaGeometry(VisualElement caster)
        {
            ForcePanelUpdate(caster.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            caster.SimulateEvent(evt);
        }

        [Component]
        private static VNode RenderClassColumn()
        {
            var (cls, setClass) = Hooks.UseState("skew-x-12 w-[120px]");
            s_setClass = setClass;
            return V.Div(className: cls, name: "col", children: new VNode?[]
            {
                V.Div(key: "a", name: "a", className: "h-[24px]"),
                V.Div(key: "b", name: "b", className: "h-[24px]"),
            });
        }

        [Component]
        private static VNode RenderItemsColumn()
        {
            var (items, setItems) = Hooks.UseState(new[] { "a", "b" });
            s_setItems = setItems;
            // A FIXED caster box (w+h) so adding a row does not resize the caster: the manipulator only listens to
            // the CASTER's own GeometryChangedEvent, so an unchanged caster box guarantees the newly-added,
            // freshly-laid-out row can be seated by nothing but the reconciler's SyncChildTranslate re-run.
            return V.Div(className: "skew-x-12 w-[120px] h-[200px]", name: "col",
                children: items.Select(it => (VNode?)V.Div(key: it, name: it, className: "h-[24px]")).ToArray());
        }

        [Component]
        private static VNode RenderFlowTransitionColumn()
        {
            var (casterCls, setCaster) = Hooks.UseState("skew-x-12 w-[120px] h-[200px]");
            var (childCls, setChild) = Hooks.UseState("h-[24px]");
            s_setCasterClass = setCaster;
            s_setChildClass = setChild;
            return V.Div(className: casterCls, name: "col", children: new VNode?[]
            {
                V.Div(key: "m", name: "m", className: childCls),
            });
        }

        [Component]
        private static VNode RenderTranslateChildColumn()
        {
            var (cls, setClass) = Hooks.UseState("skew-x-12 w-[120px]");
            s_setTranslateClass = setClass;
            return V.Div(className: cls, name: "col", children: new VNode?[]
            {
                V.Div(key: "t", name: "t", className: "translate-x-1 h-[24px]"),
                V.Div(key: "b", name: "b", className: "h-[24px]"),
            });
        }

        [Test]
        public void Given_SkewX12ColumnWithFixedHeightRows_When_Reconciled_Then_TopRowTranslatesOppositeBottomRow()
        {
            // Arrange — a positive-skewX column of three fixed-height rows: the top row's centroid is above the
            // box centre and the bottom's below, so the shear carries them opposite ways along x.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                className: "skew-x-12 w-[120px]", name: "col", children: new VNode?[]
                {
                    V.Div(key: "a", name: "a", className: "h-[24px]"),
                    V.Div(key: "b", name: "b", className: "h-[24px]"),
                    V.Div(key: "c", name: "c", className: "h-[24px]"),
                }));
            var col = _window.rootVisualElement.Q<VisualElement>("col");
            var top = _window.rootVisualElement.Q<VisualElement>("a");
            var bottom = _window.rootVisualElement.Q<VisualElement>("c");

            // Act
            SeatViaGeometry(col);
            Assume.That(top.style.translate.keyword != StyleKeyword.Null
                && bottom.style.translate.keyword != StyleKeyword.Null, Is.True,
                "Precondition: the manipulator seated both the top and bottom rows");

            // Assert
            Assert.That(Mathf.Sign(top.style.translate.value.x.value),
                Is.Not.EqualTo(Mathf.Sign(bottom.style.translate.value.x.value)));
        }

        [Test]
        public void Given_SkewRemovedFromCaster_When_Patched_Then_ChildTranslateResetsToNull()
        {
            // Arrange — a skewed column whose children were seated by the manipulator.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderClassColumn));
            var col = _window.rootVisualElement.Q<VisualElement>("col");
            var child = _window.rootVisualElement.Q<VisualElement>("a");
            SeatViaGeometry(col);
            Assume.That(child.style.translate.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: the child carried a shear translate while the caster was skewed");

            // Act — patch the caster to a non-skewed class list on the same key.
            s_setClass.Invoke("w-[120px]");
            Drain();

            // Assert — Detach's RemoveManipulator cleared the shear translate the child had no class to restore.
            Assert.That(child.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ChildAddedToAlreadySkewedSteadyStateContainer_When_Reconciled_Then_NewChildReceivesCounterShearTranslate()
        {
            // Arrange — a skewed column of two rows, laid out at a FIXED box, then a third row added under the
            // SAME skew tokens. Seating the first two settles the caster box, so adding the third cannot resize
            // the caster and therefore fires no caster GeometryChangedEvent.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderItemsColumn));
            var col = _window.rootVisualElement.Q<VisualElement>("col");
            SeatViaGeometry(col);
            s_setItems.Invoke(new[] { "a", "b", "c" });
            Drain();
            // Lay out the freshly added row. The caster box is unchanged (fixed size), so no caster geometry event
            // fires and the manipulator's own geometry callback stays silent — isolating the next assertion to the
            // reconciler's steady-state SyncChildTranslate re-run as the ONLY thing that can seat the new row.
            ForcePanelUpdate(col.panel);
            var added = _window.rootVisualElement.Q<VisualElement>("c");
            Assume.That(added != null && !float.IsNaN(added.layout.height)
                && added.style.translate.keyword == StyleKeyword.Null, Is.True,
                "Precondition: the added row is laid out but NOT yet seated, so only SyncChildTranslate can seat it");

            // Act — a steady-state patch (skew tokens unchanged) after layout.
            s_setItems.Invoke(new[] { "a", "b", "c" });
            Drain();

            // Assert
            Assert.That(added.style.translate.keyword, Is.Not.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_OutOfFlowAbsoluteChildUnderSkewedContainer_When_Reconciled_Then_AbsoluteChildTranslateStaysNull()
        {
            // Arrange — a skewed column with one in-flow child and one out-of-flow .absolute child.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                className: "skew-x-12 w-[120px]", name: "col", children: new VNode?[]
                {
                    V.Div(key: "flow", name: "flow", className: "h-[24px]"),
                    V.Div(key: "abs", name: "abs", className: "absolute h-[24px]"),
                }));
            var col = _window.rootVisualElement.Q<VisualElement>("col");
            var flow = _window.rootVisualElement.Q<VisualElement>("flow");
            var abs = _window.rootVisualElement.Q<VisualElement>("abs");

            // Act
            SeatViaGeometry(col);
            Assume.That(flow.style.translate.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: the manipulator seated the in-flow child");
            Assume.That(abs.resolvedStyle.position, Is.EqualTo(Position.Absolute),
                "Precondition: the .absolute child resolved out of flow");

            // Assert — the out-of-flow child holds no seat in the slanted frame, so its translate is untouched.
            Assert.That(abs.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ChildWithExplicitTranslateXUnderCaster_When_CasterUnskewed_Then_ChildsOwnTranslateXSurvives()
        {
            // Arrange — a child carries its own translate-x-1 (4px) under a skewed caster, which overwrites it
            // with the shear seat while active.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderTranslateChildColumn));
            var col = _window.rootVisualElement.Q<VisualElement>("col");
            var child = _window.rootVisualElement.Q<VisualElement>("t");
            SeatViaGeometry(col);
            Assume.That(Mathf.Approximately(child.style.translate.value.x.value, 4f), Is.False,
                "Precondition: the manipulator overwrote the child's own translate-x while skewed");

            // Act — unskew the caster.
            s_setTranslateClass.Invoke("w-[120px]");
            Drain();

            // Assert — the child's own translate (captured before the shear overwrite) was restored, not nulled.
            Assert.That(child.style.translate.value.x.value, Is.EqualTo(4f).Within(0.01f));
        }

        [Test]
        public void Given_ChildWentOutOfFlowThenGainedOwnTranslate_When_CasterUnskewed_Then_ThatTranslateSurvives()
        {
            // Arrange — a child seated in-flow (its captured own-translate is Null), then transitioned out-of-flow
            // (.absolute) and settled, then given its OWN translate-x-1 (4px) AFTER the transition. The manipulator
            // relinquished the child when it went out-of-flow, so that 4px is legitimately the child's own — not a
            // shear seat — and must not be clobbered when the caster is later unskewed.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderFlowTransitionColumn));
            var col = _window.rootVisualElement.Q<VisualElement>("col");
            var child = _window.rootVisualElement.Q<VisualElement>("m");
            SeatViaGeometry(col);
            Assume.That(child.style.translate.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: the manipulator seated the in-flow child");

            // Act — first move the child out of flow and settle its resolved position, so the manipulator stops
            // owning it; then hand it its own translate-x-1; finally unskew the caster.
            s_setChildClass.Invoke("absolute h-[24px]");
            Drain();
            SeatViaGeometry(col);
            s_setChildClass.Invoke("absolute translate-x-1 h-[24px]");
            Drain();
            Assume.That(child.resolvedStyle.position == Position.Absolute
                && Mathf.Approximately(child.style.translate.value.x.value, 4f), Is.True,
                "Precondition: the out-of-flow child carries its OWN 4px translate before the caster is unskewed");
            s_setCasterClass.Invoke("w-[120px] h-[200px]");
            Drain();

            // Assert — unskew released the out-of-flow child untouched instead of restoring the stale Null capture.
            Assert.That(child.style.translate.value.x.value, Is.EqualTo(4f).Within(0.01f));
        }
    }
}
