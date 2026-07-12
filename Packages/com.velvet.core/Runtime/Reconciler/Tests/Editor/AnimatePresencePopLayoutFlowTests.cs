using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins PopLayout's geometry against a laid-out (simulated) panel. The pin must reproduce the
    /// child's last visual rect exactly: layout.x/y already include margins, and absolute
    /// positioning applies margins again, so pinning at the raw layout rect makes a margined child
    /// (explicit m-* or the child margins the gap-* emulation writes) jump the instant its exit
    /// starts. Cancelling the exit must restore what the pin overwrote without destroying
    /// arbitrary-value geometry (w-[..]/h-[..] live in those same inline slots and a re-add with an
    /// unchanged class list never re-applies them). And the whole point of the mode — siblings
    /// reflowing immediately — requires the index-driven gap manipulator to stop counting a pinned
    /// (absolute) ghost as an in-flow child.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresencePopLayoutFlowTests
    {
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore(string initial) : base(new SetState(initial)) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState("ab"));
        }

        private static SetStore s_store;
        private static Dictionary<char, string> s_itemClasses;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_store = null;
            s_itemClasses = null;
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private void Tick() => _sim.FrameUpdateMs(16);

        [Component]
        private static VNode PopRow()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                var extra = s_itemClasses != null && s_itemClasses.TryGetValue(key, out var cls) ? " " + cls : "";
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    className: "h-[20px] w-[40px]" + extra,
                    variants: s_fade, animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }));
            }
            return V.Div(name: "row", className: "flex flex-row gap-x-2", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false,
                    mode: AnimatePresenceMode.PopLayout, children: children.ToArray()),
            });
        }

        [Test]
        public void Given_AGapMarginedChild_When_ItsPopLayoutExitStarts_Then_ItStaysAtItsLastLaidOutPosition()
        {
            // Arrange — [a,b] under gap-x-2: b carries the gap emulation's leading margin, so its
            // laid-out x already includes it.
            using var store = new SetStore("ab");
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(PopRow, key: "row"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            var b = Root.Q<VisualElement>("item-b");
            var xBefore = b.layout.x;
            Assume.That(xBefore, Is.GreaterThan(0f), "Precondition: b sits after a with the gap margin applied");

            // Act — remove b; PopLayout pins it out of flow at its last rect.
            store.Set("a");
            scheduler.DrainImmediateForTest();
            Tick();

            // Assert — the pinned ghost has not moved (no margin double-application jump).
            Assert.That(b.layout.x, Is.EqualTo(xBefore).Within(0.5f));
        }

        [Test]
        public void Given_AnArbitraryWidthChild_When_ItsPopLayoutExitIsCancelled_Then_TheWidthSurvives()
        {
            // Arrange — b's width lives ONLY as the resolver-applied inline style of w-[60px].
            s_itemClasses = new Dictionary<char, string> { ['b'] = "w-[60px]" };
            using var store = new SetStore("ab");
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(PopRow, key: "row"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            var b = Root.Q<VisualElement>("item-b");
            Assume.That(b.resolvedStyle.width, Is.EqualTo(60f).Within(0.5f),
                "Precondition: the arbitrary width applied");

            // Act — start the exit (pin overwrites the inline slots), then cancel by re-adding.
            store.Set("a");
            scheduler.DrainImmediateForTest();
            store.Set("ab");
            scheduler.DrainImmediateForTest();
            Tick();

            // Assert — restoring to flow must not destroy the class-owned inline width.
            Assert.That(Root.Q<VisualElement>("item-b").resolvedStyle.width, Is.EqualTo(60f).Within(0.5f));
        }

        [Test]
        public void Given_TheFirstChildExitsUnderPopLayout_When_TheGhostIsPinned_Then_TheSurvivorReflowsToTheFront()
        {
            // Arrange — [a,b]: while a exits under PopLayout, b must reflow into a's place
            // immediately (that is the mode's purpose), which requires the gap manipulator to stop
            // counting the pinned ghost as an in-flow child.
            using var store = new SetStore("ab");
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(PopRow, key: "row"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Assume.That(Root.Q<VisualElement>("item-b").layout.x, Is.GreaterThan(0f),
                "Precondition: b starts behind a");

            // Act — remove a (the FIRST child) and let layout settle while the ghost is pinned.
            store.Set("b");
            scheduler.DrainImmediateForTest();
            Tick();

            // Assert — the survivor now leads the row (no leading gap margin, no reserved slot).
            Assert.That(Root.Q<VisualElement>("item-b").layout.x, Is.EqualTo(0f).Within(0.5f));
        }
    }
}
