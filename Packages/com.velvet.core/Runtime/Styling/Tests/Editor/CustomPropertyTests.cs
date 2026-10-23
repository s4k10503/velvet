using System.Linq;
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

        // inner and an absolute sibling at z-10, lit, under outer.
        [Component]
        private static VNode RenderStacked()
        {
            var (outer, setOuter) = Hooks.UseState(s_outer);
            s_setOuter = setOuter;
            return V.Div(name: "outer", className: outer, children: new VNode?[]
            {
                V.Div(name: "inner", className: s_inner),
                V.Div(name: "lit", className: "absolute z-10"),
            });
        }

        private VisualElement MountStacked(string outer, string inner)
        {
            (s_outer, s_inner) = (outer, inner);
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderStacked));
            return _window.rootVisualElement.Q<VisualElement>("inner");
        }

        // The order inner, or the wrapper holding it, and lit paint in within lit's layer.
        private string PaintOrder()
        {
            var inner = _window.rootVisualElement.Q<VisualElement>("inner");
            var slot = inner.parent.name == "outer" || inner.parent.ClassListContains(FiberZLayerCoordinator.FrontMarkerClass)
                ? inner
                : inner.parent;
            var lit = _window.rootVisualElement.Q<VisualElement>("lit");
            return string.Join(",", lit.parent.Children().Where(child => child == slot || child == lit)
                .Select(child => child == lit ? "lit" : "inner"));
        }

        // Attaches CustomPropertyRule.uss, and whether it loaded, which the cases reading it fold into their
        // assertion: a tree without the sheet has no rule to read.
        private bool AddRuleSheet()
        {
            var sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(RuleSheet);
            if (sheet != null)
            {
                _window.rootVisualElement.styleSheets.Add(sheet);
            }
            return sheet != null;
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

        // GREEN_ON_BASE(refactor): the shown string it reads through RenderedTextProbe is the leaf's text on the base.
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
            Assert.That(RenderedTextProbe.Of(_window.rootVisualElement.Q<Label>()), Is.EqualTo("<line-height=12px>hi</line-height>"));
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
        public void Given_AStylesheetDeclaringAProperty_When_AStylePassRuns_Then_TheChildTakesItsValueInThatPass()
        {
            // Arrange
            var loaded = AddRuleSheet();
            var child = Mount("css-var-host", "w-[var(--w)]");

            // Act — no scheduler tick: the value has to be written while the style pass runs.
            ForcePanelUpdate(child.panel);

            // Assert
            Assert.That((loaded, Width(child)), Is.EqualTo((true, (StyleKeyword.Undefined, 25f))));
        }

        [Test]
        public void Given_AThemeRuleOnTheReader_When_AnInheritedColourIsUnset_Then_TheParentsColourShows()
        {
            // Arrange — the reader's own rule sets a colour, which the browser's unset does not let through.
            var loaded = AddRuleSheet();
            var child = Mount("text-[#ff0000]", "css-var-themed hover:text-[var(--c)]");

            // Act
            using (var over = PointerOverEvent.GetPooled()) child.SimulateEvent(over);
            ForcePanelUpdate(child.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);
            ForcePanelUpdate(child.panel);

            // Assert
            Assert.That((loaded, child.resolvedStyle.color), Is.EqualTo((true, new UnityEngine.Color(1f, 0f, 0f, 1f))));
        }

        // GREEN_ON_BASE(refactor): the shown string it reads through RenderedTextProbe is the leaf's text on the base.
        [Test]
        public void Given_AnUnresolvableLineHeight_When_AnAncestorSetsOne_Then_TheLabelInheritsIt()
        {
            // Arrange / Act — line-height inherits, so the own leading-[30px] the reader outranks does not show.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "leading-[20px]", children: new VNode?[]
                {
                    V.Label(className: "leading-[30px] leading-[var(--lh)]", text: "hi"),
                }));

            // Assert
            Assert.That(RenderedTextProbe.Of(_window.rootVisualElement.Q<Label>()), Is.EqualTo("<line-height=20px>hi</line-height>"));
        }

        [TestCase("bg-[#ff0000]/(--a)")]
        [TestCase("bg-[#ff0000]/[var(--a)]")]
        public void Given_AModifierReadingAPercentage_When_Mounted_Then_TheColourTakesThatAlpha(string inner)
        {
            // Arrange / Act
            var child = Mount("[--a:50%]", inner);

            // Assert
            Assert.That(child.style.backgroundColor.value.a, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Given_AModifierReadingANumber_When_Mounted_Then_TheColourIsUnset()
        {
            // Arrange / Act — color-mix() takes a percentage, so a bare number is invalid at computed-value time.
            var child = Mount("[--a:0.5]", "bg-[#ff0000]/(--a)");

            // Assert
            Assert.That(child.style.backgroundColor.keyword, Is.EqualTo(StyleKeyword.Initial));
        }

        [Test]
        public void Given_AnArbitraryPaddingOfTwoValues_When_Mounted_Then_EachSideTakesItsValue()
        {
            // Arrange / Act
            var child = Mount("", "[padding:1px_2px]");

            // Assert
            Assert.That((child.style.paddingTop.value.value, child.style.paddingRight.value.value,
                child.style.paddingBottom.value.value, child.style.paddingLeft.value.value),
                Is.EqualTo((1f, 2f, 1f, 2f)));
        }

        [Test]
        public void Given_AReadPaddingOfThreeValues_When_Mounted_Then_TheLeftTakesTheSecond()
        {
            // Arrange / Act
            var child = Mount("[--p:1px_2px_3px]", "p-(--p)");

            // Assert
            Assert.That((child.style.paddingBottom.value.value, child.style.paddingLeft.value.value), Is.EqualTo((3f, 2f)));
        }

        [Test]
        public void Given_AnArbitraryPaddingBesideALiteralTop_When_Mounted_Then_TheLiteralTopWins()
        {
            // Arrange / Act — Tailwind emits padding ahead of padding-top, whatever order the class list holds.
            var child = Mount("", "pt-[9px] [padding:1px_2px]");

            // Assert
            Assert.That((child.style.paddingTop.value.value, child.style.paddingRight.value.value), Is.EqualTo((9f, 2f)));
        }

        [Test]
        public void Given_ATwoValuePaddingBesideALiteralAxis_When_Mounted_Then_TheAxisKeepsItsSides()
        {
            // Arrange / Act — Tailwind emits padding ahead of padding-inline, so px- wins left and right.
            var child = Mount("", "px-[8px] p-[1px_2px]");

            // Assert
            Assert.That((child.style.paddingLeft.value.value, child.style.paddingTop.value.value), Is.EqualTo((8f, 1f)));
        }

        [Test]
        public void Given_ASplitPaddingReaderAndALaterAxisAdded_When_Rendered_Then_TheAxisTakesItsSides()
        {
            // Arrange
            MountSwitchable("[--p:1px_2px] p-(--p)", "");
            var outer = _window.rootVisualElement.Q<VisualElement>("outer");

            // Act
            s_setOuter.Invoke("[--p:1px_2px] p-(--p) px-[8px]");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert
            Assert.That((outer.style.paddingLeft.value.value, outer.style.paddingTop.value.value), Is.EqualTo((8f, 1f)));
        }

        [Test]
        public void Given_ASplitPaddingReaderAndALaterAxisRemoved_When_Rendered_Then_TheSidesComeBack()
        {
            // Arrange
            MountSwitchable("[--p:1px_2px] p-(--p) px-[8px]", "");
            var outer = _window.rootVisualElement.Q<VisualElement>("outer");

            // Act
            s_setOuter.Invoke("[--p:1px_2px] p-(--p)");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert
            Assert.That((outer.style.paddingLeft.value.value, outer.style.paddingTop.value.value), Is.EqualTo((2f, 1f)));
        }

        [Test]
        public void Given_ASplitPaddingReaderWithOneSideNotOutranked_When_Mounted_Then_ThatSideIsWritten()
        {
            // Arrange / Act — px- takes left and right, pt- the top, so only the bottom is the reader's.
            var child = Mount("[--pad:1px_2px]", "p-(--pad) px-[8px] pt-[3px]");

            // Assert
            Assert.That((child.style.paddingBottom.value.value, child.style.paddingLeft.value.value, child.style.paddingTop.value.value),
                Is.EqualTo((1f, 8f, 3f)));
        }

        [Test]
        public void Given_APaintedReaderBesideALiteralTailwindEmitsLater_When_Mounted_Then_TheLiteralWins()
        {
            // Arrange / Act — gap-2 sorts after gap-(--g), whatever order the class list writes them in. The
            // reader alone, beside it, is what shows the reader applies at all.
            _mounted = V.Mount(_window.rootVisualElement, V.Fragment(
                V.Div(className: "[--g:20px] flex-row gap-(--g)", children: new VNode?[]
                {
                    V.Div(),
                    V.Div(name: "alone"),
                }),
                V.Div(className: "[--g:20px] flex-row gap-2 gap-(--g)", children: new VNode?[]
                {
                    V.Div(),
                    V.Div(name: "second"),
                })));

            // Assert
            Assert.That(new[]
                {
                    _window.rootVisualElement.Q<VisualElement>("alone").style.marginLeft.value.value,
                    _window.rootVisualElement.Q<VisualElement>("second").style.marginLeft.value.value,
                },
                Is.EqualTo(new[] { 20f, 8f }));
        }

        [Test]
        public void Given_TwoReadersOfOneWidth_When_Mounted_Then_TheOneTailwindEmitsLaterWins()
        {
            // Arrange / Act — w-[var(--b)] sorts after w-[var(--a)], though the class list writes it first.
            var child = Mount("[--a:10px] [--b:20px]", "w-[var(--b)] w-[var(--a)]");

            // Assert
            Assert.That(Width(child), Is.EqualTo((StyleKeyword.Undefined, 20f)));
        }

        [Test]
        public void Given_AZIndexReadingADeclaration_When_TheDeclarationChanges_Then_TheElementRestacks()
        {
            // Arrange — unstacked while nothing declares --z, as z-auto leaves it, then raised above lit.
            var child = MountStacked("", "absolute z-(--z)");
            var unresolved = child.parent.name;
            s_setOuter.Invoke("[--z:20]");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
            var raised = PaintOrder();

            // Act
            s_setOuter.Invoke("[--z:5]");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert
            Assert.That((unresolved, raised, PaintOrder()), Is.EqualTo(("outer", "lit,inner", "inner,lit")));
        }

        // GREEN_ON_BASE(characterization): the base reads no z-* bracket holding a var(), so it leaves this element unstacked too.
        [Test]
        public void Given_AZIndexReadingAuto_When_Resolved_Then_TheElementIsUnstacked()
        {
            // Arrange
            var child = MountStacked("[--z:auto]", "absolute z-(--z)");

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);

            // Assert
            Assert.That(child.parent.name, Is.EqualTo("outer"));
        }

        [Test]
        public void Given_AClipPathWrappedZIndexReadingADeclaration_When_Resolved_Then_TheWrapperRestacks()
        {
            // Arrange
            var child = MountStacked("[--z:20]", "absolute clip-path-[circle(40%)] z-(--z)");

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);

            // Assert
            Assert.That(PaintOrder(), Is.EqualTo("lit,inner"));
        }

        [Test]
        public void Given_AStylesheetRuleRedeclaringAProperty_When_ItsClassIsRemoved_Then_TheReaderTakesTheOuterValue()
        {
            // Arrange
            var loaded = AddRuleSheet();
            var child = MountSwitchable("css-var-host css-var-alt", "w-[var(--w)]");
            ForcePanelUpdate(child.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);
            var redeclared = Width(child);

            // Act — a theme class turning off: the rule redeclaring --w stops matching the declaring element.
            s_setOuter.Invoke("css-var-host");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
            ForcePanelUpdate(child.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);

            // Assert
            Assert.That((loaded, redeclared, Width(child)),
                Is.EqualTo((true, (StyleKeyword.Undefined, 40f), (StyleKeyword.Undefined, 25f))));
        }

        [Test]
        public void Given_AShorthandWhoseFallbackHoldsAFunction_When_Mounted_Then_TheFallbackIsTaken()
        {
            // Arrange / Act — the group closes at the parenthesis matching its own, not at calc()'s.
            var child = Mount("", "w-(--w,calc(6px*2))");

            // Assert
            Assert.That(Width(child), Is.EqualTo((StyleKeyword.Undefined, 12f)));
        }

        // GREEN_ON_BASE(characterization): the base reads no parenthesised modifier, so a hinted one is inert there too.
        [Test]
        public void Given_AModifierShorthandWithATypeHint_When_Mounted_Then_TheClassIsInert()
        {
            // Arrange / Act — Tailwind reads a parenthesised modifier as a bare var(), and declines one with a hint.
            var child = Mount("[--a:50%]", "bg-[#ff0000]/(number:--a)");

            // Assert
            Assert.That(child.style.backgroundColor.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ABorderSideReadingAnUndeclaredProperty_When_Mounted_Then_ItsColourIsUnset()
        {
            // Arrange / Act — Tailwind writes an untyped var() on a border side as the side's colour.
            var child = Mount("", "border-t-[var(--c)]");

            // Assert
            Assert.That(child.style.borderTopColor.keyword, Is.EqualTo(StyleKeyword.Initial));
        }

        [Test]
        public void Given_AColourHintedReaderOfAnUndeclaredProperty_When_Mounted_Then_TheBackgroundIsUnset()
        {
            // Arrange / Act
            var child = Mount("", "bg-[color:var(--m)]");

            // Assert
            Assert.That(child.style.backgroundColor.keyword, Is.EqualTo(StyleKeyword.Initial));
        }

        [Test]
        public void Given_ATranslateReadingAnUndeclaredProperty_When_Important_Then_ItTranslatesByNothing()
        {
            // Arrange / Act — each axis variable is registered with an initial value of 0.
            var child = Mount("", "translate-x-[7px] translate-[var(--m)]!");

            // Assert
            Assert.That(child.style.translate.value.x.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AFlexReadingAnUndeclaredProperty_When_Important_Then_TheLiteralGrowIsReplaced()
        {
            // Arrange / Act
            var child = Mount("", "flex-[2] flex-[var(--m)]!");

            // Assert
            Assert.That(child.style.flexGrow.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ASkewAxisReadingAnUndeclaredProperty_When_Important_Then_ThatAxisIsNotSheared()
        {
            // Arrange / Act — the transform reads the axis variable with an empty fallback.
            var child = Mount("", "skew-x-[12deg] skew-x-[var(--m)]!");
            var skews = _mounted.Root.Reconciler.Context.SkewBindings;

            // Assert
            Assert.That(skews.TryGetValue(child, out var binding) ? binding.Spec.XDeg : 0f, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AStylesheetDeclaringAProperty_When_AChildReadsIt_Then_TheChildTakesItsValue()
        {
            // Arrange
            var loaded = AddRuleSheet();
            var child = Mount("css-var-host", "w-[var(--w)]");

            // Act — the engine fills the declaration in at a style pass, and the scheduler is what reads it again.
            ForcePanelUpdate(child.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);

            // Assert
            Assert.That((loaded, Width(child)), Is.EqualTo((true, (StyleKeyword.Undefined, 25f))));
        }
    }
}
