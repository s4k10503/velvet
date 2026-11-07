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
        [TestCase("p-guttertest", "p-[length:10px]")]
        [TestCase("p-4", "p-[length:12px]")]
        [TestCase("w-1.5", "w-[length:4.5px]")]
        [TestCase("-mt-2", "-mt-[length:6px]")]
        [TestCase("p-7", "p-[length:10px]")]
        [TestCase("-mt-guttertest", "-mt-[length:10px]")]
        [TestCase("max-h-4", "max-h-[length:12px]")]
        [TestCase("max-w-guttertest", "max-w-[length:10px]")]
        [TestCase("translate-x-2", "translate-x-[length:6px]")]
        [TestCase("w-widetest", "w-[length:var(--container-widetest)]")]
        [TestCase("text-sizetest/7", "text-[length:var(--text-sizetest)]/7")]
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
            // Arrange — the window's mount reads the sheet; the other mount's root is outside it.
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
            }
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

        [TestCase(StyleVariantKind.Sm, 640f)]
        [TestCase(StyleVariantKind.Md, 768f)]
        [TestCase(StyleVariantKind.Lg, 1024f)]
        [TestCase(StyleVariantKind.Xl, 1280f)]
        [TestCase(StyleVariantKind.Xxl, 1536f)]
        public void Given_NoThemeScope_When_ADefaultBreakpointIsRead_Then_ItIsTheTailwindThreshold(StyleVariantKind kind, float px)
        {
            // Act
            var read = StyleVariantClass.BreakpointPx(kind);

            // Assert
            Assert.That(read, Is.EqualTo(px));
        }

        [TestCase("sm:p-2", StyleVariantKind.Sm)]
        [TestCase("md:p-2", StyleVariantKind.Md)]
        [TestCase("lg:p-2", StyleVariantKind.Lg)]
        [TestCase("xl:p-2", StyleVariantKind.Xl)]
        [TestCase("2xl:p-2", StyleVariantKind.Xxl)]
        public void Given_ADefaultBreakpointToken_When_Parsed_Then_ItIsThatKind(string token, StyleVariantKind expected)
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            StyleVariantKind kind;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                StyleVariantClass.TryParse(token, out kind, out _, out _);
            }

            // Assert
            Assert.That(kind, Is.EqualTo(expected));
        }

        [TestCase("3xltest:p-2", "@min-3xltest/*viewport")]
        [TestCase("max-3xltest:p-2", "@max-3xltest/*viewport")]
        [TestCase("max-md:p-2", "@max-md/*viewport")]
        [TestCase("min-lg:p-2", "@min-lg/*viewport")]
        public void Given_ABreakpointASheetDeclaresOrAFloorOrCeilingOnOne_When_Parsed_Then_ItIsAQueryOnTheViewport(string token, string expected)
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            string name;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                StyleVariantClass.TryParse(token, out _, out name, out _);
            }

            // Assert
            Assert.That(name, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABreakpointASheetDeclares_When_ItsQueryIsRead_Then_TheThresholdIsTheDeclaredLength()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            float px;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                StyleContainerQuery.TryParse("@min-3xltest/*viewport", out var query);
                px = query.Px;
            }

            // Assert
            Assert.That(px, Is.EqualTo(1920f));
        }

        [Test]
        public void Given_ABreakpointNoSheetDeclares_When_ItsTokenIsParsed_Then_ItIsNotAVariant()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            bool parsed;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                parsed = StyleVariantClass.TryParse("3xlnever:p-2", out _, out _, out _);
            }

            // Assert
            Assert.That(parsed, Is.False);
        }

        [Test]
        public void Given_ACustomBreakpointToken_When_ParsedOutsideEveryScope_Then_ItIsNotAVariant()
        {
            // Act
            var parsed = StyleVariantClass.TryParse("3xltest:p-2", out _, out _, out _);

            // Assert
            Assert.That(parsed, Is.False);
        }

        [Test]
        public void Given_AReaderHoldingOnlyAnElement_When_ItAsksOutsideAPass_Then_ItReadsTheElementsMountScope()
        {
            // Arrange
            AttachSheets(false);
            var leaf = Mount("flex");

            // Act
            string normalized;
            using (StyleThemeTokens.Use(null, leaf))
            {
                normalized = StyleThemeClass.Normalize("bg-brandtest");
            }

            // Assert
            Assert.That(normalized, Is.EqualTo("bg-[color:var(--color-brandtest)]"));
        }

        [Test]
        public void Given_ABreakpointASheetNames_When_ItsRankIsRead_Then_ItIsBelowEveryDefaultBreakpoint()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            long rank;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                rank = StyleContainerQuery.RankOf("@min-xstest/*viewport");
            }

            // Assert
            Assert.That(rank, Is.LessThan(StyleLayerPriority.ResponsiveSm));
        }

        [Test]
        public void Given_ACeilingOnABreakpoint_When_ItsRankIsRead_Then_ItIsBelowEveryFloor()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            long rank;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                rank = StyleContainerQuery.RankOf("@max-md/*viewport");
            }

            // Assert
            Assert.That(rank, Is.LessThan(StyleLayerPriority.ViewportMin));
        }

        [Test]
        public void Given_AFloorBetweenTwoDefaultBreakpoints_When_ItsRankIsRead_Then_ItIsTheLowerOnes()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            long rank;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                rank = StyleContainerQuery.RankOf("@min-[1100px]/*viewport");
            }

            // Assert
            Assert.That(rank, Is.EqualTo(StyleLayerPriority.ResponsiveLg));
        }

        [Test]
        public void Given_ABreakpointAboveTheLargestDefault_When_ItsRankIsRead_Then_ItIsBelowEveryContainerQuery()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            long rank;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                rank = StyleContainerQuery.RankOf("@min-3xltest/*viewport");
            }

            // Assert
            Assert.That(rank, Is.LessThan(StyleLayerPriority.ContainerMax));
        }

        [Test]
        public void Given_TwoCeilings_When_TheirOrderKeysAreCompared_Then_TheLargerComesFirst()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            int order;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                order = string.CompareOrdinal(StyleContainerQuery.OrderKeyOf("@max-lg/*viewport"),
                    StyleContainerQuery.OrderKeyOf("@max-md/*viewport"));
            }

            // Assert
            Assert.That(order, Is.LessThan(0));
        }

        [TestCase("shadow-md")]
        [TestCase("font-bold")]
        [TestCase("tracking-wide")]
        [TestCase("leading-tight")]
        [TestCase("blur-md")]
        [TestCase("drop-shadow-lg")]
        [TestCase("p-4")]
        public void Given_EveryNamespaceSetToInitialAndNoSpacing_When_ABuiltInScaleUtilityIsRead_Then_ItIsRemoved(string className)
        {
            // Arrange
            VelvetStyleUtilities.AttachTo(_window.rootVisualElement);
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
        public void Given_EveryNamespaceSetToInitial_When_ADefaultBreakpointTokenIsParsed_Then_ItIsNotAVariant()
        {
            // Arrange
            VelvetStyleUtilities.AttachTo(_window.rootVisualElement);
            Attach(AllClearedSheet);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            bool parsed;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                parsed = StyleVariantClass.TryParse("md:p-2", out _, out _, out _);
            }

            // Assert
            Assert.That(parsed, Is.False);
        }

        [Test]
        public void Given_AColourDeclaredAfterTheColorNamespaceWasSetToInitial_When_ABundledOpacityFormIsMounted_Then_NoColourIsApplied()
        {
            // Arrange
            AttachSheets(true);
            Attach(ClearedSheet);

            // Act
            var leaf = Mount("bg-primary/50");

            // Assert
            Assert.That(leaf.style.backgroundColor.value, Is.EqualTo(new Color(0f, 0f, 0f, 0f)));
        }

        [Test]
        public void Given_ASheetAttachedAfterAMount_When_TheScopesPollRuns_Then_TheMountIsToldItsSheetsChanged()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div());
            var context = _mounted.Root.Reconciler.Context;
            using (StyleThemeTokens.Pass(context))
            {
            }
            var told = false;
            System.Action<ReconcilerContext> listener = changed => told |= ReferenceEquals(changed, context);
            StyleThemeTokens.ScopeChanged += listener;
            try
            {
                Attach(TokenSheet);

                // Act
                EditorPanelTestHelpers.DriveSchedulerOnce(_window.rootVisualElement.panel);
            }
            finally
            {
                StyleThemeTokens.ScopeChanged -= listener;
            }

            // Assert
            Assert.That(told, Is.True);
        }

        [Test]
        public void Given_AClassListThatStoppedNamingAThemeClass_When_TheSheetsChange_Then_ItIsNotConfiguredAgain()
        {
            // Arrange
            AttachSheets(false);
            var leaf = Mount("bg-brandtest");
            var context = _mounted.Root.Reconciler.Context;
            var patcher = new FiberNodePatcher(context);
            patcher.ApplyVariantManipulators(leaf, new[] { "flex" });

            // Act
            StyleThemeTokens.Untrack(leaf);
            patcher.ReleaseCustomPropertyTokens(leaf);
            patcher.ApplyVariantManipulators(leaf, new[] { "flex" });

            // Assert
            Assert.That(context.CustomPropertyClasses.ContainsKey(leaf), Is.False);
        }

        [Test]
        public void Given_AMountDisposedAndMountedAgainOnTheSameRoot_When_TheSheetsAreRead_Then_TheNewMountReadsThem()
        {
            // Arrange
            AttachSheets(false);
            var first = V.Mount(_window.rootVisualElement, V.Div());
            first.Dispose();
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            var normalized = NormalizeInMount("bg-brandtest");

            // Assert
            Assert.That(normalized, Is.EqualTo("bg-[color:var(--color-brandtest)]"));
        }

        [Test]
        public void Given_SmMovedAboveMd_When_TheirRanksAreRead_Then_SmRanksAfterMd()
        {
            // Arrange
            AttachSheets(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            bool smAfterMd;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                smAfterMd = StyleLayerPriority.ForVariant(StyleVariantKind.Sm) > StyleLayerPriority.ForVariant(StyleVariantKind.Md);
            }

            // Assert
            Assert.That(smAfterMd, Is.True);
        }
    }
}
