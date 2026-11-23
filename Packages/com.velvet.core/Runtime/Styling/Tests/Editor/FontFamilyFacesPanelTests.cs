using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Velvet.Editor;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which face a weight class resolves on a panel: the family of a font an author's inline style gives
    /// an element above the tree rather than <see cref="VelvetFonts.DefaultFamily"/>, that font alone where no family
    /// holds it, the family of a font an author's rule gives the element itself or an element between it and an ancestor
    /// naming a family, and the asset a changed registry gives each element whose font a class resolved. Reads the asset
    /// Velvet wrote inline on the label. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class FontFamilyFacesPanelTests
    {
        // Created once and never destroyed, as DefaultFamilyScopePanelTests' are, for the reason FontRelativeUnitPanelTests gives.
        private static FontAsset s_sans;
        private static FontAsset s_sansBold;
        private static FontAsset s_serif;
        private static FontAsset s_serifBold;
        private static FontAsset s_serifBoldReplacement;
        private static FontAsset s_unregistered;

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            VelvetFonts.Clear();
            s_sans = s_sans != null ? s_sans : (s_sans = NewAsset());
            s_sansBold = s_sansBold != null ? s_sansBold : (s_sansBold = NewAsset());
            s_serif = s_serif != null ? s_serif : (s_serif = NewAsset());
            s_serifBold = s_serifBold != null ? s_serifBold : (s_serifBold = NewAsset());
            s_serifBoldReplacement = s_serifBoldReplacement != null
                ? s_serifBoldReplacement
                : (s_serifBoldReplacement = NewAsset());
            s_unregistered = s_unregistered != null ? s_unregistered : (s_unregistered = NewAsset());
            VelvetFonts.Register(Family("sans", s_sans, s_sansBold));
            VelvetFonts.Register(Family("serif", s_serif, s_serifBold));
            _host = new HeadlessEditorPanelHost();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            VelvetFonts.Clear();
            if (_ruledSheet != null)
            {
                UnityEngine.Object.DestroyImmediate(_ruledSheet);
                _ruledSheet = null;
            }

            AssetDatabase.DeleteAsset(RuledPath);
        }

        private const string RuledPath = "Assets/VelvetFontFamilyFacesRuled.asset";
        private StyleSheet _ruledSheet;

        // Attaches a sheet of the app's own, imported as the editor imports a .uss file.
        private void AttachSheet(string uss)
        {
            _ruledSheet = ScriptableObject.CreateInstance<StyleSheet>();
            var impl = Activator.CreateInstance(StockStyleSheetImport.Impl!, true)!;
            try
            {
                StockStyleSheetImport.Import!.Invoke(impl, new object[] { _ruledSheet, uss });
            }
            catch (TargetInvocationException)
            {
                // The importer reports through its context, which this instance has none of; what it stored is used.
            }

            _host.Root.styleSheets.Add(_ruledSheet);
        }

        // Registers a family "ruled" whose normal font is saved where a sheet can name it, and attaches a sheet whose
        // .ruled rule gives that font. Returns the family's bold.
        private FontAsset AttachSheetGivingTheRuledFont()
        {
            var ruled = NewAsset();
            var ruledBold = NewAsset();
            // Made from the built-in font, the asset is given the operating system's fonts as fallbacks, built for one editor
            // session and not saved with an asset; the asset database refuses to save one flagged so.
            ruled.fallbackFontAssetTable?.Clear();
            AssetDatabase.CreateAsset(ruled, RuledPath);
            AssetDatabase.AddObjectToAsset(ruled.atlasTexture, ruled);
            AssetDatabase.AddObjectToAsset(ruled.material, ruled);
            VelvetFonts.Register(Family("ruled", ruled, ruledBold));
            var ruledFont = "-unity-font-definition: url(\"project://database/" + RuledPath + "\");";
            AttachSheet(".ruled { " + ruledFont + " } .ruled-hover:hover { " + ruledFont + " } .dark .dark-mid { " + ruledFont + " }");
            return ruledBold;
        }

        private static FontAsset NewAsset() =>
            FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        private static VelvetFontFamily Family(string name, FontAsset normal, FontAsset bold) => new(name,
            new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = normal },
            new VelvetFontWeightEntry { weight = VelvetFontWeight.Bold, upright = bold });

        // An element of its own under the panel root, given a font of its own as an author's inline style gives one.
        private VisualElement NewRootGivenFont(FontAsset font)
        {
            var element = new VisualElement();
            element.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            _host.Root.Add(element);
            return element;
        }

        private static VNode BoldLabel(string className = "font-bold") =>
            V.Div(children: new[] { V.Label(className: className, text: "target", name: "target") });

        // The panel updates its styles, then runs its scheduler once, as one frame does.
        private void RunAFrame()
        {
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        private Label Target(VisualElement root) => root.Q<Label>("target");

        [Test]
        public void Given_AFontAnAuthorGaveTheMountRoot_When_ABoldLabelMounts_Then_TheLabelResolvesThatFontsFamilysBold()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var root = NewRootGivenFont(s_serif);

            // Act
            _mounted = V.Mount(root, BoldLabel());
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(s_serifBold));
        }

        [Test]
        public void Given_AFontAnAuthorGaveTheMountRootThatNoFamilyHolds_When_ABoldLabelMounts_Then_TheLabelWritesNoFontOfItsOwn()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var root = NewRootGivenFont(s_unregistered);

            // Act
            _mounted = V.Mount(root, BoldLabel());
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ABoldLabelInABoldContainer_When_TheFontTheContainerInheritsChanges_Then_TheLabelFollowsInTheSameFrame()
        {
            // Arrange
            var root = NewRootGivenFont(s_sans);
            _mounted = V.Mount(root, V.Div(className: "font-bold", children: new[]
            {
                V.Label(className: "font-bold", text: "target", name: "target"),
            }));
            RunAFrame();

            // Act
            root.style.unityFontDefinition = FontDefinition.FromSDFFont(s_serif);
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(s_serifBold));
        }

        [Test]
        public void Given_ABoldLabelOnTheDefaultFamily_When_TheDefaultFamilyChanges_Then_TheLabelResolvesTheNewFamilysBold()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var root = new VisualElement();
            _host.Root.Add(root);
            _mounted = V.Mount(root, BoldLabel());
            RunAFrame();

            // Act
            VelvetFonts.DefaultFamily = "serif";
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(s_serifBold));
        }

        [Test]
        public void Given_ABoldLabelNamingAFamily_When_TheFamilyIsRegisteredAgain_Then_TheLabelResolvesTheNewBold()
        {
            // Arrange
            var root = new VisualElement();
            _host.Root.Add(root);
            _mounted = V.Mount(root, BoldLabel("font-serif font-bold"));

            // Act
            VelvetFonts.Register(Family("serif", s_serif, s_serifBoldReplacement));

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(s_serifBoldReplacement));
        }

        [Test]
        public void Given_ABoldLabelInAFamilyContainer_When_TheFamilyIsRegisteredAgain_Then_TheLabelResolvesTheNewBold()
        {
            // Arrange
            var root = new VisualElement();
            _host.Root.Add(root);
            _mounted = V.Mount(root, V.Div(className: "font-serif", children: new[] { BoldLabel() }));

            // Act
            VelvetFonts.Register(Family("serif", s_serif, s_serifBoldReplacement));

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(s_serifBoldReplacement));
        }

        private static void SeedAddress(string key, FontAsset asset)
        {
            var field = typeof(VelvetFonts).GetField("_addrCache",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            ((System.Collections.Generic.Dictionary<string, FontAsset>)field.GetValue(null))[key] = asset;
        }

        // GREEN_ON_BASE(characterization): the base resolves a family registered by address alone by its name; this pins
        // that the family-of-font lookup keeps matching an address's asset. Removing the address match from Holds turns it red.
        [Test]
        public void Given_AFamilyRegisteredByAddressOnly_When_ABoldLabelMountsUnderTheDefaultFamily_Then_TheLabelResolvesThatFamilysBold()
        {
            // Arrange
            SeedAddress("Fonts/Sans", s_sans);
            SeedAddress("Fonts/SansBold", s_sansBold);
            VelvetFonts.Register(new VelvetFontFamily("sans",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, uprightAddress = "Fonts/Sans" },
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Bold, uprightAddress = "Fonts/SansBold" }));
            VelvetFonts.DefaultFamily = "sans";
            var root = new VisualElement();
            _host.Root.Add(root);

            // Act
            _mounted = V.Mount(root, BoldLabel());
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(s_sansBold));
        }

        [Test]
        public void Given_ABoldLabelWhoseOwnRuleGivesItAFont_When_ItMounts_Then_TheLabelResolvesThatFontsFamilysBold()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var ruledBold = AttachSheetGivingTheRuledFont();
            var root = new VisualElement();
            _host.Root.Add(root);

            // Act
            _mounted = V.Mount(root, BoldLabel("ruled font-bold"));
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(ruledBold));
        }

        [Test]
        public void Given_AFamilyContainerWithARuledElementBetweenItAndABoldLabel_When_TheLabelMounts_Then_TheRulesFamilyDecides()
        {
            // Arrange
            var ruledBold = AttachSheetGivingTheRuledFont();
            var root = new VisualElement();
            _host.Root.Add(root);
            var tree = V.Div(className: "font-serif", children: new[]
            {
                V.Div(className: "ruled", children: new[] { V.Label(className: "font-bold", text: "target", name: "target") }),
            });

            // Act
            _mounted = V.Mount(root, tree);
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(ruledBold));
        }

        private const string EngineFont = "resource(\"UIPackageResources/Fonts/NotInter/NotInter-Regular.ttf\")";

        private static VNode ClassedBoldLabel(string className) => V.Div(children: new[]
        {
            V.Label(className: className, text: "target", name: "target"),
        });

        [Test]
        public void Given_ABoldLabel_When_AClassGivingItARuleFontIsPatchedInWhileItsParentFontStays_Then_TheLabelResolvesThatFontsFamilysBold()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var ruledBold = AttachSheetGivingTheRuledFont();
            var root = new VisualElement();
            _host.Root.Add(root);
            _mounted = V.Mount(root, ClassedBoldLabel("font-bold"));
            RunAFrame();

            // Act
            _mounted.Render(ClassedBoldLabel("ruled font-bold"));
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(ruledBold));
        }

        [Test]
        public void Given_ABoldLabelUnderAFamilyContainer_When_AnAncestorBetweenThemGainsARuleFontAfterTheLabelResolved_Then_TheLabelFollows()
        {
            // Arrange
            var ruledBold = AttachSheetGivingTheRuledFont();
            var root = new VisualElement();
            _host.Root.Add(root);
            VNode Tree(string middleClass) => V.Div(className: "font-serif", children: new[]
            {
                V.Div(className: middleClass, name: "mid", children: new[] { V.Label(className: "font-bold", text: "target", name: "target") }),
            });
            _mounted = V.Mount(root, Tree("p-1"));
            RunAFrame();

            // Act
            root.Q("mid").AddToClassList("ruled");
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(ruledBold));
        }

        [Test]
        public void Given_ABoldLabelWhoseOwnRuleGivesItAFontFile_When_ItMounts_Then_TheLabelWritesNoFontOfItsOwn()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            AttachSheet(".file { -unity-font-definition: " + EngineFont + "; }");
            var root = new VisualElement();
            _host.Root.Add(root);

            // Act
            _mounted = V.Mount(root, ClassedBoldLabel("file font-bold"));
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // The label's own legacy -unity-font rule loses to the definition the default family writes on the mount root, which
        // the engine draws ahead of a legacy font; CSS gives the element its own declaration, so the font is written as
        // the label's definition. The asserted font is the one the engine resolves the rule to, and null fails the assertion.
        [Test]
        public void Given_ABoldLabelWhoseOwnRuleGivesItALegacyFont_When_ItMountsUnderADefaultFamily_Then_TheLabelsDefinitionIsThatLegacyFont()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            AttachSheet(".legacy { -unity-font: " + EngineFont + "; }");
            var root = new VisualElement();
            _host.Root.Add(root);

            // Act
            _mounted = V.Mount(root, ClassedBoldLabel("legacy font-bold"));
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.font,
                Is.Not.Null.And.SameAs(Target(root).resolvedStyle.unityFont));
        }

        [Test]
        public void Given_ABoldLabelUnderAnAncestorWhoseRuleGivesALegacyFont_When_ItMountsUnderADefaultFamily_Then_TheLabelsDefinitionIsThatLegacyFont()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            AttachSheet(".legacy { -unity-font: " + EngineFont + "; }");
            var root = new VisualElement();
            _host.Root.Add(root);

            // Act
            _mounted = V.Mount(root, V.Div(className: "legacy", children: new[]
            {
                V.Label(className: "font-bold", text: "target", name: "target"),
            }));
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.font,
                Is.Not.Null.And.SameAs(Target(root).resolvedStyle.unityFont));
        }

        [Test]
        public void Given_ABoldLabelUnderAnAncestorGivenAFontFile_When_ItMounts_Then_TheLabelWritesNoFontOfItsOwn()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            AttachSheet(".file { -unity-font-definition: " + EngineFont + "; }");
            var root = new VisualElement();
            root.AddToClassList("file");
            _host.Root.Add(root);

            // Act
            _mounted = V.Mount(root, ClassedBoldLabel("font-bold"));
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ABoldLabelWithAnInlineFontNoFamilyHolds_When_ItsClassIsPatched_Then_TheInlineFontStays()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var root = new VisualElement();
            _host.Root.Add(root);
            _mounted = V.Mount(root, ClassedBoldLabel("font-normal"));
            RunAFrame();
            Target(root).style.unityFontDefinition = FontDefinition.FromSDFFont(s_unregistered);

            // Act
            _mounted.Render(ClassedBoldLabel("font-bold"));
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(s_unregistered));
        }

        [Test]
        public void Given_ABoldLabelWithAnInlineFontOfAFamily_When_ItsClassIsPatched_Then_TheLabelResolvesThatFamilysBold()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var root = new VisualElement();
            _host.Root.Add(root);
            _mounted = V.Mount(root, ClassedBoldLabel("font-normal"));
            RunAFrame();
            Target(root).style.unityFontDefinition = FontDefinition.FromSDFFont(s_serif);

            // Act
            _mounted.Render(ClassedBoldLabel("font-bold"));
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(s_serifBold));
        }

        [Test]
        public void Given_ABoldLabelUnderAFamilyContainer_When_AnAncestorBetweenThemIsHoveredAndAHoverRuleGivesItAFont_Then_TheLabelFollows()
        {
            // Arrange
            var ruledBold = AttachSheetGivingTheRuledFont();
            var root = new VisualElement();
            _host.Root.Add(root);
            _mounted = V.Mount(root, V.Div(className: "font-serif", children: new[]
            {
                V.Div(className: "ruled-hover", name: "mid", children: new[] { V.Label(className: "font-bold", text: "target", name: "target") }),
            }));
            RunAFrame();

            // Act
            var pseudo = EngineMember.PseudoStates.ResolveProperty()!;
            var hover = Convert.ToInt32(Enum.Parse(pseudo.PropertyType, "Hover"));
            var states = Convert.ToInt32(pseudo.GetValue(root.Q("mid")));
            pseudo.SetValue(root.Q("mid"), Enum.ToObject(pseudo.PropertyType, states | hover));
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(ruledBold));
        }

        private static VNode SerifContainerWithAMiddle(string middleClass) => V.Div(className: "font-serif", children: new[]
        {
            V.Div(className: middleClass, name: "mid", children: new[] { V.Label(className: "font-bold", text: "target", name: "target") }),
        });

        [Test]
        public void Given_ABoldLabelUnderAFamilyContainer_When_ASheetGivingARuledMiddleItsFontIsAttachedAboveTheContainer_Then_TheLabelFollows()
        {
            // Arrange
            var root = new VisualElement();
            _host.Root.Add(root);
            _mounted = V.Mount(root, SerifContainerWithAMiddle("ruled"));
            RunAFrame();

            // Act
            var ruledBold = AttachSheetGivingTheRuledFont();
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(ruledBold));
        }

        [Test]
        public void Given_ABoldLabelUnderAFamilyContainer_When_ADescendantRuleOfAClassAboveTheContainerStartsToMatchTheMiddle_Then_TheLabelFollows()
        {
            // Arrange
            var ruledBold = AttachSheetGivingTheRuledFont();
            var root = new VisualElement();
            _host.Root.Add(root);
            _mounted = V.Mount(root, SerifContainerWithAMiddle("dark-mid"));
            RunAFrame();

            // Act
            root.AddToClassList("dark");
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(ruledBold));
        }

        [Test]
        public void Given_ABoldLabelWhoseInlineFontReplacedByTheClassIsOfAFamily_When_ThatFamilyIsUnregistered_Then_TheInlineFontIsGivenBack()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var root = new VisualElement();
            _host.Root.Add(root);
            _mounted = V.Mount(root, ClassedBoldLabel(string.Empty));
            RunAFrame();
            Target(root).style.unityFontDefinition = FontDefinition.FromSDFFont(s_serif);
            _mounted.Render(ClassedBoldLabel("font-bold"));
            RunAFrame();

            // Act
            VelvetFonts.Unregister("serif");
            RunAFrame();
            RunAFrame();
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.fontAsset, Is.SameAs(s_serif));
        }

        [Test]
        public void Given_AnAddressNamedFamily_When_AskedWhetherItHoldsTheAssetTheAddressLoads_Then_ItDoes()
        {
            // Arrange
            SeedAddress("Fonts/Sans", s_sans);

            // Act
            var holds = VelvetFonts.NamedHolds("addr:Fonts/Sans", s_sans);

            // Assert
            Assert.That(holds, Is.True);
        }

        [Test]
        public void Given_ABoldLabelUnderAnAncestorWithAnInlineLegacyFont_When_ItMountsUnderADefaultFamily_Then_TheLabelsDefinitionIsThatLegacyFont()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new VisualElement();
            _host.Root.Add(root);

            // Act
            _mounted = V.Mount(root, V.Div(children: new[]
            {
                V.Div(name: "mid", children: new[] { V.Label(className: "font-bold", text: "target", name: "target") }),
            }));
            root.Q("mid").style.unityFont = legacy;
            RunAFrame();

            // Assert
            Assert.That(Target(root).style.unityFontDefinition.value.font, Is.Not.Null.And.SameAs(legacy));
        }

        // GREEN_ON_BASE(characterization): the base takes its write off with the class whatever copies it. Leave
        // `LineBoxFont.SourceOf(shown.fontAsset)` out of WrittenFont in the ledger and the copy of the legacy write reads as
        // the author's, which the removal then leaves on the label.
        [Test]
        public void Given_ALabelWhoseLegacyFontWriteWasCopiedByALineBoxFont_When_ItsFontClassIsRemoved_Then_TheWriteIsTakenOff()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new VisualElement();
            _host.Root.Add(root);
            _mounted = V.Mount(root, ClassedBoldLabel(string.Empty));
            RunAFrame();
            var label = Target(root);
            label.style.unityFont = legacy;
            _mounted.Render(ClassedBoldLabel("font-bold"));
            RunAFrame();
            var user = new object();
            var copy = LineBoxFont.Acquire(user, StyleCascade.CachedFontAsset(label, legacy), 24f, 16f);
            label.style.unityFontDefinition = FontDefinition.FromSDFFont(copy);

            // Act
            _mounted.Render(ClassedBoldLabel(string.Empty));
            RunAFrame();
            LineBoxFont.Release(user, copy);

            // Assert
            Assert.That(label.style.unityFontDefinition.keyword, Is.EqualTo(StyleKeyword.Null));
        }
    }
}
