using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Behavioural coverage for the element-local <c>checked:</c> variant, driven by a control's
    /// <c>ChangeEvent&lt;bool&gt;</c> (a <see cref="Toggle"/>). The payload applies while the control is
    /// checked and clears when it is unchecked; a control mounted already-checked lights up on attach
    /// (the change event only fires on a change). GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class CheckedVariantBehaviorTests
    {
        private VisualElement _root;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        [Test]
        public void Given_CheckedVariantToggle_When_Checked_Then_PayloadApplied()
        {
            // Arrange — a Toggle with checked:bg-hot, currently unchecked.
            _mounted = V.Mount(_root, V.Toggle(name: "leaf", className: "checked:bg-hot"));
            var leaf = _root.Q<Toggle>("leaf");
            Assume.That(leaf.ClassListContains("bg-hot"), Is.False, "Precondition: payload off while unchecked");

            // Act — the toggle is checked.
            leaf.SimulateChange(true);

            // Assert — the checked: payload is applied.
            Assert.IsTrue(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_CheckedVariantToggleApplied_When_Unchecked_Then_PayloadRemoved()
        {
            // Arrange — a checked Toggle whose payload is applied.
            _mounted = V.Mount(_root, V.Toggle(name: "leaf", className: "checked:bg-hot"));
            var leaf = _root.Q<Toggle>("leaf");
            leaf.SimulateChange(true);
            Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: payload on while checked");

            // Act — the toggle is unchecked.
            leaf.SimulateChange(false);

            // Assert — the payload clears.
            Assert.IsFalse(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_ToggleMountedAlreadyChecked_When_Mounted_Then_PayloadAppliedOnAttach()
        {
            // Arrange / Act — a Toggle mounted with value: true; ChangeEvent<bool> never fires, so the payload
            // must be applied from the attach-time read of the control's current checked state.
            _mounted = V.Mount(_root, V.Toggle(name: "leaf", className: "checked:bg-hot", value: true));
            var leaf = _root.Q<Toggle>("leaf");

            // Assert
            Assert.IsTrue(leaf.ClassListContains("bg-hot"));
        }
    }
}
