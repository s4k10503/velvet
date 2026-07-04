using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;

namespace Velvet.Tests
{
    /// <summary>
    /// Locks the inline-slot DESYNC-FREE invariant under realistic load. ChildReconciler keeps a recovery net for a
    /// baseline/DOM desync — the live container shorter than a fiber's committed baseline — that
    /// <c>TryRebuildDesyncedSlotRange</c> (keyed) and the indexed <c>slotExists</c> guard rebuild from the
    /// authoritative new tree. That net's production trigger (an out-of-band AnimatePresence ghost drop under rapid
    /// re-key) was eliminated upstream, so it now fires ONLY in fixtures that hand-build the desync
    /// (ReconcilerDesyncRecoveryTests / the desynced-non-last-tenant cases). This fixture is the guard that the
    /// realistic paths stay desync-free: it drives aggressive AnimatePresence exit + rapid-rekey + inline-sibling
    /// races on a simulated panel and asserts the recovery's DESTRUCTIVE rebuild never fires.
    ///
    /// The probe is element identity: a keyed child that never leaves the set keeps its VE instance across reorders
    /// (a keyed move re-parents the same element), so a CHANGED instance for a continuously-present anchor is the
    /// fingerprint of a destructive range rebuild — i.e. a desync slipped through. A regression that reintroduces an
    /// out-of-band ghost drop would surface here as a rebuilt anchor (or a stranded/duplicated wait-mode card),
    /// rather than silently relying on the recovery net to paper over it.
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerInlineSlotDesyncTests : SimulatedPanelTestBase
    {
        private const float DurationSec = 0.1f;
        private static Action<int> s_setGen;
        private static Action<int> s_setSide;

        protected override void OnSetUp()
        {
            s_setGen = null;
            s_setSide = null;
        }

        private static VNode Card(string k) =>
            V.Motion(
                key: k,
                name: "box-" + k,
                transition: StyleTransition.Fade.With(durationSec: DurationSec),
                children: new VNode[] { V.Label(text: "title-" + k) });

        // A presence list with a STABLE anchor card (always present) plus a sliding window of volatile cards whose
        // SIZE also oscillates, so every generation drops/adds volatile keys (exits perpetually in flight) AND the
        // presence's own child count fluctuates (stressing PropagateInlineSlotShift under the inline sibling).
        [Component(Compiler = false)]
        private static VNode PresenceList()
        {
            var (gen, setGen) = Hooks.UseState(0);
            s_setGen = setGen;
            var keys = new List<string> { "anchor" };
            var window = 2 + (gen % 3); // 2..4 volatile cards
            for (var i = 0; i < window; i++) keys.Add("v" + ((gen + i) % 7));
            return V.AnimatePresence(
                staggerSec: 0.02f,
                children: V.List(keys.ToArray(), (k, i) => k, (k, i) => Card(k)));
        }

        // An inline sibling sharing the parent Div with PresenceList: its independent re-renders perturb the shared
        // parent's multi-tenant inline-slot accounting.
        [Component(Compiler = false)]
        private static VNode SidePanel()
        {
            var (n, setN) = Hooks.UseState(0);
            s_setSide = setN;
            return V.Label(name: "side", text: "side-" + n);
        }

        // Sibling AFTER the presence: the presence owns slot 0; the sibling's MountSlotStart shifts as the presence
        // grows/shrinks under it.
        [Component(Compiler = false)]
        private static VNode HostSiblingAfter() =>
            V.Div(className: "flex flex-row", children: new VNode[]
            {
                V.Component(PresenceList, key: "plist"),
                V.Component(SidePanel, key: "side"),
            });

        // Sibling BEFORE the presence: the presence's own MountSlotStart is non-zero and shifts when the sibling
        // re-renders — exercising the captured-slotStart path of the bounded keyed range.
        [Component(Compiler = false)]
        private static VNode HostSiblingBefore() =>
            V.Div(className: "flex flex-row", children: new VNode[]
            {
                V.Component(SidePanel, key: "side"),
                V.Component(PresenceList, key: "plist"),
            });

        private void DriveChurn(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                s_setGen.Invoke(i + 1);
                if (i % 2 == 0) s_setSide.Invoke(i);
                Frame(8);
            }
        }

