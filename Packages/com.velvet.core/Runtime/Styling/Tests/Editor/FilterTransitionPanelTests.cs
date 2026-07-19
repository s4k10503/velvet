using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Coverage for the transition-filter opt-in tween (<see cref="StyleFilterTransitionDriver"/>), which lerps
    /// the inline filter's parameters because UI Toolkit cannot CSS-transition an inline filter.
    /// Group A drives the pure <see cref="StyleFilterTransitionDriver.ApplyFrame"/> at explicit phases (the
    /// scheduler never ticks in EditMode; filter is geometry-independent, so no panel is needed to interpolate).
    /// Group B mounts a real <see cref="EditorWindow"/> panel with the bundled stylesheet so the transition-*
    /// longhands resolve, then exercises the real write hook through the arbitrary-value resolver. GWT, one
    /// assert each.
    /// </summary>
    [TestFixture]
    internal sealed class FilterTransitionPanelTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        // A single-blur inline filter list, the simplest interpolable filter (one float parameter).
        private static List<FilterFunction> BlurList(float px)
        {
            var fn = new FilterFunction(FilterFunctionType.Blur);
            fn.AddParameter(new FilterParameter(px));
            return new List<FilterFunction> { fn };
        }

        // Mounts a named leaf, forces a layout pass so resolvedStyle.transitionDuration resolves, and returns it.
        private VisualElement MountResolved(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card", className: className));
            var element = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(element.panel);
            return element;
        }

        // Applies a blur through the arbitrary-value resolver — the exact path a class-diff / variant swap takes,
        // so it flows through ApplyCombinedFilter's transition hook.
        private static void ApplyBlur(VisualElement element, float px)
            => StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(ArbitraryProperty.FilterBlur, px, LengthUnit.Pixel));

        #region Group A — pure ApplyFrame

        [Test]
        public void Given_BlurTween_When_FrameAtMid_Then_BlurIsHalfway()
        {
            // Arrange — a blur 0 → 12 tween, linear, aligned into one channel.
            var element = new VisualElement();
            var to = BlurList(12f);
            StyleFilterTransitionDriver.TryBuildChannels(BlurList(0f), to, out var channels);
            var binding = new StyleFilterTransitionBinding { Channels = channels, Easing = EasingMode.Linear, Target = to };
            Assume.That(channels.Length, Is.EqualTo(1), "Precondition: one blur channel aligns");

            // Act — the midpoint frame.
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);

            // Assert — half of the way from 0 to 12 (an instant write would leave 0 or 12).
            Assert.That(element.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(6f));
        }

        [Test]
        public void Given_TweenSecondFrame_When_FrameApplied_Then_FreshListReference()
        {
            // Arrange — UI Toolkit dirties the inline filter for repaint only when the backing list REFERENCE
            // changes (it ref-compares, not content-compares), so each frame MUST write a fresh list.
            var element = new VisualElement();
            var to = BlurList(12f);
            StyleFilterTransitionDriver.TryBuildChannels(BlurList(0f), to, out var channels);
            var binding = new StyleFilterTransitionBinding { Channels = channels, Easing = EasingMode.Linear, Target = to };

            // Act — two successive frames.
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.25f);
            var first = element.style.filter.value;
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);
            var second = element.style.filter.value;

            // Assert — a distinct reference each frame (RED if the driver reuses one mutated list).
            Assert.That(ReferenceEquals(first, second), Is.False);
        }

        [Test]
        public void Given_NoneToBlur_When_FrameAtMid_Then_BlurFadesInFromZero()
        {
            // Arrange — a freshly-mounted element has no inline filter, so its from-list reads null (not []); the
            // added blur must fade in from its neutral value (0), not snap.
            var element = new VisualElement();
            var from = element.style.filter.value;
            Assume.That(from, Is.Null, "Precondition: a fresh element has no inline filter list");
            var to = BlurList(12f);
            StyleFilterTransitionDriver.TryBuildChannels(from, to, out var channels);
            var binding = new StyleFilterTransitionBinding { Channels = channels, Easing = EasingMode.Linear, Target = to };

            // Act
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);

            // Assert — halfway between the neutral 0 and 12.
            Assert.That(element.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(6f));
        }

        [Test]
        public void Given_EaseInOut_When_FrameAtQuarter_Then_ProgressBelowLinear()
        {
            // Arrange — the same blur 0 → 12, but ease-in-out, whose slow start puts the quarter-way progress
            // below the linear value (linear at t=0.25 would be exactly 3).
            var element = new VisualElement();
            var to = BlurList(12f);
            StyleFilterTransitionDriver.TryBuildChannels(BlurList(0f), to, out var channels);
            var binding = new StyleFilterTransitionBinding { Channels = channels, Easing = EasingMode.EaseInOut, Target = to };

            // Act
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.25f);

            // Assert — RED if Ease ignores the mode and lerps linearly (which would land at 3).
            Assert.That(element.style.filter.value[0].GetParameter(0).floatValue, Is.LessThan(3f));
        }

        #endregion

        #region Group B — detection / kickoff on a real panel

        [Test]
        public void Given_TransitionFilter_When_FilterChanges_Then_TweenBindingRuns()
        {
            // Arrange — the opt-in class registers a binding; the bundled sheet resolves a non-zero duration.
            var element = MountResolved("transition-filter");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            Assume.That(binding.Scheduled, Is.Null, "Precondition: no tween before the change");
            Assume.That(element.resolvedStyle.transitionDuration.First().value, Is.GreaterThan(0f),
                "Precondition: the transition-filter duration resolved");

            // Act — a filter change through the resolver (the class-diff / variant path).
            ApplyBlur(element, 12f);

            // Assert — the tween's tick is live (RED without the ApplyCombinedFilter hook).
            Assert.That(binding.Scheduled, Is.Not.Null);
        }

        [Test]
        public void Given_NoTransitionFilterClass_When_FilterChanges_Then_InstantWrite()
        {
            // Arrange — no opt-in class, so no binding: the opt-in gate must keep the change instant.
            var element = MountResolved("w-[100px] h-[40px]");
            Assume.That(_mounted.Root.Reconciler.Context.FilterTransitionBindings.ContainsKey(element), Is.False,
                "Precondition: no binding without the opt-in class");

            // Act
            ApplyBlur(element, 12f);

            // Assert — the composed value lands immediately, un-tweened.
            Assert.That(element.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(12f));
        }

        [Test]
        public void Given_ZeroDuration_When_FilterChanges_Then_InstantWrite()
        {
            // Arrange — duration-0 overrides the bundled default, so even with the opt-in the change is instant.
            var element = MountResolved("transition-filter duration-0");
            Assume.That(element.resolvedStyle.transitionDuration.First().value, Is.EqualTo(0f),
                "Precondition: duration-0 resolved to zero");

            // Act
            ApplyBlur(element, 12f);

            // Assert — the zero-duration guard writes the target immediately (RED if it started a 0s tween at 0).
            Assert.That(element.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(12f));
        }

        [Test]
        public void Given_RunningTween_When_Detached_Then_TickPaused()
        {
            // Arrange — a running filter tween.
            var element = MountResolved("transition-filter");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);
            Assume.That(binding.Scheduled, Is.Not.Null, "Precondition: a tween is running");

            // Act — teardown of the binding (class removed / element unmounted).
            StyleFilterTransitionDriver.Detach(element, binding);

            // Assert — the one-shot tick is paused and dropped (a filter transition owns no persistent slot).
            Assert.That(binding.Scheduled, Is.Null);
        }

        [Test]
        public void Given_RunningTweenAtMidFrame_When_DetachedWhileMounted_Then_SettlesToTarget()
        {
            // Arrange — a tween running to blur-12, advanced to a mid-frame so the inline value is neither end.
            var element = MountResolved("transition-filter");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);
            Assume.That(element.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(6f),
                "Precondition: the tween is mid-flight at half the target");

            // Act — the opt-in class is dropped while the element stays mounted (the filter-* class is unchanged,
            // so the resolver never re-asserts the static value).
            StyleFilterTransitionDriver.Detach(element, binding);

            // Assert — the cancelled tween settles to its target instead of freezing at the mid-frame value.
            Assert.That(element.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(12f));
        }

        #endregion
    }
}
