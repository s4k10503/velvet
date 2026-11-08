using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a text element Velvet creates is as tall as CSS makes it: one line box of the line height per
    /// line, whatever the font's own content height. Every expected height is the declared line height times the
    /// line count, so none depends on the font.
    /// </summary>
    internal sealed class TextLineBoxPanelTests : PanelTestBase
    {
        private static string s_className = string.Empty;
        private static StateUpdater<string> s_setClass;

        [Component]
        private static VNode Switchable()
        {
            var (className, setClass) = Hooks.UseState(s_className);
            s_setClass = setClass;
            return V.Label(name: "tag", className: className, text: "Tag");
        }

        [Component]
        private static VNode SwitchableParent()
        {
            var (className, setClass) = Hooks.UseState(s_className);
            s_setClass = setClass;
            return V.Div(name: "parent", className: className, children: new VNode[] { V.Label(name: "tag", text: "Tag") });
        }

        private VisualElement MountSwitchable(System.Func<VNode> component, string className)
        {
            s_className = className;
            return Mount(V.Component(component), "tag");
        }

        // Changes the class and runs one panel update, the styles and layout of one frame.
        private VisualElement PatchOnce(VisualElement element, string className)
        {
            s_setClass.Invoke(className);
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
            ForcePanelUpdate(element.panel);
            return element;
        }
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private VisualElement Mount(VNode node, string name)
        {
            _mounted = V.Mount(_window.rootVisualElement, node);
            var element = _window.rootVisualElement.Q<VisualElement>(name);
            for (var i = 0; i < 3; i++)
            {
                ForcePanelUpdate(element.panel);
            }
            return element;
        }

        private VisualElement MountLabel(string className, string text = "Tag")
            => Mount(V.Label(name: "tag", className: className, text: text), "tag");

        [TestCase("text-xs", 16f)]
        [TestCase("text-sm", 20f)]
        [TestCase("text-base", 24f)]
        [TestCase("text-lg", 28f)]
        [TestCase("text-xl", 28f)]
        [TestCase("text-2xl", 32f)]
        [TestCase("text-3xl", 36f)]
        [TestCase("text-4xl", 40f)]
        [TestCase("text-base leading-8", 32f)]
        [TestCase("text-sm/6", 24f)]
        [TestCase("text-base leading-none", 16f)]
        public void Given_ALineOfText_When_LaidOut_Then_ItIsOneLineHeightTall(string className, float expected)
        {
            // Act
            var label = MountLabel(className);

            // Assert
            Assert.That(label.layout.height, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_ThreeExplicitLines_When_LaidOut_Then_TheyAreThreeLineHeightsTall()
        {
            // Act
            var label = MountLabel("whitespace-pre text-sm/6", "a\nb\nc");

            // Assert
            Assert.That(label.layout.height, Is.EqualTo(72f).Within(0.01f));
        }

        [Test]
        public void Given_TextWrappedOneWordPerLine_When_LaidOut_Then_ItIsThreeLineHeightsTall()
        {
            // Act — a box narrower than any glyph puts each one-letter word on a line of its own.
            var label = MountLabel("w-[1px] whitespace-normal text-sm/6", "a b c");

            // Assert
            Assert.That(label.layout.height, Is.EqualTo(72f).Within(0.01f));
        }

        [Test]
        public void Given_PaddingAroundALine_When_LaidOut_Then_TheResolvedHeightIsTheLineAndThePadding()
        {
            // Act
            var label = MountLabel("py-[5px] text-base");

            // Assert
            Assert.That(label.resolvedStyle.height, Is.EqualTo(34f).Within(0.01f));
        }

        [Test]
        public void Given_ALeadingOnTheParent_When_ATextLeafIsLaidOut_Then_ItIsThatLineHeightTall()
        {
            // Act
            var parent = Mount(V.Div(name: "parent", className: "text-base leading-8", children: new VNode[] { V.Text("Tag") }),
                "parent");

            // Assert
            Assert.That(parent.layout.height, Is.EqualTo(32f).Within(0.01f));
        }

        [Test]
        public void Given_AButtonsText_When_LaidOut_Then_ItsContentIsOneLineHeightTall()
        {
            // Act
            var button = Mount(V.Button(name: "go", className: "text-base leading-7", text: "Go"), "go");

            // Assert
            Assert.That(button.contentRect.height, Is.EqualTo(28f).Within(0.01f));
        }

        [Test]
        public void Given_TextWithNoLineHeightClass_When_LaidOut_Then_ItTakesThePreflightLineHeight()
        {
            // Act — a bracket size pairs no line height, so the preflight's 1.5 is what the text inherits.
            var label = MountLabel("text-[16px]");

            // Assert
            Assert.That(label.layout.height, Is.EqualTo(24f).Within(0.01f));
        }

        [Test]
        public void Given_ThreeLinesUnderThePreflightLineHeight_When_LaidOut_Then_TheyAreThreeLineHeightsTall()
        {
            // Act
            var label = MountLabel("whitespace-pre text-[16px]", "a\nb\nc");

            // Assert
            Assert.That(label.layout.height, Is.EqualTo(72f).Within(0.01f));
        }

        [Test]
        public void Given_AMiddleAlignedLabel_When_OnlyItsAlignmentBecomesTopAligned_Then_OneUpdateAlignsItMiddle()
        {
            // Arrange
            var label = MountSwitchable(Switchable, "text-left h-[80px]");

            // Act
            PatchOnce(label, "text-top-center h-[80px]");

            // Assert — middle-center, the horizontal part the class gives, within the update that changed it.
            Assert.That(label.resolvedStyle.unityTextAlign, Is.EqualTo(TextAnchor.MiddleCenter));
        }

        // GREEN_ON_BASE(characterization): a parent's text alignment reaches a label, as on the base. A middle
        // alignment written over the label that ignored its parent's would make this read MiddleLeft.
        [Test]
        public void Given_ALabelAlignedMiddleOverTheDefault_When_ItsParentBecomesRightAligned_Then_TheLabelIsRightAligned()
        {
            // Arrange
            var label = MountSwitchable(SwitchableParent, string.Empty);

            // Act
            PatchOnce(label, "text-right");

            // Assert
            Assert.That(label.resolvedStyle.unityTextAlign, Is.EqualTo(TextAnchor.MiddleRight));
        }
    }
}
