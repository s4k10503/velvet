using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="StyleTextBalanceManipulator"/>'s actual measure-and-narrow behavior against a real
    /// runtime panel — <c>TextBalanceParityTests</c> (EditMode) only pins the classifier + attach/detach
    /// wiring, since EditMode never resolves layout and the manipulator's own <c>Apply</c> defers without
    /// one.
    /// </summary>
    /// <remarks>
    /// A themeless <see cref="RenderTexturePanelHost"/> panel has no font — every Label measures zero
    /// tall — and a Label's engine default is nowrap, so every label here supplies both inline via
    /// refCallback (a built-in Font plus <c>WhiteSpace.Normal</c>) rather than relying on a stylesheet
    /// that was never loaded onto this panel. No stylesheet is needed for the `w-[Npx]` wrapper width or
    /// the `text-balance` class either: both are arbitrary-value / classifier tokens Velvet resolves in
    /// C#, independent of any USS asset (mirrors <c>BuiltInFilterShaderPlaybackTests</c>' own
    /// stylesheet-less `w-[100px]` usage). The one exception is the `max-w-32` case, which exists only as
    /// a USS rule and so asks its mount helper for the bundled stylesheet.
    /// </remarks>
    internal sealed class TextBalancePlaybackTests
    {
        private RenderTexturePanelHost _host;
        private MountedTree _mounted;

        // Stable delegate identity (a static readonly field, not a per-render closure), mirroring
        // StyleTextEffectPanelTests.s_manualWhiteSpaceRef: both labels in a pair share the identical font
        // and wrap setting, so the only difference between them is the text-balance class itself.
        private static readonly Func<VisualElement, Action> s_wrapWithFontRef = element =>
        {
            element.style.whiteSpace = WhiteSpace.Normal;
            element.style.unityFontDefinition = new StyleFontDefinition(
                FontDefinition.FromFont(UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf")));
            return null;
        };

        // The wrapper's direction, supplied inline for the same reason the font is: no stylesheet is
        // loaded, so `flex-row` would be an inert class name and the engine's own default is a column.
        private static readonly Func<VisualElement, Action> s_rowRef = element =>
        {
            element.style.flexDirection = FlexDirection.Row;
            return null;
        };

        // Many short words so the wrap point has real work to do at any wrapper width used below (100px
        // up to ~350px) — reused by the state-driven tests, which each need long-wrapping text at a
        // width chosen after mount rather than at construction time like MountPair's own literal.
        private const string LongWrapText = "This label carries enough short plain words to wrap across many lines " +
            "inside a narrow box so the balance search over its width has real work to do and a " +
            "clearly uneven last line to fix";

        // Wraps to several lines at the bound below, yet fits comfortably on ONE line at the wrapper's own
        // width — the case where the two widths disagree about whether the text wraps at all.
        private const string MediumWrapText = "A heading that fits one line here";

        // A wrapper far wider than anything the sizing utilities below ask for, so a label that ignored one
        // resolves visibly wider instead of landing on it by coincidence.
        private const int WrapperWidthPx = 380;

        // The arbitrary (inline-resolved) bound and its class, spelled once.
        private const int CoPresentMaxWidthPx = 120;
        private const string CoPresentMaxWidthClass = "max-w-[120px]";

        // The USS scale bound: `.max-w-32 { max-width: var(--space-32); }` with `--space-32: 128px`. Only
        // reachable through the bundled stylesheet, and only through the element's RESOLVED style — there
        // is no arbitrary-value layer behind a scale class.
        private const int ScaleMaxWidthPx = 128;
        private const string ScaleMaxWidthClass = "max-w-32";

        private static StateUpdater<bool> s_setWrapperWide;
        private static StateUpdater<string> s_setSwapText;
        private static StateUpdater<bool> s_setAddCeiling;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            s_setWrapperWide = default;
            s_setSwapText = default;
            s_setAddCeiling = default;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            VelvetTheme.IsDark = false;
            yield return null;
        }

        // Mounts an unbalanced/balanced sibling pair with identical text inside identically-sized wrapper
        // containers (the parent width StyleTextBalanceManipulator measures against), then waits for
        // layout to settle.
        private IEnumerator MountPair(string text, int wrapperWidthPx)
        {
            _host = new RenderTexturePanelHost("TextBalancePanel", 400, 400);
            var tree = V.Div(children: new VNode[]
            {
                V.Div(className: $"w-[{wrapperWidthPx}px]", children: new VNode[]
                {
                    V.Label(name: "unbalanced", text: text, refCallback: s_wrapWithFontRef),
                }),
                V.Div(className: $"w-[{wrapperWidthPx}px]", children: new VNode[]
                {
                    V.Label(name: "balanced", text: text, className: "text-balance", refCallback: s_wrapWithFontRef),
                }),
            });
            _mounted = V.Mount(_host.Root, tree);
            yield return WaitRealtime(0.5);
        }

        // Mounts one balanced label carrying an extra sizing utility — a ceiling or a declared width —
        // inside a wrapper wider than anything that utility asks for, then waits for layout to settle.
        // loadUtilities attaches the bundled stylesheet, which the USS scale forms need and the
        // arbitrary-value forms resolve without.
        private IEnumerator MountWithSizingClass(string panelName, string text, string boundClass, bool loadUtilities = false)
        {
            _host = new RenderTexturePanelHost(panelName, 400, 400);
            if (loadUtilities)
            {
                VelvetStyleUtilities.AttachTo(_host.Root);
            }
            var tree = V.Div(className: $"w-[{WrapperWidthPx}px]", children: new VNode[]
            {
                V.Label(name: "balanced", text: text,
                    className: $"text-balance {boundClass}", refCallback: s_wrapWithFontRef),
            });
            _mounted = V.Mount(_host.Root, tree);
            yield return WaitRealtime(0.5);
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabelBesideAnArbitraryMaxWidth_When_ItsTextWraps_Then_TheBalancedBoxStaysInsideThatCeiling()
        {
            // Arrange / Act — text long enough to wrap at either width, so balance genuinely runs and
            // writes a width of its own rather than releasing the slot.
            yield return MountWithSizingClass("TextBalanceArbitraryCeilingPanel", LongWrapText, CoPresentMaxWidthClass);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text wrapped, so balance wrote a real (not released) width");

            // Assert — a declared max-width is a ceiling the balanced width must fit inside, the way CSS
            // balances lines INSIDE the box max-width already allows; deriving it from the parent's width
            // alone overshoots the ceiling outright.
            Assert.That(balanced.resolvedStyle.width, Is.LessThanOrEqualTo(CoPresentMaxWidthPx + 0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabelBesideAScaleMaxWidth_When_ItsTextWraps_Then_TheBalancedBoxStaysInsideThatCeiling()
        {
            // Arrange / Act — the same ceiling contract for a max-width that exists ONLY as a USS class:
            // it registers no arbitrary-value layer, so the ceiling has to come from the element's own
            // resolved style instead.
            yield return MountWithSizingClass("TextBalanceScaleCeilingPanel", LongWrapText, ScaleMaxWidthClass, loadUtilities: true);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text wrapped, so balance wrote a real (not released) width");

            // Assert — the two spellings of a ceiling behave identically; a bracket-only clamp would be
            // harder to predict than uniformly ignoring the ceiling.
            Assert.That(balanced.resolvedStyle.width, Is.LessThanOrEqualTo(ScaleMaxWidthPx + 0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabelWhoseTextFitsTheParentButWrapsInsideItsCeiling_When_ItSettles_Then_BalanceRunsAnyway()
        {
            // Arrange — one wrapper, two labels with the same text: a probe carrying neither balance nor a
            // ceiling, which shows that the text does fit one line at the wrapper's own width, and the
            // subject, whose ceiling is narrow enough that the same text wraps inside it.
            _host = new RenderTexturePanelHost("TextBalanceCeilingGatePanel", 400, 400);
            var tree = V.Div(className: $"w-[{WrapperWidthPx}px]", children: new VNode[]
            {
                V.Label(name: "probe", text: MediumWrapText, refCallback: s_wrapWithFontRef),
                V.Label(name: "balanced", text: MediumWrapText,
                    className: $"text-balance {CoPresentMaxWidthClass}", refCallback: s_wrapWithFontRef),
            });
            _mounted = V.Mount(_host.Root, tree);
            yield return WaitRealtime(0.5);
            var probe = _host.Root.Q<Label>("probe");
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(probe.resolvedStyle.height, Is.LessThan(probe.resolvedStyle.fontSize * 1.8f),
                "Precondition: the text fits one line at the wrapper's own width, so only the ceiling makes it wrap");

            // Assert — the single-line gate measures at the ceiling-clamped width, so this text counts as
            // wrapping and gets balanced. Measuring at the parent's width instead would dismiss it as
            // single-line and leave the box sitting at EXACTLY the ceiling, so the bound only has to clear
            // that value: how far under it a balanced width lands is font metrics, which differ per
            // platform, and must not be part of the assertion.
            Assert.That(balanced.resolvedStyle.width, Is.LessThan(CoPresentMaxWidthPx - 0.5f));
        }

        [UnityTest]
        public IEnumerator Given_AWrappedMultiLineLabel_When_Balanced_Then_ItIsNarrowerThanItsUnbalancedSiblingAtTheSameHeight()
        {
            // Arrange — many short words in a narrow wrapper reliably wrap into several densely-packed
            // lines regardless of the panel's (unthemed) default font size.
            const string text = LongWrapText;

            // Act
            yield return MountPair(text, 100);
            var unbalanced = _host.Root.Q<Label>("unbalanced");
            var balanced = _host.Root.Q<Label>("balanced");

            // Self-referential rather than a hardcoded pixel guess: any real multi-line wrap totals well
            // over 1.5x a single font-size unit, regardless of what the panel's actual default size is.
            Assume.That(unbalanced.resolvedStyle.height, Is.GreaterThan(unbalanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the long text actually wrapped onto multiple lines in the unbalanced sibling");

            // Assert — the core balance property, as one tuple: a narrower box (balance did something) at
            // the SAME line count (it did not also wrap an extra line to get there — an extra-wrapped-line
            // regression must fail this, not just Inconclusive out as a precondition). Resolved layout
            // floats can carry sub-pixel rounding noise even for identical text/font on two elements, so
            // the height half uses the same 0.5px tolerance this fixture's other height/width comparisons
            // already accept.
            Assert.That(
                (balanced.resolvedStyle.width < unbalanced.resolvedStyle.width,
                 Mathf.Abs(balanced.resolvedStyle.height - unbalanced.resolvedStyle.height) < 0.5f),
                Is.EqualTo((true, true)));
        }

        [UnityTest]
        public IEnumerator Given_ASingleLineLabel_When_Balanced_Then_ItKeepsItsFullUnbalancedBoxWidth()
        {
            // Arrange — short text in a wide wrapper stays on a single line regardless of the panel's
            // default font size.
            const string text = "Hi";

            // Act
            yield return MountPair(text, 480);
            var unbalanced = _host.Root.Q<Label>("unbalanced");
            var balanced = _host.Root.Q<Label>("balanced");

            Assume.That(unbalanced.resolvedStyle.height, Is.LessThan(unbalanced.resolvedStyle.fontSize * 1.8f),
                "Precondition: the short text stayed on a single line in the unbalanced sibling");

            // Assert — the multi-line-only gate: a single-line label's box is left untouched.
            Assert.That(balanced.resolvedStyle.width, Is.EqualTo(unbalanced.resolvedStyle.width).Within(0.5f));
        }

        // The wrapper (the balanced label's PARENT) toggles between a narrow and a wide width class —
        // state-driven so the SAME element widens after mount instead of two separately-mounted trees.
        [Component]
        private static VNode WidenHost()
        {
            var (wide, setWide) = Hooks.UseState(false);
            s_setWrapperWide = setWide;
            return V.Div(className: wide ? "w-[350px]" : "w-[100px]", children: new VNode[]
            {
                V.Label(name: "balanced", text: LongWrapText, className: "text-balance", refCallback: s_wrapWithFontRef),
            });
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabel_When_ItsWrapperWidensAfterMount_Then_ItRebalancesToAWiderBox()
        {
            // Arrange
            _host = new RenderTexturePanelHost("TextBalanceWidenPanel", 400, 400);
            _mounted = V.Mount(_host.Root, V.Component(WidenHost, key: "root"));
            yield return WaitRealtime(0.5);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced, Is.Not.Null, "Precondition: the label mounted");
            var narrowWidth = balanced.resolvedStyle.width;
            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text wrapped multi-line at the narrow wrapper, so balance wrote a real narrower width");

            // Act — widen the wrapper, an ANCESTOR of the label, not the label itself.
            s_setWrapperWide.Invoke(true);
            yield return WaitRealtime(0.6);

            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text still wraps multi-line at the wider wrapper, so this is a real re-balance and not the single-line clear path");

            // Assert — the manipulator's own width write pins the target's size, so only a listener on
            // the PARENT's own GeometryChangedEvent (not just the target's) can catch an ancestor widening
            // and re-run the search; without it the label stays stuck at the narrow value forever.
            Assert.That(balanced.resolvedStyle.width, Is.GreaterThan(narrowWidth));
        }

        // The label's own text toggles between a short single-line string and LongWrapText, at a fixed
        // wrapper width wide enough that a balanced value and the released box are far apart — state-driven
        // so the CHANGE happens after mount via TextElement's ChangeEvent<string>.
        [Component]
        private static VNode TextSwapHost()
        {
            var (text, setText) = Hooks.UseState("Hi");
            s_setSwapText = setText;
            return V.Div(className: $"w-[{WrapperWidthPx}px]", children: new VNode[]
            {
                V.Label(name: "balanced", text: text, className: "text-balance", refCallback: s_wrapWithFontRef),
            });
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabel_When_ItsTextChangesToAWrappingStringAfterMount_Then_TheInlineWidthAppears()
        {
            // Arrange — mounts short enough to sit on one line, which the multi-line gate leaves entirely
            // unconstrained: no inline width exists yet, giving the swap below a clean edge to observe.
            _host = new RenderTexturePanelHost("TextBalanceTextSwapPanel", 400, 400);
            _mounted = V.Mount(_host.Root, V.Component(TextSwapHost, key: "root"));
            yield return WaitRealtime(0.5);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced, Is.Not.Null, "Precondition: the label mounted");
            Assume.That(balanced.resolvedStyle.height, Is.LessThan(balanced.resolvedStyle.fontSize * 1.8f),
                "Precondition: the short initial text stayed on a single line");
            Assume.That(balanced.style.width.keyword, Is.EqualTo(StyleKeyword.Null),
                "Precondition: a single-line label carries no balanced constraint to begin with");

            // Act — swap in text long enough to wrap at the SAME (unchanged) wrapper width.
            s_setSwapText.Invoke(LongWrapText);
            yield return WaitRealtime(0.6);

            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the new text actually wrapped onto multiple lines");

            // Assert — a text change re-triggers the balance computation THROUGH SOME PATH: the multi-line
            // gate flips from "leave unconstrained" to "write the searched bound", so an inline width
            // appearing at all proves the swap re-ran the search. This swap changes the label's resolved
            // height, so the geometry listener alone would also re-run it — the text-change notification
            // is NOT isolated here (isolating it needs a same-height swap, whose construction depends on
            // font metrics too fragile to pin). The magnitude of the resulting width is deliberately not
            // asserted either — how densely text packs at the wrapper's width is font-metric trivia, not
            // the re-trigger contract under test.
            Assert.That(balanced.style.width.keyword, Is.Not.EqualTo(StyleKeyword.Null));
        }

        // The label's classNames gains a USS ceiling class AFTER mount — state-driven so the ceiling
        // arrives in a later patch than the one that first established the balanced width, which is the
        // case a ceiling read taken once at the first derive can never see.
        [Component]
        private static VNode LateCeilingHost()
        {
            var (addCeiling, setAddCeiling) = Hooks.UseState(false);
            s_setAddCeiling = setAddCeiling;
            var cls = addCeiling ? $"text-balance {ScaleMaxWidthClass}" : "text-balance";
            return V.Div(className: $"w-[{WrapperWidthPx}px]", children: new VNode[]
            {
                V.Label(name: "balanced", text: LongWrapText, className: cls, refCallback: s_wrapWithFontRef),
            });
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabelAlreadyHoldingAWidth_When_ACeilingArrivesInALaterPatch_Then_TheBoxMovesInsideIt()
        {
            // Arrange — mounts with text-balance alone, so a balanced width is established and held before
            // any ceiling exists at all.
            _host = new RenderTexturePanelHost("TextBalanceLateCeilingPanel", 400, 400);
            VelvetStyleUtilities.AttachTo(_host.Root);
            _mounted = V.Mount(_host.Root, V.Component(LateCeilingHost, key: "root"));
            yield return WaitRealtime(0.5);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.width, Is.GreaterThan(ScaleMaxWidthPx),
                "Precondition: balance is holding a width wider than the ceiling that is about to arrive");

            // Act — the ceiling class enters the class list while that width is held.
            s_setAddCeiling.Invoke(true);
            yield return WaitRealtime(0.6);

            // Assert — a ceiling is read fresh on every pass, so one that appears mid-life binds like one
            // present at mount. Reading it once and caching cannot see this, since a label whose text
            // always wraps never releases the slot that would trigger a re-read.
            Assert.That(balanced.resolvedStyle.width, Is.LessThanOrEqualTo(ScaleMaxWidthPx + 0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabelAlreadyHoldingAWidth_When_AVariantSwapsInANarrowerCeiling_Then_TheBoxMovesInsideIt()
        {
            // Arrange — a USS base ceiling, and a dark: payload that narrows it. The theme flip changes
            // the ceiling while the balanced width is held, with no re-render at all. The payload is an
            // arbitrary value rather than another scale class because two USS classes resolve against each
            // other by stylesheet source order, which no variant can reorder.
            _host = new RenderTexturePanelHost("TextBalanceVariantCeilingPanel", 400, 400);
            VelvetStyleUtilities.AttachTo(_host.Root);
            var tree = V.Div(className: $"w-[{WrapperWidthPx}px]", children: new VNode[]
            {
                V.Label(name: "balanced", text: LongWrapText,
                    className: $"text-balance {ScaleMaxWidthClass} dark:max-w-[80px]", refCallback: s_wrapWithFontRef),
            });
            _mounted = V.Mount(_host.Root, tree);
            yield return WaitRealtime(0.5);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.width, Is.GreaterThan(80.5f),
                "Precondition: the base ceiling is the one binding, so balance holds a width above the narrower one");

            // Act
            VelvetTheme.IsDark = true;
            yield return WaitRealtime(0.6);

            // Assert
            Assert.That(balanced.resolvedStyle.width, Is.LessThanOrEqualTo(80.5f));
        }

        [UnityTest]
        public IEnumerator Given_AVariantCeilingOverABaseOne_When_TheVariantIsRemoved_Then_TheBaseCeilingStillBinds()
        {
            // Arrange — the dark: layer supplies the narrower ceiling over a base USS one. Removing it must
            // fall back to the base rather than to "no ceiling at all", which is the widening direction of
            // the same currency contract the two tests above cover in the narrowing one.
            _host = new RenderTexturePanelHost("TextBalanceCeilingFallbackPanel", 400, 400);
            VelvetStyleUtilities.AttachTo(_host.Root);
            VelvetTheme.IsDark = true;
            var tree = V.Div(className: $"w-[{WrapperWidthPx}px]", children: new VNode[]
            {
                V.Label(name: "balanced", text: LongWrapText,
                    className: $"text-balance {ScaleMaxWidthClass} dark:max-w-[80px]", refCallback: s_wrapWithFontRef),
            });
            _mounted = V.Mount(_host.Root, tree);
            yield return WaitRealtime(0.5);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.width, Is.LessThanOrEqualTo(80.5f),
                "Precondition: the dark layer's narrower ceiling is the one binding to begin with");

            // Act
            VelvetTheme.IsDark = false;
            yield return WaitRealtime(0.6);

            // Assert
            Assert.That(balanced.resolvedStyle.width, Is.LessThanOrEqualTo(ScaleMaxWidthPx + 0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabelHoldingAWidth_When_ItsTextShrinksToOneLine_Then_TheBoxIsHandedBack()
        {
            // Arrange — the relocated release contract: balance borrows the WIDTH slot now, so a label that
            // stops needing a balanced value must hand that slot back or stay pinned at it forever.
            _host = new RenderTexturePanelHost("TextBalanceReleaseWidthPanel", 400, 400);
            _mounted = V.Mount(_host.Root, V.Component(TextSwapHost, key: "root"));
            yield return WaitRealtime(0.5);
            var balanced = _host.Root.Q<Label>("balanced");
            s_setSwapText.Invoke(LongWrapText);
            yield return WaitRealtime(0.6);
            var wrapperWidth = balanced.parent.resolvedStyle.width;
            Assume.That(balanced.resolvedStyle.width, Is.LessThan(wrapperWidth - 0.5f),
                "Precondition: balance is holding a width narrower than the wrapper");

            // Act — back to text that fits one line, which is a gate balance declines to act on.
            s_setSwapText.Invoke("Hi");
            yield return WaitRealtime(0.6);

            // Assert — the box returns to what the cascade gives it, rather than staying at the width
            // balance wrote while it still had a reason to.
            Assert.That(balanced.resolvedStyle.width, Is.EqualTo(wrapperWidth).Within(0.5f));
        }

        // Two cells of a grid, one balanced. `grid-cols-2` carries no bare `grid` token, so this also
        // pins that the stand-down follows the grid manipulator rather than the marker class.
        private IEnumerator MountGridCells(string containerClass)
        {
            _host = new RenderTexturePanelHost("TextBalanceGridChildPanel", 400, 400);
            var tree = V.Div(className: containerClass, children: new VNode[]
            {
                V.Label(name: "plain", text: LongWrapText, refCallback: s_wrapWithFontRef),
                V.Label(name: "balanced", text: "Hi", className: "text-balance", refCallback: s_wrapWithFontRef),
            });
            _mounted = V.Mount(_host.Root, tree);
            yield return WaitRealtime(0.5);
        }

        [UnityTest]
        public IEnumerator Given_ALabelInsideAGridContainer_When_ItsTextIsSetDirectly_Then_TheGridsColumnWidthSurvives()
        {
            // Arrange
            yield return MountGridCells($"grid-cols-2 w-[{WrapperWidthPx}px]");
            var plain = _host.Root.Q<Label>("plain");
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(plain.resolvedStyle.width, Is.LessThan(WrapperWidthPx),
                "Precondition: the grid sized its children into columns narrower than the container");

            // Act — assigned straight onto the element, which is the trigger that reaches balance without
            // reaching the grid: TextElement raises ChangeEvent<string> synchronously, while the grid's own
            // re-derive is gated on a container width that a child re-wrapping never moves.
            balanced.text = LongWrapText;
            yield return WaitRealtime(0.6);

            // Assert — the grid owns this slot, so balance stands down instead of taking the column.
            Assert.That(balanced.resolvedStyle.width, Is.EqualTo(plain.resolvedStyle.width).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabelInsideACompositeWidget_When_Balanced_Then_ItMeasuresAgainstTheInnerBox()
        {
            // Arrange — a ScrollView redirects its children into an inner box, so the label's parent is
            // that box and not the widget. Both the measurement and the parent subscription use it.
            _host = new RenderTexturePanelHost("TextBalanceCompositeWidgetPanel", 400, 400);
            var tree = V.ScrollView(className: $"w-[{WrapperWidthPx}px] h-[200px]", children: new VNode[]
            {
                V.Label(name: "balanced", text: LongWrapText, className: "text-balance", refCallback: s_wrapWithFontRef),
            });
            _mounted = V.Mount(_host.Root, tree);
            yield return WaitRealtime(0.5);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text wrapped inside the widget's inner box");

            // Assert — a box narrower than the inner box it measured against, which only happens if the
            // available width resolved through the redirect instead of coming out as nothing.
            Assert.That(balanced.resolvedStyle.width,
                Is.LessThan(balanced.parent.resolvedStyle.width - 0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ALabelInsideAScrollViewGrid_When_ItsTextIsSetDirectly_Then_TheGridsColumnWidthSurvives()
        {
            // Arrange — a ScrollView grid sizes the children of its contentContainer, so the manipulator is
            // keyed on an element that is NOT the children's parent.
            _host = new RenderTexturePanelHost("TextBalanceScrollGridPanel", 400, 400);
            var tree = V.ScrollView(className: $"grid-cols-2 w-[{WrapperWidthPx}px] h-[200px]", children: new VNode[]
            {
                V.Label(name: "plain", text: LongWrapText, refCallback: s_wrapWithFontRef),
                V.Label(name: "balanced", text: "Hi", className: "text-balance", refCallback: s_wrapWithFontRef),
            });
            _mounted = V.Mount(_host.Root, tree);
            yield return WaitRealtime(0.5);
            var plain = _host.Root.Q<Label>("plain");
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(plain.resolvedStyle.width, Is.LessThan(WrapperWidthPx),
                "Precondition: the grid sized the contentContainer's children into columns");

            // Act
            balanced.text = LongWrapText;
            yield return WaitRealtime(0.6);

            // Assert
            Assert.That(balanced.resolvedStyle.width, Is.EqualTo(plain.resolvedStyle.width).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabelHoldingAWidth_When_ItsTextBecomesEmpty_Then_TheBoxIsHandedBack()
        {
            // Arrange — the empty-text gate has its own release branch, reached before any measurement.
            _host = new RenderTexturePanelHost("TextBalanceReleaseEmptyPanel", 400, 400);
            _mounted = V.Mount(_host.Root, V.Component(TextSwapHost, key: "root"));
            yield return WaitRealtime(0.5);
            var balanced = _host.Root.Q<Label>("balanced");
            s_setSwapText.Invoke(LongWrapText);
            yield return WaitRealtime(0.6);
            var wrapperWidth = balanced.parent.resolvedStyle.width;
            Assume.That(balanced.resolvedStyle.width, Is.LessThan(wrapperWidth - 0.5f),
                "Precondition: balance is holding a width narrower than the wrapper");

            // Act
            s_setSwapText.Invoke(string.Empty);
            yield return WaitRealtime(0.6);

            // Assert
            Assert.That(balanced.resolvedStyle.width, Is.EqualTo(wrapperWidth).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabelHoldingAWidth_When_AVariantWidthIsToggledOn_Then_TheDeclaredWidthTakesOver()
        {
            // Arrange — balance settles on a width of its own BEFORE the declaration exists. A USS-spelled
            // variant width then loses to that inline value, so it moves no resolved width and raises no
            // geometry event: only the layout re-sync its gate token triggers can deliver it.
            yield return MountWithSizingClass("TextBalanceLateVariantWidthPanel", LongWrapText, "dark:w-40", loadUtilities: true);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.width, Is.GreaterThan(200f),
                "Precondition: balance is holding a width well above the one the variant will declare");

            // Act
            VelvetTheme.IsDark = true;
            yield return WaitRealtime(0.6);

            // Assert — `.w-40 { width: var(--space-40); }` with `--space-40: 160px`.
            Assert.That(balanced.resolvedStyle.width, Is.EqualTo(160f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabel_When_AnInlineResolvedVariantWidthIsToggledOnAndOff_Then_ItBalancesAgain()
        {
            // Arrange — a bracket variant width takes the inline-resolved branch, which fires no layout
            // re-sync at all: only the layer probe sees either edge of it.
            yield return MountWithSizingClass("TextBalanceVariantLayerEdgePanel", LongWrapText, "dark:w-[200px]");
            var balanced = _host.Root.Q<Label>("balanced");
            VelvetTheme.IsDark = true;
            yield return WaitRealtime(0.6);
            Assume.That(balanced.resolvedStyle.width, Is.EqualTo(200f).Within(0.5f),
                "Precondition: the variant's width took the box over while it was active");

            // Act — the OFF edge removes the layer, which nothing else reports.
            VelvetTheme.IsDark = false;
            yield return WaitRealtime(0.6);

            // Assert — back to a balanced box rather than the full wrapper it would stretch to unbalanced.
            Assert.That(balanced.resolvedStyle.width, Is.LessThan(WrapperWidthPx - 0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ALabelDeclaringAnArbitraryWidth_When_Balanced_Then_TheDeclaredWidthIsLeftAlone()
        {
            // Arrange / Act — a browser balances INSIDE a fixed box; this approximation can only resize the
            // box, so on an element the author has sized there is no honest approximation left and balance
            // stands down rather than overruling the declaration.
            yield return MountWithSizingClass("TextBalanceDeclaredWidthPanel", LongWrapText, "w-[200px]");
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text wraps at the declared width, so balance had a real candidate to write");

            // Assert
            Assert.That(balanced.resolvedStyle.width, Is.EqualTo(200f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ALabelDeclaringAVariantWidth_When_ThatVariantIsActive_Then_TheDeclaredWidthIsLeftAlone()
        {
            // Arrange — a variant-prefixed token never starts with `w-`, so only the payload it writes onto
            // the live class list identifies it as a width declaration.
            VelvetTheme.IsDark = true;

            // Act
            yield return MountWithSizingClass("TextBalanceVariantWidthPanel", LongWrapText, "dark:w-40", loadUtilities: true);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text wraps at the declared width, so balance had a real candidate to write");

            // Assert — `.w-40 { width: var(--space-40); }` with `--space-40: 160px`.
            Assert.That(balanced.resolvedStyle.width, Is.EqualTo(160f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ALabelDeclaringAnImportantWidth_When_Balanced_Then_TheDeclaredWidthIsLeftAlone()
        {
            // Arrange / Act — the bang form registers its layer at a higher priority and never reaches the
            // class list under either spelling.
            yield return MountWithSizingClass("TextBalanceImportantWidthPanel", LongWrapText, "!w-[200px]");
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text wraps at the declared width, so balance had a real candidate to write");

            // Assert
            Assert.That(balanced.resolvedStyle.width, Is.EqualTo(200f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator Given_AVariantTrackedLabelDeclaringABracketWidth_When_Balanced_Then_TheDeclaredWidthIsLeftAlone()
        {
            // Arrange / Act — the `dark:gap-4` puts the element on the variant-gated path, where the layout
            // families read the LIVE class list; a bracket width resolves to inline style and never appears
            // there, so only the recorded layer still sees it.
            VelvetTheme.IsDark = true;
            yield return MountWithSizingClass("TextBalanceTrackedBracketWidthPanel", LongWrapText, "w-[200px] dark:gap-4");
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text wraps at the declared width, so balance had a real candidate to write");

            // Assert
            Assert.That(balanced.resolvedStyle.width, Is.EqualTo(200f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabelSharingARowWithASibling_When_Balanced_Then_TheSiblingGivesUpSomeWidth()
        {
            // Arrange — the over-estimate the class doc calls harmless is not, in a row: the search
            // measures against the whole row, so the balanced box plus its sibling exceed it. Measured to
            // be the same under either write slot, because Yoga folds a child's max-width into the MEASURE
            // constraint before computing a flex basis, and wrapping text measured under that cap returns
            // the cap itself — the same basis a width sets outright. So this pins a consequence of
            // measuring against the parent, not of the slot.
            _host = new RenderTexturePanelHost("TextBalanceRowSiblingPanel", 400, 400);
            var tree = V.Div(className: $"w-[{WrapperWidthPx}px]", refCallback: s_rowRef, children: new VNode[]
            {
                V.Label(name: "balanced", text: LongWrapText, className: "text-balance", refCallback: s_wrapWithFontRef),
                V.Div(name: "sibling", className: "w-[200px] h-[10px]"),
            });
            _mounted = V.Mount(_host.Root, tree);
            yield return WaitRealtime(0.5);
            var balanced = _host.Root.Q<Label>("balanced");
            var sibling = _host.Root.Q<VisualElement>("sibling");
            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text wrapped, so balance wrote a real width into the row");

            // Assert — the sibling ends up under its declared 200px: the row overflows and flex-shrink
            // shares the loss across both boxes. The sibling is wide enough that any balanced width
            // overflows the row by a large margin, so the outcome does not depend on where a platform's
            // font metrics put that width.
            Assert.That(sibling.resolvedStyle.width, Is.LessThan(200f));
        }

        [UnityTest]
        public IEnumerator Given_ALabelDeclaringASizeShorthand_When_Balanced_Then_TheDeclaredWidthIsLeftAlone()
        {
            // Arrange / Act — `size-[..]` is the one declaration whose layer key is not the slot it writes:
            // it registers under Size and writes width AND height, so restoring only Width hands back an
            // element with no width at all.
            yield return MountWithSizingClass("TextBalanceSizeShorthandPanel", LongWrapText, "size-[40px]");
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.height, Is.EqualTo(40f).Within(0.5f),
                "Precondition: the shorthand's height took effect, so its width is the half under test");

            // Assert
            Assert.That(balanced.resolvedStyle.width, Is.EqualTo(40f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabelDeclaringOnlyAHeight_When_ItReleasesTheWidthSlot_Then_TheHeightSurvives()
        {
            // Arrange / Act — the mirror hazard: re-asserting Size on an element that has no Size layer
            // would null the height along with the width, and this height comes from a layer of its own.
            // Short text, so the single-line gate releases the slot.
            yield return MountWithSizingClass("TextBalanceHeightOnlyPanel", "Hi", "h-[100px]");
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.width, Is.EqualTo(WrapperWidthPx).Within(0.5f),
                "Precondition: the width slot was released, so the box stretches to the wrapper");

            // Assert
            Assert.That(balanced.resolvedStyle.height, Is.EqualTo(100f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ALabelDeclaringAScaleWidth_When_Balanced_Then_TheDeclaredWidthIsLeftAlone()
        {
            // Arrange / Act — the same contract for a width that exists only as a USS class. Both spellings
            // are read from the class tokens, so neither is honored more than the other.
            yield return MountWithSizingClass("TextBalanceDeclaredScaleWidthPanel", LongWrapText, "w-40", loadUtilities: true);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text wraps at the declared width, so balance had a real candidate to write");

            // Assert — `.w-40 { width: var(--space-40); }` with `--space-40: 160px`, far enough from any
            // width the search would have produced that a font-metric shift cannot blur the two.
            Assert.That(balanced.resolvedStyle.width, Is.EqualTo(160f).Within(0.5f));
        }
    }
}
