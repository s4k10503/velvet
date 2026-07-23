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
    /// stylesheet-less `w-[100px]` usage).
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

        // Many short words so the wrap point has real work to do at any wrapper width used below (100px
        // up to ~350px) — reused by the state-driven tests, which each need long-wrapping text at a
        // width chosen after mount rather than at construction time like MountPair's own literal.
        private const string LongWrapText = "This label carries enough short plain words to wrap across many lines " +
            "inside a narrow box so the balance search over its width has real work to do and a " +
            "clearly uneven last line to fix";

        private static StateUpdater<bool> s_setWrapperWide;
        private static StateUpdater<string> s_setSwapText;
        private static StateUpdater<bool> s_setAddMaxWidth;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            s_setWrapperWide = default;
            s_setSwapText = default;
            s_setAddMaxWidth = default;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
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
        public IEnumerator Given_ABalancedLabel_When_ItsWrapperWidensAfterMount_Then_ItRebalancesToAWiderMaxWidth()
        {
            // Arrange
            _host = new RenderTexturePanelHost("TextBalanceWidenPanel", 400, 400);
            _mounted = V.Mount(_host.Root, V.Component(WidenHost, key: "root"));
            yield return WaitRealtime(0.5);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced, Is.Not.Null, "Precondition: the label mounted");
            var narrowWidth = balanced.resolvedStyle.width;
            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text wrapped multi-line at the narrow wrapper, so balance wrote a real narrower maxWidth");

            // Act — widen the wrapper, an ANCESTOR of the label, not the label itself.
            s_setWrapperWide.Invoke(true);
            yield return WaitRealtime(0.6);

            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text still wraps multi-line at the wider wrapper, so this is a real re-balance and not the single-line clear path");

            // Assert — the manipulator's own maxWidth write pins the target's size, so only a listener on
            // the PARENT's own GeometryChangedEvent (not just the target's) can catch an ancestor widening
            // and re-run the search; without it the label stays stuck at the narrow value forever.
            Assert.That(balanced.resolvedStyle.width, Is.GreaterThan(narrowWidth));
        }

        // The label's own text toggles between a short single-line string and LongWrapText, at a fixed
        // wrapper width — state-driven so the CHANGE happens after mount via TextElement's ChangeEvent<string>.
        [Component]
        private static VNode TextSwapHost()
        {
            var (text, setText) = Hooks.UseState("Hi");
            s_setSwapText = setText;
            return V.Div(className: "w-[140px]", children: new VNode[]
            {
                V.Label(name: "balanced", text: text, className: "text-balance", refCallback: s_wrapWithFontRef),
            });
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabel_When_ItsTextChangesToAWrappingStringAfterMount_Then_TheInlineMaxWidthAppears()
        {
            // Arrange — mounts short enough to sit on one line, which the multi-line gate leaves entirely
            // unconstrained: no inline maxWidth exists yet, giving the swap below a clean edge to observe.
            _host = new RenderTexturePanelHost("TextBalanceTextSwapPanel", 400, 400);
            _mounted = V.Mount(_host.Root, V.Component(TextSwapHost, key: "root"));
            yield return WaitRealtime(0.5);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced, Is.Not.Null, "Precondition: the label mounted");
            Assume.That(balanced.resolvedStyle.height, Is.LessThan(balanced.resolvedStyle.fontSize * 1.8f),
                "Precondition: the short initial text stayed on a single line");
            Assume.That(balanced.style.maxWidth.keyword, Is.EqualTo(StyleKeyword.Null),
                "Precondition: a single-line label carries no balanced constraint to begin with");

            // Act — swap in text long enough to wrap at the SAME (unchanged) wrapper width.
            s_setSwapText.Invoke(LongWrapText);
            yield return WaitRealtime(0.6);

            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the new text actually wrapped onto multiple lines");

            // Assert — a text change re-triggers the balance computation THROUGH SOME PATH: the multi-line
            // gate flips from "leave unconstrained" to "write the searched bound", so an inline maxWidth
            // appearing at all proves the swap re-ran the search. This swap changes the label's resolved
            // height, so the geometry listener alone would also re-run it — the text-change notification
            // is NOT isolated here (isolating it needs a same-height swap, whose construction depends on
            // font metrics too fragile to pin). The magnitude of the resulting width is deliberately not
            // asserted either — how densely text packs at the wrapper's width is font-metric trivia, not
            // the re-trigger contract under test.
            Assert.That(balanced.style.maxWidth.keyword, Is.Not.EqualTo(StyleKeyword.Null));
        }

        // The label's classNames toggles a co-present max-w-[400px] utility on/off alongside the
        // ever-present text-balance token — state-driven so the utility's own inline write lands in a
        // LATER patch than the one that first established the manipulator's own balanced value.
        [Component]
        private static VNode OwnershipHost()
        {
            var (addMaxWidth, setAddMaxWidth) = Hooks.UseState(false);
            s_setAddMaxWidth = setAddMaxWidth;
            var cls = addMaxWidth ? "text-balance max-w-[400px]" : "text-balance";
            return V.Div(className: "w-[100px]", children: new VNode[]
            {
                V.Label(name: "balanced", text: LongWrapText, className: cls, refCallback: s_wrapWithFontRef),
            });
        }

        [UnityTest]
        public IEnumerator Given_ABalancedLabel_When_AMaxWidthUtilityIsAddedInALaterPatch_Then_BalanceKeepsOwningTheInlineMaxWidth()
        {
            // Arrange — mounts with text-balance alone so its own computed maxWidth is already established
            // before the utility ever enters the class list.
            _host = new RenderTexturePanelHost("TextBalanceOwnershipPanel", 400, 400);
            _mounted = V.Mount(_host.Root, V.Component(OwnershipHost, key: "root"));
            yield return WaitRealtime(0.5);
            var balanced = _host.Root.Q<Label>("balanced");
            Assume.That(balanced, Is.Not.Null, "Precondition: the label mounted");
            Assume.That(balanced.resolvedStyle.height, Is.GreaterThan(balanced.resolvedStyle.fontSize * 1.5f),
                "Precondition: the text wrapped, so text-balance wrote a real (not cleared) maxWidth");
            var balancedMaxWidth = balanced.style.maxWidth.value.value;

            // Act — patch in a co-present max-w-[400px] utility on the SAME element, in the SAME render
            // that first applies it: DiffClassList's inline write and the manipulator's own Refresh() both
            // touch style.maxWidth in this one patch.
            s_setAddMaxWidth.Invoke(true);
            yield return WaitRealtime(0.6);

            // Assert — text-balance re-asserts every patch while its class is present, so the utility's
            // own 400px write never survives past the same patch that introduced it.
            Assert.That(balanced.style.maxWidth.value.value, Is.EqualTo(balancedMaxWidth).Within(0.5f));
        }
    }
}
