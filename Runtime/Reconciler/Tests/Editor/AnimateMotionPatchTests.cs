using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Patch-path coverage for the animate-* motion binding driven through real state updates + batch drains on
    /// an EditorWindow panel: attach / detach on add / remove, restart on a changed mode or duration, no-restart
    /// on an unchanged spec, animate-none cancel, and — the load-bearing one — the Gradient pan re-asserting its
    /// 200% oversize after a gradient re-bake clobbers the background size. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class AnimateMotionPatchTests
    {
        private const string GradientBase = "w-[100px] h-[40px] bg-gradient-to-r to-blue-500";

        private EditorWindow _window;
        private MountedTree _mounted;
        private static StateUpdater<int> s_setStep;
        private static Func<int, string> s_classFor;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            s_setStep = default;
            s_classFor = _ => GradientBase;
            _window = ScriptableObject.CreateInstance<TestHostWindow>();
            _window.position = new Rect(0, 0, 800, 600);
            _window.Show();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_window != null)
            {
                _window.Close();
                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }
        }

        // The card's className is chosen by the current step, so a test seeds s_classFor then advances the step
        // to drive a real reconcile patch from one class list to the next.
        [Component]
        private static VNode RenderCard()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(className: s_classFor(step), name: "card");
        }

        private FiberBatchScheduler Scheduler => _mounted.Root.Reconciler.Context.BatchScheduler;
        private VisualElement Card => _window.rootVisualElement[0];
        private bool HasBinding => _mounted.Root.Reconciler.Context.AnimationBindings.ContainsKey(Card);
        private StyleAnimateBinding Binding => _mounted.Root.Reconciler.Context.AnimationBindings[Card];

        private void Mount(Func<int, string> classFor)
        {
            s_classFor = classFor;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCard));
        }

        private void Step(int n)
        {
            s_setStep.Invoke(n);
            Scheduler.DrainImmediateForTest();
        }

        [Test]
        public void Given_NoAnimateInitially_When_Mounted_Then_NoBinding()
        {
            Mount(_ => GradientBase);

            Assert.That(HasBinding, Is.False);
        }

        [Test]
        public void Given_AnimateAddedOnPatch_When_Drained_Then_BindingAttaches()
        {
            Mount(s => s == 0 ? GradientBase : GradientBase + " animate-gradient");
            Step(1);

            Assert.That(HasBinding, Is.True);
        }

        [Test]
        public void Given_AnimateRemovedOnPatch_When_Drained_Then_BindingDetaches()
        {
            Mount(s => s == 0 ? GradientBase + " animate-gradient" : GradientBase);
            Assume.That(HasBinding, Is.True, "Precondition: the motion attached while present");
            Step(1);

            Assert.That(HasBinding, Is.False);
        }

        [Test]
        public void Given_GradientSpecChangedUnderPan_When_Drained_Then_OversizeRetained()
        {
            // A from-/to- colour change re-bakes the gradient, which resets backgroundSize to 100% stretch.
            // The unchanged animate-gradient must re-assert its 200% oversize on the steady-state patch, else
            // the pan drags the gradient's clamped edge into the box.
            Mount(s => (s == 0 ? "from-red-500 " : "from-blue-500 ") + GradientBase + " animate-gradient");
            Step(1);

            Assert.That(Card.style.backgroundSize.value.x.value, Is.EqualTo(200f));
        }

        [Test]
        public void Given_ModeChangedOnPatch_When_Drained_Then_BindingRestartsWithNewMode()
        {
            Mount(s => GradientBase + (s == 0 ? " animate-gradient" : " animate-hue"));
            Step(1);

            Assert.That(Binding.Spec.Mode, Is.EqualTo(AnimateMode.Hue));
        }

        [Test]
        public void Given_DurationChangedOnPatch_When_Drained_Then_BindingRestartsWithNewDuration()
        {
            Mount(s => GradientBase + (s == 0 ? " animate-hue" : " animate-hue-[2s]"));
            Step(1);

            Assert.That(Binding.Spec.DurationSec, Is.EqualTo(2f));
        }

        [Test]
        public void Given_UnchangedSpecOnPatch_When_Drained_Then_SameBindingInstanceKept()
        {
            // A re-render with an identical animate spec must NOT restart (no churn) — the same binding survives.
            Mount(_ => GradientBase + " animate-hue");
            var before = Binding;
            Step(1);

            Assert.That(ReferenceEquals(before, Binding), Is.True);
        }

        [Test]
        public void Given_AnimateNoneOnPatch_When_Drained_Then_BindingDetaches()
        {
            Mount(s => GradientBase + (s == 0 ? " animate-hue" : " animate-hue animate-none"));
            Assume.That(HasBinding, Is.True, "Precondition: the motion attached before the cancel");
            Step(1);

            Assert.That(HasBinding, Is.False);
        }

        [Test]
        public void Given_PulseOverArbitraryOpacity_When_PulseRemoved_Then_ArbitraryOpacityRestored()
        {
            // Pulse owns the opacity slot, then is removed while an arbitrary opacity-[.3] survives in the class
            // list. Detach nulls the inline slot; DiffClassList never re-applies the unchanged opacity-[.3] token,
            // so without the post-detach re-assertion the element ghosts at full opacity. The arbitrary value must
            // return to .3 (RED before the reconciler re-asserts the class-driven inline slot, GREEN after).
            Mount(s => "w-[100px] h-[40px] bg-red-500 opacity-[.3]" + (s == 0 ? " animate-pulse" : string.Empty));
            Assume.That(HasBinding, Is.True, "Precondition: the pulse attached while present");
            Step(1);

            Assert.That(Card.style.opacity.value, Is.EqualTo(0.3f).Within(1e-5f));
        }

        /// <summary>Minimal EditorWindow host that supplies a real panel so the reconcile patch runs end-to-end.</summary>
        private sealed class TestHostWindow : EditorWindow { }
    }
}
