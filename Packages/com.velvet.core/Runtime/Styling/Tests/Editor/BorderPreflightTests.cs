using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies Tailwind's preflight <c>border-color: currentcolor</c> on an element carrying a border-width utility:
    /// it draws in the element's text colour, follows it, and gives way to every author rule that sets a border colour
    /// whatever its selector, but not to a control theme's.
    /// </summary>
    internal sealed class BorderPreflightTests : PanelTestBase
    {
        private const string RuleSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/BorderPreflightRules.uss";
        private const string ThemeSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/BorderPreflightTheme.uss";

        protected override void LoadStyleSheets()
        {
            var root = _window.rootVisualElement;
            VelvetStyleUtilities.AttachTo(root);
            root.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemeSheet));
            EngineMember.StyleSheetIsDefault.ResolveProperty()!.SetValue(
                UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemeSheet), true);
            root.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(RuleSheet));
        }

        private static StateUpdater<string> s_setClass;
        private static string s_className;

        public override void SetUp()
        {
            s_setClass = default;
            base.SetUp();
        }

        [Component]
        private static VNode RenderSwitchable()
        {
            var (className, setClass) = Hooks.UseState(s_className);
            s_setClass = setClass;
            return V.Label(name: "tag", className: className, text: "Tag");
        }

        // Mounts a Label whose class list is replaced through s_setClass.
        private VisualElement MountSwitchable(string className)
        {
            s_className = className;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderSwitchable));
            return Settled("tag");
        }

        private VisualElement Patch(string className)
        {
            s_setClass.Invoke(className);
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
            return Settled("tag");
        }

        private VisualElement MountLabel(string className, string name = "tag")
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Label(name: name, className: className, text: "Tag"));
            return Settled(name);
        }

        private VisualElement Settled(string name)
        {
            var element = _window.rootVisualElement.Q<VisualElement>(name);
            for (var i = 0; i < 3; i++)
            {
                ForcePanelUpdate(element.panel);
            }
            return element;
        }

        // A reference element whose text colour is what a utility resolves to, to compare a border colour with.
        private Color TextColourOf(string utility)
        {
            var reference = new Label { name = "reference" };
            reference.AddToClassList(utility);
            _window.rootVisualElement.Add(reference);
            ForcePanelUpdate(reference.panel);
            var colour = reference.resolvedStyle.color;
            reference.RemoveFromHierarchy();
            return colour;
        }

        private static void SetPseudo(VisualElement element, string state, bool on)
        {
            var property = EngineMember.PseudoStates.ResolveProperty()!;
            var flag = System.Convert.ToInt32(System.Enum.Parse(property.PropertyType, state));
            var current = System.Convert.ToInt32(property.GetValue(element));
            property.SetValue(element, System.Enum.ToObject(property.PropertyType, on ? current | flag : current & ~flag));
        }

        [Test]
        public void Given_ALabelWithABorderWidthAndATextColour_When_Resolved_Then_ItsBorderIsDrawnInTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");

            // Act
            var tag = MountLabel("border rounded px-2 text-red-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ALabelWithABorderWidthAndATextColour_When_Resolved_Then_EverySideIsDrawnInTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");

            // Act
            var tag = MountLabel("border text-red-500");

            // Assert
            Assert.That(new[]
            {
                tag.resolvedStyle.borderRightColor, tag.resolvedStyle.borderBottomColor, tag.resolvedStyle.borderLeftColor,
            }, Is.All.EqualTo(expected));
        }

        [Test]
        public void Given_ABorderedLabel_When_ItsTextColourChanges_Then_ItsBorderFollows()
        {
            // Arrange
            var tag = MountLabel("border text-red-500");

            // Act
            tag.style.color = new StyleColor(Color.blue);
            ForcePanelUpdate(tag.panel);

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_ABorderedLabelWithoutATextColourOfItsOwn_When_ItsParentsChanges_Then_ItsBorderFollows()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "parent", className: "text-red-500",
                children: new VNode[] { V.Label(name: "tag", className: "border", text: "Tag") }));
            var tag = Settled("tag");

            // Act
            _window.rootVisualElement.Q<VisualElement>("parent").style.color = new StyleColor(Color.blue);
            ForcePanelUpdate(tag.panel);

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_AnElementWithoutABorderWidth_When_Resolved_Then_PreflightDoesNotMarkIt()
        {
            // Act
            var tag = MountLabel("text-red-500");

            // Assert
            Assert.That(tag.ClassListContains(ColorReferenceWatch.MarkerClass), Is.False);
        }

        [Test]
        public void Given_ABorderedLabelWithABorderColourClass_When_Resolved_Then_TheClassWins()
        {
            // Arrange
            var expected = TextColourOf("text-green-500");

            // Act
            var tag = MountLabel("border border-green-500 text-red-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABorderedLabelWithABracketBorderColour_When_Resolved_Then_TheBracketWins()
        {
            // Act
            var tag = MountLabel("border border-[#00ff00] text-red-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(new Color(0f, 1f, 0f, 1f)));
        }

        [Test]
        public void Given_ABorderedLabelWithAColourlessSibling_When_Resolved_Then_TheSiblingKeepsTheEnginesBorderColour()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "row", children: new VNode[]
            {
                V.Label(name: "tag", className: "border text-red-500", text: "A"),
                V.Label(name: "other", className: "text-red-500", text: "B"),
            }));
            Settled("tag");

            // Act
            var other = Settled("other");

            // Assert
            Assert.That(other.resolvedStyle.borderTopColor, Is.EqualTo(Color.clear));
        }

        [Test]
        public void Given_ABorderedLabelWithAClassRuleSettingTransparent_When_Resolved_Then_ItsBorderIsTransparent()
        {
            // Act
            var tag = MountLabel("border pf-transparent text-red-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(Color.clear));
        }

        [Test]
        public void Given_ABorderedLabelWithAnIdRuleSettingABorderColour_When_Resolved_Then_TheRuleWins()
        {
            // Act
            var tag = MountLabel("border text-red-500", "pf-id");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_ABorderedLabelUnderAnAncestorRule_When_Resolved_Then_TheDescendantRuleWins()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: "pf-ancestor", children: new VNode[]
            {
                V.Label(name: "tag", className: "border pf-descendant text-red-500", text: "Tag"),
            }));

            // Act
            var tag = Settled("tag");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(Color.yellow));
        }

        [Test]
        public void Given_ABorderedLabelWithAHoverRule_When_ThePointerIsNotOverIt_Then_ItsBorderIsTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");

            // Act
            var tag = MountLabel("border pf-hover text-red-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABorderedLabelWithAHoverRule_When_ThePointerIsOverIt_Then_TheRuleWins()
        {
            // Arrange
            var tag = MountLabel("border pf-hover text-red-500");

            // Act
            SetPseudo(tag, "Hover", true);
            ForcePanelUpdate(tag.panel);

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(Color.green));
        }

        [Test]
        public void Given_ABorderedLabelWithAHoverRule_When_ThePointerLeavesIt_Then_ItsBorderIsTheTextColourAgain()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");
            var tag = MountLabel("border pf-hover text-red-500");
            SetPseudo(tag, "Hover", true);
            ForcePanelUpdate(tag.panel);

            // Act
            SetPseudo(tag, "Hover", false);
            ForcePanelUpdate(tag.panel);

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABorderedLabelWithASideRule_When_Resolved_Then_TheRuleSetsThatSideOnly()
        {
            // Act
            var tag = MountLabel("border pf-top text-blue-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_ABorderedLabelWithASideRule_When_Resolved_Then_TheOtherSidesAreTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-blue-500");

            // Act
            var tag = MountLabel("border pf-top text-blue-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderBottomColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABorderedLabelWithARuleOfADefaultStylesheet_When_Resolved_Then_PreflightOutranksIt()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");

            // Act
            var tag = MountLabel("border pf-theme text-red-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABorderedLabelWhoseClassGainsARuleSettingABorderColour_When_Patched_Then_TheRuleWins()
        {
            // Arrange
            MountSwitchable("border text-red-500");

            // Act
            var tag = Patch("border pf-transparent text-red-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(Color.clear));
        }

        [Test]
        public void Given_ABorderedLabelWhoseBorderWidthIsRemoved_When_Patched_Then_ItsBorderColourIsLeftToTheEngine()
        {
            // Arrange
            MountSwitchable("border text-red-500");

            // Act
            var tag = Patch("text-red-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(Color.clear));
        }

        [Test]
        public void Given_ALabelWhoseCodeWritesABorderColourInline_When_Mounted_Then_TheInlineColourStands()
        {
            // Act
            _mounted = V.Mount(_window.rootVisualElement, V.Label(name: "tag", className: "border text-red-500", text: "Tag",
                refCallback: element =>
                {
                    element.style.borderTopColor = new StyleColor(Color.magenta);
                    return () => { };
                }));
            var tag = Settled("tag");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(Color.magenta));
        }

        [Test]
        public void Given_ABorderedLabel_When_CodeWritesABorderColourInlineAfterwards_Then_TheInlineColourStands()
        {
            // Arrange
            var tag = MountLabel("border text-red-500");

            // Act
            tag.style.borderTopColor = new StyleColor(Color.magenta);
            ForcePanelUpdate(tag.panel);
            tag.style.color = new StyleColor(Color.blue);
            ForcePanelUpdate(tag.panel);

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(Color.magenta));
        }

        [Test]
        public void Given_ALabelWhoseCodeWroteOneSideInline_When_Mounted_Then_TheOtherSidesAreTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");

            // Act
            _mounted = V.Mount(_window.rootVisualElement, V.Label(name: "tag", className: "border text-red-500", text: "Tag",
                refCallback: element =>
                {
                    element.style.borderTopColor = new StyleColor(Color.magenta);
                    return () => { };
                }));
            var tag = Settled("tag");

            // Assert
            Assert.That(tag.resolvedStyle.borderBottomColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ADividedChildWithoutADivideColour_When_Resolved_Then_TheDividerIsInItsTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "row", className: "flex flex-row divide-x-2 text-red-500",
                children: new VNode[]
                {
                    V.Label(name: "first", text: "A"),
                    V.Label(name: "second", text: "B"),
                }));
            Settled("row");

            // Act
            var first = Settled("first");

            // Assert
            Assert.That(first.resolvedStyle.borderRightColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ADividedRowWithoutADivideColour_When_Resolved_Then_TheLastChildHasNoDivider()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "row", className: "flex flex-row divide-x-2 text-red-500",
                children: new VNode[]
                {
                    V.Label(name: "first", text: "A"),
                    V.Label(name: "second", text: "B"),
                }));
            Settled("row");

            // Act
            var second = Settled("second");

            // Assert
            Assert.That(second.resolvedStyle.borderRightColor, Is.EqualTo(Color.clear));
        }

        [Test]
        public void Given_ADividedChildWithADivideColour_When_Resolved_Then_TheDivideColourHoldsTheDivider()
        {
            // Arrange
            var expected = TextColourOf("text-blue-500");
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "row",
                className: "flex flex-row divide-x-2 divide-blue-500 text-red-500", children: new VNode[]
                {
                    V.Label(name: "first", text: "A"),
                    V.Label(name: "second", text: "B"),
                }));
            Settled("row");

            // Act
            var first = Settled("first");

            // Assert
            Assert.That(first.resolvedStyle.borderRightColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ALabelWhoseClassRuleSetsABorderWidth_When_Resolved_Then_ItsBorderIsTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");

            // Act
            var tag = MountLabel("pf-width text-red-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ALabelWhoseClassRuleSetsABorderWidthAndColour_When_Resolved_Then_TheRuleColourStands()
        {
            // Act
            var tag = MountLabel("pf-width-colored text-red-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_ALabelWhoseIdRuleSetsABorderWidth_When_Resolved_Then_ItsBorderIsTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");

            // Act
            var tag = MountLabel("text-red-500", "pf-width-id");

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ALabelWhoseClassRuleSetsNoBorderWidth_When_Resolved_Then_ItIsNotMarked()
        {
            // Act
            var tag = MountLabel("pf-plain text-red-500");

            // Assert
            Assert.That(tag.ClassListContains(ColorReferenceWatch.MarkerClass), Is.False);
        }

        [Test]
        public void Given_AnElementOwningAStylesheet_When_ItsRulesAreAsked_Then_ItsPseudoStatesAreUnchanged()
        {
            // Arrange
            var states = EngineMember.PseudoStates.ResolveProperty()!;
            var owner = new Label { name = "owner" };
            owner.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(RuleSheet));
            _window.rootVisualElement.Add(owner);
            ForcePanelUpdate(owner.panel);
            var before = (int)states.GetValue(owner);

            // Act
            BorderPreflight.OnClassesResolved(owner, new[] { "border" }, classesChanged: true);

            // Assert
            Assert.That((int)states.GetValue(owner), Is.EqualTo(before));
        }

        [Test]
        public void Given_ABorderedElementWithAShadow_When_Resolved_Then_TheFaceTakesTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "tag", className: "w-[40px] h-[40px] border shadow-md text-red-500"));

            // Act
            var tag = Settled("tag");
            SilhouetteFaceStash.TryReadFace(tag, DivideEdge.Left, out var face, out _);

            // Assert
            Assert.That(face, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABorderedElementWithAShadow_When_ItsTextColourChanges_Then_TheFaceFollows()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "tag", className: "w-[40px] h-[40px] border shadow-md text-red-500"));
            var tag = Settled("tag");

            // Act
            tag.style.color = new StyleColor(Color.blue);
            ForcePanelUpdate(tag.panel);
            SilhouetteFaceStash.TryReadFace(tag, DivideEdge.Left, out var face, out _);

            // Assert
            Assert.That(face, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_ADashedBorderedElement_When_Resolved_Then_TheFaceTakesTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "tag", className: "w-[40px] h-[40px] border border-dashed text-red-500"));

            // Act
            var tag = Settled("tag");
            SilhouetteFaceStash.TryReadFace(tag, DivideEdge.Left, out var face, out _);

            // Assert
            Assert.That(face, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ADashedDividerWithoutADivideColour_When_Resolved_Then_TheDashesPaintTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "row",
                className: "flex flex-row divide-x-2 divide-dashed text-red-500", children: new VNode[]
                {
                    V.Label(name: "first", text: "A"),
                    V.Label(name: "second", text: "B"),
                }));
            Settled("row");

            // Act
            var first = Settled("first");

            // Assert
            Assert.That(DivideDashPainter.ColorUnderMask(first, DivideEdge.Right), Is.EqualTo(expected));
        }

        [TestCase("border-s")]
        [TestCase("border-e-2")]
        [TestCase("border-bs")]
        [TestCase("border-be-4")]
        [TestCase("border-s-[3px]")]
        public void Given_ALogicalBorderWidthToken_When_AskedWhetherItIsABorderWidth_Then_ItIs(string token)
        {
            // Act
            var isWidth = BorderPreflight.IsBorderWidthToken(token);

            // Assert
            Assert.That(isWidth, Is.True);
        }

        [Test]
        public void Given_ALogicalBorderWidth_When_Resolved_Then_ItsBorderIsTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");

            // Act
            var tag = MountLabel("border-s-4 text-red-500");

            // Assert
            Assert.That(tag.resolvedStyle.borderLeftColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_AChildScopedBorderWidth_When_TheChildrenResolve_Then_EachChildsBorderIsTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "row", className: "*:border text-red-500",
                children: new VNode[] { V.Label(name: "child", text: "A") }));
            Settled("row");

            // Act
            var child = Settled("child");

            // Assert
            Assert.That(child.resolvedStyle.borderTopColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_AFieldWithABorderWidth_When_Resolved_Then_ItsInputBoxBorderIsItsTextColour()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.TextField(name: "field", className: "border"));
            var field = Settled("field");
            StyleInputBoxSurface.TryGetBox(field, out var box);

            // Act
            ForcePanelUpdate(field.panel);

            // Assert
            Assert.That(box.resolvedStyle.borderTopColor, Is.EqualTo(box.resolvedStyle.color));
        }

        [Test]
        public void Given_ALabelWhoseHoverRuleSetsABorderWidth_When_ThePointerIsOverIt_Then_ItsBorderIsTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");
            var tag = MountLabel("pf-hover-width text-red-500");

            // Act
            SetPseudo(tag, "Hover", true);
            ForcePanelUpdate(tag.panel);

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ALabelWhoseDescendantRuleSetsABorderWidth_When_ItsAncestorGainsTheClass_Then_ItsBorderIsTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "outer", children: new VNode[]
            {
                V.Label(name: "tag", className: "pf-item text-red-500", text: "Tag"),
            }));
            var tag = Settled("tag");

            // Act
            _window.rootVisualElement.Q<VisualElement>("outer").AddToClassList("pf-open");
            ForcePanelUpdate(tag.panel);

            // Assert
            Assert.That(tag.resolvedStyle.borderBottomColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ALabelWithAGapUtility_When_Resolved_Then_ItIsNotMarked()
        {
            // Act
            var tag = MountLabel("gap-4 text-red-500");

            // Assert
            Assert.That(tag.ClassListContains(ColorReferenceWatch.MarkerClass), Is.False);
        }

        [Test]
        public void Given_ABorderedElement_When_AMotionSlotAsksWhetherAnInlineColourHoldsItsBorder_Then_PreflightDoesNot()
        {
            // Arrange
            var tag = MountLabel("border text-red-500");

            // Act
            var held = MotionSlotContext.Read(tag, null, null, _ => false).HoldsInlineOutsideSwap(ArbitraryProperty.BorderColor);

            // Assert
            Assert.That(held, Is.False);
        }

        private static StateUpdater<string> s_setRow;
        private static string s_rowClass;

        [Component]
        private static VNode RenderRow()
        {
            var (className, setClass) = Hooks.UseState(s_rowClass);
            s_setRow = setClass;
            return V.Div(name: "row", className: className, children: new VNode[]
            {
                V.Label(name: "first", text: "A"),
                V.Label(name: "second", text: "B"),
            });
        }

        private VisualElement MountRow(string className)
        {
            s_rowClass = className;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderRow));
            Settled("row");
            return Settled("first");
        }

        [Test]
        public void Given_ADividedRowWithoutADivideColour_When_ADivideColourIsPatchedIn_Then_TheDivideColourHoldsTheDivider()
        {
            // Arrange
            var expected = TextColourOf("text-blue-500");
            MountRow("flex flex-row divide-x-2 text-red-500");

            // Act
            s_setRow.Invoke("flex flex-row divide-x-2 divide-blue-500 text-red-500");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
            var first = Settled("first");

            // Assert
            Assert.That(first.resolvedStyle.borderRightColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ADividedRowWithoutADivideColour_When_ItsHoverDivideColourLights_Then_TheDivideColourHoldsTheDivider()
        {
            // Arrange
            var expected = TextColourOf("text-blue-500");
            var first = MountRow("flex flex-row divide-x-2 hover:divide-blue-500 text-red-500");
            var row = _window.rootVisualElement.Q<VisualElement>("row");

            // Act
            SetPseudo(row, "Hover", true);
            using (var over = PointerOverEvent.GetPooled())
            {
                row.SimulateEvent(over);
            }
            ForcePanelUpdate(first.panel);

            // Assert
            Assert.That(first.resolvedStyle.borderRightColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABorderedLabelWhoseBracketColourIsPatchedIn_When_AMotionSlotAsksForAnInlineHolder_Then_TheBracketColourIsOne()
        {
            // Arrange
            MountSwitchable("border text-red-500");
            var tag = Patch("border border-[#ff0000] text-red-500");

            // Act
            var held = MotionSlotContext.Read(tag, null, null, _ => false).HoldsInlineOutsideSwap(ArbitraryProperty.BorderColor);

            // Assert
            Assert.That(held, Is.True);
        }

        [Test]
        public void Given_TwoOwnersGivingAChildAWidth_When_OneOfThemTakesItBack_Then_TheOtherStillHoldsItsPreflight()
        {
            // Arrange
            var child = MountLabel("text-red-500");
            var first = new object();
            var second = new object();
            BorderPreflight.SetScoped(child, first, true);
            BorderPreflight.SetScoped(child, second, true);

            // Act
            BorderPreflight.SetScoped(child, first, false);

            // Assert
            Assert.That(child.ClassListContains(ColorReferenceWatch.MarkerClass), Is.True);
        }

        [Test]
        public void Given_ALabelWhoseClassIsWidenedByADefaultStylesheetOnly_When_Resolved_Then_ItIsNotMarked()
        {
            // Act
            var tag = MountLabel("pf-theme-width text-red-500");

            // Assert
            Assert.That(tag.ClassListContains(ColorReferenceWatch.MarkerClass), Is.False);
        }

        [Test]
        public void Given_ALabelListeningForARuleWidth_When_CodeWritesABorderWidthInline_Then_ItsBorderIsTheTextColour()
        {
            // Arrange
            var expected = TextColourOf("text-red-500");
            var tag = MountLabel("pf-hover-width text-red-500");

            // Act
            tag.style.borderTopWidth = 2f;
            ForcePanelUpdate(tag.panel);

            // Assert
            Assert.That(tag.resolvedStyle.borderTopColor, Is.EqualTo(expected));
        }

        [Test]
        public void Given_APreflightLayer_When_ItsKeyIsAskedWhetherItIsUnderAVariant_Then_ItIsNot()
        {
            // Assert
            Assert.That(StyleLayerPriority.UnderVariant(StyleLayerPriority.Preflight), Is.False);
        }

        [Test]
        public void Given_APreflightLayer_When_ComparedWithABaseLayer_Then_ItRanksBelow()
        {
            // Assert
            Assert.That(StyleLayerPriority.Preflight, Is.LessThan(StyleLayerPriority.Base));
        }

        [Test]
        public void Given_ABorderedLabel_When_AMotionSlotAsksWhetherAVariantHoldsItsBorderColour_Then_NoneDoes()
        {
            // Arrange
            var tag = MountLabel("border text-red-500");

            // Act
            var held = StyleArbitraryValueResolver.HoldsVariantLayer(tag, ArbitraryProperty.BorderTopColor);

            // Assert
            Assert.That(held, Is.False);
        }

        [TestCase("border", true)]
        [TestCase("border-2", true)]
        [TestCase("border-3", true)]
        [TestCase("border-x-4", true)]
        [TestCase("border-t", true)]
        [TestCase("hover:border", true)]
        [TestCase("md:hover:!border-2", true)]
        [TestCase("border-[3px]", true)]
        [TestCase("border-y-[0.5rem]", true)]
        [TestCase("border-0", false)]
        [TestCase("border-x-0", false)]
        [TestCase("border-red-500", false)]
        [TestCase("border-solid", false)]
        [TestCase("border-top", false)]
        [TestCase("border-[#fff]", false)]
        [TestCase("borders", false)]
        [TestCase("rounded", false)]
        public void Given_AClassToken_When_AskedWhetherItIsABorderWidth_Then_OnlyAWidthIs(string token, bool expected)
        {
            // Act
            var isWidth = BorderPreflight.IsBorderWidthToken(token);

            // Assert
            Assert.That(isWidth, Is.EqualTo(expected));
        }
    }
}
