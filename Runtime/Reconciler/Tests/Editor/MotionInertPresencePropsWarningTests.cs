using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the diagnostic for presence-scoped Motion props used outside AnimatePresence. Enter and
    /// exit tweens are scheduled by the AnimatePresence expansion, so a standalone
    /// <c>V.Motion(initial:, animate:)</c> mounts already at its animate/resting classes and the
    /// declared initial never plays — previously with no trace, which is surprising for a
    /// Framer-style entrance-animation pair. The factory already warns for other silently-inert
    /// Motion configurations (shadow-*, clip-path-*); initial/exit must warn the same way.
    /// </summary>
    [TestFixture]
    internal sealed class MotionInertPresencePropsWarningTests
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

        [Test]
        public void Given_AMotionWithInitialOutsideAnimatePresence_When_Mounted_Then_ItWarnsThatInitialIsInert()
        {
            // Arrange — the warning is expected (LogAssert fails the test if it never fires).
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("initial/exit on a Motion outside AnimatePresence"));

            // Act — an idiomatic Framer-style entrance pair, mounted with no AnimatePresence.
            using var mounted = V.Mount(_root,
                V.Motion(name: "m", variants: s_fade, initial: "hidden", animate: "visible"));

            // Assert — the element mounted; the expected warning is enforced by LogAssert at test
            // end (an Assert.Pass would bypass that unmatched-expectation check).
            Assert.That(_root.Q<VisualElement>("m"), Is.Not.Null);
        }
    }
}
