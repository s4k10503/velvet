using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how far a registered family reaches a label that carries no font class, read through
    /// <c>resolvedStyle</c> on a panel with no theme stylesheet so inheritance takes part:
    /// <see cref="VelvetFonts.DefaultFamily"/> reaches such a label under the element a tree is mounted on, under a
    /// portal's container and under a layer or world-space panel's root, and a family an element between them names
    /// decides instead. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class DefaultFamilyScopePanelTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private MountedTree _second;
        private FontAsset _sans;
        private FontAsset _sansItalic;
        private FontAsset _serif;

        [SetUp]
        public void SetUp()
        {
            VelvetFonts.Clear();
            _sans = FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            _sansItalic = FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            _serif = FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            // An italic face beside the upright one, so a write of the default family has a face to pick between.
            VelvetFonts.Register(new VelvetFontFamily("sans",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = _sans, italic = _sansItalic }));
            VelvetFonts.Register(new VelvetFontFamily("serif",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = _serif }));
            _host = new HeadlessEditorPanelHost();
        }

        [TearDown]
        public void TearDown()
        {
            _second?.Dispose();
            _second = null;
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            VelvetFonts.Clear();
            DestroyAsset(_sans);
            _sans = null;
            DestroyAsset(_sansItalic);
            _sansItalic = null;
            DestroyAsset(_serif);
            _serif = null;
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

        private bool ResolvesSans(string name) =>
            _host.Root.Q<Label>(name).resolvedStyle.unityFontDefinition.fontAsset == _sans;

        // An element of its own under the panel root, so what a tree is mounted on is not the panel's root.
        private VisualElement NewRootElement()
        {
            var element = new VisualElement();
            _host.Root.Add(element);
            return element;
        }

        private static FontAsset ResolvedFont(VisualElement root, string name)
        {
            EditorPanelTestHelpers.ForcePanelUpdate(root.panel);
            return root.Q<Label>(name).resolvedStyle.unityFontDefinition.fontAsset;
        }

        // The framework hosts are hidden objects, which FindObjectsOfTypeAll still sees.
        private static VisualElement HostRootHolding(string name) => Resources.FindObjectsOfTypeAll<UIDocument>()
            .Select(document => document.rootVisualElement)
            .First(root => root?.Q<VisualElement>(name) != null);

        private static VNode Plain(string name) => V.Label(text: name, name: name);

        [Test]
        public void Given_ADefaultFamily_When_ALabelWithNoFontClassMounts_Then_ItResolvesTheDefaultFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var mountRoot = NewRootElement();

            // Act
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.SameAs(_sans));
        }

        // GREEN_ON_BASE(characterization): a family class reaches what is inside its element and nothing beside it.
        [Test]
        public void Given_AFamilyClassOnAContainer_When_AnUnadornedLabelMountsInsideIt_Then_TheLabelResolvesThatFamily()
        {
            // Arrange — "sans" is registered by the setup, with no DefaultFamily.

            // Act — the outside label separates inheritance from the family reaching every label.
            _mounted = V.Mount(_host.Root, V.Div(children: new VNode[]
            {
                V.Div(className: "font-sans", children: new VNode[] { V.Label(text: "inside", name: "inside") }),
                V.Label(text: "outside", name: "outside"),
            }));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That((ResolvesSans("inside"), ResolvesSans("outside")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ATreeMountedWithNoDefaultFamily_When_ADefaultFamilyIsSet_Then_ALabelWithNoFontClassResolvesIt()
        {
            // Arrange
            var mountRoot = NewRootElement();
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Act — no render follows, so the tree takes the change through nothing of its own.
            VelvetFonts.DefaultFamily = "sans";

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.SameAs(_sans));
        }

        [Test]
        public void Given_ADefaultFamily_When_APortalRendersALabelIntoAContainerBesideTheMountRoot_Then_TheLabelResolvesTheDefaultFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var mountRoot = NewRootElement();
            var container = NewRootElement();

            // Act
            _mounted = V.Mount(mountRoot, V.Div(children: new VNode[]
            {
                V.Portal(container, new[] { Plain("ported") }),
            }));

            // Assert
            Assert.That(ResolvedFont(container, "ported"), Is.SameAs(_sans));
        }

        // GREEN_ON_BASE(characterization): a portal's container inside a mounted tree keeps the family it inherits.
        // The base writes the default family on no container; this pins that the change leaves such a one alone.
        [Test]
        public void Given_ADefaultFamily_When_APortalRendersALabelIntoAContainerUnderAFamilyContainer_Then_TheFamilyContainersFamilyDecides()
        {
            // Arrange — the container is another tree's, inside the element that tree is mounted on.
            VelvetFonts.DefaultFamily = "sans";
            var familyRoot = NewRootElement();
            _mounted = V.Mount(familyRoot, V.Div(className: "font-serif", children: new VNode[]
            {
                V.Div(name: "inner"),
            }));
            var inner = familyRoot.Q<VisualElement>("inner");

            // Act
            _second = V.Mount(NewRootElement(), V.Div(children: new VNode[]
            {
                V.Portal(inner, new[] { Plain("ported") }),
            }));

            // Assert
            Assert.That(ResolvedFont(familyRoot, "ported"), Is.SameAs(_serif));
        }

        [Test]
        public void Given_ADefaultFamily_When_ALayerPortalRendersALabelWithNoFontClass_Then_TheLabelResolvesTheDefaultFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";

            // Act
            _mounted = V.Mount(_host.Root, V.Portal(UILayer.Overlay, new[] { Plain("overlaid") }));

            // Assert
            Assert.That(ResolvedFont(HostRootHolding("overlaid"), "overlaid"), Is.SameAs(_sans));
        }

        [Test]
        public void Given_ADefaultFamily_When_AWorldSpacePanelMounts_Then_ItsRootCarriesTheDefaultFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";

            // Act
            _mounted = V.Mount(_host.Root, V.WorldSpace(Vector3.zero, children: new[] { Plain("in-world") }));

            // Assert — the root's own value, which the layer case above follows into a label.
            Assert.That(HostRootHolding("in-world").style.unityFontDefinition.value.fontAsset, Is.SameAs(_sans));
        }

        // GREEN_ON_BASE(characterization): nothing of the default family stays on a root once its tree is gone.
        // The base writes nothing there; this pins that the write the change adds goes with the tree.
        [Test]
        public void Given_ADefaultFamilyOnAMountedTree_When_TheTreeIsDisposed_Then_ALabelAddedToItsRootDoesNotResolveIt()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var mountRoot = NewRootElement();
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Act
            _mounted.Dispose();
            _mounted = null;
            mountRoot.Add(new Label("later") { name = "later" });

            // Assert
            Assert.That(ResolvedFont(mountRoot, "later"), Is.Not.SameAs(_sans));
        }

        // GREEN_ON_BASE(characterization): nothing of the default family stays on a container once its portal is gone.
        // The base writes nothing there; this pins that the write the change adds goes with the portal.
        [Test]
        public void Given_ADefaultFamilyOnAPortalsContainer_When_ItsTreeIsDisposed_Then_ALabelAddedToTheContainerDoesNotResolveIt()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var container = NewRootElement();
            _mounted = V.Mount(NewRootElement(), V.Div(children: new VNode[]
            {
                V.Portal(container, new[] { Plain("ported") }),
            }));

            // Act
            _mounted.Dispose();
            _mounted = null;
            container.Add(new Label("later") { name = "later" });

            // Assert
            Assert.That(ResolvedFont(container, "later"), Is.Not.SameAs(_sans));
        }

        [Test]
        public void Given_ATreeMountedInsideAPortalsContainer_When_ThePortalsTreeIsDisposed_Then_TheInnerTreesRootCarriesTheDefaultFamily()
        {
            // Arrange — while the portal is there, the inner root holds no value and inherits the container's, so
            // its own value is what moves when the container's goes.
            VelvetFonts.DefaultFamily = "sans";
            var container = NewRootElement();
            var innerRoot = new VisualElement();
            container.Add(innerRoot);
            _mounted = V.Mount(innerRoot, V.Div(children: new[] { Plain("inner") }));
            _second = V.Mount(NewRootElement(), V.Div(children: new VNode[]
            {
                V.Portal(container, new[] { Plain("ported") }),
            }));

            // Act
            _second.Dispose();
            _second = null;

            // Assert
            Assert.That(innerRoot.style.unityFontDefinition.value.fontAsset, Is.SameAs(_sans));
        }

        // GREEN_ON_BASE(characterization): an inline font the app wrote on the mount root keeps reaching the tree.
        // The base writes nothing there; this pins that the default family does not replace it. A legacy Font is
        // the form whose definition holds no Font Asset, the one an asset comparison alone would take for none.
        [Test]
        public void Given_AnInlineFontTheAppWroteOnTheMountRoot_When_ATreeMountsWithADefaultFamily_Then_ItsLabelResolvesTheAppsFont()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var mountRoot = NewRootElement();
            var legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mountRoot.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(legacy));

            // Act
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That(mountRoot.Q<Label>("plain").resolvedStyle.unityFontDefinition.font, Is.SameAs(legacy));
        }
    }
}
