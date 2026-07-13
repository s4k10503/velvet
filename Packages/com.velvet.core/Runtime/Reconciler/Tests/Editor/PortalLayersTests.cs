using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <c>V.Portal(layer:)</c> and <c>V.WorldSpace</c>:
    /// <list type="bullet">
    /// <item>A layer portal lazily creates ONE framework-owned host panel per layer per reconciler
    /// (screen-space overlay, sorted Background &lt; Overlay &lt; Topmost) and mounts its children
    /// there; two portals on one layer share the host; context crosses the boundary like every
    /// portal; teardown removes the children, and disposing the tree destroys the hosts.</item>
    /// <item>A WorldSpace node creates a per-instance world-space host (render mode WorldSpace,
    /// transform-positioned, fixed virtual panel size), follows position patches, mounts children
    /// inside, and destroys the host on unmount.</item>
    /// </list>
    /// Host accounting reads through Resources.FindObjectsOfTypeAll, which sees hidden objects.
    /// </summary>
    internal sealed class PortalLayersTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private readonly List<Object> _spawned = new();
        private HashSet<int> _baselineDocs;

        private static StateUpdater<bool> s_setFlag;
        private static string s_observedContext;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _baselineDocs = DocIds();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            FiberPortalRegistry.Unregister("late-target");
            FiberPortalRegistry.Unregister("swap-target");
            foreach (var obj in _spawned)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _spawned.Clear();
        }

        private static HashSet<int> DocIds()
        {
            var ids = new HashSet<int>();
            foreach (var doc in Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                ids.Add(doc.GetInstanceID());
            }
            return ids;
        }

        // The framework-created hosts are the UIDocuments that did not exist at fixture setup.
        private List<UIDocument> NewDocs()
        {
            var created = new List<UIDocument>();
            foreach (var doc in Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                if (!_baselineDocs.Contains(doc.GetInstanceID()))
                {
                    created.Add(doc);
                }
            }
            return created;
        }

        private void MountAndLayout(VNode node)
        {
            _mounted = V.Mount(_host.Root, node);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private void FlushAndLayout()
        {
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        #region Layer portals

        [Test]
        public void Given_ALayerPortal_When_Mounted_Then_AFrameworkHostPanelExists()
        {
            // Arrange & Act
            MountAndLayout(V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[]
                {
                    V.Div(name: "inside", className: "w-[10px] h-[10px]"),
                }),
            }));

            // Assert — exactly one framework host was created for the layer.
            Assert.That(NewDocs().Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ALayerPortal_When_Mounted_Then_ChildrenAttachUnderTheHostPanel()
        {
            // Arrange & Act
            MountAndLayout(V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[]
                {
                    V.Div(name: "inside", className: "w-[10px] h-[10px]"),
                }),
            }));

            // Assert — the child lives under the layer host's root, not under the main mount.
            var docs = NewDocs();
            Assume.That(docs.Count, Is.EqualTo(1), "Precondition: the layer host exists");
            Assert.That((docs[0].rootVisualElement.Q<VisualElement>("inside") != null,
                    _host.Root.Q<VisualElement>("inside") == null),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_TwoPortalsOnOneLayer_When_Mounted_Then_TheyShareOneHost()
        {
            // Arrange & Act
            MountAndLayout(V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Topmost, key: "a", children: new VNode[] { V.Div(name: "a1") }),
                V.Portal(UILayer.Topmost, key: "b", children: new VNode[] { V.Div(name: "b1") }),
            }));

            // Assert — one host, both children present under it.
            var docs = NewDocs();
            Assume.That(docs.Count, Is.EqualTo(1), "Precondition: a single shared host exists");
            var root = docs[0].rootVisualElement;
            Assert.That((root.Q<VisualElement>("a1") != null, root.Q<VisualElement>("b1") != null),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_DifferentLayers_When_Mounted_Then_SortingOrdersThePanels()
        {
            // Arrange & Act
            MountAndLayout(V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Background, key: "bg", children: new VNode[] { V.Div(name: "bg1") }),
                V.Portal(UILayer.Topmost, key: "top", children: new VNode[] { V.Div(name: "top1") }),
            }));

            // Assert — the background layer's panel sorts below the topmost layer's.
            float? background = null, topmost = null;
            foreach (var doc in NewDocs())
            {
                if (doc.rootVisualElement.Q<VisualElement>("bg1") != null) background = doc.panelSettings.sortingOrder;
                if (doc.rootVisualElement.Q<VisualElement>("top1") != null) topmost = doc.panelSettings.sortingOrder;
            }
            Assume.That(background.HasValue && topmost.HasValue, Is.True, "Precondition: both hosts exist");
            Assert.That(background.Value, Is.LessThan(topmost.Value));
        }

        private static readonly ComponentContext<string> s_stringContext = ComponentContext<string>.Create();

        [Component]
        private static VNode ContextReader()
        {
            s_observedContext = Hooks.UseContext(s_stringContext);
            return V.Div(name: "reader");
        }

        [Test]
        public void Given_AProviderAboveALayerPortal_When_TheChildReads_Then_ContextCrossesTheBoundary()
        {
            // Arrange & Act — the portal child is on another panel, but the LOGICAL tree carries context.
            s_observedContext = null;
            MountAndLayout(V.Provider(s_stringContext, value: "crossed", children: new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[]
                {
                    V.Component(ContextReader, key: "r"),
                }),
            }));

            // Assert
            Assert.That(s_observedContext, Is.EqualTo("crossed"));
        }

        [Component]
        private static VNode ConditionalLayerHost()
        {
            var (removed, setRemoved) = Hooks.UseState(false);
            s_setFlag = setRemoved;
            return V.Div(children: new VNode[]
            {
                removed ? null : V.Portal(UILayer.Overlay, key: "p", children: new VNode[]
                {
                    V.Div(name: "inside"),
                }),
            });
        }

        [Test]
        public void Given_AConditionalRemoval_When_ThePortalLeavesTheTree_Then_ChildrenLeaveTheHost()
        {
            // Arrange
            MountAndLayout(V.Component(ConditionalLayerHost, key: "root"));
            var docs = NewDocs();
            Assume.That(docs.Count == 1 && docs[0].rootVisualElement.Q<VisualElement>("inside") != null,
                Is.True, "Precondition: the child is mounted on the layer host");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That(docs[0].rootVisualElement.Q<VisualElement>("inside"), Is.Null);
        }

        [Test]
        public void Given_TreeDisposal_When_TheReconcilerTearsDown_Then_TheLayerHostsAreDestroyed()
        {
            // Arrange
            MountAndLayout(V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[] { V.Div(name: "inside") }),
            }));
            Assume.That(NewDocs().Count, Is.EqualTo(1), "Precondition: the layer host exists");

            // Act
            _mounted.Dispose();
            _mounted = null;

            // Assert
            Assert.That(NewDocs().Count, Is.EqualTo(0));
        }

        private static StateUpdater<int> s_bump;
        private static StateUpdater<bool> s_setRemoved2;

        [Component]
        private static VNode LatePortalHost()
        {
            var (_, bump) = Hooks.UseState(0);
            var (removed, setRemoved) = Hooks.UseState(false);
            s_bump = bump;
            s_setRemoved2 = setRemoved;
            return V.Div(children: new VNode[]
            {
                removed ? null : V.Portal("late-target", key: "p", children: new VNode[]
                {
                    V.Div(name: "inside"),
                }),
            });
        }

        [Test]
        public void Given_ATargetRegisteredAfterMount_When_Patched_Then_TheMountHeals()
        {
            // Arrange — a portal mounted before its id exists warns and stays empty; registering the id
            // must let the next patch mount the children instead of leaving the portal dead forever.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("late-target"));
            MountAndLayout(V.Component(LatePortalHost, key: "root"));
            var target = new VisualElement();
            FiberPortalRegistry.Register("late-target", target);

            // Act — an unrelated state bump re-renders the host and patches the portal.
            s_bump.Invoke(v => v + 1);
            FlushAndLayout();

            // Assert
            Assert.That(target.childCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_AHealedLateMount_When_ThePortalUnmounts_Then_TheChildrenAreRemoved()
        {
            // Arrange — the recorded target must follow the heal, or the eventual cleanup skips the
            // live children entirely (elements and effect cleanups leak on the healed target).
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("late-target"));
            MountAndLayout(V.Component(LatePortalHost, key: "root"));
            var target = new VisualElement();
            FiberPortalRegistry.Register("late-target", target);
            s_bump.Invoke(v => v + 1);
            FlushAndLayout();
            Assume.That(target.childCount, Is.EqualTo(1),
                "Precondition: the patch after registration healed the mount");

            // Act
            s_setRemoved2.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That(target.childCount, Is.EqualTo(0));
        }

        [Component]
        private static VNode GrowingIdPortalHost()
        {
            var (grown, setGrown) = Hooks.UseState(false);
            s_setFlag = setGrown;
            return V.Portal("swap-target", children: grown
                ? new VNode[] { V.Div(name: "one"), V.Div(name: "two") }
                : new VNode[] { V.Div(name: "one") });
        }

        [Test]
        public void Given_ATargetReRegisteredMidLife_When_ThePortalPatches_Then_ItKeepsItsMountedTarget()
        {
            // Arrange — re-registering an id points FUTURE portals elsewhere; a live portal's children
            // already occupy a slot range on the ORIGINAL element, so its patches must keep operating
            // there (patching into the new element would diff against another element's children).
            var original = new VisualElement();
            var replacement = new VisualElement();
            FiberPortalRegistry.Register("swap-target", original);
            MountAndLayout(V.Component(GrowingIdPortalHost, key: "root"));
            Assume.That(original.childCount, Is.EqualTo(1), "Precondition: mounted into the original target");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("swap-target"));
            FiberPortalRegistry.Register("swap-target", replacement);

            // Act — grow the portal's children after the re-registration.
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — both children live on the original target; the replacement stays untouched.
            Assert.That((original.childCount, replacement.childCount), Is.EqualTo((2, 0)));
        }

        #endregion

        #region World space

        [Test]
        public void Given_AWorldSpaceNode_When_Mounted_Then_AWorldSpaceHostExistsAtThePosition()
        {
            // Arrange & Act
            MountAndLayout(V.Div(children: new VNode[]
            {
                V.WorldSpace(new Vector3(1f, 2f, 3f), children: new VNode[] { V.Div(name: "ws1") }),
            }));

            // Assert — a dedicated world-space host at the requested transform.
            var docs = NewDocs();
            Assume.That(docs.Count, Is.EqualTo(1), "Precondition: the world-space host exists");
            Assert.That((docs[0].panelSettings.renderMode, docs[0].transform.position),
                Is.EqualTo((PanelRenderMode.WorldSpace, new Vector3(1f, 2f, 3f))));
        }

        [Test]
        public void Given_AWorldSpaceNode_When_Mounted_Then_ChildrenAttachInsideThePanel()
        {
            // Arrange & Act
            MountAndLayout(V.Div(children: new VNode[]
            {
                V.WorldSpace(Vector3.zero, children: new VNode[] { V.Div(name: "ws1") }),
            }));

            // Assert
            var docs = NewDocs();
            Assume.That(docs.Count, Is.EqualTo(1), "Precondition: the world-space host exists");
            Assert.That(docs[0].rootVisualElement.Q<VisualElement>("ws1"), Is.Not.Null);
        }

        [Test]
        public void Given_AWorldSpaceNode_When_Mounted_Then_ThePanelSizeApplies()
        {
            // Arrange & Act
            MountAndLayout(V.Div(children: new VNode[]
            {
                V.WorldSpace(Vector3.zero, panelSize: new Vector2(640f, 480f),
                    children: new VNode[] { V.Div(name: "ws1") }),
            }));

            // Assert
            var docs = NewDocs();
            Assume.That(docs.Count, Is.EqualTo(1), "Precondition: the world-space host exists");
            Assert.That(docs[0].worldSpaceSize, Is.EqualTo(new Vector2(640f, 480f)));
        }

        [Component]
        private static VNode MovingWorldSpaceHost()
        {
            var (moved, setMoved) = Hooks.UseState(false);
            s_setFlag = setMoved;
            return V.WorldSpace(moved ? new Vector3(5f, 0f, 0f) : Vector3.zero,
                key: "ws", children: new VNode[] { V.Div(name: "ws1") });
        }

        [Test]
        public void Given_APositionPatch_When_Repatched_Then_TheHostTransformFollows()
        {
            // Arrange
            MountAndLayout(V.Component(MovingWorldSpaceHost, key: "root"));
            var docs = NewDocs();
            Assume.That(docs.Count, Is.EqualTo(1), "Precondition: the world-space host exists");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That(docs[0].transform.position, Is.EqualTo(new Vector3(5f, 0f, 0f)));
        }

        [Component]
        private static VNode ConditionalWorldSpaceHost()
        {
            var (removed, setRemoved) = Hooks.UseState(false);
            s_setFlag = setRemoved;
            return V.Div(children: new VNode[]
            {
                removed ? null : V.WorldSpace(Vector3.zero, key: "ws",
                    children: new VNode[] { V.Div(name: "ws1") }),
            });
        }

        [Test]
        public void Given_AConditionalRemoval_When_TheWorldSpaceLeavesTheTree_Then_TheHostIsDestroyed()
        {
            // Arrange
            MountAndLayout(V.Component(ConditionalWorldSpaceHost, key: "root"));
            Assume.That(NewDocs().Count, Is.EqualTo(1), "Precondition: the world-space host exists");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That(NewDocs().Count, Is.EqualTo(0));
        }

        #endregion
    }
}
