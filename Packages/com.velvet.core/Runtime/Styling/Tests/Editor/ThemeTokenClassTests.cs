using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the utilities a theme variable generates: a custom property a stylesheet declares in a Tailwind v4
    /// theme namespace (<c>--color-*</c>, <c>--radius-*</c>, …) makes the utilities of that namespace, and the
    /// built-in semantic colours take the families the palette names do.
    /// </summary>
    internal sealed class ThemeTokenClassTests : PanelTestBase
    {
        private const string TokenSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ThemeTokenSheet.uss";
        private const string AllClearedSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ThemeTokenAllClearedSheet.uss";
        private const string ClearedSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ThemeTokenClearedSheet.uss";

        private void Attach(string path)
            => _window.rootVisualElement.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(path));

        private void AttachSheets(bool bundled)
        {
            Attach(TokenSheet);
            if (bundled)
            {
                VelvetStyleUtilities.AttachTo(_window.rootVisualElement);
            }
        }

        // What Normalize answers inside a pass of a mount onto the window's root, which reads the sheets attached to it.
        private string Normalize(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div());
            return NormalizeInMount(className);
        }

        private string NormalizeInMount(string className)
        {
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                return StyleThemeClass.Normalize(className);
            }
        }

        private VisualElement Mount(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: "theme-token-host", children: new VNode?[]
            {
                V.Div(name: "leaf", className: className),
            }));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }

        private static string Rgb(StyleColor colour)
            => colour.keyword != StyleKeyword.Undefined ? colour.keyword.ToString()
                : ColorUtility.ToHtmlStringRGB(colour.value);

        [TestCase("bg-brandtest", "bg-[color:var(--color-brandtest)]")]
        [TestCase("bg-brandtest/50", "bg-[color:var(--color-brandtest)]/50")]
        [TestCase("text-brandtest", "text-[color:var(--color-brandtest)]")]
        [TestCase("border-brandtest", "border-[color:var(--color-brandtest)]")]
        [TestCase("border-b-brandtest", "border-b-[color:var(--color-brandtest)]")]
        [TestCase("border-bs-brandtest", "border-bs-[color:var(--color-brandtest)]")]
        [TestCase("ring-brandtest", "ring-[color:var(--color-brandtest)]")]
        [TestCase("ring-offset-brandtest", "ring-offset-[color:var(--color-brandtest)]")]
        [TestCase("outline-brandtest", "outline-[color:var(--color-brandtest)]")]
        [TestCase("divide-brandtest", "divide-[color:var(--color-brandtest)]")]
        [TestCase("shadow-brandtest", "shadow-[color:var(--color-brandtest)]")]
        [TestCase("drop-shadow-brandtest", "drop-shadow-[color:var(--color-brandtest)]")]
        [TestCase("from-brandtest", "from-[color:var(--color-brandtest)]")]
        [TestCase("caret-brandtest", "caret-[color:var(--color-brandtest)]")]
        [TestCase("rounded-pilltest", "rounded-[var(--radius-pilltest)]")]
        [TestCase("rounded-tl-pilltest", "rounded-tl-[var(--radius-pilltest)]")]
        [TestCase("p-guttertest", "p-[length:var(--spacing-guttertest)]")]
        [TestCase("p-4", "p-[length:calc(var(--spacing)*4)]")]
        [TestCase("w-1.5", "w-[length:calc(var(--spacing)*1.5)]")]
        [TestCase("-mt-2", "-mt-[length:calc(var(--spacing)*-2)]")]
        [TestCase("max-w-widetest", "max-w-[length:var(--container-widetest)]")]
        [TestCase("inset-shadow-glowtest", "inset-shadow-[var(--inset-shadow-glowtest)]")]
        public void Given_AProjectTokenInANamespace_When_AUtilityOfThatNamespaceIsRead_Then_ItIsTheBracketedUtilityReadingTheVariable(
            string className, string expected)
        {
            // Arrange
            AttachSheets(false);

            // Act
            var normalized = Normalize(className);

            // Assert
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [TestCase("bg-nevertokenx")]
        [TestCase("bg-nevertokenx/50")]
        [TestCase("border-b-nevertokenx")]
        [TestCase("rounded-nevertokenx")]
        public void Given_NoDeclaredToken_When_AUtilityNamesIt_Then_TheClassIsNotAThemeClass(string className)
        {
            // Arrange
            AttachSheets(true);

            // Act
            var normalized = Normalize(className);

            // Assert
            Assert.That(normalized, Is.Null);
        }

        [TestCase("border-b-primary", "border-b-[color:var(--color-primary)]")]
        [TestCase("ring-primary", "ring-[color:var(--color-primary)]")]
        [TestCase("bg-primary/50", "bg-[color:var(--color-primary)]/50")]
        public void Given_ABuiltInSemanticColour_When_AFormItsRulesDoNotGiveIsRead_Then_ItIsTheBracketedUtilityReadingTheVariable(
            string className, string expected)
        {
            // Arrange
            AttachSheets(true);

            // Act
            var normalized = Normalize(className);

            // Assert
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [TestCase("bg-surface")]
        [TestCase("text-subtle")]
        [TestCase("border-b-red-500")]
        public void Given_AUtilityTheBundledSheetOrThePaletteAlreadyResolves_When_Read_Then_ItIsLeftToThem(string className)
        {
            // Arrange
            AttachSheets(true);

            // Act
            var normalized = Normalize(className);

            // Assert
            Assert.That(normalized, Is.Null);
        }

        [Test]
        public void Given_ABottomBorderColouredByAProjectToken_When_Mounted_Then_OnlyTheBottomEdgeTakesIt()
        {
            // Arrange
            AttachSheets(false);

            // Act
            var leaf = Mount("border-2 border-slate-300 border-b-brandtest");

            // Assert
            Assert.That(new[] { Rgb(leaf.style.borderBottomColor), Rgb(leaf.style.borderTopColor) },
                Is.EqualTo(new[] { "FF0066", "CAD5E2" }));
        }

        [Test]
        public void Given_AThemeClassRedeclaringTheToken_When_Mounted_Then_TheUtilityDrawsTheRedeclaredValue()
        {
            // Arrange
            AttachSheets(false);

            // Act
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "theme-token-host theme-token-alt", children: new VNode?[]
                {
                    V.Div(name: "leaf", className: "bg-brandtest"),
                }));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);

            // Assert
            Assert.That(Rgb(leaf.style.backgroundColor), Is.EqualTo("00FF00"));
        }

        [Test]
        public void Given_ABuiltInSemanticColourOnABorderSide_When_Mounted_Then_TheSideTakesTheTokenAndTheOthersKeepTheShorthand()
        {
            // Arrange
            AttachSheets(true);

            // Act
            var leaf = Mount("border-2 border-slate-300 border-b-primary");

            // Assert
            Assert.That(new[] { Rgb(leaf.style.borderBottomColor), Rgb(leaf.style.borderTopColor) },
                Is.EqualTo(new[] { "155DFC", "CAD5E2" }));
        }

        [Test]
        public void Given_AProjectRadiusToken_When_Mounted_Then_TheCornersTakeIt()
        {
            // Arrange
            AttachSheets(false);

            // Act
            var leaf = Mount("rounded-pilltest");

            // Assert
            Assert.That(leaf.style.borderTopLeftRadius.value.value, Is.EqualTo(40f));
        }

        [Test]
        public void Given_ASheetAttachedAfterTheMount_When_APassReadsTheSheets_Then_ItsTokenGeneratesUtilities()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div());
            Attach(TokenSheet);

            // Act
            var normalized = NormalizeInMount("bg-brandtest");

            // Assert
            Assert.That(normalized, Is.EqualTo("bg-[color:var(--color-brandtest)]"));
        }

        [Test]
        public void Given_ATokenDeclaredOnOneMountsRoot_When_AnotherMountsPassReadsIt_Then_TheClassIsNotAThemeClass()
        {
            // Arrange
            AttachSheets(false);
            var otherMount = V.Mount(new VisualElement(), V.Div());

            // Act
            string normalized;
            using (StyleThemeTokens.Pass(otherMount.Root.Reconciler.Context))
            {
                normalized = StyleThemeClass.Normalize("bg-brandtest");
            }
            otherMount.Dispose();

            // Assert
            Assert.That(normalized, Is.Null);
        }

        [Test]
        public void Given_ASheetDetachedAfterAMount_When_APassReadsTheSheets_Then_ItsTokenGeneratesNothing()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());
            _window.rootVisualElement.styleSheets.Clear();

            // Act
            var normalized = NormalizeInMount("bg-brandtest");

            // Assert
            Assert.That(normalized, Is.Null);
        }

        [TestCase("bg-primary/50")]
        [TestCase("border-b-primary")]
        [TestCase("bg-red-500")]
        [TestCase("text-white")]
        public void Given_TheColorNamespaceSetToInitial_When_ABundledColourUtilityIsRead_Then_ItIsRemoved(string className)
        {
            // Arrange
            AttachSheets(true);
            Attach(ClearedSheet);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            bool cleared;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                cleared = StyleThemeClass.IsClearedBundled(className);
            }

            // Assert
            Assert.That(cleared, Is.True);
        }

        [Test]
        public void Given_TheColorNamespaceSetToInitial_When_AProjectColourIsRead_Then_ItStillGeneratesUtilities()
        {
            // Arrange
            AttachSheets(true);
            Attach(ClearedSheet);

            // Act
            var normalized = Normalize("border-b-keeptest");

            // Assert
            Assert.That(normalized, Is.EqualTo("border-b-[color:var(--color-keeptest)]"));
        }

        [Test]
        public void Given_TheColorNamespaceSetToInitial_When_ABundledClassIsMounted_Then_ItIsNotOnTheClassList()
        {
            // Arrange
            AttachSheets(true);
            Attach(ClearedSheet);

            // Act
            var leaf = Mount("bg-red-500");

            // Assert
            Assert.That(leaf.ClassListContains("bg-red-500"), Is.False);
        }

        [Test]
        public void Given_TheColorNamespaceSetToInitial_When_APaletteOpacityFormIsMounted_Then_NoColourIsApplied()
        {
            // Arrange
            AttachSheets(true);
            Attach(ClearedSheet);

            // Act
            var leaf = Mount("bg-red-500/50");

            // Assert
            Assert.That(leaf.style.backgroundColor.value, Is.EqualTo(new Color(0f, 0f, 0f, 0f)));
        }

        [Test]
        public void Given_ASpacingMultiplierAndANamedSpacing_When_Mounted_Then_TheNumberScalesAndTheNameKeepsItsValue()
        {
            // Arrange
            AttachSheets(false);

            // Act
            var leaf = Mount("p-4 pt-guttertest");

            // Assert
            Assert.That(new[] { leaf.style.paddingLeft.value.value, leaf.style.paddingTop.value.value },
                Is.EqualTo(new[] { 12f, 10f }));
        }

        [TestCase("bg-red-500")]
        [TestCase("rounded-lg")]
        [TestCase("bg-primary/50")]
        public void Given_EveryNamespaceSetToInitial_When_ABundledUtilityIsRead_Then_ItIsRemoved(string className)
        {
            // Arrange
            AttachSheets(true);
            Attach(AllClearedSheet);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            bool cleared;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                cleared = StyleThemeClass.IsClearedBundled(className);
            }

            // Assert
            Assert.That(cleared, Is.True);
        }

        [Test]
        public void Given_EveryNamespaceSetToInitial_When_AProjectColourIsRead_Then_ItStillGeneratesUtilities()
        {
            // Arrange
            AttachSheets(true);
            Attach(AllClearedSheet);

            // Act
            var normalized = Normalize("bg-brandtest");

            // Assert
            Assert.That(normalized, Is.EqualTo("bg-[color:var(--color-brandtest)]"));
        }

        [Test]
        public void Given_AContainerSizeDeclared_When_ItsQueryIsRead_Then_TheThresholdIsTheDeclaredLength()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            float px;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                StyleContainerQuery.TryParse("@widetest", out var query);
                px = query.Px;
            }

            // Assert
            Assert.That(px, Is.EqualTo(1600f));
        }

        [Test]
        public void Given_ABreakpointRedeclared_When_ItsThresholdIsRead_Then_ItIsTheDeclaredLength()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            float px;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                px = StyleVariantClass.BreakpointPx(StyleVariantKind.Sm);
            }

            // Assert
            Assert.That(px, Is.EqualTo(800f));
        }

        [Test]
        public void Given_ABreakpointNoSheetRedeclares_When_ItsThresholdIsRead_Then_ItIsTheDefault()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            float px;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                px = StyleVariantClass.BreakpointPx(StyleVariantKind.Md);
            }

            // Assert
            Assert.That(px, Is.EqualTo(768f));
        }

        [Test]
        public void Given_AnAnimationDeclaredAsAThemeVariable_When_TheClassIsRead_Then_ItIsALoopOfTheDeclaredDuration()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());
            VelvetKeyframes.Register("kf-theme", new[] { new KeyframeStop(0.5f) { RotateDeg = 10f } });
            try
            {
                // Act
                float seconds;
                using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
                {
                    StyleAnimateClass.TryExtract(new[] { "animate-wiggletest" }, out var spec);
                    seconds = spec.DurationSec;
                }

                // Assert
                Assert.That(seconds, Is.EqualTo(2f));
            }
            finally
            {
                VelvetKeyframes.Unregister("kf-theme");
            }
        }
    }
}
