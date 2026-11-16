using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a root asked for while another root renders, commits or runs its passive effects renders: once
    /// that work has ended and before control returns to what started it, after the passive effects of the root that
    /// ran last, as React's root.render() schedules there and its sync work starts by flushing pending passive effects.
    /// A discrete handler fired outside every render is not inside one, a root's own handler fired in its drain is left
    /// to that drain, and a pass that throws still makes what it held.
    /// </summary>
    [TestFixture]
    internal sealed class ExecutionContextEntryTests
    {
        private static readonly List<MountedTree> s_mounts = new();
        private static int s_popupRenders;
        private static string s_shared;
        private static string s_seen;

        [SetUp]
        public void SetUp()
        {
            FiberStrictMode.Enabled = false;
            (s_popupRenders, s_shared, s_seen) = (0, "before", null);
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = s_mounts.Count - 1; i >= 0; i--) s_mounts[i].Dispose();
            s_mounts.Clear();
        }

        private static MountedTree Mount(VNode tree)
        {
            var mounted = V.Mount(new VisualElement(), tree);
            s_mounts.Add(mounted);
            return mounted;
        }

        [Component]
        private static VNode Popup()
        {
            s_popupRenders++;
            return V.Div(name: "popup");
        }

        private static void MountAPopup(VisualElement host) => s_mounts.Add(V.Mount(host, V.Component(Popup)));

        // --- Roots held behind a commit, and the passive effects that run ahead of them ---

        private static Button s_bButton;
        private static Button s_cButton;
        private static int s_bRenders;
        private static StateUpdater<bool> s_showClicker;
        private static ComponentFiber s_ownerFiber;

        // On a click asks for its own render. c records what was written before it rendered.
        [Component]
        private static VNode RendersOnAClick(string name)
        {
            var (count, setCount) = Hooks.UseState(0);
            if (name == "b") s_bRenders++;
            if (name == "c") s_seen = s_shared;
            return V.Button(text: name, onClick: () => setCount.Invoke(count + 1), refCallback: element =>
            {
                if (name == "b") s_bButton = (Button)element;
                else s_cButton = (Button)element;
                return () => { };
            });
        }

        // Writes from the effect its mount commits.
        [Component]
        private static VNode WritesFromItsMountEffect()
        {
            Hooks.UseEffect(() =>
            {
                s_shared = "a mounted root's effect";
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div();
        }

        // Where its layout cleanup runs, mounts a root that writes if mountsAWriter, then clicks c's button.
        [Component]
        private static VNode ClicksInItsCleanup(bool mountsAWriter)
        {
            Hooks.UseLayoutEffect(() => () =>
            {
                if (mountsAWriter) Mount(V.Component(WritesFromItsMountEffect));
                s_cButton.SimulateClick();
            }, Array.Empty<object>());
            return V.Div();
        }

        // Writes from the effect of the render that drops the clicker.
        [Component]
        private static VNode OwnerOfTheClicker(bool mountsAWriter)
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_showClicker = setShown;
            s_ownerFiber = FiberAmbientStack.Current;
            Hooks.UseEffect(() =>
            {
                if (!shown) s_shared = "the owner's effect";
                return (Action)null;
            }, new object[] { shown });
            return V.Fragment(shown ? V.Component(ClicksInItsCleanup, mountsAWriter, key: "c") : null);
        }

        private static void MountTheOwnerAnd(bool mountsAWriter)
        {
            Mount(V.Component(RendersOnAClick, "c")).FlushEffectsForTest();
            Mount(V.Component(OwnerOfTheClicker, mountsAWriter)).FlushEffectsForTest();
            s_shared = "before";
            s_seen = null;
        }

        // React's sync work on a root starts by flushing the passive effects pending, whatever lane their commit took.
        [Test]
        public void Given_ANormalLaneCommitWithAPendingEffect_When_ARootIsHeldBehindIt_Then_TheEffectRanBeforeTheHeldRootRendered()
        {
            // Arrange
            MountTheOwnerAnd(mountsAWriter: false);
            s_showClicker.Invoke(false);

            // Act
            FiberWorkLoop.FlushState(s_ownerFiber);

            // Assert — null where c never rendered
            Assert.That(s_seen, Is.EqualTo("the owner's effect"));
        }

        // The first is a mount rather than a click, whose sync-lane drain would run its effects itself.
        [Test]
        public void Given_TwoHeldRoots_When_TheFirstLeavesAPendingEffect_Then_ItRanBeforeTheSecondRendered()
        {
            // Arrange
            MountTheOwnerAnd(mountsAWriter: true);
            s_showClicker.Invoke(false);

            // Act
            FiberWorkLoop.FlushState(s_ownerFiber);

            // Assert — "the owner's effect" where the release ran only the owner's, "before" where it ran neither
            Assert.That(s_seen, Is.EqualTo("a mounted root's effect"));
        }

        // --- Where a root asked for in a render, a passive flush or a handler renders ---

        private static int s_rendersAtTheOwnersLayout;

        [Component]
        private static VNode PortalsARootMount(VisualElement target)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_rendersAtTheOwnersLayout = s_popupRenders;
                return null;
            }), Array.Empty<object>());
            return V.Portal(target, new VNode[] { V.ScrollView(onCreated: MountAPopup) });
        }

        // The Portal's children render in the owner's render, which a root mounted there waits for.
        [Test]
        public void Given_APortalChildsOnCreatedThatMountsAnotherRoot_When_ThePortalMounts_Then_TheRootHasNotRenderedBeforeTheOwnersLayoutEffect()
        {
            // Arrange
            var target = new VisualElement();
            s_rendersAtTheOwnersLayout = -1;

            // Act
            Mount(V.Component(PortalsARootMount, target));

            // Assert
            Assert.That(s_rendersAtTheOwnersLayout, Is.Zero);
        }

        private static int s_rendersWhenTheEffectReturned;

        [Component]
        private static VNode MountsARootFromAnEffect(VisualElement target)
        {
            Hooks.UseEffect(() =>
            {
                MountAPopup(target);
                s_rendersWhenTheEffectReturned = s_popupRenders;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div();
        }

        // A passive flush is a commit of its own, as React's flushPassiveEffects is.
        [Test]
        public void Given_AUseEffectThatMountsARoot_When_TheEffectReturns_Then_TheRootHasNotRendered()
        {
            // Arrange
            s_rendersWhenTheEffectReturned = -1;
            var owner = Mount(V.Component(MountsARootFromAnEffect, new VisualElement()));

            // Act
            owner.FlushEffectsForTest();

            // Assert
            Assert.That(s_rendersWhenTheEffectReturned, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base rendered the root inside the effect, so it has rendered by the time
        // the flush returns.
        [Test]
        public void Given_AUseEffectThatMountsARoot_When_ThePassiveFlushEnds_Then_ItHasRendered()
        {
            // Arrange
            var owner = Mount(V.Component(MountsARootFromAnEffect, new VisualElement()));

            // Act
            owner.FlushEffectsForTest();

            // Assert
            Assert.That(s_popupRenders, Is.EqualTo(1));
        }

        private static int s_rendersWhenTheHandlerReturned;

        [Component]
        private static VNode MountsARootFromAClick(VisualElement target)
            => V.Button(name: "mount", onClick: () =>
            {
                MountAPopup(target);
                s_rendersWhenTheHandlerReturned = s_popupRenders;
            });

        // GREEN_ON_BASE(characterization): the base rendered the root where the handler mounted it. React's batched
        // handler does not hold a synchronous mount either.
        [Test]
        public void Given_AClickHandlerThatMountsARoot_When_ItReturns_Then_TheRootHasRendered()
        {
            // Arrange
            var host = new VisualElement();
            s_mounts.Add(V.Mount(host, V.Component(MountsARootFromAClick, new VisualElement())));
            s_rendersWhenTheHandlerReturned = -1;

            // Act
            host.Q<Button>("mount").SimulateClick();

            // Assert
            Assert.That(s_rendersWhenTheHandlerReturned, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base rendered the other root inside the bare pass's ref setup. The pass is
        // an entry now, so it renders as that entry ends, and no render runs outside one.
        [Test]
        public void Given_ABareReconcileWhoseRefSetupClicksAnotherRoot_When_ItReturns_Then_TheOtherRootHasRendered()
        {
            // Arrange
            Mount(V.Component(RendersOnAClick, "b")).FlushEffectsForTest();
            var before = s_bRenders;
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(refCallback: _ =>
                {
                    s_bButton.SimulateClick();
                    return null;
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(s_bRenders - before, Is.EqualTo(1));
        }

        // --- A drain that throws, and a root's own handler fired in its drain ---

        private static StateUpdater<int> s_setThrower;
        private static readonly Ref<string> s_handle = new();

        // On its first update mounts a root from its layout effect, then fails in an imperative-handle factory, which
        // leaves the drain.
        [Component]
        private static VNode MountsThenThrows(VisualElement target)
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setThrower = setTick;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (tick == 1) MountAPopup(target);
                return null;
            }), new object[] { tick });
            Hooks.UseImperativeHandle(s_handle, () =>
            {
                if (tick == 1) throw new InvalidOperationException("handle");
                return "handle";
            });
            return V.Div();
        }

        // GREEN_ON_BASE(characterization): the base rendered the root inside the layout effect, ahead of the throw.
        [Test]
        public void Given_ADrainWhoseLayoutCommitThrows_When_ItHeldAMount_Then_TheRootHasRenderedWhenTheExceptionLeaves()
        {
            // Arrange
            var owner = Mount(V.Component(MountsThenThrows, new VisualElement()));
            s_setThrower.Invoke(1);

            // Act
            try
            {
                owner.GetSchedulerForTest().DrainImmediateForTest();
            }
            catch (InvalidOperationException)
            {
            }

            // Assert
            Assert.That(s_popupRenders, Is.EqualTo(1));
        }

        private static StateUpdater<int> s_setClicksItself;
        private static int s_ownEffectRuns;

        // Clicks its own button from the layout effect of an update, whose handler asks for nothing, and counts the runs of
        // that update's passive effect.
        [Component]
        private static VNode ClicksItselfInItsLayoutEffect()
        {
            var (n, setN) = Hooks.UseState(0);
            s_setClicksItself = setN;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (n > 0) s_ownButton.SimulateClick();
                return null;
            }), new object[] { n });
            Hooks.UseEffect(() =>
            {
                if (n > 0) s_ownEffectRuns++;
                return (Action)null;
            }, new object[] { n });
            return V.Button(name: "own", onClick: () => { }, refCallback: element =>
            {
                s_ownButton = (Button)element;
                return () => { };
            });
        }

        private static Button s_ownButton;

        // GREEN_ON_BASE(characterization): the base's handler flush is a no-op inside its own root's drain. The flush is
        // left to that drain, which renders what the handler queues; held instead, it would run the drain's Normal-lane
        // passive effects before control returned.
        [Test]
        public void Given_ARootsOwnHandlerFiredInItsDrain_When_TheDrainEnds_Then_ItsPassiveEffectsHaveNotRun()
        {
            // Arrange
            var owner = Mount(V.Component(ClicksItselfInItsLayoutEffect));
            owner.FlushEffectsForTest();
            s_ownEffectRuns = 0;
            s_setClicksItself.Invoke(1);

            // Act
            owner.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_ownEffectRuns, Is.Zero);
        }

        private static Button s_clicksItself;
        private static string s_countAndEcho;

        // A click asks for its own render. The UseEffect of the first clicks the button again, and that of the second asks
        // for an echo render, off the sync lane.
        [Component]
        private static VNode ClicksItselfFromItsEffect()
        {
            var (count, setCount) = Hooks.UseState(0);
            var (echo, setEcho) = Hooks.UseState(0);
            s_countAndEcho = count + ":" + echo;
            Hooks.UseEffect(() =>
            {
                if (count == 1) s_clicksItself.SimulateClick();
                if (count == 2) setEcho.Invoke(1);
                return (Action)null;
            }, new object[] { count });
            return V.Button(name: "clicks-itself", onClick: () => setCount.Invoke(count + 1), refCallback: element =>
            {
                s_clicksItself = (Button)element;
                return () => { };
            });
        }

        // GREEN_ON_BASE(characterization): the base rendered the second click by draining again inside the passive flush.
        // The update after it took the Normal lane and waited there, as it does here. The click is left to the drain now,
        // which is between its passes then; held instead, its flush would render the echo before control returned.
        [Test]
        public void Given_AClickAUseEffectOfItsRootsSyncLaneDrainFires_When_TheDrainEnds_Then_TheNormalLaneUpdateAfterItWaits()
        {
            // Arrange
            Mount(V.Component(ClicksItselfFromItsEffect)).FlushEffectsForTest();

            // Act
            s_clicksItself.SimulateClick();

            // Assert — 2:1 where the echo rendered, 1:0 where the second click never did
            Assert.That(s_countAndEcho, Is.EqualTo("2:0"));
        }

        // --- What disposing a root releases ---

        // GREEN_ON_BASE(characterization): the base releases the target's theme scope as the root is disposed, which no
        // case read.
        [Test]
        public void Given_ADisposedRoot_When_ItsTargetIsAskedForItsThemeScope_Then_ItHasNone()
        {
            // Arrange
            var target = new VisualElement();
            var mounted = V.Mount(target, V.Div());

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(StyleThemeTokens.ContextOf(target), Is.Null);
        }
    }
}
