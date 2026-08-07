using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for the text-effect cascade on a <c>V.Motion</c>'s CREATE path. That path ran no
    /// cascade where the patch path did, so a plain <c>uppercase</c> (or <c>underline</c>,
    /// <c>line-through</c>, <c>whitespace-pre-line</c>, <c>leading-*</c>) on a Motion rendered untransformed
    /// text from mount — for the element's whole life when nothing ever re-rendered it. Two halves have to
    /// be present for the cascade to land on a Motion: the pass itself, which reaches the descendant text
    /// leaves, and the raw-text capture the pass rewrites the element's OWN text from. A test per half, so neither can
    /// be dropped without one going red.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class MotionTextEffectCreateTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [Test]
        public void Given_AMotionCarryingUppercase_When_ItMountsWithoutEverPatching_Then_ItsTextLeafIsUppercased()
        {
            // Arrange — a Motion whose only text is a descendant leaf, which carries no class of its own and
            // so can only be transformed by the Motion's own cascade pass.
            var tree = V.Motion(className: "uppercase", children: new VNode[] { V.Text("shout") });

            // Act — mount it once and never patch.
            using var mounted = V.Mount(_root, tree);

            // Assert — the leaf shows the transformed string from the first frame.
            Assert.That(_root.Q<Label>().text, Is.EqualTo("SHOUT"));
        }

        [Test]
        public void Given_AMotionHostingALabelWithItsOwnText_When_ItMountsWithoutEverPatching_Then_ThatTextIsUppercased()
        {
            // Arrange — a Motion that IS the text-bearing element: the Text prop is applied by the element
            // factory, so the cascade has nothing to rewrite from unless the raw value was captured.
            var tree = V.Motion(className: "uppercase", elementType: typeof(Label),
                props: new FiberElementProps { Text = "shout" });

            // Act — mount it once and never patch.
            using var mounted = V.Mount(_root, tree);

            // Assert — the Motion's own text shows the transformed string.
            Assert.That(_root.Q<Label>().text, Is.EqualTo("SHOUT"));
        }
    }
}
