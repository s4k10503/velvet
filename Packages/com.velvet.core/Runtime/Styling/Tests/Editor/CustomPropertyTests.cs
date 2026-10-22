using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies CSS custom properties written as classes: a `[--name:value]` declaration inherits to the element's
    /// descendants, and var() in a utility's bracket, Tailwind's `(--name)` shorthand and an arbitrary property read
    /// it on the element, as a browser computes them.
    /// </summary>
    internal sealed class CustomPropertyTests : PanelTestBase
    {
        private const string RuleSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/CustomPropertyRule.uss";

        private static StateUpdater<string> s_setOuter;
        private static string s_outer;
        private static string s_inner;

        public override void SetUp()
        {
            s_setOuter = default;
            base.SetUp();
        }

        private VisualElement Mount(string outer, string inner)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "outer", className: outer, children: new VNode?[]
                {
                    V.Div(name: "inner", className: inner),
                }));
            return _window.rootVisualElement.Q<VisualElement>("inner");
        }

        // Mounts outer around inner, with outer's class list replaced through s_setOuter.
        private VisualElement MountSwitchable(string outer, string inner)
        {
            (s_outer, s_inner) = (outer, inner);
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderSwitchable));
            return _window.rootVisualElement.Q<VisualElement>("inner");
        }

        [Component]
        private static VNode RenderSwitchable()
        {
            var (outer, setOuter) = Hooks.UseState(s_outer);
            s_setOuter = setOuter;
            return V.Div(name: "outer", className: outer, children: new VNode?[]
            {
                V.Div(name: "inner", className: s_inner),
            });
        }

        private static (StyleKeyword, float) Width(VisualElement element)
            => (element.style.width.keyword, element.style.width.value.value);

        [TestCase("w-[var(--w)]")]
        [TestCase("w-(--w)")]
        [TestCase("[width:var(--w)]")]
        public void Given_AParentDeclaringAWidth_When_AChildReadsIt_Then_TheChildTakesThatWidth(string inner)
        {
            // Arrange / Act
            var child = Mount("[--w:30px]", inner);

            // Assert
            Assert.That(Width(child), Is.EqualTo((StyleKeyword.Undefined, 30f)));
        }

        [Test]
        public void Given_AReadInsideCalc_When_Mounted_Then_TheCalcIsTakenOfTheValue()
        {
            // Arrange / Act
            var child = Mount("[--w:30px]", "w-[calc(var(--w)*2)]");

            // Assert
            Assert.That(Width(child), Is.EqualTo((StyleKeyword.Undefined, 60f)));
        }

        [Test]
        public void Given_TwoAncestorsDeclaringOneProperty_When_AChildReadsIt_Then_TheNearerDeclarationWins()
        {
            // Arrange / Act
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "[--w:30px]", children: new VNode?[]
                {
                    V.Div(className: "[--w:50px]", children: new VNode?[]
                    {
                        V.Div(name: "inner", className: "w-[var(--w)]"),
                    }),
                }));
            var child = _window.rootVisualElement.Q<VisualElement>("inner");

            // Assert
            Assert.That(Width(child), Is.EqualTo((StyleKeyword.Undefined, 50f)));
        }

        [Test]
        public void Given_AnUndeclaredPropertyWithAFallback_When_Read_Then_TheFallbackIsTaken()
        {
            // Arrange / Act
            var child = Mount("", "w-[var(--w,12px)]");

            // Assert
            Assert.That(Width(child), Is.EqualTo((StyleKeyword.Undefined, 12f)));
        }

        [Test]
        public void Given_TwoDeclarationsReadingEachOther_When_OneIsRead_Then_ItsFallbackIsTaken()
        {
            // Arrange / Act — a cycle makes both properties invalid, which a var() treats as undeclared.
            var child = Mount("[--a:var(--b)] [--b:var(--a)]", "w-[var(--a,9px)]");

            // Assert
            Assert.That(Width(child), Is.EqualTo((StyleKeyword.Undefined, 9f)));
        }

        [Test]
        public void Given_AnUndeclaredPropertyWithoutAFallback_When_ReadAsAWidth_Then_TheWidthIsItsInitialValue()
        {
            // Arrange / Act — invalid at computed-value time: the browser computes width as unset, which for a
            // property that does not inherit is its initial value.
            var child = Mount("", "w-[var(--w)]");

            // Assert
            Assert.That(child.style.width.keyword, Is.EqualTo(StyleKeyword.Initial));
        }

        [Test]
        public void Given_AHoverColourReadingAnUndeclaredProperty_When_Hovered_Then_TheColourIsLeftToInherit()
        {
            // Arrange — colour inherits, so its unset value is the parent's: no inline value, over the base layer.
            var child = Mount("", "text-[#00ff00] hover:text-[var(--c)]");

            // Act
            using (var over = PointerOverEvent.GetPooled()) child.SimulateEvent(over);

            // Assert
            Assert.That(child.style.color.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALengthHintOnTheShorthand_When_ATextUtilityReadsALength_Then_ItSetsTheFontSize()
        {
            // Arrange / Act
            var child = Mount("[--size:12px]", "text-(length:--size)");

            // Assert
            Assert.That((child.style.fontSize.keyword, child.style.fontSize.value.value),
                Is.EqualTo((StyleKeyword.Undefined, 12f)));
        }

        [Test]
        public void Given_AHoverDeclaration_When_TheParentIsHovered_Then_TheChildReadsTheHoverValue()
        {
            // Arrange
            var child = Mount("[--w:10px] hover:[--w:40px]", "w-[var(--w)]");
            var parent = _window.rootVisualElement.Q<VisualElement>("outer");

            // Act
            using (var over = PointerOverEvent.GetPooled()) parent.SimulateEvent(over);

            // Assert
            Assert.That(Width(child), Is.EqualTo((StyleKeyword.Undefined, 40f)));
        }

        [Test]
        public void Given_ADeclarationChangedByARender_When_TheChildIsUnchanged_Then_TheChildReadsTheNewValue()
        {
            // Arrange
            var child = MountSwitchable("[--w:10px]", "w-[var(--w)]");

            // Act
            s_setOuter.Invoke("[--w:40px]");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert
            Assert.That(Width(child), Is.EqualTo((StyleKeyword.Undefined, 40f)));
        }

        // GREEN_ON_BASE(characterization): the literal a render puts in place of a reader keeps its value, as it did
        // when the reader was inert. Recording the class list after the reader leaves rather than before reddens it.
        [Test]
        public void Given_AReaderReplacedByALiteralWidth_When_Rendered_Then_TheLiteralWidthHolds()
        {
            // Arrange
            MountSwitchable("[--w:30px] w-(--w)", "");
            var outer = _window.rootVisualElement.Q<VisualElement>("outer");

            // Act
            s_setOuter.Invoke("[--w:30px] w-[20px]");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert
            Assert.That(Width(outer), Is.EqualTo((StyleKeyword.Undefined, 20f)));
        }

        [Test]
        public void Given_AReadingUtility_When_Mounted_Then_ItIsNotOnTheClassList()
        {
            // Arrange / Act
            var child = Mount("[--w:30px]", "w-[var(--w)]");

            // Assert
            Assert.That(child.ClassListContains("w-[var(--w)]"), Is.False);
        }

        [Test]
        public void Given_ALineHeightReadingAParentsDeclaration_When_Mounted_Then_TheLabelTakesThatLineHeight()
        {
            // Arrange / Act — the parent declares after its children are created, so the label resolves to a payload
            // its own class passes have to pick up afterwards.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "[--lh:12px]", children: new VNode?[]
                {
                    V.Label(className: "leading-[var(--lh)]", text: "hi"),
                }));

            // Assert
            Assert.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("<line-height=12px>hi</line-height>"));
        }

        [Test]
        public void Given_AReadPaddingBesideALiteralLonghand_When_Mounted_Then_TheLonghandKeepsItsSides()
        {
            // Arrange / Act — Tailwind emits padding before padding-inline, so px- wins its two sides.
            var child = Mount("[--pad:20px]", "px-[8px] p-(--pad)");

            // Assert
            Assert.That((child.style.paddingLeft.value.value, child.style.paddingTop.value.value),
                Is.EqualTo((8f, 20f)));
        }

        // GREEN_ON_BASE(characterization): a bracket opening the token was an inert class and must stay one.
        [TestCase("[var(--x)]")]
        [TestCase("-[var(--x)]")]
        public void Given_ABracketOpeningTheToken_When_Mounted_Then_NothingThrows(string inner)
        {
            // Act / Assert
            Assert.DoesNotThrow(() => Mount("[--x:1px]", inner));
        }

        [Test]
        public void Given_AStylesheetDeclaringAProperty_When_AChildReadsIt_Then_TheChildTakesItsValue()
        {
            // Arrange
            _window.rootVisualElement.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(RuleSheet));
            var child = Mount("css-var-host", "w-[var(--w)]");

            // Act — the engine fills the declaration in at a style pass, and the scheduler is what reads it again.
            ForcePanelUpdate(child.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);

            // Assert
            Assert.That(Width(child), Is.EqualTo((StyleKeyword.Undefined, 25f)));
        }
    }
}
