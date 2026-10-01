using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins variant-Motion resolution across AnimatePresence: which element a presence expansion,
    /// enter, exit, and interruption resolve their classes against, and which configurations are
    /// genuinely inert enough to warn about. Three failure classes recur here. First, resolution
    /// target: a variant Motion sitting under a transparent wrapper (a z-managed Div or a
    /// ContextProvider) inside a keyed presence child must still resolve enter/exit CLASSES against
    /// its own element, not the wrapper the presence anchor walk finds; and a Motion that is not the
    /// presence's anchor (nested under a keyed Div) must keep its own standalone enter instead of
    /// being blanket-suppressed by the ambient expansion-depth counter. Second, interruption
    /// continuity: cancelling an exit (by re-adding the key) must resume the transition from the
    /// value the element currently shows — not clear the transition styles synchronously and pop,
    /// and must not replay the declared <c>initial</c> pose on a still-mounted element — and an
    /// enter cancelled by a classic exit, or a re-entry landing inside a completed exit's window,
    /// must both restore the resting <c>variants[animate]</c> classes rather than leaving the
    /// element parked at <c>initial</c> or <c>exit</c>. Third, diagnostics: <c>exit</c> outside
    /// AnimatePresence has nothing to defer the unmount for, so it must warn like the factory's
    /// other inert-configuration diagnostics, while a standalone <c>initial</c>/<c>animate</c> pair
    /// — which plays its own mount enter per Framer parity — must NOT warn, and an <c>initial</c>
    /// the enter machinery cannot resolve (no own <c>animate</c>) must.
    /// </summary>
    [TestFixture]
    internal sealed class MotionPresenceVariantResolutionTests
    {
        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore() : base(new SetState("a")) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState("a"));
        }

        private static SetStore s_store;
        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("light");
        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };
        // Classes no utility defines, on purpose: a spring finds no channel in them to drive, so a spring
        // exit over this pair completes SYNCHRONOUSLY (the swap lands, no tick) — the only EditMode-reachable
        // way to park a presence key in its completed-exit window. A color pair no longer does: springs drive
        // colors, so such an exit is still playing when the key returns. Each completed-exit case reads
        // IsExiting beside the parked pose, so a pair that starts playing again fails there.
        private static readonly Dictionary<string, MotionVariant> s_recolor = new()
        {
            ["hidden"] = "pose-exit",
            ["visible"] = "pose-rest",
        };
        // A third label distinct from the exit's: with initial == exit the initial→animate replay would
        // wash the exit residue out coincidentally, hiding a missing restoration.
        private static readonly Dictionary<string, MotionVariant> s_recolorWithStart = new()
        {
            ["hidden"] = "pose-exit",
            ["start"] = "pose-start",
            ["visible"] = "pose-rest",
        };
        private static bool s_withInitial;
        private static bool s_boxed;
        private static readonly ComponentContext<int> s_context = ComponentContext<int>.Create(0);

        private VisualElement _root;
        private EditorWindow _window;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_withInitial = false;
            s_boxed = false;
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

        private static VNode VariantMotion(string key)
            => V.Motion(name: "inner-" + key,
                variants: s_fade, initial: "hidden", animate: "visible", exit: "hidden",
                transition: new StyleTransitionConfig { DurationSec = 0.3f });

        [Component]
        private static VNode PresenceHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_fade,
                    animate: "visible",
                    initial: s_withInitial ? "hidden" : null,
                    exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false, children: children.ToArray()),
            });
        }

        [Component]
        private static VNode ZWrappedPresenceHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                var k = key.ToString();
                children.Add(V.Div(key: k, name: "wrapper-" + k, className: "absolute z-10",
                    children: new VNode[] { VariantMotion(k) }));
            }
            return V.Div(name: "host", className: "relative", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        [Component]
        private static VNode ProviderWrappedPresenceHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                var k = key.ToString();
                children.Add(V.Provider(ThemeContext, "dark", key: k,
                    children: new VNode[] { VariantMotion(k) }));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        [Component]
        private static VNode EnterInterruptHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                // initial + animate, NO exit label: the exit that interrupts the enter is the classic
                // preset path, which re-adds no variant classes of its own.
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_fade, initial: "hidden", animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        [Component]
        private static VNode CompletedExitHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_recolor, animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { Type = TransitionType.Spring }));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        // A variant Motion with no exit label: its removal plays the classic exit of this spring, whose class
        // pair names no channel either, so that exit completes synchronously too.
        private static readonly StyleTransitionConfig s_classicSpringExit = new()
        {
            Type = TransitionType.Spring,
            ExitFromClass = "anim-fade-exit-from",
            ExitToClass = "anim-fade-exit-to",
        };

        [Component]
        private static VNode CompletedClassicExitHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_recolor, animate: "visible", transition: s_classicSpringExit));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        // Whether the exit has completed and left element at variants[exit]: that pose is on it and nothing
        // is still playing there.
        private static bool Parked(MountedTree mounted, VisualElement element)
            => element != null && element.ClassListContains("pose-exit")
                && !mounted.Root.Reconciler.Context.StyleAnimationScheduler.IsExiting(element);

        [Component]
        private static VNode RecolorRow() => V.Motion(name: "item", variants: s_recolor, animate: "visible",
            exit: "hidden", transition: new StyleTransitionConfig { Type = TransitionType.Spring });

        // CompletedExitHost's Motion, behind a component, so it exits as a descendant rather than an anchor.
        [Component]
        private static VNode CompletedDescendantExitHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Component(RecolorRow, key: key.ToString()));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        // s_recolorWithStart's poses with a spring on the exit pose alone, so the exit completes inside the flush that
        // starts it while the enter takes the Motion's tween.
        private static readonly Dictionary<string, MotionVariant> s_springExitTweenEnter = new()
        {
            ["hidden"] = new MotionVariant("pose-exit", new StyleTransitionConfig { Type = TransitionType.Spring }),
            ["start"] = "pose-start",
            ["visible"] = "pose-rest",
        };

        [Component]
        private static VNode CompletedExitThenTweenEnterHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_springExitTweenEnter, initial: "start", animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        // A PopLayout presence whose keyed children are Providers around a Motion sized by arbitrary values, which the
        // Provider's node carries none of; s_boxed flips the Motion's element type while its key stays.
        [Component]
        private static VNode CompletedPopLayoutProviderHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Provider(s_context, 1, new VNode[]
                {
                    V.Motion(name: "item-" + key, elementType: s_boxed ? typeof(Box) : null,
                        className: "w-[60px] h-[24px]", variants: s_recolor, animate: "visible", exit: "hidden",
                        transition: new StyleTransitionConfig { Type = TransitionType.Spring }),
                }, key: key.ToString()));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", mode: AnimatePresenceMode.PopLayout, children: children.ToArray()),
            });
        }

        [Component]
        private static VNode CompletedExitWithInitialHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_recolorWithStart, initial: "start", animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { Type = TransitionType.Spring }));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        [Component]
        private static VNode WrappedPresence()
        {
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: new VNode[]
                {
                    // The keyed child is a plain Div, so the presence has no anchor Motion to
                    // animate; the nested Motion below is on its own.
                    V.Div(key: "card", name: "card", children: new VNode[]
                    {
                        V.Motion(name: "inner", variants: s_fade,
                            initial: "hidden", animate: "visible",
                            transition: new StyleTransitionConfig { DurationSec = 0.3f }),
                    }),
                }),
            });
        }

        [Test]
        public void Given_AVariantExitWasCancelled_When_TheElementReverts_Then_TheTransitionStaysAliveForTheReversal()
        {
            // Arrange — a real panel (the reversal is panel-interpolated; off-panel cancels clear
            // immediately), a settled child whose exit has started, then the key re-added mid-exit.
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            _window = ScriptableObject.CreateInstance<EditorWindow>();
            _window.Show();
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_window.rootVisualElement, V.Component(PresenceHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set("");
            scheduler.DrainImmediateForTest();
            var item = _window.rootVisualElement.Q<VisualElement>("item-a");
            Assume.That(item, Is.Not.Null, "Precondition: the exiting ghost is still mounted");

            // Act — cancel the exit by re-adding the key.
            store.Set("a");
            scheduler.DrainImmediateForTest();

            // Assert — the transition styles survive the cancel, so the panel interpolates from the
            // currently-resolved value back to the resting classes instead of snapping.
            Assert.That(_window.rootVisualElement.Q<VisualElement>("item-a").style.transitionDuration.keyword,
                Is.Not.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AVariantMotionWithInitial_When_ItsExitIsCancelled_Then_TheInitialPoseIsNotReplayed()
        {
            // Arrange — the full initial+animate+exit pattern; exit starts, then the key returns.
            s_withInitial = true;
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(PresenceHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set("");
            scheduler.DrainImmediateForTest();
            Assume.That(_root.Q<VisualElement>("item-a"), Is.Not.Null,
                "Precondition: the exiting ghost is still mounted");

            // Act — cancel the exit; the same still-attached element is reproduced (not a remount).
            store.Set("a");
            scheduler.DrainImmediateForTest();

            // Assert — initial applies only on a genuine first mount: the cancel leaves the element
            // at its resting variant instead of re-seeding the declared initial pose.
            var item = _root.Q<VisualElement>("item-a");
            Assert.That((item.ClassListContains("opacity-0"), item.ClassListContains("opacity-100")),
                Is.EqualTo((false, true)));
        }

        // The variant enter's synchronous strip-to-initial is the observable EditMode evidence (the
        // scheduler never fires the swap back to the resting classes) — the enter-gate oracle this
        // fixture relies on throughout.

        [Test]
        public void Given_AZWrappedVariantMotion_When_Mounted_Then_TheInnerMotionStartsAtItsInitialVariant()
        {
            // Arrange
            using var store = new SetStore();
            s_store = store;

            // Act
            using var mounted = V.Mount(_root, V.Component(ZWrappedPresenceHost, key: "host"));

            // Assert — the variant enter played against the Motion's own element: it carries
            // variants[initial] instead of resting at variants[animate].
            Assert.That(_root.Q<VisualElement>("inner-a").ClassListContains("opacity-0"), Is.True,
                "The z-wrapped Motion's variant enter resolves against its own element");
        }

        [Test]
        public void Given_AProviderWrappedVariantMotion_When_Mounted_Then_TheInnerMotionStartsAtItsInitialVariant()
        {
            // Arrange
            using var store = new SetStore();
            s_store = store;

            // Act
            using var mounted = V.Mount(_root, V.Component(ProviderWrappedPresenceHost, key: "host"));

            // Assert
            Assert.That(_root.Q<VisualElement>("inner-a").ClassListContains("opacity-0"), Is.True,
                "The Provider-wrapped Motion's variant enter resolves against its own element");
        }

        // A VARIANT exit cancels the in-flight enter, applies its from-classes — the resting
        // variants[animate] — and sets transition-property: all inline on its target, all
        // synchronously (only the swap to variants[exit] is scheduler-driven). The INNER element
        // resting at variants[animate] with the initial classes gone AND carrying the variant swap's
        // transition-property is the observable evidence the exit resolved the declared variants and
        // targeted the Motion: the silent classic fallback leaves the inner element untouched — frozen
        // at the enter's variants[initial] pose (or, with no enter fix either, resting with no
        // transition-property at all).

        private static (bool op0, bool op100, bool tpAll) VariantExitEvidence(VisualElement inner)
        {
            var tp = inner.style.transitionProperty;
            var tpAll = tp.keyword != StyleKeyword.Null && tp.value.Count > 0 && tp.value[0].ToString() == "all";
            return (inner.ClassListContains("opacity-0"), inner.ClassListContains("opacity-100"), tpAll);
        }

        [Test]
        public void Given_AZWrappedVariantMotion_When_TheKeyIsRemoved_Then_TheVariantExitTargetsTheInnerMotion()
        {
            // Arrange
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ZWrappedPresenceHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;

            // Act — remove the key; the ghost's exit must start on the Motion's own element.
            store.Set("");
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.AreEqual((false, true, true), VariantExitEvidence(_root.Q<VisualElement>("inner-a")),
                "The z-wrapped Motion's variant exit resolves against its own element");
        }

        [Test]
        public void Given_AProviderWrappedVariantMotion_When_TheKeyIsRemoved_Then_TheVariantExitTargetsTheInnerMotion()
        {
            // Arrange
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ProviderWrappedPresenceHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;

            // Act
            store.Set("");
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.AreEqual((false, true, true), VariantExitEvidence(_root.Q<VisualElement>("inner-a")),
                "The Provider-wrapped Motion's variant exit resolves against its own element");
        }

        [Test]
        public void Given_AZWrappedVariantMotionMidExit_When_TheKeyIsReAdded_Then_TheInnerMotionRestsAtItsAnimateVariant()
        {
            // Arrange — the exit must be cancelled ON THE MOTION'S ELEMENT (a cancel aimed only at the
            // wrapper would leave the inner exit running to completion, whose ghost-drop then removes the
            // freshly re-entered child).
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ZWrappedPresenceHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set("");
            scheduler.DrainImmediateForTest();
            Assume.That(_root.Q<VisualElement>("inner-a"), Is.Not.Null,
                "Precondition: the exiting ghost is still mounted");

            // Act — cancel the exit by re-adding the key.
            store.Set("a");
            scheduler.DrainImmediateForTest();

            // Assert — the cancel reverses the inner element toward its resting variant (initial is not
            // replayed on the still-attached element) AND scrubs the exit's inline transition styles (an
            // off-panel cancel clears immediately) — a cancel that misses the inner element would leave
            // both the pending exit and its inline duration alive there.
            var inner = _root.Q<VisualElement>("inner-a");
            Assert.AreEqual((false, true, true),
                (inner.ClassListContains("opacity-0"), inner.ClassListContains("opacity-100"),
                    inner.style.transitionDuration.keyword == StyleKeyword.Null),
                "Cancelling a wrapped Motion's variant exit restores its resting variant and clears the exit");
        }

        [Test]
        public void Given_AVariantEnterInFlight_When_AClassicExitInterruptsIt_Then_TheRestingVariantIsRestored()
        {
            // Arrange — mount plays the initial→animate enter; its strip leaves the element at
            // variants[initial] until the (scheduler-driven) swap, which EditMode never fires.
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(EnterInterruptHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<VisualElement>("item-a").ClassListContains("opacity-0"), Is.True,
                "Precondition: the enter is in flight at its initial pose");

            // Act — remove the key: the exit cancels the in-flight enter, and with no exit label the
            // CLASSIC exit follows, which has no variant from-classes to re-add.
            store.Set("");
            scheduler.DrainImmediateForTest();

            // Assert — the enter's cancel must restore the resting variants[animate] it had stripped,
            // so the ghost plays its exit from the resting pose instead of a bare, variant-less one.
            var item = _root.Q<VisualElement>("item-a");
            Assert.AreEqual((false, true),
                (item.ClassListContains("opacity-0"), item.ClassListContains("opacity-100")),
                "Cancelling an in-flight variant enter restores the resting variant");
        }

        // GREEN_ON_BASE(characterization): an anchor's completed variant exit was already undone on re-entry.
        [Test]
        public void Given_ACompletedVariantExit_When_TheKeyIsReAddedBeforeTheDropRender_Then_TheRestingVariantIsRestored()
        {
            // Arrange — remove the key with ONE flush (not a drain): the spring exit completes
            // synchronously (the swap lands the element at variants[exit]) and schedules the ghost-drop
            // re-render, which this single flush deliberately leaves pending.
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(CompletedExitHost, key: "host"));
            store.Set("");
            mounted.FlushStateForTest();
            var parked = Parked(mounted, _root.Q<VisualElement>("item-a"));

            // Act — re-add the key inside the completed-exit window; the re-entry reproduces the SAME
            // still-attached element.
            store.Set("a");
            mounted.FlushStateForTest();

            // Assert — the re-entry must put the resting pose back: no pending animation is left to
            // cancel, and the class diff cannot restore it (the resting set is still recorded as applied).
            var item = _root.Q<VisualElement>("item-a");
            Assert.AreEqual((true, false, true),
                (parked, item.ClassListContains("pose-exit"), item.ClassListContains("pose-rest")),
                "A re-entry after a completed exit restores the resting variant");
        }

        [Test]
        public void Given_ACompletedExitOnAMotionBehindAComponent_When_TheKeyIsReAddedBeforeTheDropRender_Then_ItsRestingVariantIsRestored()
        {
            // Arrange — the same completed-exit window as above, on a Motion the presence reaches as a descendant.
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(CompletedDescendantExitHost, key: "host"));
            var before = _root.Q<VisualElement>("item");
            store.Set("");
            mounted.FlushStateForTest();
            var parked = Parked(mounted, _root.Q<VisualElement>("item"));

            // Act
            store.Set("a");
            mounted.FlushStateForTest();

            // Assert — the exit parked the same element at variants[exit], and the re-entry put the resting pose back.
            var item = _root.Q<VisualElement>("item");
            Assert.That(
                (parked, ReferenceEquals(item, before), item.ClassListContains("pose-exit"),
                    item.ClassListContains("pose-rest")),
                Is.EqualTo((true, true, false, true)));
        }

        [Test]
        public void Given_ACompletedClassicExitOnAVariantMotion_When_TheKeyIsReAddedBeforeTheDropRender_Then_TheExitClassIsRemoved()
        {
            // Arrange — the same completed-exit window as above, reached by the classic exit.
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(CompletedClassicExitHost, key: "host"));
            store.Set("");
            mounted.FlushStateForTest();
            var item0 = _root.Q<VisualElement>("item-a");
            var parked = item0?.ClassListContains("anim-fade-exit-to") == true
                && !mounted.Root.Reconciler.Context.StyleAnimationScheduler.IsExiting(item0);

            // Act — re-add the key inside that window, reproducing the same still-attached element.
            store.Set("a");
            mounted.FlushStateForTest();

            // Assert — the completed exit parked its class on the element, and the re-entry took it off.
            Assert.That((parked, _root.Q<VisualElement>("item-a").ClassListContains("anim-fade-exit-to")),
                Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the re-entry already took an anchor's exit pose off first.
        [Test]
        public void Given_ACompletedVariantExitWithInitial_When_TheKeyIsReAddedBeforeTheDropRender_Then_TheExitPoseIsFullyReplaced()
        {
            // Arrange — same completed-exit window as above, with an `initial` label DISTINCT from the
            // exit's: the re-entry replays initial→animate, and only an explicit restoration removes the
            // exit classes first (the replay's own strip touches the resting label, not the exit's).
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(CompletedExitWithInitialHost, key: "host"));
            store.Set("");
            mounted.FlushStateForTest();
            var parked = Parked(mounted, _root.Q<VisualElement>("item-a"));

            // Act
            store.Set("a");
            mounted.FlushStateForTest();

            // Assert — the exit residue is gone and the (synchronously settled) spring replay landed the
            // element at its resting variant.
            var item = _root.Q<VisualElement>("item-a");
            Assert.AreEqual((true, false, true),
                (parked, item.ClassListContains("pose-exit"), item.ClassListContains("pose-rest")),
                "The re-entry replaces the exit pose before replaying the enter");
        }

        // GREEN_ON_BASE(characterization): the base skipped the PopLayout restore for an element it never pinned.
        [Test]
        public void Given_ACompletedPopLayoutExit_When_TheKeyReturnsOnANewElement_Then_ItsArbitrarySizeIsKept()
        {
            // Arrange — the spring exit completes inside this flush and leaves the drop render pending.
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(CompletedPopLayoutProviderHost, key: "host"));
            store.Set("");
            mounted.FlushStateForTest();
            var parked = Parked(mounted, _root.Q<VisualElement>("item-a"));

            // Act — the key returns inside the completed-exit window on a new element type.
            s_boxed = true;
            store.Set("a");
            mounted.FlushStateForTest();

            // Assert — the new element keeps the width its arbitrary class wrote.
            var item = _root.Q<VisualElement>("item-a");
            Assert.That((parked, item is Box, item.style.width.value.value), Is.EqualTo((true, true, 60f)));
        }

        // GREEN_ON_BASE(characterization): the base replays the enter on the element a completed exit left attached.
        [Test]
        public void Given_ACompletedVariantExit_When_TheKeyIsReAddedBeforeTheDropRender_Then_TheSameElementReplaysItsEnter()
        {
            // Arrange — the spring exit completes inside this flush and leaves the drop render pending.
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(CompletedExitThenTweenEnterHost, key: "host"));
            store.Set("");
            mounted.FlushStateForTest();
            var before = _root.Q<VisualElement>("item-a");
            var parked = Parked(mounted, before);

            // Act — re-add the key inside the completed-exit window.
            store.Set("a");
            mounted.FlushStateForTest();

            // Assert — the element the exit parked is kept, and its tween enter has stripped it to the initial pose.
            var item = _root.Q<VisualElement>("item-a");
            Assert.That((parked, ReferenceEquals(item, before), item.ClassListContains("pose-start")),
                Is.EqualTo((true, true, true)));
        }

        [Test]
        public void Given_ANonAnchorMotionInsideAPresenceChild_When_Mounted_Then_ItStartsAtItsInitialVariant()
        {
            // Arrange / Act — mount the presence subtree; the nested Motion is not the presence's
            // anchor, so its own initial→animate enter must play (it starts at the initial classes;
            // the EditMode scheduler never fires the swap, mirroring MotionScheduledMechanicsTests).
            using var mounted = V.Mount(_root, V.Component(WrappedPresence, key: "host"));

            // Assert — the enter was scheduled: the element carries variants[initial]'s classes
            // instead of mounting directly at rest.
            Assert.That(_root.Q<VisualElement>("inner").ClassListContains("opacity-0"), Is.True);
        }

        [Test]
        public void Given_AnInitialWithNoAnimateUnderALabelledParent_When_Mounted_Then_ItRestsAtTheInitialPoseWithoutAWarning()
        {
            // Arrange — Framer leaves a Motion naming only an initial at the values that initial names, taking
            // no label from its parent, and nothing is wrong with it.
            var warned = 0;
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.Contains("initial is set but has no resolvable enter"))
                {
                    warned++;
                }
            }
            Application.logMessageReceived += OnLog;

            // Act
            MountedTree mounted;
            try
            {
                mounted = V.Mount(_root, V.Motion(name: "p", animate: "visible", children: new VNode[]
                {
                    V.Motion(name: "m", variants: s_fade, initial: "hidden"),
                }));
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }
            using var _ = mounted;

            // Assert
            Assert.That((_root.Q<VisualElement>("m").ClassListContains("opacity-0"), warned), Is.EqualTo((true, 0)));
        }

        [Test]
        public void Given_AMotionWithExitOutsideAnimatePresence_When_Mounted_Then_ItWarnsThatExitIsInert()
        {
            // Arrange — the warning is expected (LogAssert fails the test if it never fires).
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("exit on a Motion outside AnimatePresence"));

            // Act — an `exit` variant declared with no AnimatePresence to defer the unmount for.
            using var mounted = V.Mount(_root,
                V.Motion(name: "m", variants: s_fade, animate: "visible", exit: "hidden"));

            // Assert — the element mounted; the expected warning is enforced by LogAssert at test
            // end (an Assert.Pass would bypass that unmatched-expectation check).
            Assert.That(_root.Q<VisualElement>("m"), Is.Not.Null);
        }

        [Test]
        public void Given_AMotionWithOnlyInitialAndAnimateOutsideAnimatePresence_When_Mounted_Then_ItDoesNotWarn()
        {
            // Arrange — capture log messages directly rather than relying on LogAssert's implicit
            // unmatched-message behavior: a Warning with no LogAssert.Expect does NOT fail a Unity test on
            // its own (only Error/Exception/Assert do), so the negative case needs a real assertion instead.
            var warned = false;
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning) warned = true;
            }
            Application.logMessageReceived += OnLog;
            try
            {
                // Act — a standalone entrance pair (no `exit`, no AnimatePresence).
                using var mounted = V.Mount(_root,
                    V.Motion(name: "m", variants: s_fade, initial: "hidden", animate: "visible"));
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            // Assert — initial/animate work standalone, so nothing is inert and nothing warns.
            Assert.That(warned, Is.False);
        }
    }
}
