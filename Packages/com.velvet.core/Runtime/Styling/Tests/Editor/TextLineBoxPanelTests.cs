using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor;
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

        [Component]
        private static VNode SwitchableButton()
        {
            var (className, setClass) = Hooks.UseState(s_className);
            s_setClass = setClass;
            return V.Button(name: "tag", className: className, text: "Tag");
        }

        [Component]
        private static VNode SwitchableField()
        {
            var (className, setClass) = Hooks.UseState(s_className);
            s_setClass = setClass;
            return V.TextField(name: "tag", className: className, value: "Tag");
        }

        private StyleSheet _appSheet;

        [TearDown]
        public void DestroyAppSheet()
        {
            if (_appSheet != null)
            {
                UnityEngine.Object.DestroyImmediate(_appSheet);
                _appSheet = null;
            }
        }

        // Attaches a sheet of the app's own, imported as the editor imports a .uss file.
        private void AttachAppSheet(string uss)
        {
            _appSheet = ScriptableObject.CreateInstance<StyleSheet>();
            var impl = Activator.CreateInstance(StockStyleSheetImport.Impl!, true)!;
            try
            {
                StockStyleSheetImport.Import!.Invoke(impl, new object[] { _appSheet, uss });
            }
            catch (TargetInvocationException)
            {
                // The importer reports through its context, which this instance has none of; what it stored is used.
            }
            _window.rootVisualElement.styleSheets.Add(_appSheet);
        }

        private static string Raise(float px) => px.ToString(CultureInfo.InvariantCulture);

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

        [Test]
        public void Given_ASingleLineFieldsInput_When_LaidOut_Then_ItsTextIsOneLineHeightTall()
        {
            // Act
            var field = (TextField)Mount(V.TextField(name: "field", className: "text-sm/6", value: "hello"), "field");

            // Assert
            Assert.That(((TextElement)field.textEdition).contentRect.height, Is.EqualTo(24f).Within(0.01f));
        }
    
        [Test]
        public void Given_ALabelWithNoTextEffectInABoxTallerThanItsLine_When_LaidOut_Then_ItsTextIsRaisedIntoItsFirstLineBox()
        {
            // Act
            var label = MountLabel("h-12", "OK");

            // Assert — the content height less the one line box, under the preflight's 1.5.
            Assert.That(RenderedTextProbe.Of(label),
                Is.EqualTo("<voffset=" + Raise(48f - 1.5f * label.resolvedStyle.fontSize) + "px>OK</voffset>"));
        }

        [Test]
        public void Given_APooledLabelWithNoTextEffect_When_RentedIntoABoxTallerThanItsLine_Then_ItsTextIsRaisedIntoItsFirstLineBox()
        {
            // Arrange
            var first = MountLabel(string.Empty, "OK");
            _mounted.Dispose();

            // Act
            var label = MountLabel("h-12", "OK");

            // Assert — null where the pool handed back another label, which the expected text can never equal.
            Assert.That(ReferenceEquals(label, first) ? RenderedTextProbe.Of(label) : null,
                Is.EqualTo("<voffset=" + Raise(48f - 1.5f * label.resolvedStyle.fontSize) + "px>OK</voffset>"));
        }

        // GREEN_ON_BASE(characterization): on the base a button's text is centred by its theme's middle-center and
        // nothing raises it. A button raised into its first line box, as a label is, would show a <voffset> here.
        [Test]
        public void Given_AButtonTallerThanItsLine_When_LaidOut_Then_ItsTextIsNotRaised()
        {
            // Act
            var button = Mount(V.Button(name: "go", className: "h-12", text: "Go"), "go");

            // Assert
            Assert.That(RenderedTextProbe.Of(button), Is.EqualTo("Go"));
        }

        [Test]
        public void Given_ALabelTopCentredByARuleOfTheApps_When_LaidOut_Then_ItIsAlignedMiddleCenter()
        {
            // Arrange
            AttachAppSheet(".title { -unity-text-align: upper-center; }");

            // Act
            var label = MountLabel("title");

            // Assert
            Assert.That(label.resolvedStyle.unityTextAlign, Is.EqualTo(TextAnchor.MiddleCenter));
        }

        // GREEN_ON_BASE(characterization): on the base nothing writes a button's alignment, so its theme's
        // middle-center returns with the class. One that took the parent's alignment instead would read MiddleLeft.
        [Test]
        public void Given_AButtonTopLeftAligned_When_ItsAlignmentClassIsRemoved_Then_ItIsAlignedAsItsThemeCentresIt()
        {
            // Arrange
            var button = MountSwitchable(SwitchableButton, "text-top-left");

            // Act
            PatchOnce(button, string.Empty);

            // Assert
            Assert.That(button.resolvedStyle.unityTextAlign, Is.EqualTo(TextAnchor.MiddleCenter));
        }

        // GREEN_ON_BASE(characterization): on the base nothing writes a field's input alignment, so the class it
        // inherits from reaches it. An input alignment kept from the first class would read MiddleLeft.
        [Test]
        public void Given_AFieldTopLeftAligned_When_ItBecomesRightAligned_Then_ItsInputTextIsRightAligned()
        {
            // Arrange
            var field = (TextField)MountSwitchable(SwitchableField, "text-top-left");

            // Act
            PatchOnce(field, "text-right");

            // Assert
            Assert.That(((TextElement)field.textEdition).resolvedStyle.unityTextAlign, Is.EqualTo(TextAnchor.MiddleRight));
        }

        [Test]
        public void Given_APooledFieldThatWasTopCentred_When_RentedWithNoAlignmentClass_Then_ItsInputTextIsAlignedMiddleLeft()
        {
            // Arrange
            var first = (TextField)Mount(V.TextField(name: "field", className: "text-top-center", value: "a"), "field");
            _mounted.Dispose();

            // Act
            var field = (TextField)Mount(V.TextField(name: "field", value: "a"), "field");

            // Assert — null where the pool handed back another field, which MiddleLeft can never equal.
            Assert.That(ReferenceEquals(field, first) ? ((TextElement)field.textEdition).resolvedStyle.unityTextAlign : (TextAnchor?)null,
                Is.EqualTo(TextAnchor.MiddleLeft));
        }

        [Test]
        public void Given_APooledSingleLineField_When_RentedAgain_Then_ItsInputTextCarriesNoInlineHeightIntoItsFirstStylePass()
        {
            // Arrange
            var first = (TextField)Mount(V.TextField(name: "field", value: "a"), "field");
            _mounted.Dispose();

            // Act — mounted with no panel update, so nothing has settled the field for its new use.
            _mounted = V.Mount(_window.rootVisualElement, V.TextField(name: "field", value: "a"));
            var field = _window.rootVisualElement.Q<TextField>("field");

            // Assert — null where the pool handed back another field, which StyleKeyword.Null can never equal.
            Assert.That(ReferenceEquals(field, first) ? ((TextElement)field.textEdition).style.height.keyword : (StyleKeyword?)null,
                Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AnOffsetOfTheAppsUnderARaise_When_Shown_Then_TheRaiseIsAddedToItAndOpenedAgainAfterIt()
        {
            // Act
            var label = MountLabel("h-12", "x<voffset=3px>y</voffset> z");

            // Assert — the outer raise is read off the shown text, so the case holds at any font height.
            var shown = RenderedTextProbe.Of(label);
            var outer = Regex.Match(shown, "^<voffset=(-?[0-9.]+)px>").Groups[1].Value;
            var raise = float.TryParse(outer, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : float.NaN;
            Assert.That(shown, Is.EqualTo("<voffset=" + outer + "px>x<voffset=" + Raise(raise + 3f) + "px>y</voffset><voffset="
                + outer + "px> z</voffset>"));
        }

        [Test]
        public void Given_ALineHeightOfTheAppsAcrossTwoLines_When_LaidOut_Then_TheSecondLineIsThatAdvanceBelowTheFirstLineBox()
        {
            // Act
            var label = MountLabel("whitespace-pre text-[16px]", "<line-height=40px>a\nb</line-height>");

            // Assert — one 24px line box, then the 40px the app's tag advances the second line by.
            Assert.That(label.layout.height, Is.EqualTo(64f).Within(0.01f));
        }

        [Test]
        public void Given_ALineHeightOfTheAppsThatCloses_When_Shown_Then_ThePreflightLineHeightIsOpenedAgainAfterIt()
        {
            // Act
            var label = MountLabel("text-[16px]", "<line-height=40px>a</line-height><br>b");

            // Assert
            Assert.That(RenderedTextProbe.Of(label),
                Is.EqualTo("<line-height=1.5em><line-height=40px>a</line-height><line-height=1.5em><br>b</line-height>"));
        }

        [Test]
        public void Given_ALineBreakTag_When_Shown_Then_TheTextTakesThePreflightLineHeight()
        {
            // Act
            var label = MountLabel("text-[16px]", "Line1<br>Line2");

            // Assert
            Assert.That(RenderedTextProbe.Of(label), Is.EqualTo("<line-height=1.5em>Line1<br>Line2</line-height>"));
        }

        [Test]
        public void Given_AWordWiderThanItsBox_When_TheGeneratorBreaksIt_Then_TheTextTakesThePreflightLineHeight()
        {
            // Act
            var label = MountLabel("w-10 text-[16px]", "Supercalifragilistic");

            // Assert
            Assert.That(RenderedTextProbe.Of(label), Is.EqualTo("<line-height=1.5em>Supercalifragilistic</line-height>"));
        }

        [Test]
        public void Given_AWordThatFitsItsBox_When_LaidOut_Then_ItIsShownWithoutTheLineHeightTag()
        {
            // Act
            var label = MountLabel("w-[300px] text-[16px]", "Supercalifragilistic");

            // Assert
            Assert.That(RenderedTextProbe.Of(label), Is.EqualTo("Supercalifragilistic"));
        }

        [Test]
        public void Given_ALineCappedByAMaximumHeight_When_LaidOut_Then_ItsTextStartsAtTheTopOfTheBox()
        {
            // Act — a definite width, so the layout measures the label at the width it lays it out at.
            var label = MountLabel("w-[200px] text-[16px] max-h-[10px]", "Hello");

            // Assert — lowered by the 10px box less its 24px line box, which puts the line's top at the box's.
            Assert.That(RenderedTextProbe.Of(label), Is.EqualTo("<voffset=-14px>Hello</voffset>"));
        }
    }
}
