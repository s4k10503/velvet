using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="AnimatePresenceMode.PopLayout"/>: the instant a keyed child starts exiting, it is pulled
    /// out of layout flow and pinned via inline absolute positioning (position/left/top/width/height) at the
    /// last rect it occupied, so still-present siblings are free to reflow into its place while its exit
    /// animation finishes on top of them. The pin is captured from the child's own last resolved layout, so a
    /// cancelled exit (the key re-added before the animation finishes) can simply clear those five inline
    /// styles to rejoin normal flow. Under <see cref="AnimatePresenceMode.Sync"/> (the default) none of this
    /// applies — an exiting child keeps participating in flow exactly as it did before this mode existed.
    /// Mounted in a real <see cref="EditorWindow"/> panel with a forced layout pass, because the pin only
    /// applies when the child's resolved rect is already finite (an un-laid-out panel leaves it NaN).
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresencePopLayoutTests
    {
        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore() : base(new SetState("abc")) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState("abc"));
        }

        private static SetStore s_store;
        private static AnimatePresenceMode s_mode;
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["visible"] = "opacity-100",
            ["hidden"] = "opacity-0",
        };

        private EditorWindow _window;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            _window = ScriptableObject.CreateInstance<TestHostWindow>();
            _window.Show();
            s_store = null;
            s_mode = AnimatePresenceMode.PopLayout;
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                _window.Close();
                Object.DestroyImmediate(_window);
                _window = null;
            }
        }

        [Component]
        private static VNode PresenceList()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    className: "w-[60px] h-[24px]",
                    variants: s_fade, animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }));
            }
            return V.Div(name: "presence-host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", mode: s_mode, children: children.ToArray()),
            });
        }

        // Mounts the list and forces a real layout pass so every child's `.layout` is finite BEFORE any exit
        // starts: the reconciler reads that pre-exit rect to pin a ghost, and by design skips pinning
        // altogether when it is not finite (an un-forced EditMode layout pass leaves it NaN).
        private MountedTree MountLaidOut()
        {
            var mounted = V.Mount(_window.rootVisualElement, V.Component(PresenceList, key: "list"));
            EditorPanelTestHelpers.ForcePanelUpdate(_window.rootVisualElement.panel);
            return mounted;
        }

        // "item-b" itself is z-managed (absolute + z-10): its real content relocates into "presence-host"'s own
        // front layer container, with a Motion child (found via FindFirstMotionDescendant, since the presence
        // child's own node is the Div wrapper, not a MotionNode) driving the actual enter/exit variants — an
        // "animated top-most modal" shape. The wrapper Div carries the name and the key; the z-layer placeholder
        // FiberZLayerCoordinator.CreatePlaceholder builds carries no name at all, so a query by name always
        // resolves the real element regardless of which one the animation machinery held as its own anchor.
        [Component]
        private static VNode ZManagedPresenceList()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Div(name: "item-" + key, key: key.ToString(), className: "absolute z-10",
                    children: new VNode[]
                    {
                        V.Motion(className: "w-[60px] h-[24px]",
                            variants: s_fade, animate: "visible", exit: "hidden",
                            transition: new StyleTransitionConfig { DurationSec = 0.3f }),
                    }));
            }
            return V.Div(name: "presence-host", className: "relative", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", mode: s_mode, children: children.ToArray()),
            });
        }

        // The z-* scope gate classifies "item-b" from its class NAME array alone (no live style needed), but
        // the bundled utility sheet is attached anyway so "absolute"/"relative" also resolve visually, matching
        // real usage (see ZIndexPanelTests.Mount's own comment on this same attach).
        private MountedTree MountZManagedLaidOut()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss");
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
            var mounted = V.Mount(_window.rootVisualElement, V.Component(ZManagedPresenceList, key: "list"));
            EditorPanelTestHelpers.ForcePanelUpdate(_window.rootVisualElement.panel);
            return mounted;
        }

        [Test]
        public void Given_AZManagedKeyedChild_When_ItsPopLayoutExitStarts_Then_TheRealElementNotThePlaceholderIsPinned()
        {
            // Arrange — "item-b" is a z-managed absolute Div; force a real layout pass so its pre-exit rect is
            // finite before the exit starts (the pin bails out silently on a non-finite rect either way, which
            // would make this test uninformative regardless of the fix).
            using var store = new SetStore();
            s_store = store;
            using var mounted = MountZManagedLaidOut();
            var ctx = mounted.Root.Reconciler.Context;
            var scheduler = ctx.BatchScheduler;
            var item = _window.rootVisualElement.Q<VisualElement>("item-b");
            Assume.That(ctx.ZLayerMembers.ContainsKey(item), Is.True,
                "Precondition: item-b actually relocated into its layer container");
            Assume.That(float.IsFinite(item.layout.width), Is.True,
                "Precondition: item-b's pre-exit rect is finite");

            // Act — remove the middle child; PopLayout pins the exiting ghost's anchor out of flow.
            store.Set("ac");
            scheduler.DrainImmediateForTest();

            // Assert — the pin landed on the REAL element (found by name — the placeholder carries no name at
            // all), not silently on a zero-size placeholder the animation dispatch held as its anchor instead.
            Assert.That(_window.rootVisualElement.Q<VisualElement>("item-b").style.position.value,
                Is.EqualTo(Position.Absolute));
        }

        [Test]
        public void Given_AZManagedElementWrappingMotion_When_FindFirstMotionDescendantRuns_Then_ReturnsTheNestedMotion()
        {
            // Arrange — a z-managed (absolute + z-10) Div wrapping a Motion: the only shape that can combine
            // z-* with an AnimatePresence-driven Motion, since z-* on a Motion itself is a documented no-op.
            var motion = V.Motion(transition: StyleTransition.FadeSlideUp);
            var wrapper = V.Div(className: "absolute z-10", children: new VNode[] { motion });

            // Act
            var resolved = FiberNodeFactory.FindFirstMotionDescendant(wrapper);

            // Assert — the walk descends into the z-managed wrapper's own children, exactly as it already does
            // for the transparent Provider/Fragment wrappers, so AnimatePresence's enter/exit dispatch can read
            // Transition + OnEnterComplete off the same node this method surfaces for an ordinary (non-z) wrap.
            Assert.That(resolved, Is.SameAs(motion));
        }

        [Test]
        public void Given_ANewlyAddedZManagedPresenceChild_When_ItEntersAndDrains_Then_TheEnterDispatchTargetsTheRealElement()
        {
            // Arrange — mount with "b" absent (its own Div/Motion never render this pass), mirroring the
            // "modal added to an already-mounted AnimatePresence" shape — the single most common
            // AnimatePresence+z use case — where the Initial flag's first-mount suppression does not apply.
            using var store = new SetStore();
            s_store = store;
            store.Set("ac");
            using var mounted = MountZManagedLaidOut();
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_window.rootVisualElement.Q<VisualElement>("item-b"), Is.Null,
                "Precondition: item-b has not mounted yet");

            // Act — add "b"; its EnqueueMount and this render's enter dispatch both run inside this SAME
            // synchronous pass, before the post-pass drain that (without eagerly registering the pair at
            // enqueue time) would be the only place ZLayerPlaceholders learns about it.
            store.Set("abc");
            scheduler.DrainImmediateForTest();

            // Assert — PlayEnter's own transition-duration inline write (applied synchronously, not deferred)
            // landed on the REAL element found by name, so the enter's anchor was resolved through the
            // registry to `real`, not left as the zero-size placeholder EmitPresenceChild would otherwise have
            // returned at this still-synchronous point.
            var real = _window.rootVisualElement.Q<VisualElement>("item-b");
            Assert.That(real.style.transitionDuration.keyword, Is.Not.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AZManagedPopLayoutExitInFlight_When_TheKeyReturnsBeforeItFinishes_Then_TheRealElementRejoinsFlowWithoutStrayGeometry()
        {
            // Arrange — item-b's exit has started (pinned on the REAL element via the registry); its 0.3s
            // duration has not elapsed (the EditMode scheduler never ticks a scheduled swap on its own).
            using var store = new SetStore();
            s_store = store;
            using var mounted = MountZManagedLaidOut();
            var ctx = mounted.Root.Reconciler.Context;
            var scheduler = ctx.BatchScheduler;
            store.Set("ac");
            scheduler.DrainImmediateForTest();
            var pinnedBeforeCancel = _window.rootVisualElement.Q<VisualElement>("item-b");
            Assume.That(pinnedBeforeCancel.style.position.value, Is.EqualTo(Position.Absolute),
                "Precondition: the exiting real element is pinned out of flow");

            // Act — re-add the key before the exit finishes, cancelling it.
            store.Set("abc");
            scheduler.DrainImmediateForTest();

            // Assert — the cancel clears item-b's own pin (position back to Null, rejoining the front layer
            // container's normal flow) AND leaves its width untouched by the nested Motion's own w-[60px]
            // class: RestorePopLayoutChildToFlow reapplies the ANCHOR's own declared classes (item-b's
            // "absolute z-10", neither an arbitrary-value token), not the differently-classed Motion nested
            // inside it — a stray reapply from the wrong element's class list would otherwise leave a
            // concrete (non-Null) width behind.
            var restored = _window.rootVisualElement.Q<VisualElement>("item-b");
            Assert.That((restored.style.position.keyword, restored.style.width.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_APopLayoutModeChild_When_ItStartsExiting_Then_ItsInlinePositionBecomesAbsolute()
        {
            // Arrange — three keyed children laid out in a real panel, so item-b already has a finite
            // resolved rect before its exit starts.
            using var store = new SetStore();
            s_store = store;
            using var mounted = MountLaidOut();
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var item = _window.rootVisualElement.Q<VisualElement>("item-b");
            Assume.That(float.IsFinite(item.layout.width), Is.True,
                "Precondition: the panel already laid out item-b (finite rect) before the exit starts");

            // Act — remove the middle child; PopLayout pins its ghost out of flow the instant its exit starts.
            store.Set("ac");
            scheduler.DrainImmediateForTest();

            // Assert — the exiting ghost is pinned via inline absolute positioning.
            Assert.That(_window.rootVisualElement.Q<VisualElement>("item-b").style.position.value,
                Is.EqualTo(Position.Absolute));
        }

        [Test]
        public void Given_APopLayoutModeChild_When_Pinned_Then_ItsInlineRectMatchesItsLastLaidOutBox()
        {
            // Arrange — capture item-b's last resolved (parent-relative) rect before removing it, so the
            // pinned inline rect can be checked against the box it actually occupied in flow.
            using var store = new SetStore();
            s_store = store;
            using var mounted = MountLaidOut();
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var item = _window.rootVisualElement.Q<VisualElement>("item-b");
            var lastLayout = item.layout;
            Assume.That(float.IsFinite(lastLayout.height), Is.True,
                "Precondition: item-b's pre-exit rect is finite");

            // Act — remove the middle child, pinning its ghost out of flow at that captured rect.
            store.Set("ac");
            scheduler.DrainImmediateForTest();

            // Assert — the pinned inline box (left/top/width/height) matches the rect it held in flow, so
            // still-present siblings are freed to reflow into exactly the space it no longer occupies.
            var pinned = _window.rootVisualElement.Q<VisualElement>("item-b");
            Assert.That(
                (pinned.style.left.value.value, pinned.style.top.value.value,
                    pinned.style.width.value.value, pinned.style.height.value.value),
                Is.EqualTo((lastLayout.x, lastLayout.y, lastLayout.width, lastLayout.height)));
        }

        [Test]
        public void Given_APopLayoutExitInFlight_When_TheKeyReturnsBeforeItFinishes_Then_ItsInlinePositionClearsToNull()
        {
            // Arrange — item-b's exit has started (and is pinned out of flow); its 0.3s duration has not
            // elapsed (the EditMode scheduler never ticks a scheduled swap on its own), so it is still exiting.
            using var store = new SetStore();
            s_store = store;
            using var mounted = MountLaidOut();
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set("ac");
            scheduler.DrainImmediateForTest();
            var pinnedBeforeCancel = _window.rootVisualElement.Q<VisualElement>("item-b");
            Assume.That(pinnedBeforeCancel.style.position.value, Is.EqualTo(Position.Absolute),
                "Precondition: the exiting ghost is pinned out of flow");

            // Act — re-add the key before the exit finishes, cancelling it.
            store.Set("abc");
            scheduler.DrainImmediateForTest();

            // Assert — the cancel clears the inline position back to Null, rejoining normal flow.
            Assert.That(_window.rootVisualElement.Q<VisualElement>("item-b").style.position.keyword,
                Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ASyncModeChild_When_ItIsExiting_Then_ItsInlinePositionStaysNull()
        {
            // Arrange — same three-child list, but under the default Sync mode.
            s_mode = AnimatePresenceMode.Sync;
            using var store = new SetStore();
            s_store = store;
            using var mounted = MountLaidOut();
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;

            // Act — remove the middle child; Sync mode keeps its exit in flow (no pinning).
            store.Set("ac");
            scheduler.DrainImmediateForTest();

            // Assert — no PopLayout pinning applies under Sync mode.
            Assert.That(_window.rootVisualElement.Q<VisualElement>("item-b").style.position.keyword,
                Is.EqualTo(StyleKeyword.Null));
        }

        /// <summary>Minimal EditorWindow host that supplies a real panel so layout resolves.</summary>
        private sealed class TestHostWindow : EditorWindow { }
    }
}
