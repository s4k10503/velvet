using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a commit attaches a host ref among the commit's layout effects: in the one post-order
    /// sequence React's layout phase walks, where a component's layout effects run after the refs of the elements
    /// it rendered and after the effects of the components below it, and an element's ref attaches after
    /// everything inside it.
    /// <list type="bullet">
    /// <item>A child's layout effect finds neither an ancestor element's ref nor a later sibling's set; it finds
    /// an earlier sibling's, and a parent's finds its child's.</item>
    /// <item>A mount and an update each run their refs and layout effects as one sequence, and so does a drain
    /// that commits several components at once.</item>
    /// <item>A layout cleanup runs after the ref it shares a commit with detaches and before the new one
    /// attaches.</item>
    /// <item>A Portal's elements and components take the position of the Portal in the tree, not the place the
    /// walk reaches them.</item>
    /// <item>A virtual list's rows commit one at a time, so a row's layout effect does not find the ref of a row
    /// committed after it.</item>
    /// <item>A write made from inside a ref setup or a ref cleanup leaves the refs it queues to the commit after
    /// the one running, and the cleanups it replaces run after that setup or cleanup returns.</item>
    /// <item>A commit that is not a parked pass's own leaves the setup that pass queued where it is, and so does
    /// the commit of a render the pass owes another component ahead of its own.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RefLayoutInterleaveTests
    {
        private VisualElement _root;

        private static readonly List<string> s_log = new();

        private static readonly Dictionary<string, StateUpdater<int>> s_setters = new();

        private static readonly Dictionary<string, Ref<VisualElement>> s_rowRefs = new();

        private static readonly Ref<VisualElement> s_watched = new();

        private static VisualElement s_portalTarget;

        private static VisualElement s_seenByReader;

        private static int s_readerRuns;

        private static VisualElement s_seenByParent;

        private static int s_parentRuns;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            s_log.Clear();
            s_setters.Clear();
            s_rowRefs.Clear();
            s_watched.Set(null);
            s_portalTarget = new VisualElement();
            s_seenByReader = null;
            s_readerRuns = 0;
            s_seenByParent = null;
            s_parentRuns = 0;
            AsyncActionsInFlightTestAccess.ResetForTest();
            s_add = null;
            s_start = default;
            s_setTick = default;
            s_siblingFiber = null;
            s_lastWatchedTick = 0;
        }

        [TearDown]
        public void TearDown() => AsyncActionsInFlightTestAccess.ResetForTest();

        private static Func<VisualElement, Action> Attach(string name, int tick = 0)
            => _ =>
            {
                s_log.Add("ref:" + name + (tick > 0 ? ":" + tick : string.Empty));
                return null;
            };

        // Reads the ref under test the moment its layout effect runs on mount.
        [Component]
        private static VNode Reader()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_readerRuns++;
                s_seenByReader = s_watched.Current;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(name: "reader", text: "reader");
        }

        [Component]
        private static VNode AncestorHost()
            => V.Div(name: "ancestor", refCallback: s_watched.SetElement, children: new VNode[]
            {
                V.Component(Reader, key: "reader"),
            });

        [Component]
        private static VNode LaterSiblingHost()
            => V.Div(children: new VNode[]
            {
                V.Component(Reader, key: "reader"),
                V.Div(name: "later", refCallback: s_watched.SetElement),
            });

        [Component]
        private static VNode EarlierSiblingHost()
            => V.Div(children: new VNode[]
            {
                V.Div(name: "earlier", refCallback: s_watched.SetElement),
                V.Component(Reader, key: "reader"),
            });

        [Component]
        private static VNode ParentOfBox()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_parentRuns++;
                s_seenByParent = s_watched.Current;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div(children: new VNode[] { V.Component(BoxChild, key: "box") });
        }

        [Component]
        private static VNode BoxChild()
            => V.Div(name: "box", refCallback: s_watched.SetElement);

        [Test]
        public void Given_AChildWhoseAncestorElementCarriesARef_When_TheChildsLayoutEffectRunsOnMount_Then_TheAncestorsRefIsNotYetSet()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(AncestorHost, key: "host"));

            // Assert — the run count and the ref after the mount are read beside the reading, because a layout
            // effect that never ran and a ref that never attached both leave null in it.
            Assert.That((s_readerRuns, s_seenByReader == null, s_watched.Current != null),
                Is.EqualTo((1, true, true)));
        }

        [Test]
        public void Given_AChildWithALaterSiblingCarryingARef_When_TheChildsLayoutEffectRunsOnMount_Then_TheSiblingsRefIsNotYetSet()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(LaterSiblingHost, key: "host"));

            // Assert
            Assert.That((s_readerRuns, s_seenByReader == null, s_watched.Current != null),
                Is.EqualTo((1, true, true)));
        }

        // GREEN_ON_BASE(characterization): the base finds an earlier sibling's ref set in a layout effect.
        // It attaches every ref of a pass ahead of every layout effect.
        [Test]
        public void Given_AChildWithAnEarlierSiblingCarryingARef_When_TheChildsLayoutEffectRunsOnMount_Then_TheSiblingsRefIsSet()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(EarlierSiblingHost, key: "host"));

            // Assert
            Assert.That((s_readerRuns, ReferenceEquals(s_seenByReader, _root.Q("earlier"))),
                Is.EqualTo((1, true)));
        }

        // GREEN_ON_BASE(characterization): the base finds a child's ref set in its parent's layout effect.
        // It attaches every ref of a pass ahead of every layout effect.
        [Test]
        public void Given_AParentWhoseChildRendersARefdElement_When_TheParentsLayoutEffectRunsOnMount_Then_TheChildsRefIsSet()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(ParentOfBox, key: "host"));

            // Assert
            Assert.That((s_parentRuns, ReferenceEquals(s_seenByParent, _root.Q("box"))),
                Is.EqualTo((1, true)));
        }

        [Component]
        private static VNode OrderParent()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("effect:parent");
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div(name: "outer", refCallback: Attach("outer"), children: new VNode[]
            {
                V.Component(OrderChild, "first", key: "first"),
                V.Div(name: "middle", refCallback: Attach("middle")),
                V.Component(OrderChild, "second", key: "second"),
            });
        }

        [Component]
        private static VNode OrderChild(string name)
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("effect:" + name);
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div(name: name, refCallback: Attach(name));
        }

        [Test]
        public void Given_ComponentsAndElementsCarryingRefsAndLayoutEffects_When_Mounted_Then_TheyRunInOnePostOrderSequence()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(OrderParent, key: "parent"));

            // Assert
            Assert.That(string.Join(",", s_log),
                Is.EqualTo("ref:first,effect:first,ref:middle,ref:second,effect:second,ref:outer,effect:parent"));
        }

        // Every ref callback is new at each render and every effect's dependency changes, so an update queues
        // the setups again and runs the effects again.
        [Component]
        private static VNode TickParent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setters["tick"] = setTick;
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("effect:parent:" + tick);
                return (Action)null;
            }, new object[] { tick });
            return V.Div(name: "outer", refCallback: Attach("outer", tick + 1), children: new VNode[]
            {
                V.Component(TickChild, tick, key: "child"),
                V.Div(name: "late", refCallback: Attach("late", tick + 1)),
            });
        }

        [Component]
        private static VNode TickChild(int tick)
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("effect:child:" + tick);
                return (Action)null;
            }, new object[] { tick });
            return V.Div(name: "child", refCallback: Attach("child", tick + 1));
        }

        [Test]
        public void Given_TheSameTreeReRendered_When_EveryRefAndLayoutEffectChanges_Then_TheyRunInOnePostOrderSequence()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TickParent, key: "parent"));
            s_log.Clear();
            s_setters["tick"].Invoke(1);

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(",", s_log),
                Is.EqualTo("ref:child:2,effect:child:1,ref:late:2,ref:outer:2,effect:parent:1"));
        }

        [Component]
        private static VNode CleanupHost()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setters["tick"] = setTick;
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("effect:" + tick);
                return () => s_log.Add("cleanup:" + tick);
            }, new object[] { tick });
            return V.Div(name: "box", refCallback: _ =>
            {
                s_log.Add("attach:" + tick);
                return () => s_log.Add("detach:" + tick);
            });
        }

        [Test]
        public void Given_AComponentWhoseRefAndLayoutEffectBothChange_When_ItReRenders_Then_TheLayoutCleanupRunsBetweenTheRefsDetachAndAttach()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CleanupHost, key: "host"));
            s_log.Clear();
            s_setters["tick"].Invoke(1);

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("detach:0,cleanup:0,attach:1,effect:1"));
        }

        [Component]
        private static VNode DrainSibling(string name)
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setters[name] = setTick;
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("effect:" + name);
                return (Action)null;
            }, new object[] { tick });
            return V.Div(name: name, refCallback: Attach(name, tick));
        }

        [Component]
        private static VNode DrainHost()
            => V.Div(children: new VNode[]
            {
                V.Component(DrainSibling, "a", key: "a"),
                V.Component(DrainSibling, "b", key: "b"),
            });

        [Test]
        public void Given_TwoSiblingsDirtiedInOneDrain_When_Drained_Then_TheirRefsAndLayoutEffectsRunInOneSequence()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DrainHost, key: "host"));
            s_log.Clear();
            s_setters["b"].Invoke(1);
            s_setters["a"].Invoke(1);

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("ref:a:1,effect:a,ref:b:1,effect:b"));
        }

        [Component]
        private static VNode PortalOrderHost()
            => V.Div(children: new VNode[]
            {
                V.Div(name: "before", refCallback: Attach("before")),
                V.Portal(s_portalTarget, children: new VNode[]
                {
                    V.Div(name: "portaled", refCallback: Attach("portaled")),
                }),
                V.Div(name: "after", refCallback: Attach("after")),
            });

        [Test]
        public void Given_APortalBetweenTwoRefdElements_When_Mounted_Then_TheirRefsAttachInTreeOrder()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(PortalOrderHost, key: "host"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("ref:before,ref:portaled,ref:after"));
        }

        [Component]
        private static VNode PortalReaderHost()
            => V.Div(name: "host-box", refCallback: s_watched.SetElement, children: new VNode[]
            {
                V.Portal(s_portalTarget, children: new VNode[] { V.Component(Reader, key: "reader") }),
            });

        [Test]
        public void Given_APortalChildInsideAnElementCarryingARef_When_TheChildsLayoutEffectRunsOnMount_Then_TheElementsRefIsNotYetSet()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(PortalReaderHost, key: "host"));

            // Assert — the run count and the ref after the mount are read beside the reading, because a layout
            // effect that never ran and a ref that never attached both leave null in it.
            Assert.That((s_readerRuns, s_seenByReader == null, s_watched.Current != null),
                Is.EqualTo((1, true, true)));
        }

        [Component]
        private static VNode WrappedReaderBeforeRefHost()
            => V.Div(children: new VNode[]
            {
                V.Div(name: "wrap", children: new VNode[] { V.Component(Reader, key: "reader") }),
                V.Div(name: "later", refCallback: s_watched.SetElement),
            });

        [Component]
        private static VNode RefBeforeWrappedReaderHost()
            => V.Div(children: new VNode[]
            {
                V.Div(name: "earlier", refCallback: s_watched.SetElement),
                V.Div(name: "wrap", children: new VNode[] { V.Component(Reader, key: "reader") }),
            });

        [Test]
        public void Given_AChildInsideAnElementBeforeASiblingCarryingARef_When_TheChildsLayoutEffectRunsOnMount_Then_TheSiblingsRefIsNotYetSet()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(WrappedReaderBeforeRefHost, key: "host"));

            // Assert
            Assert.That((s_readerRuns, s_seenByReader == null, s_watched.Current != null),
                Is.EqualTo((1, true, true)));
        }

        // GREEN_ON_BASE(characterization): the base finds the ref of an element before the child's wrapper set.
        // It attaches every ref of a pass ahead of every layout effect.
        [Test]
        public void Given_AChildInsideAnElementAfterASiblingCarryingARef_When_TheChildsLayoutEffectRunsOnMount_Then_TheSiblingsRefIsSet()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(RefBeforeWrappedReaderHost, key: "host"));

            // Assert
            Assert.That((s_readerRuns, ReferenceEquals(s_seenByReader, _root.Q("earlier"))),
                Is.EqualTo((1, true)));
        }

        [Component]
        private static VNode GrandchildAfterRefHost()
            => V.Div(children: new VNode[]
            {
                V.Div(name: "earlier", refCallback: s_watched.SetElement),
                V.Component(Passthrough, key: "passthrough"),
            });

        // Renders only a component, so the one below it is a grandchild of the component that rendered the ref.
        [Component]
        private static VNode Passthrough()
            => V.Component(Reader, key: "reader");

        // GREEN_ON_BASE(characterization): the base finds an earlier sibling's ref set in the layout effect of a
        // component two levels below. It attaches every ref of a pass ahead of every layout effect.
        [Test]
        public void Given_AGrandchildAfterAnElementCarryingARef_When_TheGrandchildsLayoutEffectRunsOnMount_Then_TheElementsRefIsSet()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(GrandchildAfterRefHost, key: "host"));

            // Assert
            Assert.That((s_readerRuns, ReferenceEquals(s_seenByReader, _root.Q("earlier"))),
                Is.EqualTo((1, true)));
        }

        [Component]
        private static VNode PortalFirstHost()
            => V.Div(children: new VNode[]
            {
                V.Portal(s_portalTarget, children: new VNode[]
                {
                    V.Div(name: "portaled", refCallback: Attach("portaled")),
                }),
                V.Div(name: "after", refCallback: Attach("after")),
            });

        [Test]
        public void Given_APortalBeforeARefdElement_When_Mounted_Then_TheirRefsAttachInTreeOrder()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(PortalFirstHost, key: "host"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("ref:portaled,ref:after"));
        }

        [Component]
        private static VNode TwoPortalsHost()
            => V.Div(children: new VNode[]
            {
                V.Portal(s_portalTarget, children: new VNode[] { V.Div(name: "a", refCallback: Attach("a")) }),
                V.Div(name: "x", refCallback: Attach("x")),
                V.Portal(s_portalTarget, children: new VNode[] { V.Div(name: "b", refCallback: Attach("b")) }),
            });

        [Test]
        public void Given_TwoPortalsIntoOneTargetAroundARefdElement_When_Mounted_Then_TheirRefsAttachInTreeOrder()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(TwoPortalsHost, key: "host"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("ref:a,ref:x,ref:b"));
        }

        [Component]
        private static VNode ZIndexedBeforeReaderHost()
            => V.Div(children: new VNode[]
            {
                V.Div(name: "z", className: "absolute z-10", refCallback: s_watched.SetElement),
                V.Component(Reader, key: "reader"),
            });

        // GREEN_ON_BASE(characterization): the base finds the ref of a z-indexed element set in the layout effect
        // of a component after it. It attaches every ref of a pass ahead of every layout effect.
        [Test]
        public void Given_AZIndexedElementCarryingARefBeforeAChild_When_TheChildsLayoutEffectRunsOnMount_Then_TheElementsRefIsSet()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(ZIndexedBeforeReaderHost, key: "host"));

            // Assert
            Assert.That((s_readerRuns, s_seenByReader != null), Is.EqualTo((1, true)));
        }

        [Component]
        private static VNode EmptyReader()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_readerRuns++;
                s_seenByReader = s_watched.Current;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Fragment(children: Array.Empty<VNode>());
        }

        [Component]
        private static VNode EmptyBeforeRefHost()
            => V.Div(children: new VNode[]
            {
                V.Component(EmptyReader, key: "empty"),
                V.Div(name: "later", refCallback: s_watched.SetElement),
            });

        [Test]
        public void Given_AChildRenderingNothingBeforeAnElementCarryingARef_When_TheChildsLayoutEffectRunsOnMount_Then_TheElementsRefIsNotYetSet()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(EmptyBeforeRefHost, key: "host"));

            // Assert
            Assert.That((s_readerRuns, s_seenByReader == null, s_watched.Current != null),
                Is.EqualTo((1, true, true)));
        }

        [Component]
        private static VNode EffectRow()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("effect:row");
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(text: "row");
        }

        // The element after the list gets a new callback at every render, so the render that adds the row queues it.
        [Component]
        private static VNode ListThenRefHost()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setters["host"] = setTick;
            return V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: tick == 0 ? Array.Empty<string>() : new[] { "row" },
                    keySelector: item => item,
                    itemHeight: 50f,
                    renderer: item => V.Component(EffectRow, key: item),
                    overscan: 0,
                    key: "list"),
                V.Div(name: "after", refCallback: Attach("after", tick)),
            });
        }

        [Test]
        public void Given_AVirtualListRowMountedInADrainBeforeARefdElement_When_Drained_Then_TheRowsLayoutEffectRunsAheadOfTheElementsRef()
        {
            // Arrange — the viewport height a geometry pass would have left, so the host's re-render renders a range.
            using var mounted = V.Mount(_root, V.Component(ListThenRefHost, key: "host"));
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()], 50f);
            s_log.Clear();
            s_setters["host"].Invoke(1);

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("effect:row,ref:after:1"));
        }

        private static int QueuedRefSetupCount(ReconcilerContext context)
        {
            var field = typeof(ReconcilerContext)
                .GetField("_pendingRefAttaches", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return -1;
            return ((ICollection)field.GetValue(context)).Count;
        }

        // GREEN_ON_BASE(characterization): the base keeps no slot in the ref queue for a setup it has run.
        // It compacts the queue where the drain at the end of the pass ends.
        [Test]
        public void Given_AMountThatAttachedARef_When_TheMountReturns_Then_TheQueueKeepsNoSlotForIt()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(BoxChild, key: "box"));

            // Assert — the attach is read beside the count, because a mount that queued nothing reads the same
            // empty queue with nothing about the compaction measured.
            Assert.That((s_watched.Current != null, QueuedRefSetupCount(mounted.Root.Reconciler.Context)),
                Is.EqualTo((true, 0)));
        }

        // Dispatches the click whose write the nested commit below it answers for: it runs inside a ref setup, so the
        // commit the write starts is one the enclosing commit has on its stack.
        private static readonly Func<VisualElement, Action> s_clickAndLog = element =>
        {
            s_log.Add("clicker:click");
            element.parent.Q<UnityEngine.UIElements.Button>("go").SimulateClick();
            s_log.Add("clicker:after");
            return null;
        };

        private static Func<VisualElement, Action> Tracking(int phase)
            => _ =>
            {
                s_log.Add("attach:" + phase);
                return () => s_log.Add("detach:" + phase);
            };

        // The tracker's ref changes with the phase, so each phase queues a setup and a cleanup for it; the clicker
        // runs in phase one only, and its write moves the tree to phase two from inside the commit.
        [Component]
        private static VNode NestedWriteHost()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setters["phase"] = setPhase;
            return V.Div(children: new VNode[]
            {
                V.Button(name: "go", onClick: () => setPhase.Invoke(2)),
                V.Div(name: "tracker", refCallback: Tracking(phase)),
                V.Div(name: "clicker", refCallback: phase == 1 ? s_clickAndLog : null),
            });
        }

        // GREEN_ON_BASE(characterization): the base runs the cleanup of a ref a setup's write replaces after the setup
        // has returned. It re-reads its queue after each setup and runs the cleanups of what was queued meanwhile
        // ahead of their setups.
        [Test]
        public void Given_ASetupWhoseWriteReplacesTheAttachedRefOfAnotherElement_When_TheSetupReturns_Then_ThatRefsCleanupRunsAfterItAndAheadOfTheNewSetup()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NestedWriteHost, key: "host"));
            s_log.Clear();
            s_setters["phase"].Invoke(1);

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(",", s_log),
                Is.EqualTo("detach:0,attach:1,clicker:click,clicker:after,detach:1,attach:2"));
        }

        private static VisualElement s_clickRoot;

        private static void ClickInCleanup()
        {
            s_log.Add("detach:A0");
            s_clickRoot.Q<UnityEngine.UIElements.Button>("go").SimulateClick();
            s_log.Add("detach:A0:done");
        }

        private static readonly Func<VisualElement, Action> s_attachA0 = _ =>
        {
            s_log.Add("attach:A0");
            return ClickInCleanup;
        };

        private static readonly Func<VisualElement, Action> s_attachA1 = _ =>
        {
            s_log.Add("attach:A1");
            return null;
        };

        // A's ref moves from its first callback to its second in phase one, and the first callback's cleanup writes the
        // state that adds C in phase two, from inside the cleanup.
        [Component]
        private static VNode CleanupWriteHost()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setters["phase"] = setPhase;
            return V.Div(children: new VNode[]
            {
                V.Button(name: "go", onClick: () => setPhase.Invoke(2)),
                V.Div(name: "a", refCallback: phase == 0 ? s_attachA0 : s_attachA1),
                phase == 2 ? V.Div(name: "c", refCallback: Attach("C")) : null,
            });
        }

        // GREEN_ON_BASE(characterization): the base finishes the cleanup a write runs inside before any setup runs.
        // Its drain holds off the passes that write starts until the cleanup has returned, and takes what they
        // queued after the setups already queued.
        [Test]
        public void Given_ACleanupWhoseWriteAddsAnElementCarryingARef_When_TheCommitDetachesTheReplacedRef_Then_NoSetupRunsInsideTheCleanup()
        {
            // Arrange
            s_clickRoot = _root;
            using var mounted = V.Mount(_root, V.Component(CleanupWriteHost, key: "host"));
            s_log.Clear();
            s_setters["phase"].Invoke(1);

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("detach:A0,detach:A0:done,attach:A1,ref:C"));
        }

        private static VNode[] TrackedList(string which)
        {
            var nodes = new VNode[6];
            nodes[0] = V.Div(key: which, name: which, refCallback: Attach(which));
            for (var i = 1; i < nodes.Length; i++)
            {
                nodes[i] = V.Label(key: "filler" + i, text: "f" + i);
            }
            return nodes;
        }

        // Parks a pass on a Reconciler of the caller's choosing, with the arrival's ref setup queued and the departure
        // still in the tree, and reports whether it parked there.
        private static bool ParkAPassWithASetupQueued(Velvet.Reconciler reconciler, ReconcilerContext context)
        {
            var parkedRoot = new VisualElement();
            var departing = TrackedList("departing");
            reconciler.Reconcile(parkedRoot, Array.Empty<VNode>(), departing);
            reconciler.Reconcile(parkedRoot, departing, TrackedList("arriving"), frameBudgetMs: 0.001);
            var slices = 0;
            while (reconciler.HasPendingWork && QueuedRefSetupCount(context) == 0)
            {
                if (slices++ >= 500) Assert.Fail("no slice boundary left the arrival's setup queued");
                reconciler.ContinueReconcile(frameBudgetMs: 0.001);
            }
            return reconciler.HasPendingWork && QueuedRefSetupCount(context) == 1;
        }

        // GREEN_ON_BASE(characterization): the base attaches the ref of a drained component and leaves the setup a
        // parked pass queued where it is. Each pass that ends attaches the setups of the passes finished by then, and
        // none of the parked one's.
        [Test]
        public void Given_APassParkedOnTheSameContext_When_ADrainCommitsAnUnrelatedComponent_Then_TheParkedPassesSetupStaysQueued()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DrainHost, key: "host"));
            using var parkedPass = new Velvet.Reconciler(mounted.Root.Reconciler.Context);
            var parked = ParkAPassWithASetupQueued(parkedPass, mounted.Root.Reconciler.Context);
            s_log.Clear();
            s_setters["a"].Invoke(1);

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the park is read beside the log, because a context holding no parked pass attaches the same
            // ref with nothing about the held setup measured.
            Assert.That((parked, string.Join(",", s_log)), Is.EqualTo((true, "ref:a:1,effect:a")));
        }

        private static Action<string> s_add;

        private static TransitionStarter s_start;

        private static ComponentFiber s_siblingFiber;

        private static int s_lastWatchedTick;

        // Unwoven, so a write to a component renders it rather than reusing its last output.
        [Component(Compiler = false)]
        private static VNode OwedHolder()
        {
            var (state, addOptimistic) = Hooks.UseOptimistic<string, string>(
                "base",
                (current, action) => current + "+" + action);
            s_add = addOptimistic;
            return V.Label(text: state);
        }

        [Component(Compiler = false)]
        private static VNode OwedRoot()
        {
            var (_, start) = Hooks.UseTransition();
            s_start = start;
            return V.Div(children: new VNode[]
            {
                V.Component(OwedHolder, key: "holder"),
                V.Component(OwedSibling, key: "sibling"),
            });
        }

        // The element's ref is a new callback at every render, and its cleanup clears the ref, so the layout effect
        // of the child below reads whether this render's setup has run.
        [Component(Compiler = false)]
        private static VNode OwedSibling()
        {
            s_siblingFiber = FiberAmbientStack.Current;
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(
                refCallback: element =>
                {
                    s_lastWatchedTick = tick;
                    s_watched.Set(element);
                    return () => s_watched.Set(null);
                },
                children: new VNode[] { V.Component(OwedReader, key: "reader") });
        }

        private static StateUpdater<int> s_setTick;

        // Every render, since no dependencies are given.
        [Component(Compiler = false)]
        private static VNode OwedReader()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_log.Add("reader:" + (s_watched.Current == null ? "null" : "set"));
                return null;
            }));
            return V.Label(text: "reader");
        }

        [Test]
        public void Given_AFlushWhoseSettledTransitionOwesAnotherComponentARender_When_ThatRenderCommitsAheadOfIt_Then_TheFlushsRefsStillAttachAfterTheLayoutEffectsOfTheComponentsBelowThem()
        {
            // Arrange — the transition's write is on the sibling, so the flush that lands it settles the transition
            // and renders the holder, outside the sibling's own commit
            using var mounted = V.Mount(_root, V.Component(OwedRoot, key: "root"));
            var sibling = s_siblingFiber;
            s_start.Invoke(() =>
            {
                s_add.Invoke("sent");
                s_setTick.Invoke(1);
            });
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_log.Clear();

            // Act
            FiberWorkLoop.FlushState(sibling);

            // Assert — the tick the ref attached with is read beside the layout effect, because a flush that never
            // rendered the sibling leaves the earlier ref in place and reads the same
            Assert.That((string.Join(",", s_log), s_lastWatchedTick), Is.EqualTo(("reader:null", 1)));
        }

        // Owns the same write as OwedSibling and logs the order of a re-rendered component's own commit: the
        // ref callback is a new delegate at every render, and the component's insertion and layout effects have no
        // dependencies, so each runs at every render.
        [Component(Compiler = false)]
        private static VNode OwedLoggedSibling()
        {
            s_siblingFiber = FiberAmbientStack.Current;
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseInsertionEffect((Func<Action>)(() =>
            {
                s_log.Add("insertion");
                return null;
            }));
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_log.Add("layout");
                return () => s_log.Add("layout-cleanup");
            }));
            return V.Div(refCallback: element =>
            {
                s_log.Add("attach");
                return null;
            });
        }

        [Component(Compiler = false)]
        private static VNode OwedLoggedRoot()
        {
            var (_, start) = Hooks.UseTransition();
            s_start = start;
            return V.Div(children: new VNode[]
            {
                V.Component(OwedHolder, key: "holder"),
                V.Component(OwedLoggedSibling, key: "sibling"),
            });
        }

        private List<string> FlushTheOwedSiblingAndReadTheLog()
        {
            using var mounted = V.Mount(_root, V.Component(OwedLoggedRoot, key: "root"));
            var sibling = s_siblingFiber;
            s_start.Invoke(() =>
            {
                s_add.Invoke("sent");
                s_setTick.Invoke(1);
            });
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_log.Clear();
            FiberWorkLoop.FlushState(sibling);
            return new List<string>(s_log);
        }

        // GREEN_ON_BASE(characterization): the base runs a component's insertion effects before it attaches the ref of an
        // element the component rendered.
        [Test]
        public void Given_AFlushWhoseSettledTransitionOwesAnotherComponentARender_When_TheFlushedComponentHasAnInsertionEffectAndARef_Then_TheInsertionEffectRunsBeforeTheRefAttaches()
        {
            // Act
            var log = FlushTheOwedSiblingAndReadTheLog();

            // Assert
            Assert.That(log.FindAll(entry => entry == "insertion" || entry == "attach"),
                Is.EqualTo(new[] { "insertion", "attach" }));
        }

        [Test]
        public void Given_AFlushWhoseSettledTransitionOwesAnotherComponentARender_When_TheFlushedComponentsRefCallbackChanges_Then_ItsLayoutCleanupRunsBeforeTheNewRefAttaches()
        {
            // Act
            var log = FlushTheOwedSiblingAndReadTheLog();

            // Assert
            Assert.That(log.FindAll(entry => entry == "layout-cleanup" || entry == "attach"),
                Is.EqualTo(new[] { "layout-cleanup", "attach" }));
        }

        // The presence mounts when the state is written, so its first render suppresses the enter of the Motion it
        // holds.
        [Component(Compiler = false)]
        private static VNode NoOpEnterHost()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                tick == 0
                    ? null
                    : V.AnimatePresence(initial: false, children: new VNode[]
                    {
                        V.Motion(key: "m", transition: StyleTransition.Fade,
                            onEnterComplete: () => s_log.Add("enter-complete"),
                            refCallback: element =>
                            {
                                s_log.Add("attach");
                                return null;
                            }),
                    }),
            });
        }

        // GREEN_ON_BASE(characterization): the base attaches the ref of a Motion whose enter plays nothing before the
        // enter completes. It attaches every ref of a pass at the pass's end, ahead of the enter completions.
        [Test]
        public void Given_AMotionWhoseEnterPlaysNothing_When_ADrainMountsIt_Then_ItsRefAttachesBeforeItsEnterCompletes()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NoOpEnterHost, key: "host"));
            s_setTick.Invoke(1);

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("attach,enter-complete"));
        }

        [Component]
        private static VNode RefRow()
            => V.Label(name: "row-a", text: "a", refCallback: s_rowRefs["a"].SetElement);

        // The list renders no row until the host's state is written, and the effect has no dependencies, so it runs
        // in the commit of that write.
        [Component(Compiler = false)]
        private static VNode ListHostReadingARowRef()
        {
            s_siblingFiber = FiberAmbientStack.Current;
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_log.Add("host:" + (s_rowRefs["a"].Current != null ? "set" : "null"));
                return null;
            }));
            return V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: tick == 0 ? Array.Empty<string>() : new[] { "a" },
                    keySelector: item => item,
                    itemHeight: 50f,
                    renderer: item => V.Component(RefRow, key: item),
                    overscan: 0,
                    key: "list"),
            });
        }

        // GREEN_ON_BASE(characterization): the base finds the ref of a row the flush rendered set in the host's layout
        // effect. It attaches every ref of a pass ahead of every layout effect.
        [Test]
        public void Given_AHostWhoseFlushRendersAVirtualListRow_When_TheHostsLayoutEffectRuns_Then_TheRowsRefIsAlreadySet()
        {
            // Arrange — the viewport height a geometry pass would have left, so the host's render renders a range.
            s_rowRefs["a"] = new Ref<VisualElement>();
            using var mounted = V.Mount(_root, V.Component(ListHostReadingARowRef, key: "host"));
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()], 50f);
            var host = s_siblingFiber;
            s_setTick.Invoke(1);
            s_log.Clear();

            // Act
            FiberWorkLoop.FlushState(host);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("host:set"));
        }

        [Component]
        private static VNode RowProbe(string id)
        {
            Hooks.UseLayoutEffect(() =>
            {
                var other = s_rowRefs[id == "a" ? "b" : "a"];
                s_log.Add(id + ":" + (other.Current != null ? "set" : "null"));
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(name: "row-" + id, text: id, refCallback: s_rowRefs[id].SetElement);
        }

        [Test]
        public void Given_TwoVirtualListRowsWhoseEffectsReadEachOthersRef_When_ARangeIsRenderedFromAScroll_Then_TheEarlierRowSeesNoRefOfTheLater()
        {
            // Arrange — a headless mount measures no viewport, so the list renders no row until the range update.
            s_rowRefs["a"] = new Ref<VisualElement>();
            s_rowRefs["b"] = new Ref<VisualElement>();
            using var mounted = V.Mount(_root, V.VirtualList(
                items: new[] { "a", "b" },
                keySelector: item => item,
                itemHeight: 50f,
                renderer: item => V.Component(RowProbe, item, key: item),
                overscan: 0));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 100f);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("a:null,b:set"));
        }
    }
}
