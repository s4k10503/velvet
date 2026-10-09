using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of arbitrary-value class parsing and application
    /// (<see cref="StyleArbitraryValueResolver"/>), the <c>prefix-[value]</c> inline-style syntax.
    /// <list type="bullet">
    /// <item>A <c>prefix-[value]</c> class parses to the property the prefix names, with the value and the unit
    /// implied by its suffix (<c>%</c> is percent, <c>px</c> or no suffix is pixel).</item>
    /// <item>A leading <c>-</c> negates the numeric value (negative classes).</item>
    /// <item><c>text-[...]</c> is overloaded: a parseable color sets the text color, otherwise the value is a
    /// font size. <c>bg-[...]</c> accepts only a color and declines a non-color value so an image resolver can
    /// handle it.</item>
    /// <item>A null input, an unknown prefix, empty brackets, a non-numeric value, a missing closing bracket,
    /// or a non-finite number (NaN / Infinity) all fail to parse.</item>
    /// <item>Applying a parsed style writes the corresponding inline style(s); shorthands (inset / padding /
    /// margin / border-radius) write every edge or corner they cover, and clearing reverts each to the USS
    /// default (<see cref="StyleKeyword.Null"/>).</item>
    /// <item>A later individual edge overrides the matching edge of an earlier shorthand.</item>
    /// <item>Through reconciliation an arbitrary class applies its inline style without entering the class list,
    /// updates when its value changes, and clears when removed; this holds on both the linear and hash-set
    /// class-diff paths.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ArbitraryValueTests
    {
        #region TryParse — Valid Inputs (Size)

        static readonly object[] SizePropertyValueUnitCases =
        {
            // Given_HeightPercentClass_When_Parsed_Then_ResolvesHeightAsPercentLength
            new object[] { "h-[15%]", ArbitraryProperty.Height, 15f, LengthUnit.Percent },
            // Given_MinHeightPixelClass_When_Parsed_Then_ResolvesMinHeightAsPixelLength
            new object[] { "min-h-[60px]", ArbitraryProperty.MinHeight, 60f, LengthUnit.Pixel },
            // Given_WidthValueWithoutUnit_When_Parsed_Then_DefaultsToPixelLength
            new object[] { "w-[200]", ArbitraryProperty.Width, 200f, LengthUnit.Pixel },
            // Given_MaxWidthPercentClass_When_Parsed_Then_ResolvesMaxWidthAsPercentLength
            new object[] { "max-w-[50%]", ArbitraryProperty.MaxWidth, 50f, LengthUnit.Percent },
            // Given_MaxHeightPixelClass_When_Parsed_Then_ResolvesMaxHeightAsPixelLength
            new object[] { "max-h-[300px]", ArbitraryProperty.MaxHeight, 300f, LengthUnit.Pixel },
            // Given_MinWidthPixelClass_When_Parsed_Then_ResolvesMinWidthAsPixelLength
            new object[] { "min-w-[120px]", ArbitraryProperty.MinWidth, 120f, LengthUnit.Pixel },
            // Given_DecimalPercentValue_When_Parsed_Then_PreservesFractionalValue
            new object[] { "h-[31.25%]", ArbitraryProperty.Height, 31.25f, LengthUnit.Percent },
        };

        [TestCaseSource(nameof(SizePropertyValueUnitCases))]
        public void Given_SizeClass_When_Parsed_Then_ResolvesPropertyValueUnit(
            string className, object property, float value, LengthUnit unit)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value, s.Unit), Is.EqualTo(((ArbitraryProperty)property, value, unit)));
        }

        #endregion

        #region TryParse — Valid Inputs (rem unit)

        [Test]
        public void Given_TopRemClass_When_Parsed_Then_ResolvesTopAsPixelLengthAtSixteenPerRem()
        {
            // Act — rem has no UI Toolkit unit, so 3.5rem resolves to a fixed 3.5 * 16 = 56px.
            var ok = StyleArbitraryValueResolver.TryParse("top-[3.5rem]", out var s);

            // Assert — `ok` is part of the assertion so a regression that stops recognizing rem goes red.
            Assert.That((ok, s.Property, s.Value, s.Unit),
                Is.EqualTo((true, ArbitraryProperty.Top, 56f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_FontSizeRemClass_When_Parsed_Then_ConvertsRemToPixels()
        {
            // Act — text-[1.25rem] is not a color, so it is a font size of 1.25 * 16 = 20px.
            var ok = StyleArbitraryValueResolver.TryParse("text-[1.25rem]", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FontSize, 20f)));
        }

        [Test]
        public void Given_NegativeMarginRemClass_When_Parsed_Then_ResolvesNegativePixelLength()
        {
            // Act — a leading '-' negates the 1rem (= 16px) value.
            var ok = StyleArbitraryValueResolver.TryParse("-mt-[1rem]", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value, s.Unit),
                Is.EqualTo((true, ArbitraryProperty.MarginTop, -16f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_ViewportHeightClass_When_Parsed_Then_DeclinesAsUnsupportedUnit()
        {
            // Act — vh/vw need the panel viewport and are not yet supported, so the class stays unrecognized.
            var ok = StyleArbitraryValueResolver.TryParse("h-[50vh]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        #endregion

        #region Color Opacity Modifier ({bg|text|border}-{color}/N)

        [Test]
        public void Given_BackgroundPaletteColorWithOpacityModifier_When_Parsed_Then_ResolvesBaseColorWithAlpha()
        {
            // Act — bg-red-500 (#ef4444) at 50% alpha.
            var ok = StyleArbitraryValueResolver.TryParse("bg-red-500/50", out var s);

            // Assert — ok + property + base red channel + alpha in one shot (RED-without: ok is false).
            Assert.That((ok, s.Property, Mathf.RoundToInt(s.Color.r * 255f), s.Color.a),
                Is.EqualTo((true, ArbitraryProperty.BackgroundColor, 239, 0.5f)));
        }

        [Test]
        public void Given_TextBlackWithOpacityModifier_When_Parsed_Then_ResolvesBlackWithAlpha()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("text-black/75", out var s);

            // Assert
            Assert.That((ok, s.Property, Mathf.RoundToInt(s.Color.r * 255f), s.Color.a),
                Is.EqualTo((true, ArbitraryProperty.TextColor, 0, 0.75f)));
        }

        [Test]
        public void Given_BorderWhiteWithOpacityModifier_When_Parsed_Then_ResolvesWhiteWithAlpha()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("border-white/10", out var s);

            // Assert
            Assert.That((ok, s.Property, Mathf.RoundToInt(s.Color.r * 255f), s.Color.a),
                Is.EqualTo((true, ArbitraryProperty.BorderColor, 255, 0.1f)));
        }

        [Test]
        public void Given_ArbitraryHexBaseWithOpacityModifier_When_Parsed_Then_ResolvesBaseWithAlpha()
        {
            // Act — the modifier '/' sits after the ']' of the arbitrary base.
            var ok = StyleArbitraryValueResolver.TryParse("bg-[#3b82f6]/50", out var s);

            // Assert
            Assert.That((ok, s.Property, Mathf.RoundToInt(s.Color.r * 255f), s.Color.a),
                Is.EqualTo((true, ArbitraryProperty.BackgroundColor, 59, 0.5f)));
        }

        [Test]
        public void Given_AlphaHexBaseWithOpacityModifier_When_Parsed_Then_ModifierReplacesBaseAlpha()
        {
            // Act — base alpha 0x80 (~0.5); the /20 modifier is authoritative and replaces it.
            var ok = StyleArbitraryValueResolver.TryParse("bg-[#ef444480]/20", out var s);

            // Assert
            Assert.That((ok, s.Color.a), Is.EqualTo((true, 0.2f)));
        }

        [Test]
        public void Given_PaletteColorWithArbitraryAlphaFraction_When_Parsed_Then_UsesFraction()
        {
            // Act — the bracketed 0..1 fraction form.
            var ok = StyleArbitraryValueResolver.TryParse("bg-blue-500/[0.32]", out var s);

            // Assert
            Assert.That((ok, s.Color.a), Is.EqualTo((true, 0.32f)));
        }

        [Test]
        public void Given_OpacityModifierAbove100_When_Parsed_Then_Declines()
        {
            // Act + Assert — an out-of-range percent is not a recognized utility (declines, no clamp).
            Assert.That(StyleArbitraryValueResolver.TryParse("bg-red-500/150", out _), Is.False);
        }

        [Test]
        public void Given_AspectRatioWithSlashInBracket_When_Parsed_Then_TreatedAsRatioNotModifier()
        {
            // Act — an in-bracket '/' must not be read as an opacity modifier.
            var ok = StyleArbitraryValueResolver.TryParse("aspect-[4/3]", out var s);

            // Assert
            Assert.That((ok, s.Property), Is.EqualTo((true, ArbitraryProperty.AspectRatio)));
        }

        [Test]
        public void Given_PaletteOpacityModifierClass_When_PredicateChecked_Then_True()
        {
            // Act + Assert — the dispatch-gate predicate recognizes the no-bracket modifier form.
            Assert.That(StyleColorValueParser.HasColorOpacityModifier("bg-red-500/50"), Is.True);
        }

        [Test]
        public void Given_OpacityModifierStyle_When_Applied_Then_SetsInlineBackgroundColorAlpha()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("bg-red-500/50", out var s);

            // Act
            StyleArbitraryValueResolver.Apply(el, in s);

            // Assert — RED-without: parse fails so s is default and backgroundColor stays unset (a = 0).
            Assert.That(el.style.backgroundColor.value.a, Is.EqualTo(0.5f).Within(0.001f));
        }

        #endregion

        #region Static-scale utility names (no bracket: -mt-2, -rotate-6, translate-x-1/2)

        [Test]
        public void Given_NegativeStaticMarginTop_When_Parsed_Then_ResolvesNegativeScaledPixels()
        {
            // Act — the static-scale negative margin (no bracket); --space-2 == 8px.
            var ok = StyleArbitraryValueResolver.TryParse("-mt-2", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value, s.Unit),
                Is.EqualTo((true, ArbitraryProperty.MarginTop, -8f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_NegativeStaticMarginXShorthand_When_Parsed_Then_ResolvesNegativePixels()
        {
            // Act — -mx-4 is the left+right shorthand; --space-4 == 16px.
            var ok = StyleArbitraryValueResolver.TryParse("-mx-4", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.MarginX, -16f)));
        }

        [Test]
        public void Given_NegativeStaticMarginExtendedScale_When_Parsed_Then_ResolvesNegativePixels()
        {
            // Act — -mt-44 exercises the extended scale (44 -> 176px) shared with the USS scale additions.
            var ok = StyleArbitraryValueResolver.TryParse("-mt-44", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.MarginTop, -176f)));
        }

        [Test]
        public void Given_NegativeRotateStatic_When_Parsed_Then_ResolvesNegativeDegrees()
        {
            // Act — the alias -rotate-6 (the bundled USS spells negatives as .rotate-n6).
            var ok = StyleArbitraryValueResolver.TryParse("-rotate-6", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.Rotate, -6f)));
        }

        [Test]
        public void Given_TranslateXHalfFraction_When_Parsed_Then_ResolvesFiftyPercent()
        {
            // Act — the fraction alias translate-x-1/2 (a '/' is illegal in a USS selector).
            var ok = StyleArbitraryValueResolver.TryParse("translate-x-1/2", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value, s.Unit),
                Is.EqualTo((true, ArbitraryProperty.TranslateX, 50f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_NegativeTranslateYFraction_When_Parsed_Then_ResolvesNegativePercent()
        {
            // Act — -translate-y-1/2 negates the fraction.
            var ok = StyleArbitraryValueResolver.TryParse("-translate-y-1/2", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value, s.Unit),
                Is.EqualTo((true, ArbitraryProperty.TranslateY, -50f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_NegativePxTranslate_When_Parsed_Then_ResolvesNegativeScaledPixels()
        {
            // Act — -translate-x-6 (the USS spells it .translate-x-n6); --space-6 == 24px.
            var ok = StyleArbitraryValueResolver.TryParse("-translate-x-6", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value, s.Unit),
                Is.EqualTo((true, ArbitraryProperty.TranslateX, -24f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_PositiveStaticMargin_When_Checked_Then_NotClaimedSoItKeepsItsUssClass()
        {
            // Act — a positive mt-2 HAS a USS class, so the resolver must NOT claim it (only the unsignable
            // names route inline; intercepting mt-2 would flip its cascade priority from class to inline).
            var claimed = StyleArbitraryValueResolver.MayBeStaticScale("mt-2");
            var parsed = StyleArbitraryValueResolver.TryParse("mt-2", out _);

            // Assert
            Assert.That((claimed, parsed), Is.EqualTo((false, false)));
        }

        #endregion

        #region TryParse — Valid Inputs (Position)

        static readonly object[] PositionPropertyValueUnitCases =
        {
            // Given_TopPercentClass_When_Parsed_Then_ResolvesTopAsPercentLength
            new object[] { "top-[15%]", ArbitraryProperty.Top, 15f, LengthUnit.Percent },
            // Given_BottomDecimalPercentClass_When_Parsed_Then_ResolvesBottomAsPercentLength
            new object[] { "bottom-[31.25%]", ArbitraryProperty.Bottom, 31.25f, LengthUnit.Percent },
            // Given_LeftPercentClass_When_Parsed_Then_ResolvesLeftAsPercentLength
            new object[] { "left-[50%]", ArbitraryProperty.Left, 50f, LengthUnit.Percent },
        };

        [TestCaseSource(nameof(PositionPropertyValueUnitCases))]
        public void Given_PositionClass_When_Parsed_Then_ResolvesPropertyValueUnit(
            string className, object property, float value, LengthUnit unit)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value, s.Unit), Is.EqualTo(((ArbitraryProperty)property, value, unit)));
        }

        [Test]
        public void Given_RightPixelClass_When_Parsed_Then_ResolvesRightWithValue()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("right-[16px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.Right, 16f)));
        }

        static readonly object[] InsetShorthandPropertyCases =
        {
            // Given_InsetShorthandClass_When_Parsed_Then_ResolvesInsetProperty
            new object[] { "inset-[10px]", ArbitraryProperty.Inset },
            // Given_InsetXShorthandClass_When_Parsed_Then_ResolvesInsetXProperty
            new object[] { "inset-x-[20px]", ArbitraryProperty.InsetX },
            // Given_InsetYShorthandClass_When_Parsed_Then_ResolvesInsetYProperty
            new object[] { "inset-y-[20px]", ArbitraryProperty.InsetY },
        };

        [TestCaseSource(nameof(InsetShorthandPropertyCases))]
        public void Given_InsetShorthandClass_When_Parsed_Then_ResolvesProperty(
            string className, object property)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That(s.Property, Is.EqualTo((ArbitraryProperty)property));
        }

        #endregion

        #region TryParse — Valid Inputs (Padding)

        static readonly object[] PaddingPropertyValueCases =
        {
            // Given_PaddingShorthandClass_When_Parsed_Then_ResolvesPaddingWithValue
            new object[] { "p-[24px]", ArbitraryProperty.Padding, 24f },
            // Given_PaddingXShorthandClass_When_Parsed_Then_ResolvesPaddingXWithValue
            new object[] { "px-[32px]", ArbitraryProperty.PaddingX, 32f },
            // Given_PaddingYShorthandClass_When_Parsed_Then_ResolvesPaddingYWithValue
            new object[] { "py-[24px]", ArbitraryProperty.PaddingY, 24f },
            // Given_PaddingTopClass_When_Parsed_Then_ResolvesPaddingTopWithValue
            new object[] { "pt-[26px]", ArbitraryProperty.PaddingTop, 26f },
        };

        [TestCaseSource(nameof(PaddingPropertyValueCases))]
        public void Given_PaddingClass_When_Parsed_Then_ResolvesPropertyValue(
            string className, object property, float value)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo(((ArbitraryProperty)property, value)));
        }

        [Test]
        public void Given_PaddingLeftClass_When_Parsed_Then_ResolvesPaddingLeftProperty()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("pl-[16px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That(s.Property, Is.EqualTo(ArbitraryProperty.PaddingLeft));
        }

        #endregion

        #region TryParse — Valid Inputs (Margin)

        static readonly object[] MarginShorthandPropertyCases =
        {
            // Given_MarginShorthandClass_When_Parsed_Then_ResolvesMarginProperty
            new object[] { "m-[10px]", ArbitraryProperty.Margin },
            // Given_MarginXShorthandClass_When_Parsed_Then_ResolvesMarginXProperty
            new object[] { "mx-[20px]", ArbitraryProperty.MarginX },
        };

        [TestCaseSource(nameof(MarginShorthandPropertyCases))]
        public void Given_MarginShorthandClass_When_Parsed_Then_ResolvesProperty(
            string className, object property)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That(s.Property, Is.EqualTo((ArbitraryProperty)property));
        }

        static readonly object[] MarginPropertyValueCases =
        {
            // Given_MarginYShorthandClass_When_Parsed_Then_ResolvesMarginYWithValue
            new object[] { "my-[50px]", ArbitraryProperty.MarginY, 50f },
            // Given_MarginTopClass_When_Parsed_Then_ResolvesMarginTopWithValue
            new object[] { "mt-[20px]", ArbitraryProperty.MarginTop, 20f },
        };

        [TestCaseSource(nameof(MarginPropertyValueCases))]
        public void Given_MarginClass_When_Parsed_Then_ResolvesPropertyValue(
            string className, object property, float value)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo(((ArbitraryProperty)property, value)));
        }

        #endregion

        #region TryParse — Valid Inputs (BorderRadius / FontSize)

        [Test]
        public void Given_RoundedClass_When_Parsed_Then_ResolvesBorderRadiusWithValue()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("rounded-[36px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.BorderRadius, 36f)));
        }

        [Test]
        public void Given_TextLengthClass_When_Parsed_Then_ResolvesFontSizeWithValue()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("text-[18px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.FontSize, 18f)));
        }

        #endregion

        #region TryParse — Valid Inputs (Color)

        [Test]
        public void Given_TextHexColorClass_When_Parsed_Then_ResolvesTextColor()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("text-[#ff0000]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Color), Is.EqualTo((ArbitraryProperty.TextColor, Color.red)));
        }

        [Test]
        public void Given_TextShortHexColorClass_When_Parsed_Then_ResolvesTextColor()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("text-[#fff]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Color), Is.EqualTo((ArbitraryProperty.TextColor, Color.white)));
        }

        [Test]
        public void Given_BackgroundHexColorClass_When_Parsed_Then_ResolvesBackgroundColor()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("bg-[#00ff00]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Color), Is.EqualTo((ArbitraryProperty.BackgroundColor, Color.green)));
        }

        [Test]
        public void Given_BackgroundRgbaColorClass_When_Parsed_Then_ResolvesChannelsAndAlpha()
        {
            // Act — CSS rgba() functional notation (channels 0..255, alpha 0..1).
            var ok = StyleArbitraryValueResolver.TryParse("bg-[rgba(255,0,0,0.5)]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: rgba() is a recognized arbitrary color");
            Assert.That((s.Property, s.Color.r, s.Color.a),
                Is.EqualTo((ArbitraryProperty.BackgroundColor, 1f, 0.5f)));
        }

        [Test]
        public void Given_TextWithLengthValue_When_Parsed_Then_ResolvesFontSizeNotColor()
        {
            // Act — the text-[...] overload resolves a length to font size rather than a color
            var ok = StyleArbitraryValueResolver.TryParse("text-[18px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That(s.Property, Is.EqualTo(ArbitraryProperty.FontSize));
        }

        [Test]
        public void Given_BackgroundNonColorValue_When_Parsed_Then_Declines()
        {
            // Act — bg-[addr:...] is an image, so the color resolver declines and defers to the image resolver
            var ok = StyleArbitraryValueResolver.TryParse("bg-[addr:logo]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_TextAlphaHexColorClass_When_Parsed_Then_PreservesAlphaChannel()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("text-[#ff000080]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assume.That(s.Color.r, Is.EqualTo(1f).Within(0.001f), "Precondition: the red channel is fully on");
            Assert.That(s.Color.a, Is.EqualTo(128f / 255f).Within(0.001f));
        }

        [Test]
        public void Given_TextNamedColorClass_When_Parsed_Then_ResolvesTextColor()
        {
            // Act — color names are accepted in addition to hex values
            var ok = StyleArbitraryValueResolver.TryParse("text-[red]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Color), Is.EqualTo((ArbitraryProperty.TextColor, Color.red)));
        }

        [Test]
        public void Given_TextMalformedHexValue_When_Parsed_Then_Declines()
        {
            // Act — malformed hex is neither a color nor a length, so the class produces no inline style
            var ok = StyleArbitraryValueResolver.TryParse("text-[#zzz]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        #endregion

        #region TryParse — Negative Values

        [Test]
        public void Given_NegativeMarginTopClass_When_Parsed_Then_ResolvesNegativePixelLength()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("-mt-[20px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value, s.Unit),
                Is.EqualTo((ArbitraryProperty.MarginTop, -20f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_NegativeMarginLeftClass_When_Parsed_Then_ResolvesNegativePercentLength()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("-ml-[50%]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value, s.Unit),
                Is.EqualTo((ArbitraryProperty.MarginLeft, -50f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_NegativeLeftClass_When_Parsed_Then_ResolvesNegativePercentLength()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("-left-[50%]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value, s.Unit),
                Is.EqualTo((ArbitraryProperty.Left, -50f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_NegativeTopClass_When_Parsed_Then_ResolvesNegativeValue()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("-top-[10px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.Top, -10f)));
        }

        #endregion

        #region TryParse — Invalid Inputs

        [Test]
        public void Given_NormalBemClass_When_Parsed_Then_Declines()
        {
            // Act + Assert
            Assert.That(StyleArbitraryValueResolver.TryParse("btn--primary", out _), Is.False);
        }

        [Test]
        public void Given_NullClass_When_Parsed_Then_Declines()
        {
            // Act + Assert
            Assert.That(StyleArbitraryValueResolver.TryParse(null, out _), Is.False);
        }

        [Test]
        public void Given_UnknownPrefix_When_Parsed_Then_Declines()
        {
            // Act + Assert
            Assert.That(StyleArbitraryValueResolver.TryParse("z-[10]", out _), Is.False);
        }

        [Test]
        public void Given_EmptyBrackets_When_Parsed_Then_Declines()
        {
            // Act + Assert
            Assert.That(StyleArbitraryValueResolver.TryParse("h-[]", out _), Is.False);
        }

        [Test]
        public void Given_NonNumericValue_When_Parsed_Then_Declines()
        {
            // Act + Assert
            Assert.That(StyleArbitraryValueResolver.TryParse("h-[abc]", out _), Is.False);
        }

        [Test]
        public void Given_MissingClosingBracket_When_Parsed_Then_Declines()
        {
            // Act + Assert
            Assert.That(StyleArbitraryValueResolver.TryParse("h-[15%", out _), Is.False);
        }

        [Test]
        public void Given_NaNValue_When_Parsed_Then_Declines()
        {
            // Act + Assert
            Assert.That(StyleArbitraryValueResolver.TryParse("h-[NaN]", out _), Is.False);
        }

        [Test]
        public void Given_InfinityValue_When_Parsed_Then_Declines()
        {
            // Act + Assert
            Assert.That(StyleArbitraryValueResolver.TryParse("w-[Infinity]", out _), Is.False);
        }

        [Test]
        public void Given_NegativeInfinityPercentValue_When_Parsed_Then_Declines()
        {
            // Act + Assert
            Assert.That(StyleArbitraryValueResolver.TryParse("h-[-Infinity%]", out _), Is.False);
        }

        #endregion

        #region Apply / Clear — Size

        [Test]
        public void Given_HeightStyle_When_Applied_Then_SetsInlineHeight()
        {
            // Arrange
            var el = new VisualElement();
            var style = new ArbitraryStyle(ArbitraryProperty.Height, 15f, LengthUnit.Percent);

            // Act
            StyleArbitraryValueResolver.Apply(el, in style);

            // Assert
            Assert.That((el.style.height.value.value, el.style.height.value.unit),
                Is.EqualTo((15f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_MinHeightStyle_When_Applied_Then_SetsInlineMinHeight()
        {
            // Arrange
            var el = new VisualElement();
            var style = new ArbitraryStyle(ArbitraryProperty.MinHeight, 60f, LengthUnit.Pixel);

            // Act
            StyleArbitraryValueResolver.Apply(el, in style);

            // Assert
            Assert.That((el.style.minHeight.value.value, el.style.minHeight.value.unit),
                Is.EqualTo((60f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_HeightWithInlineValue_When_Cleared_Then_RevertsToNull()
        {
            // Arrange
            var el = new VisualElement();
            el.style.height = new Length(100, LengthUnit.Pixel);

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.Height);

            // Assert
            Assert.That(el.style.height.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        #endregion

        #region Apply / Clear — Position

        [Test]
        public void Given_TopStyle_When_Applied_Then_SetsInlineTop()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Top, 15f, LengthUnit.Percent));

            // Assert
            Assert.That((el.style.top.value.value, el.style.top.value.unit),
                Is.EqualTo((15f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_InsetShorthand_When_Applied_Then_SetsAllFourEdges()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Inset, 10f, LengthUnit.Pixel));

            // Assert
            Assert.That(
                (el.style.top.value.value, el.style.right.value.value,
                    el.style.bottom.value.value, el.style.left.value.value),
                Is.EqualTo((10f, 10f, 10f, 10f)));
        }

        [Test]
        public void Given_InsetXShorthand_When_Applied_Then_SetsLeftAndRight()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.InsetX, 20f, LengthUnit.Pixel));

            // Assert
            Assert.That((el.style.left.value.value, el.style.right.value.value), Is.EqualTo((20f, 20f)));
        }

        [Test]
        public void Given_AllFourEdgesSet_When_InsetCleared_Then_RevertsAllFourToNull()
        {
            // Arrange
            var el = new VisualElement();
            el.style.top = new Length(10, LengthUnit.Pixel);
            el.style.right = new Length(10, LengthUnit.Pixel);
            el.style.bottom = new Length(10, LengthUnit.Pixel);
            el.style.left = new Length(10, LengthUnit.Pixel);

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.Inset);

            // Assert
            Assert.That(
                (el.style.top.keyword, el.style.right.keyword,
                    el.style.bottom.keyword, el.style.left.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null, StyleKeyword.Null, StyleKeyword.Null)));
        }

        #endregion

        #region Apply / Clear — Padding

        [Test]
        public void Given_PaddingShorthand_When_Applied_Then_SetsAllFourEdges()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Padding, 24f, LengthUnit.Pixel));

            // Assert
            Assert.That(
                (el.style.paddingTop.value.value, el.style.paddingRight.value.value,
                    el.style.paddingBottom.value.value, el.style.paddingLeft.value.value),
                Is.EqualTo((24f, 24f, 24f, 24f)));
        }

        [Test]
        public void Given_PaddingXShorthand_When_Applied_Then_SetsLeftAndRight()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.PaddingX, 32f, LengthUnit.Pixel));

            // Assert
            Assert.That((el.style.paddingLeft.value.value, el.style.paddingRight.value.value),
                Is.EqualTo((32f, 32f)));
        }

        [Test]
        public void Given_PaddingYShorthand_When_Applied_Then_SetsTopAndBottom()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.PaddingY, 24f, LengthUnit.Pixel));

            // Assert
            Assert.That((el.style.paddingTop.value.value, el.style.paddingBottom.value.value),
                Is.EqualTo((24f, 24f)));
        }

        [Test]
        public void Given_PaddingTopStyle_When_Applied_Then_SetsInlinePaddingTop()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.PaddingTop, 26f, LengthUnit.Pixel));

            // Assert
            Assert.That(el.style.paddingTop.value.value, Is.EqualTo(26f));
        }

        [Test]
        public void Given_AllFourEdgesSet_When_PaddingCleared_Then_RevertsAllFourToNull()
        {
            // Arrange
            var el = new VisualElement();
            el.style.paddingTop = new Length(24, LengthUnit.Pixel);
            el.style.paddingRight = new Length(24, LengthUnit.Pixel);
            el.style.paddingBottom = new Length(24, LengthUnit.Pixel);
            el.style.paddingLeft = new Length(24, LengthUnit.Pixel);

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.Padding);

            // Assert
            Assert.That(
                (el.style.paddingTop.keyword, el.style.paddingRight.keyword,
                    el.style.paddingBottom.keyword, el.style.paddingLeft.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null, StyleKeyword.Null, StyleKeyword.Null)));
        }

        #endregion

        #region Apply / Clear — Margin

        [Test]
        public void Given_MarginShorthand_When_Applied_Then_SetsAllFourEdges()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Margin, 10f, LengthUnit.Pixel));

            // Assert
            Assert.That(
                (el.style.marginTop.value.value, el.style.marginRight.value.value,
                    el.style.marginBottom.value.value, el.style.marginLeft.value.value),
                Is.EqualTo((10f, 10f, 10f, 10f)));
        }

        [Test]
        public void Given_MarginYShorthand_When_Applied_Then_SetsTopAndBottom()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.MarginY, 50f, LengthUnit.Pixel));

            // Assert
            Assert.That((el.style.marginTop.value.value, el.style.marginBottom.value.value),
                Is.EqualTo((50f, 50f)));
        }

        [Test]
        public void Given_NegativeMarginTopStyle_When_Applied_Then_SetsNegativeInlineValue()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.MarginTop, -20f, LengthUnit.Pixel));

            // Assert
            Assert.That(el.style.marginTop.value.value, Is.EqualTo(-20f));
        }

        [Test]
        public void Given_MarginTopWithInlineValue_When_Cleared_Then_RevertsToNull()
        {
            // Arrange
            var el = new VisualElement();
            el.style.marginTop = new Length(-20, LengthUnit.Pixel);

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.MarginTop);

            // Assert
            Assert.That(el.style.marginTop.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        #endregion

        #region Apply / Clear — BorderRadius

        [Test]
        public void Given_BorderRadiusStyle_When_Applied_Then_SetsAllFourCorners()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.BorderRadius, 36f, LengthUnit.Pixel));

            // Assert
            Assert.That(
                (el.style.borderTopLeftRadius.value.value, el.style.borderTopRightRadius.value.value,
                    el.style.borderBottomLeftRadius.value.value, el.style.borderBottomRightRadius.value.value),
                Is.EqualTo((36f, 36f, 36f, 36f)));
        }

        [Test]
        public void Given_AllFourCornersSet_When_BorderRadiusCleared_Then_RevertsAllFourToNull()
        {
            // Arrange
            var el = new VisualElement();
            el.style.borderTopLeftRadius = new Length(36, LengthUnit.Pixel);
            el.style.borderTopRightRadius = new Length(36, LengthUnit.Pixel);
            el.style.borderBottomLeftRadius = new Length(36, LengthUnit.Pixel);
            el.style.borderBottomRightRadius = new Length(36, LengthUnit.Pixel);

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.BorderRadius);

            // Assert
            Assert.That(
                (el.style.borderTopLeftRadius.keyword, el.style.borderTopRightRadius.keyword,
                    el.style.borderBottomLeftRadius.keyword, el.style.borderBottomRightRadius.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null, StyleKeyword.Null, StyleKeyword.Null)));
        }

        #endregion

        #region Apply / Clear — FontSize

        [Test]
        public void Given_FontSizeStyle_When_Applied_Then_SetsInlineFontSize()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.FontSize, 18f, LengthUnit.Pixel));

            // Assert
            Assert.That(el.style.fontSize.value.value, Is.EqualTo(18f));
        }

        [Test]
        public void Given_FontSizeWithInlineValue_When_Cleared_Then_RevertsToNull()
        {
            // Arrange
            var el = new VisualElement();
            el.style.fontSize = new Length(18, LengthUnit.Pixel);

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.FontSize);

            // Assert
            Assert.That(el.style.fontSize.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        #endregion

        #region Apply / Clear — Color

        [Test]
        public void Given_TextColorStyle_When_Applied_Then_SetsInlineColor()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.TextColor, Color.red));

            // Assert
            Assert.That(el.style.color.value, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_BackgroundColorStyle_When_Applied_Then_SetsInlineBackgroundColor()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.BackgroundColor, Color.green));

            // Assert
            Assert.That(el.style.backgroundColor.value, Is.EqualTo(Color.green));
        }

        [Test]
        public void Given_ColorWithInlineValue_When_TextColorCleared_Then_RevertsToNull()
        {
            // Arrange
            var el = new VisualElement();
            el.style.color = Color.red;

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.TextColor);

            // Assert
            Assert.That(el.style.color.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        #endregion

        #region Shorthand + Individual Priority

        [Test]
        public void Given_PaddingXThenPaddingLeft_When_Applied_Then_IndividualEdgeOverridesShorthand()
        {
            // Arrange — px-[24px] sets left and right to 24
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.PaddingX, 24f, LengthUnit.Pixel));

            // Act — pl-[16px] overrides only the left edge
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.PaddingLeft, 16f, LengthUnit.Pixel));

            // Assert
            Assert.That((el.style.paddingLeft.value.value, el.style.paddingRight.value.value),
                Is.EqualTo((16f, 24f)));
        }

        [TestCase(StyleLayerPriority.Base)]
        [TestCase(StyleLayerPriority.Important)]
        public void Given_ALonghandBeforeAShorthandOnOneLayer_When_Applied_Then_TheLaterShorthandHoldsEveryEdge(long priority)
        {
            // Arrange
            var element = new VisualElement();
            StyleArbitraryValueResolver.Apply(element,
                new ArbitraryStyle(ArbitraryProperty.PaddingTop, 2f, LengthUnit.Pixel), priority);
            var before = element.style.paddingTop.value.value;

            // Act
            StyleArbitraryValueResolver.Apply(element,
                new ArbitraryStyle(ArbitraryProperty.Padding, 8f, LengthUnit.Pixel), priority);

            // Assert
            Assert.That((before, element.style.paddingTop.value.value, element.style.paddingBottom.value.value),
                Is.EqualTo((2f, 8f, 8f)));
        }

        [TestCase(false, false, "leading")]
        [TestCase(false, true, "leading")]
        [TestCase(true, false, "leading")]
        [TestCase(true, true, "leading")]
        [TestCase(false, false, "crossing")]
        [TestCase(false, true, "crossing")]
        [TestCase(true, false, "crossing")]
        [TestCase(true, true, "crossing")]
        [TestCase(false, false, "duplicate-width")]
        [TestCase(false, true, "duplicate-width")]
        [TestCase(true, false, "duplicate-width")]
        [TestCase(true, true, "duplicate-width")]
        [TestCase(false, false, "remove-occurrence")]
        [TestCase(false, true, "remove-occurrence")]
        [TestCase(true, false, "remove-occurrence")]
        [TestCase(true, true, "remove-occurrence")]
        [TestCase(false, false, "append-occurrence")]
        [TestCase(false, true, "append-occurrence")]
        [TestCase(true, false, "append-occurrence")]
        [TestCase(true, true, "append-occurrence")]
        [TestCase(false, false, "corner-chain")]
        [TestCase(false, true, "corner-chain")]
        [TestCase(true, false, "corner-chain")]
        [TestCase(true, true, "corner-chain")]
        [TestCase(false, false, "wrap-new-inline")]
        [TestCase(false, true, "wrap-new-inline")]
        [TestCase(true, false, "wrap-new-inline")]
        [TestCase(true, true, "wrap-new-inline")]
        public void Given_InlineClassOccurrences_When_TheirOrderOrCountChanges_Then_TheLastWritersHoldTheirSlots(bool important, bool largeList, string change)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var bang = important ? "!" : "";
            var extra = largeList ? " flex relative items-start justify-start overflow-hidden opacity-50 bg-red-500" : "";
            var padding = bang + "p-[8px]";
            var top = bang + "pt-[2px]";
            var (oldClasses, newClasses, oldValue, newValue) = change switch
            {
                "leading" => ($"w-[20px] {padding} {top}", $"w-[20px] {top} {padding}", 2f, 8f),
                "crossing" => ($"{padding} {top} w-[20px]", $"{top} w-[20px] {padding}", 2f, 8f),
                "duplicate-width" => ($"w-[20px] w-[20px] {padding} {top}", $"w-[20px] w-[20px] {top} {padding}", 2f, 8f),
                "remove-occurrence" => ($"w-[20px] {padding} {top} {padding}", $"w-[20px] {padding} {top}", 8f, 2f),
                "append-occurrence" => ($"w-[20px] {padding} {top}", $"w-[20px] {padding} {top} {padding}", 2f, 8f),
                "corner-chain" => ($"w-[20px] {bang}rounded-r-[6px] {bang}rounded-b-[8px] {bang}rounded-t-[4px]",
                    $"w-[20px] {bang}rounded-r-[6px] {bang}rounded-b-[8px] {bang}rounded-t-[4px] {bang}rounded-tl-[2px]", 8f, 8f),
                "wrap-new-inline" => ("relative", $"relative {padding} {top}", 0f, 2f),
                _ => throw new System.ArgumentOutOfRangeException(nameof(change)),
            };
            var oldTree = new VNode[] { V.Div(oldClasses + extra) };
            var newTree = new VNode[] { V.Div(newClasses + extra) };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);
            var historyConsistent = true;
            if (change == "wrap-new-inline")
            {
                var target = root.ElementAt(0);
                StyleArbitraryValueResolver.Apply(target, new ArbitraryStyle(ArbitraryProperty.Width, 1f, LengthUnit.Pixel));
                StyleArbitraryValueResolver.Clear(target, ArbitraryProperty.Width);
                var layers = typeof(StyleArbitraryValueResolver).GetField("s_layers",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
                var mapArgs = new object[] { target, null! };
                layers.GetType().GetMethod("TryGetValue")!.Invoke(layers, mapArgs);
                var arrivals = (Dictionary<(ArbitraryProperty, long), long>)mapArgs[1].GetType().GetField("_arrivals",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(mapArgs[1])!;
                historyConsistent = arrivals.Count == 0;
                // Compress an empty map's Apply/Clear history; the compared arrivals are generated by Reconcile.
                mapArgs[1].GetType().GetField("_nextArrival",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(mapArgs[1], long.MaxValue - 1);
            }
            var corners = change == "corner-chain";
            var before = corners
                ? root.ElementAt(0).style.borderBottomRightRadius.value.value
                : root.ElementAt(0).style.paddingTop.value.value;
            root.ElementAt(0).style.width = 99f;

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            var element = root.ElementAt(0);
            var after = corners ? element.style.borderBottomRightRadius.value.value : element.style.paddingTop.value.value;
            var companion = corners ? element.style.borderTopLeftRadius.value.value : element.style.paddingBottom.value.value;
            Assert.That((before, after, companion, element.style.width.value.value, historyConsistent),
                Is.EqualTo((oldValue, newValue, corners ? 2f : 8f, 99f, true)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_ASurvivingInlineShorthand_When_ALonghandIsInsertedBeforeIt_Then_TheSurvivingShorthandStillHoldsTheTop(bool important)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var bang = important ? "!" : "";
            var oldTree = new VNode[] { V.Div($"{bang}p-[8px]") };
            var newTree = new VNode[] { V.Div($"{bang}pt-[2px] {bang}p-[8px]") };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);
            var before = root.ElementAt(0).style.paddingTop.value.value;

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That((before, root.ElementAt(0).style.paddingTop.value.value), Is.EqualTo((8f, 8f)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_EqualLayerInlineVariantPayloads_When_Enabled_Then_TheLaterShorthandHoldsTheTop(bool important)
        {
            // Arrange
            var element = new VisualElement();
            var bang = important ? "!" : "";
            var payloads = new[] { bang + "pt-[2px]", bang + "p-[8px]" };

            // Act
            StyleVariantPayload.Apply(element, payloads, true, StyleLayerPriority.Hover);
            var settled = element.style.paddingTop.value.value;
            var priority = important ? StyleLayerPriority.ImportantOf(StyleLayerPriority.Hover) : StyleLayerPriority.Hover;
            StyleArbitraryValueResolver.Clear(element, ArbitraryProperty.Padding, priority);

            // Assert
            Assert.That((settled, element.style.paddingTop.value.value), Is.EqualTo((8f, 2f)));
        }

        [Test]
        public void Given_AnOlderBaseLonghandAndLaterBaseShorthand_When_AnUpperLonghandClears_Then_TheBaseArrivalOrderIsRestored()
        {
            // Arrange
            var element = new VisualElement();
            StyleArbitraryValueResolver.Apply(element,
                new ArbitraryStyle(ArbitraryProperty.PaddingTop, 2f, LengthUnit.Pixel));
            StyleArbitraryValueResolver.Apply(element,
                new ArbitraryStyle(ArbitraryProperty.Padding, 8f, LengthUnit.Pixel));
            StyleArbitraryValueResolver.Apply(element,
                new ArbitraryStyle(ArbitraryProperty.PaddingTop, 4f, LengthUnit.Pixel), StyleLayerPriority.Hover);
            StyleArbitraryValueResolver.Apply(element,
                new ArbitraryStyle(ArbitraryProperty.Width, 5f, LengthUnit.Pixel));
            var layerTable = typeof(StyleArbitraryValueResolver).GetField("s_layers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            var mapArgs = new object[] { element, null! };
            layerTable.GetType().GetMethod("TryGetValue")!.Invoke(layerTable, mapArgs);
            var arrivals = (Dictionary<(ArbitraryProperty, long), long>)mapArgs[1].GetType().GetField("_arrivals",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(mapArgs[1])!;
            var target = (ArbitraryProperty.PaddingTop, StyleLayerPriority.Hover);
            var survivor = (ArbitraryProperty.Width, StyleLayerPriority.Base);
            var oldArrival = arrivals[target];
            var survivingArrival = arrivals[survivor];
            var upper = element.style.paddingTop.value.value;

            // Act
            StyleArbitraryValueResolver.Clear(element, ArbitraryProperty.PaddingTop, StyleLayerPriority.Hover);
            var restored = element.style.paddingTop.value.value;
            var released = !arrivals.ContainsKey(target);
            var survivorKept = arrivals[survivor] == survivingArrival;
            StyleArbitraryValueResolver.Apply(element,
                new ArbitraryStyle(ArbitraryProperty.PaddingTop, 6f, LengthUnit.Pixel), StyleLayerPriority.Hover);
            var registered = element.style.paddingTop.value.value;
            var freshArrival = arrivals[target] > oldArrival;
            StyleArbitraryValueResolver.Clear(element, ArbitraryProperty.PaddingTop, StyleLayerPriority.Hover);

            // Assert
            Assert.That((upper, restored, released && !arrivals.ContainsKey(target),
                    survivorKept && arrivals[survivor] == survivingArrival && element.style.width.value.value == 5f,
                    registered, freshArrival, element.style.paddingTop.value.value),
                Is.EqualTo((4f, 8f, true, true, 6f, true, 8f)));
        }

        // GREEN_ON_BASE(characterization): adding an unrelated class already leaves an unchanged inline token alone.
        [TestCase("bg-red-500", 0f)]
        [TestCase("h-[5px]", 5f)]
        [TestCase("reorder-filter", 0f)]
        public void Given_AnUnchangedInlineTokenWithAForeignWrite_When_AnUnrelatedClassIsAdded_Then_TheForeignWriteIsPreserved(string addedClass, float height)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var padding = addedClass == "reorder-filter";
            var oldTree = new VNode[] { V.Div(padding ? "blur-[2px] p-[8px] pt-[2px] p-[8px]" : "w-[20px]") };
            var newTree = new VNode[] { V.Div(padding ? "relative p-[8px] blur-[2px] pt-[2px] p-[8px]" : "w-[20px] " + addedClass) };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);
            var element = root.ElementAt(0);
            var initial = padding ? element.style.paddingTop.value.value : element.style.width.value.value;
            if (padding)
            {
                element.style.paddingTop = 99f;
            }
            else
            {
                element.style.width = 99f;
            }
            var before = padding ? element.style.paddingTop.value.value : element.style.width.value.value;

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            var after = padding ? element.style.paddingTop.value.value : element.style.width.value.value;
            Assert.That((initial, before, after, element.style.height.value.value),
                Is.EqualTo((padding ? 8f : 20f, 99f, 99f, height)));
        }

        // GREEN_ON_BASE(characterization): active hover payloads already keep their rule priority after a class reorder.
        [TestCase(false)]
        [TestCase(true)]
        public void Given_ActiveOverlappingHoverPayloads_When_TheirClassesAreReordered_Then_TheLonghandKeepsItsRulePriority(bool important)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var bang = important ? "!" : "";
            var oldTree = new VNode[] { V.Div($"p-[0px] hover:{bang}p-[8px] hover:{bang}pt-[2px]") };
            var newTree = new VNode[] { V.Div($"p-[0px] hover:{bang}pt-[2px] hover:{bang}p-[8px]") };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);
            var leaf = root.ElementAt(0);
            using (var evt = PointerOverEvent.GetPooled())
                Velvet.TestUtilities.VisualElementTestExtensions.SimulateEvent(leaf, evt);
            var before = leaf.style.paddingTop.value.value;

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            var manipulator = reconciler.Context.VariantManipulators[root.ElementAt(0)];
            var payloads = (string[])typeof(StyleVariantManipulator).GetField("_hover",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(manipulator)!;

            // Assert
            Assert.That((before, root.ElementAt(0).style.paddingTop.value.value, string.Join(" ", payloads)),
                Is.EqualTo((2f, 2f, $"{bang}pt-[2px] {bang}p-[8px]")));
        }

        #endregion

        #region Reconciler Integration

        [Test]
        public void Given_ArbitraryClassAdded_When_Reconciled_Then_AppliesInlineStyle()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div() };
            var newTree = new VNode[] { V.Div("h-[15%]") };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            var el = root.ElementAt(0);
            Assert.That((el.style.height.value.value, el.style.height.value.unit),
                Is.EqualTo((15f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_ArbitraryClassAdded_When_Reconciled_Then_DoesNotEnterClassList()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div() };
            var newTree = new VNode[] { V.Div("h-[15%]") };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That(root.ElementAt(0).ClassListContains("h-[15%]"), Is.False);
        }

        [Test]
        public void Given_PositionFractionClassAdded_When_Reconciled_Then_AppliesInlinePercentLeft()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div() };
            var newTree = new VNode[] { V.Div("left-1/2") };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            var el = root.ElementAt(0);
            Assert.That((el.style.left.value.value, el.style.left.value.unit),
                Is.EqualTo((50f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_PositionFractionClass_When_Mounted_Then_DoesNotEnterClassList()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("-top-1/2") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(root.ElementAt(0).ClassListContains("-top-1/2"), Is.False);
        }

        [Test]
        public void Given_ArbitraryClassRemoved_When_Reconciled_Then_ClearsInlineStyle()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("h-[15%]") };
            var newTree = new VNode[] { V.Div() };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That(root.ElementAt(0).style.height.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ArbitraryColorClassRemoved_When_Reconciled_Then_ClearsInlineColor()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("text-[#ff0000]") };
            var newTree = new VNode[] { V.Div() };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);
            Assume.That(root.ElementAt(0).style.color.value, Is.EqualTo(Color.red),
                "Precondition: the color class applied the inline color");

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That(root.ElementAt(0).style.color.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_MixedClassesAndArbitraryValues_When_Reconciled_Then_RegularClassesEnterClassList()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("absolute top-0 h-[15%] min-h-[60px]") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            var el = root.ElementAt(0);
            Assert.That((el.ClassListContains("absolute"), el.ClassListContains("top-0")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_MixedClassesAndArbitraryValues_When_Reconciled_Then_ArbitraryValuesApplyInlineStyle()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("absolute top-0 h-[15%] min-h-[60px]") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            var el = root.ElementAt(0);
            Assert.That((el.style.height.value.value, el.style.minHeight.value.value),
                Is.EqualTo((15f, 60f)));
        }

        [Test]
        public void Given_DuplicateSamePropertyClass_When_OneRemoved_Then_SurvivingValueIsPreserved()
        {
            // Arrange — both h-[10%] and h-[20%] are present (last wins); removing h-[10%] re-applies h-[20%]
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("h-[10%] h-[20%]") };
            var newTree = new VNode[] { V.Div("h-[20%]") };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            var el = root.ElementAt(0);
            Assert.That((el.style.height.value.value, el.style.height.value.unit),
                Is.EqualTo((20f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_ArbitraryValueChanged_When_Reconciled_Then_UpdatesInlineStyle()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("h-[15%]") };
            var newTree = new VNode[] { V.Div("h-[20%]") };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            var el = root.ElementAt(0);
            Assert.That((el.style.height.value.value, el.style.height.value.unit),
                Is.EqualTo((20f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_HashSetDiffPath_When_DuplicateSamePropertyClassRemoved_Then_SurvivingValueIsPreserved()
        {
            // Arrange — 9+ classes select the hash-set diff path (linear threshold is 8)
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("a b c d e f g h h-[10%] h-[20%]") };
            var newTree = new VNode[] { V.Div("a b c d e f g h h-[20%]") };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            var el = root.ElementAt(0);
            Assert.That((el.style.height.value.value, el.style.height.value.unit),
                Is.EqualTo((20f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_NegativeArbitraryValues_When_Reconciled_Then_AppliesNegativeInlineStyles()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("absolute top-[50%] left-[50%] -mt-[20px] -ml-[50%]") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            var el = root.ElementAt(0);
            Assert.That(
                (el.style.top.value.value, el.style.marginTop.value.value,
                    el.style.marginLeft.value.value, el.style.marginLeft.value.unit),
                Is.EqualTo((50f, -20f, -50f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_BorderRadiusClass_When_Reconciled_Then_AppliesAllFourCorners()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("rounded-[36px]") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            var el = root.ElementAt(0);
            Assert.That(
                (el.style.borderTopLeftRadius.value.value, el.style.borderTopRightRadius.value.value,
                    el.style.borderBottomLeftRadius.value.value, el.style.borderBottomRightRadius.value.value),
                Is.EqualTo((36f, 36f, 36f, 36f)));
        }

        [Test]
        public void Given_PaddingShorthandClasses_When_Reconciled_Then_AppliesAllFourSides()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("py-[24px] px-[32px]") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            var el = root.ElementAt(0);
            Assert.That(
                (el.style.paddingTop.value.value, el.style.paddingBottom.value.value,
                    el.style.paddingLeft.value.value, el.style.paddingRight.value.value),
                Is.EqualTo((24f, 24f, 32f, 32f)));
        }

        [Test]
        public void Given_FontSizeClass_When_Reconciled_Then_AppliesInlineFontSize()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("text-[18px]") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(root.ElementAt(0).style.fontSize.value.value, Is.EqualTo(18f));
        }

        [Test]
        public void Given_TextColorClass_When_Reconciled_Then_AppliesInlineColor()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("text-[#ff0000]") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(root.ElementAt(0).style.color.value, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_BackgroundColorClass_When_Reconciled_Then_AppliesInlineBackgroundColor()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("bg-[#00ff00]") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(root.ElementAt(0).style.backgroundColor.value, Is.EqualTo(Color.green));
        }

        #endregion

        #region Border / Letter-spacing extensions

        [Test]
        public void Given_BorderWidthClass_When_Parsed_Then_ResolvesBorderWidthAsPixel()
        {
            var ok = StyleArbitraryValueResolver.TryParse("border-[3px]", out var s);

            Assume.That(ok, Is.True);
            Assert.That((s.Property, s.Value, s.Unit),
                Is.EqualTo((ArbitraryProperty.BorderWidth, 3f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_PerSideBorderWidthClass_When_Parsed_Then_ResolvesThatSide()
        {
            var ok = StyleArbitraryValueResolver.TryParse("border-t-[2px]", out var s);

            Assume.That(ok, Is.True);
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.BorderTopWidth, 2f)));
        }

        [Test]
        public void Given_BorderColorClass_When_Parsed_Then_ResolvesBorderColor()
        {
            var ok = StyleArbitraryValueResolver.TryParse("border-[#00ff00]", out var s);

            Assume.That(ok, Is.True);
            Assert.That((s.Property, s.Color), Is.EqualTo((ArbitraryProperty.BorderColor, Color.green)));
        }

        [Test]
        public void Given_PerCornerRadiusClass_When_Parsed_Then_ResolvesThatCorner()
        {
            var ok = StyleArbitraryValueResolver.TryParse("rounded-tl-[8px]", out var s);

            Assume.That(ok, Is.True);
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.BorderTopLeftRadius, 8f)));
        }

        [Test]
        public void Given_TrackingClass_When_Parsed_Then_ResolvesLetterSpacing()
        {
            var ok = StyleArbitraryValueResolver.TryParse("tracking-[2px]", out var s);

            Assume.That(ok, Is.True);
            Assert.That((s.Property, s.Value, s.Unit),
                Is.EqualTo((ArbitraryProperty.LetterSpacing, 2f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_BorderWidthClass_When_Applied_Then_SetsAllFourBorderWidths()
        {
            var element = new VisualElement();

            StyleArbitraryValueResolver.TryParse("border-[3px]", out var s);
            StyleArbitraryValueResolver.Apply(element, in s);

            Assert.That(
                (element.style.borderTopWidth.value, element.style.borderRightWidth.value,
                 element.style.borderBottomWidth.value, element.style.borderLeftWidth.value),
                Is.EqualTo((3f, 3f, 3f, 3f)));
        }

        [Test]
        public void Given_BorderColorClass_When_Applied_Then_SetsAllFourBorderColors()
        {
            var element = new VisualElement();

            StyleArbitraryValueResolver.TryParse("border-[#00ff00]", out var s);
            StyleArbitraryValueResolver.Apply(element, in s);

            Assert.That(
                (element.style.borderTopColor.value, element.style.borderRightColor.value,
                 element.style.borderBottomColor.value, element.style.borderLeftColor.value),
                Is.EqualTo((Color.green, Color.green, Color.green, Color.green)));
        }

        #endregion

        #region Transform extensions (scale / translate / rotate)

        [Test]
        public void Given_ScaleArbitraryClass_When_Parsed_Then_ResolvesScaleWithUnitlessValue()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("scale-[1.4]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.Scale, 1.4f)));
        }

        [Test]
        public void Given_ScaleArbitraryStyle_When_Applied_Then_SetsUniformInlineScale()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("scale-[1.4]", out var s);

            // Act
            StyleArbitraryValueResolver.Apply(el, in s);

            // Assert
            Assert.That((el.style.scale.value.value.x, el.style.scale.value.value.y),
                Is.EqualTo((1.4f, 1.4f)));
        }

        [Test]
        public void Given_NonNumericScaleValue_When_Parsed_Then_Declines()
        {
            // Act + Assert
            Assert.That(StyleArbitraryValueResolver.TryParse("scale-[abc]", out _), Is.False);
        }

        [Test]
        public void Given_TranslateXArbitraryClass_When_Parsed_Then_ResolvesTranslateXAsPixelLength()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("translate-x-[4px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value, s.Unit),
                Is.EqualTo((ArbitraryProperty.TranslateX, 4f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_TranslateYArbitraryClass_When_Parsed_Then_ResolvesTranslateYWithValue()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("translate-y-[8px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.TranslateY, 8f)));
        }

        [Test]
        public void Given_TranslateXArbitraryStyle_When_Applied_Then_SetsInlineTranslateXAxis()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("translate-x-[4px]", out var s);

            // Act
            StyleArbitraryValueResolver.Apply(el, in s);

            // Assert
            Assert.That(el.style.translate.value.x.value, Is.EqualTo(4f));
        }

        [Test]
        public void Given_TranslateXThenTranslateY_When_Applied_Then_BothAxesCompose()
        {
            // Arrange — apply x then y; the y application must preserve the previously set x.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("translate-x-[4px]", out var sx);
            StyleArbitraryValueResolver.TryParse("translate-y-[8px]", out var sy);
            StyleArbitraryValueResolver.Apply(el, in sx);

            // Act
            StyleArbitraryValueResolver.Apply(el, in sy);

            // Assert
            Assert.That((el.style.translate.value.x.value, el.style.translate.value.y.value),
                Is.EqualTo((4f, 8f)));
        }

        [Test]
        public void Given_NegativeRotateArbitraryClass_When_Parsed_Then_ResolvesNegativeDegrees()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("-rotate-[45deg]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.Rotate, -45f)));
        }

        [Test]
        public void Given_RotateRadiansClass_When_Parsed_Then_NormalizesToDegrees()
        {
            // Act — half a turn in radians is 180 degrees.
            var ok = StyleArbitraryValueResolver.TryParse("rotate-[3.14159265rad]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That(s.Value, Is.EqualTo(180f).Within(0.01f));
        }

        [Test]
        public void Given_RotateTurnClass_When_Parsed_Then_NormalizesToDegrees()
        {
            // Act — a quarter turn is 90 degrees.
            var ok = StyleArbitraryValueResolver.TryParse("rotate-[0.25turn]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That(s.Value, Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void Given_ScaleWithALengthSuffix_When_Parsed_Then_Declines()
        {
            // Act — the length grammar would convert the suffix and hand back 32, a scale of thirty-two.
            // The float grammar rejects it, so the class falls through unrecognised instead.
            var ok = StyleArbitraryValueResolver.TryParse("scale-[2rem]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_TransformOriginPairClass_When_Parsed_Then_BothComponentsSurvive()
        {
            // Act — the underscore is how the bracket grammar spells the space CSS puts between them.
            var ok = StyleArbitraryValueResolver.TryParse("origin-[33%_75%]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value, s.Unit, s.Value2, s.Unit2),
                Is.EqualTo((ArbitraryProperty.TransformOrigin, 33f, LengthUnit.Percent, 75f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_TransformOriginSingleComponentClass_When_Parsed_Then_TheSecondIsHalfAndNotTheFirst()
        {
            // Act — CSS leaves an unstated y at 50%, so origin-[0px] is the left edge's middle rather than
            // the top-left corner. Copying the x across would make those two the same pivot.
            var ok = StyleArbitraryValueResolver.TryParse("origin-[0px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Value, s.Unit, s.Value2, s.Unit2),
                Is.EqualTo((0f, LengthUnit.Pixel, 50f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_TransformOriginMixedUnits_When_Parsed_Then_EachComponentKeepsItsOwnUnit()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[12px_80%]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Unit, s.Unit2), Is.EqualTo((LengthUnit.Pixel, LengthUnit.Percent)));
        }

        // CSS transform-origin's grammar inside the brackets: a keyword stands for its percentage, a lone
        // vertical keyword is the y, a keyword-only pair may come in either order, and a pair holding a
        // length is x then y. Each pivot below differs on both axes, so a swapped reading cannot pass.

        [Test]
        public void Given_TransformOriginLoneHorizontalKeyword_When_Parsed_Then_ItIsTheXAndTheYIsHalf()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[right]", out var s);

            // Assert
            Assert.That((ok, s.Value, s.Unit, s.Value2, s.Unit2),
                Is.EqualTo((true, 100f, LengthUnit.Percent, 50f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_TransformOriginLoneVerticalKeyword_When_Parsed_Then_ItIsTheYAndTheXIsHalf()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[bottom]", out var s);

            // Assert
            Assert.That((ok, s.Value, s.Unit, s.Value2, s.Unit2),
                Is.EqualTo((true, 50f, LengthUnit.Percent, 100f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_TransformOriginKeywordPairWrittenYFirst_When_Parsed_Then_EachKeywordTakesItsOwnAxis()
        {
            // Act — CSS accepts a keyword-only pair in either order.
            var ok = StyleArbitraryValueResolver.TryParse("origin-[bottom_left]", out var s);

            // Assert
            Assert.That((ok, s.Value, s.Value2), Is.EqualTo((true, 0f, 100f)));
        }

        [Test]
        public void Given_TransformOriginCenterThenTop_When_Parsed_Then_TheXIsHalfAndTheYIsZero()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[center_top]", out var s);

            // Assert
            Assert.That((ok, s.Value, s.Value2), Is.EqualTo((true, 50f, 0f)));
        }

        [Test]
        public void Given_TransformOriginKeywordThenLength_When_Parsed_Then_TheKeywordIsTheX()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[left_20px]", out var s);

            // Assert
            Assert.That((ok, s.Value, s.Unit, s.Value2, s.Unit2),
                Is.EqualTo((true, 0f, LengthUnit.Percent, 20f, LengthUnit.Pixel)));
        }

        // The pairs CSS declares invalid: a length is always read x then y, so a vertical keyword ahead of
        // one or a horizontal keyword behind one has no axis left, and two keywords of one axis leave the
        // other unstated.

        // GREEN_ON_BASE(characterization): the base refuses every component that is not a length, and every third one.
        [Test]
        public void Given_TransformOriginVerticalKeywordThenLength_When_Parsed_Then_Declines()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[top_20px]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base refuses every component that is not a length, and every third one.
        [Test]
        public void Given_TransformOriginLengthThenHorizontalKeyword_When_Parsed_Then_Declines()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[20px_left]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base refuses every component that is not a length, and every third one.
        [Test]
        public void Given_TransformOriginTwoHorizontalKeywords_When_Parsed_Then_Declines()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[left_right]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base refuses every component that is not a length, and every third one.
        [Test]
        public void Given_TransformOriginTwoVerticalKeywords_When_Parsed_Then_Declines()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[top_bottom]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base refuses every component that is not a length, and every third one.
        [Test]
        public void Given_TransformOriginLoneUnknownWord_When_Parsed_Then_Declines()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[middle]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base refuses every component that is not a length, and every third one.
        [Test]
        public void Given_TransformOriginUnknownWordFirst_When_Parsed_Then_Declines()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[middle_20px]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base refuses every component that is not a length, and every third one.
        [Test]
        public void Given_TransformOriginUnknownWordSecond_When_Parsed_Then_Declines()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[20px_middle]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base refuses every component that is not a length, and every third one.
        [Test]
        public void Given_TransformOriginWithAPercentZ_When_Parsed_Then_Declines()
        {
            // Act — CSS takes the z as a length only.
            var ok = StyleArbitraryValueResolver.TryParse("origin-[10%_20%_30%]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base refuses every component that is not a length, and every third one.
        [Test]
        public void Given_TransformOriginWithFourComponents_When_Parsed_Then_Declines()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[10px_20px_30px_40px]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_TransformOriginWithALengthZ_When_Applied_Then_TheInlineOriginCarriesIt()
        {
            // Arrange
            var el = new VisualElement();
            var ok = StyleArbitraryValueResolver.TryParse("origin-[10%_20%_30px]", out var s);

            // Act
            StyleArbitraryValueResolver.Apply(el, in s);

            // Assert
            Assert.That((ok, el.style.transformOrigin.value.z), Is.EqualTo((true, 30f)));
        }

        // GREEN_ON_BASE(characterization): the base declines both spellings, so each keys by its raw text.
        // What the case adds is that a z stays in the key once both parse.
        [Test]
        public void Given_TwoTransformOriginsDifferingOnlyInZ_When_Keyed_Then_TheKeysDiffer()
        {
            // Act
            var first = FiberNodePatcher.ValueKey("origin-[1px_2px_3px]");
            var second = FiberNodePatcher.ValueKey("origin-[1px_2px_4px]");

            // Assert
            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void Given_NegatedTransformOriginClass_When_Parsed_Then_Declines()
        {
            // Act — Tailwind declares no negative variant of this utility; a minus inside the
            // brackets is how to write one, and it parses.
            var ok = StyleArbitraryValueResolver.TryParse("-origin-[10%_20%]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_TransformOriginArbitraryStyle_When_Applied_Then_SetsInlineTransformOrigin()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("origin-[33%_75%]", out var s);

            // Act
            StyleArbitraryValueResolver.Apply(el, in s);

            // Assert
            Assert.That((el.style.transformOrigin.value.x.value, el.style.transformOrigin.value.y.value),
                Is.EqualTo((33f, 75f)));
        }

        [Test]
        public void Given_TransformOriginWithAPercentY_When_Applied_Then_TheYKeepsItsOwnUnit()
        {
            // Arrange — the one thing a pair adds over every neighbouring transform utility is a second
            // unit, and a test whose components share one cannot tell them apart. Pixel is the enum's
            // default, so only the percent side of a mixed pair is detectable per case: this one pins the y.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("origin-[12px_80%]", out var s);

            // Act
            StyleArbitraryValueResolver.Apply(el, in s);

            // Assert
            Assert.That(el.style.transformOrigin.value.y.unit, Is.EqualTo(LengthUnit.Percent));
        }

        [Test]
        public void Given_TransformOriginWithAPercentX_When_Applied_Then_TheXKeepsItsOwnUnit()
        {
            // Arrange — the mirror of the case above, which is what it takes to pin both: dropping the x's
            // unit yields Pixel, and Pixel is what a percent-y case expects there anyway.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("origin-[80%_12px]", out var s);

            // Act
            StyleArbitraryValueResolver.Apply(el, in s);

            // Assert
            Assert.That(el.style.transformOrigin.value.x.unit, Is.EqualTo(LengthUnit.Percent));
        }

        [Test]
        public void Given_TransformOriginWithInlineValue_When_Cleared_Then_RevertsToNull()
        {
            // Arrange
            var el = new VisualElement();
            el.style.transformOrigin = new TransformOrigin(Length.Percent(33f), Length.Percent(75f));

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.TransformOrigin);

            // Assert
            Assert.That(el.style.transformOrigin.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_RotateArbitraryStyle_When_Applied_Then_SetsInlineRotateInDegrees()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("rotate-[45deg]", out var s);

            // Act
            StyleArbitraryValueResolver.Apply(el, in s);

            // Assert
            Assert.That(el.style.rotate.value.angle.ToDegrees(), Is.EqualTo(45f).Within(0.001f));
        }

        [Test]
        public void Given_ScaleWithInlineValue_When_Cleared_Then_RevertsToNull()
        {
            // Arrange
            var el = new VisualElement();
            el.style.scale = new Scale(new Vector2(1.4f, 1.4f));

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.Scale);

            // Assert
            Assert.That(el.style.scale.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ScaleArbitraryClass_When_Reconciled_Then_AppliesInlineScale()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("scale-[1.4]") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That((root.ElementAt(0).style.scale.value.value.x, root.ElementAt(0).style.scale.value.value.y),
                Is.EqualTo((1.4f, 1.4f)));
        }

        [Test]
        public void Given_ScaleArbitraryClassRemoved_When_Reconciled_Then_ClearsInlineScale()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("scale-[1.4]") };
            var newTree = new VNode[] { V.Div() };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);
            Assume.That(root.ElementAt(0).style.scale.value.value.x, Is.EqualTo(1.4f),
                "Precondition: the scale class applied the inline scale");

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That(root.ElementAt(0).style.scale.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ScaleXArbitraryClass_When_Parsed_Then_ResolvesScaleXAsUnitlessFactor()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("scale-x-[0.5]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: scale-x-[..] is a recognized arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.ScaleX, 0.5f)));
        }

        [Test]
        public void Given_OnlyScaleX_When_Applied_Then_YAxisStaysIdentity()
        {
            // Arrange — a single axis must leave the other at 1 (identity), not collapse it to 0.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("scale-x-[0.5]", out var sx);

            // Act
            StyleArbitraryValueResolver.Apply(el, in sx);

            // Assert
            Assert.That((el.style.scale.value.value.x, el.style.scale.value.value.y),
                Is.EqualTo((0.5f, 1f)));
        }

        [Test]
        public void Given_ScaleXThenScaleY_When_Applied_Then_BothAxesCompose()
        {
            // Arrange — apply x then y; the y application must preserve the previously set x (merge, not last-win).
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("scale-x-[0.5]", out var sx);
            StyleArbitraryValueResolver.TryParse("scale-y-[1.5]", out var sy);
            StyleArbitraryValueResolver.Apply(el, in sx);

            // Act
            StyleArbitraryValueResolver.Apply(el, in sy);

            // Assert
            Assert.That((el.style.scale.value.value.x, el.style.scale.value.value.y),
                Is.EqualTo((0.5f, 1.5f)));
        }

        [Test]
        public void Given_ScaleXPreset_When_Parsed_Then_ResolvesScaleXFactorFromPercent()
        {
            // scale-x-50 is the preset form (50 -> 0.5), mirroring the uniform .scale-50 USS class.
            var ok = StyleArbitraryValueResolver.TryParse("scale-x-50", out var s);

            Assume.That(ok, Is.True, "Precondition: scale-x-50 is a recognized preset");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.ScaleX, 0.5f)));
        }

        [Test]
        public void Given_ScaleYPreset_When_DispatchGateChecked_Then_ClaimedAsStaticScale()
        {
            // The preset has no USS class, so the dispatch must route it to the resolver, not the class list.
            Assert.That(StyleArbitraryValueResolver.MayBeStaticScale("scale-y-110"), Is.True);
        }

        [Test]
        public void Given_UnknownScaleXPreset_When_DispatchGateChecked_Then_NotClaimed()
        {
            // 37 is not a scale preset, so it is not claimed (falls through; not a recognized utility).
            Assert.That(StyleArbitraryValueResolver.MayBeStaticScale("scale-x-37"), Is.False);
        }

        [Test]
        public void Given_ScaleXPresetThenScaleYPreset_When_Applied_Then_BothAxesCompose()
        {
            // Preset per-axis scales must compose onto one inline `scale`, just like the bracket form.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("scale-x-50", out var sx);
            StyleArbitraryValueResolver.TryParse("scale-y-110", out var sy);
            StyleArbitraryValueResolver.Apply(el, in sx);

            // Act
            StyleArbitraryValueResolver.Apply(el, in sy);

            // Assert
            Assert.That((el.style.scale.value.value.x, el.style.scale.value.value.y),
                Is.EqualTo((0.5f, 1.1f)));
        }

        [Test]
        public void Given_UniformScaleThenScaleX_When_Applied_Then_PerAxisWinsAndUniformIsTheYFallback()
        {
            // Arrange — scale-[1.4] sets both axes to the uniform factor.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("scale-[1.4]", out var uniform);
            StyleArbitraryValueResolver.TryParse("scale-x-[0.5]", out var sx);
            StyleArbitraryValueResolver.Apply(el, in uniform);
            Assume.That((el.style.scale.value.value.x, el.style.scale.value.value.y), Is.EqualTo((1.4f, 1.4f)),
                "Precondition: the uniform scale set both axes");

            // Act — a per-axis scale-x-[.5] is applied over the uniform layer.
            StyleArbitraryValueResolver.Apply(el, in sx);

            // Assert — x takes the per-axis value, y falls back to the uniform scale (merge, not last-write-wins).
            Assert.That((el.style.scale.value.value.x, el.style.scale.value.value.y),
                Is.EqualTo((0.5f, 1.4f)));
        }

        [Test]
        public void Given_PositiveTranslateXPreset_When_Parsed_Then_ResolvesTranslateXFromSpacingScale()
        {
            // translate-x-4 is the positive preset (4 -> 16px). It has no USS class (a .translate-x-N rule
            // clobbers the y axis), so it routes to the TranslateX merge like the negative / bracket forms.
            var ok = StyleArbitraryValueResolver.TryParse("translate-x-4", out var s);

            Assume.That(ok, Is.True, "Precondition: translate-x-4 is a recognized preset");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.TranslateX, 16f)));
        }

        [Test]
        public void Given_TranslateYPreset_When_DispatchGateChecked_Then_ClaimedAsStaticScale()
        {
            // The positive preset has no USS class, so the dispatch must route it to the resolver merge path.
            Assert.That(StyleArbitraryValueResolver.MayBeStaticScale("translate-y-2"), Is.True);
        }

        [Test]
        public void Given_UnknownTranslatePreset_When_DispatchGateChecked_Then_NotClaimed()
        {
            // 37 is not on the spacing scale, so translate-x-37 is not claimed (falls through; not a utility).
            Assert.That(StyleArbitraryValueResolver.MayBeStaticScale("translate-x-37"), Is.False);
        }

        [Test]
        public void Given_DroppedTranslateVelvetism_When_DispatchGateChecked_Then_NotClaimed()
        {
            // The non-standard half-spelling is intentionally dropped (use the canonical 1/2 fraction). It is not
            // claimed, so it no longer resolves — pinning that the removed Velvet-ism stays gone.
            Assert.That(StyleArbitraryValueResolver.MayBeStaticScale("translate-x-half"), Is.False);
        }

        [Test]
        public void Given_TranslateFullPreset_When_Parsed_Then_ResolvesHundredPercent()
        {
            // translate-x-full is 100% of the element's own size (percent unit), merged on the X axis.
            var ok = StyleArbitraryValueResolver.TryParse("translate-x-full", out var s);

            Assume.That(ok, Is.True, "Precondition: translate-x-full is a recognized preset");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.TranslateX, 100f)));
        }

        [Test]
        public void Given_TranslateXPresetThenTranslateYPreset_When_Applied_Then_BothAxesCompose()
        {
            // translate is a single inline Vector3 property: an x and a y preset applied in sequence must
            // compose onto that one value rather than last-write-wins overwriting the other axis.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("translate-x-4", out var tx);
            StyleArbitraryValueResolver.TryParse("translate-y-2", out var ty);
            StyleArbitraryValueResolver.Apply(el, in tx);

            // Act
            StyleArbitraryValueResolver.Apply(el, in ty);

            // Assert
            Assert.That((el.style.translate.value.x.value, el.style.translate.value.y.value),
                Is.EqualTo((16f, 8f)));
        }

        [Test]
        public void Given_WidthHalfFraction_When_Parsed_Then_ResolvesWidthFiftyPercent()
        {
            // w-1/2 == 50% of the parent. USS cannot spell '/', so the slash form resolves here.
            var ok = StyleArbitraryValueResolver.TryParse("w-1/2", out var s);

            Assume.That(ok, Is.True, "Precondition: w-1/2 is a recognized fraction");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.Width, 50f)));
        }

        [Test]
        public void Given_HeightTwoThirdsFraction_When_Parsed_Then_ResolvesHeightFromRatio()
        {
            // h-2/3 has no hyphen equivalent; the slash form is the only syntax that expresses a fractional height.
            var ok = StyleArbitraryValueResolver.TryParse("h-2/3", out var s);

            Assume.That(ok, Is.True, "Precondition: h-2/3 is a recognized fraction");

            // .Within() never reaches a tuple's members under Unity's NUnit (no ValueTupleComparer), so the
            // folded control term gates a scalar and substitutes NaN, which satisfies no tolerance.
            Assert.That(s.Property == ArbitraryProperty.Height ? s.Value : float.NaN,
                Is.EqualTo(200f / 3f).Within(1e-3f));
        }

        [Test]
        public void Given_SizeQuarterFraction_When_Parsed_Then_ResolvesSizeTwentyFivePercent()
        {
            // size-1/4 fans out to both width and height (the Size property).
            var ok = StyleArbitraryValueResolver.TryParse("size-1/4", out var s);

            Assume.That(ok, Is.True, "Precondition: size-1/4 is a recognized fraction");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.Size, 25f)));
        }

        [Test]
        public void Given_WidthTwelfthFraction_When_Parsed_Then_ResolvesFromTwelve()
        {
            // Twelfths are the finest sizing denominator (w-1/12 .. w-11/12).
            var ok = StyleArbitraryValueResolver.TryParse("w-5/12", out var s);

            Assume.That(ok, Is.True, "Precondition: w-5/12 is a recognized fraction");
            Assert.That(s.Property == ArbitraryProperty.Width ? s.Value : float.NaN,
                Is.EqualTo(500f / 12f).Within(1e-3f));
        }

        [Test]
        public void Given_WidthFifthFraction_When_Parsed_Then_ResolvesFromFive()
        {
            // Fifths (/5) are an accepted denominator — pinned so narrowing the accepted set is caught.
            var ok = StyleArbitraryValueResolver.TryParse("w-3/5", out var s);

            Assume.That(ok, Is.True, "Precondition: w-3/5 is a recognized fraction");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.Width, 60f)));
        }

        [Test]
        public void Given_HeightSixthFraction_When_Parsed_Then_ResolvesFromSix()
        {
            // Sixths (/6) are an accepted denominator — pinned alongside fifths.
            var ok = StyleArbitraryValueResolver.TryParse("h-5/6", out var s);

            Assume.That(ok, Is.True, "Precondition: h-5/6 is a recognized fraction");
            Assert.That(s.Property == ArbitraryProperty.Height ? s.Value : float.NaN,
                Is.EqualTo(500f / 6f).Within(1e-3f));
        }

        [Test]
        public void Given_WidthFraction_When_DispatchGateChecked_Then_ClaimedAsStaticScale()
        {
            // The slash form has no USS class, so the dispatch must route it to the resolver.
            Assert.That(StyleArbitraryValueResolver.MayBeStaticScale("w-1/2"), Is.True);
        }

        static readonly object[] TranslateFractionCases =
        {
            // Given_TranslateXFifthFraction_When_Parsed_Then_ResolvesTwentyPercent
            new object[] { "translate-x-1/5", ArbitraryProperty.TranslateX, 20f },
            // Given_TranslateYEighthsFraction_When_Parsed_Then_ResolvesFromEight
            new object[] { "translate-y-3/8", ArbitraryProperty.TranslateY, 37.5f },
            // Given_NegatedTranslateXSixthFraction_When_Parsed_Then_ResolvesNegativeFromSix
            new object[] { "-translate-x-5/6", ArbitraryProperty.TranslateX, -500f / 6f },
            // Given_ImproperTranslateYFraction_When_Parsed_Then_ResolvesAboveOneHundredPercent
            new object[] { "translate-y-3/2", ArbitraryProperty.TranslateY, 150f },
            // Given_NegatedTranslateXTwentyFourthFraction_When_Parsed_Then_ResolvesFromTwentyFour
            new object[] { "-translate-x-5/24", ArbitraryProperty.TranslateX, -500f / 24f },
        };

        [TestCaseSource(nameof(TranslateFractionCases))]
        public void Given_TranslateFractionOutsideThePriorTable_When_Parsed_Then_ResolvesLikeTailwind(
            string className, object property, float percent)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var s);

            // Assert — a declined parse reads as (false, default) and fails the same comparison.
            Assert.That((ok, s.Property, s.Unit, Mathf.Abs(s.Value - percent) < 1e-3f),
                Is.EqualTo((true, (ArbitraryProperty)property, LengthUnit.Percent, true)));
        }

        // GREEN_ON_BASE(characterization): the base's fixed table already declines each of these.
        // Dropping the digit rules or the zero-denominator rule from the shared parser reddens one.
        [TestCase("translate-x-1/0")]
        [TestCase("translate-y-01/2")]
        [TestCase("translate-x-1/")]
        [TestCase("-translate-y-a/b")]
        public void Given_MalformedTranslateFraction_When_Parsed_Then_NotResolved(string className)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [TestCase("top-auto", ArbitraryProperty.Top)]
        [TestCase("right-auto", ArbitraryProperty.Right)]
        [TestCase("bottom-auto", ArbitraryProperty.Bottom)]
        [TestCase("left-auto", ArbitraryProperty.Left)]
        [TestCase("inset-auto", ArbitraryProperty.Inset)]
        [TestCase("inset-x-auto", ArbitraryProperty.InsetX)]
        [TestCase("inset-y-auto", ArbitraryProperty.InsetY)]
        public void Given_PositionAuto_When_Parsed_Then_ResolvesTheAutoKeywordOfThatProperty(
            string className, ArbitraryProperty property)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var s);

            // Assert
            Assert.That((ok, s.Property, s.Auto), Is.EqualTo((true, property, true)));
        }

        // GREEN_ON_BASE(characterization): the base declines each of these. The negated form has no Tailwind
        // utility, and the sizing `*-auto` classes are USS rules; extending the keyword to either reddens this.
        [TestCase("-top-auto")]
        [TestCase("w-auto")]
        [TestCase("min-w-auto")]
        [TestCase("top-autos")]
        public void Given_NonPositionAuto_When_ParsedAndGated_Then_NeitherClaimsIt(string className)
        {
            // Act
            var claimed = StyleArbitraryValueResolver.MayBeStaticScale(className);
            var parsed = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That((claimed, parsed), Is.EqualTo((false, false)));
        }

        [Test]
        public void Given_PositionAutoClassAdded_When_Reconciled_Then_WritesTheAutoKeywordInline()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div() };
            var newTree = new VNode[] { V.Div("top-auto") };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That(root.ElementAt(0).style.top.keyword, Is.EqualTo(StyleKeyword.Auto));
        }

        [Test]
        public void Given_PositionAutoClassRemoved_When_Reconciled_Then_ClearsTheInlineKeyword()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("left-auto") };
            var newTree = new VNode[] { V.Div() };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That(root.ElementAt(0).style.left.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AutoAndAZeroPixelOffset_When_ValueKeysCompared_Then_TheKeysDiffer()
        {
            // Act
            var same = FiberNodePatcher.ValueKey("top-auto") == FiberNodePatcher.ValueKey("top-[0px]");

            // Assert
            Assert.That(same, Is.False);
        }

        static readonly object[] PositionFractionCases =
        {
            // Given_LeftHalfFraction_When_Parsed_Then_ResolvesLeftFiftyPercent
            new object[] { "left-1/2", ArbitraryProperty.Left, 50f },
            // Given_TopHalfFraction_When_Parsed_Then_ResolvesTopFiftyPercent
            new object[] { "top-1/2", ArbitraryProperty.Top, 50f },
            // Given_RightQuarterFraction_When_Parsed_Then_ResolvesRightTwentyFivePercent
            new object[] { "right-1/4", ArbitraryProperty.Right, 25f },
            // Given_BottomThreeQuartersFraction_When_Parsed_Then_ResolvesBottomSeventyFivePercent
            new object[] { "bottom-3/4", ArbitraryProperty.Bottom, 75f },
            // Given_InsetHalfFraction_When_Parsed_Then_ResolvesInsetFiftyPercent
            new object[] { "inset-1/2", ArbitraryProperty.Inset, 50f },
            // Given_InsetXQuarterFraction_When_Parsed_Then_ResolvesInsetXRatherThanInset
            new object[] { "inset-x-1/4", ArbitraryProperty.InsetX, 25f },
            // Given_InsetYThreeQuartersFraction_When_Parsed_Then_ResolvesInsetYRatherThanInset
            new object[] { "inset-y-3/4", ArbitraryProperty.InsetY, 75f },
            // Given_NegatedTopHalfFraction_When_Parsed_Then_ResolvesNegativeFiftyPercent
            new object[] { "-top-1/2", ArbitraryProperty.Top, -50f },
            // Given_NegatedRightQuarterFraction_When_Parsed_Then_ResolvesNegativeTwentyFivePercent
            new object[] { "-right-1/4", ArbitraryProperty.Right, -25f },
            // Given_NegatedBottomThreeQuartersFraction_When_Parsed_Then_ResolvesNegativeSeventyFivePercent
            new object[] { "-bottom-3/4", ArbitraryProperty.Bottom, -75f },
            // Given_NegatedLeftHalfFraction_When_Parsed_Then_ResolvesNegativeFiftyPercent
            new object[] { "-left-1/2", ArbitraryProperty.Left, -50f },
            // Given_NegatedInsetHalfFraction_When_Parsed_Then_ResolvesNegativeFiftyPercent
            new object[] { "-inset-1/2", ArbitraryProperty.Inset, -50f },
            // Given_NegatedInsetXQuarterFraction_When_Parsed_Then_ResolvesNegativeTwentyFivePercent
            new object[] { "-inset-x-1/4", ArbitraryProperty.InsetX, -25f },
            // Given_NegatedInsetYThreeQuartersFraction_When_Parsed_Then_ResolvesNegativeSeventyFivePercent
            new object[] { "-inset-y-3/4", ArbitraryProperty.InsetY, -75f },
        };

        static readonly object[] WideFractionCases =
        {
            // Given_WidthSeventhFraction_When_Parsed_Then_ResolvesFromSeven
            new object[] { "w-1/7", ArbitraryProperty.Width, 100f / 7f },
            // Given_HeightEighthFraction_When_Parsed_Then_ResolvesFromEight
            new object[] { "h-7/8", ArbitraryProperty.Height, 87.5f },
            // Given_SizeTwentyFourthFraction_When_Parsed_Then_ResolvesFromTwentyFour
            new object[] { "size-5/24", ArbitraryProperty.Size, 500f / 24f },
            // Given_MinWidthHalfFraction_When_Parsed_Then_ResolvesMinWidthFiftyPercent
            new object[] { "min-w-1/2", ArbitraryProperty.MinWidth, 50f },
            // Given_MinHeightThirdFraction_When_Parsed_Then_ResolvesMinHeightPercent
            new object[] { "min-h-1/3", ArbitraryProperty.MinHeight, 100f / 3f },
            // Given_MaxWidthQuarterFraction_When_Parsed_Then_ResolvesMaxWidthTwentyFivePercent
            new object[] { "max-w-1/4", ArbitraryProperty.MaxWidth, 25f },
            // Given_MaxHeightThreeQuartersFraction_When_Parsed_Then_ResolvesMaxHeightSeventyFivePercent
            new object[] { "max-h-3/4", ArbitraryProperty.MaxHeight, 75f },
            // Given_BasisThreeQuartersFraction_When_Parsed_Then_ResolvesFlexBasisSeventyFivePercent
            new object[] { "basis-3/4", ArbitraryProperty.FlexBasis, 75f },
            // Given_LeftEighthFraction_When_Parsed_Then_ResolvesFromEight
            new object[] { "left-1/8", ArbitraryProperty.Left, 12.5f },
            // Given_TopSevenTwelfthsFraction_When_Parsed_Then_ResolvesFromTwelve
            new object[] { "top-7/12", ArbitraryProperty.Top, 700f / 12f },
            // Given_ImproperLeftFraction_When_Parsed_Then_ResolvesAboveOneHundredPercent
            new object[] { "left-3/2", ArbitraryProperty.Left, 150f },
            // Given_NegatedImproperLeftFraction_When_Parsed_Then_ResolvesBelowMinusOneHundredPercent
            new object[] { "-left-3/2", ArbitraryProperty.Left, -150f },
            // Given_ZeroNumeratorFraction_When_Parsed_Then_ResolvesZeroPercent
            new object[] { "left-0/5", ArbitraryProperty.Left, 0f },
            // Given_TopFull_When_Parsed_Then_ResolvesOneHundredPercent
            new object[] { "top-full", ArbitraryProperty.Top, 100f },
            // Given_NegatedTopFull_When_Parsed_Then_ResolvesMinusOneHundredPercent
            new object[] { "-top-full", ArbitraryProperty.Top, -100f },
            // Given_InsetFull_When_Parsed_Then_ResolvesInsetOneHundredPercent
            new object[] { "inset-full", ArbitraryProperty.Inset, 100f },
            // Given_InsetXFull_When_Parsed_Then_ResolvesInsetXRatherThanInset
            new object[] { "inset-x-full", ArbitraryProperty.InsetX, 100f },
            // Given_NegatedInsetYFull_When_Parsed_Then_ResolvesInsetYMinusOneHundredPercent
            new object[] { "-inset-y-full", ArbitraryProperty.InsetY, -100f },
        };

        [TestCaseSource(nameof(WideFractionCases))]
        public void Given_FractionOutsideTheSizingDenominators_When_Parsed_Then_ResolvesLikeTailwind(
            string className, object property, float percent)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var s);

            // Assert — a declined parse reads as (false, default) and fails the same comparison.
            Assert.That((ok, s.Property, s.Unit, Mathf.Abs(s.Value - percent) < 1e-3f),
                Is.EqualTo((true, (ArbitraryProperty)property, LengthUnit.Percent, true)));
        }

        // GREEN_ON_BASE(characterization): the base's fixed denominator set already declines each of these.
        // The widened grammar admits any digits, so dropping a digit rule or the zero-denominator rule reddens one.
        // Tailwind reads each half through isPositiveInteger, which refuses a sign, a leading zero, a fraction,
        // a non-digit and an empty half; a zero denominator has no percent to stand for it.
        [TestCase("left-1/0")]
        [TestCase("left-01/2")]
        [TestCase("left-+1/2")]
        [TestCase("left-1.5/2")]
        [TestCase("left-a/b")]
        [TestCase("left-1/")]
        [TestCase("left-/2")]
        [TestCase("left-1/2/3")]
        [TestCase("w-1/0")]
        [TestCase("min-w-1/")]
        public void Given_MalformedFraction_When_Parsed_Then_NotResolved(string className)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the sizing `*-full` classes are USS rules, so neither the gate nor
        // the parser claims them; widening the `full` keyword to the sizing rows reddens this.
        [TestCase("w-full")]
        [TestCase("min-w-full")]
        [TestCase("-w-full")]
        public void Given_SizingFull_When_ParsedAndGated_Then_NeitherClaimsIt(string className)
        {
            // Act
            var claimed = StyleArbitraryValueResolver.MayBeStaticScale(className);
            var parsed = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That((claimed, parsed), Is.EqualTo((false, false)));
        }

        [TestCase("-top-full")]
        [TestCase("inset-x-full")]
        [TestCase("min-h-1/3")]
        [TestCase("basis-3/4")]
        public void Given_FullOrWideFraction_When_DispatchGateChecked_Then_ClaimedAsStaticScale(string className)
        {
            // Act
            var claimed = StyleArbitraryValueResolver.MayBeStaticScale(className);

            // Assert
            Assert.That(claimed, Is.True);
        }

        [TestCaseSource(nameof(PositionFractionCases))]
        public void Given_PositionFraction_When_Parsed_Then_ResolvesAPercentOfTheContainingBlock(
            string className, object property, float percent)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var s);

            // Assert — a declined parse reads as (false, default) and fails the same comparison.
            Assert.That((ok, s.Property, s.Value, s.Unit),
                Is.EqualTo((true, (ArbitraryProperty)property, percent, LengthUnit.Percent)));
        }

        [TestCase("left-1/2")]
        [TestCase("-top-1/2")]
        public void Given_PositionFraction_When_DispatchGateChecked_Then_ClaimedAsStaticScale(string className)
        {
            // Act
            var claimed = StyleArbitraryValueResolver.MayBeStaticScale(className);

            // Assert
            Assert.That(claimed, Is.True);
        }

        // GREEN_ON_BASE(characterization): a negated sizing fraction already resolves to nothing on the base.
        // The signed position families now share its table, and dropping the Signed column reddens this.
        [TestCase("-w-1/2")]
        [TestCase("-h-1/2")]
        [TestCase("-size-1/2")]
        public void Given_NegatedSizingFraction_When_ParsedAndGated_Then_NeitherClaimsIt(string className)
        {
            // Act
            var claimed = StyleArbitraryValueResolver.MayBeStaticScale(className);
            var parsed = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That((claimed, parsed), Is.EqualTo((false, false)));
        }

        // GREEN_ON_BASE(characterization): a gradient's interpolation modifier already stays off the base gate.
        // The fraction gate claims a '/' only behind a fraction family's prefix, and this pins that it does.
        [Test]
        public void Given_ASlashTokenOutsideTheFractionFamilies_When_DispatchGateChecked_Then_NotClaimed()
        {
            // Act
            var claimed = StyleArbitraryValueResolver.MayBeStaticScale("bg-linear-to-r/oklch");

            // Assert
            Assert.That(claimed, Is.False);
        }

        [Test]
        public void Given_ArbitraryDurationMs_When_Parsed_Then_ResolvesTransitionDurationInSeconds()
        {
            // duration-[400ms] is a TIME value -> the TransitionDuration property, stored in seconds (0.4).
            var ok = StyleArbitraryValueResolver.TryParse("duration-[400ms]", out var s);

            Assume.That(ok, Is.True, "Precondition: duration-[400ms] is a recognized arbitrary value");
            Assert.That(s.Property == ArbitraryProperty.TransitionDuration ? s.Value : float.NaN,
                Is.EqualTo(0.4f).Within(1e-5f));
        }

        [Test]
        public void Given_ArbitraryDurationSeconds_When_Parsed_Then_ResolvesSecondsDirectly()
        {
            var ok = StyleArbitraryValueResolver.TryParse("duration-[2s]", out var s);

            Assume.That(ok, Is.True, "Precondition: duration-[2s] is a recognized arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.TransitionDuration, 2f)));
        }

        [Test]
        public void Given_ArbitraryDurationWithoutUnit_When_Parsed_Then_NotResolved()
        {
            // A unit is required on arbitrary durations; a bare number is rejected.
            Assert.That(StyleArbitraryValueResolver.TryParse("duration-[400]", out _), Is.False);
        }

        [Test]
        public void Given_TransitionDurationLayer_When_Cleared_Then_RevertsToNull()
        {
            // The out-of-band TransitionDuration path must clear its inline list on removal (parity with the
            // other shared/out-of-band properties' clear-to-null lifecycle tests).
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.TransitionDuration, 0.4f, LengthUnit.Pixel));
            Assume.That(el.style.transitionDuration.keyword, Is.Not.EqualTo(StyleKeyword.Null), "Precondition: duration applied");

            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.TransitionDuration);

            Assert.That(el.style.transitionDuration.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AspectArbitraryClass_When_Parsed_Then_ResolvesTheDividedRatio()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("aspect-[4/3]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: aspect-[w/h] is a recognized arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.AspectRatio, 4f / 3f)));
        }

        [Test]
        public void Given_AspectArbitrary_When_Applied_Then_SetsInlineAspectRatio()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("aspect-[4/3]", out var s);

            // Act
            StyleArbitraryValueResolver.Apply(el, in s);

            // Assert
            Assert.That(el.style.aspectRatio.value.value, Is.EqualTo(4f / 3f).Within(0.0001f));
        }

        [Test]
        public void Given_BlurArbitraryClass_When_Parsed_Then_ResolvesFilterBlurInPixels()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("blur-[6px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: blur-[..] is a recognized arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.FilterBlur, 6f)));
        }

        // GREEN_ON_BASE(characterization): pins the blur bracket grammar styling-filters.md states.
        // A rem resolves at 16px, and a percentage is rejected, as CSS blur() rejects one.
        [Test]
        public void Given_RemAndPercentBlurBrackets_When_Parsed_Then_RemResolvesToPixelsAndPercentIsRejected()
        {
            // Act
            var rem = StyleArbitraryValueResolver.TryParse("blur-[0.5rem]", out var s);
            var percent = StyleArbitraryValueResolver.TryParse("blur-[50%]", out _);

            // Assert
            Assert.That((rem, s.Property, s.Value, percent), Is.EqualTo((true, ArbitraryProperty.FilterBlur, 8f, false)));
        }

        [Test]
        public void Given_AGrayscaleBracketAboveOne_When_Parsed_Then_ItIsClampedToOne()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("grayscale-[2]", out var s);

            // Assert — CSS clamps a grayscale() amount above 1 to 1.
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FilterGrayscale, 1f)));
        }

        [Test]
        public void Given_AnInvertBracketAboveOne_When_Parsed_Then_ItIsClampedToOne()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("invert-[1.5]", out var s);

            // Assert — CSS clamps an invert() amount above 1 to 1.
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FilterInvert, 1f)));
        }

        [Test]
        public void Given_ASepiaBracketAboveOne_When_Parsed_Then_ItIsClampedToOne()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("sepia-[3]", out var s);

            // Assert — CSS clamps a sepia() amount above 1 to 1.
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FilterSepia, 1f)));
        }

        [Test]
        public void Given_AGrayscaleBracketInPercent_When_Parsed_Then_ItIsTheFraction()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("grayscale-[50%]", out var s);

            // Assert — Tailwind writes grayscale(50%), which CSS reads as 0.5.
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FilterGrayscale, 0.5f)));
        }

        [Test]
        public void Given_AContrastBracketInPercent_When_Parsed_Then_ItIsTheFraction()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("contrast-[150%]", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FilterContrast, 1.5f)));
        }

        [Test]
        public void Given_AUnitlessHueRotateBracket_When_Parsed_Then_ItIsRejected()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("hue-rotate-[30]", out _);

            // Assert — CSS hue-rotate() takes an angle, and a number only where it is zero.
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): a unitless zero stays accepted, as CSS accepts hue-rotate(0).
        [Test]
        public void Given_AUnitlessZeroHueRotateBracket_When_Parsed_Then_ItResolvesToZero()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("hue-rotate-[0]", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FilterHueRotate, 0f)));
        }

        // GREEN_ON_BASE(characterization): a negative percentage is rejected, as CSS rejects a negative contrast().
        [Test]
        public void Given_ANegativeContrastPercentBracket_When_Parsed_Then_ItIsRejected()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("contrast-[-50%]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): a bracket holding no number is rejected, as it was on the base.
        [Test]
        public void Given_AGrayscaleBracketHoldingNoNumber_When_Parsed_Then_ItIsRejected()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("grayscale-[half]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): a zero amount is kept, as CSS takes invert(0).
        [Test]
        public void Given_AZeroInvertBracket_When_Parsed_Then_ItIsKept()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("invert-[0]", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FilterInvert, 0f)));
        }

        // GREEN_ON_BASE(characterization): contrast is not clamped, as CSS leaves contrast() unbounded above 1.
        [Test]
        public void Given_AContrastBracketAboveOne_When_Parsed_Then_ItIsKept()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("contrast-[2.5]", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FilterContrast, 2.5f)));
        }

        // GREEN_ON_BASE(characterization): pins the hue-rotate bracket's angle units styling-filters.md states.
        [Test]
        public void Given_AHueRotateBracketInTurns_When_Parsed_Then_ItResolvesToDegrees()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("hue-rotate-[0.5turn]", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FilterHueRotate, 180f)));
        }

        [Test]
        public void Given_BlurArbitrary_When_Applied_Then_SetsAOneFunctionBlurFilter()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("blur-[6px]", out var s);

            // Act
            StyleArbitraryValueResolver.Apply(el, in s);

            // Assert — one filter function, Blur, with the 6px parameter.
            var filter = el.style.filter.value;
            Assert.That((filter.Count, filter[0].type, filter[0].GetParameter(0).floatValue),
                Is.EqualTo((1, FilterFunctionType.Blur, 6f)));
        }

        [Test]
        public void Given_BlurThenGrayscale_When_Applied_Then_BothComposeInCanonicalOrder()
        {
            // Arrange — two distinct filter utilities must compose into one list (blur before grayscale).
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("blur-[2px]", out var blur);
            StyleArbitraryValueResolver.TryParse("grayscale-[0.5]", out var gray);
            StyleArbitraryValueResolver.Apply(el, in blur);

            // Act
            StyleArbitraryValueResolver.Apply(el, in gray);

            // Assert
            var filter = el.style.filter.value;
            Assert.That((filter.Count, filter[0].type, filter[1].type),
                Is.EqualTo((2, FilterFunctionType.Blur, FilterFunctionType.Grayscale)));
        }

        [Test]
        public void Given_BlurPreset_When_Parsed_Then_ResolvesBlurPixelsFromTheNamedScale()
        {
            // blur-sm is the named preset (4px); no USS class, routed to the filter path.
            var ok = StyleArbitraryValueResolver.TryParse("blur-sm", out var s);

            Assume.That(ok, Is.True, "Precondition: blur-sm is a recognized filter preset");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.FilterBlur, 4f)));
        }

        [Test]
        public void Given_BareBlur_When_Parsed_Then_ResolvesDefaultEightPixels()
        {
            var ok = StyleArbitraryValueResolver.TryParse("blur", out var s);

            Assume.That(ok, Is.True, "Precondition: bare blur is a recognized filter preset");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.FilterBlur, 8f)));
        }

        [Test]
        public void Given_BareGrayscale_When_Parsed_Then_ResolvesFullGrayscale()
        {
            // The bare grayscale means grayscale(100%).
            var ok = StyleArbitraryValueResolver.TryParse("grayscale", out var s);

            Assume.That(ok, Is.True, "Precondition: bare grayscale is a recognized filter preset");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.FilterGrayscale, 1f)));
        }

        [Test]
        public void Given_ContrastPreset_When_Parsed_Then_ResolvesContrastFactor()
        {
            var ok = StyleArbitraryValueResolver.TryParse("contrast-125", out var s);

            Assume.That(ok, Is.True, "Precondition: contrast-125 is a recognized filter preset");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.FilterContrast, 1.25f)));
        }

        [Test]
        public void Given_NegativeHueRotatePreset_When_Parsed_Then_ResolvesNegativeDegrees()
        {
            // hue-rotate is the only filter with a negative preset.
            var ok = StyleArbitraryValueResolver.TryParse("-hue-rotate-90", out var s);

            Assume.That(ok, Is.True, "Precondition: -hue-rotate-90 is a recognized filter preset");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.FilterHueRotate, -90f)));
        }

        [Test]
        public void Given_FilterPreset_When_DispatchGateChecked_Then_ClaimedAsResolverToken()
        {
            // No USS class exists for the preset, so the dispatch must route it to the resolver, not the class list.
            Assert.That(StyleArbitraryValueResolver.MayBeStaticScale("contrast-125"), Is.True);
        }

        [TestCase("blur")]
        [TestCase("grayscale")]
        [TestCase("invert")]
        [TestCase("sepia")]
        public void Given_BareFilterToken_When_DispatchGateChecked_Then_ClaimedAsResolverToken(string token)
        {
            // A bare filter token has no USS class, so the dispatch must claim it for the resolver — otherwise it
            // would silently fall back to an inert class. (TryParse alone cannot catch a gate regression here.)
            Assert.That(StyleArbitraryValueResolver.MayBeStaticScale(token), Is.True);
        }

        [Test]
        public void Given_UnknownFilterPresetSuffix_When_Parsed_Then_NotRecognized()
        {
            // A suffix outside the preset table fails the precise parse (falls through to the class list).
            Assert.That(StyleArbitraryValueResolver.TryParse("blur-foo", out _), Is.False);
        }

        [Test]
        public void Given_BlurPresetThenContrastPreset_When_Applied_Then_BothComposeAsFilters()
        {
            // Preset filters must compose through the same combined-filter path as the bracket forms.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("blur-sm", out var blur);
            StyleArbitraryValueResolver.TryParse("contrast-125", out var contrast);
            StyleArbitraryValueResolver.Apply(el, in blur);

            // Act
            StyleArbitraryValueResolver.Apply(el, in contrast);

            // Assert
            var filter = el.style.filter.value;
            Assert.That((filter.Count, filter[0].type, filter[1].type),
                Is.EqualTo((2, FilterFunctionType.Blur, FilterFunctionType.Contrast)));
        }

        [Test]
        public void Given_BrightnessPreset_When_Parsed_Then_ResolvesMultiplier()
        {
            // brightness-50 == multiplier 0.5.
            var ok = StyleArbitraryValueResolver.TryParse("brightness-50", out var s);

            Assume.That(ok, Is.True, "Precondition: brightness-50 is recognized");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.FilterBrightness, 0.5f)));
        }

        [Test]
        public void Given_BrightnessArbitrary_When_Applied_Then_EmitsTheBuiltInBrightnessShaderFunction()
        {
            // brightness renders through the first-party custom-filter shader as a Custom function bound to
            // the shared brightness definition, carrying the raw multiplier as its single parameter.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("brightness-[0.8]", out var s);

            StyleArbitraryValueResolver.Apply(el, in s);

            var f = el.style.filter.value[0];
            Assert.That((f.type, f.customDefinition, f.GetParameter(0).floatValue),
                Is.EqualTo((FilterFunctionType.Custom, BuiltInFilterDefinitions.Brightness, 0.8f)));
        }

        [Test]
        public void Given_OverBrightnessPreset_When_Parsed_Then_ResolvesTheWidenedMultiplier()
        {
            // brightness-150 is an over-bright preset (N>1): the shader multiplies unclamped, so the value must
            // resolve to a multiplier above 1.
            var ok = StyleArbitraryValueResolver.TryParse("brightness-150", out var s);

            Assume.That(ok, Is.True, "Precondition: brightness-150 is recognized");
            Assert.That(s.Value, Is.EqualTo(1.5f));
        }

        [Test]
        public void Given_OverBrightnessArbitrary_When_Parsed_Then_ResolvesTheUnclampedMultiplier()
        {
            // brightness-[1.5] is accepted (only negative amounts are rejected) and carries the raw factor.
            var ok = StyleArbitraryValueResolver.TryParse("brightness-[1.5]", out var s);

            Assume.That(ok, Is.True, "Precondition: brightness-[1.5] is recognized");
            Assert.That(s.Value, Is.EqualTo(1.5f));
        }

        [Test]
        public void Given_OverSaturateArbitrary_When_Parsed_Then_ResolvesTheUnclampedFraction()
        {
            // saturate-[1.5] is accepted (only negative amounts are rejected) and carries the raw fraction.
            var ok = StyleArbitraryValueResolver.TryParse("saturate-[1.5]", out var s);

            Assume.That(ok, Is.True, "Precondition: saturate-[1.5] is recognized");
            Assert.That(s.Value, Is.EqualTo(1.5f));
        }

        [Test]
        public void Given_GrayscaleAndSaturate_When_Applied_Then_TheyRemainIndependentComposedLayers()
        {
            // grayscale and saturate are separate layers, so an element with both composes TWO filter functions
            // rather than one clobbering the other.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("grayscale", out var g);
            StyleArbitraryValueResolver.TryParse("saturate-50", out var s);
            StyleArbitraryValueResolver.Apply(el, in g);

            StyleArbitraryValueResolver.Apply(el, in s);

            Assert.That(el.style.filter.value.Count, Is.EqualTo(2));
        }

        [Test]
        public void Given_BrightnessAndContrast_When_Applied_Then_BrightnessComposesBeforeContrast()
        {
            // Canonical CSS filter order: brightness precedes contrast. Brightness is a Custom function keyed by
            // its own filter slot rather than routed through LayerMap.Customs, which would compose it after
            // every built-in and break this order.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("contrast-125", out var c);
            StyleArbitraryValueResolver.TryParse("brightness-50", out var b);
            StyleArbitraryValueResolver.Apply(el, in c);

            StyleArbitraryValueResolver.Apply(el, in b);

            var f = el.style.filter.value[0];
            Assert.That((f.type, f.customDefinition),
                Is.EqualTo((FilterFunctionType.Custom, BuiltInFilterDefinitions.Brightness)));
        }

        [Test]
        public void Given_SaturatePreset_When_Applied_Then_EmitsTheBuiltInSaturateShaderFunction()
        {
            // saturate renders through the first-party custom-filter shader as a Custom function bound to the
            // shared saturate definition. The stored parameter is the RAW fraction (0.5): the shader implements
            // the lerp-toward-luminance natively, so there is no 1-N complement to pre-compute.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("saturate-50", out var s);

            StyleArbitraryValueResolver.Apply(el, in s);

            var f = el.style.filter.value[0];
            Assert.That((f.type, f.customDefinition, f.GetParameter(0).floatValue),
                Is.EqualTo((FilterFunctionType.Custom, BuiltInFilterDefinitions.Saturate, 0.5f)));
        }

        [Test]
        public void Given_FullSaturate_When_Applied_Then_EmitsIdentityParameter()
        {
            // saturate-100 is the CSS identity, so the shader parameter is 1 (unchanged) — the raw fraction
            // itself, since the shader needs no complement transform.
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("saturate-100", out var s);

            StyleArbitraryValueResolver.Apply(el, in s);

            Assert.That(el.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(1f));
        }

        [Test]
        public void Given_OverSaturate_When_Parsed_Then_ResolvesTheWidenedFraction()
        {
            // saturate-150 is an over-saturate preset (N>1): the shader lerps toward luminance unclamped, so the
            // value must resolve to a fraction above 1.
            var ok = StyleArbitraryValueResolver.TryParse("saturate-150", out var s);

            Assume.That(ok, Is.True, "Precondition: saturate-150 is recognized");
            Assert.That(s.Value, Is.EqualTo(1.5f));
        }

        [TestCase("brightness-50")]
        [TestCase("saturate-50")]
        public void Given_BrightnessOrSaturatePreset_When_DispatchGateChecked_Then_Claimed(string token)
        {
            Assert.That(StyleArbitraryValueResolver.MayBeStaticScale(token), Is.True);
        }

        #endregion

        #region Per-property layering (variant off falls back, not wipes)

        [Test]
        public void Given_BaseHoverActiveWidthLayers_When_ActiveCleared_Then_HoverWidthIsRestored()
        {
            // Arrange — w-[80px] (base) + hover:w-[200px] + active:w-[100px], all active (active wins).
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Width, 80f, LengthUnit.Pixel), StyleLayerPriority.Base);
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Width, 200f, LengthUnit.Pixel), StyleLayerPriority.Hover);
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Width, 100f, LengthUnit.Pixel), StyleLayerPriority.Active);
            Assume.That(el.style.width.value.value, Is.EqualTo(100f), "Precondition: active (highest) wins");

            // Act — release (active off).
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.Width, StyleLayerPriority.Active);

            // Assert — falls back to hover (200), NOT wiped to the base/USS default.
            Assert.That(el.style.width.value.value, Is.EqualTo(200f));
        }

        [Test]
        public void Given_BaseAndHoverWidthLayers_When_HoverCleared_Then_BaseWidthIsRestored()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Width, 80f, LengthUnit.Pixel), StyleLayerPriority.Base);
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Width, 200f, LengthUnit.Pixel), StyleLayerPriority.Hover);
            Assume.That(el.style.width.value.value, Is.EqualTo(200f), "Precondition: hover wins");

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.Width, StyleLayerPriority.Hover);

            // Assert — base (80) survives the hover-off.
            Assert.That(el.style.width.value.value, Is.EqualTo(80f));
        }

        [Test]
        public void Given_CheckedAndHoverWidthLayers_When_BothActive_Then_HoverWins()
        {
            // Tailwind emits checked before hover in its variant order, so a hovered checked control's
            // hover:w-[200px] wins the same-property tie over checked:w-[100px].
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Width, 100f, LengthUnit.Pixel), StyleLayerPriority.Checked);
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Width, 200f, LengthUnit.Pixel), StyleLayerPriority.Hover);

            Assert.That(el.style.width.value.value, Is.EqualTo(200f));
        }

        [Test]
        public void Given_CheckedAndHoverWidthLayers_When_HoverCleared_Then_CheckedIsRestored()
        {
            // Hover off falls back to the still-checked layer (not wiped), so the checked value returns.
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Width, 100f, LengthUnit.Pixel), StyleLayerPriority.Checked);
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Width, 200f, LengthUnit.Pixel), StyleLayerPriority.Hover);
            Assume.That(el.style.width.value.value, Is.EqualTo(200f), "Precondition: hover wins over checked");

            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.Width, StyleLayerPriority.Hover);

            Assert.That(el.style.width.value.value, Is.EqualTo(100f));
        }

        [Test]
        public void Given_TranslateYBaseAndTranslateXHover_When_TranslateXCleared_Then_TranslateYSurvives()
        {
            // Arrange — translate-y-[20px] (base) + hover:translate-x-[10px].
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.TranslateY, 20f, LengthUnit.Pixel), StyleLayerPriority.Base);
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.TranslateX, 10f, LengthUnit.Pixel), StyleLayerPriority.Hover);
            Assume.That(el.style.translate.value.x.value, Is.EqualTo(10f), "Precondition: translate-x applied");

            // Act — hover off clears translate-x only.
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.TranslateX, StyleLayerPriority.Hover);

            // Assert — translate-y is NOT lost when the other axis clears (the translate sub-case).
            Assert.That(el.style.translate.value.y.value, Is.EqualTo(20f));
        }

        [Test]
        public void Given_BaseSmMdFontSizeLayers_When_MdCleared_Then_SmFontSizeIsRestored()
        {
            // Arrange — text-[12px] (base) + sm:text-[14px] + md:text-[18px], all active (md wins).
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.FontSize, 12f, LengthUnit.Pixel), StyleLayerPriority.Base);
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.FontSize, 14f, LengthUnit.Pixel), StyleLayerPriority.ResponsiveSm);
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.FontSize, 18f, LengthUnit.Pixel), StyleLayerPriority.ResponsiveMd);
            Assume.That(el.style.fontSize.value.value, Is.EqualTo(18f), "Precondition: md (largest bp) wins");

            // Act — shrink below md.
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.FontSize, StyleLayerPriority.ResponsiveMd);

            // Assert — falls back to sm (14), not wiped.
            Assert.That(el.style.fontSize.value.value, Is.EqualTo(14f));
        }

        [Test]
        public void Given_GroupHoverAndGroupActiveWidthLayers_When_ActiveCleared_Then_HoverLayerSurvives()
        {
            // Arrange — group-hover:w-[100px] + group-active:w-[200px], both active (distinct relational states).
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Width, 100f, LengthUnit.Pixel), StyleLayerPriority.GroupHover);
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Width, 200f, LengthUnit.Pixel), StyleLayerPriority.GroupActive);
            Assume.That(el.style.width.value.value, Is.EqualTo(200f), "Precondition: group-active wins");

            // Act — group-active off (pointer up over the group, still hovering).
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.Width, StyleLayerPriority.GroupActive);

            // Assert — group-hover (100) survives; the two relational sub-states must not share a layer slot.
            Assert.That(el.style.width.value.value, Is.EqualTo(100f));
        }

        [Test]
        public void Given_OnlyBaseWidthLayer_When_Cleared_Then_PropertyRevertsToNull()
        {
            // Arrange — a single layer (the common case): clearing it reverts to the USS default, as before.
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Width, 80f, LengthUnit.Pixel), StyleLayerPriority.Base);
            Assume.That(el.style.width.value.value, Is.EqualTo(80f), "Precondition: base applied");

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.Width, StyleLayerPriority.Base);

            // Assert — no surviving layer, so the inline value is cleared.
            Assert.That(el.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        #endregion

        #region Opacity / Size / FlexBasis (parity additions)

        [Test]
        public void Given_OpacityArbitraryWithLeadingDot_When_Parsed_Then_ResolvesOpacityAsUnitlessFloat()
        {
            // Act — opacity is a unitless float (0..1), routed alongside scale-/rotate-.
            var ok = StyleArbitraryValueResolver.TryParse("opacity-[.37]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.Opacity, 0.37f)));
        }

        [Test]
        public void Given_OpacityArbitrary_When_AppliedToElement_Then_SetsInlineOpacity()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("opacity-[0.5]", out var style);

            // Act
            StyleArbitraryValueResolver.Apply(el, in style);

            // Assert
            Assert.That(el.style.opacity.value, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void Given_OpacityArbitrary_When_Cleared_Then_RevertsInlineOpacityToNull()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Opacity, 0.5f, LengthUnit.Pixel));
            Assume.That(el.style.opacity.value, Is.EqualTo(0.5f).Within(0.0001f), "Precondition: applied");

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.Opacity);

            // Assert
            Assert.That(el.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_SizeArbitraryPixel_When_Parsed_Then_ResolvesSizeProperty()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("size-[40px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value, s.Unit),
                Is.EqualTo((ArbitraryProperty.Size, 40f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_SizeArbitrary_When_AppliedToElement_Then_SetsBothWidthAndHeight()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("size-[40px]", out var style);

            // Act
            StyleArbitraryValueResolver.Apply(el, in style);

            // Assert
            Assert.That((el.style.width.value.value, el.style.height.value.value), Is.EqualTo((40f, 40f)));
        }

        [Test]
        public void Given_SizeArbitrary_When_Cleared_Then_RevertsBothWidthAndHeight()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.Size, 40f, LengthUnit.Pixel));

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.Size);

            // Assert
            Assert.That((el.style.width.keyword, el.style.height.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_GrowArbitrary_When_Parsed_Then_ResolvesFlexGrow()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("grow-[2]", out var s);

            // Assert — the recognition rides in the tuple because a default ArbitraryStyle is a real
            // property/value pair, so an unrecognized token would otherwise report as an Assume.
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FlexGrow, 2f)));
        }

        [Test]
        public void Given_ShrinkArbitrary_When_Parsed_Then_ResolvesFlexShrink()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("shrink-[3]", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FlexShrink, 3f)));
        }

        [Test]
        public void Given_GrowArbitraryWithAPercent_When_Parsed_Then_DeclinesToParse()
        {
            // Act — a ratio has no unit, and the float setters read only the value: accepting this would
            // make grow-[50%] a factor of fifty.
            var ok = StyleArbitraryValueResolver.TryParse("grow-[50%]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): a flex factor never took a percentage, and the border decline leaves it so.
        [Test]
        public void Given_ShrinkArbitraryWithAPercent_When_Parsed_Then_DeclinesToParse()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("shrink-[50%]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_GrowArbitraryWithALengthUnit_When_Parsed_Then_DeclinesToParse()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("grow-[2rem]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_NegatedGrowArbitrary_When_Parsed_Then_DeclinesToParse()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("-grow-[2]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_GrowArbitraryFraction_When_AppliedToElement_Then_SetsInlineFlexGrow()
        {
            // Arrange — a fraction, because the whole point is a ratio the 0/1 utilities cannot express.
            var el = new VisualElement();
            var ok = StyleArbitraryValueResolver.TryParse("grow-[2.5]", out var style);

            // Act
            StyleArbitraryValueResolver.Apply(el, in style);

            // Assert
            Assert.That((ok, el.style.flexGrow.value), Is.EqualTo((true, 2.5f)));
        }

        [Test]
        public void Given_ShrinkArbitrary_When_AppliedToElement_Then_SetsInlineFlexShrink()
        {
            // Arrange
            var el = new VisualElement();
            var ok = StyleArbitraryValueResolver.TryParse("shrink-[3]", out var style);

            // Act
            StyleArbitraryValueResolver.Apply(el, in style);

            // Assert
            Assert.That((ok, el.style.flexShrink.value), Is.EqualTo((true, 3f)));
        }

        [Test]
        public void Given_GrowArbitrary_When_Cleared_Then_RevertsInlineFlexGrowToNull()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.FlexGrow, 2f, LengthUnit.Pixel));
            var applied = el.style.flexGrow.value;

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.FlexGrow);

            // Assert — the applied value rides along because a fresh element already reads Null, so the
            // keyword alone cannot tell a revert from a setter that never wrote.
            Assert.That((applied, el.style.flexGrow.keyword), Is.EqualTo((2f, StyleKeyword.Null)));
        }

        [Test]
        public void Given_BareGrow_When_Parsed_Then_ResolvesFlexGrow()
        {
            // Act — Tailwind's grow-<number>, the bracketless spelling of a whole factor.
            var ok = StyleArbitraryValueResolver.TryParse("grow-3", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FlexGrow, 3f)));
        }

        [Test]
        public void Given_BareShrink_When_Parsed_Then_ResolvesFlexShrink()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("shrink-2", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FlexShrink, 2f)));
        }

        [Test]
        public void Given_BareGrowOfTwoDigits_When_Parsed_Then_ResolvesThatFactor()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("grow-12", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, ArbitraryProperty.FlexGrow, 12f)));
        }

        // GREEN_ON_BASE(characterization): the base routes no bare grow factor inline, grow-0 included.
        // What reddens it is dropping the `whole != 0` check in TryGetFlexFactorPreset, which claims the zero
        // the USS class already carries.
        [Test]
        public void Given_BareGrowZero_When_TheInlineGateIsAsked_Then_ItIsLeftToTheClassList()
        {
            // Act
            var inline = StyleArbitraryValueResolver.IsInlineResolved("grow-0");

            // Assert
            Assert.That(inline, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base parses no bare grow factor, a fractional one included.
        // What reddens it is a parse that accepts a decimal point, where Tailwind's bare value is a whole
        // number and the fraction takes the bracket form.
        [Test]
        public void Given_BareGrowWithAFraction_When_Parsed_Then_DeclinesToParse()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("grow-1.5", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ADivCarryingBareGrow_When_Reconciled_Then_TheElementCarriesTheInlineFactor()
        {
            // Arrange — the whole path: the class has no USS rule, so it lands only if the dispatch gate
            // routes it to the resolver.
            using var scope = new Velvet.TestUtilities.ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "grow-3") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0].style.flexGrow.value, Is.EqualTo(3f));
        }

        [Test]
        public void Given_BasisArbitraryPixel_When_Parsed_Then_ResolvesFlexBasis()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("basis-[120px]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value, s.Unit),
                Is.EqualTo((ArbitraryProperty.FlexBasis, 120f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_BasisArbitraryPercent_When_Parsed_Then_ResolvesFlexBasisAsPercent()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("basis-[50%]", out var s);

            // Assert
            Assume.That(ok, Is.True, "Precondition: the class is recognized as an arbitrary value");
            Assert.That((s.Property, s.Value, s.Unit),
                Is.EqualTo((ArbitraryProperty.FlexBasis, 50f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_BasisArbitrary_When_AppliedToElement_Then_SetsInlineFlexBasis()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("basis-[120px]", out var style);

            // Act
            StyleArbitraryValueResolver.Apply(el, in style);

            // Assert
            Assert.That(el.style.flexBasis.value.value, Is.EqualTo(120f));
        }

        [Test]
        public void Given_BasisArbitrary_When_Cleared_Then_RevertsInlineFlexBasisToNull()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.Apply(el, new ArbitraryStyle(ArbitraryProperty.FlexBasis, 120f, LengthUnit.Pixel));
            Assume.That(el.style.flexBasis.value.value, Is.EqualTo(120f), "Precondition: applied");

            // Act
            StyleArbitraryValueResolver.Clear(el, ArbitraryProperty.FlexBasis);

            // Assert
            Assert.That(el.style.flexBasis.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_OpacityArbitraryAboveOne_When_Parsed_Then_DeclinesToParse()
        {
            // Act — opacity is 0..1; UITK does not clamp, so an out-of-range value is not a utility.
            var ok = StyleArbitraryValueResolver.TryParse("opacity-[2]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_NegatedOpacityArbitrary_When_Parsed_Then_DeclinesToParse()
        {
            // Act — a negative opacity is meaningless, so the negated form is rejected.
            var ok = StyleArbitraryValueResolver.TryParse("-opacity-[.5]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_EveryPerEdgeAndPerCornerPrefix_When_Resolved_Then_ItNamesTheEdgeItsSpellingDoes()
        {
            // Arrange — the per-edge and per-corner prefixes are the half of the prefix table whose entries
            // are indistinguishable to a reader: `pb-` and `pr-` differ by one letter and map to properties
            // that differ by one word, so a transposed pair parses, applies, and writes the wrong edge in
            // silence. Deriving the expected name from the spelling catches that without restating the table:
            // an entry added along the same convention needs no edit here, and only a swap fails.
            var edges = new[] { ("t", "Top"), ("r", "Right"), ("b", "Bottom"), ("l", "Left") };
            var corners = new[] { ("tl", "TopLeft"), ("tr", "TopRight"), ("bl", "BottomLeft"), ("br", "BottomRight") };
            var expected = new List<(string Prefix, string Property)>();
            foreach (var (letter, word) in edges)
            {
                expected.Add(($"p{letter}-", $"Padding{word}"));
                expected.Add(($"m{letter}-", $"Margin{word}"));
                expected.Add(($"border-{letter}-", $"Border{word}Width"));
                expected.Add(($"rounded-{letter}-", $"Border{word}Radius"));
                expected.Add(($"{word.ToLowerInvariant()}-", word));
            }
            foreach (var (letters, words) in corners)
            {
                expected.Add(($"rounded-{letters}-", $"Border{words}Radius"));
            }

            // Act
            var wrong = new List<string>();
            foreach (var (prefix, property) in expected)
            {
                var resolved = StyleArbitraryValueResolver.TryGetProperty(prefix, out var actual)
                    ? actual.ToString()
                    : "<unmapped>";
                if (resolved != property)
                {
                    wrong.Add($"{prefix} -> {resolved}, expected {property}");
                }
            }

            // Assert — the population rides along so a convention change that emptied the derivation cannot
            // leave this green having compared nothing.
            Assert.That((expected.Count, string.Join("; ", wrong)), Is.EqualTo((24, string.Empty)));
        }

        #endregion
    }
}
