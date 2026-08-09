using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Resolved-style coverage for a class-payload variant beating a base utility that writes the same USS
    /// property. The override pairs are spelled with the base declared AFTER the payload in the bundled
    /// stylesheets, which is the arrangement source order alone resolves the wrong way; the remaining cases
    /// pin what is deliberately still left to source order, the important band, the agreement with an inline
    /// arbitrary value, and the <c>Visible</c> prop. Class membership would read the same either way, so
    /// every case runs in a real <see cref="UnityEditor.EditorWindow"/> panel with <c>StyleUtilities.uss</c>
    /// attached, drives a real breakpoint or theme change, and reads <c>resolvedStyle</c>. Each example the
    /// variants guide states is one of the cases below.
    /// <para>
    /// A case whose subject is a payload turning back OFF asserts the tuple of both states rather than the
    /// restored one behind an <c>Assume</c> for the applied one: the restored value on its own is what the
    /// un-fixed behaviour already produced, so a precondition there would report Inconclusive — or, deleted,
    /// a silent pass. GWT, one assert per case.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class VariantClassProjectionPanelTests : PanelTestBase
    {
        private const float MdBreakpoint = 768f;
        private const float WideWidth = 1000f;
        private const float NarrowWidth = 500f;

        private static readonly Color Neutral900 = new Color32(0x17, 0x17, 0x17, 0xFF);
        private static readonly Color Red500 = new Color32(0xEF, 0x44, 0x44, 0xFF);
        private static readonly Color Blue500 = new Color32(0x3B, 0x82, 0xF6, 0xFF);

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        public override void TearDown()
        {
            VelvetTheme.IsDark = false;
            base.TearDown();
        }

        // Mounts a leaf at a panel width and resolves it. The panel needs a forced layout pass before
        // resolvedStyle exists, and the responsive manipulator re-reads the width off a GeometryChangedEvent
        // the EditMode player loop never delivers.
        private VisualElement MountAt(float width, string className, FiberElementProps? props = null)
        {
            _window.position = new Rect(0, 0, width, 600);
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "leaf", className: className, props: props));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ResolveAt(width, leaf);
            return leaf;
        }

        private void ResolveAt(float width, VisualElement leaf)
        {
            _window.position = new Rect(0, 0, width, 600);
            ForcePanelUpdate(leaf.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            leaf.panel.visualTree.SimulateEvent(evt);
            ForcePanelUpdate(leaf.panel);
        }

        private void SetDark(bool dark, VisualElement leaf)
        {
            VelvetTheme.IsDark = dark;
            ForcePanelUpdate(leaf.panel);
        }

        #region Display

        [Test]
        public void Given_AHiddenBaseWithAnMdFlexVariant_When_TheRootIsWiderThanMd_Then_TheVariantDisplayWins()
        {
            // Arrange / Act — .flex is declared before .hidden, so source order alone keeps the element hidden.
            var leaf = MountAt(WideWidth, "hidden md:flex");
            Assume.That(leaf.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert
            Assert.That(leaf.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void Given_AnMdFlexVariantActiveWide_When_ThePanelShrinksBelowTheBreakpoint_Then_TheBaseHiddenReturns()
        {
            // Arrange
            var leaf = MountAt(WideWidth, "hidden md:flex");
            var wide = leaf.resolvedStyle.display;

            // Act
            ResolveAt(NarrowWidth, leaf);

            // Assert
            Assert.AreEqual((DisplayStyle.Flex, DisplayStyle.None), (wide, leaf.resolvedStyle.display));
        }

        #endregion

        #region Direction

        [Test]
        public void Given_ARowBaseWithAnMdColumnVariant_When_TheRootIsWiderThanMd_Then_TheVariantDirectionWins()
        {
            // Arrange / Act — the column family is declared before the row family, so this is the half of the
            // direction pair the mobile-first source order was never arranged for.
            var leaf = MountAt(WideWidth, "flex flex-row md:flex-col");
            Assume.That(leaf.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert
            Assert.That(leaf.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Column));
        }

        [Test]
        public void Given_AReversedBaseWithAPlainSmVariant_When_TheRootIsWiderThanSm_Then_TheVariantUnreversesIt()
        {
            // Arrange / Act — the same-axis pair, again in the direction source order declares against.
            var leaf = MountAt(WideWidth, "flex flex-col-reverse sm:flex-col");

            // Assert
            Assert.That(leaf.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Column));
        }

        [Test]
        public void Given_TwoDirectionUtilitiesAtOnePriority_When_OneIsMarkedImportant_Then_ItWinsInsteadOfTheLaterRule()
        {
            // Arrange — nothing ranks two base utilities against each other, so _layout.uss decides and the
            // row family, declared last, takes it. The bang is the only thing that can change that.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode?[]
            {
                V.Div(name: "plain", className: "flex-row flex-col"),
                V.Div(name: "important", className: "flex-row !flex-col"),
            }));
            var plain = _window.rootVisualElement.Q<VisualElement>("plain");
            var important = _window.rootVisualElement.Q<VisualElement>("important");
            ForcePanelUpdate(plain.panel);

            // Assert
            Assert.AreEqual((FlexDirection.Row, FlexDirection.Column),
                (plain.resolvedStyle.flexDirection, important.resolvedStyle.flexDirection));
        }

        #endregion

        #region Colour

        [Test]
        public void Given_AWhiteBaseWithADarkNeutralVariant_When_TheThemeTurnsDark_Then_TheVariantColourWins()
        {
            // Arrange — .bg-neutral-900 lives in the palette partial, .bg-white in the later backgrounds one.
            var leaf = MountAt(WideWidth, "bg-white dark:bg-neutral-900");
            Assume.That(leaf.resolvedStyle.backgroundColor, Is.EqualTo(Color.white),
                "Precondition: the light theme resolves the base colour");

            // Act
            SetDark(true, leaf);

            // Assert
            Assert.That(leaf.resolvedStyle.backgroundColor, Is.EqualTo(Neutral900));
        }

        [Test]
        public void Given_ADarkNeutralVariantInForce_When_TheThemeTurnsLight_Then_TheBaseColourReturns()
        {
            // Arrange
            var leaf = MountAt(WideWidth, "bg-white dark:bg-neutral-900");
            SetDark(true, leaf);
            var dark = leaf.resolvedStyle.backgroundColor;

            // Act
            SetDark(false, leaf);

            // Assert
            Assert.AreEqual((Neutral900, Color.white), (dark, leaf.resolvedStyle.backgroundColor));
        }

        #endregion

        #region Spacing, sizing and alignment

        [Test]
        public void Given_ALargePaddingBaseWithASmallerMdVariant_When_TheRootIsWiderThanMd_Then_TheVariantPaddingWins()
        {
            // Arrange / Act — the spacing scale ascends in source order, so the larger base wins on its own.
            var leaf = MountAt(WideWidth, "p-8 md:p-2");
            Assume.That(leaf.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert — --space-2 is 8px.
            Assert.That(leaf.resolvedStyle.paddingTop, Is.EqualTo(8f));
        }

        [Test]
        public void Given_AFullWidthBaseWithAFixedMdVariant_When_TheRootIsWiderThanMd_Then_TheVariantWidthWins()
        {
            // Arrange / Act — the sizing keywords are declared after the numeric steps, so w-full wins alone.
            var leaf = MountAt(WideWidth, "w-full md:w-64");
            Assume.That(leaf.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert — --space-64 is 256px.
            Assert.That(leaf.resolvedStyle.width, Is.EqualTo(256f));
        }

        [Test]
        public void Given_ANarrowerBaseAndAShorthandVariant_When_TheVariantApplies_Then_ItTakesBothAxes()
        {
            // Arrange / Act — size-8 covers everything w-4 writes, so the base is displaced outright.
            var leaf = MountAt(WideWidth, "w-4 md:size-8");

            // Assert — --space-8 is 32px on both axes.
            Assert.AreEqual((32f, 32f), (leaf.resolvedStyle.width, leaf.resolvedStyle.height));
        }

        [Test]
        public void Given_AShorthandBaseAndANarrowerVariant_When_TheVariantApplies_Then_TheShorthandKeepsTheOtherAxis()
        {
            // Arrange / Act — the containment runs the other way, so both classes stay and declaration order
            // settles the width: the invariant that every utility precedes the narrower ones it contains.
            var leaf = MountAt(WideWidth, "size-8 md:w-4");

            // Assert — --space-4 is 16px, --space-8 is 32px.
            Assert.AreEqual((16f, 32f), (leaf.resolvedStyle.width, leaf.resolvedStyle.height));
        }

        [Test]
        public void Given_AnArbitraryPivotAndAVariantKeywordPivot_When_TheVariantApplies_Then_TheKeywordWins()
        {
            // Arrange / Act — the keyword class claims the same longhand the inline pair writes, so the
            // projection has to retire the inline layer. What decides that is one row in
            // StyleArbitraryLonghands, and without a case here deleting the row leaves every parse and
            // apply test green while a variant pivot silently never moves the element.
            var leaf = MountAt(WideWidth, "origin-[10%_20%] md:origin-top-left");

            // Assert — origin-top-left is `left top`.
            Assert.AreEqual((0f, 0f),
                (leaf.resolvedStyle.transformOrigin.x, leaf.resolvedStyle.transformOrigin.y));
        }

        [Test]
        public void Given_ADisplacedBaseWidth_When_ThePanelShrinksBelowTheBreakpoint_Then_TheBaseWidthReturns()
        {
            // Arrange
            var leaf = MountAt(WideWidth, "w-4 md:size-8");
            var wide = leaf.resolvedStyle.width;

            // Act
            ResolveAt(NarrowWidth, leaf);

            // Assert
            Assert.AreEqual((32f, 16f), (wide, leaf.resolvedStyle.width));
        }

        [Test]
        public void Given_ACenteredBaseWithAnMdStartVariant_When_TheRootIsWiderThanMd_Then_TheVariantAlignmentWins()
        {
            // Arrange / Act — .items-start is declared before .items-center, so the base wins on its own.
            var leaf = MountAt(WideWidth, "items-center md:items-start");
            Assume.That(leaf.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert
            Assert.That(leaf.resolvedStyle.alignItems, Is.EqualTo(Align.FlexStart));
        }

        #endregion

        #region A payload that evaluates off without ever having applied

        [Test]
        public void Given_ATokenWrittenLiterallyAndBehindAStructuralVariant_When_TheRuleDoesNotMatch_Then_TheLiteralHolds()
        {
            // Arrange / Act — the structural pass evaluates an unconditional off on every child but the first,
            // naming a class the author also wrote literally.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: "flex", children: new VNode?[]
            {
                V.Div(name: "first", className: "p-8 first:p-8"),
                V.Div(name: "second", className: "p-8 first:p-8"),
            }));
            var first = _window.rootVisualElement.Q<VisualElement>("first");
            var second = _window.rootVisualElement.Q<VisualElement>("second");
            ForcePanelUpdate(first.panel);

            // Assert — both children keep the literal padding, whatever the rule evaluated to.
            Assert.AreEqual((32f, 32f), (first.resolvedStyle.paddingTop, second.resolvedStyle.paddingTop));
        }

        #endregion

        #region Several variants at once

        [Test]
        public void Given_AnMdAndADarkVariantOnOneProperty_When_BothAreActive_Then_TheHigherPrecedenceOneWins()
        {
            // Arrange — dark outranks a breakpoint, and .bg-blue-500 is declared before .bg-red-500's sibling
            // steps, so neither source order nor application order picks the winner here.
            var leaf = MountAt(WideWidth, "bg-white md:bg-red-500 dark:bg-blue-500");

            // Act
            SetDark(true, leaf);

            // Assert
            Assert.That(leaf.resolvedStyle.backgroundColor, Is.EqualTo(Blue500));
        }

        [Test]
        public void Given_TwoActiveVariantsOnOneProperty_When_TheHigherOneTurnsOff_Then_TheLowerOneTakesOver()
        {
            // Arrange
            var leaf = MountAt(WideWidth, "bg-white md:bg-red-500 dark:bg-blue-500");
            SetDark(true, leaf);
            var dark = leaf.resolvedStyle.backgroundColor;

            // Act — the md payload was never removed, so it is what the property must fall back to.
            SetDark(false, leaf);

            // Assert
            Assert.AreEqual((Blue500, Red500), (dark, leaf.resolvedStyle.backgroundColor));
        }

        [Test]
        public void Given_TwoActiveVariantsOnOneProperty_When_BothTurnOff_Then_TheBaseUtilityReturns()
        {
            // Arrange
            var leaf = MountAt(WideWidth, "bg-white md:bg-red-500 dark:bg-blue-500");
            SetDark(true, leaf);
            var dark = leaf.resolvedStyle.backgroundColor;

            // Act
            SetDark(false, leaf);
            ResolveAt(NarrowWidth, leaf);

            // Assert
            Assert.AreEqual((Blue500, Color.white), (dark, leaf.resolvedStyle.backgroundColor));
        }

        #endregion

        #region The important modifier

        [Test]
        public void Given_TwoColourUtilitiesAtOnePriority_When_OneIsMarkedImportant_Then_ItWinsInsteadOfTheLaterRule()
        {
            // Arrange / Act — .bg-white is declared after the palette, so it takes every ordinary tie; the
            // bang is the only thing that changes that. Both halves are the pair the variants guide states.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode?[]
            {
                V.Div(name: "plain", className: "bg-white bg-red-500"),
                V.Div(name: "important", className: "bg-white !bg-red-500"),
            }));
            var plain = _window.rootVisualElement.Q<VisualElement>("plain");
            var important = _window.rootVisualElement.Q<VisualElement>("important");
            ForcePanelUpdate(plain.panel);

            // Assert
            Assert.AreEqual((Color.white, Red500),
                (plain.resolvedStyle.backgroundColor, important.resolvedStyle.backgroundColor));
        }

        [Test]
        public void Given_AnImportantVariantAndAnImportantBase_When_TheVariantIsActive_Then_TheVariantWins()
        {
            // Arrange — both payloads sit in the important band, so the ordinary ladder has to break the tie.
            var leaf = MountAt(WideWidth, "!bg-blue-500 dark:!bg-red-500");

            // Act
            SetDark(true, leaf);

            // Assert
            Assert.That(leaf.resolvedStyle.backgroundColor, Is.EqualTo(Red500));
        }

        [Test]
        public void Given_AnImportantBaseUnderAPlainVariant_When_TheVariantIsActive_Then_TheImportantBaseHolds()
        {
            // Arrange — the escape hatch: an important base outranks every ordinary payload above it.
            var leaf = MountAt(WideWidth, "!bg-red-500 dark:bg-blue-500");

            // Act
            SetDark(true, leaf);

            // Assert
            Assert.That(leaf.resolvedStyle.backgroundColor, Is.EqualTo(Red500));
        }

        [Test]
        public void Given_AnImportantClassOverAnArbitraryBase_When_Resolved_Then_TheClassTakesTheProperty()
        {
            // Arrange / Act — the base resolves to an inline style, which UI Toolkit ranks above every USS
            // class, so only the important band can take the property back off it.
            var leaf = MountAt(WideWidth, "bg-[#3b82f6] !bg-red-500");

            // Assert
            Assert.That(leaf.resolvedStyle.backgroundColor, Is.EqualTo(Red500));
        }

        #endregion

        #region Against an inline arbitrary value

        [Test]
        public void Given_AnArbitraryBaseColourWithADarkClassVariant_When_TheThemeTurnsDark_Then_TheClassVariantWins()
        {
            // Arrange — the base resolves to an INLINE style, which UI Toolkit ranks above every USS class, so
            // the variant cannot win by being a class at all unless the inline layer stands down.
            var leaf = MountAt(WideWidth, "bg-[#fff] dark:bg-neutral-900");
            Assume.That(leaf.resolvedStyle.backgroundColor, Is.EqualTo(Color.white),
                "Precondition: the light theme resolves the inline base colour");

            // Act
            SetDark(true, leaf);

            // Assert
            Assert.That(leaf.resolvedStyle.backgroundColor, Is.EqualTo(Neutral900));
        }

        [Test]
        public void Given_AClassVariantOverAnArbitraryBase_When_TheVariantTurnsOff_Then_TheInlineBaseReturns()
        {
            // Arrange
            var leaf = MountAt(WideWidth, "bg-[#fff] dark:bg-neutral-900");
            SetDark(true, leaf);
            var dark = leaf.resolvedStyle.backgroundColor;

            // Act
            SetDark(false, leaf);

            // Assert
            Assert.AreEqual((Neutral900, Color.white), (dark, leaf.resolvedStyle.backgroundColor));
        }

        [Test]
        public void Given_AClassBaseColourWithAnArbitraryDarkVariant_When_TheThemeTurnsDark_Then_TheInlineVariantWins()
        {
            // Arrange — the mirror pairing: an inline layer above a class it must outrank.
            var leaf = MountAt(WideWidth, "bg-white dark:bg-[#171717]");

            // Act
            SetDark(true, leaf);

            // Assert
            Assert.That(leaf.resolvedStyle.backgroundColor, Is.EqualTo(Neutral900));
        }

        [Test]
        public void Given_TwoInlineLayersWithAClassBetweenThem_When_TheTopOneTurnsOff_Then_TheFloorLiftsOneStep()
        {
            // Arrange — three layers on one property: an inline base, a class above it, an inline layer above
            // that. Each state has to leave the inline base masked by a different amount.
            var leaf = MountAt(WideWidth, "w-[10px] md:w-20 dark:w-[30px]");
            SetDark(true, leaf);
            var darkWide = leaf.resolvedStyle.width;

            // Act — the top inline layer goes first, then the class below it.
            SetDark(false, leaf);
            var lightWide = leaf.resolvedStyle.width;
            ResolveAt(NarrowWidth, leaf);

            // Assert — --space-20 is 80px.
            Assert.AreEqual((30f, 80f, 10f), (darkWide, lightWide, leaf.resolvedStyle.width));
        }

        #endregion

        #region The Visible prop

        [Test]
        public void Given_AHiddenElementWithAnMdFlexVariant_When_TheRootIsWiderThanMd_Then_ThePropStillHides()
        {
            // Arrange / Act — the prop writes the same `hidden` utility a variant payload can, so the two need
            // a ranking between them rather than the stylesheet's declaration order.
            var leaf = MountAt(WideWidth, "md:flex", new FiberElementProps { Visible = false });
            Assume.That(leaf.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert
            Assert.That(leaf.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Given_AHiddenElementWithAnMdFlexVariant_When_ThePropTurnsVisible_Then_TheVariantDisplayApplies()
        {
            // Arrange
            var leaf = MountAt(WideWidth, "md:flex", new FiberElementProps { Visible = false });
            var hidden = leaf.resolvedStyle.display;

            // Act
            FiberPropApplier.ApplyVisible(leaf, true);
            ForcePanelUpdate(leaf.panel);

            // Assert
            Assert.AreEqual((DisplayStyle.None, DisplayStyle.Flex), (hidden, leaf.resolvedStyle.display));
        }

        [Test]
        public void Given_ALiteralHiddenClass_When_ThePropIsVisible_Then_TheClassKeepsHidingItEitherWay()
        {
            // Arrange / Act — Visible clears only its own layer, so an authored `hidden` survives it. Both
            // spellings are here because the prop takes a different path depending on whether some other
            // payload has already built a model for the element, and the two must not disagree.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode?[]
            {
                V.Div(name: "bare", className: "hidden", props: new FiberElementProps { Visible = true }),
                V.Div(name: "modelled", className: "hidden md:bg-red-500",
                    props: new FiberElementProps { Visible = true }),
            }));
            var bare = _window.rootVisualElement.Q<VisualElement>("bare");
            var modelled = _window.rootVisualElement.Q<VisualElement>("modelled");
            ForcePanelUpdate(bare.panel);

            // Assert
            Assert.AreEqual((DisplayStyle.None, DisplayStyle.None),
                (bare.resolvedStyle.display, modelled.resolvedStyle.display));
        }

        #endregion
    }
}
