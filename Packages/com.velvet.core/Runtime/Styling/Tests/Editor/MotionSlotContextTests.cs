using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class MotionSlotContextTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AnUnusableNativeMemberReceiver_When_Read_Then_ItReturnsNull(bool method)
        {
            // Arrange
            System.Reflection.MemberInfo member = method
                ? EngineMember.HasRunningStyleAnimation.ResolveMethod()
                : EngineMember.StylePropertyNameId.ResolveProperty();
            var reader = typeof(MotionSlotContext).GetMethod("ReadNativeMemberOrNull",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var id = EngineMember.StylePropertyNameId.ResolveProperty()?.GetValue(new StylePropertyName("width"));

            // Act
            var value = reader.Invoke(null, new object[] { member, new object(), method ? new[] { id } : null });

            // Assert
            Assert.That((member != null, value), Is.EqualTo((true, (object)null)));
        }

        [Test]
        public void Given_AnOffPanelElement_When_TheContextIsRead_Then_NoContextIsCreated()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            var context = MotionSlotContext.Read(element, new string[0], new[] { "w-[100px]" }, _ => false);

            // Assert
            Assert.That(context, Is.Null);
        }

        [TestCase(ArbitraryProperty.Opacity, "opacity-[0.25]", 0.25f)]
        [TestCase(ArbitraryProperty.TranslateX, "translate-x-[17px]", 17f)]
        [TestCase(ArbitraryProperty.TranslateY, "translate-y-[19px]", 19f)]
        [TestCase(ArbitraryProperty.Scale, "scale-[0.5]", 0.5f)]
        [TestCase(ArbitraryProperty.Rotate, "rotate-[15deg]", 15f)]
        [TestCase(ArbitraryProperty.Width, "w-[23px]", 23f)]
        [TestCase(ArbitraryProperty.Height, "h-[29px]", 29f)]
        [TestCase(ArbitraryProperty.PaddingTop, "pt-[7px]", 7f)]
        [TestCase(ArbitraryProperty.BorderTopWidth, "border-t-[3px]", 3f)]
        [TestCase(ArbitraryProperty.BorderRightWidth, "border-r-[4px]", 4f)]
        [TestCase(ArbitraryProperty.BorderBottomWidth, "border-b-[5px]", 5f)]
        [TestCase(ArbitraryProperty.BorderLeftWidth, "border-l-[6px]", 6f)]
        public void Given_AnInlineNumericSlot_When_TheProviderSamplesIt_Then_ItReturnsTheDeclaredValueAndHolder(
            ArbitraryProperty slot, string token, float expected)
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, token, StyleLayerPriority.Base);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(slot, LengthUnit.Pixel, out var value);
            var holder = context.HoldsInlineOutsideSwap(slot);

            // Assert
            Assert.That(new[] { read && value.Property == slot && value.Unit == LengthUnit.Pixel ? value.Value : float.NaN, holder ? 1f : 0f },
                Is.EqualTo(new[] { expected, 1f }).Within(0.001f));
        }

        [TestCase(ArbitraryProperty.TextColor, "text-[#ff0000]")]
        [TestCase(ArbitraryProperty.BackgroundColor, "bg-[#ff0000]")]
        [TestCase(ArbitraryProperty.BorderColor, "border-[#ff0000]")]
        public void Given_AnInlineColor_When_TheProviderSamplesIt_Then_ItReturnsThatColor(ArbitraryProperty slot, string token)
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, token, StyleLayerPriority.Base);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(slot, LengthUnit.Percent, out var value);

            // Assert
            Assert.That((read, value.Property, value.Color), Is.EqualTo((true, slot, Color.red)));
        }

        [TestCase(ArbitraryProperty.Width, "w-[50%]", LengthUnit.Pixel, 100f)]
        [TestCase(ArbitraryProperty.Width, "w-[40px]", LengthUnit.Percent, 20f)]
        [TestCase(ArbitraryProperty.Height, "h-[25%]", LengthUnit.Pixel, 25f)]
        [TestCase(ArbitraryProperty.Height, "h-[40px]", LengthUnit.Percent, 40f)]
        public void Given_ADimensionInAnotherUnit_When_TheProviderSamplesIt_Then_ItUsesTheParentBasis(
            ArbitraryProperty slot, string token, LengthUnit unit, float expected)
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, token, StyleLayerPriority.Base);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(slot, unit, out var value);

            // Assert
            Assert.That(read && value.Unit == unit ? value.Value : float.NaN, Is.EqualTo(expected).Within(0.02f));
        }

        [TestCase("pt-[10%]", false)]
        [TestCase("w-[50%]", true)]
        [TestCase("w-[40px]", false, true)]
        public void Given_AConversionWithoutASupportedBasis_When_TheProviderSamplesIt_Then_ItDeclinesTheValue(
            string token, bool zeroBasis, bool infiniteBasis = false)
        {
            // Arrange
            var element = MountInFixedParent();
            if (zeroBasis)
            {
                element.parent.style.width = 0f;
                ForcePanelUpdate(element.panel);
            }
            StyleArbitraryValueResolver.ApplyClassToken(element, token, StyleLayerPriority.Base);
            if (infiniteBasis)
            {
                element.parent.style.width = StyleKeyword.Auto;
                element.parent.style.alignSelf = Align.FlexStart;
                element.parent.style.flexDirection = FlexDirection.Row;
                element.style.borderLeftWidth = float.PositiveInfinity;
                ForcePanelUpdate(element.panel);
            }
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(zeroBasis || infiniteBasis ? ArbitraryProperty.Width : ArbitraryProperty.PaddingTop,
                infiniteBasis ? LengthUnit.Percent : LengthUnit.Pixel, out _);
            var basisPresent = !infiniteBasis || float.IsPositiveInfinity(element.parent.contentRect.width);

            // Assert
            Assert.That((basisPresent, read), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_FlaggedLengths_When_ReadInline_Then_TheGettersNormalizeNonNumericKeywords()
        {
            // Arrange
            var element = new VisualElement();
            var readers = (Dictionary<ArbitraryProperty, System.Func<IStyle, StyleLength>>)typeof(MotionSlotContext)
                .GetField("s_inlineLengths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);
            var properties = typeof(IStyle).GetProperties().Where(property => property.PropertyType == typeof(StyleLength)).ToArray();
            var samples = new[] { (new Length(17f), StyleKeyword.Undefined),
                (Length.Auto(), StyleKeyword.Auto), (Length.None(), StyleKeyword.None) };
            var normalized = new List<bool>();

            // Act
            foreach (var sample in samples)
            {
                var input = new StyleLength(sample.Item1) { keyword = StyleKeyword.Undefined };
                foreach (var property in properties) property.SetValue(element.style, input);
                foreach (var read in readers.Values)
                {
                    var inline = read(element.style);
                    normalized.Add(inline.keyword == sample.Item2 && inline.value.Equals(input.value));
                }
            }

            // Assert
            Assert.That(normalized, Is.EqualTo(Enumerable.Repeat(true, 75)));
        }

        [TestCase(ArbitraryProperty.Width, 200f)]
        [TestCase(ArbitraryProperty.Opacity, 1f)]
        [TestCase(ArbitraryProperty.Scale, 1f)]
        [TestCase(ArbitraryProperty.Rotate, 0f)]
        [TestCase(ArbitraryProperty.TranslateX, 0f)]
        [TestCase(ArbitraryProperty.TranslateY, 0f)]
        [TestCase(ArbitraryProperty.Opacity, 0.5f, true)]
        [TestCase(ArbitraryProperty.Width, 100f, false, LengthUnit.Percent)]
        public void Given_AComputedSlotWithoutAnInlineOverride_When_TheProviderSamplesIt_Then_ItReadsTheResolvedValue(
            ArbitraryProperty slot, float expected, bool unrelatedInlineScale = false, LengthUnit unit = LengthUnit.Pixel)
        {
            // Arrange
            var element = MountInFixedParent();
            if (unrelatedInlineScale)
            {
                element.AddToClassList("opacity-50");
                element.style.scale = new Scale(new Vector3(2f, 2f, 1f));
                ForcePanelUpdate(element.panel);
            }
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(slot, unit, out var value);
            var scalePresent = !unrelatedInlineScale || element.style.scale.keyword == StyleKeyword.Undefined;

            // Assert
            Assert.That(new[] { read && value.Property == slot && value.Unit == unit && scalePresent ? value.Value : float.NaN,
                    context.HoldsInlineOutsideSwap(slot) ? 1f : 0f, context.HasImportantInlineOutsideSwap(slot) ? 1f : 0f },
                Is.EqualTo(new[] { expected, 0f, 0f }).Within(0.02f));
        }

        [TestCase(0.75f)]
        [TestCase(0.50000006f)]
        public void Given_ANonuniformInlineScale_When_TheProviderSamplesIt_Then_ItDeclinesTheValue(float scaleY)
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.scale = new Scale(new Vector3(0.5f, scaleY, 1f));
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.Scale, LengthUnit.Pixel, out _);

            // Assert
            Assert.That(read, Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Given_OnlyOneInlineBorderEdge_When_TheProviderClassifiesIt_Then_ItFindsThatHolder(int edge)
        {
            // Arrange
            var element = MountInFixedParent();
            SetBorderEdge(element, edge, Color.red);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var holder = context.HoldsInlineOutsideSwap(ArbitraryProperty.BorderColor);

            // Assert
            Assert.That(holder, Is.True);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Given_OneDifferentBorderEdge_When_TheProviderSamplesUniformColor_Then_ItDeclinesTheValue(int edge)
        {
            // Arrange
            var element = MountInFixedParent();
            for (var i = 0; i < 4; i++) SetBorderEdge(element, i, Color.red);
            SetBorderEdge(element, edge, Color.blue);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.BorderColor, LengthUnit.Pixel, out _);

            // Assert
            Assert.That(read, Is.False);
        }

        [Test]
        public void Given_DifferentSlotsOnBothSwapSides_When_TheProviderClassifiesHolders_Then_ItRegistersBothSides()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, "w-[40px]", StyleLayerPriority.Base);
            StyleArbitraryValueResolver.ApplyClassToken(element, "h-[20px]", StyleLayerPriority.Base);
            element.style.opacity = 0.5f;

            // Act
            var context = MotionSlotContext.Read(element, new[] { null, string.Empty, "w-[40px]" }, new[] { "h-[20px]", string.Empty, null }, _ => false);

            // Assert
            Assert.That((context.HoldsInlineOutsideSwap(ArbitraryProperty.Width), context.HoldsInlineOutsideSwap(ArbitraryProperty.Height), context.HoldsInlineOutsideSwap(ArbitraryProperty.Opacity)),
                Is.EqualTo((false, false, true)));
        }

        [Test]
        public void Given_ImportantAndPlainRestingClasses_When_TheProviderReadsThem_Then_ItKeepsTheirPriorityAndOmitsTheSwap()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleClassProjection.Add(element, "opacity-50", StyleLayerPriority.ImportantOf(StyleLayerPriority.Base));
            StyleClassProjection.Add(element, "pt-4", StyleLayerPriority.Base);
            StyleClassProjection.Add(element, "w-40", StyleLayerPriority.Base);

            // Act
            var context = MotionSlotContext.Read(element, new[] { "w-40" }, null, _ => false);
            var resting = string.Join(" ", context.RestingClasses().OrderBy(c => c, System.StringComparer.Ordinal));

            // Assert
            Assert.That(resting, Is.EqualTo("!opacity-50 pt-4"));
        }

        [Test]
        public void Given_ARemovedImportantProjection_When_TheProviderReadsTheReaddedPlainClass_Then_ItDoesNotRestoreTheRemovedPriority()
        {
            // Arrange
            var element = MountInFixedParent();
            var priority = StyleLayerPriority.ImportantOf(StyleLayerPriority.Base);
            StyleClassProjection.Add(element, "w-40", priority);
            StyleClassProjection.Remove(element, "w-40", priority);
            StyleClassProjection.Add(element, "w-40", StyleLayerPriority.Base);

            // Act
            var resting = string.Join(" ", MotionSlotContext.Read(element, null, null, _ => false).RestingClasses());

            // Assert
            Assert.That(resting, Is.EqualTo("w-40"));
        }

        [Test]
        public void Given_ASuppressedImportantClass_When_ItIsReaddedToTheLiveList_Then_TheProviderReadsItWithoutImportantPriority()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleClassProjection.Add(element, "w-40", StyleLayerPriority.ImportantOf(StyleLayerPriority.Base));
            StyleClassProjection.Add(element, "w-80", StyleLayerPriority.ImportantOf(StyleLayerPriority.Hover));
            var suppressed = !element.ClassListContains("w-40");

            // Act
            element.AddToClassList("w-40");
            var resting = string.Join(" ", MotionSlotContext.Read(element, null, null, _ => false).RestingClasses()
                .OrderBy(c => c, System.StringComparer.Ordinal));

            // Assert
            Assert.That((suppressed, resting), Is.EqualTo((true, "!w-80 w-40")));
        }

        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void Given_AnImportantInlineWidth_When_TheProviderClassifiesASwap_Then_OnlyASwappedBaseLayerIsReleased(bool swapped, bool hover, bool expected)
        {
            // Arrange
            var element = MountInFixedParent();
            var priority = StyleLayerPriority.ImportantOf(hover ? StyleLayerPriority.Hover : StyleLayerPriority.Base);
            StyleArbitraryValueResolver.ApplyClassToken(element, "w-[40px]", priority);
            var context = MotionSlotContext.Read(element, swapped ? new[] { "!w-[40px]" } : null, new[] { "h-[20px]" }, _ => false);

            // Act
            var width = context.HasImportantInlineOutsideSwap(ArbitraryProperty.Width);
            var height = context.HasImportantInlineOutsideSwap(ArbitraryProperty.Height);

            // Assert
            Assert.That((width, height), Is.EqualTo((expected, false)));
        }

        [TestCase("running", 160f, false)]
        [TestCase("running", 80f, false, LengthUnit.Percent)]
        [TestCase("restarted", 150f, false)]
        [TestCase("completed", 80f, true)]
        [TestCase("cancelled", 80f, true)]
        [TestCase("detached", 80f, true)]
        [TestCase("unrelated", 80f, true)]
        public void Given_ANativeTransitionBoundary_When_TheProviderSamplesWidth_Then_ItDistinguishesThePaintFromAPlainHolder(string phase, float expected, bool holder, LengthUnit unit = LengthUnit.Pixel)
        {
            // Arrange
            var element = MountInFixedParent();
            var parent = element.parent;
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            if (phase == "unrelated")
            {
                element.style.height = 100f;
                ForcePanelUpdate(element.panel);
                ConfigureNativeTransition(element, "height");
                element.style.height = 20f;
                ForcePanelUpdate(element.panel);
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            }
            else StartNativeWidth(element);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            if (phase == "completed")
            {
                now += 2;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
            }
            if (phase == "cancelled")
            {
                element.style.transitionProperty = new List<StylePropertyName> { new("none") };
                ForcePanelUpdate(element.panel);
            }
            if (phase == "detached")
            {
                element.RemoveFromHierarchy();
                parent.Add(element);
                ForcePanelUpdate(element.panel);
            }
            if (phase == "restarted")
            {
                element.style.width = 120f;
                ForcePanelUpdate(element.panel);
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                now += 0.5;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
            }
            var boundaryHolder = MotionSlotContext.Read(element, null, null, _ => false).HoldsInlineOutsideSwap(ArbitraryProperty.Width);
            if (holder)
            {
                if (phase is "completed" or "detached")
                {
                    element.style.transitionDuration = new List<TimeValue> { new(0f, TimeUnit.Second) };
                    ForcePanelUpdate(element.panel);
                }
                element.style.width = 80f;
                ForcePanelUpdate(element.panel);
            }
            var painted = element.resolvedStyle.width;
            var unrelatedRunning = phase == "unrelated" && element.resolvedStyle.height != element.style.height.value.value;
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.Width, unit, out var value);

            // Assert
            Assert.That(new[] { painted, read && value.Unit == unit ? value.Value : float.NaN, context.HoldsInlineOutsideSwap(ArbitraryProperty.Width) ? 1f : 0f, unrelatedRunning ? 1f : 0f, boundaryHolder ? 1f : 0f },
                Is.EqualTo(new[] { unit == LengthUnit.Percent ? expected * 2f : expected, expected, holder ? 1f : 0f, phase == "unrelated" ? 1f : 0f, holder && phase != "unrelated" ? 1f : 0f }).Within(0.02f));
        }

        [Test]
        public void Given_ACallbackOwnedFrameDuringNativeActivity_When_TheProviderSamplesIt_Then_ItReadsTheInlineFrame()
        {
            // Arrange
            var element = MountInFixedParent();
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            StartNativeWidth(element);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = element.resolvedStyle.width;
            element.style.width = 26f;
            var context = MotionSlotContext.Read(element, null, null, slot => slot == ArbitraryProperty.Width);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.Width, LengthUnit.Pixel, out var value);

            // Assert
            Assert.That(new[] { paint, read ? value.Value : float.NaN, context.HoldsInlineOutsideSwap(ArbitraryProperty.Width) ? 1f : 0f },
                Is.EqualTo(new[] { 160f, 26f, 0f }).Within(0.02f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AClippedLayoutSlot_When_TheProviderSamplesIt_Then_ItReadsTheLayoutOwnersCurrentValue(bool nativeTransition)
        {
            // Arrange
            var parent = MountInFixedParent().parent;
            _mounted = V.Mount(parent, V.Div(name: "provider-clipped", className: "clip-path-[inset(0)]"));
            var element = parent.Q<VisualElement>("provider-clipped");
            ForcePanelUpdate(element.panel);
            var owner = ClipPathLayoutBox.Of(element);
            var now = 100.0;
            if (nativeTransition)
            {
                EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
                StartNativeWidth(owner);
                now += 0.5;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
            }
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.Width, LengthUnit.Pixel, out var value);

            // Assert
            Assert.That(new[] { ReferenceEquals(owner, element) ? 0f : 1f, owner.resolvedStyle.width, read ? value.Value : float.NaN },
                Is.EqualTo(new[] { 1f, nativeTransition ? 160f : 200f, nativeTransition ? 160f : 200f }).Within(0.02f));
        }

        [TestCase(ArbitraryProperty.TextColor, "text-white")]
        [TestCase(ArbitraryProperty.BackgroundColor, "bg-white")]
        [TestCase(ArbitraryProperty.BorderColor, "border-white")]
        public void Given_AComputedColorWithoutAnInlineOverride_When_TheProviderSamplesIt_Then_ItReturnsTheStylesheetColor(ArbitraryProperty slot, string cls)
        {
            // Arrange
            var element = MountInFixedParent();
            element.AddToClassList(cls);
            ForcePanelUpdate(element.panel);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(slot, LengthUnit.Percent, out var value);

            // Assert
            Assert.That((read, value.Property, value.Color), Is.EqualTo((true, slot, Color.white)));
        }

        [Test]
        public void Given_ANonnumericComputedMaximum_When_TheProviderSamplesIt_Then_ItDeclinesTheValue()
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.maxWidth = StyleKeyword.None;
            ForcePanelUpdate(element.panel);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.MaxWidth, LengthUnit.Pixel, out _);

            // Assert
            Assert.That((element.resolvedStyle.maxWidth.keyword, read), Is.EqualTo((StyleKeyword.None, false)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_ANativeOpacityFrame_When_TheProviderSamplesIt_Then_ItAcceptsOnlyTheFinitePaint(bool infinite)
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.opacity = 0.5f;
            ForcePanelUpdate(element.panel);
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            ConfigureNativeTransition(element, "opacity");
            element.style.opacity = infinite ? float.PositiveInfinity : 0.25f;
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = element.resolvedStyle.opacity;
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.Opacity, LengthUnit.Pixel, out var value);

            // Assert
            Assert.That(new[] { infinite ? (float.IsPositiveInfinity(paint) ? 1f : 0f) : paint,
                    read ? 1f : 0f, infinite ? 0f : value.Property == ArbitraryProperty.Opacity && value.Unit == LengthUnit.Pixel
                        ? value.Value : float.NaN,
                    context.HoldsInlineOutsideSwap(ArbitraryProperty.Opacity) ? 1f : 0f },
                Is.EqualTo(new[] { infinite ? 1f : 0.4375f, infinite ? 0f : 1f,
                    infinite ? 0f : 0.4375f, 0f }).Within(0.02f));
        }

        [Test]
        public void Given_AnUnavailableNativeBorderIdentity_When_TheProviderSamplesIt_Then_ItUsesTheInlineTarget()
        {
            // Arrange
            var element = MountInFixedParent();
            for (var i = 0; i < 4; i++) SetBorderEdge(element, i, Color.red);
            ForcePanelUpdate(element.panel);
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            ConfigureNativeTransition(element, "border-color");
            for (var i = 0; i < 4; i++) SetBorderEdge(element, i, Color.blue);
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = element.resolvedStyle.borderTopColor;
            var ids = (object[])typeof(MotionSlotContext).GetField("s_nativePropertyIds",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            var saved = ids.ToArray();
            var edges = new[] { StyleLonghand.BorderTopColor, StyleLonghand.BorderRightColor,
                StyleLonghand.BorderBottomColor, StyleLonghand.BorderLeftColor };
            var native = (bool)EngineMember.HasRunningStyleAnimation.ResolveMethod()
                .Invoke(element, new[] { saved[(int)StyleLonghand.BorderTopColor] });
            var context = MotionSlotContext.Read(element, null, null, _ => false);
            bool read;
            bool holder;
            ArbitraryStyle value;

            // Act
            try
            {
                foreach (var edge in edges) ids[(int)edge] = null;
                read = context.TryReadCurrent(ArbitraryProperty.BorderColor, LengthUnit.Pixel, out value);
                holder = context.HoldsInlineOutsideSwap(ArbitraryProperty.BorderColor);
            }
            finally
            {
                System.Array.Copy(saved, ids, saved.Length);
            }

            // Assert
            Assert.That(new[] { native ? 1f : 0f, paint.r, paint.b,
                    read ? value.Color.r : float.NaN, read ? value.Color.b : float.NaN, holder ? 1f : 0f },
                Is.EqualTo(new[] { 1f, 0.75f, 0.25f, 0f, 1f, 1f }).Within(0.02f));
        }

        [TestCase(ArbitraryProperty.TextColor, "color")]
        [TestCase(ArbitraryProperty.BackgroundColor, "background-color")]
        public void Given_ARunningNativeColor_When_TheProviderSamplesIt_Then_ItReadsThePaintRegardlessOfLengthUnit(ArbitraryProperty slot, string property)
        {
            // Arrange
            var element = MountInFixedParent();
            if (slot == ArbitraryProperty.TextColor) element.style.color = Color.red;
            else element.style.backgroundColor = Color.red;
            ForcePanelUpdate(element.panel);
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            ConfigureNativeTransition(element, property);
            if (slot == ArbitraryProperty.TextColor) element.style.color = Color.blue;
            else element.style.backgroundColor = Color.blue;
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = slot == ArbitraryProperty.TextColor ? element.resolvedStyle.color : element.resolvedStyle.backgroundColor;
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(slot, LengthUnit.Percent, out var value);

            // Assert
            Assert.That(new[] { paint.r, paint.b, read ? value.Color.r : float.NaN, read ? value.Color.b : float.NaN,
                    context.HoldsInlineOutsideSwap(slot) ? 1f : 0f },
                Is.EqualTo(new[] { 0.75f, 0.25f, 0.75f, 0.25f, 0f }).Within(0.02f));
        }

        [TestCase(false)]
        [TestCase(true)]
        [TestCase(true, true)]
        public void Given_ANativeBorderFrameWithUniformPaint_When_TheProviderSamplesIt_Then_ItSeparatesTheSampleFromInactiveEdgeHolders(
            bool allEdges, bool drivenByPlay = false)
        {
            // Arrange
            var element = MountInFixedParent();
            for (var i = 0; i < 4; i++) SetBorderEdge(element, i, Color.red);
            ForcePanelUpdate(element.panel);
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            ConfigureNativeTransition(element, allEdges ? "border-color" : "border-top-color");
            for (var i = 0; i < (allEdges ? 4 : 1); i++) SetBorderEdge(element, i, Color.blue);
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = element.resolvedStyle.borderTopColor;
            if (!allEdges)
            {
                for (var i = 1; i < 4; i++) SetBorderEdge(element, i, paint);
                ForcePanelUpdate(element.panel);
            }
            var context = MotionSlotContext.Read(element, null, null, slot => drivenByPlay && slot == ArbitraryProperty.BorderColor);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.BorderColor, LengthUnit.Pixel, out var value);

            // Assert
            Assert.That(new[] { paint.r, paint.b, read ? value.Color.r : float.NaN, read ? value.Color.b : float.NaN,
                    context.HoldsInlineOutsideSwap(ArbitraryProperty.BorderColor) ? 1f : 0f },
                Is.EqualTo(new[] { 0.75f, 0.25f, drivenByPlay ? 0f : 0.75f, drivenByPlay ? 1f : 0.25f,
                    allEdges ? 0f : 1f }).Within(0.02f));
        }

        [Test]
        public void Given_ANonuniformNativeScaleWithAUniformInlineTarget_When_TheProviderSamplesIt_Then_ItDeclinesThePaint()
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.scale = new Scale(new Vector3(0.5f, 0.75f, 1f));
            ForcePanelUpdate(element.panel);
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            ConfigureNativeTransition(element, "scale");
            element.style.scale = new Scale(new Vector3(1.5f, 1.5f, 1f));
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = element.resolvedStyle.scale.value;
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.Scale, LengthUnit.Pixel, out _);

            // Assert
            Assert.That(read ? new[] { float.NaN, float.NaN } : new[] { paint.x, paint.y },
                Is.EqualTo(new[] { 0.75f, 0.9375f }).Within(0.02f));
        }

        private VisualElement MountInFixedParent()
        {
            var parent = new VisualElement();
            parent.style.width = 200f;
            parent.style.height = 100f;
            var element = new VisualElement();
            parent.Add(element);
            _window.rootVisualElement.Add(parent);
            EditorPanelTestHelpers.ForcePanelUpdate(element.panel);
            return element;
        }

        private static void SetBorderEdge(VisualElement element, int edge, Color color)
        {
            switch (edge)
            {
                case 0: element.style.borderTopColor = color; break;
                case 1: element.style.borderRightColor = color; break;
                case 2: element.style.borderBottomColor = color; break;
                case 3: element.style.borderLeftColor = color; break;
            }
        }

        private static void ConfigureNativeTransition(VisualElement element, string property)
        {
            element.style.transitionProperty = new List<StylePropertyName> { new(property) };
            element.style.transitionDuration = new List<TimeValue> { new(2f, TimeUnit.Second) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.Linear) };
            EditorPanelTestHelpers.ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
        }

        private static void StartNativeWidth(VisualElement element)
        {
            element.style.width = 200f;
            EditorPanelTestHelpers.ForcePanelUpdate(element.panel);
            ConfigureNativeTransition(element, "width");
            element.style.width = 40f;
            EditorPanelTestHelpers.ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
        }
    }
}
