using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The runtime layout proof for leading-* (line-height): a wrapped Label's MEASURED height depends on
    /// the native text generator actually running a multi-line layout pass keyed off the
    /// <c>&lt;line-height=X&gt;</c> tag — unlike the box-model reads other EditMode panel fixtures force via
    /// the panel's ApplyStyles/UpdateForRepaint reflection helpers (<c>FlexDefaultDirectionParityTests</c>,
    /// <c>ResponsiveBreakpointPanelTests</c>), no existing EditMode fixture in this codebase has ever
    /// exercised that measurement (the whitespace-* resolved-style fixtures only assert the
    /// <c>WhiteSpace</c> enum, never a measured height), so there is no precedent that the reflection trick
    /// actually drives text shaping that far. Rather than gamble on that, this proves it on a REAL ticking
    /// runtime panel instead, where a UIDocument's PlayerLoop-driven layout pass is exactly what a shipped
    /// game already relies on. A weaker EditMode string-level pin (the tag text itself, not a measured
    /// height) lives alongside the other three axes in <c>StyleTextEffectClassTests</c> /
    /// <c>StyleTextEffectPanelTests</c>.
    /// <para/>
    /// Forces a width narrow enough to wrap the SAME long sentence into several lines at the SAME font size
    /// for both labels: line-height changes the vertical advance BETWEEN lines, not a glyph's own
    /// horizontal advance, so the wrap boundaries — and therefore the line count — are identical for both;
    /// only the resulting total height differs. leading-none (1) and leading-loose (2) are used for maximum
    /// contrast against measurement/rounding noise, and a short, unconstrained reference label (guaranteed
    /// single-line) is used as an Assume precondition that the two long labels actually wrapped at all.
    /// </summary>
    internal sealed class LeadingLineHeightPlaybackTests
    {
        private const string WrappingText =
            "Velvet renders declarative user interfaces for Unity UI Toolkit using familiar React style " +
            "components and hooks throughout the entire framework";

        // A themeless RenderTexture panel provides neither a font (empty runtime theme — every label
        // measures 0 tall without one) nor the wrap behavior (a Label's own default style is nowrap, and
        // no stylesheet is loaded here since leading-*/w-[...]/text-[...] are all inline-resolved), so
        // both are supplied inline through the ref — the one channel that reaches a bare panel.
        private static readonly System.Func<VisualElement, System.Action> s_enableTextMeasurement = el =>
        {
            el.style.whiteSpace = WhiteSpace.Normal;
            el.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(
                UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf")));
            return null;
        };

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Given_TwoWrappedLabelsDifferingOnlyInLeading_When_ARealPanelTicks_Then_TheLooserLeadingMeasuresTaller()
        {
            // Arrange — identical text, font size, and width; only leading-* differs between "tight" and
            // "loose". "reference" carries the same font size and leading as "tight" but no width
            // constraint, so it stays a single line — the precondition baseline proving the other two
            // actually wrapped across multiple lines instead of measuring one line each (which would make
            // the main assertion below true only by accident, not by the mechanism under test).
            _host = new RenderTexturePanelHost("LeadingPlayback", 300, 1000);
            _mounted = V.Mount(_host.Root, V.Div(children: new VNode[]
            {
                V.Label(name: "tight", className: "leading-none text-[16px] w-[140px]", text: WrappingText,
                    refCallback: s_enableTextMeasurement),
                V.Label(name: "loose", className: "leading-loose text-[16px] w-[140px]", text: WrappingText,
                    refCallback: s_enableTextMeasurement),
                V.Label(name: "reference", className: "leading-none text-[16px]", text: "x",
                    refCallback: s_enableTextMeasurement),
            }));
            var tight = _host.Root.Q<Label>("tight");
            var loose = _host.Root.Q<Label>("loose");
            var reference = _host.Root.Q<Label>("reference");
            Assume.That((tight != null, loose != null, reference != null), Is.EqualTo((true, true, true)),
                "Precondition: all three labels mounted");

            // Act — let the panel actually tick layout across several real frames (generous margin: a
            // first-time text-shaping pass — font/glyph-atlas population plus the actual line-wrap — is
            // heavier than the single-property resolves other playback fixtures settle in 1-2 frames).
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            yield return null;

            // The three heights stay in the message (unlike a plain static precondition): a wrap failure
            // here is otherwise opaque — no way to tell a font-less 0-height measurement from a genuine
            // single-line non-wrap without seeing all three numbers side by side.
            Assume.That(
                (tight.resolvedStyle.height > reference.resolvedStyle.height,
                    loose.resolvedStyle.height > reference.resolvedStyle.height),
                Is.EqualTo((true, true)),
                $"Precondition: both constrained labels wrapped (tight h={tight.resolvedStyle.height}, "
                + $"loose h={loose.resolvedStyle.height}, reference h={reference.resolvedStyle.height})");

            // Assert
            Assert.That(loose.resolvedStyle.height, Is.GreaterThan(tight.resolvedStyle.height));
        }
    }
}
