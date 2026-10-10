using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>caret-*</c> and <c>selection:bg-*</c> utilities on a text field, read as the field's
    /// caret and selection colours, on a panel carrying the bundled sheet and no theme. With no theme declaring
    /// either colour the engine writes nothing when a utility goes away, so a restore that is missing leaves the
    /// utility's colour behind here rather than the theme's. <c>SelectionTextOverlayTests</c> holds the
    /// selected-text overlay.
    /// </summary>
    internal sealed class TextInputColorUtilityTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static StateUpdater<bool> s_setDeclared;
        private static StateUpdater<int> s_setStep;
        private static string s_base;
        private static string s_utility;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            VelvetStyleUtilities.AttachTo(_host.Root);
            s_setDeclared = default;
            s_setStep = default;
            s_base = null;
            s_utility = null;
        }

        private const string BoxThemePath = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/TextInputBoxThemeStandIn.uss";
        private const string ControlThemePath =
            "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/TextInputControlThemeStandIn.uss";

        // Whether the sheet was there to attach, which a case folds into what it asserts: a tree without the
        // stand-in then answers by assertion rather than by an exception from the attach.
        private bool AttachTheme(string path)
        {
            var sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet != null)
            {
                _host.Root.styleSheets.Add(sheet);
            }

            return sheet != null;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private TextField Mount(Func<VNode> body)
        {
            _mounted = V.Mount(_host.Root, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            return _host.Root.Q<TextField>("field");
        }

        private void Render(bool declared)
        {
            s_setDeclared.Invoke(declared);
            Settle();
        }

        private void RenderStep(int step)
        {
            s_setStep.Invoke(step);
            Settle();
        }

        private void Settle()
        {
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private static VisualElement Box(TextField field)
            => field.Q<VisualElement>(className: StyleChildVariantClass.InputBoxClass);

#pragma warning disable CS0618 // the caret and selection colours have no other public reading
        private static Color32 Caret(TextField field) => field.textSelection.cursorColor;

        private static Color32 Selection(TextField field) => field.textSelection.selectionColor;

        private static Color32 FreshCaret() => new TextField().textSelection.cursorColor;

        private static Color32 FreshSelection() => new TextField().textSelection.selectionColor;
#pragma warning restore CS0618

        private static Color32 Palette(string name, float alpha = 1f)
        {
            VelvetPalette.TryGet(name, out var color);
            color.a = alpha;
            return color;
        }

        private static VNode Field(string className) => V.TextField(name: "field", className: className);

        [Component]
        private static VNode StaticHost() => Field("caret-red-500");

        [Component]
        private static VNode ArbitraryHost() => Field("caret-[#00ff00]");

        [Component]
        private static VNode OpacityHost() => Field("caret-red-500/50");

        [Component]
        private static VNode CurrentHost()
        {
            var (declared, setDeclared) = Hooks.UseState(true);
            s_setDeclared = setDeclared;
            return Field(declared ? "caret-current text-blue-500" : "caret-current text-green-500");
        }

        [Component]
        private static VNode AncestorHost() => V.Div(className: "caret-red-500", children: new VNode[]
        {
            V.Div(children: new VNode[] { V.TextField(name: "field") }),
        });

        [Component]
        private static VNode InheritHost() => V.Div(className: "caret-red-500", children: new VNode[]
        {
            Field("caret-blue-500 caret-inherit"),
        });

        [Component]
        private static VNode InvalidHost() => Field("caret-red-500 caret-nonsense");

        // Tailwind emits caret-blue-500 before caret-red-500, so red wins although it is added first.
        [Component]
        private static VNode SheetOrderHost() => Field("caret-red-500 caret-blue-500");

        // Tailwind emits caret-blue-500 before caret-inherit, so inherit wins although it is added first.
        [Component]
        private static VNode SheetOrderInheritHost() => V.Div(className: "caret-red-500", children: new VNode[]
        {
            Field("caret-inherit caret-blue-500"),
        });

        [Component]
        private static VNode PlainFieldHost() => V.TextField(name: "field");

        [Component]
        private static VNode CurrentSuffixHost() => Field("caret-currentx text-blue-500");

        [Component]
        private static VNode CurrentModifierHost() => Field("caret-current/abc text-blue-500");

        // Built by Activator rather than rented, so no earlier field of the same type has been tracked.
        private sealed class UnpooledTextField : TextField { }

        [Component]
        private static VNode UnpooledMotionHost()
            => V.Motion(name: "field", elementType: typeof(UnpooledTextField), className: "caret-red-500");

        [Component]
        private static VNode InvalidOpacityBaseHost() => Field("caret-red-500 caret-nonsense/50");

        [Component]
        private static VNode InvalidOpacityHost() => Field("caret-red-500 caret-blue-500/abc");

        // s_base stays; s_utility comes and goes with the state.
        [Component]
        private static VNode ThemedTogglingHost()
        {
            var (declared, setDeclared) = Hooks.UseState(true);
            s_setDeclared = setDeclared;
            return V.TextField(name: "field", className: declared ? s_base + " " + s_utility : s_base);
        }

        // Step 0 carries a selection background, 1 drops it, 2 adds a radius utility in its place.
        [Component]
        private static VNode SelectionSteppingHost()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.TextField(name: "field", className: step switch { 0 => "selection:bg-red-500", 1 => null, _ => "rounded-lg" });
        }

        [Component]
        private static VNode FocusHost() => Field("focus:caret-red-500");

        [Component]
        private static VNode TogglingHost()
        {
            var (declared, setDeclared) = Hooks.UseState(true);
            s_setDeclared = setDeclared;
            return V.TextField(name: "field", className: declared ? "caret-red-500" : null);
        }

        [Component]
        private static VNode MotionTogglingHost()
        {
            var (declared, setDeclared) = Hooks.UseState(true);
            s_setDeclared = setDeclared;
            return V.Motion(name: "field", elementType: typeof(TextField), className: declared ? "caret-red-500" : null);
        }

        [Component]
        private static VNode SelectionTogglingHost()
        {
            var (declared, setDeclared) = Hooks.UseState(true);
            s_setDeclared = setDeclared;
            return V.TextField(name: "field", className: declared ? "selection:bg-red-500" : null);
        }

        [Component]
        private static VNode SelectionAncestorHost() => V.Div(className: "selection:bg-red-500", children: new VNode[]
        {
            V.TextField(name: "field"),
        });

        // Tailwind emits selection:bg-blue-500 before selection:bg-red-500, so the ancestor's red reaches the
        // field's selection over the field's own blue.
        [Component]
        private static VNode SelectionAcrossElementsHost() => V.Div(className: "selection:bg-red-500", children: new VNode[]
        {
            Field("selection:bg-blue-500"),
        });

        [Component]
        private static VNode CurrentOpacityHost() => Field("caret-current/50 text-blue-500");

        // Step 0 carries a caret utility, 1 drops it, 2 adds a radius utility in its place.
        [Component]
        private static VNode SteppingHost()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.TextField(name: "field", className: step switch { 0 => "caret-red-500", 1 => null, _ => "rounded-lg" });
        }

        // Step 0 mounts a field carrying a caret utility, 1 unmounts it, 2 mounts one from the pool whose
        // refCallback writes the caret colour, and 3 drops that field's radius utility.
        [Component]
        private static VNode RecyclingHost()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return step switch
            {
                0 => V.TextField(name: "field", className: "caret-red-500"),
                1 => V.Div(),
                _ => V.TextField(
                    name: "field",
                    className: step == 2 ? "rounded-lg" : null,
                    refCallback: WriteBlueCaret),
            };
        }

        [Component]
        private static VNode RefCallbackHost()
        {
            var (declared, setDeclared) = Hooks.UseState(true);
            s_setDeclared = setDeclared;
            return V.TextField(name: "field", className: declared ? "rounded-lg" : null, refCallback: WriteBlueCaret);
        }

        private static Action WriteBlueCaret(VisualElement element)
        {
#pragma warning disable CS0618 // what an author reaching past the utilities writes
            ((TextField)element).textSelection.cursorColor = Color.blue;
#pragma warning restore CS0618
            return () => { };
        }

        [Test]
        public void Given_ACaretUtilityOnATextField_When_ItMounts_Then_TheCaretTakesThePaletteColor()
        {
            // Arrange / Act
            var field = Mount(StaticHost);

            // Assert
            Assert.That(Caret(field), Is.EqualTo(Palette("red-500")));
        }

        [Test]
        public void Given_ACaretUtilityOnATextField_When_ItMounts_Then_TheInputBoxCarriesIt()
        {
            // Arrange / Act
            var field = Mount(StaticHost);

            // Assert
            Assert.That((Box(field).ClassListContains("caret-red-500"), field.ClassListContains("caret-red-500")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ASelectionUtilityOnATextField_When_ItMounts_Then_TheInputBoxCarriesIt()
        {
            // Arrange / Act
            var field = Mount(SelectionTogglingHost);

            // Assert
            Assert.That(
                (Box(field).ClassListContains("selection:bg-red-500"), field.ClassListContains("selection:bg-red-500")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnArbitraryCaretColor_When_ItMounts_Then_TheCaretTakesIt()
        {
            // Arrange / Act
            var field = Mount(ArbitraryHost);

            // Assert
            Assert.That(Caret(field), Is.EqualTo((Color32)Color.green));
        }

        [Test]
        public void Given_ACaretColorWithAnOpacityModifier_When_ItMounts_Then_TheCaretTakesThatAlpha()
        {
            // Arrange / Act
            var field = Mount(OpacityHost);

            // Assert
            Assert.That(Caret(field), Is.EqualTo(Palette("red-500", 0.5f)));
        }

        [Test]
        public void Given_CaretCurrent_When_TheTextColorChanges_Then_TheCaretFollowsIt()
        {
            // Arrange
            var field = Mount(CurrentHost);
            var before = Caret(field);

            // Act
            Render(false);

            // Assert
            Assert.That((before, Caret(field)), Is.EqualTo((Palette("blue-500"), Palette("green-500"))));
        }

        [Test]
        public void Given_ACaretUtilityOnAnAncestor_When_ADescendantFieldDeclaresNone_Then_ItsCaretTakesTheAncestors()
        {
            // Arrange / Act
            var field = Mount(AncestorHost);

            // Assert
            Assert.That(Caret(field), Is.EqualTo(Palette("red-500")));
        }

        [Test]
        public void Given_CaretInheritAfterAColorOnTheField_When_ItMounts_Then_TheCaretTakesTheAncestors()
        {
            // Arrange / Act
            var field = Mount(InheritHost);

            // Assert
            Assert.That(Caret(field), Is.EqualTo(Palette("red-500")));
        }

        [Test]
        public void Given_TwoCaretColorsOnOneField_When_ItMounts_Then_TheOneTailwindEmitsLastWinsWhateverTheClassOrder()
        {
            // Arrange / Act
            var field = Mount(SheetOrderHost);

            // Assert
            Assert.That(Caret(field), Is.EqualTo(Palette("red-500")));
        }

        [Test]
        public void Given_CaretInheritAddedBeforeAColor_When_ItMounts_Then_InheritStillWinsAndTheCaretTakesTheAncestors()
        {
            // Arrange / Act
            var field = Mount(SheetOrderInheritHost);

            // Assert
            Assert.That(Caret(field), Is.EqualTo(Palette("red-500")));
        }

        [Test]
        public void Given_AFieldsInputAndAPlainTextElement_When_AStylePassRuns_Then_OnlyTheInputHearsItsCustomStyleEvent()
        {
            // Arrange — both carry a callback, and differ only in the custom property the bundled sheet gives
            // the text element inside a text input's box.
            var input = (TextElement)Mount(PlainFieldHost).textEdition;
            var plain = new TextElement();
            _host.Root.Add(plain);
            var inputHeard = 0;
            var plainHeard = 0;
            input.RegisterCallback<CustomStyleResolvedEvent>(_ => inputHeard++);
            plain.RegisterCallback<CustomStyleResolvedEvent>(_ => plainHeard++);

            // Act
            _host.Root.AddToClassList("restyled");
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That((inputHeard > 0, plainHeard), Is.EqualTo((true, 0)));
        }

        [Test]
        public void Given_ACaretValueThatDoesNotParse_When_ItIsAddedLast_Then_TheEarlierOneApplies()
        {
            // Arrange / Act
            var field = Mount(InvalidHost);

            // Assert
            Assert.That(Caret(field), Is.EqualTo(Palette("red-500")));
        }

        [Test]
        public void Given_AFocusCaretUtility_When_TheFieldIsFocused_Then_TheCaretTakesThePaletteColor()
        {
            // Arrange
            var field = Mount(FocusHost);
            var unfocused = Caret(field);

            // Act
            field.Focus();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That((unfocused, Caret(field)), Is.EqualTo((FreshCaret(), Palette("red-500"))));
        }

        [Test]
        public void Given_ACaretUtility_When_ALaterRenderDropsIt_Then_TheCaretGoesBackToAFreshFieldsColor()
        {
            // Arrange
            var field = Mount(TogglingHost);
            var whileDeclared = Caret(field);

            // Act
            Render(false);

            // Assert
            Assert.That((whileDeclared, Caret(field)), Is.EqualTo((Palette("red-500"), FreshCaret())));
        }

        [Test]
        public void Given_ACaretUtilityOnAMotionTextField_When_ALaterRenderDropsIt_Then_TheCaretGoesBackToAFreshFieldsColor()
        {
            // Arrange — V.Motion leaves its classes on the control rather than the box.
            var field = Mount(MotionTogglingHost);
            var whileDeclared = Caret(field);

            // Act
            Render(false);

            // Assert
            Assert.That((whileDeclared, Caret(field)), Is.EqualTo((Palette("red-500"), FreshCaret())));
        }

        [Test]
        public void Given_ASelectionBackgroundUtility_When_ALaterRenderDropsIt_Then_TheSelectionColorGoesBack()
        {
            // Arrange
            var field = Mount(SelectionTogglingHost);
            var whileDeclared = Selection(field);

            // Act
            Render(false);

            // Assert
            Assert.That((whileDeclared, Selection(field)), Is.EqualTo((Palette("red-500"), FreshSelection())));
        }

        [Test]
        public void Given_ASelectionBackgroundOnAnAncestor_When_ADescendantFieldDeclaresNone_Then_ItsSelectionTakesIt()
        {
            // Arrange / Act
            var field = Mount(SelectionAncestorHost);

            // Assert
            Assert.That(Selection(field), Is.EqualTo(Palette("red-500")));
        }

        [Test]
        public void Given_SelectionColorsOnTheFieldAndAnAncestor_When_ItMounts_Then_TheOneTailwindEmitsLastWinsWhereverItSits()
        {
            // Arrange / Act
            var field = Mount(SelectionAcrossElementsHost);

            // Assert
            Assert.That(Selection(field), Is.EqualTo(Palette("red-500")));
        }

        [Test]
        public void Given_CaretCurrentWithAnOpacityModifier_When_ItMounts_Then_TheCaretTakesTheTextColorAtThatAlpha()
        {
            // Arrange / Act
            var field = Mount(CurrentOpacityHost);

            // Assert
            Assert.That(Caret(field), Is.EqualTo(Palette("blue-500", 0.5f)));
        }

        [Test]
        public void Given_ACaretValueWithAnUnknownBaseAndAModifier_When_ItIsAddedLast_Then_TheEarlierOneApplies()
        {
            // Arrange / Act
            var field = Mount(InvalidOpacityBaseHost);

            // Assert
            Assert.That(Caret(field), Is.EqualTo(Palette("red-500")));
        }

        [Test]
        public void Given_ACaretValueWithAModifierThatDoesNotParse_When_ItIsAddedLast_Then_TheEarlierOneApplies()
        {
            // Arrange / Act
            var field = Mount(InvalidOpacityHost);

            // Assert
            Assert.That(Caret(field), Is.EqualTo(Palette("red-500")));
        }

        [TestCase(BoxThemePath, "", "caret-red-500", true)]
        [TestCase(ControlThemePath, "theme-control", "caret-red-500", true)]
        [TestCase(BoxThemePath, "", "selection:bg-red-500", false)]
        [TestCase(ControlThemePath, "theme-control", "selection:bg-red-500", false)]
        public void Given_AThemeDeclaringTheColor_When_AUtilityComesAndGoes_Then_ItWinsAndThenGivesWayToTheTheme(
            string themePath, string baseClasses, string utility, bool caret)
        {
            // Arrange
            var themed = AttachTheme(themePath);
            s_base = baseClasses;
            s_utility = utility;
            var field = Mount(ThemedTogglingHost);
            var whileDeclared = caret ? Caret(field) : Selection(field);

            // Act
            Render(false);

            // Assert
            Assert.That((themed, whileDeclared, caret ? Caret(field) : Selection(field)),
                Is.EqualTo((true, Palette("red-500"), (Color32)Color.blue)));
        }

        // GREEN_ON_BASE(characterization): a selection colour written after the restore survives later style passes.
        // The restore answers once per utility going away, and this is what a second answer would overwrite.
        [Test]
        public void Given_ASelectionColorRestoredAndThenWritten_When_AnotherUtilityResolvesTheBox_Then_TheWrittenColorStays()
        {
            // Arrange
            var field = Mount(SelectionSteppingHost);
            RenderStep(1);
#pragma warning disable CS0618 // what an author reaching past the utilities writes
            field.textSelection.selectionColor = Color.blue;
#pragma warning restore CS0618

            // Act
            RenderStep(2);

            // Assert
            Assert.That(Selection(field), Is.EqualTo((Color32)Color.blue));
        }

        // GREEN_ON_BASE(characterization): a caret colour written after the restore survives later style passes.
        // The restore answers once per utility going away, and this is what a second answer would overwrite.
        [Test]
        public void Given_ACaretColorRestoredAndThenWritten_When_AnotherUtilityResolvesTheBox_Then_TheWrittenColorStays()
        {
            // Arrange
            var field = Mount(SteppingHost);
            RenderStep(1);
#pragma warning disable CS0618 // what an author reaching past the utilities writes
            field.textSelection.cursorColor = Color.blue;
#pragma warning restore CS0618

            // Act
            RenderStep(2);

            // Assert
            Assert.That(Caret(field), Is.EqualTo((Color32)Color.blue));
        }

        // GREEN_ON_BASE(characterization): a recycled field's refCallback caret colour survives its first style pass.
        // The record of the last consumer's utility goes with the pool reset, and this is what it would overwrite.
        [Test]
        public void Given_APooledFieldACaretUtilityColored_When_ItIsReusedWithARefCallbackColor_Then_TheColorStays()
        {
            // Arrange
            var first = Mount(RecyclingHost);
            RenderStep(1);
            RenderStep(2);
            var second = _host.Root.Q<TextField>("field");

            // Act
            RenderStep(3);

            // Assert — the identity term is what makes this the recycled field rather than a fresh one.
            Assert.That((ReferenceEquals(first, second), Caret(second)), Is.EqualTo((true, (Color32)Color.blue)));
        }

        // GREEN_ON_BASE(characterization): a caret colour written from refCallback survives a style pass.
        // The restore answers only for a field a caret utility coloured, and this is what it must leave alone.
        [Test]
        public void Given_ACaretColorFromRefCallback_When_ARadiusUtilityComesOff_Then_TheColorStays()
        {
            // Arrange
            var field = Mount(RefCallbackHost);

            // Act
            Render(false);

            // Assert
            Assert.That(Caret(field), Is.EqualTo((Color32)Color.blue));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_ACaretCurrentValueThatDoesNotParse_When_ItMounts_Then_TheCaretKeepsAFreshFieldsColor(bool modifier)
        {
            // Arrange
            Func<VNode> host = modifier ? CurrentModifierHost : CurrentSuffixHost;

            // Act
            var field = Mount(host);

            // Assert
            Assert.That(Caret(field), Is.EqualTo(FreshCaret()));
        }

        [Test]
        public void Given_ACaretUtilityOnAMotionOfATypeNoPoolRents_When_ItMounts_Then_TheCaretTakesIt()
        {
            // Arrange / Act
            var field = Mount(UnpooledMotionHost);

            // Assert
            Assert.That(Caret(field), Is.EqualTo(Palette("red-500")));
        }
    }
}
