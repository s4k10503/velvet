using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;

namespace Velvet.Tests
{
    /// <summary>
    /// Convergence contract for a staggered AnimatePresence list re-keyed across tabs: after a churn and a real
    /// settle on a known tab, every one of that tab's cards must be present and every prior-tab ghost must be
    /// gone — no card disappears, none pile up. This regressed on three independent layers; the one this pins
    /// deterministically is the exit-completion re-render bailing on an auto-memoized boundary: exit completion
    /// re-renders the boundary via ScheduleRerender, but the boundary's own hook inputs are unchanged, so an
    /// auto-memoized boundary returns its cached VNode and the reconciler bails — the AnimatePresence never
    /// re-expands and the finished ghost lingers forever (masked while tabs keep toggling; exposed once
    /// interaction stops). The fix invalidates the boundary's memo cache before that re-render
    /// (InvalidateMemoCache); reverting it strands the prior-tab ghosts so the settled set never reduces to the
    /// four expected cards.
    ///
    /// Runs in EditMode on a simulated panel: <see cref="Frame"/> advances the clock so the staggered exits
    /// actually complete and their drop re-renders run, deterministically draining the list to its settled state.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresenceListConvergenceTests : SimulatedPanelTestBase
    {
        private const float DurationSec = 0.1f;

        private static Action<int> s_setTab;

        protected override void OnSetUp() => s_setTab = null;

        private static string[] CardsForTab(int tab) => tab switch
        {
            0 => new[] { "a0", "a1", "a2", "a3" },
            1 => new[] { "b0", "b1" },
            _ => new[] { "c0", "c1", "c2" },
        };

        // One card, mirroring GemShopDialog.Card: a shadow-lg Div with CONDITIONAL children (a date badge / NEW
        // badge / remaining label that are null for some cards, so sibling counts differ per card and shift as
        // the list re-keys) plus a nested shadow-md inner Div.
        private static VNode Card(string name, int index)
        {
            return V.Motion(
                key: name,
                transition: StyleTransition.FadeSlideUp.With(durationSec: DurationSec),
                children: new VNode[]
                {
                    V.Div(className: "shadow-lg rounded-lg w-[120px] h-[160px]", children: new VNode[]
                    {
                        V.Label(text: "title-" + name),
                        V.Div(className: "relative w-[100px] h-[60px]", children: new VNode[]
                        {
                            (index % 2 == 0) ? V.Label(className: "absolute", text: "date-" + name) : null,
                            (index % 3 == 0) ? V.Label(className: "absolute", text: "NEW") : null,
                            V.Label(text: "art-" + name),
                        }),
                        (index % 2 == 1) ? V.Label(text: "remaining-" + name) : null,
                        V.Div(className: "shadow-md rounded-md w-[100px] h-[40px]", children: new VNode[]
                        {
                            V.Label(text: "buy-" + name),
                        }),
                    }),
                });
        }

        [Component]
        private static VNode DialogRender()
        {
            var (tab, setTab) = Hooks.UseState(0);
            s_setTab = setTab;
            var cards = CardsForTab(tab);
            return V.ScrollView(className: "absolute inset-0", children: new VNode[]
            {
                V.Div(className: "flex flex-row flex-wrap", children: new VNode[]
                {
                    V.AnimatePresence(
                        staggerSec: 0.04f,
                        children: V.List(cards, (c, i) => c, (c, i) => Card(c, i))),
                }),
            });
        }

        [Test]
        public void Given_RapidRekey_When_SettledOnKnownTab_Then_AllCardsPresent_NoneDisappeared()
        {
            // Arrange — the staggered gem-dialog list mounted on tab 0.
            using var mounted = V.Mount(Root, V.Component(DialogRender, key: "gem-converge"));
            Settle();
            Assume.That(s_setTab, Is.Not.Null, "Precondition: the dialog component mounted and exposed its tab setter");

            // Act — churn hard so a ghost overlap occurs at least once, then move toward tab 0 via a REAL
            // transition (2 -> 0). The drain-until-stable loop below is the actual settle gate; the frames here
            // only nudge the churn off its last random tab toward the target.
            var rng = new System.Random(424242);
            for (var i = 0; i < 60; i++)
            {
                s_setTab.Invoke(rng.Next(3));
                Frame(16);
            }
            s_setTab.Invoke(2);
            for (var i = 0; i < 16; i++) Frame(50);
            s_setTab.Invoke(0);

            // Drain until the live title set stops changing across several consecutive frames, rather than breaking
            // on a transient == 4 — exits complete staggered and removal is two-phase (complete → next render
            // drops), so the count passes through 4 only momentarily; a leaked ghost would instead make the set
            // stabilize ABOVE the four. The assertion then checks the STABLE set.
            var titles = new List<string>();
            var prevSnapshot = "";
            var stableStreak = 0;
            for (var i = 0; i < 300; i++)
            {
                Frame(40);
                titles.Clear();
                foreach (var lbl in Root.Query<Label>().ToList())
                {
                    if (lbl.text != null && lbl.text.StartsWith("title-")) titles.Add(lbl.text);
                }
                titles.Sort();
                var snapshot = string.Join(",", titles);
                if (snapshot == prevSnapshot)
                {
                    if (++stableStreak >= 20) break;
                }
                else
                {
                    stableStreak = 0;
                    prevSnapshot = snapshot;
                }
            }

            // Assert — Tab 0 is a0..a3: all four present, no missing card and no stranded ghost from b*/c*.
            Assert.That(titles, Is.EqualTo(new List<string> { "title-a0", "title-a1", "title-a2", "title-a3" }));
        }
    }
}
