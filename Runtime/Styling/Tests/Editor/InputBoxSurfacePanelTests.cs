using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The surface utilities on a field control, measured on a real panel with the bundled utility sheet
    /// attached: the box paints what the control's class list says, and the states and sources that are not the
    /// box's own are read from the control. <see cref="InputBoxSurfaceTests"/> holds the cases that need no panel.
    /// </summary>
    [TestFixture]
    internal sealed class InputBoxSurfacePanelTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private VisualElement Mount(params VNode[] children)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: children));
            var root = _window.rootVisualElement;
            ForcePanelUpdate(root.panel);
            return root;
        }

        private static VisualElement Box(VisualElement field)
            => field.Q<VisualElement>(className: StyleChildVariantClass.InputBoxClass);

        [Test]
        public void Given_APeerCheckedBackgroundOnATextField_When_ThePrecedingPeerIsChecked_Then_TheInputBoxTakesIt()
        {
            // Arrange / Act — the peer is a sibling of the control, and the box's own sibling is the label.
            var root = Mount(
                V.Toggle(name: "peer", className: "peer", value: true),
                V.TextField(name: "field", className: "peer-checked:bg-on"));
            var field = root.Q<VisualElement>("field");

            // Assert
            Assert.That((Box(field).ClassListContains("bg-on"), field.ClassListContains("bg-on")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AGroupHoverBackgroundOnATextField_When_TheGroupIsHovered_Then_TheInputBoxTakesIt()
        {
            // Arrange
            var root = Mount(V.Div(className: "group", name: "group", children: new VNode[]
            {
                V.TextField(name: "field", className: "group-hover:bg-on"),
            }));
            var field = root.Q<VisualElement>("field");

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                root.Q<VisualElement>("group").SimulateEvent(over);
            }

            // Assert
            Assert.That((Box(field).ClassListContains("bg-on"), field.ClassListContains("bg-on")),
                Is.EqualTo((true, false)));
        }

        // The reference element carries the same utility on a plain Div, so the colour compared against is the
        // sheet's own rather than a literal.
        [Test]
        public void Given_AFocusBorderColorOnATextField_When_TheFieldIsFocused_Then_TheInputBoxResolvesTheUtilitysColor()
        {
            // Arrange
            var root = Mount(
                V.TextField(name: "field", className: "border focus:border-blue-500"),
                V.Div(name: "reference", className: "border border-blue-500"));
            var field = root.Q<TextField>("field");
            var box = Box(field);

            // Act
            field.Focus();
            ForcePanelUpdate(root.panel);

            // Assert
            Assert.That(box.resolvedStyle.borderTopColor,
                Is.EqualTo(root.Q<VisualElement>("reference").resolvedStyle.borderTopColor));
        }

        [Test]
        public void Given_AFocusBorderColorOnATextField_When_FocusLeavesTheField_Then_TheInputBoxGivesTheUtilityUp()
        {
            // Arrange
            var root = Mount(
                V.TextField(name: "field", className: "border focus:border-blue-500"),
                V.Button(name: "other"));
            var field = root.Q<TextField>("field");
            field.Focus();
            var focused = Box(field).ClassListContains("border-blue-500");

            // Act
            root.Q<Button>("other").Focus();

            // Assert
            Assert.That((focused, Box(field).ClassListContains("border-blue-500")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AFocusWhileClassOnATextField_When_TheFieldIsFocused_Then_TheInputBoxTakesIt()
        {
            // Arrange
            var root = Mount(V.TextField(name: "field", whileFocusClass: "bg-on"));
            var field = root.Q<TextField>("field");

            // Act
            field.Focus();

            // Assert
            Assert.That((Box(field).ClassListContains("bg-on"), field.ClassListContains("bg-on")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ABackgroundClassOnATextField_When_TheThemeDressesTheBox_Then_TheBoxResolvesTheUtilitysColor()
        {
            // Arrange / Act
            var root = Mount(
                V.TextField(name: "field", className: "bg-red-500"),
                V.Div(name: "reference", className: "bg-red-500"));

            // Assert — the theme's own background for the box is not what the box resolves.
            Assert.That(Box(root.Q<VisualElement>("field")).resolvedStyle.backgroundColor,
                Is.EqualTo(root.Q<VisualElement>("reference").resolvedStyle.backgroundColor));
        }
    }
}
