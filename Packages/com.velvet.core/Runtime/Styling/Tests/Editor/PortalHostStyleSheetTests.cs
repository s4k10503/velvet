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

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp() => _host = new HeadlessEditorPanelHost();

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host.Dispose();
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
        public void Given_ADeclaringPanelCarryingTheSheet_When_ALayerPortalMounts_Then_ItsChildrenReachTheSheet()
        {
            // Arrange
            _host.Root.styleSheets.Add(VelvetStyleUtilities.Sheet);

            // Act
            _mounted = V.Mount(_host.Root, V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));

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
