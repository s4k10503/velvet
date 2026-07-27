using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using Object = UnityEngine.Object;

namespace Velvet.Tests
{
    /// <summary>
    /// Coverage for the filter-* transition tween (<see cref="StyleFilterTransitionDriver"/>), which lerps the
    /// inline filter's parameters itself whenever the resolved <c>transition-property</c> CONTAINS
    /// <c>filter</c> — the shape under which the engine's inline-filter setter leaves a write on its plain
    /// direct-write path instead of running it as its own uncancellable animation.
    /// Group A drives the pure <see cref="StyleFilterTransitionDriver.ApplyFrame"/> at explicit phases (the
    /// scheduler never ticks in EditMode; filter is geometry-independent, so no panel is needed to interpolate).
    /// Group B mounts a real <see cref="EditorWindow"/> panel with the bundled stylesheet so the transition-*
    /// longhands resolve, then exercises the real write hook through the arbitrary-value resolver; the cases
    /// that must distinguish "Velvet's write landed" from "the engine swallowed it and animated instead" assert
    /// on <c>resolvedStyle.filter</c> (what is painted) rather than <c>style.filter</c> (what was written), and
    /// drive the panel's animation phase on a fake clock so the reading is load-independent. GWT, one assert
    /// each.
    /// </summary>
    [TestFixture]
    internal sealed class FilterTransitionPanelTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        // Fake panel clock: the engine's animation phase reads elapsed time exclusively through the panel's
        // time function, so stepping this by hand makes every painted mid-animation value deterministic.
        private double _now;
        private readonly List<Object> _spawned = new();

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
            _now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, () => _now);
        }

        public override void TearDown()
        {
            base.TearDown();
            foreach (var obj in _spawned)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }
            _spawned.Clear();
        }

        // A user-authored custom filter definition (NOT one of the first-party brightness/saturate ones), with
        // one declared parameter slot per supplied default — the slot types drive what may interpolate.
        private FilterFunctionDefinition CreateUserDefinition(params FilterParameter[] parameterDefaults)
        {
            var def = ScriptableObject.CreateInstance<FilterFunctionDefinition>();
            var declarations = new FilterParameterDeclaration[parameterDefaults.Length];
            for (var i = 0; i < parameterDefaults.Length; i++)
            {
                declarations[i] = new FilterParameterDeclaration
                {
                    name = "p" + i,
                    interpolationDefaultValue = parameterDefaults[i],
                };
            }
            def.parameters = declarations;
            _spawned.Add(def);
            return def;
        }

        // A single-function list holding a user custom bound to def, carrying the supplied arguments.
        private static List<FilterFunction> CustomList(FilterFunctionDefinition def, params FilterParameter[] args)
        {
            var fn = new FilterFunction(def);
            foreach (var arg in args)
            {
                fn.AddParameter(arg);
            }
            return new List<FilterFunction> { fn };
        }

        // A single-blur inline filter list, the simplest interpolable filter (one float parameter).
        private static List<FilterFunction> BlurList(float px)
        {
            var fn = new FilterFunction(FilterFunctionType.Blur);
            fn.AddParameter(new FilterParameter(px));
            return new List<FilterFunction> { fn };
        }

        // A single brightness inline filter list. brightness renders as a FIRST-PARTY custom-filter function
        // (FilterFunctionType.Custom bound to BuiltInFilterDefinitions.Brightness), so it exercises the driver's
        // Custom interpolation path through a definition Velvet itself owns, rather than a user
        // filter-[name:args] one.
        private static List<FilterFunction> BrightnessList(float amount)
        {
            var fn = new FilterFunction(BuiltInFilterDefinitions.Brightness);
            fn.AddParameter(new FilterParameter(amount));
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

        // Advances the fake clock and runs the panel's animation phase, the pair a live panel performs once per
        // frame. What resolvedStyle reports afterwards is what would be painted.
        private void AdvanceAndPaint(IPanel panel, double seconds)
        {
            _now += seconds;
            EditorPanelTestHelpers.DriveAnimationsOnce(panel);
        }

        // The float parameter of the element's single PAINTED filter function.
        private static float PaintedFloat(VisualElement element)
            => element.resolvedStyle.filter.First().GetParameter(0).floatValue;

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
        public void Given_BrightnessTween_When_FrameAtMid_Then_BrightnessIsHalfway()
        {
            // Arrange — a built-in brightness 1.0 → 1.5 tween. brightness composes as FilterFunctionType.Custom,
            // so the driver must interpolate it like blur rather than treating every Custom as a discrete
            // instant write (which would leave the aligned channels empty and write no filter at all).
            Assume.That(BuiltInFilterDefinitions.Brightness, Is.Not.Null,
                "Precondition: the brightness shader resolved into a definition");
            var element = new VisualElement();
            var to = BrightnessList(1.5f);
            StyleFilterTransitionDriver.TryBuildChannels(BrightnessList(1f), to, out var channels);
            var binding = new StyleFilterTransitionBinding { Channels = channels, Easing = EasingMode.Linear, Target = to };

            // Act — the midpoint frame.
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);

            // Assert — the rebuilt Custom carries the brightness definition (not a definition-less Custom that
            // renders nothing) and lerps to halfway (1.25). RED without the built-in-custom fix (TryBuildChannels
            // bails on any Custom → zero channels → empty list → fn null), and RED if ApplyFrame drops the
            // definition threading (a rebuilt Custom would carry a null customDefinition). The definition's
            // filterName is the probe, not its reference: a material-invalidation rebuild hands back a fresh
            // definition instance, so a reference compare would flake once the cache is re-primed.
            var list = element.style.filter.value;
            FilterFunction? fn = list != null && list.Count > 0 ? list[0] : null;
            Assert.That((fn?.type, fn?.customDefinition?.filterName, fn?.GetParameter(0).floatValue),
                Is.EqualTo(((FilterFunctionType?)FilterFunctionType.Custom, "velvet-brightness", (float?)1.25f)));
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

        #region Group A2 — user custom filters (filter-[name:args])

        [Test]
        public void Given_TwoListsOfOneUserCustom_When_ChannelsBuilt_Then_TheCustomAligns()
        {
            // Arrange — the same user definition on both sides, one float argument each. The two functions are
            // the same shader with the same declared slots, so lerping the arguments is exactly what animating
            // that filter means.
            var def = CreateUserDefinition(new FilterParameter(0f));

            // Act
            var built = StyleFilterTransitionDriver.TryBuildChannels(
                CustomList(def, new FilterParameter(0f)), CustomList(def, new FilterParameter(1f)),
                out var channels);

            // Assert — one interpolable channel (RED while any user custom forces an instant write).
            Assert.That((built, channels.Length), Is.EqualTo((true, 1)));
        }

        [Test]
        public void Given_AUserCustomTween_When_FrameAtMid_Then_TheArgumentIsHalfway()
        {
            // Arrange — a user custom whose single float argument runs 0 → 1.
            var element = new VisualElement();
            var def = CreateUserDefinition(new FilterParameter(0f));
            var to = CustomList(def, new FilterParameter(1f));
            StyleFilterTransitionDriver.TryBuildChannels(CustomList(def, new FilterParameter(0f)), to, out var channels);
            var binding = new StyleFilterTransitionBinding { Channels = channels, Easing = EasingMode.Linear, Target = to };
            Assume.That(channels.Length, Is.EqualTo(1), "Precondition: the custom aligned into one channel");

            // Act
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);

            // Assert — the rebuilt function still carries its definition (a definition-less Custom renders
            // nothing) and its argument sits halfway.
            var fn = element.style.filter.value[0];
            Assert.That((ReferenceEquals(fn.customDefinition, def), fn.GetParameter(0).floatValue),
                Is.EqualTo((true, 0.5f)));
        }

        [Test]
        public void Given_AUserCustomWithAColorArgument_When_FrameAtMid_Then_TheColorIsHalfway()
        {
            // Arrange — a color slot interpolates like any other: the lerp helper handles colors and a color
            // argument is as continuous as a float one.
            var element = new VisualElement();
            var def = CreateUserDefinition(new FilterParameter(Color.black));
            var to = CustomList(def, new FilterParameter(Color.white));
            StyleFilterTransitionDriver.TryBuildChannels(CustomList(def, new FilterParameter(Color.black)), to, out var channels);
            var binding = new StyleFilterTransitionBinding { Channels = channels, Easing = EasingMode.Linear, Target = to };

            // Act
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);

            // Assert — one channel painting mid-grey, not a snap to either end. The count travels in the assert
            // rather than an Assume so an inadmissible color slot (which leaves no channel at all) fails here
            // instead of reporting inconclusive.
            var list = element.style.filter.value;
            Assert.That((list.Count, list.Count == 1 && Mathf.Approximately(list[0].GetParameter(0).colorValue.r, 0.5f)),
                Is.EqualTo((1, true)));
        }

        [Test]
        public void Given_TwoDifferentUserCustoms_When_ChannelsBuilt_Then_ItSnaps()
        {
            // Arrange — different definitions are different shaders; there is no correspondence between their
            // arguments to interpolate along.
            var a = CreateUserDefinition(new FilterParameter(0f));
            var b = CreateUserDefinition(new FilterParameter(0f));

            // Act
            var built = StyleFilterTransitionDriver.TryBuildChannels(
                CustomList(a, new FilterParameter(0f)), CustomList(b, new FilterParameter(1f)), out _);

            // Assert — an instant write.
            Assert.That(built, Is.False);
        }

        [Test]
        public void Given_AUserCustomWithDifferingArity_When_ChannelsBuilt_Then_ItSnaps()
        {
            // Arrange — the same definition but a different number of supplied arguments: the slots do not
            // line up pairwise, so there is nothing well-defined to lerp.
            var def = CreateUserDefinition(new FilterParameter(0f), new FilterParameter(0f));

            // Act
            var built = StyleFilterTransitionDriver.TryBuildChannels(
                CustomList(def, new FilterParameter(0f)),
                CustomList(def, new FilterParameter(0f), new FilterParameter(1f)), out _);

            // Assert
            Assert.That(built, Is.False);
        }

        [Test]
        public void Given_AUserCustomWithAMismatchedArgumentType_When_ChannelsBuilt_Then_ItSnaps()
        {
            // Arrange — same definition and arity, but one side supplies a color where the other supplies a
            // float; lerping across the two kinds is meaningless.
            var def = CreateUserDefinition(new FilterParameter(0f));

            // Act
            var built = StyleFilterTransitionDriver.TryBuildChannels(
                CustomList(def, new FilterParameter(0f)), CustomList(def, new FilterParameter(Color.white)), out _);

            // Assert
            Assert.That(built, Is.False);
        }

        [Test]
        public void Given_AUserCustomAddedOnOneSide_When_FrameAtMid_Then_ItFadesFromItsDeclaredNeutral()
        {
            // Arrange — a user custom present only in the to-list is padded from a neutral, and a user shader's
            // neutral is the one its own declaration states (the same value the engine pads its filter-list
            // transitions with). The declared 0.4 is chosen so the painted midpoint tells reading the
            // declaration (0.7) apart from assuming a native filter's zero (0.5).
            var element = new VisualElement();
            var def = CreateUserDefinition(new FilterParameter(0.4f));
            var to = new List<FilterFunction>(BlurList(12f));
            to.AddRange(CustomList(def, new FilterParameter(1f)));
            StyleFilterTransitionDriver.TryBuildChannels(BlurList(0f), to, out var channels);
            var binding = new StyleFilterTransitionBinding { Channels = channels, Easing = EasingMode.Linear, Target = to };

            // Act
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);

            // Assert — halfway from the declared 0.4 to 1. RED while an added user custom snaps (no channels at
            // all), and RED if the neutral is guessed from the function's shape instead of read (0.5).
            var list = element.style.filter.value;
            Assert.That((list.Count, list.Count == 2 && Mathf.Approximately(list[1].GetParameter(0).floatValue, 0.7f)),
                Is.EqualTo((2, true)));
        }

        [Test]
        public void Given_ABuiltInCustomAddedOnOneSide_When_FrameAtMid_Then_ItFadesFromOne()
        {
            // Arrange — brightness declares 1 as its neutral (brightness(1) is the CSS no-op), so reading the
            // declaration has to keep yielding the multiplicative identity rather than the 0 a blur fades from.
            Assume.That(BuiltInFilterDefinitions.Brightness, Is.Not.Null,
                "Precondition: the brightness shader resolved into a definition");
            var element = new VisualElement();
            var to = BrightnessList(1.5f);
            StyleFilterTransitionDriver.TryBuildChannels(null, to, out var channels);
            var binding = new StyleFilterTransitionBinding { Channels = channels, Easing = EasingMode.Linear, Target = to };

            // Act
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);

            // Assert — halfway from 1 to 1.5, not from 0.
            Assert.That(element.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(1.25f).Within(1e-5f));
        }

        [Test]
        public void Given_ABlurAddedBesideAPairedUserCustom_When_ChannelsBuilt_Then_BothChannelsAlign()
        {
            // Arrange — base filter-[custom:1], hover blur-4 filter-[custom:2]: the custom pairs on both sides
            // and only the blur is added. The custom needs no invented identity, and with a single distinct
            // user custom the canonical ranks stay strictly ordered, so the merge places both correctly.
            var def = CreateUserDefinition(new FilterParameter(0f));
            var to = new List<FilterFunction>(BlurList(4f));
            to.AddRange(CustomList(def, new FilterParameter(2f)));

            // Act
            var built = StyleFilterTransitionDriver.TryBuildChannels(
                CustomList(def, new FilterParameter(1f)), to, out var channels);

            // Assert — blur first (canonical order), then the custom (RED while any user custom bars the merge).
            // Joined rather than compared as arrays: a tuple assert compares each slot with Equals, which for
            // an array is reference identity.
            Assert.That((built, string.Join(",", channels.Select(c => c.Type))),
                Is.EqualTo((true, "Blur,Custom")));
        }

        [Test]
        public void Given_TwoDistinctUserCustomsAcrossAnAddOrRemove_When_ChannelsBuilt_Then_ItSnaps()
        {
            // Arrange — two different user definitions share the same last canonical rank, so the sorted merge
            // has no tiebreak and would emit them in an order the resolver does not compose in.
            var a = CreateUserDefinition(new FilterParameter(0f));
            var b = CreateUserDefinition(new FilterParameter(0f));
            var from = new List<FilterFunction>(CustomList(a, new FilterParameter(1f)));
            from.AddRange(CustomList(b, new FilterParameter(1f)));
            var to = new List<FilterFunction>(BlurList(4f));
            to.AddRange(CustomList(a, new FilterParameter(2f)));
            to.AddRange(CustomList(b, new FilterParameter(2f)));

            // Act
            var built = StyleFilterTransitionDriver.TryBuildChannels(from, to, out _);

            // Assert
            Assert.That(built, Is.False);
        }

        [Test]
        public void Given_AUserCustomDefinitionDestroyedMidTween_When_FrameApplied_Then_TheChannelIsOmitted()
        {
            // Arrange — a blur + user custom tween whose definition is destroyed while the tween is in flight.
            // The engine's filter-function constructor throws on a dead definition, and a Custom rebuilt
            // without one would render nothing anyway.
            var element = new VisualElement();
            var def = CreateUserDefinition(new FilterParameter(0f));
            var from = new List<FilterFunction>(BlurList(0f));
            from.AddRange(CustomList(def, new FilterParameter(0f)));
            var to = new List<FilterFunction>(BlurList(12f));
            to.AddRange(CustomList(def, new FilterParameter(1f)));
            StyleFilterTransitionDriver.TryBuildChannels(from, to, out var channels);
            var binding = new StyleFilterTransitionBinding { Channels = channels, Easing = EasingMode.Linear, Target = to };
            Object.DestroyImmediate(def);

            // Act
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);

            // Assert — the surviving blur still paints and the dead channel is dropped rather than throwing.
            // Both facts travel in the assert (rather than an Assume that the pair aligned) so a regression
            // making the pair inadmissible — which leaves an empty list — fails here instead of skipping.
            Assert.That(element.style.filter.value.Select(f => f.type), Is.EqualTo(new[] { FilterFunctionType.Blur }));
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
        public void Given_ARunningFilterTween_When_TheLayerReassertRuns_Then_ItsStartValueIsUnchanged()
        {
            // Arrange — a running blur tween, advanced one frame so the LIVE inline filter is mid-flight and
            // differs from where the tween started.
            var element = MountResolved("transition-filter");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);
            Assume.That(binding.Scheduled, Is.Not.Null, "Precondition: a tween is running");
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);
            var startedFrom = binding.Channels[0].From[0].floatValue;

            // Act — what a settling spring/bezier play does when it hands its slots back: re-assert every
            // arbitrary-value layer the element still carries.
            StyleArbitraryValueResolver.ReapplyLayeredValues(element);

            // Assert — the filter family is exempt from that re-assert, so the tween still starts where it
            // started. Without the exemption the re-resolve redirects it from the mid-frame value, restarting a
            // full duration from wherever the eye happened to be.
            Assert.That(binding.Channels[0].From[0].floatValue, Is.EqualTo(startedFrom));
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

        #region Group C — what the panel actually paints

        [Test]
        public void Given_TransitionFilterMidTween_When_TheFrameIsPainted_Then_ThePaintedFilterIsTheTweenValue()
        {
            // Arrange — a tween to blur-12 stepped to its own midpoint. The inline value alone cannot tell
            // whether the write landed: under a whole-property transition-property the engine swallows each
            // write and re-targets its own animation, leaving the PAINTED value near zero while the inline
            // value reads exactly 6.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);
            Assume.That(element.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(6f),
                "Precondition: the tween wrote its own midpoint");

            // Act — the panel paints a frame, 150 ms after the write.
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — the paint shows what the tween wrote, unmoved.
            Assert.That(PaintedFloat(element), Is.EqualTo(6f));
        }

        [Test]
        public void Given_TransitionColorsOverridingTheOptIn_When_FilterChanges_Then_InstantWrite()
        {
            // Arrange — transition-colors is declared after transition-filter in the bundled sheet, so at equal
            // specificity it wins transition-property while the opt-in class still registers a binding. The
            // resolved property names no filter, so this must stay a discrete change: guards the driver's probe
            // against matching any non-empty transition list.
            var element = MountResolved("transition-filter transition-colors");
            Assume.That(_mounted.Root.Reconciler.Context.FilterTransitionBindings.ContainsKey(element), Is.True,
                "Precondition: a binding is registered");
            Assume.That(element.resolvedStyle.transitionProperty.Select(p => p.ToString()), Does.Not.Contain("filter"),
                "Precondition: the resolved transition-property names no filter");

            // Act
            ApplyBlur(element, 12f);

            // Assert — the composed value lands immediately (RED if the driver tweens on any live duration).
            Assert.That(element.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(12f));
        }

        [Test]
        public void Given_TransitionAll_When_FilterChanges_Then_TheEngineAnimatesThePaintedFilter()
        {
            // CHARACTERIZATION PIN, not a requirement on Velvet: nothing in Velvet animates this, and the
            // asserted midpoint is what THIS editor's inline-filter setter does with a whole-property
            // transition. It is recorded because the whole design branches on it — the setter reaches its
            // animating path by matching the transition list against background-size, not filter. Should that
            // property-id ever change, this test keeps passing while the filter-naming tests below start
            // failing, and the right response is to re-measure both rather than to patch either.
            //
            // Arrange
            var element = MountResolved("transition-all duration-300");
            Assume.That(element.resolvedStyle.transitionProperty.Select(p => p.ToString()), Does.Not.Contain("filter"),
                "Precondition: the resolved transition-property is the whole-property value, not filter");

            // Act — the change is written, then the panel paints a frame at the animation's midpoint.
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — the paint is mid-flight, neither the old value nor the target.
            Assert.That(PaintedFloat(element), Is.EqualTo(6f).Within(0.5f));
        }

        [Test]
        public void Given_TransitionPropertyNamingFilterAmongOthers_When_FilterChanges_Then_TheTweenRuns()
        {
            // Arrange — a list naming filter alongside another property leaves the inline-filter setter on the
            // same direct-write path a lone filter does, so the tween owns the change exactly as CSS would.
            // Pins the probe as a CONTAINS test: narrowing it to "the only name" would silently snap this.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("filter"), new StylePropertyName("opacity") });
            ForcePanelUpdate(element.panel);
            Assume.That(element.resolvedStyle.transitionProperty.Select(p => p.ToString()),
                Is.EqualTo(new[] { "filter", "opacity" }), "Precondition: the multi-name list resolved");

            // Act
            ApplyBlur(element, 12f);

            // Assert — the tween took the write.
            Assert.That(binding.Scheduled, Is.Not.Null);
        }

        [Test]
        public void Given_ARunningTween_When_TheTransitionPropertyStopsNamingFilter_Then_TheTickSettlesAndStops()
        {
            // Arrange — a live tween whose transition-property is then rewritten out from under it (what a
            // Motion play does inline). From that moment every frame write would be taken over by the setter's
            // own animation and restarted from the painted value, so the tick must hand over instead of
            // ticking on. The fake clock keeps the tick's own elapsed reading irrelevant here.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);
            Assume.That(binding.Scheduled, Is.Not.Null, "Precondition: a tween is running");
            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("all") });
            ForcePanelUpdate(element.panel);

            // Act — the tick fires once after the rewrite.
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);

            // Assert — the tick settled to its target and stopped rather than continuing to write frames.
            Assert.That((binding.Scheduled, element.style.filter.value[0].GetParameter(0).floatValue),
                Is.EqualTo(((IVisualElementScheduledItem)null, 12f)));
        }

        #endregion

        #region Group D — what the inline-filter setter actually gates on

        // These three mount WITHOUT transition-filter, so no tween binding exists and the driver returns at its
        // first line. The resolver's instant write is then the only writer and the engine the only animator,
        // which makes each row a single-animator, fully deterministic reading of the setter's gate.
        //
        // Together they pin the gap between what Velvet's probe asks ("does the list name filter?") and what
        // the setter asks ("does the list name background-size, or a shorthand covering it?"). Today those two
        // questions disagree, which is exactly why naming filter is safe and naming the background-size family
        // is not. If the engine ever changes which property-id it queries, the second and third rows flip and
        // say so loudly — they are the canary for the whole design.
        private void MountWithInlineTransition(string className, out VisualElement element, params string[] properties)
        {
            element = MountResolved(className);
            Assume.That(_mounted.Root.Reconciler.Context.FilterTransitionBindings.ContainsKey(element), Is.False,
                "Precondition: no tween binding, so the engine is the only animator");
            var names = new List<StylePropertyName>();
            foreach (var property in properties)
            {
                names.Add(new StylePropertyName(property));
            }
            element.style.transitionProperty = new StyleList<StylePropertyName>(names);
            element.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(0.3f) });
            // Linear so a painted midpoint is exactly half the target. These elements carry no ease-* class, so
            // they would otherwise resolve the initial `ease` curve and land at 0.725 of the way.
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.Linear) });
            ForcePanelUpdate(element.panel);
        }

        [Test]
        public void Given_NoBindingAndATransitionNamingFilterAndOpacity_When_FilterChanges_Then_ThePaintIsInstant()
        {
            // Arrange — neither name is background-size-shaped, so the setter takes its direct-write path even
            // though the list names filter and a live duration is resolved. This is the property that makes
            // Velvet's tween able to paint its own frames at all.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "filter", "opacity");

            // Act
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — the composed value is painted outright, never animated.
            Assert.That(PaintedFloat(element), Is.EqualTo(12f));
        }

        [Test]
        public void Given_NoBindingAndATransitionNamingBackgroundSize_When_FilterChanges_Then_TheEngineAnimatesIt()
        {
            // Arrange — background-size is the property-id the setter actually queries, so naming it animates
            // the FILTER even though the list says nothing about filters.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "background-size");

            // Act
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — exactly half way at the half-way point, so a list naming this alongside filter would
            // put the engine's animation and Velvet's tween on the same property at once.
            Assert.That(PaintedFloat(element), Is.EqualTo(6f).Within(1e-3f));
        }

        [Test]
        public void Given_NoBindingAndATransitionNamingTheBackgroundScaleModeShorthand_When_FilterChanges_Then_TheEngineAnimatesIt()
        {
            // Arrange — the same gate also accepts any shorthand covering background-size, and
            // -unity-background-scale-mode is one, so it is a SECOND way into the engine's filter animation.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "-unity-background-scale-mode");

            // Act
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — exactly half way, same as naming background-size outright.
            Assert.That(PaintedFloat(element), Is.EqualTo(6f).Within(1e-3f));
        }

        #endregion
    }
}
