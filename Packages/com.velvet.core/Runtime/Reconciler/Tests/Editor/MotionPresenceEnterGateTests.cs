using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the standalone-enter gate against AnimatePresence subtrees. The presence expansion
    /// animates only its first-found anchor Motion, so a Motion that is NOT the anchor (e.g. nested
    /// under a keyed wrapper Div) must keep its own mount enter — the ambient expansion-depth
    /// counter must not blanket-suppress it, or wrapping content in AnimatePresence silently
    /// disables unrelated Motions' documented initial→animate behavior. And an <c>initial</c> the
    /// enter machinery cannot resolve (no own <c>animate</c>, or a label missing from the variants)
    /// must warn instead of being silently inert, matching the factory's other inert-configuration
    /// diagnostics.
    /// </summary>
    [TestFixture]
    internal sealed class MotionPresenceEnterGateTests
    {
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
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
        public void Given_ANonAnchorMotionInsideAPresenceChild_When_Mounted_Then_ItStartsAtItsInitialVariant()
        {
            // Arrange / Act — mount the presence subtree; the nested Motion is not the presence's
            // anchor, so its own initial→animate enter must play (it starts at the initial classes;
            // the EditMode scheduler never fires the swap, mirroring MotionStandaloneEnterTests).
            using var mounted = V.Mount(_root, V.Component(WrappedPresence, key: "host"));

            // Assert — the enter was scheduled: the element carries variants[initial]'s classes
            // instead of mounting directly at rest.
            Assert.That(_root.Q<VisualElement>("inner").ClassListContains("opacity-0"), Is.True);
        }

        [Test]
        public void Given_AnInitialTheEnterCannotResolve_When_Mounted_Then_ItWarnsInsteadOfStayingSilentlyInert()
        {
            // Arrange — initial with NO own animate (inherited-label configurations are not yet
            // driven by the standalone enter), which previously warned and now must again.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("initial"));

            // Act
            using var mounted = V.Mount(_root,
                V.Motion(name: "m", variants: s_fade, initial: "hidden"));

            // Assert — the element mounted; the warning expectation is enforced at test end.
            Assert.That(_root.Q<VisualElement>("m"), Is.Not.Null);
        }
    }
}