        private int FirstFrameAnchorRebuilt(int frames)
        {
            var anchor0 = Root.Q("box-anchor");
            Assume.That(anchor0, Is.Not.Null, "Precondition: the stable anchor card mounted");
            for (var i = 0; i < frames; i++)
            {
                s_setGen.Invoke(i + 1);
                if (i % 2 == 0) s_setSide.Invoke(i);
                Frame(8);
                if (!ReferenceEquals(Root.Q("box-anchor"), anchor0)) return i;
            }
            return -1;
        }

        [Test]
        public void Given_SiblingAfterPresence_When_ExitChurnAndSiblingRerender_Then_AnchorVeNeverDestructivelyRebuilt()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(HostSiblingAfter, key: "host"));
            Settle();

            // Act
            var brokenAt = FirstFrameAnchorRebuilt(200);

            // Assert
            Assert.That(brokenAt, Is.EqualTo(-1),
                "The continuously-present anchor's VE was destructively rebuilt at frame " + brokenAt
                + " — the inline-slot desync recovery fired.");
        }

        [Test]
        public void Given_SiblingBeforePresence_When_ExitChurnAndSiblingRerender_Then_AnchorVeNeverDestructivelyRebuilt()
        {
            // Arrange — the presence's own slot range starts at a non-zero, shifting offset.
            using var mounted = V.Mount(Root, V.Component(HostSiblingBefore, key: "host"));
            Settle();

            // Act
            var brokenAt = FirstFrameAnchorRebuilt(200);

            // Assert
            Assert.That(brokenAt, Is.EqualTo(-1),
                "The continuously-present anchor's VE (in a non-zero, shifting slot range) was destructively rebuilt "
                + "at frame " + brokenAt + " — the inline-slot desync recovery fired.");
        }

        // Wait-mode rapidly swapping the single visible key: each swap withholds the new child while the old exits,
        // a path where the live vs baseline count diverges most.
        private static Action<string> s_setWaitKey;

        [Component(Compiler = false)]
        private static VNode WaitHost()
        {
            var (key, setKey) = Hooks.UseState("k0");
            s_setWaitKey = setKey;
            return V.Div(className: "flex flex-row", children: new VNode[]
            {
                V.AnimatePresence(mode: AnimatePresenceMode.Wait, children: new VNode[]
                {
                    V.Motion(key: key, name: "box-" + key,
                        transition: StyleTransition.Fade.With(durationSec: DurationSec),
                        children: new VNode[] { V.Label(text: "title-" + key) }),
                }),
                V.Component(SidePanel, key: "side"),
            });
        }

        [Test]
        public void Given_WaitModeRapidSwap_When_OldExitsWhileNewWithheld_Then_NoStrandedOrDuplicatedCard()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(WaitHost, key: "wait-host"));
            Settle();
            Assume.That(s_setWaitKey, Is.Not.Null, "Precondition: the wait-mode host mounted");

            // Act — rapid key swaps with exits in flight, then settle to a known key.
            for (var i = 0; i < 80; i++)
            {
                s_setWaitKey.Invoke("k" + (i % 5));
                if (i % 2 == 0) s_setSide.Invoke(i);
                Frame(8);
            }
            s_setWaitKey.Invoke("k3");
            var titles = new List<string>();
            for (var i = 0; i < 200; i++)
            {
                Frame(40);
                titles.Clear();
                foreach (var lbl in Root.Query<Label>().ToList())
                {
                    if (lbl.text != null && lbl.text.StartsWith("title-")) titles.Add(lbl.text);
                }
                if (titles.Count == 1 && titles[0] == "title-k3") break;
            }

            // Assert — wait-mode settles to exactly the one selected card (no stranded ghost, no duplicate).
            Assert.That(titles, Is.EqualTo(new List<string> { "title-k3" }));
        }
    }
}
