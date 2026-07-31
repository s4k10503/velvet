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
    /// Pins, by real GPU pixel readback, how much of a drop shadow reaches the screen when its caster is
    /// translucent — at rest, and while an enter is in flight over it.
    /// </summary>
    /// <remarks>
    /// The shadow is a textured <c>mgc.Allocate</c> quad in the caster's own generated content, and the
    /// renderer scales it by the caster's resolved opacity exactly as it scales the <c>painter2D</c> face
    /// repainted beside it — measured as byte-for-byte parity across every context that could have separated
    /// them, in <see cref="PaintOpacityParityPlaybackTests"/>. A scheduler that ALSO scaled the shadow while
    /// an animation fades the caster therefore applied the same opacity twice and landed the shadow at
    /// opacity squared; one did, and these cases are what keeps it gone.
    /// <para>
    /// The caster's opacity is pinned by INLINE style, which outranks the play's own class-driven fade, so the
    /// in-flight case samples a known translucency instead of racing the tween. Whether a play is running at
    /// the read is asserted rather than assumed, through the enter's to-class: a classic enter adds it at its
    /// frame-boundary swap and removes it on completion, so the term fails both for a play that finished early
    /// and for one that never started. A duration outside the scheduler's accepted range is refused with a
    /// warning and no play, which reads as a clean pass with nothing under test.
    /// </para>
    /// </remarks>
    [Timeout(600000)]
    internal sealed class ShadowFadeOpacityPlaybackTests
    {
        private const int Size = 240;
        private const string CasterClasses =
            "w-[80px] h-[80px] mt-[80px] ml-[80px] bg-[#ffffff] shadow-[0px_0px_24px_#ff0000]";

        // Below this the halo is too faint for "scaled once" and "scaled twice" to be separable in 8-bit
        // readback, so a case that measured it would pass on either. Guards the instrument, never the verdict.
        private const float SeparableHalo = 20f;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _frameRateScope.Dispose();
            DisposePanel();
            yield return null;
        }

        private void DisposePanel()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        // The mean red of a strip just outside the caster's LEFT edge — the outer halo, where only the shadow
        // quad paints. Derived from the caster's measured box, never from the declared margins.
        private float HaloRed(VisualElement caster)
        {
            var box = caster.worldBound;
            var x = Mathf.RoundToInt(box.xMin) - 5;
            var y = Mathf.RoundToInt(box.yMin) + 20;
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Size, Size));
            var total = 0f;
            var n = 0;
            for (var row = y; row < y + 40; row++)
            {
                for (var col = x; col < x + 4; col++)
                {
                    total += pixels[((Size - 1 - row) * Size) + col].r;
                    n++;
                }
            }
            return n == 0 ? 0f : total / n;
        }

        private IEnumerator MountCaster(string name, float opacity)
        {
            DisposePanel();
            _host = new RenderTexturePanelHost(name, Size, Size);
            _host.Root.style.backgroundColor = Color.black;
            _host.Root.LoadBundledStyleUtilitiesForTest();
            _mounted = V.Mount(_host.Root, V.Div(name: "caster", className: CasterClasses));
            _host.Root.Q<VisualElement>("caster").style.opacity = opacity;
            yield return WaitRealtime(0.9);
        }

        private const string EnterToClass = "opacity-100";

        // Long enough that the play is still running at the read, and inside the range the scheduler accepts.
        private static readonly StyleTransitionConfig LongEnter = new()
        {
            DurationSec = 8f,
            EnterFromClass = "opacity-0",
            EnterToClass = EnterToClass,
        };

        [UnityTest]
        public IEnumerator Given_ATranslucentShadowCaster_When_AnEnterIsInFlightOverIt_Then_TheHaloMatchesTheSameCasterAtRest()
        {
            // Arrange — the caster held at half opacity with nothing animating, as the reference reading.
            yield return MountCaster("ShadowResting", 0.5f);
            var resting = HaloRed(_host.Root.Q<VisualElement>("caster"));

            // Act — the same caster, same pinned opacity, with an enter running over it.
            yield return MountCaster("ShadowFading", 0.5f);
            var caster = _host.Root.Q<VisualElement>("caster");
            _mounted.Root.Reconciler.Context.StyleAnimationScheduler.PlayEnter(caster, LongEnter);
            yield return WaitRealtime(1.2);
            var stillPlaying = caster.ClassListContains(EnterToClass);
            var casterOpacityPercent = Mathf.RoundToInt(caster.resolvedStyle.opacity * 100f);
            var fading = HaloRed(caster);

            // Assert — the reference reading is strong enough to tell the two outcomes apart, the caster really
            // is at the arranged half opacity, a play really is covering it at the moment of the read, and the
            // in-flight halo lands on the "scaled once" reading rather than the halved one. A play-driven
            // multiplier reads back at half the reference. All four in one comparison: the last term is
            // satisfied on its own by a black frame, by a play that never began, and — the reason the opacity
            // term is here — by an OPAQUE caster, for which the two hypotheses predict the same halo and the
            // case would pin nothing while still reading green.
            var halved = resting * 0.5f;
            Assert.That(
                (resting > SeparableHalo, casterOpacityPercent, stillPlaying,
                    Mathf.Abs(fading - resting) < Mathf.Abs(fading - halved)),
                Is.EqualTo((true, 50, true, true)),
                $"resting={resting} fading={fading} halved={halved} playing={stillPlaying} "
                + $"casterOpacity={casterOpacityPercent}");
        }

        [UnityTest]
        public IEnumerator Given_AShadowCaster_When_ItsOpacityIsHalved_Then_TheHaloIsHalvedWithIt()
        {
            // Arrange — the same caster fully opaque, as the reference reading.
            yield return MountCaster("ShadowOpaque", 1f);
            var opaque = HaloRed(_host.Root.Q<VisualElement>("caster"));

            // Act
            yield return MountCaster("ShadowHalf", 0.5f);
            var half = HaloRed(_host.Root.Q<VisualElement>("caster"));

            // Assert — the renderer applies the caster's opacity to the textured quad exactly once, so the
            // halo tracks it linearly. An opacity-blind quad would read the same at both settings, and a
            // doubly-scaled one would read a quarter; the tolerance is wide enough for 8-bit rounding and far
            // narrower than either alternative. The first term is what makes a black frame fail.
            Assert.That(
                (opaque > SeparableHalo, Mathf.Abs(half - (opaque * 0.5f)) < opaque * 0.1f),
                Is.EqualTo((true, true)), $"opaque={opaque} half={half}");
        }
    }
}
