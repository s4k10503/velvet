using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which family a weight or italic class with no family class of its own resolves against:
    /// the one the nearest ancestor with a <c>font-&lt;name&gt;</c> class names, as CSS's
    /// <c>font-weight</c> keeps an inherited <c>font-family</c>, and <see cref="VelvetFonts.DefaultFamily"/>
    /// only when no ancestor names one. Reads the asset Velvet wrote inline on the label. GWT, one assert
    /// per case.
    /// </summary>
    [TestFixture]
    internal sealed class InheritedFontFamilyPanelTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private readonly List<FontAsset> _assets = new();
        private FontAsset _sansBold;
        private FontAsset _serifBold;
        private FontAsset _monoBold;

        [SetUp]
        public void SetUp()
        {
            VelvetFonts.Clear();
            _sansBold = Register("sans");
            _serifBold = Register("serif");
            _monoBold = Register("mono");
            VelvetFonts.DefaultFamily = "sans";
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
            foreach (var asset in _assets)
            {
                DestroyAsset(asset);
            }

            _assets.Clear();
        }

        // Registers a family with a Normal and a Bold entry and returns the Bold asset.
        private FontAsset Register(string name)
        {
            var normal = NewAsset();
            var bold = NewAsset();
            VelvetFonts.Register(new VelvetFontFamily(name,
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = normal },
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Bold, upright = bold }));
            return bold;
        }

        private FontAsset NewAsset()
        {
            var asset = FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            _assets.Add(asset);
            return asset;
        }

        private static void DestroyAsset(FontAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            foreach (var atlas in asset.atlasTextures ?? System.Array.Empty<Texture2D>())
            {
                if (atlas != null)
                {
                    Object.DestroyImmediate(atlas);
                }
            }

            if (asset.material != null)
            {
                Object.DestroyImmediate(asset.material);
            }

            Object.DestroyImmediate(asset);
        }

        private static VNode Target(string className) => V.Label(className: className, text: "target", name: "target");

        private FontAsset TargetAsset() =>
            _host.Root.Q<Label>("target").style.unityFontDefinition.value.fontAsset;

        [Test]
        public void Given_ABoldLabelInAFamilyContainer_When_Mounted_Then_TheLabelResolvesThatFamilysBold()
        {
            // Arrange
            var tree = V.Div(className: "font-serif", children: new[] { Target("font-bold") });

            // Act
            _mounted = V.Mount(_host.Root, tree);

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_serifBold));
        }

        [Test]
        public void Given_AFamilyContainer_When_ABoldLabelIsPatchedIn_Then_TheLabelResolvesThatFamilysBold()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(className: "font-serif", children: new VNode[0]));

            // Act
            _mounted.Render(V.Div(className: "font-serif", children: new[] { Target("font-bold") }));

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_serifBold));
        }

        [Test]
        public void Given_ABoldLabelInAFamilyContainer_When_TheContainersFamilyChanges_Then_TheLabelFollows()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(className: "font-serif", children: new[] { Target("font-bold") }));

            // Act
            _mounted.Render(V.Div(className: "font-mono", children: new[] { Target("font-bold") }));

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoBold));
        }

        [Test]
        public void Given_NestedFamilyContainers_When_ABoldLabelMounts_Then_TheNearestFamilyDecides()
        {
            // Arrange
            var tree = V.Div(className: "font-serif", children: new VNode[]
            {
                V.Div(className: "font-mono", children: new[] { Target("font-bold") }),
            });

            // Act
            _mounted = V.Mount(_host.Root, tree);

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoBold));
        }

        [Test]
        public void Given_AWeightOnlyContainerInAFamilyContainer_When_ABoldLabelMounts_Then_TheOuterFamilyDecides()
        {
            // Arrange
            var tree = V.Div(className: "font-serif", children: new VNode[]
            {
                V.Div(className: "font-normal", children: new[] { Target("font-bold") }),
            });

            // Act
            _mounted = V.Mount(_host.Root, tree);

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_serifBold));
        }

        // GREEN_ON_BASE(characterization): the base resolves the default family everywhere; this pins that the
        // label leaves the former family once the container stops naming it.
        [Test]
        public void Given_ABoldLabelInAFamilyContainer_When_TheContainerDropsItsFamilyClass_Then_TheLabelFallsBackToTheDefaultFamily()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(className: "font-serif", children: new[] { Target("font-bold") }));

            // Act
            _mounted.Render(V.Div(children: new[] { Target("font-bold") }));

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_sansBold));
        }

        // GREEN_ON_BASE(characterization): the base resolves the default family everywhere; this pins that the
        // label leaves the former family once the container stops naming it.
        [Test]
        public void Given_ABoldLabelInAFamilyContainer_When_TheContainerSwapsItsFamilyForAWeight_Then_TheLabelFallsBackToTheDefaultFamily()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(className: "font-serif", children: new[] { Target("font-bold") }));

            // Act
            _mounted.Render(V.Div(className: "font-normal", children: new[] { Target("font-bold") }));

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_sansBold));
        }

        // GREEN_ON_BASE(characterization): the family a label names itself still wins over an ancestor's.
        [Test]
        public void Given_ALabelNamingItsOwnFamily_When_MountedInAFamilyContainer_Then_ItsOwnFamilyDecides()
        {
            // Arrange
            var tree = V.Div(className: "font-serif", children: new[] { Target("font-mono font-bold") });

            // Act
            _mounted = V.Mount(_host.Root, tree);

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoBold));
        }

        // GREEN_ON_BASE(characterization): with no ancestor naming a family, DefaultFamily still decides.
        [Test]
        public void Given_NoAncestorNamingAFamily_When_ABoldLabelMounts_Then_TheDefaultFamilyDecides()
        {
            // Arrange
            var tree = V.Div(className: "font-bold", children: new[] { Target("font-bold") });

            // Act
            _mounted = V.Mount(_host.Root, tree);

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_sansBold));
        }
    }
}
