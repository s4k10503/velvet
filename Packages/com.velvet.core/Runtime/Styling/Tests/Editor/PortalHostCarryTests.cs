using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a panel Velvet creates for a portal carries the stylesheets of the element its tree was mounted on
    /// and of that element's ancestors, and the classes of the document roots above it, rather than anything of the
    /// portal's own position, and follows them at each tick of that element's panel.
    /// </summary>
    [TestFixture]
    internal sealed class PortalHostCarryTests
    {
        // Collection is counted over several roots rather than asserted of one, the way ThemeBindingTests counts it.
        private const int Roots = 10;

        private const string RootRulesSheetPath =
            "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/PortalHostRootRules.uss";

        private static readonly FieldInfo ImportsField =
            typeof(StyleSheet).GetField("imports", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<StyleSheet> _created = new();
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private bool _darkBefore;

        [SetUp]
        public void SetUp()
        {
            _darkBefore = VelvetTheme.IsDark;
            VelvetTheme.IsDark = false;
            _host = new HeadlessEditorPanelHost();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host.Dispose();
            VelvetTheme.IsDark = _darkBefore;
            foreach (var sheet in _created) UnityEngine.Object.DestroyImmediate(sheet);
            _created.Clear();
        }

        // A dark section of the tree, holding a portal the way a component inside it would.
        [Component]
        private static VNode PortalInADarkSection() => V.Div(className: VelvetStyleUtilities.DarkThemeClass,
            name: "section",
            children: new VNode[] { V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }) });

        private T Created<T>(string name) where T : StyleSheet
        {
            var sheet = ScriptableObject.CreateInstance<T>();
            sheet.name = name;
            _created.Add(sheet);
            return sheet;
        }

        // Mounted and ticked, so both the mount and the tick after it have already looked.
        private void MountAndTick(VNode tree)
        {
            _mounted = V.Mount(_host.Root, tree);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        // The framework hosts are hidden objects, which FindObjectsOfTypeAll still sees.
        private static VisualElement HostRootHolding(string name) => Resources.FindObjectsOfTypeAll<UIDocument>()
            .Select(document => document.rootVisualElement)
            .First(root => root?.Q<VisualElement>(name) != null);

        private static VisualElement Rendered(string name) => HostRootHolding(name).Q<VisualElement>(name);

        private static bool Reaches(VisualElement element, StyleSheet sheet)
        {
            for (var current = element; current != null; current = current.hierarchy.parent)
            {
                if (current.styleSheets.Contains(sheet)) return true;
            }

            return false;
        }

        private static bool IsDarkAtOrAbove(VisualElement element)
        {
            for (var current = element; current != null; current = current.hierarchy.parent)
            {
                if (current.ClassListContains(VelvetStyleUtilities.DarkThemeClass)) return true;
            }

            return false;
        }

        private static string SheetNames(VisualElement element)
        {
            var names = new List<string>();
            for (var i = 0; i < element.styleSheets.count; i++) names.Add(element.styleSheets[i].name);
            return string.Join(", ", names);
        }

        // Mounts one world-space panel per name, disposes the tree and ticks its panel, and hands back weak references
        // to the host roots, held nowhere else here.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private List<WeakReference> DisposedWorldSpaceHostRoots(params string[] names)
        {
            _mounted = V.Mount(_host.Root, V.Fragment(names
                .Select(name => (VNode)V.WorldSpace(Vector3.zero, key: name, children: new VNode[] { V.Div(name: name) }))
                .ToArray()));
            var roots = names.Select(name => new WeakReference(HostRootHolding(name))).ToList();
            _mounted.Dispose();
            _mounted = null;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            return roots;
        }

        [Test]
        public void Given_ADeclaringRootCarryingTheAppsOwnSheet_When_ALayerPortalMounts_Then_ItsChildrenReachIt()
        {
            // Arrange
            var own = Created<StyleSheet>("AppSheet");
            _host.Root.styleSheets.Add(own);

            // Act
            _mounted = V.Mount(_host.Root, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));

            // Assert
            Assert.That(Reaches(Rendered("overlaid"), own), Is.True);
        }

        [Test]
        public void Given_ADeclaringRootCarryingTheAppsOwnSheet_When_AWorldSpacePanelMounts_Then_ItsChildrenReachIt()
        {
            // Arrange
            var own = Created<StyleSheet>("AppSheet");
            _host.Root.styleSheets.Add(own);

            // Act
            _mounted = V.Mount(_host.Root,
                V.WorldSpace(Vector3.zero, children: new VNode[] { V.Div(name: "in-world") }));

            // Assert
            Assert.That(Reaches(Rendered("in-world"), own), Is.True);
        }

        [Test]
        public void Given_ALayerPortalMountedAndTicked_When_TheAppAddsItsOwnSheetToTheDeclaringRootAndThatPanelTicks_Then_ItsChildrenReachIt()
        {
            // Arrange — no patch follows, so only the declaring panel's tick can carry it.
            MountAndTick(V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));
            var own = Created<StyleSheet>("AppSheet");
            _host.Root.styleSheets.Add(own);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(Reaches(Rendered("overlaid"), own), Is.True);
        }

        [Test]
        public void Given_AHostCarryingTheAppsOwnSheet_When_ItComesOffTheDeclaringRootAndThatPanelTicks_Then_ItsChildrenNoLongerReachIt()
        {
            // Arrange
            var own = Created<StyleSheet>("AppSheet");
            _host.Root.styleSheets.Add(own);
            _mounted = V.Mount(_host.Root, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));
            var carried = Reaches(Rendered("overlaid"), own);

            // Act
            _host.Root.styleSheets.Remove(own);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert — carried first, so an absence now says it came off.
            Assert.That((carried, Reaches(Rendered("overlaid"), own)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADeclaringRootCarryingASheetTheHostsPanelAlreadyHolds_When_ItIsCarried_Then_OnlyTheOthersAreAdded()
        {
            // Arrange — the parent stands for the host's panel, holding the theme its settings copy put there. The
            // app's own theme is on the root too, which nothing else brings to the host.
            var hostPanel = new VisualElement();
            var hostRoot = new VisualElement();
            hostPanel.Add(hostRoot);
            var copiedTheme = Created<ThemeStyleSheet>("CopiedTheme");
            hostPanel.styleSheets.Add(copiedTheme);
            var appTheme = Created<ThemeStyleSheet>("AppTheme");
            var declaringRoot = new VisualElement();
            declaringRoot.styleSheets.Add(copiedTheme);
            declaringRoot.styleSheets.Add(appTheme);
            declaringRoot.styleSheets.Add(Created<StyleSheet>("AppSheet"));

            // Act
            VelvetStyleUtilities.CarryToHost(declaringRoot, hostRoot);

            // Assert
            Assert.That(SheetNames(hostRoot), Is.EqualTo("AppTheme, AppSheet"));
        }

        [Test]
        public void Given_TheUtilitiesBetweenTwoAppSheetsOnTheDeclaringRoot_When_TheyAreCarried_Then_TheyKeepTheirPlace()
        {
            // Arrange
            var declaringRoot = new VisualElement();
            declaringRoot.styleSheets.Add(Created<StyleSheet>("Before"));
            declaringRoot.styleSheets.Add(VelvetStyleUtilities.Sheet);
            declaringRoot.styleSheets.Add(Created<StyleSheet>("After"));
            var hostRoot = new VisualElement();

            // Act
            VelvetStyleUtilities.CarryToHost(declaringRoot, hostRoot);

            // Assert
            Assert.That(SheetNames(hostRoot), Is.EqualTo($"Before, {VelvetStyleUtilities.Sheet.name}, After"));
        }

        [Test]
        public void Given_SheetsOnTheDeclaringRootAndItsParent_When_TheyAreCarried_Then_TheHostRootHoldsThemInCascadeOrder()
        {
            // Arrange — the parent's sheet appears again on the root, where it ranks above the root's other sheet.
            var parent = new VisualElement();
            var declaringRoot = new VisualElement();
            parent.Add(declaringRoot);
            var first = Created<StyleSheet>("First");
            var second = Created<StyleSheet>("Second");
            parent.styleSheets.Add(first);
            declaringRoot.styleSheets.Add(second);
            declaringRoot.styleSheets.Add(first);
            var hostRoot = new VisualElement();

            // Act
            VelvetStyleUtilities.CarryToHost(declaringRoot, hostRoot);

            // Assert
            Assert.That(SheetNames(hostRoot), Is.EqualTo("Second, First"));
        }

        [Test]
        public void Given_AHostCarryingASheetAndGivenOneOfItsOwnAfter_When_ItIsCarriedAgainWithNothingChanged_Then_ItsOwnStaysLast()
        {
            // Arrange
            var declaringRoot = new VisualElement();
            declaringRoot.styleSheets.Add(Created<StyleSheet>("Carried"));
            var hostRoot = new VisualElement();
            VelvetStyleUtilities.CarryToHost(declaringRoot, hostRoot);
            hostRoot.styleSheets.Add(Created<StyleSheet>("HostsOwn"));

            // Act
            VelvetStyleUtilities.CarryToHost(declaringRoot, hostRoot);

            // Assert
            Assert.That(SheetNames(hostRoot), Is.EqualTo("Carried, HostsOwn"));
        }

        [Test]
        public void Given_APortalInsideADarkSectionOfALightTree_When_ItMounts_Then_ItsChildrenSitUnderNoDarkClass()
        {
            // Act
            _mounted = V.Mount(_host.Root, V.Component(PortalInADarkSection, key: "root"));

            // Assert — the section's own class is the control: it says the portal was declared somewhere dark.
            var section = _host.Root.Q<VisualElement>("section");
            Assert.That((section.ClassListContains(VelvetStyleUtilities.DarkThemeClass), IsDarkAtOrAbove(Rendered("overlaid"))),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ALayerPortalMountedAndTickedUnderALightRoot_When_TheAppGivesTheRootTheDarkClassAndThatPanelTicks_Then_ItsChildrenSitUnderIt()
        {
            // Arrange — no patch follows, so only the declaring panel's tick can carry it.
            MountAndTick(V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));
            _host.Root.AddToClassList(VelvetStyleUtilities.DarkThemeClass);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(IsDarkAtOrAbove(Rendered("overlaid")), Is.True);
        }

        [Test]
        public void Given_AHostCarryingTheSheetBesideACopyThatImportsNothing_When_TheCopysImportListIsEditedInPlace_Then_TheSheetComesOff()
        {
            // Arrange — a copy under the utilities' name matches them once its imports are theirs. It imports
            // nothing yet, so the sheet is carried beside it; the importer moves the content hash with the text.
            var copy = Created<StyleSheet>(VelvetStyleUtilities.Sheet.name);
            var hostRoot = new VisualElement();
            hostRoot.styleSheets.Add(copy);
            var declaredAt = new VisualElement();
            declaredAt.styleSheets.Add(VelvetStyleUtilities.Sheet);
            VelvetStyleUtilities.CarryToHost(declaredAt, hostRoot);
            var carried = hostRoot.styleSheets.Contains(VelvetStyleUtilities.Sheet);
            ImportsField.SetValue(copy, ((Array)ImportsField.GetValue(VelvetStyleUtilities.Sheet)).Clone());
            copy.contentHash = 1;

            // Act
            VelvetStyleUtilities.CarryToHost(declaredAt, hostRoot);

            // Assert
            Assert.That((carried, hostRoot.styleSheets.Contains(VelvetStyleUtilities.Sheet)), Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the base lets a bound root decide the dark class over an ancestor's.
        [Test]
        public void Given_ARootBoundToALightThemeUnderAnAncestorWithTheDarkClass_When_ItIsCarried_Then_TheHostRootStaysLight()
        {
            // Arrange
            var ancestor = new VisualElement();
            ancestor.AddToClassList(VelvetStyleUtilities.DarkThemeClass);
            var declaringRoot = new VisualElement();
            ancestor.Add(declaringRoot);
            VelvetStyleUtilities.BindThemeTo(declaringRoot);
            var hostRoot = new VisualElement();

            // Act
            VelvetStyleUtilities.CarryToHost(declaringRoot, hostRoot);

            // Assert
            Assert.That(hostRoot.ClassListContains(VelvetStyleUtilities.DarkThemeClass), Is.False);
        }

        [Test]
        public void Given_AHostCarryingTheSheetBesideACopyOnTheDeclaringRoot_When_TheCopysImportsAreEditedInPlaceAndThatPanelTicks_Then_TheSheetComesOff()
        {
            // Arrange — a copy under the utilities' name matches them once its imports are theirs; importing nothing
            // yet, it leaves the sheet carried. No patch follows, so only the declaring panel's tick can see the edit.
            var copy = Created<StyleSheet>(VelvetStyleUtilities.Sheet.name);
            _host.Root.styleSheets.Add(VelvetStyleUtilities.Sheet);
            _host.Root.styleSheets.Add(copy);
            MountAndTick(V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));
            var hostRoot = HostRootHolding("overlaid");
            var carried = hostRoot.styleSheets.Contains(VelvetStyleUtilities.Sheet);
            ImportsField.SetValue(copy, ((Array)ImportsField.GetValue(VelvetStyleUtilities.Sheet)).Clone());
            copy.contentHash = 1;

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That((carried, hostRoot.styleSheets.Contains(VelvetStyleUtilities.Sheet)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AHostCarryingTheSheetBesideASheetImportingACopy_When_TheCopysImportsAreEditedInPlaceAndThatPanelTicks_Then_TheSheetComesOff()
        {
            // Arrange — the copy is reached only through the outer sheet's import, whose own content hash the edit
            // leaves alone. No patch follows, so only the declaring panel's tick can see the edit.
            var copy = Created<StyleSheet>(VelvetStyleUtilities.Sheet.name);
            var outer = Created<StyleSheet>("Outer");
            var importType = ImportsField.FieldType.GetElementType();
            var import = Activator.CreateInstance(importType);
            importType.GetField("styleSheet").SetValue(import, copy);
            var imports = Array.CreateInstance(importType, 1);
            imports.SetValue(import, 0);
            ImportsField.SetValue(outer, imports);
            _host.Root.styleSheets.Add(VelvetStyleUtilities.Sheet);
            _host.Root.styleSheets.Add(outer);
            MountAndTick(V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));
            var hostRoot = HostRootHolding("overlaid");
            var carried = hostRoot.styleSheets.Contains(VelvetStyleUtilities.Sheet);
            ImportsField.SetValue(copy, ((Array)ImportsField.GetValue(VelvetStyleUtilities.Sheet)).Clone());
            copy.contentHash = 1;

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That((carried, hostRoot.styleSheets.Contains(VelvetStyleUtilities.Sheet)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ClassesOnThePanelRootTheDocumentRootAndTheMountElement_When_ALayerPortalMounts_Then_TheHostRootTakesTheDocumentRootsOnly()
        {
            // Arrange — the panel root holds html's place and the element under it body's; the mount element is inside
            // the page. The unity- class names the element carrying it and stays there.
            var document = new VisualElement();
            var app = new VisualElement();
            _host.Root.Add(document);
            document.Add(app);
            _host.Root.AddToClassList("theme-html");
            _host.Root.AddToClassList("unity-probe");
            document.AddToClassList("theme-body");
            app.AddToClassList("theme-app");

            // Act
            _mounted = V.Mount(app, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));

            // Assert
            var hostRoot = HostRootHolding("overlaid");
            Assert.That(
                (hostRoot.ClassListContains("theme-html"), hostRoot.ClassListContains("theme-body"),
                    hostRoot.ClassListContains("theme-app"), hostRoot.ClassListContains("unity-probe")),
                Is.EqualTo((true, true, false, false)));
        }

        [Test]
        public void Given_ALayerPortalMountedAndTicked_When_ThePanelRootGainsAClassAndThatPanelTicks_Then_TheHostRootTakesIt()
        {
            // Arrange — no patch follows, so only the declaring panel's tick can carry it.
            MountAndTick(V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));
            _host.Root.AddToClassList("theme-ocean");

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(HostRootHolding("overlaid").ClassListContains("theme-ocean"), Is.True);
        }

        [Test]
        public void Given_AHostCarryingAPanelRootClass_When_ItComesOffThePanelRootAndThatPanelTicks_Then_ItComesOffTheHostRoot()
        {
            // Arrange
            _host.Root.AddToClassList("theme-ocean");
            _mounted = V.Mount(_host.Root, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));
            var hostRoot = HostRootHolding("overlaid");
            var carried = hostRoot.ClassListContains("theme-ocean");

            // Act
            _host.Root.RemoveFromClassList("theme-ocean");
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert — carried first, so an absence now says it came off.
            Assert.That((carried, hostRoot.ClassListContains("theme-ocean")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ACarriedSheetWhoseRootRuleSetsEveryBoxLonghand_When_ALayerPortalMounts_Then_TheHostRootKeepsItsBoxAndItsChildrenTakeTheColour()
        {
            // Arrange — the colour is inherited, so it reaching the child is the control that the rule was carried.
            // Loaded tolerantly: a tree without the fixture carries no rule, which the colour term reports.
            var rules = AssetDatabase.LoadAssetAtPath<StyleSheet>(RootRulesSheetPath);
            if (rules != null) _host.Root.styleSheets.Add(rules);

            // Act
            _mounted = V.Mount(_host.Root, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));

            // Assert — a bare element on the host's panel holds every initial value; the root's offsets and size are
            // compared with the panel's root, which it fills. Overflow and translate have no resolved reading, so
            // their inline pins are read instead.
            var hostRoot = HostRootHolding("overlaid");
            var bare = new VisualElement();
            hostRoot.panel.visualTree.Add(bare);
            EditorPanelTestHelpers.ForcePanelUpdate(hostRoot.panel);
            var panel = hostRoot.panel.visualTree.resolvedStyle;
            var root = hostRoot.resolvedStyle;
            var inline = hostRoot.style;
            var reading = string.Join(" ",
                BoxLonghands(root) == BoxLonghands(bare.resolvedStyle),
                inline.overflow.value, inline.translate.keyword,
                root.position, root.left, root.top, root.right, root.bottom,
                root.width == panel.width, root.height == panel.height,
                Rendered("overlaid").resolvedStyle.color == Color.green);
            bare.RemoveFromHierarchy();
            Assert.That(reading, Is.EqualTo("True Visible None Absolute 0 0 0 0 True True True"));
        }

        // The longhands a resolved style reads, bar the offsets and size.
        private static string BoxLonghands(IResolvedStyle style) => string.Join(" ",
            style.backgroundColor, style.backgroundImage,
            style.borderTopWidth, style.borderRightWidth, style.borderBottomWidth, style.borderLeftWidth,
            style.borderTopLeftRadius, style.borderTopRightRadius, style.borderBottomRightRadius,
            style.borderBottomLeftRadius,
            style.paddingTop, style.paddingRight, style.paddingBottom, style.paddingLeft,
            style.marginTop, style.marginRight, style.marginBottom, style.marginLeft,
            style.flexDirection, style.flexWrap, style.alignItems, style.alignContent, style.justifyContent,
            style.opacity, style.display, style.rotate, style.scale,
            style.minWidth, style.minHeight, style.maxWidth, style.maxHeight);

        [Test]
        public void Given_ACarriedSheetWhoseRootRuleSetsAWidth_When_AWorldSpacePanelMounts_Then_ItsHostRootKeepsItsPanelSizeAndTakesTheDocumentRootClass()
        {
            // Arrange
            var rules = AssetDatabase.LoadAssetAtPath<StyleSheet>(RootRulesSheetPath);
            if (rules != null) _host.Root.styleSheets.Add(rules);

            // Act
            _mounted = V.Mount(_host.Root, V.WorldSpace(Vector3.zero, panelSize: new Vector2(600f, 200f),
                children: new VNode[] { V.Div(name: "in-world") }));

            // Assert
            var hostRoot = HostRootHolding("in-world");
            Assert.That(
                (hostRoot.style.width.keyword, hostRoot.style.width.value.value,
                    hostRoot.ClassListContains("unity-ui-document__root")),
                Is.EqualTo((StyleKeyword.Undefined, 600f, true)));
        }

        // GREEN_ON_BASE(characterization): the base keeps no host root once its tree is disposed.
        [Test]
        public void Given_WorldSpacePanelsMounted_When_TheirTreeIsDisposedAndItsPanelTicks_Then_TheirHostRootsCanBeCollected()
        {
            // Arrange
            var roots = DisposedWorldSpaceHostRoots(Enumerable.Range(0, Roots).Select(i => $"host-{i}").ToArray());

            // Act
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Assert
            Assert.That(roots.Count(root => root.IsAlive), Is.LessThan(Roots / 2));
        }

        [Test]
        public void Given_TheDarkClassOnTheDocumentRootUnderALightPanelRoot_When_ATreeInsideItMountsALayerPortal_Then_ItsChildrenSitUnderIt()
        {
            // Arrange — body's place, as a UIDocument's root takes it under the panel root.
            var document = new VisualElement();
            var app = new VisualElement();
            _host.Root.Add(document);
            document.Add(app);
            document.AddToClassList(VelvetStyleUtilities.DarkThemeClass);

            // Act
            _mounted = V.Mount(app, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));

            // Assert
            Assert.That(HostRootHolding("overlaid").ClassListContains(VelvetStyleUtilities.DarkThemeClass), Is.True);
        }

        [Test]
        public void Given_ALayerPortalMountedFromInsideThePage_When_TheMountElementIsBoundToTheThemeAndDarkModeTurnsOn_Then_ItsChildrenSitUnderTheDarkClass()
        {
            // Arrange — the mount element sits below the document roots, so its own class never moves the stamp; only
            // the binding can.
            var document = new VisualElement();
            var app = new VisualElement();
            _host.Root.Add(document);
            document.Add(app);
            _mounted = V.Mount(app, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            VelvetStyleUtilities.BindThemeTo(app);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(IsDarkAtOrAbove(Rendered("overlaid")), Is.True);
        }
    }
}
