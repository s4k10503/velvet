using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the diagnostic for the one Motion prop that is still genuinely inert outside AnimatePresence:
    /// <c>exit</c> requires AnimatePresence to defer the unmount a removal animates against, so it warns like the
    /// factory's other silently-inert configurations (shadow-*, clip-path-*). <c>initial</c> is NOT presence-scoped
    /// — a standalone <c>V.Motion(initial:, animate:)</c> plays its own mount enter (Framer parity: initial/animate
    /// apply to any motion.* component) — so it must NOT warn.
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
        public void Given_AMotionWithExitOutsideAnimatePresence_When_Mounted_Then_ItWarnsThatExitIsInert()
        {
            // Arrange — the warning is expected (LogAssert fails the test if it never fires).
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("exit on a Motion outside AnimatePresence"));

            // Act — an `exit` variant declared with no AnimatePresence to defer the unmount for.
            using var mounted = V.Mount(_root,
                V.Motion(name: "m", variants: s_fade, animate: "visible", exit: "hidden"));

            // Assert — the element mounted; the expected warning is enforced by LogAssert at test
            // end (an Assert.Pass would bypass that unmatched-expectation check).
            Assert.That(_root.Q<VisualElement>("m"), Is.Not.Null);
        }

        [Test]
        public void Given_AMotionWithOnlyInitialAndAnimateOutsideAnimatePresence_When_Mounted_Then_ItDoesNotWarn()
        {
            // Arrange — capture log messages directly rather than relying on LogAssert's implicit
            // unmatched-message behavior: a Warning with no LogAssert.Expect does NOT fail a Unity test on
            // its own (only Error/Exception/Assert do), so the negative case needs a real assertion instead.
            var warned = false;
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning) warned = true;
            }
            Application.logMessageReceived += OnLog;
            try
            {
                // Act — a standalone entrance pair (no `exit`, no AnimatePresence).
                using var mounted = V.Mount(_root,
                    V.Motion(name: "m", variants: s_fade, initial: "hidden", animate: "visible"));
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            // Assert — initial/animate work standalone, so nothing is inert and nothing warns.
            Assert.That(warned, Is.False);
        }
    }
}
