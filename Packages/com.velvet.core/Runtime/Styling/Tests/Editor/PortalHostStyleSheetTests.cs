using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a panel Velvet creates for a portal carries the bundled utility stylesheet exactly where the
    /// panel the portal was declared on reaches it, so the portal's children resolve the utilities their
    /// declaring side does.
    /// </summary>
    [TestFixture]
    internal sealed class PortalHostStyleSheetTests
    {
        private const string ImportingSheetPath =
            "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ImportsVelvetUtilities.uss";

        private const string PartialSheetPath = "Packages/com.velvet.core/Runtime/Styles/_palette.uss";

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private bool _darkBefore;

        private static StateUpdater<bool> s_setFlag;

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
        }

        // Re-renders its portal's child with a different class when the flag flips, which patches the portal.
        [Component]
        private static VNode ReclassedLayerPortal()
        {
            var (flipped, setFlipped) = Hooks.UseState(false);
            s_setFlag = setFlipped;
            return V.Portal(UILayer.Overlay, key: "p",
                children: new VNode[] { V.Div(className: flipped ? "flipped" : null, name: "overlaid") });
        }

        [Component]
        private static VNode ReclassedWorldSpace()
        {
            var (flipped, setFlipped) = Hooks.UseState(false);
            s_setFlag = setFlipped;
            return V.WorldSpace(Vector3.zero, key: "ws",
                children: new VNode[] { V.Div(className: flipped ? "flipped" : null, name: "in-world") });
        }

        private static bool IsDarkAtOrAbove(VisualElement element)
        {
            for (var current = element; current != null; current = current.hierarchy.parent)
            {
                if (current.ClassListContains(VelvetStyleUtilities.DarkThemeClass)) return true;
            }

            return false;
        }

        // Mounted before the sheet reaches the declaring panel, and ticked, so both the mount and the tick after it
        // have looked and found nothing to carry.
        private void MountAndTickWithoutTheSheet(VNode tree)
        {
            _mounted = V.Mount(_host.Root, tree);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        // The framework hosts are hidden objects, which FindObjectsOfTypeAll still sees.
        private static VisualElement Rendered(string name) => Resources.FindObjectsOfTypeAll<UIDocument>()
            .Select(document => document.rootVisualElement?.Q<VisualElement>(name))
            .First(element => element != null);

        private static bool ReachesTheSheet(VisualElement element)
        {
            for (var current = element; current != null; current = current.hierarchy.parent)
            {
                if (current.styleSheets.Contains(VelvetStyleUtilities.Sheet)) return true;
            }

            return false;
        }

        [Test]
        public void Given_ADeclaringPanelCarryingTheSheet_When_ALayerPortalMountsAndThatPanelTicks_Then_ItsChildrenReachTheSheet()
        {
            // Arrange
            _host.Root.styleSheets.Add(VelvetStyleUtilities.Sheet);

            // Act — the tick asks again, with the sheet already on the host.
            _mounted = V.Mount(_host.Root, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(ReachesTheSheet(Rendered("overlaid")), Is.True);
        }

        [Test]
        public void Given_ALayerPortalMounted_When_TheSheetReachesItsDeclaringPanelBeforeThatPanelTicks_Then_ItsChildrenReachTheSheet()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));

            // Act
            _host.Root.styleSheets.Add(VelvetStyleUtilities.Sheet);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(ReachesTheSheet(Rendered("overlaid")), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base gives no host the sheet.
        [Test]
        public void Given_ADeclaringPanelWithoutTheSheet_When_ALayerPortalMounts_Then_ItsChildrenDoNotReachIt()
        {
            // Act
            _mounted = V.Mount(_host.Root, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));

            // Assert
            Assert.That(ReachesTheSheet(Rendered("overlaid")), Is.False);
        }

        [Test]
        public void Given_ADeclaringPanelCarryingTheSheet_When_AWorldSpacePanelMounts_Then_ItsChildrenReachTheSheet()
        {
            // Arrange
            _host.Root.styleSheets.Add(VelvetStyleUtilities.Sheet);

            // Act
            _mounted = V.Mount(_host.Root,
                V.WorldSpace(Vector3.zero, children: new VNode[] { V.Div(name: "in-world") }));

            // Assert
            Assert.That(ReachesTheSheet(Rendered("in-world")), Is.True);
        }

        [Test]
        public void Given_ALayerPortalMountedAndTickedWithoutTheSheet_When_TheSheetArrivesAndThePortalIsPatched_Then_ItsChildrenReachIt()
        {
            // Arrange
            MountAndTickWithoutTheSheet(V.Component(ReclassedLayerPortal, key: "root"));
            _host.Root.styleSheets.Add(VelvetStyleUtilities.Sheet);

            // Act
            s_setFlag.Invoke(true);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(ReachesTheSheet(Rendered("overlaid")), Is.True);
        }

        [Test]
        public void Given_AWorldSpacePanelMountedAndTickedWithoutTheSheet_When_TheSheetArrivesAndItIsPatched_Then_ItsChildrenReachIt()
        {
            // Arrange
            MountAndTickWithoutTheSheet(V.Component(ReclassedWorldSpace, key: "root"));
            _host.Root.styleSheets.Add(VelvetStyleUtilities.Sheet);

            // Act
            s_setFlag.Invoke(true);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(ReachesTheSheet(Rendered("in-world")), Is.True);
        }

        [Test]
        public void Given_ALayerPortalDeclaredUnderARootBoundToTheTheme_When_DarkModeTurnsOn_Then_ItsChildrenSitUnderTheDarkClass()
        {
            // Arrange — bound without the sheet, as a project reaching the sheet through its theme binds it. Dark
            // mode turns on after the mount, so a class copied at the mount would still read light.
            VelvetStyleUtilities.BindThemeTo(_host.Root);
            _mounted = V.Mount(_host.Root, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(IsDarkAtOrAbove(Rendered("overlaid")), Is.True);
        }

        [Test]
        public void Given_ADeclaringRootTheAppGaveTheDarkClass_When_ALayerPortalMounts_Then_ItsChildrenSitUnderTheDarkClass()
        {
            // Arrange — dark mode itself stays off, so only the class the app wrote can carry it.
            _host.Root.AddToClassList(VelvetStyleUtilities.DarkThemeClass);

            // Act
            _mounted = V.Mount(_host.Root, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));

            // Assert
            Assert.That(IsDarkAtOrAbove(Rendered("overlaid")), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base gives no host the dark class.
        [Test]
        public void Given_ADeclaringRootNeitherBoundNorDark_When_ALayerPortalMounts_Then_ItsChildrenSitUnderNoDarkClass()
        {
            // Act
            _mounted = V.Mount(_host.Root, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));

            // Assert
            Assert.That(IsDarkAtOrAbove(Rendered("overlaid")), Is.False);
        }

        [Test]
        public void Given_AHostCarryingTheSheet_When_ItComesToReachTheUtilitiesThroughAnImport_Then_TheSheetComesOff()
        {
            // Arrange — the parent stands for a panel whose copied theme, settled after the host was first
            // carried, imports the utilities.
            var hostPanel = new VisualElement();
            var hostRoot = new VisualElement();
            hostPanel.Add(hostRoot);
            var declaredAt = new VisualElement();
            declaredAt.styleSheets.Add(VelvetStyleUtilities.Sheet);
            VelvetStyleUtilities.CarryToHost(declaredAt, hostRoot);
            hostPanel.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(ImportingSheetPath));

            // Act
            VelvetStyleUtilities.SyncHost(declaredAt, hostRoot);

            // Assert — carried first, so an absence now says it came off.
            Assert.That(hostRoot.styleSheets.Contains(VelvetStyleUtilities.Sheet), Is.False);
        }

        [Test]
        public void Given_AHostCarryingAnUnrelatedSheet_When_TheSheetIsCarriedToIt_Then_ItIsAttached()
        {
            // Arrange — a partial the utilities import, which is not the utilities and imports nothing that is.
            var hostRoot = new VisualElement();
            hostRoot.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(PartialSheetPath));
            var declaredAt = new VisualElement();
            declaredAt.styleSheets.Add(VelvetStyleUtilities.Sheet);

            // Act
            VelvetStyleUtilities.CarryToHost(declaredAt, hostRoot);

            // Assert
            Assert.That(hostRoot.styleSheets.Contains(VelvetStyleUtilities.Sheet), Is.True);
        }

        [Test]
        public void Given_AHostReachingTheUtilitiesThroughAnImport_When_TheSheetIsCarriedToIt_Then_ItIsNotAttachedAgain()
        {
            // Arrange — the import stands for a declaring theme that imports the utilities, which a layer host
            // copies; attaching the sheet on top would put its rules above that theme's own overrides.
            var hostRoot = new VisualElement();
            hostRoot.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(ImportingSheetPath));
            var declaredAt = new VisualElement();
            declaredAt.styleSheets.Add(VelvetStyleUtilities.Sheet);

            // Act
            VelvetStyleUtilities.CarryToHost(declaredAt, hostRoot);

            // Assert
            Assert.That(hostRoot.styleSheets.Contains(VelvetStyleUtilities.Sheet), Is.False);
        }
    }
}
