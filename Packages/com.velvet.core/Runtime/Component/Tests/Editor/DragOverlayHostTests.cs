using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what V.DragOverlay shows around a drop animation, driven through the handle a drop animation
    /// drives, on a real editor panel: the children it keeps through the animation go once the animation ends, a
    /// drop that starts after its children were cleared shows none of them, and where it stays in place is decided
    /// by the panel the tree is mounted in rather than by where a Portal put the overlay.
    /// </summary>
    [TestFixture]
    internal sealed class DragOverlayHostTests
    {
        private HeadlessEditorPanelHost _host;
        private VisualElement _mountPoint;
        private MountedTree _mounted;

        private static StateUpdater<bool> s_setShow;
        private static VisualElement s_portalTarget;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _mountPoint = new VisualElement { name = "mount" };
            _host.Root.Add(_mountPoint);
            s_setShow = default;
            s_portalTarget = null;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            s_portalTarget = null;
        }

        [Component]
        private static VNode OverlayScene()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShow = setShow;
            return V.DragOverlay(key: "overlay", children: show
                ? new VNode[] { V.Label("ghost", name: "ghost", key: "ghost") }
                : System.Array.Empty<VNode>());
        }

        [Component]
        private static VNode PortaledOverlayScene() => V.Portal(s_portalTarget, key: "portal", children: new VNode[]
        {
            V.DragOverlay(key: "overlay", children: new VNode[] { V.Label("ghost", name: "ghost", key: "ghost") }),
        });

        private VisualElement Ghost() => _mountPoint.Q<VisualElement>("ghost");

        private DragOverlayHandle Handle()
        {
            for (var fiber = _mounted.Root; fiber != null; fiber = fiber.Child)
            {
                if (fiber.Props is DragOverlayHost.Props) return ((Ref<DragOverlayHandle>)fiber.RefSlots![0].Ref).Current;
            }
            return null;
        }

        // GREEN_ON_BASE(characterization): the base already shows what is asserted here; no case asserted it before.
        [Test]
        public void Given_AnOverlayKeepingItsChildrenThroughADrop_When_TheDropEnds_Then_ItShowsNone()
        {
            // Arrange
            _mounted = V.Mount(_mountPoint, V.Component(OverlayScene, key: "root"));
            var handle = Handle();
            handle.BeginDrop();
            s_setShow.Invoke(false);
            _mounted.FlushStateForTest();
            var keptThroughTheDrop = Ghost() != null;

            // Act
            handle.EndDrop();
            _mounted.FlushStateForTest();

            // Assert — a value that is not null where the drop never kept the children.
            Assert.That(keptThroughTheDrop ? Ghost() : (object)"the drop did not keep the children", Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base already shows what is asserted here; no case asserted it before.
        [Test]
        public void Given_AnOverlayWhoseChildrenWereClearedBeforeADrop_When_ItRendersDuringTheDrop_Then_ItShowsNone()
        {
            // Arrange
            _mounted = V.Mount(_mountPoint, V.Component(OverlayScene, key: "root"));
            var handle = Handle();
            s_setShow.Invoke(false);
            _mounted.FlushStateForTest();
            handle.BeginDrop();

            // Act
            handle.Rerender.Invoke();
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Ghost(), Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base already shows what is asserted here; no case asserted it before.
        [Test]
        public void Given_AnOverlayPortaledOutOfAnEditorPanelMount_When_Mounted_Then_ItsChildrenStayInThePortal()
        {
            // Arrange
            s_portalTarget = new VisualElement { name = "portal-target" };

            // Act
            _mounted = V.Mount(_mountPoint, V.Component(PortaledOverlayScene, key: "root"));

            // Assert
            Assert.That(s_portalTarget.Q<VisualElement>("ghost"), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base already shows what is asserted here; no case asserted it before.
        [Test]
        public void Given_TheOverlaysComponent_When_TheCompilerTransformHasRun_Then_ItLeftTheBodyUnwoven()
        {
            // Arrange
            var render = typeof(DragOverlayHost).GetMethod(nameof(DragOverlayHost.Render), BindingFlags.Static | BindingFlags.Public);

            // Act
            var report = render!.GetCustomAttribute<MemoizationReportAttribute>();

            // Assert
            Assert.That(report, Is.Null);
        }
    }
}
