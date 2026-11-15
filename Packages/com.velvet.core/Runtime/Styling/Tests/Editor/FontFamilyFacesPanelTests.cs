using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which face a weight class resolves on a panel: the family of a font an author's inline style gives
    /// an element above the tree rather than <see cref="VelvetFonts.DefaultFamily"/>, that font alone where no family
    /// holds it, and the asset a changed registry gives each element whose font a class resolved. Reads the asset
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
    }
}
