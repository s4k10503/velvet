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
    /// <see cref="VelvetFonts.DefaultFamily"/> reaches such a label under the element a tree is mounted on, under the
    /// rows a portal renders and under a layer or world-space panel's root, and a family an element between them names
    /// decides instead, as does a font an author's stylesheet or inline style gives one of those elements or one
    /// above it. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class DefaultFamilyScopePanelTests
    {
        private const string AuthorSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/DefaultFamilyAuthorFont.uss";
        private const string ThemeSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/DefaultFamilyThemeFont.uss";

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

        // The author's sheet on the panel root, and a root for a tree under it carrying className.
        private VisualElement NewRootUnderAuthorSheet(string className)
        {
            _host.Root.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(AuthorSheet));
            var element = NewRootElement();
            element.AddToClassList(className);
            return element;
        }

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
        // The base writes nothing there; this pins that the write the change adds goes with the tree, and that a
        // restyle of the root after it, which a held root is read again for, writes nothing either.
        [Test]
        public void Given_ADefaultFamilyOnAMountedTree_When_TheTreeIsDisposedAndItsRootRestyles_Then_ALabelAddedToItsRootDoesNotResolveIt()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var mountRoot = NewRootElement();
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Act
            _mounted.Dispose();
            _mounted = null;
            mountRoot.AddToClassList("restyled");
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            mountRoot.Add(new Label("later") { name = "later" });

            // Assert
            Assert.That(ResolvedFont(mountRoot, "later"), Is.Not.SameAs(_sans));
        }

        private static VNode PortalInto(VisualElement target, params VNode[] rows)
            => V.Div(children: new VNode[] { V.Portal(target, rows) });

        // GREEN_ON_BASE(characterization): a child of a portal's target the portal did not render keeps its font.
        // The base writes the default family nowhere; this pins that the change writes it on neither the target nor
        // a child of the target's own, here one standing behind the portal's rows when a pass reads them again.
        [Test]
        public void Given_APortalIntoATarget_When_AChildOfTheTargetsOwnFollowsItsRows_Then_ThatChildDoesNotResolveTheDefaultFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var target = NewRootElement();
            _mounted = V.Mount(NewRootElement(), PortalInto(target, Plain("ported")));

            // Act
            target.Add(new Label("own") { name = "own" });
            _mounted.Render(PortalInto(target, Plain("ported")));

            // Assert
            Assert.That(ResolvedFont(target, "own"), Is.Not.SameAs(_sans));
        }

        // The rows ReconcilerContext holds for the default family; -1 where the table is missing, which no count of
        // rows can equal.
        private int HeldPortalRows()
        {
            var table = typeof(ReconcilerContext).GetField("_portalRowFamilies",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var held = table?.GetValue(_mounted.Root.Reconciler.Context) as System.Collections.ICollection;
            return held?.Count ?? -1;
        }

        [Test]
        public void Given_APortalRenderingARow_When_ARenderTakesThePortalOut_Then_NoRowIsHeldForTheDefaultFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var target = NewRootElement();
            _mounted = V.Mount(NewRootElement(), PortalInto(target, Plain("ported")));

            // Act
            _mounted.Render(V.Div());

            // Assert
            Assert.That(HeldPortalRows(), Is.EqualTo(0));
        }

        private static VNode TwoPortals(VisualElement first, VisualElement second, bool withFirst)
            => V.Div(children: new VNode[]
            {
                withFirst ? V.Portal(first, new[] { Plain("first") }, key: "first") : null,
                V.Portal(second, new[] { Plain("second") }, key: "second"),
            });

        [Test]
        public void Given_TwoPortalsRenderingARowEach_When_RendersTakeOneOut_Then_TheOtherRowAloneIsHeldForTheDefaultFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var first = NewRootElement();
            var second = NewRootElement();
            _mounted = V.Mount(NewRootElement(), TwoPortals(first, second, withFirst: true));

            // Act — twice, so the pass after the one that let the row go reads what that one left.
            _mounted.Render(TwoPortals(first, second, withFirst: false));
            _mounted.Render(TwoPortals(first, second, withFirst: false));

            // Assert
            Assert.That(HeldPortalRows(), Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): a portal row StyleFontResolver writes the default face on keeps that face.
        // The base writes that face from the row's class alone; this pins that the change, once an author's rule comes
        // to give the target a font, does not take back a write that is the row's own although it equals its own.
        [Test]
        public void Given_APortalRowGivenTheDefaultFaceByItsClass_When_AnAuthorsFontRuleComesToMatchItsTarget_Then_TheRowKeepsThatFace()
        {
            // Arrange — written by DefaultFamilyRoots first, then by the row's own weight class with the same asset.
            VelvetFonts.DefaultFamily = "sans";
            var target = NewRootUnderAuthorSheet("unruled");
            _mounted = V.Mount(NewRootElement(), PortalInto(target, V.Label(text: "row", name: "row")));
            _mounted.Render(PortalInto(target, V.Label(className: "font-normal", text: "row", name: "row")));

            // Act — the restyle first, then the update of the panel that reads the row again.
            target.AddToClassList("author-font");
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(ResolvedFont(target, "row"), Is.SameAs(_sans));
        }

        [Test]
        public void Given_APortalRowNamingAFamily_When_ARenderTakesTheFamilyClassOff_Then_TheRowResolvesTheDefaultFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var target = NewRootElement();
            _mounted = V.Mount(NewRootElement(),
                PortalInto(target, V.Label(className: "font-serif", text: "row", name: "row")));

            // Act
            _mounted.Render(PortalInto(target, V.Label(text: "row", name: "row")));

            // Assert
            Assert.That(ResolvedFont(target, "row"), Is.SameAs(_sans));
        }

        [Test]
        public void Given_ATreeMountedOnAnElementInsideAnotherTreesRoot_When_TheOuterTreeIsDisposed_Then_TheInnerRootCarriesTheDefaultFamily()
        {
            // Arrange — while the outer tree is there, the inner root holds no value and inherits the outer root's, so
            // its own value is what moves when the outer one goes.
            VelvetFonts.DefaultFamily = "sans";
            var outerRoot = NewRootElement();
            _mounted = V.Mount(outerRoot, V.Div(children: new[] { Plain("outer") }));
            var innerRoot = new VisualElement();
            outerRoot.Add(innerRoot);
            _second = V.Mount(innerRoot, V.Div(children: new[] { Plain("inner") }));

            // Act
            _mounted.Dispose();
            _mounted = null;

            // Assert
            Assert.That(innerRoot.style.unityFontDefinition.value.fontAsset, Is.SameAs(_sans));
        }

        // GREEN_ON_BASE(characterization): a tree placed in a family container after mounting takes that family.
        // The base writes nothing on the root; this pins that the default family it was given is taken off on placing.
        [Test]
        public void Given_ATreeMountedOnADetachedRoot_When_TheRootIsPlacedInAnotherTreesFamilyContainer_Then_ItsLabelResolvesThatFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var outerRoot = NewRootElement();
            _mounted = V.Mount(outerRoot, V.Div(className: "font-serif", name: "family"));
            var detached = new VisualElement();
            _second = V.Mount(detached, V.Div(children: new[] { Plain("row") }));

            // Act — placed, then the update of the panel that reads the root again.
            outerRoot.Q<VisualElement>("family").Add(detached);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(ResolvedFont(outerRoot, "row"), Is.SameAs(_serif));
        }

        // GREEN_ON_BASE(characterization): an author's hover font rule on the mount root's ancestor reaches the tree.
        // The base writes nothing there; this pins that the default family is taken off once the ancestor is hovered.
        [Test]
        public void Given_ATreeUnderAnAncestorAHoverRuleGivesAFont_When_TheAncestorIsHovered_Then_ItsLabelDoesNotResolveIt()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var ancestor = NewRootUnderAuthorSheet("author-hover");
            var mountRoot = new VisualElement();
            ancestor.Add(mountRoot);
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Act — hovered, then the update of the panel that reads the root again.
            var pseudo = EngineMember.PseudoStates.ResolveProperty()!;
            var hover = System.Convert.ToInt32(System.Enum.Parse(pseudo.PropertyType, "Hover"));
            var states = System.Convert.ToInt32(pseudo.GetValue(ancestor));
            pseudo.SetValue(ancestor, System.Enum.ToObject(pseudo.PropertyType, states | hover));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.Not.SameAs(_sans));
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

        // GREEN_ON_BASE(characterization): an author's font rule on the mount root reaches the tree.
        // The base writes nothing there; this pins that the default family does not replace it.
        [Test]
        public void Given_AnAuthorsRuleSettingAFontOnTheMountRoot_When_ATreeMountsWithADefaultFamily_Then_ItsLabelDoesNotResolveIt()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var mountRoot = NewRootUnderAuthorSheet("author-font");

            // Act
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.Not.SameAs(_sans));
        }

        // GREEN_ON_BASE(characterization): an author's font rule on an ancestor of the mount root reaches the tree.
        // The base writes nothing there; this pins that the default family does not replace it.
        [Test]
        public void Given_AnAuthorsRuleSettingAFontOnAnAncestor_When_ATreeMountsWithADefaultFamily_Then_ItsLabelDoesNotResolveIt()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var ancestor = NewRootUnderAuthorSheet("author-font");
            var mountRoot = new VisualElement();
            ancestor.Add(mountRoot);

            // Act
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.Not.SameAs(_sans));
        }

        // GREEN_ON_BASE(characterization): an app's inline font on an ancestor of the mount root reaches the tree.
        // The base writes nothing there; this pins that the default family does not replace it.
        [Test]
        public void Given_AnInlineFontTheAppWroteOnAnAncestor_When_ATreeMountsWithADefaultFamily_Then_ItsLabelResolvesTheAppsFont()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var ancestor = NewRootElement();
            var legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ancestor.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(legacy));
            var mountRoot = new VisualElement();
            ancestor.Add(mountRoot);

            // Act
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That(mountRoot.Q<Label>("plain").resolvedStyle.unityFontDefinition.font, Is.SameAs(legacy));
        }

        // GREEN_ON_BASE(characterization): an app's legacy inline font on the mount root's ancestor reaches the tree.
        // The base writes nothing there; this pins that the default family's definition does not stand over it.
        [Test]
        public void Given_ALegacyInlineFontTheAppWroteOnAnAncestor_When_ATreeMountsWithADefaultFamily_Then_ItsLabelDoesNotResolveIt()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var ancestor = NewRootElement();
            ancestor.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var mountRoot = new VisualElement();
            ancestor.Add(mountRoot);

            // Act
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.Not.SameAs(_sans));
        }

        [Test]
        public void Given_ADefaultStylesheetsRuleSettingAFontOnTheMountRoot_When_ATreeMountsWithADefaultFamily_Then_ItsLabelResolvesTheDefaultFamily()
        {
            // Arrange — flagged the way a panel's theme is.
            VelvetFonts.DefaultFamily = "sans";
            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemeSheet);
            EngineMember.StyleSheetIsDefault.ResolveProperty()!.SetValue(theme, true);
            _host.Root.styleSheets.Add(theme);
            var mountRoot = NewRootElement();
            mountRoot.AddToClassList("theme-font");

            // Act
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.SameAs(_sans));
        }

        // GREEN_ON_BASE(characterization): an author's font rule that comes to match the mount root reaches the tree.
        // The base writes nothing there; this pins that the default family is taken off once the rule applies.
        [Test]
        public void Given_ATreeMountedWithADefaultFamily_When_AnAuthorsRuleSettingAFontComesToApplyToItsRoot_Then_ItsLabelDoesNotResolveIt()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var mountRoot = NewRootUnderAuthorSheet("unruled");
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Act — the panel's scheduler ahead of its style pass, so what reads the root again is the change
            // itself rather than the rules it comes to match.
            mountRoot.AddToClassList("author-font");
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.Not.SameAs(_sans));
        }

        // GREEN_ON_BASE(characterization): an author's sheet attached above the mount root reaches the tree.
        // The base writes nothing there; this pins that the default family is taken off once the sheet is attached.
        [Test]
        public void Given_ATreeMountedWithADefaultFamily_When_AnAuthorsSheetSettingItsRootsFontIsAttachedAboveIt_Then_ItsLabelDoesNotResolveIt()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var mountRoot = NewRootElement();
            mountRoot.AddToClassList("author-font");
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Act — the panel's scheduler ahead of its style pass, so what reads the root again is the change
            // itself rather than the rules it comes to match.
            _host.Root.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(AuthorSheet));
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.Not.SameAs(_sans));
        }

        [Test]
        public void Given_ATreeOnARootAnAuthorsFontRuleMatches_When_TheRuleStopsMatchingIt_Then_ItsLabelResolvesTheDefaultFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var mountRoot = NewRootUnderAuthorSheet("author-font");
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Act — the restyle first, then the update of the panel that reads the root again.
            mountRoot.RemoveFromClassList("author-font");
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.SameAs(_sans));
        }

        [Test]
        public void Given_ADefaultFamily_When_APortalRendersAZIndexedRowBesideTheMountRoot_Then_ItsLabelResolvesTheDefaultFamily()
        {
            // Arrange — an absolute z-50 row is relocated, leaving a placeholder in its slot.
            VelvetFonts.DefaultFamily = "sans";
            var target = NewRootElement();

            // Act
            _mounted = V.Mount(NewRootElement(), PortalInto(target,
                V.Div(className: "absolute z-50", children: new[] { Plain("raised") })));

            // Assert
            Assert.That(ResolvedFont(target, "raised"), Is.SameAs(_sans));
        }

        // GREEN_ON_BASE(characterization): an author's legacy font rule reaches the tree where no definition is.
        // The base writes nothing there; this pins that the default family's definition does not stand over it.
        [Test]
        public void Given_AnAuthorsLegacyFontRuleOnTheMountRootAndNoDefinition_When_ATreeMounts_Then_ItsLabelDoesNotResolveTheDefaultFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var mountRoot = NewRootUnderAuthorSheet("author-legacy-font");

            // Act
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.Not.SameAs(_sans));
        }

        // GREEN_ON_BASE(characterization): an app's legacy inline font on the mount root shows where no definition is.
        // The base writes nothing there; this pins that the default family's definition does not stand over it.
        [Test]
        public void Given_ALegacyInlineFontTheAppWroteOnTheMountRootAndNoDefinition_When_ATreeMounts_Then_ItsLabelDoesNotResolveTheDefaultFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var mountRoot = NewRootElement();
            mountRoot.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Act
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.Not.SameAs(_sans));
        }

        // Created once and never destroyed, for the reason FontRelativeUnitPanelTests gives its own.
        private static FontAsset s_lineBoxFirst;
        private static FontAsset s_lineBoxSecond;

        private static FontAsset LineBoxFamily(string name, ref FontAsset asset)
        {
            if (asset == null)
            {
                asset = FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            }
            VelvetFonts.Register(new VelvetFontFamily(name,
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = asset }));
            return asset;
        }

        // GREEN_ON_BASE(characterization): a legacy font the app writes inline on the mount root later shows there.
        // The base writes nothing there; this pins that the definition written before is taken off for it.
        [Test]
        public void Given_ATreeMountedWithADefaultFamily_When_TheAppWritesALegacyFontInlineOnItsRoot_Then_ItsLabelDoesNotResolveTheDefaultFamily()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";
            var mountRoot = NewRootElement();
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Act — written, then the update of the panel that reads the root again.
            mountRoot.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.Not.SameAs(_sans));
        }

        [Test]
        public void Given_ALegacyInlineFontOnARootADefaultStylesheetsRuleGivesADefinition_When_ATreeMounts_Then_ItsLabelResolvesTheDefaultFamily()
        {
            // Arrange — flagged the way a panel's theme is; its definition is drawn ahead of the legacy font.
            VelvetFonts.DefaultFamily = "sans";
            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemeSheet);
            EngineMember.StyleSheetIsDefault.ResolveProperty()!.SetValue(theme, true);
            _host.Root.styleSheets.Add(theme);
            var mountRoot = NewRootElement();
            mountRoot.AddToClassList("theme-defined-font");
            mountRoot.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Act
            _mounted = V.Mount(mountRoot, V.Div(children: new[] { Plain("plain") }));

            // Assert
            Assert.That(ResolvedFont(mountRoot, "plain"), Is.SameAs(_sans));
        }

        [Test]
        public void Given_APortalRowWithRichTextOffDrawnInACopyOfTheDefaultFamily_When_TheDefaultFamilyChanges_Then_ItIsDrawnInACopyOfTheNewOne()
        {
            // Arrange — the bundled sheet gives the label the property its line box answers to; rich text off draws it
            // in LineBoxFont's copy of the font written on it.
            LineBoxFamily("first", ref s_lineBoxFirst);
            var second = LineBoxFamily("second", ref s_lineBoxSecond);
            VelvetFonts.DefaultFamily = "first";
            VelvetStyleUtilities.AttachTo(_host.Root);
            var target = NewRootElement();
            _mounted = V.Mount(NewRootElement(),
                PortalInto(target, V.Label(className: "text-[16px]", text: "row", name: "row")));
            var row = target.Q<Label>("row");
            row.enableRichText = false;
            for (var i = 0; i < 3; i++)
            {
                EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            }

            // Act
            VelvetFonts.DefaultFamily = "second";
            for (var i = 0; i < 3; i++)
            {
                EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            }

            // Assert
            Assert.That(LineBoxFont.SourceOf(row.resolvedStyle.unityFontDefinition.fontAsset), Is.SameAs(second));
        }
    }
}
