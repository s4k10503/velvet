using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which renders write a Motion pose's inline-resolved tokens and which leave them for a tween swap
    /// to write: what a runtime label change, a presence exit and an exit's cancel do with them. The root has
    /// no panel, so no swap ever runs, and each case reads what the render itself left on the element.
    /// </summary>
    [TestFixture]
    internal sealed class MotionInlineHoldTests
    {
        private static readonly StyleTransitionConfig s_tween = new() { DurationSec = 0.3f };
        private static readonly StyleTransitionConfig s_spring = new() { Type = TransitionType.Spring };

        private readonly record struct KeySetState(string Keys);

        private sealed class KeySetStore : Store<KeySetState>
        {
            public KeySetStore() : base(new KeySetState("a")) { }
            public void Set(string keys) => SetState(_ => new KeySetState(keys));
            protected override void ResetCore() => SetState(_ => new KeySetState("a"));
        }

        private static KeySetStore s_keyStore;
        private static Func<string, VNode> s_child;

        private VisualElement _root;
        private Reconciler _reconciler;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _reconciler = new Reconciler();
            s_keyStore = null;
            s_child = null;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _reconciler?.Dispose();
        }

        private static Dictionary<string, MotionVariant> Slide(StyleTransitionConfig toRight) => new()
        {
            ["left"] = "translate-x-[0px]",
            ["right"] = new MotionVariant("translate-x-[40px]", toRight),
        };

        private static MotionNode Card(IReadOnlyDictionary<string, MotionVariant> variants, string label,
            string className)
            => V.Motion(className: className, name: "card", variants: variants, animate: label, transition: s_tween);

        // Mounts the card at the first label, then renders it at each later one in turn.
        private VisualElement Render(IReadOnlyDictionary<string, MotionVariant> variants, string className,
            params string[] labels)
        {
            VNode previous = Card(variants, labels[0], className);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), new[] { previous });
            for (var i = 1; i < labels.Length; i++)
            {
                VNode next = Card(variants, labels[i], className);
                _reconciler.Reconcile(_root, new[] { previous }, new[] { next });
                previous = next;
            }
            return _root.Q<VisualElement>("card");
        }

        // The inline translate x in pixels, or NaN where nothing is written.
        private static float TranslateX(VisualElement element)
            => element.style.translate.keyword == StyleKeyword.Undefined
                ? element.style.translate.value.x.value
                : float.NaN;

        private static float Width(VisualElement element)
            => element.style.width.keyword == StyleKeyword.Undefined ? element.style.width.value.value : float.NaN;

        // --- runtime label change ---

        // GREEN_ON_BASE(characterization): the base writes every pose at the render, holding nothing back.
        // A swap that plays no tween has to keep doing so while an earlier tween still holds its pose.
        [Test]
        public void Given_ATweenSwapStillHoldingItsPose_When_AZeroDurationSwapFollows_Then_TheNewPoseIsWrittenAtOnce()
        {
            // Arrange
            var variants = new Dictionary<string, MotionVariant>
            {
                ["left"] = "translate-x-[0px]",
                ["right"] = new MotionVariant("translate-x-[40px]", s_tween),
                ["far"] = new MotionVariant("translate-x-[80px]", StyleTransitionConfig.None),
            };

            // Act
            var card = Render(variants, null, "left", "right", "far");

            // Assert
            Assert.That(TranslateX(card), Is.EqualTo(80f));
        }

        // GREEN_ON_BASE(characterization): the base writes every pose at the render, holding nothing back.
        // A zero-duration tween completes without the swap that would release a held one.
        [Test]
        public void Given_ARestingPose_When_AZeroDurationTweenChangesIt_Then_ThePoseIsWrittenAtTheRender()
        {
            // Act
            var card = Render(Slide(StyleTransitionConfig.None), null, "left", "right");

            // Assert
            Assert.That(TranslateX(card), Is.EqualTo(40f));
        }

        // GREEN_ON_BASE(characterization): a zero-duration bezier writes the destination pose at the render.
        [Test]
        public void Given_ARestingPose_When_AZeroDurationBezierChangesIt_Then_ThePoseIsWrittenAtTheRender()
        {
            // Arrange
            var transition = new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 0f };

            // Act
            var card = Render(Slide(transition), null, "left", "right");

            // Assert
            Assert.That(TranslateX(card), Is.EqualTo(40f));
        }

        [Test]
        public void Given_ATweenAtTheLongestPlayableDuration_When_ItChangesThePose_Then_TheOldPoseStaysUntilTheSwap()
        {
            // Act — 10s is the scheduler's longest playable duration.
            var card = Render(Slide(new StyleTransitionConfig { DurationSec = 10f }), null, "left", "right");

            // Assert
            Assert.That(TranslateX(card), Is.EqualTo(0f));
        }

        // GREEN_ON_BASE(characterization): the base writes every pose at the render, holding nothing back.
        // A tween past the longest playable duration never plays the swap that would release a held one.
        [Test]
        public void Given_ATweenPastTheLongestPlayableDuration_When_ItChangesThePose_Then_ThePoseIsWrittenAtTheRender()
        {
            // Act
            var card = Render(Slide(new StyleTransitionConfig { DurationSec = 11f }), null, "left", "right");

            // Assert
            Assert.That(TranslateX(card), Is.EqualTo(40f));
        }

        [Test]
        public void Given_APoseWhoseInlineValueTheNextPoseLacks_When_ATweenSwapStarts_Then_TheValueStaysUntilTheSwap()
        {
            // Arrange — the destination carries no inline-resolved token at all.
            var variants = new Dictionary<string, MotionVariant>
            {
                ["left"] = "w-[10px]",
                ["right"] = "opacity-50",
            };

            // Act
            var card = Render(variants, null, "left", "right");

            // Assert
            Assert.That(Width(card), Is.EqualTo(10f));
        }

        // GREEN_ON_BASE(characterization): the base writes the Motion's own classes at every render.
        // The class set a hold composes has to keep carrying them.
        [Test]
        public void Given_AnInlineValueInTheMotionsOwnClasses_When_ATweenSwapHoldsThePose_Then_TheValueStays()
        {
            // Act
            var card = Render(Slide(s_tween), "w-[37px]", "left", "right");

            // Assert
            Assert.That(Width(card), Is.EqualTo(37f));
        }

        // GREEN_ON_BASE(characterization): the base writes a pose's classes at the render, holding nothing back.
        // A hold keeps back only the inline-resolved ones, which a font utility is not.
        [Test]
        public void Given_ADestinationPoseWithAFontUtility_When_ATweenSwapStarts_Then_ItsFontIsWrittenAtTheRender()
        {
            // Arrange
            var variants = new Dictionary<string, MotionVariant>
            {
                ["left"] = "translate-x-[0px]",
                ["right"] = "translate-x-[40px] font-bold",
            };

            // Act
            var card = Render(variants, null, "left", "right");

            // Assert
            Assert.That(card.style.unityFontStyleAndWeight.value, Is.EqualTo(UnityEngine.FontStyle.Bold));
        }

        // Swaps into a destination pose carrying the given variant token beside a translate. Every token the
        // cases below pass carries a bracket, which IsInlineResolved alone would take for an inline value.
        private (VisualElement Card, ReconcilerContext Context) SwapIntoVariantToken(string token)
        {
            var variants = new Dictionary<string, MotionVariant>
            {
                ["left"] = "translate-x-[0px]",
                ["right"] = $"translate-x-[40px] {token}",
            };
            var card = Render(variants, null, "left", "right");
            return (card, _reconciler.Context);
        }

        // GREEN_ON_BASE(characterization): the base configures a pose's variant tokens at the render.
        // A hold keeps back only inline-resolved tokens, which a hover: token is not.
        [Test]
        public void Given_ADestinationPoseWithAHoverToken_When_ATweenSwapStarts_Then_ItsManipulatorIsConfiguredAtTheRender()
        {
            // Act
            var (card, ctx) = SwapIntoVariantToken("hover:bg-[#ff0000]");

            // Assert
            Assert.That(ctx.VariantManipulators.ContainsKey(card), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base configures a pose's variant tokens at the render.
        // A hold keeps back only inline-resolved tokens, which a first: token is not.
        [Test]
        public void Given_ADestinationPoseWithAStructuralToken_When_ATweenSwapStarts_Then_ItsRuleIsConfiguredAtTheRender()
        {
            // Act
            var (card, ctx) = SwapIntoVariantToken("first:bg-[#ff0000]");

            // Assert
            Assert.That(ctx.StructuralVariants.ContainsKey(card), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base configures a pose's variant tokens at the render.
        // A hold keeps back only inline-resolved tokens, which a has- token is not.
        [Test]
        public void Given_ADestinationPoseWithAHasToken_When_ATweenSwapStarts_Then_ItsRuleIsConfiguredAtTheRender()
        {
            // Act
            var (card, ctx) = SwapIntoVariantToken("has-[.active]:bg-[#ff0000]");

            // Assert
            Assert.That(ctx.HasClassVariants.ContainsKey(card), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base configures a pose's variant tokens at the render.
        // A hold keeps back only inline-resolved tokens, which a data- token is not.
        [Test]
        public void Given_ADestinationPoseWithAnAttributeToken_When_ATweenSwapStarts_Then_ItsRuleIsConfiguredAtTheRender()
        {
            // Act
            var (card, ctx) = SwapIntoVariantToken("data-[state=open]:bg-[#ff0000]");

            // Assert
            Assert.That(ctx.AttributeVariants.ContainsKey(card), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base configures a pose's variant tokens at the render.
        // A hold keeps back only inline-resolved tokens, which a supports- token is not.
        [Test]
        public void Given_ADestinationPoseWithASupportsToken_When_ATweenSwapStarts_Then_ItsRuleIsConfiguredAtTheRender()
        {
            // Act
            var (card, ctx) = SwapIntoVariantToken("supports-[display:flex]:bg-[#ff0000]");

            // Assert
            Assert.That(ctx.SupportsVariants.ContainsKey(card), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base records a pose's gated child-variant payload at the render.
        // A hold keeps back only inline-resolved tokens, which a [&>*]: token is not.
        [Test]
        public void Given_ADestinationPoseWithAGatedChildVariantToken_When_ATweenSwapStarts_Then_ItsGateSourceIsRecordedAtTheRender()
        {
            // Act
            var (card, ctx) = SwapIntoVariantToken("[&>*]:font-bold");

            // Assert
            Assert.That(ctx.VariantGateClasses.ContainsKey(card), Is.True);
        }

        // --- presence exit and its cancel ---

        [Component]
        private static VNode PresenceHost()
        {
            var keys = Hooks.UseStore(s_keyStore, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(s_child(key.ToString()));
            }
            return V.AnimatePresence(key: "presence", children: children.ToArray());
        }

        // Enters from `from` to translate-x-[0px] on a tween and exits to translate-x-[60px] on `exit`, so the
        // hold its enter leaves is still pending when a removal comes before the swap.
        private static Dictionary<string, MotionVariant> Travel(StyleTransitionConfig exit,
            string from = "translate-x-[30px]") => new()
        {
            ["from"] = from,
            ["rest"] = new MotionVariant("translate-x-[0px]", s_tween),
            ["gone"] = new MotionVariant("translate-x-[60px]", exit),
        };

        private static VNode Traveller(string name, StyleTransitionConfig exit, string from = "translate-x-[30px]")
            => V.Motion(name: name, variants: Travel(exit, from), initial: "from", animate: "rest", exit: "gone",
                transition: s_tween);

        // Mounts the presence with key "a", then removes it.
        private VisualElement MountAndRemove(Func<string, VNode> child, string name, KeySetStore keys)
        {
            s_keyStore = keys;
            s_child = child;
            _mounted = V.Mount(_root, V.Component(PresenceHost, key: "root"));
            keys.Set(string.Empty);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            return _root.Q<VisualElement>(name);
        }

        // GREEN_ON_BASE(characterization): the base's enter never writes initial's translate at all.
        // The anchor rests at animate's whatever its descendants do.
        [Test]
        public void Given_AnAnchorThatPlaysNoExitRemovedMidEnter_When_ADescendantExits_Then_TheAnchorRestsAtItsAnimatePose()
        {
            // Arrange — the anchor's own transition plays no exit.
            using var keys = new KeySetStore();
            VNode Child(string key) => V.Motion(key: key, name: "item", variants: Travel(s_tween), initial: "from",
                animate: "rest", transition: StyleTransitionConfig.None,
                children: new VNode[] { Traveller("inner", s_tween) });

            // Act
            var item = MountAndRemove(Child, "item", keys);

            // Assert
            Assert.That(TranslateX(item), Is.EqualTo(0f));
        }

        // GREEN_ON_BASE(characterization): the base's enter never writes initial's translate at all.
        // A descendant rests at animate's until its exit swaps.
        [Test]
        public void Given_ADescendantRemovedMidEnter_When_ItsTweenExitStarts_Then_ItRestsAtItsAnimatePose()
        {
            // Arrange
            using var keys = new KeySetStore();
            VNode Child(string key) => V.Div(key: key, children: new VNode[] { Traveller("inner", s_tween) });

            // Act
            var inner = MountAndRemove(Child, "inner", keys);

            // Assert
            Assert.That(TranslateX(inner), Is.EqualTo(0f));
        }

        // GREEN_ON_BASE(characterization): the base's enter never writes initial's translate at all.
        // So y is never anything but its resting 0 whatever the spring exit reads.
        [Test]
        public void Given_ADescendantRemovedMidEnter_When_ItsSpringExitStarts_Then_AnAxisNoPoseNamesStaysAtRest()
        {
            // Arrange — the enter starts from a y the resting and exit poses leave unnamed, which a spring
            // reads off the element as it starts.
            using var keys = new KeySetStore();
            VNode Child(string key) => V.Div(key: key, children: new VNode[]
            {
                Traveller("inner", s_spring, from: "translate-x-[30px] translate-y-[40px]"),
            });

            // Act
            var inner = MountAndRemove(Child, "inner", keys);

            // Assert
            Assert.That(inner.style.translate.value.y.value, Is.EqualTo(0f));
        }

        // GREEN_ON_BASE(characterization): the base's enter never writes initial's translate at all.
        // The anchor rests at animate's until its exit swaps.
        [Test]
        public void Given_AnAnchorRemovedMidEnter_When_ItsTweenExitStarts_Then_ItRestsAtItsAnimatePose()
        {
            // Arrange
            using var keys = new KeySetStore();
            VNode Child(string key) => V.Motion(key: key, name: "item", variants: Travel(s_tween), initial: "from",
                animate: "rest", exit: "gone", transition: s_tween);

            // Act
            var item = MountAndRemove(Child, "item", keys);

            // Assert
            Assert.That(TranslateX(item), Is.EqualTo(0f));
        }

        [Test]
        public void Given_APresetExitFromOneOfTheMotionsOwnClasses_When_TheKeyReturnsMidExit_Then_TheClassStays()
        {
            // Arrange — the exit starts from keep-me, which is also the first of the Motion's own classes.
            using var keys = new KeySetStore();
            var exit = new StyleTransitionConfig { DurationSec = 0.3f, ExitFromClass = "keep-me", ExitToClass = "gone" };
            VNode Child(string key) => V.Motion(key: key, name: "item", className: "keep-me other", transition: exit);
            var item = MountAndRemove(Child, "item", keys);

            // Act
            keys.Set("a");
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(item.ClassListContains("keep-me"), Is.True);
        }
    }
}
