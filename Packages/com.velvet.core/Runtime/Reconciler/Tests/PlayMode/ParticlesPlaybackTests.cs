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
    /// Pins that V.Particles actually DRAWS the live simulation on a real runtime panel: a
    /// mount-triggered emitter fills the element with particle-colored quads that move frame to
    /// frame, and a Manual trigger renders nothing until played.
    /// </summary>
    /// <remarks>
    /// The raised per-test budget covers a software-rasterizer quirk, not slow assertions: on a
    /// GPU-less runner the process's FIRST particle-drawing frames stall for tens of seconds each
    /// while the GL stack warms against the per-frame regenerated textured mesh, a one-time window
    /// of several minutes that expires on its own — identical draws are sub-second afterwards (and
    /// on real GPUs throughout). Each test here needs only a handful of frame boundaries, so it
    /// survives that window, but whichever drawing test runs first would blow the 3-minute default
    /// budget alone. Nothing about the verified behavior changes.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class ParticlesPlaybackTests
    {
        private GameObject _effectGo;
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
            _mounted?.Dispose();
            _mounted = null;
            if (_effectGo != null) Object.Destroy(_effectGo);
            _host?.Dispose();
            _host = null;
            yield return null;
        }

        // A dense, slow, large-particle red emitter so the element's center reliably carries color.
        private ParticleSystem CreateEmitter()
        {
            _effectGo = new GameObject("fx");
            var ps = _effectGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startColor = Color.red;
            main.startSize = 0.8f;
            main.startSpeed = 0.4f;
            main.startLifetime = 5f;
            main.maxParticles = 300;
            main.playOnAwake = false;
            var emission = ps.emission;
            emission.rateOverTime = 300f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        private void MountPanel(ParticleSystem effect, PlayTrigger playOn)
        {
            _host = new RenderTexturePanelHost("ParticlesPanel", 300, 300);
            _mounted = V.Mount(_host.Root,
                V.Particles(effect, className: "w-[300px] h-[300px]", playOn: playOn));
        }

        // Counts red-dominant pixels in the panel's center region (bottom-origin irrelevant: centered).
        private int CountParticlePixels()
        {
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(100, 100, 100, 100));
            var count = 0;
            foreach (var p in pixels)
            {
                if (RenderTexturePixelReader.IsRedPixel(p)) count++;
            }
            return count;
        }

        [UnityTest]
        public IEnumerator Given_AMountPlayTrigger_When_FramesAdvance_Then_ParticlesRenderInsideTheElement()
        {
            // Arrange
            var effect = CreateEmitter();

            // Act
            MountPanel(effect, PlayTrigger.Mount);
            yield return WaitRealtime(0.8);

            // Assert — the element's center carries particle-colored pixels.
            Assert.That(CountParticlePixels(), Is.GreaterThan(20));
        }

        [UnityTest]
        public IEnumerator Given_ALiveSimulation_When_FramesAdvance_Then_TheDrawnParticlesMove()
        {
            // Arrange
            var effect = CreateEmitter();
            MountPanel(effect, PlayTrigger.Mount);
            yield return WaitRealtime(0.6);
            var first = Snapshot();
            Assume.That(CountParticlePixels(), Is.GreaterThan(20), "Precondition: particles are visible");

            // Act — no Velvet re-render: the simulation alone must change the drawn output.
            yield return WaitRealtime(0.4);

            // Assert
            var second = Snapshot();
            var differing = 0;
            for (var i = 0; i < first.Length; i += 3)
            {
                if (Mathf.Abs(first[i].r - second[i].r) > 12) differing++;
            }
            Assert.That(differing, Is.GreaterThan(30));
        }

        // The internal bounds-spacer child, or null. Widening the caster's boundingBox (the size of the
        // filter's offscreen texture) is exactly what the spacer's layout rect does, so its resolved size
        // extending past the host rect is the public proxy for "the filtered quads are no longer clipped".
        private static VisualElement FindBoundsSpacer(VisualElement host)
        {
            for (var i = 0; i < host.childCount; i++)
            {
                if (SilhouetteBoundsSpacer.IsSpacer(host[i]))
                {
                    return host[i];
                }
            }
            return null;
        }

        [UnityTest]
        public IEnumerator Given_AFilteredLiveSimulation_When_FramesAdvance_Then_TheReservedBoundsCoverTheOverflow()
        {
            // Arrange — a filter renders the element through an offscreen tree sized to its layout box, so the
            // quads drawn past the small rect would clip. The fix tracks the live particle extent into a spacer
            // whose layout reaches beyond the host rect; a spacer merely pinned to the box would not.
            var effect = CreateEmitter();
            _host = new RenderTexturePanelHost("ParticlesPanel", 300, 300);
            _mounted = V.Mount(_host.Root,
                V.Particles(effect, className: "w-[40px] h-[40px] hue-rotate-90", playOn: PlayTrigger.Mount));
            var element = _host.Root.Q<ParticlesElement>();
            Assume.That(element, Is.Not.Null, "Precondition: the particles element mounted");

            // Act — let the burst populate and draw quads beyond the 40px box.
            yield return WaitRealtime(0.8);
            var spacer = FindBoundsSpacer(element);
            Assume.That(spacer, Is.Not.Null, "Precondition: the filtered element carries a bounds-spacer");

            // Assert
            Assert.That(spacer.layout.width, Is.GreaterThan(element.layout.width));
        }

        [UnityTest]
        public IEnumerator Given_AManualPlayTrigger_When_FramesAdvance_Then_NothingRenders()
        {
            // Arrange
            var effect = CreateEmitter();

            // Act — Manual instantiates the host stopped; nothing should emit or draw.
            MountPanel(effect, PlayTrigger.Manual);
            yield return WaitRealtime(0.6);

            // Assert
            Assert.That(CountParticlePixels(), Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator Given_AManualPlayTrigger_When_PlayIsCalledOnTheElement_Then_ParticlesRender()
        {
            // Arrange — Manual's other half: the element exposes the imperative trigger.
            var effect = CreateEmitter();
            MountPanel(effect, PlayTrigger.Manual);
            yield return WaitRealtime(0.3);
            Assume.That(CountParticlePixels(), Is.EqualTo(0), "Precondition: nothing draws before Play");
            var element = _host.Root.Q<ParticlesElement>();
            Assume.That(element, Is.Not.Null, "Precondition: the particles element mounted");

            // Act
            element.Play();
            yield return WaitRealtime(0.8);

            // Assert
            Assert.That(CountParticlePixels(), Is.GreaterThan(20));
        }

        [UnityTest]
        public IEnumerator Given_APlayTriggerFlipToMount_When_Repatched_Then_ParticlesStart()
        {
            // Arrange — flipping playOn on an UNCHANGED effect must start the host; gating the play
            // state on effect identity alone would make the flip a silent no-op.
            var effect = CreateEmitter();
            s_effect = effect;
            _host = new RenderTexturePanelHost("ParticlesPanel", 300, 300);
            _mounted = V.Mount(_host.Root, V.Component(TriggerFlipHost, key: "root"));
            yield return WaitRealtime(0.3);
            Assume.That(CountParticlePixels(), Is.EqualTo(0), "Precondition: Manual draws nothing");

            // Act
            s_setFlag.Invoke(true);
            yield return WaitRealtime(0.8);

            // Assert
            Assert.That(CountParticlePixels(), Is.GreaterThan(20));
        }

        [UnityTest]
        public IEnumerator Given_AKeyedReorder_When_TheElementMoves_Then_TheDrawnParticlesKeepMoving()
        {
            // Arrange — a keyed reorder re-inserts the element. A recurring tick survives that
            // detach/re-attach on its own (UI Toolkit pauses and reschedules it), but this pins the
            // OBSERVABLE contract directly: the repaint driver must keep advancing across the move, or
            // the drawn output freezes.
            var effect = CreateEmitter();
            s_effect = effect;
            _host = new RenderTexturePanelHost("ParticlesPanel", 300, 300);
            _mounted = V.Mount(_host.Root, V.Component(ReorderHost, key: "root"));
            yield return WaitRealtime(0.5);
            Assume.That(CountParticlePixels(), Is.GreaterThan(20), "Precondition: particles are visible");

            // Act — swap the keyed siblings, then let the simulation advance.
            s_setFlag.Invoke(true);
            yield return WaitRealtime(0.3);
            var first = Snapshot();
            yield return WaitRealtime(0.4);

            // Assert — the drawn output still changes after the move (the tick survived the reorder).
            var second = Snapshot();
            var differing = 0;
            for (var i = 0; i < first.Length; i += 3)
            {
                if (Mathf.Abs(first[i].r - second[i].r) > 12) differing++;
            }
            Assert.That(differing, Is.GreaterThan(30));
        }

        private static ParticleSystem s_effect;
        private static StateUpdater<bool> s_setFlag;

        [Component]
        private static VNode TriggerFlipHost()
        {
            var (mount, setMount) = Hooks.UseState(false);
            s_setFlag = setMount;
            return V.Particles(s_effect, className: "w-[300px] h-[300px]",
                playOn: mount ? PlayTrigger.Mount : PlayTrigger.Manual);
        }

        [Component]
        private static VNode ReorderHost()
        {
            var (swapped, setSwapped) = Hooks.UseState(false);
            s_setFlag = setSwapped;
            var particles = V.Particles(s_effect, key: "px", className: "w-[300px] h-[300px]");
            var spacer = V.Div(key: "sp", className: "w-[1px] h-[1px]");
            // The keyed diff's LIS-based placement leaves whichever element is first in the OLD order
            // as the anchor that is never actually detached — "particles" starts second and moves to
            // first so this reorder genuinely detaches/re-attaches its repaint tick's host.
            return V.Div(className: "flex-col", children: swapped
                ? new VNode[] { particles, spacer }
                : new VNode[] { spacer, particles });
        }

        private Color32[] Snapshot()
        {
            return RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, 300, 300));
        }
    }
}
