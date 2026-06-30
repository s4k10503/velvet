using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for an Auto-axis gap (<c>gap-*</c>, no x/y suffix) flipping its leading edge at runtime
    /// when the container's flex direction changes (<c>flex-row</c> ↔ <c>flex-col</c>). Off-panel the gap manipulator
    /// resolves the axis from the direction class marker, so a re-render that swaps the marker must move the
    /// inter-child margin from the horizontal edge to the vertical edge AND clear the edge it abandoned — otherwise a
    /// row→column flip leaves a stale horizontal margin behind. The fixed direction cases are covered by
    /// GapParityTests; this pins the RUNTIME flip. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class GapAutoDirectionFlipTests
    {
        private readonly record struct ModeState(int Mode);

        private sealed class ModeStore : Store<ModeState>
        {
            public ModeStore() : base(new ModeState(0)) { }
            public void Set(int mode) => SetState(_ => new ModeState(mode));
            protected override void ResetCore() => SetState(_ => new ModeState(0));
        }

        private static ModeStore s_store;
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        // Mode 0: row direction; mode 1: column direction. Plain gap-4 = Auto axis (follows direction).
        [Component]
        private static VNode Host()
        {
            var mode = Hooks.UseStore(s_store, s => s.Mode);
            var dir = mode == 0 ? "flex-row" : "flex-col";
            return V.Div(name: "host", className: $"flex {dir} gap-4", children: new VNode[]
            {
                V.Label(name: "a", text: "a"),
                V.Label(name: "b", text: "b"),
            });
        }

        [Test]
        public void Given_AnAutoGapRow_When_Mounted_Then_TheSecondChildHasAHorizontalLeadingMargin()
        {
            // Arrange/Act — a flex-row gap-4 container is mounted.
            using var store = new ModeStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));

            // Assert — the inter-child gap lands on the horizontal (left) edge of the second child.
            Assert.AreNotEqual(StyleKeyword.Null, _root.Q<Label>("b").style.marginLeft.keyword);
        }

        [Test]
        public void Given_AnAutoGapRow_When_FlippedToColumn_Then_TheSecondChildGainsAVerticalLeadingMargin()
        {
            // Arrange — a row gap container, then flipped to column by a re-render.
            using var store = new ModeStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<Label>("b").style.marginTop.keyword, Is.EqualTo(StyleKeyword.Null),
                "Precondition: no vertical margin while in row direction");

            // Act — the direction flips to column.
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — the gap moves to the vertical (top) edge of the second child.
            Assert.AreNotEqual(StyleKeyword.Null, _root.Q<Label>("b").style.marginTop.keyword);
        }

        [Test]
        public void Given_AnAutoGapRow_When_FlippedToColumn_Then_TheAbandonedHorizontalMarginIsCleared()
        {
            // Arrange — a row gap container, then flipped to column by a re-render.
            using var store = new ModeStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<Label>("b").style.marginLeft.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: horizontal margin present while in row direction");

            // Act — the direction flips to column.
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — the stale horizontal margin is cleared (no leftover from the abandoned axis).
            Assert.AreEqual(StyleKeyword.Null, _root.Q<Label>("b").style.marginLeft.keyword);
        }
    }
}
