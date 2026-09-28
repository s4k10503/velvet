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
    /// frame, and a Manual trigger renders nothing until played. Every simulation space draws at the
    /// element, child systems draw beside the root unless their renderer is off, and a system denser
    /// than one mesh allocation draws every particle.
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
        private GameObject _customSpaceGo;
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
            if (_customSpaceGo != null) Object.Destroy(_customSpaceGo);
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
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert — the element's center carries particle-colored pixels.
            Assert.That(CountParticlePixels(), Is.GreaterThan(20));
        }

        [UnityTest]
        public IEnumerator Given_ALiveSimulation_When_FramesAdvance_Then_TheDrawnParticlesMove()
        {
            // Arrange
            var effect = CreateEmitter();
            MountPanel(effect, PlayTrigger.Mount);
            yield return WaitRealtimeDraining(0.6, _host.TargetTexture);
            var first = Snapshot();
            Assume.That(CountParticlePixels(), Is.GreaterThan(20), "Precondition: particles are visible");

            // Act — no Velvet re-render: the simulation alone must change the drawn output.
            yield return WaitRealtimeDraining(0.4, _host.TargetTexture);

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
            // that reaches beyond the host rect; a spacer merely pinned to the box would not.
            var effect = CreateEmitter();
            _host = new RenderTexturePanelHost("ParticlesPanel", 300, 300);
            _mounted = V.Mount(_host.Root,
                V.Particles(effect, className: "w-[40px] h-[40px] hue-rotate-90", playOn: PlayTrigger.Mount));
            var element = _host.Root.Q<ParticlesElement>();
            Assume.That(element, Is.Not.Null, "Precondition: the particles element mounted");

            // Act — the spacer is sized by the tick reading the extent Draw stashed a frame earlier, a
            // multi-frame chain (draw → stash → tick → size). Advance until it settles rather than a fixed
            // realtime wait, so the GPU-less runner's one-time multi-second draw warmup can't starve the chain.
            var reached = false;
            var deadline = Time.realtimeSinceStartupAsDouble + 180.0;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                var spacer = FindBoundsSpacer(element);
                if (spacer != null && spacer.resolvedStyle.width > element.resolvedStyle.width)
                {
                    reached = true;
                    break;
                }
            }

            // Assert
            Assert.That(reached, Is.True);
        }

        [UnityTest]
        public IEnumerator Given_AManualPlayTrigger_When_FramesAdvance_Then_NothingRenders()
        {
            // Arrange
            var effect = CreateEmitter();

            // Act — Manual instantiates the host stopped; nothing should emit or draw.
            MountPanel(effect, PlayTrigger.Manual);
            yield return WaitRealtimeDraining(0.6, _host.TargetTexture);

            // Assert
            Assert.That(CountParticlePixels(), Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator Given_AManualPlayTrigger_When_PlayIsCalledOnTheElement_Then_ParticlesRender()
        {
            // Arrange — Manual's other half: the element exposes the imperative trigger.
            var effect = CreateEmitter();
            MountPanel(effect, PlayTrigger.Manual);
            yield return WaitRealtimeDraining(0.3, _host.TargetTexture);
            Assume.That(CountParticlePixels(), Is.EqualTo(0), "Precondition: nothing draws before Play");
            var element = _host.Root.Q<ParticlesElement>();
            Assume.That(element, Is.Not.Null, "Precondition: the particles element mounted");

            // Act
            element.Play();
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

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
            yield return WaitRealtimeDraining(0.3, _host.TargetTexture);
            Assume.That(CountParticlePixels(), Is.EqualTo(0), "Precondition: Manual draws nothing");

            // Act
            s_setFlag.Invoke(true);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

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
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
            Assume.That(CountParticlePixels(), Is.GreaterThan(20), "Precondition: particles are visible");

            // Act — swap the keyed siblings, then let the simulation advance.
            s_setFlag.Invoke(true);
            yield return WaitRealtimeDraining(0.3, _host.TargetTexture);
            var first = Snapshot();
            yield return WaitRealtimeDraining(0.4, _host.TargetTexture);

            // Assert — the drawn output still changes after the move (the tick survived the reorder).
            var second = Snapshot();
            var differing = 0;
            for (var i = 0; i < first.Length; i += 3)
            {
                if (Mathf.Abs(first[i].r - second[i].r) > 12) differing++;
            }
            Assert.That(differing, Is.GreaterThan(30));
        }

        [UnityTest]
        public IEnumerator Given_AWorldSpaceSource_When_FramesAdvance_Then_ParticlesRenderInsideTheElement()
        {
            // Arrange — the hidden host is parked far from the origin, so a world-space position read
            // as if it were local lands far outside the element.
            var effect = CreateEmitter();
            var main = effect.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Act
            MountPanel(effect, PlayTrigger.Mount);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert
            Assert.That(CountParticlePixels(), Is.GreaterThan(20));
        }

        [UnityTest]
        public IEnumerator Given_ACustomSpaceSource_When_FramesAdvance_Then_ParticlesRenderInsideTheElement()
        {
            // Arrange — the custom frame is offset from the origin, so positions read as world space
            // instead of through that frame land five units (500px) off the element's center.
            var effect = CreateEmitter();
            _customSpaceGo = new GameObject("fx-custom-space");
            _customSpaceGo.transform.position = new Vector3(5f, 0f, 0f);
            var main = effect.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = _customSpaceGo.transform;

            // Act
            MountPanel(effect, PlayTrigger.Mount);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert
            Assert.That(CountParticlePixels(), Is.GreaterThan(20));
        }

        [UnityTest]
        public IEnumerator Given_AChildSystem_When_FramesAdvance_Then_ItsParticlesRender()
        {
            // Arrange — the root emits nothing; only the child carries color.
            var child = CreateEmitter();
            var root = new GameObject("fx-root").AddComponent<ParticleSystem>();
            var rootEmission = root.emission;
            rootEmission.enabled = false;
            child.transform.SetParent(root.transform);
            _effectGo = root.gameObject;

            // Act
            MountPanel(root, PlayTrigger.Mount);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert
            Assert.That(CountParticlePixels(), Is.GreaterThan(20));
        }

        // GREEN_ON_BASE(characterization): a child whose renderer is off draws nothing, as the scene would show.
        [UnityTest]
        public IEnumerator Given_AChildSystemWithItsRendererOff_When_FramesAdvance_Then_NothingRenders()
        {
            // Arrange — the same emitting child as above, with its renderer disabled in the source.
            var child = CreateEmitter();
            child.GetComponent<ParticleSystemRenderer>().enabled = false;
            var root = new GameObject("fx-root").AddComponent<ParticleSystem>();
            var rootEmission = root.emission;
            rootEmission.enabled = false;
            child.transform.SetParent(root.transform);
            _effectGo = root.gameObject;

            // Act
            MountPanel(root, PlayTrigger.Mount);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert
            Assert.That(CountParticlePixels(), Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator Given_AFinishedRootWithALiveChild_When_FramesAdvance_Then_TheRepaintTickKeepsRunning()
        {
            // Arrange — the root's one short burst is over within a fraction of a second; the looping
            // child keeps emitting, and the element draws it.
            var child = CreateEmitter();
            var root = new GameObject("fx-root").AddComponent<ParticleSystem>();
            // A system plays from the moment it is added in Play Mode, and its duration is set only while stopped.
            root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var rootMain = root.main;
            rootMain.loop = false;
            rootMain.duration = 0.05f;
            rootMain.startLifetime = 0.05f;
            rootMain.playOnAwake = false;
            child.transform.SetParent(root.transform);
            _effectGo = root.gameObject;
            MountPanel(root, PlayTrigger.Mount);
            var element = _host.Root.Q<ParticlesElement>();
            Assume.That(element, Is.Not.Null, "Precondition: the particles element mounted");

            // Act
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.ParticlesBindings[element].RepaintTick, Is.Not.Null);
        }

        // A root that emits nothing over the given children, in the order given.
        private ParticleSystem CreateSilentRootOver(params ParticleSystem[] children)
        {
            var root = new GameObject("fx-root").AddComponent<ParticleSystem>();
            var rootEmission = root.emission;
            rootEmission.enabled = false;
            foreach (var child in children)
            {
                child.transform.SetParent(root.transform, false);
            }
            _effectGo = root.gameObject;
            return root;
        }

        [UnityTest]
        public IEnumerator Given_AChildThatDestroysItselfWhenDone_When_FramesAdvance_Then_TheOtherChildKeepsRendering()
        {
            // Arrange — the first child's one short burst ends and its stop action destroys it while
            // the red child after it keeps emitting.
            var finishing = new GameObject("fx-finishing").AddComponent<ParticleSystem>();
            finishing.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var finishingMain = finishing.main;
            finishingMain.loop = false;
            finishingMain.duration = 0.1f;
            finishingMain.startLifetime = 0.1f;
            finishingMain.stopAction = ParticleSystemStopAction.Destroy;
            var red = CreateEmitter();
            var root = CreateSilentRootOver(finishing, red);

            // Act
            MountPanel(root, PlayTrigger.Mount);
            yield return WaitRealtimeDraining(1.5, _host.TargetTexture);

            // Assert
            Assert.That(CountParticlePixels(), Is.GreaterThan(20));
        }

        // GREEN_ON_BASE(characterization): a child set to render nothing draws nothing, as the scene would show.
        [UnityTest]
        public IEnumerator Given_AChildRenderingInModeNone_When_FramesAdvance_Then_NothingRenders()
        {
            // Arrange — an emitting child whose renderer is on but renders no particles.
            var child = CreateEmitter();
            child.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.None;
            var root = CreateSilentRootOver(child);

            // Act
            MountPanel(root, PlayTrigger.Mount);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert
            Assert.That(CountParticlePixels(), Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator Given_ALocalSpaceChildAtAnOffset_When_FramesAdvance_Then_ItsParticlesRenderAtTheOffset()
        {
            // Arrange — 1.2 units right of the root is 120px right of the element's center. Drawn at the
            // center instead, no particle reaches the 50px strip at the element's right edge.
            var child = CreateEmitter();
            var root = CreateSilentRootOver(child);
            child.transform.localPosition = new Vector3(1.2f, 0f, 0f);

            // Act
            MountPanel(root, PlayTrigger.Mount);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert
            var red = 0;
            foreach (var p in RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(250, 100, 50, 100)))
            {
                if (RenderTexturePixelReader.IsRedPixel(p)) red++;
            }
            Assert.That(red, Is.GreaterThan(20));
        }

        // GREEN_ON_BASE(characterization): a finished root over a child that never emits parks the tick, as it did.
        [UnityTest]
        public IEnumerator Given_AFinishedRootWithAnEmptyLoopingChild_When_FramesAdvance_Then_TheRepaintTickParks()
        {
            // Arrange — the child loops with its emission disabled: playing, and never holding a particle.
            var child = new GameObject("fx-empty-child").AddComponent<ParticleSystem>();
            var childEmission = child.emission;
            childEmission.enabled = false;
            var root = new GameObject("fx-root").AddComponent<ParticleSystem>();
            root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var rootMain = root.main;
            rootMain.loop = false;
            rootMain.duration = 0.05f;
            rootMain.startLifetime = 0.05f;
            rootMain.playOnAwake = false;
            child.transform.SetParent(root.transform, false);
            _effectGo = root.gameObject;
            MountPanel(root, PlayTrigger.Mount);
            var element = _host.Root.Q<ParticlesElement>();
            Assume.That(element, Is.Not.Null, "Precondition: the particles element mounted");

            // Act
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.ParticlesBindings[element].RepaintTick, Is.Null);
        }

        [UnityTest]
        public IEnumerator Given_MoreParticlesThanOneAllocationHolds_When_Drawn_Then_EveryParticleRenders()
        {
            // Arrange — 3000 red 4px squares at a 5px pitch, emitted directly into the host at two pixels
            // per unit, filling a 300x250px block in the top-left of a 400px element: 48000 red pixels
            // there if every particle draws, at most 32768 if only the first 2048 do. The gaps and the
            // off-center block keep a wrong size or a mirrored axis from filling the same count.
            _effectGo = new GameObject("fx-dense");
            var effect = _effectGo.AddComponent<ParticleSystem>();
            var main = effect.main;
            main.maxParticles = 3000;
            main.playOnAwake = false;
            var emission = effect.emission;
            emission.enabled = false;
            var shape = effect.shape;
            shape.enabled = false;
            effect.GetComponent<ParticleSystemRenderer>().sharedMaterial = null;
            _host = new RenderTexturePanelHost("ParticlesPanel", 400, 400);
            _mounted = V.Mount(_host.Root,
                V.Particles(effect, className: "w-[400px] h-[400px]", pixelsPerUnit: 2f));
            var element = _host.Root.Q<ParticlesElement>();
            Assume.That(element, Is.Not.Null, "Precondition: the particles element mounted");
            var host = _mounted.Root.Reconciler.Context.ParticlesBindings[element].Host;
            for (var column = 0; column < 60; column++)
            {
                for (var row = 0; row < 50; row++)
                {
                    var emit = new ParticleSystem.EmitParams
                    {
                        position = new Vector3(((5f * column) + 2f - 200f) / 2f, (200f - (5f * row) - 2f) / 2f, 0f),
                        velocity = Vector3.zero,
                        startSize = 2f,
                        startLifetime = 100f,
                        startColor = Color.red,
                    };
                    host.Emit(emit, 1);
                }
            }

            // Act
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert — ReadPixels is bottom-origin, so the block's 250 rows are the texture's top 250.
            var red = 0;
            foreach (var p in RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 150, 300, 250)))
            {
                if (RenderTexturePixelReader.IsRedPixel(p)) red++;
            }
            Assert.That(red, Is.InRange(40000, 56000));
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
