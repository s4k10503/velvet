using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// Every drop-shadow paint under an in-flight enter/exit is enrolled as that play's co-fade subject
    /// exactly once. The scheduler decides which plays cover a shadow by walking the caster's ANCESTORS, so
    /// the question is unanswerable while the caster is unparented — which is the state the factory attaches
    /// the paint in. Two ways that goes wrong meet here: a caster mounted under a play that started before it
    /// existed is enrolled by nobody, and a caster whose play started while the whole subtree was still
    /// detached is enrolled by both that play's own snapshot and the deferred attach-time pass.
    /// </summary>
    /// <remarks>
    /// A simulated panel rather than the panel-free setup: the enrolment samples the caster's live
    /// <c>resolvedStyle.opacity</c>, which resolves to a default off-panel — which is exactly why the missing
    /// mount-time wire was invisible without a panel.
    /// </remarks>
    [TestFixture]
    internal sealed class MountTimeShadowCoFadeTests
    {
        private readonly record struct ShowState(bool Shown);

        private sealed class ShowStore : Store<ShowState>
        {
            public ShowStore() : base(new ShowState(false)) { }
            public void Show() => SetState(_ => new ShowState(true));
            protected override void ResetCore() => SetState(_ => new ShowState(false));
        }

        private static readonly StyleTransitionConfig SpringEnter = new()
        {
            Type = TransitionType.Spring,
            Stiffness = 200f,
            Damping = 26f,
            EnterFromClass = "opacity-0",
            EnterToClass = "opacity-100",
        };

        private static readonly Dictionary<string, string> Fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private static ShowStore s_store;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_store = null;
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private void Tick() => _sim.FrameUpdateMs(16);

        // The shadowed child appears only once the store flips, so its class list carries shadow-lg from the
        // very first render of that element — the create path, not a class patch.
        [Component]
        private static VNode CasterWithLateChild()
        {
            var shown = Hooks.UseStore(s_store, s => s.Shown);
            return V.Div(name: "caster", className: "w-[80px] h-[80px]", children: shown
                ? new VNode[] { V.Div(name: "late", className: "shadow-lg w-[40px] h-[40px]") }
                : System.Array.Empty<VNode>());
        }

        // A Motion's standalone mount enter starts inside CreateElement, after the Motion's children exist and
        // while the whole subtree is still detached — so the play's own snapshot reaches the shadow before
        // anything parents it. A TWEEN enter, not a spring: a spring writes its from-value as an inline style
        // synchronously, so the caster reports opacity 0 the moment it attaches and a re-seed would land the
        // same 0 the snapshot already set. A tween's from-value is a CLASS, which the panel has not resolved
        // when the attach event fires, so only this shape separates one enrolment from two.
        [Component]
        private static VNode MotionWithShadowedChild()
        {
            return V.Motion(name: "m", variants: Fade, initial: "hidden", animate: "visible",
                transition: new StyleTransitionConfig { DurationSec = 0.5f },
                children: new VNode[] { V.Div(name: "late", className: "shadow-lg w-[40px] h-[40px]") });
        }

        // The co-fade subjects the scheduler holds for the enter running on `animating`. Private state, so
        // reflection: the list is the play's own bookkeeping and has no production reader.
        private static List<(VisualElement element, DropShadowBinding binding)> EnterCoFadeSubjects(
            StyleAnimationScheduler scheduler, VisualElement animating)
        {
            var map = (System.Collections.IDictionary)typeof(StyleAnimationScheduler)
                .GetField("_pendingEnters", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(scheduler);
            var pending = map[animating];
            return (List<(VisualElement element, DropShadowBinding binding)>)pending.GetType()
                .GetField("Shadows", BindingFlags.Public | BindingFlags.Instance)
                .GetValue(pending);
        }

        private static int SubjectCount(StyleAnimationScheduler scheduler, VisualElement animating,
            DropShadowBinding binding)
        {
            var subjects = EnterCoFadeSubjects(scheduler, animating);
            return subjects == null ? 0 : subjects.Count(s => ReferenceEquals(s.binding, binding));
        }

        [Test]
        public void Given_AnEnterInFlight_When_AnElementWhoseInitialClassListCarriesAShadowMounts_Then_ItsShadowCoFades()
        {
            // Arrange — a caster mid-climb through a spring enter, with no shadow anywhere in its subtree yet,
            // so the play's own snapshot cannot be what enrols the shadow that follows.
            using var store = new ShowStore();
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(CasterWithLateChild, key: "root"));
            var caster = Root.Q<VisualElement>("caster");
            var scheduler = mounted.Root.Reconciler.Context.StyleAnimationScheduler;
            scheduler.PlayEnter(caster, SpringEnter);
            Tick();
            Tick();
            Tick();

            // Act — a re-render mounts a shadowed child under the still-animating caster.
            store.Show();
            mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert — the paint exists AND is below full strength: enrolled at the caster's sampled opacity.
            // Unenrolled it would sit at its resting 1 and paint a hard box through the translucent caster.
            var binding = DropShadowSilhouette.TryGet(Root.Q<VisualElement>("late"));
            Assert.That((Painted: binding != null, CoFading: binding != null && binding.ShadowOpacity < 1f),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AMotionEnrollingItsChildShadowWhileDetached_When_TheSubtreeEntersThePanel_Then_TheShadowHoldsTheEnterFromValue()
        {
            // Arrange / Act — mounting the Motion runs its standalone enter during creation: the play seeds
            // the child shadow at the enter from-value (0) so nothing flashes on the first frame, and the
            // subtree is inserted only afterwards.
            using var mounted = V.Mount(Root, V.Component(MotionWithShadowedChild, key: "root"));

            // Assert — entering the panel must not re-seed a shadow this play already drives. Re-seeding
            // samples the caster's opacity before its from-class has resolved, reading 1 and putting the
            // shadow back to full strength over an opacity-0 card until the next driver frame corrects it.
            var binding = DropShadowSilhouette.TryGet(Root.Q<VisualElement>("late"));
            Assert.That((Painted: binding != null, AtFromValue: binding != null && binding.ShadowOpacity <= 1e-4f),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AMotionThatSnapshottedItsChildShadowWhileDetached_When_TheSubtreeAttaches_Then_TheShadowIsEnrolledOnce()
        {
            // Arrange / Act — the play's detached snapshot and the deferred attach-time enrolment both reach
            // the same binding, so this is the interleaving where one play can collect a shadow twice.
            using var mounted = V.Mount(Root, V.Component(MotionWithShadowedChild, key: "root"));

            // Assert — the play holds it once. A second copy is written by every co-fade tick and unwound by
            // an EndCoFade that only removes a driver once, so the list would stop matching what it releases.
            var m = Root.Q<VisualElement>("m");
            var binding = DropShadowSilhouette.TryGet(Root.Q<VisualElement>("late"));
            Assert.That(SubjectCount(mounted.Root.Reconciler.Context.StyleAnimationScheduler, m, binding),
                Is.EqualTo(1));
        }
    }
}
