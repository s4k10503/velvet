using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    /// <item>A deferred mount that cannot resolve where it goes is reported on its own: the portals and
    /// <c>z-*</c> placements queued behind it in the same pass still land, and the render that queued it
    /// still commits.</item>
    /// <item>A <c>z-*</c> placement resolves no target and sits outside that containment: an application
    /// throw from the insert that lands its element reaches the enclosing Error Boundary.</item>
    /// </list>
    /// Host accounting reads through Resources.FindObjectsOfTypeAll, which sees hidden objects.
    /// </summary>
    internal sealed class PortalLayersTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private HashSet<int> _baselineDocs;
        private HashSet<int> _baselineSettings;

        private static StateUpdater<bool> s_setFlag;
        private static string s_observedContext;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _baselineDocs = DocIds();
            _baselineSettings = SettingsIds();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            FiberPortalRegistry.Unregister("late-target");
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

        private static HashSet<int> SettingsIds()
        {
            var ids = new HashSet<int>();
            foreach (var settings in Resources.FindObjectsOfTypeAll<PanelSettings>())
            {
                ids.Add(settings.GetInstanceID());
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

        // The settings are created unattached and assigned to the document only later, so a host abandoned
        // before that assignment leaves settings no document reaches.
        private List<PanelSettings> NewSettings()
        {
            var created = new List<PanelSettings>();
            foreach (var settings in Resources.FindObjectsOfTypeAll<PanelSettings>())
            {
                if (!_baselineSettings.Contains(settings.GetInstanceID()))
                {
                    created.Add(settings);
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

        // CreateLayerHost's ordering constraint, from the far side: a host whose record never reached
        // ReconcilerContext.LayerHosts is one no destroy sweep iterates to.
        [Test]
        public void Given_APortalOnALayerNamingNoOffset_When_ItsHostIsAttempted_Then_NoHostPartsAreLeftBehind()
        {
            // Arrange — the throw is logged rather than raised out of V.Mount, so the mount completes and
            // the assertion below is reached.
            ExpectUnnamedLayerReport();

            // Act
            MountAndLayout(V.Div(children: new VNode[]
            {
                V.Portal((UILayer)99, children: new VNode[] { V.Div(name: "inside") }),
            }));

            // Assert
            Assert.That((NewDocs().Count, NewSettings().Count), Is.EqualTo((0, 0)));
        }

        // What a layer naming no offset reports: FiberLogger.LogException's tag line, then the throw.
        private static void ExpectUnnamedLayerReport()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[Portal\] An exception occurred"));
            LogAssert.Expect(LogType.Exception, new Regex("SwitchExpressionException"));
        }

        // Anywhere this reconciler renders: the main panel, or any host it created.
        private bool RenderedAnywhere(string elementName)
        {
            if (_host.Root.Q<VisualElement>(elementName) != null) return true;
            foreach (var doc in NewDocs())
            {
                if (doc.rootVisualElement != null && doc.rootVisualElement.Q<VisualElement>(elementName) != null)
                {
                    return true;
                }
            }
            return false;
        }

        [Test]
        public void Given_APortalOnANamedLayerQueuedBehindOneNamingNoOffset_When_Mounted_Then_ItStillReachesItsHost()
        {
            // Arrange
            ExpectUnnamedLayerReport();

            // Act
            MountAndLayout(V.Div(name: "queue-root", children: new VNode[]
            {
                V.Portal((UILayer)99, key: "unnamed", children: new VNode[] { V.Div(name: "unnamed-child") }),
                V.Portal(UILayer.Overlay, key: "named", children: new VNode[] { V.Div(name: "named-child") }),
            }));

            // Assert — the offending portal holds its slot and is the only one that loses its children.
            Assert.That((_host.Root.Q<VisualElement>("queue-root").childCount,
                    RenderedAnywhere("unnamed-child"), RenderedAnywhere("named-child")),
                Is.EqualTo((2, false, true)));
        }

        [Test]
        public void Given_AZLayerPlacementQueuedBehindAPortalNamingNoOffset_When_Mounted_Then_ItStillLands()
        {
            // Arrange — a z-* placement shares the deferred-mount queue with every Portal of the pass.
            ExpectUnnamedLayerReport();

            // Act
            MountAndLayout(V.Div(name: "queue-root", children: new VNode[]
            {
                V.Portal((UILayer)99, key: "unnamed", children: new VNode[] { V.Div(name: "unnamed-child") }),
                V.Div(name: "stacked", className: "absolute z-10"),
            }));

            // Assert — the count carries the offending portal's own slot, so a case arranged without it fails
            // here rather than only on the report expected above.
            Assert.That((_host.Root.Q<VisualElement>("queue-root").childCount,
                    RenderedAnywhere("stacked"), RenderedAnywhere("unnamed-child")),
                Is.EqualTo((3, true, false)));
        }

        private static StateUpdater<int> s_bumpUnnamed;

        [Component]
        private static VNode UnnamedLayerHost()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_bumpUnnamed = setTick;
            return V.Div(name: "unnamed-root", children: new VNode[]
            {
                V.Label(text: "tick=" + tick),
                tick == 0
                    ? null
                    : V.Portal((UILayer)99, key: "unnamed", children: new VNode[] { V.Div(name: "unnamed-child") }),
            });
        }

        [Test]
        public void Given_APortalOnALayerNamingNoOffset_When_ItsDeclarerRendersAgain_Then_NoSecondPlaceholderIsAppended()
        {
            // Arrange — the portal has to arrive on an update, so the pass whose drain fails is the
            // declaring fiber's own: that fiber's committed tree must carry the placeholder the failed
            // mount left behind, or every later render diffs it as absent and appends another one.
            MountAndLayout(V.Component(UnnamedLayerHost, key: "root"));
            ExpectUnnamedLayerReport();

            // Act
            s_bumpUnnamed.Invoke(v => v + 1);
            FlushAndLayout();
            s_bumpUnnamed.Invoke(v => v + 1);
            FlushAndLayout();

            // Assert — the label carries the re-render the count is read after.
            var root = _host.Root.Q<VisualElement>("unnamed-root");
            Assert.That((root.childCount, ((Label)root[0]).text, RenderedAnywhere("unnamed-child")),
                Is.EqualTo((2, "tick=2", false)));
        }

        private static int s_attachRuns;
        private static bool s_attachedIntoALayerContainer;

        // Stands in for a V.Custom&lt;T&gt; whose element registers a panel-attach callback: the insert a
        // z-* placement performs is what runs it.
        internal sealed class LayerAttachThrower : VisualElement
        {
            public LayerAttachThrower()
            {
                RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    s_attachRuns++;
                    s_attachedIntoALayerContainer =
                        parent != null && FiberZLayerCoordinator.IsLayerContainer(parent);
                    throw new System.InvalidOperationException("attach callback");
                });
            }
        }

        private static StateUpdater<bool> s_showStacked;

        [Component]
        private static VNode StackedArrivalHost()
        {
            var (show, setShow) = Hooks.UseState(false);
            s_showStacked = setShow;
            return V.Div(name: "queue-root", className: "relative", children: new VNode[]
            {
                show ? V.Custom<LayerAttachThrower>(className: "absolute z-10", name: "stacked") : null,
            });
        }

        // GREEN_ON_BASE(characterization): the boundary a z placement's application throw already reached,
        // which the containment beside it must leave alone; it is the commit before this one that reddens.
        [Test]
        public void Given_AnAttachCallbackThatThrows_When_ADeferredZPlacementLandsItsElement_Then_TheBoundaryAboveShowsItsFallback()
        {
            // Arrange — the element arrives on an update, so the pass is the declaring fiber's own and the
            // boundary above it is reachable. A first placement is queued unconditionally
            // (FiberZLayerCoordinator.EnqueueMount has no synchronous branch), so the only insert this
            // element ever sees is the drain's.
            s_attachRuns = 0;
            s_attachedIntoALayerContainer = false;
            MountAndLayout(V.ErrorBoundary(
                fallback: _ => V.Div(name: "fallback"),
                children: new VNode[] { V.Component(StackedArrivalHost, key: "host") }));

            // Act
            s_showStacked.Invoke(true);
            FlushAndLayout();

            // Assert — the container term is what fails when the arrangement stops being a layer placement,
            // so the fallback cannot stand in for an ordinary mount's own throw reaching the same boundary.
            Assert.That((s_attachRuns, s_attachedIntoALayerContainer,
                    _host.Root.Q<VisualElement>("fallback") != null),
                Is.EqualTo((1, true, true)));
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

        [Test]
        public void Given_APanelSizeMatchingTheDocumentDefault_When_Mounted_Then_FixedSizingStillApplies()
        {
            // Arrange — the size settings are driven after the attach with a mode round-trip, so the
            // fixed sizing derives even when the requested size equals the document's own default (a
            // plain value write would read as no-change and never re-derive).
            Vector2 documentDefault;
            var probeGo = new GameObject("ws-default-probe");
            try
            {
                documentDefault = probeGo.AddComponent<UIDocument>().worldSpaceSize;
            }
            finally
            {
                Object.DestroyImmediate(probeGo);
            }

            // Act
            MountAndLayout(V.Div(children: new VNode[]
            {
                V.WorldSpace(Vector3.zero, panelSize: documentDefault,
                    children: new VNode[] { V.Div(name: "ws1") }),
            }));

            // Assert — the mode landed on Fixed with the requested (default-equal) size.
            var docs = NewDocs();
            Assume.That(docs.Count, Is.EqualTo(1), "Precondition: the world-space host exists");
            Assert.That((docs[0].worldSpaceSizeMode, docs[0].worldSpaceSize),
                Is.EqualTo((UIDocument.WorldSpaceSizeMode.Fixed, documentDefault)));
        }

        #endregion

        #region Host resilience

        [Component]
        private static VNode SlidingWorldSpaceHost()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_bumpStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.WorldSpace(new Vector3(step, 0f, 0f), key: "ws",
                    children: new VNode[] { V.Div(name: "ws-live") }),
            });
        }

        private static StateUpdater<int> s_bumpStep;

        [Test]
        public void Given_AHostKilledExternally_When_TheWorldSpacePatches_Then_TheReconcileSurvives()
        {
            // Arrange — a scene unload can destroy the host GameObject while the owning fiber tree
            // survives; EVERY later patch must skip the dead record on the same warning path instead
            // of throwing or escalating to an error-level log.
            MountAndLayout(V.Component(SlidingWorldSpaceHost, key: "root"));
            var docs = NewDocs();
            Assume.That(docs.Count, Is.EqualTo(1), "Precondition: the world-space host exists");
            LogAssert.Expect(LogType.Warning, new Regex("died externally", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Warning, new Regex("died externally", RegexOptions.IgnoreCase));
            Object.DestroyImmediate(docs[0].gameObject);

            // Act & Assert — two consecutive patches both survive on the warning path.
            Assert.That(() =>
            {
                s_bumpStep.Invoke(s => s + 1);
                FlushAndLayout();
                s_bumpStep.Invoke(s => s + 1);
                FlushAndLayout();
            }, Throws.Nothing);
        }

        #endregion

        #region Error boundary interplay

        [Component]
        private static VNode ThrowingChild() =>
            throw new System.InvalidOperationException("boundary portal boom");

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryWithPortalFallback()
        {
            Hooks.UseFallback(_ => V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Topmost, children: new VNode[] { V.Div(name: "boundary-toast") }),
                V.Div(name: "fallback-body"),
            }));
            return V.Component(ThrowingChild, key: "child");
        }

        [Test]
        public void Given_AFallbackContainingALayerPortal_When_TheBoundaryCatches_Then_ThePortalMounts()
        {
            // Arrange — the abort that ends the failed pass must not also discard the deferred mount
            // the boundary's own fallback just enqueued: that enqueue belongs to a LIVE placeholder.
            // Act
            MountAndLayout(V.Component(BoundaryWithPortalFallback, key: "root"));

            // Assert — the fallback's toast reached the Topmost layer host.
            VisualElement toast = null;
            foreach (var doc in NewDocs())
            {
                toast ??= doc.rootVisualElement?.Q<VisualElement>("boundary-toast");
            }
            Assert.That(toast, Is.Not.Null);
        }

        [Component(IsErrorBoundary = true)]
        private static VNode PortalThenThrowerBoundary()
        {
            Hooks.UseFallback(_ => V.Div(name: "plain-fallback"));
            return V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[] { V.Div(name: "victim-content") }),
                V.Component(ThrowingChild, key: "child"),
            });
        }

        [Test]
        public void Given_AFailedSubtreeWithALayerPortal_When_TheBoundaryCatches_Then_ThatPortalNeverMounts()
        {
            // Arrange — the failed subtree enqueued a portal before its sibling threw; the boundary's
            // rollback detaches that placeholder, so the drain must skip the dead enqueue instead of
            // mounting content for a subtree that no longer exists.
            // Act
            MountAndLayout(V.Component(PortalThenThrowerBoundary, key: "root"));

            // Assert — no layer host carries the failed subtree's content.
            VisualElement victim = null;
            foreach (var doc in NewDocs())
            {
                victim ??= doc.rootVisualElement?.Q<VisualElement>("victim-content");
            }
            Assert.That(victim, Is.Null);
        }

        #endregion
    }
}
