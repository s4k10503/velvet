using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Behavioural coverage for stacked variants (<c>dark:hover:</c>, <c>hover:dark:</c>), driven by REAL UI
    /// events and the <see cref="VelvetTheme"/> toggle. A stacked leaf applies iff ALL of its conditions hold
    /// simultaneously and clears when any turns off, order-independently. Two-deep is the certified path
    /// (deeper nesting falls out of the same recursion but is documented best-effort). GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class StackedVariantBehaviorTests
    {
        private VisualElement _root;
        private bool _darkBefore;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _darkBefore = VelvetTheme.IsDark;
            VelvetTheme.IsDark = false;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            VelvetTheme.IsDark = _darkBefore;
        }

        private Label MountLeaf(string className)
        {
            _mounted = V.Mount(_root, V.Label(name: "leaf", className: className, text: "x"));
            return _root.Q<Label>("leaf");
        }

        [Test]
        public void Given_DarkHoverLeaf_When_DarkThenHover_Then_PayloadApplied()
        {
            // Arrange — dark:hover:bg-hot; flip dark on so the outer gate opens but hover (inner) is still off.
            var leaf = MountLeaf("dark:hover:bg-hot");
            VelvetTheme.IsDark = true;
            Assume.That(leaf.ClassListContains("bg-hot"), Is.False, "Precondition: dark alone does not apply (hover off)");

            // Act — the pointer goes over (inner gate opens too).
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the leaf applies because BOTH dark and hover hold.
            Assert.IsTrue(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_DarkHoverLeafApplied_When_ThemeFlipsToLight_Then_PayloadRemoved()
        {
            // Arrange — applied while dark AND hovered.
            var leaf = MountLeaf("dark:hover:bg-hot");
            VelvetTheme.IsDark = true;
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
            Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: applied while dark AND hovered");

            // Act — the outer (dark) gate closes.
            VelvetTheme.IsDark = false;

            // Assert — the leaf clears (the AND no longer holds).
            Assert.IsFalse(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_DarkHoverLeafApplied_When_PointerLeaves_Then_PayloadRemoved()
        {
            // Arrange — applied while dark AND hovered.
            var leaf = MountLeaf("dark:hover:bg-hot");
            VelvetTheme.IsDark = true;
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
            Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: applied while dark AND hovered");

            // Act — the inner (hover) gate closes.
            using (var evt = PointerOutEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the leaf clears.
            Assert.IsFalse(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_DarkHoverLeaf_When_HoveredButNotDark_Then_PayloadNotApplied()
        {
            // Arrange — dark:hover:bg-hot in the light theme (outer gate closed).
            var leaf = MountLeaf("dark:hover:bg-hot");

            // Act — only the inner (hover) signal fires.
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — hover alone never lights a dark:hover: leaf (the AND withholds it).
            Assert.IsFalse(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_HoverDarkLeaf_When_HoverThenDark_Then_PayloadApplied()
        {
            // Arrange — hover:dark:bg-hot (the reverse order); hover first opens the outer gate, dark still off.
            var leaf = MountLeaf("hover:dark:bg-hot");
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
            Assume.That(leaf.ClassListContains("bg-hot"), Is.False, "Precondition: hover alone does not apply (dark off)");

            // Act — the theme flips to dark (inner gate opens).
            VelvetTheme.IsDark = true;

            // Assert — applies, proving stacking is order-independent (hover:dark: == dark:hover:).
            Assert.IsTrue(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_HoverWithStaticScalePayload_When_Hovered_Then_InlineNegativeMarginApplied()
        {
            // Arrange — hover:-mt-2; the payload -mt-2 has no '[' and is a static-scale name, so the variant
            // apply gate must route it to the inline resolver rather than add it as a (never-matching) class.
            var leaf = MountLeaf("hover:-mt-2");

            // Act
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the inline margin-top is the negated --space-2 (8px).
            Assert.That(leaf.style.marginTop.value.value, Is.EqualTo(-8f));
        }
    }
}
