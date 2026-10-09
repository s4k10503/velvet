using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    /// inline filter's parameters itself wherever an entry of the resolved transition lists runs for
    /// <c>filter</c>, except where the engine's inline-filter setter animates the write by that same entry.
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
        private (VisualElement element, StyleAnimateBinding binding)? _hueLoop;

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
            if (_hueLoop != null)
            {
                StyleAnimateDriver.Detach(_hueLoop.Value.element, _hueLoop.Value.binding);
                _hueLoop = null;
            }
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

        private static void ApplyContrast(VisualElement element, float amount)
            => StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(ArbitraryProperty.FilterContrast, amount, LengthUnit.Pixel));

        private static void ClearContrast(VisualElement element)
            => StyleArbitraryValueResolver.Clear(element, ArbitraryProperty.FilterContrast);

        // Writes the transition longhands inline: one entry per property, the durations and delays as given, and
        // linear so a painted midpoint is exactly half the change.
        private static void SetInlineTransition(VisualElement element, string[] properties, TimeValue[] durations,
            TimeValue[] delays)
        {
            element.style.transitionProperty = new StyleList<StylePropertyName>(
                properties.Select(property => new StylePropertyName(property)).ToList());
            element.style.transitionDuration = new StyleList<TimeValue>(durations.ToList());
            element.style.transitionDelay = new StyleList<TimeValue>(delays.ToList());
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.Linear) });
            ForcePanelUpdate(element.panel);
        }

        // Advances the fake clock and runs the panel's animation phase, the pair a live panel performs once per
        // frame. What resolvedStyle reports afterwards is what would be painted.
        private void AdvanceAndPaint(IPanel panel, double seconds)
        {
            _now += seconds;
            EditorPanelTestHelpers.DriveAnimationsOnce(panel);
        }

        private static string ComputedPaddingForTrace(VisualElement element)
        {
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var computed = typeof(VisualElement).GetField("m_Style", flags)!.GetValue(element)!;
                return computed.GetType().GetProperty("paddingTop", flags)!.GetValue(computed)?.ToString() ?? "null";
            }
            catch (Exception error) { return $"unavailable:{error.GetType().Name}"; }
        }

        // The float parameter of the element's single PAINTED filter function.
        private static float PaintedFloat(VisualElement element)
            => element.resolvedStyle.filter.First().GetParameter(0).floatValue;

        // The binding the driver keeps for the element, which the write hook creates for a tween on an element
        // carrying no transition-filter; null where none exists.
        private static StyleFilterTransitionBinding DriverBinding(VisualElement element)
        {
            var table = (ConditionalWeakTable<VisualElement, StyleFilterTransitionBinding>)typeof(StyleFilterTransitionDriver)
                .GetField("s_bindings", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            return table.TryGetValue(element, out var binding) ? binding : null;
        }

        // A sized element added to the panel past the reconciler, which the resolver holds no layer for.
        private VisualElement AddBareElement()
        {
            var element = new VisualElement();
            element.style.width = 100;
            element.style.height = 40;
            _window.rootVisualElement.Add(element);
            ForcePanelUpdate(element.panel);
            return element;
        }

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

            // Assert — RED if the frame ignores the mode and lerps linearly (which would land at 3).
            Assert.That(element.style.filter.value[0].GetParameter(0).floatValue, Is.LessThan(3f));
        }

        [Test]
        public void Given_EveryEasingMode_When_FramesApplied_Then_TheBlurFollowsTheCurveAUssTransitionTakes()
        {
            // Arrange — blur 0 → 1, so a frame's parameter is the eased progress itself, compared against the
            // curve UI Toolkit eases a USS transition by for the same mode, clamped to 0..1 as the frame's
            // Mathf.Lerp clamps it.
            var convert = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.ComputedTransitionUtils")
                .GetMethod("ConvertTransitionFunction", BindingFlags.NonPublic | BindingFlags.Static);
            var element = new VisualElement();
            var to = BlurList(1f);
            StyleFilterTransitionDriver.TryBuildChannels(BlurList(0f), to, out var channels);
            var samples = new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };

            // Act
            var worst = Enum.GetValues(typeof(EasingMode)).Cast<EasingMode>().Select(mode =>
            {
                var curve = (Func<float, float>)convert.Invoke(null, new object[] { mode });
                var binding = new StyleFilterTransitionBinding { Channels = channels, Easing = mode, Target = to };
                return samples.Max(t =>
                {
                    StyleFilterTransitionDriver.ApplyFrame(element, binding, t);
                    return Mathf.Abs(element.style.filter.value[0].GetParameter(0).floatValue - Mathf.Clamp01(curve(t)));
                });
            }).ToArray();

            // Assert
            Assert.That(worst, Is.All.LessThan(1e-5f));
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
        public void Given_ABlurAddedBeforeAPairedUserCustom_When_ChannelsBuilt_Then_ItSnaps()
        {
            // Arrange — base filter-[custom:1], hover blur-4 filter-[custom:2]: the blur takes the position the
            // custom held, so the first position pairs a custom with a blur.
            var def = CreateUserDefinition(new FilterParameter(0f));
            var to = new List<FilterFunction>(BlurList(4f));
            to.AddRange(CustomList(def, new FilterParameter(2f)));

            // Act
            var built = StyleFilterTransitionDriver.TryBuildChannels(
                CustomList(def, new FilterParameter(1f)), to, out _);

            // Assert — discrete, as a CSS filter list whose shorter side is not the longer side's head.
            Assert.That(built, Is.False);
        }

        [Test]
        public void Given_ASecondUserCustomAddedAtTheEnd_When_ChannelsBuilt_Then_ItFadesIn()
        {
            // Arrange — filter-[a:1] → filter-[a:2] filter-[b:2]: the first custom pairs in place and a second,
            // different definition is added after it.
            var a = CreateUserDefinition(new FilterParameter(0f));
            var b = CreateUserDefinition(new FilterParameter(0f));
            var to = new List<FilterFunction>(CustomList(a, new FilterParameter(2f)));
            to.AddRange(CustomList(b, new FilterParameter(2f)));

            // Act
            var built = StyleFilterTransitionDriver.TryBuildChannels(CustomList(a, new FilterParameter(1f)), to, out var channels);

            // Assert — two channels, the second fading in from b's declared neutral. Joined rather than compared as
            // arrays: a tuple assert compares each slot with Equals, which for an array is reference identity.
            Assert.That((built, string.Join(",", channels.Select(c => c.To[0].floatValue + "<-" + c.From[0].floatValue))),
                Is.EqualTo((true, "2<-1,2<-0")));
        }

        [Test]
        public void Given_AGrayscaleWithABlurAddedBeforeIt_When_ChannelsBuilt_Then_ItSnaps()
        {
            // Arrange — grayscale → blur grayscale: the blur takes the grayscale's position.
            var gray = new FilterFunction(FilterFunctionType.Grayscale);
            gray.AddParameter(new FilterParameter(1f));
            var to = new List<FilterFunction>(BlurList(4f)) { gray };

            // Act
            var built = StyleFilterTransitionDriver.TryBuildChannels(new List<FilterFunction> { gray }, to, out _);

            // Assert — discrete, as CSS pads only the end of the shorter list.
            Assert.That(built, Is.False);
        }

        [Test]
        public void Given_ARepeatedChannelWithAFilterAddedAtTheEnd_When_ChannelsBuilt_Then_EachPairsByPosition()
        {
            // Arrange — blur(2) blur(4) → blur(4) blur(8) grayscale(1): one channel twice, then a filter added.
            var from = new List<FilterFunction>(BlurList(2f));
            from.AddRange(BlurList(4f));
            var gray = new FilterFunction(FilterFunctionType.Grayscale);
            gray.AddParameter(new FilterParameter(1f));
            var to = new List<FilterFunction>(BlurList(4f));
            to.AddRange(BlurList(8f));
            to.Add(gray);

            // Act
            var built = StyleFilterTransitionDriver.TryBuildChannels(from, to, out var channels);

            // Assert — each blur pairs with the one at its position, and the grayscale fades in from 0.
            Assert.That((built, string.Join(",", channels.Select(c => c.From[0].floatValue + "->" + c.To[0].floatValue))),
                Is.EqualTo((true, "2->4,4->8,0->1")));
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

        [Test]
        public void Given_ARunningTween_When_AChangeThatDoesNotInterpolateIsWritten_Then_TheTickStops()
        {
            // Arrange — a blur tweened half way in, then cleared, so the tween fades it out from there.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);
            binding.StartTime -= 0.15;
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);
            StyleArbitraryValueResolver.Clear(element, ArbitraryProperty.FilterBlur);
            var started = binding.Scheduled != null;

            // Act — a grayscale is written into the position the fading blur holds.
            StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(ArbitraryProperty.FilterGrayscale, 1f, LengthUnit.Pixel));

            // Assert — the fade ran, and the change, which pairs a grayscale with a blur, stopped it.
            Assert.That((started, binding.Scheduled == null), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ARunningTween_When_ItTicksOffThePanel_Then_NoFrameIsWritten()
        {
            // Arrange — a live tween, then the element taken out of the tree past the reconciler.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            var panel = element.panel;
            ApplyBlur(element, 12f);
            var started = binding.Scheduled != null;
            element.RemoveFromHierarchy();
            var written = element.style.filter.value;

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);

            // Assert — a tween ran, it is still running, and the tick wrote no new list.
            Assert.That((started, binding.Scheduled != null, ReferenceEquals(element.style.filter.value, written)),
                Is.EqualTo((true, true, true)));
        }

        // GREEN_ON_BASE(characterization): a tween off the panel past its end settles on its target, as on the base.
        [Test]
        public void Given_ARunningTweenOffThePanelPastItsEnd_When_ItTicks_Then_ItSettlesOnItsTarget()
        {
            // Arrange — a live tween whose element is taken out of the tree, and its whole duration then past.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            var panel = element.panel;
            ApplyBlur(element, 12f);
            element.RemoveFromHierarchy();
            binding.StartTime -= 10;

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);

            // Assert
            Assert.That((binding.Scheduled, element.style.filter.value[0].GetParameter(0).floatValue),
                Is.EqualTo(((IVisualElementScheduledItem)null, 12f)));
        }

        // GREEN_ON_BASE(characterization): nothing writes a filter onto an element after its pool reset, as on the base.
        // The base bound no tween on an element without transition-filter, so none could outlive the reset.
        [Test]
        public void Given_ARunningTweenTheWriteHookBound_When_ThePoolResetRuns_Then_TheNextTickWritesNoFilter()
        {
            // Arrange — a tween on an element no reconciler owns.
            var element = AddBareElement();
            SetInlineTransition(element, new[] { "filter" }, new[] { new TimeValue(0.3f) }, new[] { new TimeValue(0f) });
            ApplyBlur(element, 12f);

            // Act — the reset a pooled element gets, with the element still on the panel as the next consumer's.
            FiberElementPoolReset.ResetCommonState(element);
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);

            // Assert
            Assert.That(element.style.filter.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AnimateHue_When_AFilterChanges_Then_NoTweenStartsAndNoBlurIsWritten()
        {
            // Arrange — an animate-hue motion driving the filter of an element carrying transition-filter.
            var element = MountResolved("transition-filter animate-hue");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];

            // Act
            ApplyBlur(element, 12f);

            // Assert — the animation masks the change, as a CSS animation masks it in both the before- and the
            // after-change style, so nothing transitions and the animation's value stays.
            var written = element.style.filter.value;
            Assert.That((binding.Scheduled == null, written != null && written.Any(f => f.type == FilterFunctionType.Blur)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnimateHueBesideABlur_When_APatchRemovesIt_Then_TheBlurIsWrittenAtOnce()
        {
            // Arrange — the blur under an animate-hue motion, on an element carrying transition-filter.
            const string animated = "transition-filter animate-hue blur-md";
            const string plain = "transition-filter blur-md";
            var element = MountResolved(animated);
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];

            // Act
            _mounted.Root.Reconciler.Reconcile(
                _window.rootVisualElement,
                new VNode[] { V.Div(name: "card", className: animated) },
                new VNode[] { V.Div(name: "card", className: plain) });

            // Assert — the value the ended animation uncovers is written without a transition, as an ended CSS
            // animation starts none.
            Assert.That((binding.Scheduled == null, element.style.filter.value[0].GetParameter(0).floatValue),
                Is.EqualTo((true, 12f)));
        }

        [Test]
        public void Given_AnimateHueWithNoFilterUnderIt_When_APatchRemovesIt_Then_NoFilterIsPainted()
        {
            // Arrange — an animate-hue frame on an element carrying transition-filter and no filter utility.
            const string animated = "transition-filter animate-hue";
            const string plain = "transition-filter";
            var element = MountResolved(animated);
            StyleAnimateDriver.ApplyFrame(element, _mounted.Root.Reconciler.Context.AnimationBindings[element], 0.25f);

            // Act
            _mounted.Root.Reconciler.Reconcile(
                _window.rootVisualElement,
                new VNode[] { V.Div(name: "card", className: animated) },
                new VNode[] { V.Div(name: "card", className: plain) });
            AdvanceAndPaint(element.panel, 0.05);

            // Assert — cleared at once, as an ended CSS animation starts no transition.
            Assert.That(element.resolvedStyle.filter.Count(), Is.Zero);
        }

        [Test]
        public void Given_AnimateHueOverAHoverBlur_When_APatchRemovesIt_Then_TheBlurIsWrittenAtOnce()
        {
            // Arrange — a hover layer's blur applied while animate-hue drove the filter.
            const string animated = "transition-filter animate-hue";
            const string plain = "transition-filter";
            var element = MountResolved(animated);
            StyleArbitraryValueResolver.Apply(element,
                new ArbitraryStyle(ArbitraryProperty.FilterBlur, 12f, LengthUnit.Pixel), StyleLayerPriority.Hover);

            // Act
            _mounted.Root.Reconciler.Reconcile(
                _window.rootVisualElement,
                new VNode[] { V.Div(name: "card", className: animated) },
                new VNode[] { V.Div(name: "card", className: plain) });

            // Assert
            var written = element.style.filter.value;
            Assert.That(written != null && written.Count == 1 ? written[0].GetParameter(0).floatValue : float.NaN,
                Is.EqualTo(12f));
        }

        [Test]
        public void Given_ATweenRunningUnderAnimateHue_When_APatchRemovesIt_Then_TheTweenRunsOn()
        {
            // Arrange — a blur tween started, then animate-hue added over it.
            const string before = "transition-filter duration-300 blur-sm";
            const string tweening = "transition-filter duration-300 blur-md";
            const string animated = "transition-filter duration-300 animate-hue blur-md";
            var element = MountResolved(before);
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            Reconcile(before, tweening);
            Reconcile(tweening, animated);
            var startedAt = binding.StartTime;

            // Act
            Reconcile(animated, tweening);

            // Assert — the tween the animation hid runs on from where its clock is.
            Assert.That((binding.Scheduled != null, binding.StartTime == startedAt), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ARunningTween_When_APatchRemovesAnimatePulse_Then_TheTweenRunsOn()
        {
            // Arrange — a blur tween started on an element carrying animate-pulse.
            const string before = "transition-filter duration-300 animate-pulse blur-sm";
            const string tweening = "transition-filter duration-300 animate-pulse blur-md";
            const string plain = "transition-filter duration-300 blur-md";
            var element = MountResolved(before);
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            Reconcile(before, tweening);
            var startedAt = binding.StartTime;

            // Act
            Reconcile(tweening, plain);

            // Assert — the loop that ended animated no filter, so the tween runs on untouched.
            Assert.That((binding.Scheduled != null, binding.StartTime == startedAt), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ATweenRunningUnderAnimateHue_When_APatchRemovesIt_Then_ItsFrameIsWrittenAtOnce()
        {
            // Arrange — a blur tween from 4 to 12 started, then an animate-hue frame written over it.
            const string before = "transition-filter duration-300 blur-sm";
            const string tweening = "transition-filter duration-300 blur-md";
            const string animated = "transition-filter duration-300 animate-hue blur-md";
            var element = MountResolved(before);
            Reconcile(before, tweening);
            Reconcile(tweening, animated);
            StyleAnimateDriver.ApplyFrame(element, _mounted.Root.Reconciler.Context.AnimationBindings[element], 0.25f);

            // Act
            Reconcile(animated, tweening);

            // Assert — the tween's own frame, near where it started, rather than the hue frame, the target, or a
            // tween restarted from nothing.
            var written = element.style.filter.value[0];
            var amount = written.GetParameter(0).floatValue;
            Assert.That((written.type, amount > 1f && amount < 11f), Is.EqualTo((FilterFunctionType.Blur, true)));
        }

        [Test]
        public void Given_AnimateHue_When_AHueRotateUtilityChanges_Then_NoTweenStarts()
        {
            // Arrange — an animate-hue frame on an element carrying transition-filter.
            var element = MountResolved("transition-filter animate-hue");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            StyleAnimateDriver.ApplyFrame(element, _mounted.Root.Reconciler.Context.AnimationBindings[element], 0.1f);

            // Act — a change the motion's hue-rotate would pair with.
            StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(ArbitraryProperty.FilterHueRotate, 90f, LengthUnit.Pixel));

            // Assert
            Assert.That(binding.Scheduled, Is.Null);
        }

        [Test]
        public void Given_AnimateHueOverAHueRotateUtility_When_APatchRemovesIt_Then_TheUtilitysAngleIsWrittenAtOnce()
        {
            // Arrange — an animate-hue frame over hue-rotate-90.
            const string animated = "transition-filter animate-hue hue-rotate-90";
            const string plain = "transition-filter hue-rotate-90";
            var element = MountResolved(animated);
            StyleAnimateDriver.ApplyFrame(element, _mounted.Root.Reconciler.Context.AnimationBindings[element], 0.1f);

            // Act
            Reconcile(animated, plain);

            // Assert — 90, where a tween from the motion's 36 would still be near 36.
            Assert.That(element.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(90f));
        }

        // GREEN_ON_BASE(characterization): a change mid-tween redirects the tween to the new value, as on the base.
        [Test]
        public void Given_ARunningTween_When_ADifferentBlurIsWritten_Then_ItHeadsForTheNewValue()
        {
            // Arrange — a tween toward blur 12.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);

            // Act
            ApplyBlur(element, 6f);

            // Assert
            Assert.That(binding.Target![0].GetParameter(0).floatValue, Is.EqualTo(6f));
        }

        // GREEN_ON_BASE(characterization): a filter added at the end mid-tween redirects the tween to it, as on the base.
        [Test]
        public void Given_ARunningTween_When_AFilterIsAddedAtTheEnd_Then_ItHeadsForTheLongerList()
        {
            // Arrange — a tween toward blur 12.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);

            // Act — the same blur with a contrast after it.
            ApplyContrast(element, 1.5f);

            // Assert
            Assert.That(binding.Target?.Count, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): a running custom tween stops where its argument count changes, as on the base.
        [Test]
        public void Given_ARunningCustomTween_When_ItsArgumentCountChanges_Then_TheTweenStops()
        {
            // Arrange — a tween fading in a one-argument custom.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            var def = CreateUserDefinition(new FilterParameter(0f), new FilterParameter(0f));
            StyleFilterTransitionDriver.TryStartOrRedirect(element, CustomList(def, new FilterParameter(1f)));
            var started = binding.Scheduled != null;

            // Act — the same custom with two arguments, which pairs with nothing.
            StyleFilterTransitionDriver.TryStartOrRedirect(element,
                CustomList(def, new FilterParameter(1f), new FilterParameter(2f)));

            // Assert
            Assert.That((started, binding.Scheduled == null), Is.EqualTo((true, true)));
        }

        private void Reconcile(string from, string to)
            => _mounted.Root.Reconciler.Reconcile(
                _window.rootVisualElement,
                new VNode[] { V.Div(name: "card", className: from) },
                new VNode[] { V.Div(name: "card", className: to) });

        // Starts an animate-hue loop on the element, detached again at teardown.
        private void RunHueLoop(VisualElement element)
        {
            var binding = StyleAnimateDriver.Attach(element, new AnimateSpec(AnimateMode.Hue, 4f), panVertical: false);
            _hueLoop = (element, binding);
        }

        [Test]
        public void Given_AHueLoopOverARunningTween_When_ATweenFrameIsWritten_Then_TheLoopsHueRotateStands()
        {
            // Arrange
            var element = MountResolved("transition-filter");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);
            RunHueLoop(element);

            // Act
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.5f);

            // Assert
            Assert.That(element.style.filter.value[0].type, Is.EqualTo(FilterFunctionType.HueRotate));
        }

        [Test]
        public void Given_AHueLoopOverARunningTween_When_TheTweenSettles_Then_TheLoopsHueRotateStands()
        {
            // Arrange
            var element = MountResolved("transition-filter");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);
            RunHueLoop(element);

            // Act — a teardown while mounted settles the tween at its target.
            StyleFilterTransitionDriver.Detach(element, binding);

            // Assert
            Assert.That(element.style.filter.value[0].type, Is.EqualTo(FilterFunctionType.HueRotate));
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

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AnEngineTransition_When_AnInlineValueChanges_Then_ThePaintUsesTheDurationAtTheFinalWrite(bool classDiff)
        {
            // Arrange — retain the whole-property filter engine control. The class-diff row starts with
            // a resolved native-only leaf and a one-second padding transition, before any inline tokens.
            using var reconciler = new Reconciler();
            var root = _window.rootVisualElement;
            var oldTree = new[] { V.Div(name: "card", className: "relative") };
            VisualElement element;
            if (classDiff)
            {
                reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);
                element = root.Q<VisualElement>("card");
                element.style.paddingTop = 0f;
                ForcePanelUpdate(element.panel);
                SetInlineTransition(element, new[] { "padding-top" },
                    new[] { new TimeValue(1f) }, new[] { new TimeValue(0f) });
            }
            else
            {
                element = MountResolved("transition-all duration-300");
            }
            var wholeProperty = !element.resolvedStyle.transitionProperty
                .Select(p => p.ToString()).Contains("filter");

            // Act — AddClass writes padding before changing duration. The ordered replay then writes
            // padding again under the new duration; skipping it retains the earlier one-second run.
            if (classDiff)
            {
                // Keep the half-second samples at whole pixels to distinguish the two durations.
                var newTree = new[] { V.Div(name: "card", className: "relative p-[64px] pt-[16px] duration-[2s]") };
                reconciler.Reconcile(root, oldTree, newTree);
            }
            else
            {
                ApplyBlur(element, 12f);
            }
            AdvanceAndPaint(element.panel, classDiff ? 0.5 : 0.15);
            ForcePanelUpdate(element.panel);
            var midpoint = classDiff ? element.resolvedStyle.paddingTop : PaintedFloat(element);
            if (classDiff)
                TestContext.WriteLine($"native midpoint: computed={ComputedPaddingForTrace(element)}; resolved={midpoint}; pixelsPerPoint={EditorGUIUtility.pixelsPerPoint}");
            AdvanceAndPaint(element.panel, 2.0);
            ForcePanelUpdate(element.panel);
            var end = classDiff ? element.resolvedStyle.paddingTop : PaintedFloat(element);
            var target = classDiff ? element.style.paddingTop.value.value
                : element.style.filter.value[0].GetParameter(0).floatValue;
            var duration = classDiff ? element.style.transitionDuration.value[0].value
                : element.resolvedStyle.transitionDuration.First().value;
            var actual = new[] { wholeProperty ? midpoint : float.NaN, end, target, duration };
            TestContext.WriteLine($"classDiff={classDiff}; midpoint={midpoint}; end={end}; target={target}; duration={duration}");

            // Assert — paint distinguishes the two runs while their inline target and settled paint agree.
            Assert.That(actual, Is.EqualTo(classDiff ? new[] { 4f, 16f, 16f, 2f }
                : new[] { 6f, 12f, 12f, 0.3f }).Within(classDiff ? 0.02f : 0.5f));
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
        public void Given_TransitionPropertyNamingFilterAndBackgroundSize_When_FilterChanges_Then_OnlyTheEngineAnimatesIt()
        {
            // Arrange — the list names filter, so the tween binding would take the change, and background-size,
            // which is what the inline-filter setter animates on. Linear over 0.3s so the engine's midpoint is
            // exactly half the target.
            var element = MountResolved("transition-filter");
            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("filter"), new StylePropertyName("background-size") });
            element.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(0.3f) });
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.Linear) });
            ForcePanelUpdate(element.panel);

            // Act
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — the engine's animation toward 12 is half way. Were the tween also to take the change, its
            // start frame (the blur's neutral 0) would be the inline value the engine animates toward instead.
            // Without a binding the tween could not have taken it, so that reads as NaN.
            var painted = _mounted.Root.Reconciler.Context.FilterTransitionBindings.ContainsKey(element)
                ? PaintedFloat(element)
                : float.NaN;
            Assert.That(painted, Is.EqualTo(6f).Within(1e-3f));
        }

        [Test]
        public void Given_TransitionPropertyNamingFilterAndTheBackgroundScaleModeShorthand_When_FilterChanges_Then_OnlyTheEngineAnimatesIt()
        {
            // Arrange — the same as naming background-size, through the shorthand covering it.
            var element = MountResolved("transition-filter");
            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("filter"), new StylePropertyName("-unity-background-scale-mode") });
            element.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(0.3f) });
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.Linear) });
            ForcePanelUpdate(element.panel);

            // Act
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — as above, NaN without a binding.
            var painted = _mounted.Root.Reconciler.Context.FilterTransitionBindings.ContainsKey(element)
                ? PaintedFloat(element)
                : float.NaN;
            Assert.That(painted, Is.EqualTo(6f).Within(1e-3f));
        }

        [Test]
        public void Given_TransitionPropertyNamingFilterAndAll_When_FilterChanges_Then_OnlyTheEngineAnimatesIt()
        {
            // Arrange — the list names filter, so the tween binding would take the change, and `all`, which covers
            // background-size.
            var element = MountResolved("transition-filter");
            SetInlineTransition(element, new[] { "filter", "all" }, new[] { new TimeValue(0.3f) }, new[] { new TimeValue(0f) });

            // Act
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — the engine's animation toward 12 is half way; NaN without a binding, as above.
            var painted = _mounted.Root.Reconciler.Context.FilterTransitionBindings.ContainsKey(element)
                ? PaintedFloat(element)
                : float.NaN;
            Assert.That(painted, Is.EqualTo(6f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the tween takes a list whose background-size entry has nothing to run.
        [Test]
        public void Given_TransitionPropertyNamingFilterAndAZeroLengthBackgroundSize_When_FilterChanges_Then_TheTweenTakesIt()
        {
            // Arrange — background-size's entry has neither a duration nor a delay, so no transition runs for it
            // and the setter writes a filter change as it is.
            var element = MountResolved("transition-filter");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            SetInlineTransition(element, new[] { "filter", "background-size" },
                new[] { new TimeValue(0.3f), new TimeValue(0f) }, new[] { new TimeValue(0f) });

            // Act
            ApplyBlur(element, 12f);

            // Assert — the tween took the write.
            Assert.That(binding.Scheduled, Is.Not.Null);
        }

        [Test]
        public void Given_TransitionPropertyNamingFilterAndABackgroundSizeRunByItsDelay_When_FilterChanges_Then_TheTweenRunsIt()
        {
            // Arrange — background-size's entry has no duration but a 0.1s delay, so a transition runs for it: the
            // engine would hold the old value through the delay and then write the target.
            var element = MountResolved("transition-filter");
            SetInlineTransition(element, new[] { "filter", "background-size" },
                new[] { new TimeValue(0.3f), new TimeValue(0f) }, new[] { new TimeValue(0f), new TimeValue(0.1f) });

            // Act
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — the tween's start frame. Past its delay the engine would have written 12.
            Assert.That(PaintedFloat(element), Is.EqualTo(0f).Within(0.05f));
        }

        [Test]
        public void Given_TransitionPropertyNamingFilterAndBackgroundSize_When_TheTweenTicks_Then_ThePaintedFilterIsTheTweensFrame()
        {
            // Arrange — a tween under a list naming background-size on a duration of its own, started 0.15s of
            // filter's 0.3s ago.
            var element = MountResolved("transition-filter");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            SetInlineTransition(element, new[] { "filter", "background-size" },
                new[] { new TimeValue(0.3f), new TimeValue(0.2f) }, new[] { new TimeValue(0f) });
            ApplyBlur(element, 12f);
            binding.StartTime -= 0.15;

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);

            // Assert — about half way to 12, painted as the tick wrote it. Taken by the setter's own animation, the
            // frame would only start animating from the painted 0.
            Assert.That(PaintedFloat(element), Is.InRange(1.5f, 11f));
        }

        [Test]
        public void Given_ATransitionListWhoseFilterEntryIsNotTheFirst_When_TheTweenTicks_Then_ItRunsForTheFilterEntrysDuration()
        {
            // Arrange — opacity's entry runs 0.1s and filter's 0.4s; the tween started 0.1s ago.
            var element = MountResolved("transition-filter");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            SetInlineTransition(element, new[] { "opacity", "filter" },
                new[] { new TimeValue(0.1f), new TimeValue(0.4f) }, new[] { new TimeValue(0f) });
            ApplyBlur(element, 12f);
            binding.StartTime -= 0.1;

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);

            // Assert — a quarter of the way to 12, plus what the clock moved since. Run for opacity's 0.1s the
            // tween would have settled on 12; a tick that never ran would leave the start frame's 0.
            Assert.That(PaintedFloat(element), Is.InRange(1.5f, 11f));
        }

        [Test]
        public void Given_ATransitionListWhoseFilterEntryIsNotTheFirst_When_AFrameIsApplied_Then_ItEasesByTheFilterEntrysCurve()
        {
            // Arrange — opacity's entry eases in and filter's is linear.
            var element = MountResolved("transition-filter");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            SetInlineTransition(element, new[] { "opacity", "filter" }, new[] { new TimeValue(0.3f) }, new[] { new TimeValue(0f) });
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(new List<EasingFunction>
            {
                new EasingFunction(EasingMode.EaseIn), new EasingFunction(EasingMode.Linear),
            });
            ForcePanelUpdate(element.panel);
            ApplyBlur(element, 12f);

            // Act — the frame a quarter of the way through.
            StyleFilterTransitionDriver.ApplyFrame(element, binding, 0.25f);

            // Assert — a quarter of the way to 12; opacity's ease-in would put it at 0.75.
            Assert.That(PaintedFloat(element), Is.EqualTo(3f).Within(1e-3f));
        }

        [Test]
        public void Given_ATransitionDelay_When_TheTweenTicks_Then_ProgressCountsFromTheDelaysEnd()
        {
            // Arrange — a 0.2s delay before a 0.3s run; the tween started 0.35s ago.
            var element = MountResolved("transition-filter");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            SetInlineTransition(element, new[] { "filter" }, new[] { new TimeValue(0.3f) }, new[] { new TimeValue(0.2f) });
            ApplyBlur(element, 12f);
            binding.StartTime -= 0.35;

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);

            // Assert — half way to 12, plus what the clock moved since. Counted from the start instead, 0.35s of
            // 0.3s would have settled on 12.
            Assert.That(PaintedFloat(element), Is.InRange(1.5f, 11f));
        }

        [Test]
        public void Given_ADurationInMilliseconds_When_TheTweenTicks_Then_ItRunsForThatManyMilliseconds()
        {
            // Arrange — a 300ms run; the tween started 0.15s ago.
            var element = MountResolved("transition-filter");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            SetInlineTransition(element, new[] { "filter" }, new[] { new TimeValue(300f, TimeUnit.Millisecond) },
                new[] { new TimeValue(0f) });
            ApplyBlur(element, 12f);
            binding.StartTime -= 0.15;

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);

            // Assert — half way to 12, plus what the clock moved since. Read as 300 seconds, the blur would barely
            // have left 0.
            Assert.That(PaintedFloat(element), Is.InRange(1.5f, 11f));
        }

        [Test]
        public void Given_ATweenClearingAContrast_When_ItSettles_Then_NoEngineTransitionFollows()
        {
            // Arrange — a contrast tweened in and settled, then cleared, and that tween past its end. Clearing the
            // inline filter under an entry for filter is a write the engine animates itself.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyContrast(element, 1.5f);
            binding.StartTime -= 10;
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);
            ClearContrast(element);
            binding.StartTime -= 10;

            // Act — the tick settles, and the panel paints a frame.
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — nothing is painted. Left to the engine, the clear would pad the contrast from 0 and still be
            // fading it there.
            Assert.That(element.resolvedStyle.filter.Count(), Is.Zero);
        }

        [Test]
        public void Given_ATweenClearingABlur_When_ItSettles_Then_NoEngineTransitionFollows()
        {
            // Arrange — a blur tweened in and settled, then cleared, and that tween past its end.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);
            binding.StartTime -= 10;
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);
            StyleArbitraryValueResolver.Clear(element, ArbitraryProperty.FilterBlur);
            binding.StartTime -= 10;

            // Act — the tick settles, and the panel paints a frame.
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — nothing is painted. Clearing the inline filter under an entry for filter is a write the
            // engine animates itself, and that animation would still hold the tween's last frame.
            Assert.That(element.resolvedStyle.filter.Count(), Is.Zero);
        }

        [Test]
        public void Given_ARunningTween_When_TheTransitionPropertyGainsBackgroundSize_Then_TheTickSettlesAndStops()
        {
            // Arrange — a live tween, then a list that still names filter but also names background-size, from
            // which moment the setter animates every frame the tick writes.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);
            var started = binding.Scheduled != null;
            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("filter"), new StylePropertyName("background-size") });
            ForcePanelUpdate(element.panel);

            // Act — the tick fires once after the rewrite.
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);

            // Assert — a tween ran, and the tick stopped it.
            Assert.That((started, binding.Scheduled == null), Is.EqualTo((true, true)));
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

        // GREEN_ON_BASE(characterization): a tick under a list running nothing for filter settles, as it did before.
        [Test]
        public void Given_ARunningTween_When_TheTransitionPropertyNamesNeitherFilterNorAll_Then_TheTickSettlesAndStops()
        {
            // Arrange — a live tween whose transition-property is then rewritten to a list running nothing for
            // filter.
            var element = MountResolved("transition-filter duration-300");
            var binding = _mounted.Root.Reconciler.Context.FilterTransitionBindings[element];
            ApplyBlur(element, 12f);
            Assume.That(binding.Scheduled, Is.Not.Null, "Precondition: a tween is running");
            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("opacity") });
            ForcePanelUpdate(element.panel);

            // Act — the tick fires once after the rewrite.
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);

            // Assert — the tick settled to its target and stopped.
            Assert.That((binding.Scheduled, element.style.filter.value[0].GetParameter(0).floatValue),
                Is.EqualTo(((IVisualElementScheduledItem)null, 12f)));
        }

        [Test]
        public void Given_ANegativeTransitionDelay_When_AFilterChanges_Then_TheStartFrameIsPartway()
        {
            // Arrange — a 0.3s run whose delay is -0.15s, so it starts half way through.
            var element = MountResolved("transition-filter");
            SetInlineTransition(element, new[] { "filter" }, new[] { new TimeValue(0.3f) }, new[] { new TimeValue(-0.15f) });

            // Act
            ApplyBlur(element, 12f);

            // Assert — the frame written with the change is half way to 12, where a run counted from 0 starts at 0.
            Assert.That(PaintedFloat(element), Is.EqualTo(6f).Within(0.5f));
        }

        #endregion

        #region Group D — elements carrying no transition-filter

        // These mount WITHOUT transition-filter, so the reconciler binds no tween: any tween that runs is one the
        // write hook bound, and otherwise the resolver's instant write is the only writer.
        private void MountWithInlineTransition(string className, out VisualElement element, params string[] properties)
        {
            element = MountResolved(className);
            Assume.That(_mounted.Root.Reconciler.Context.FilterTransitionBindings.ContainsKey(element), Is.False,
                "Precondition: the reconciler bound no tween");
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
        public void Given_NoBindingAndATransitionNamingFilterAndOpacity_When_FilterChanges_Then_TheTweenRunsIt()
        {
            // Arrange — a hand-authored list naming filter among other properties, on an element without the class.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "filter", "opacity");

            // Act
            ApplyBlur(element, 12f);

            // Assert — the write hook bound a tween and started it.
            Assert.That(DriverBinding(element)?.Scheduled, Is.Not.Null);
        }

        [Test]
        public void Given_NoBindingAndATransitionNamingFilter_When_TheFilterIsCleared_Then_TheTweenRunsIt()
        {
            // Arrange — a blur written while nothing transitions, then a hand-authored list naming filter.
            var element = MountResolved("w-[100px] h-[40px]");
            ApplyBlur(element, 12f);
            SetInlineTransition(element, new[] { "filter" }, new[] { new TimeValue(0.3f) }, new[] { new TimeValue(0f) });

            // Act
            StyleArbitraryValueResolver.Clear(element, ArbitraryProperty.FilterBlur);

            // Assert — the removal is the tween's to run, as adding a filter is.
            Assert.That(DriverBinding(element)?.Scheduled, Is.Not.Null);
        }

        [Test]
        public void Given_NoBindingAndATransitionNamingBackgroundSize_When_FilterChanges_Then_ThePaintIsInstant()
        {
            // Arrange — the list names background-size, which the inline-filter setter animates a filter write by,
            // and nothing that covers filter.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "background-size");

            // Act
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — painted outright, where the setter's animation would be half way.
            Assert.That(PaintedFloat(element), Is.EqualTo(12f));
        }

        [Test]
        public void Given_NoBindingAndATransitionNamingTheBackgroundScaleModeShorthand_When_FilterChanges_Then_ThePaintIsInstant()
        {
            // Arrange — -unity-background-scale-mode is the shorthand covering background-size.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "-unity-background-scale-mode");

            // Act
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — painted outright, same as naming background-size.
            Assert.That(PaintedFloat(element), Is.EqualTo(12f));
        }

        #endregion

        #region Group E — the contrast an animated instant write pads from

        // Mounted WITHOUT transition-filter, like Group D, under lists whose `all` entry the engine animates a filter
        // write by, so no tween runs and the engine is the only animator.

        private static List<FilterFunction> ContrastList(float amount)
        {
            var fn = new FilterFunction(FilterFunctionType.Contrast);
            fn.AddParameter(new FilterParameter(amount));
            return new List<FilterFunction> { fn };
        }

        // GREEN_ON_BASE(characterization): the engine pads a contrast the list gains from 0, not CSS's identity 1.
        // StyleFilterEngineWrite exists for this difference and names this case.
        [Test]
        public void Given_AWholePropertyTransition_When_AContrastIsWrittenStraightToTheInlineFilter_Then_TheEngineFadesItInFromZero()
        {
            // Arrange
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "all");

            // Act — written past the resolver.
            element.style.filter = ContrastList(2f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — half way from 0 to 2.
            Assert.That(PaintedFloat(element), Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void Given_NoBindingAndAWholePropertyTransition_When_AContrastIsAdded_Then_ItFadesInFromOne()
        {
            // Arrange
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "all");

            // Act
            ApplyContrast(element, 2f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — half way from CSS's identity 1 to 2; the engine's own padding would put it at 1.
            Assert.That(PaintedFloat(element), Is.EqualTo(1.5f).Within(1e-3f));
        }

        [Test]
        public void Given_NoBindingAndAWholePropertyTransition_When_AContrastIsAddedWhileAnotherFilterAnimates_Then_ItFadesInFromOne()
        {
            // Arrange — a blur half way through the engine's transition toward 12.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "all");
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.15);

            // Act — a contrast joins the list after the blur.
            ApplyContrast(element, 2f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — half way from 1 to 2. Started from the running blur transition's value instead, the engine
            // would pad the contrast from 0 and be at 1.
            Assert.That(element.resolvedStyle.filter.ElementAt(1).GetParameter(0).floatValue, Is.EqualTo(1.5f).Within(1e-3f));
        }

        [Test]
        public void Given_NoBindingAndAWholePropertyTransition_When_AContrastIsRemoved_Then_ItFadesOutToOne()
        {
            // Arrange — a contrast faded in and at rest.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "all");
            ApplyContrast(element, 2f);
            AdvanceAndPaint(element.panel, 1.0);

            // Act
            ClearContrast(element);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — half way from 2 to CSS's identity 1; the engine's own padding would put it at 1.
            Assert.That(PaintedFloat(element), Is.EqualTo(1.5f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): a removed contrast leaves no filter once its fade ends, as it did before.
        [Test]
        public void Given_NoBindingAndAWholePropertyTransition_When_ARemovedContrastsFadeEnds_Then_NoFilterIsLeft()
        {
            // Arrange — a contrast faded in and at rest, then cleared.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "all");
            ApplyContrast(element, 2f);
            AdvanceAndPaint(element.panel, 1.0);
            ClearContrast(element);
            AdvanceAndPaint(element.panel, 0.15);

            // Act — the fade ends.
            AdvanceAndPaint(element.panel, 1.0);

            // Assert — the contrast(1) the fade ran toward is gone too.
            Assert.That(element.resolvedStyle.filter.Count(), Is.Zero);
        }

        [Test]
        public void Given_ATransitionAllElementAMotionDriverSuspended_When_AContrastIsCleared_Then_ThePaintIsInstant()
        {
            // Arrange — a contrast at rest on an element whose transitions a driver writing its opacity has suspended,
            // which leaves `none` for its transition-property and the class's duration standing.
            var element = MountResolved("w-[100px] h-[40px] transition-all duration-300");
            ApplyContrast(element, 2f);
            MotionNativeTransitionGuard.SuspendIfIntercepted(element, new object(), MotionTransitionSlots.Opacity);
            AdvanceAndPaint(element.panel, 1.0);

            // Act
            ClearContrast(element);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — nothing is painted, as UI Toolkit runs no transition under `none`.
            Assert.That(element.resolvedStyle.filter.Count(), Is.Zero);
        }

        // GREEN_ON_BASE(characterization): clearing the filter under a background-size entry alone paints at once.
        [Test]
        public void Given_NoBindingAndATransitionNamingBackgroundSize_When_AContrastIsCleared_Then_ThePaintIsInstant()
        {
            // Arrange — a contrast faded in and at rest. The entry for background-size animates a list write, but
            // not the clear.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "background-size");
            ApplyContrast(element, 2f);
            AdvanceAndPaint(element.panel, 1.0);

            // Act
            ClearContrast(element);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — nothing is painted, rather than a contrast fading toward 1.
            Assert.That(element.resolvedStyle.filter.Count(), Is.Zero);
        }

        [Test]
        public void Given_ADestroyedCustomBehindAContrast_When_TheContrastIsRemoved_Then_ItFadesOutToOne()
        {
            // Arrange — a contrast and a user custom composed at rest, then the custom's definition destroyed,
            // which drops it from the next compose.
            var element = MountResolved("w-[100px] h-[40px]");
            var definition = CreateUserDefinition(new FilterParameter(0f));
            ApplyContrast(element, 2f);
            StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(ArbitraryProperty.FilterCustom,
                new CustomFilterValue("fade-x", definition, new[] { new FilterParameter(1f) })));
            SetInlineTransition(element, new[] { "all" }, new[] { new TimeValue(0.3f) }, new[] { new TimeValue(0f) });
            Object.DestroyImmediate(definition);

            // Act — the compose leaves no filter at all.
            ClearContrast(element);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — half way from 2 to 1.
            Assert.That(PaintedFloat(element), Is.EqualTo(1.5f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): removing a blur leaves the inline filter cleared at once, as it did before.
        [Test]
        public void Given_NoBindingAndAWholePropertyTransition_When_ABlurIsRemoved_Then_TheInlineFilterIsClearedAtOnce()
        {
            // Arrange — a blur faded in and at rest.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "all");
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 1.0);

            // Act
            StyleArbitraryValueResolver.Clear(element, ArbitraryProperty.FilterBlur);

            // Assert — nothing was written in the blur's place for the fade.
            Assert.That(element.style.filter.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the engine shortens a reversed fade, which the padding must leave alone.
        [Test]
        public void Given_NoBindingAndAWholePropertyTransition_When_ARemovedBlurIsAddedBackMidFade_Then_TheReversalIsShortened()
        {
            // Arrange — a blur at rest, removed, and half way through fading out.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "all");
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 1.0);
            StyleArbitraryValueResolver.Clear(element, ArbitraryProperty.FilterBlur);
            AdvanceAndPaint(element.panel, 0.15);

            // Act — the blur comes back, and a quarter of the full duration passes.
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.075);

            // Assert — half way from 6 back to 12 over the half duration the engine gives a reversal. Restarted
            // over the full duration, it would be a quarter of the way, at 7.5.
            Assert.That(PaintedFloat(element), Is.EqualTo(9f).Within(0.5f));
        }

        // GREEN_ON_BASE(characterization): the engine shortens a reversed value change, which the padding must leave alone.
        [Test]
        public void Given_NoBindingAndAWholePropertyTransition_When_AChangedBlurIsRevertedMidFade_Then_TheReversalIsShortened()
        {
            // Arrange — a blur at 12 and at rest, changed to 4, and half way there. The inline filter holds the 4.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "all");
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 1.0);
            ApplyBlur(element, 4f);
            AdvanceAndPaint(element.panel, 0.15);

            // Act — back to 12, one list of the same length as the painted one, and a quarter of the full duration
            // passes.
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.075);

            // Assert — half way from 8 back to 12 over the half duration the engine gives a reversal. Restarted
            // over the full duration, it would be a quarter of the way, at 9.
            Assert.That(PaintedFloat(element), Is.EqualTo(10f).Within(0.3f));
        }

        // GREEN_ON_BASE(characterization): the engine applies a filter added before another at once, as CSS does, which
        // styling-filters.md states for both animators.
        [Test]
        public void Given_NoBindingAndAWholePropertyTransition_When_ABlurIsAddedBeforeAGrayscale_Then_ThePaintIsInstant()
        {
            // Arrange — a grayscale at rest.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "all");
            StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(ArbitraryProperty.FilterGrayscale, 1f, LengthUnit.Pixel));
            AdvanceAndPaint(element.panel, 1.0);

            // Act — the blur composes ahead of the grayscale.
            ApplyBlur(element, 12f);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — the blur is painted at its target.
            Assert.That(PaintedFloat(element), Is.EqualTo(12f));
        }

        [Test]
        public void Given_NoBindingAndAWholePropertyTransition_When_TheElementLeavesThePanelMidFade_Then_NoFilterIsLeft()
        {
            // Arrange — a contrast faded in and at rest, then cleared, and part way through the fade to 1.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "all");
            ApplyContrast(element, 2f);
            AdvanceAndPaint(element.panel, 1.0);
            ClearContrast(element);
            AdvanceAndPaint(element.panel, 0.15);

            // Act
            element.RemoveFromHierarchy();

            // Assert — the inline filter is cleared, as the fade's end would have left it, rather than holding the
            // contrast(1) the fade ran toward.
            Assert.That(element.style.filter.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_NoBindingAndAWholePropertyTransition_When_ABrightnessAndAContrastAreAddedInOneWrite_Then_TheContrastFadesInFromOne()
        {
            // Arrange — a blur at rest.
            MountWithInlineTransition("w-[100px] h-[40px]", out var element, "all");
            ApplyBlur(element, 4f);
            AdvanceAndPaint(element.panel, 1.0);
            var brightness = new FilterFunction(BuiltInFilterDefinitions.Brightness);
            brightness.AddParameter(new FilterParameter(1.5f));
            var to = new List<FilterFunction>(BlurList(4f)) { brightness };
            to.AddRange(ContrastList(2f));

            // Act — brightness and contrast join the list together, the contrast behind the brightness.
            StyleFilterEngineWrite.Write(element, to);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — half way from CSS's identity 1 to 2; the engine's own padding would put it at 1.
            Assert.That(element.resolvedStyle.filter.ElementAt(2).GetParameter(0).floatValue, Is.EqualTo(1.5f).Within(1e-3f));
        }

        [Test]
        public void Given_ATransitionNamingFilter_When_ABlurSlotIsClearedPastTheLayerMap_Then_TheTweenRunsIt()
        {
            // Arrange — a blur at rest on an element the resolver holds no filter layer for, under a list naming filter.
            var element = AddBareElement();
            element.style.filter = BlurList(12f);
            SetInlineTransition(element, new[] { "filter" }, new[] { new TimeValue(0.3f) }, new[] { new TimeValue(0f) });

            // Act
            StyleArbitraryValueResolver.ClearInline(element, ArbitraryProperty.FilterBlur);

            // Assert — the removal is the tween's, as a removal through the layer map is.
            Assert.That(DriverBinding(element)?.Scheduled, Is.Not.Null);
        }

        [Test]
        public void Given_AWholePropertyTransition_When_AContrastSlotIsClearedPastTheLayerMap_Then_ItFadesOutToOne()
        {
            // Arrange — a contrast at rest on an element the resolver holds no filter layer for.
            var element = AddBareElement();
            element.style.filter = ContrastList(2f);
            SetInlineTransition(element, new[] { "all" }, new[] { new TimeValue(0.3f) }, new[] { new TimeValue(0f) });

            // Act
            StyleArbitraryValueResolver.ClearInline(element, ArbitraryProperty.FilterContrast);
            AdvanceAndPaint(element.panel, 0.15);

            // Assert — half way from 2 to CSS's identity 1; the engine's own padding would put it at 1.
            Assert.That(PaintedFloat(element), Is.EqualTo(1.5f).Within(1e-3f));
        }

        #endregion

        #region Group F — on a mount's MotionClock

        private const double ClockFrameSec = HeldMotionClock.FrameSec;

        // A blur tween to 12 over a linear second on a mount whose clock holds still, its transition list given here.
        private VisualElement BlurTweenOnHeldClock(HeldMotionClock clock, params string[] transitionProperties)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card", className: "transition-filter"),
                new MountOptions { MotionClock = clock });
            var element = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(element.panel);
            SetInlineTransition(element, transitionProperties, new[] { new TimeValue(1f) }, new[] { new TimeValue(0f) });
            ApplyBlur(element, 12f);
            return element;
        }

        // Ticks the panel a 16 ms frame at a time, which the clock does not follow.
        private void PanelFrames(IPanel panel, int count)
        {
            for (var i = 0; i < count; i++)
            {
                _now += 0.016;
                EditorPanelTestHelpers.DriveSchedulerOnce(panel);
            }
        }

        private static float InlineBlur(VisualElement element) => element.style.filter.value[0].GetParameter(0).floatValue;

        [Test]
        public void Given_AFilterTweenOnAHeldClock_When_ThePanelTicksAndThenTheClockStepsAFrame_Then_ItMovesOnlyThatFrame()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = BlurTweenOnHeldClock(clock, "filter");

            // Act
            PanelFrames(element.panel, 10);
            var held = InlineBlur(element);
            clock.Now += ClockFrameSec;
            PanelFrames(element.panel, 1);

            // Assert
            Assert.That(new[] { held, InlineBlur(element) }, Is.EqualTo(new[] { 0f, (float)(12 * ClockFrameSec) }).Within(1e-5f));
        }

        // The list names background-size on filter's timing, which on the panel's time leaves the write to the engine.
        [Test]
        public void Given_AFilterChangeTheEngineWouldTimeOnAHeldClock_When_TheClockStepsAFrame_Then_TheTweenMovesItThatFrame()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = BlurTweenOnHeldClock(clock, "filter", "background-size");

            // Act
            PanelFrames(element.panel, 10);
            clock.Now += ClockFrameSec;
            PanelFrames(element.panel, 1);

            // Assert
            Assert.That(InlineBlur(element), Is.EqualTo((float)(12 * ClockFrameSec)).Within(1e-5f));
        }

        [Test]
        public void Given_AFilterTweenTheEngineWouldTimeOnAHeldClock_When_ItSettles_Then_NoEngineTransitionFollows()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = BlurTweenOnHeldClock(clock, "filter", "background-size");
            PanelFrames(element.panel, 1);
            clock.Now += 2.0;

            // Act — the tick settles, and the panel paints half a second on.
            PanelFrames(element.panel, 1);
            AdvanceAndPaint(element.panel, 0.5);

            // Assert — landed. Written for the engine to animate, it would be half way from the start frame's 0.
            Assert.That(PaintedFloat(element), Is.EqualTo(12f).Within(1e-3f));
        }

        // UI Toolkit shortens a reversed transition (the reversal cases above), and so does Velvet's tween.
        [Test]
        public void Given_AFilterTweenOnAHeldClockHalfWay_When_TheChangeIsUndone_Then_TheReversalTakesHalfTheDuration()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = BlurTweenOnHeldClock(clock, "filter");
            PanelFrames(element.panel, 1);
            clock.Now += 0.5;
            PanelFrames(element.panel, 1);

            // Act — the blur is taken away again, and an eighth of a second passes.
            StyleArbitraryValueResolver.Clear(element, ArbitraryProperty.FilterBlur);
            clock.Now += 0.125;
            PanelFrames(element.panel, 1);

            // Assert — a quarter of the way from 6 back to 0 over half a second; over the full second, 5.25.
            Assert.That(InlineBlur(element), Is.EqualTo(4.5f).Within(1e-3f));
        }

        // A write no transition runs ends the running tween, so the next change starts afresh even where it returns to
        // where that tween started.
        [Test]
        public void Given_AFilterTweenEndedByAnInstantWrite_When_ItsStartValueIsWrittenAgain_Then_TheChangeTakesTheFullDuration()
        {
            // Arrange — a blur of 4 written while nothing transitions filter, a tween to 12 half run, then a 2 written
            // while nothing transitions filter.
            var clock = new HeldMotionClock();
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card", className: "transition-filter"),
                new MountOptions { MotionClock = clock });
            var element = _window.rootVisualElement.Q<VisualElement>("card");
            var none = new[] { "opacity" };
            var filter = new[] { "filter" };
            var second = new[] { new TimeValue(1f) };
            var noDelay = new[] { new TimeValue(0f) };
            SetInlineTransition(element, none, second, noDelay);
            ApplyBlur(element, 4f);
            SetInlineTransition(element, filter, second, noDelay);
            ApplyBlur(element, 12f);
            clock.Now += 0.5;
            PanelFrames(element.panel, 1);
            SetInlineTransition(element, none, second, noDelay);
            ApplyBlur(element, 2f);
            SetInlineTransition(element, filter, second, noDelay);

            // Act
            ApplyBlur(element, 4f);
            clock.Now += 0.25;
            PanelFrames(element.panel, 1);

            // Assert — a quarter of the way from 2 to 4; as a reversal of the ended tween, half way.
            Assert.That(InlineBlur(element), Is.EqualTo(2.5f).Within(1e-3f));
        }

        // The first reversal shortens to half a second; its own half way back, the second weighs that half by the
        // first's factor: 0.5 × 0.5 + 1 − 0.5, three quarters of a second.
        [Test]
        public void Given_AReversedFilterTweenHalfWayBack_When_TheChangeIsMadeAgain_Then_ItReversesOverTheChainedFactor()
        {
            // Arrange — 0 to 12 half run, cleared, and half way back to 0, at 3.
            var clock = new HeldMotionClock();
            var element = BlurTweenOnHeldClock(clock, "filter");
            PanelFrames(element.panel, 1);
            clock.Now += 0.5;
            PanelFrames(element.panel, 1);
            StyleArbitraryValueResolver.Clear(element, ArbitraryProperty.FilterBlur);
            clock.Now += 0.25;
            PanelFrames(element.panel, 1);

            // Act — the blur comes back, and a quarter of three quarters of a second passes.
            ApplyBlur(element, 12f);
            clock.Now += 0.1875;
            PanelFrames(element.panel, 1);

            // Assert — a quarter of the way from 3 to 12; over the first reversal's half second, 6.375.
            Assert.That(InlineBlur(element), Is.EqualTo(5.25f).Within(1e-3f));
        }

        [Test]
        public void Given_AFilterTweenHalfWay_When_ADifferentValueIsWritten_Then_TheChangeTakesTheFullDuration()
        {
            // Arrange — 0 to 12 half run, at 6.
            var clock = new HeldMotionClock();
            var element = BlurTweenOnHeldClock(clock, "filter");
            PanelFrames(element.panel, 1);
            clock.Now += 0.5;
            PanelFrames(element.panel, 1);

            // Act — 4, which is not where the tween started, and a quarter of a second.
            ApplyBlur(element, 4f);
            clock.Now += 0.25;
            PanelFrames(element.panel, 1);

            // Assert — a quarter of the way from 6 to 4; shortened as a reversal, half way.
            Assert.That(InlineBlur(element), Is.EqualTo(5.5f).Within(1e-3f));
        }

        #endregion
    }
}
