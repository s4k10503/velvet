using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// The ring band's side of the co-fade the <see cref="StyleAnimationScheduler"/> runs for drop shadows
    /// (specified in <see cref="ShadowAnimationVisibilityTests"/>).
    /// </summary>
    /// <remarks>
    /// The band is a SIBLING of the ringed element, not a descendant, so UI Toolkit's opacity compositing —
    /// which does reach an overlay belonging to a descendant — never reaches it. An <c>AnimatePresence</c> exit
    /// that fades its element would therefore leave the band at full strength for the whole exit and pop it out
    /// at the end, so the band is driven explicitly. This is the one respect in which the sibling hosting costs
    /// something the wrapper hosting did not: a wrapper-hosted band was inside the faded subtree.
    /// </remarks>
    [TestFixture]
    internal sealed class RingCoFadeTests
    {
        private static readonly RingSpec Spec = new(width: 2f, color: Color.red, offset: 0f, inset: false);

        private EditorPanelSimulator _sim;
        private StyleAnimationScheduler _scheduler;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            _scheduler = new StyleAnimationScheduler();
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private void Tick() => _sim.FrameUpdateMs(16);

        // A ringed element already parented, so Attach can place the band beside it.
        private RingBinding AttachRingedElement(out VisualElement target)
        {
            target = new VisualElement();
            _sim.rootVisualElement.Add(target);
            return RingOverlay.Attach(target, Spec, Array.Empty<string>());
        }

        [Test]
        public void Given_ARingedSpringExit_When_APanelTickRunsMidFlight_Then_TheBandIsFadingWithItsElement()
        {
            // Arrange
            var binding = AttachRingedElement(out var target);
            target.AddToClassList("opacity-100");
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Spring,
                Stiffness = 200f,
                Damping = 26f,
                ExitFromClass = "opacity-100",
                ExitToClass = "opacity-0",
            };

            // Act
            _scheduler.PlayExit(target, config, onComplete: null);
            Tick();
            Tick();
            Tick();

            // Assert — the element must still be mid-fade (a settled or never-started exit would make the band's
            // value meaningless), the band must carry a concrete inline opacity, and that opacity must be below
            // full. The keyword term is load-bearing: an untouched StyleFloat reads back value 0, so a value-only
            // comparison against 1 passes for a band no co-fade ever touched. All three in one comparison —
            // gating the element's progress separately would report a stalled exit as inconclusive rather than
            // as a failure, and a band left at full strength is what that would hide.
            Assert.That(
                (target.resolvedStyle.opacity < 1f, binding.Overlay.style.opacity.keyword,
                    binding.Overlay.style.opacity.value < 1f),
                Is.EqualTo((true, StyleKeyword.Undefined, true)),
                $"element={target.resolvedStyle.opacity} band={binding.Overlay.style.opacity}");
        }

        [Test]
        public void Given_ARingedExit_When_ItIsCancelled_Then_TheBandsInlineOpacityIsReleased()
        {
            // Arrange — an exit that has already seeded the band's inline opacity.
            var binding = AttachRingedElement(out var target);
            target.AddToClassList("opacity-100");
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Spring,
                Stiffness = 200f,
                Damping = 26f,
                ExitFromClass = "opacity-100",
                ExitToClass = "opacity-0",
            };
            _scheduler.PlayExit(target, config, onComplete: null);
            Tick();
            var whileFading = binding.Overlay.style.opacity.keyword;

            // Act
            _scheduler.CancelExit(target);

            // Assert — driven while the exit ran, then released to the keyword rather than pinned to 1, so the
            // band returns to whatever the cascade says instead of overriding a ring the element also fades by
            // other means. The first term is what separates a released band from one the co-fade never drove:
            // both read back the Null keyword.
            Assert.That((whileFading, binding.Overlay.style.opacity.keyword),
                Is.EqualTo((StyleKeyword.Undefined, StyleKeyword.Null)));
        }
    }
}
