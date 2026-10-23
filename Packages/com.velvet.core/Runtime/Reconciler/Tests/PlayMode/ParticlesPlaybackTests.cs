using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.VFX;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that V.Particles actually DRAWS the live simulation on a real runtime panel: a
    /// mount-triggered emitter fills the element with particle-colored quads that move frame to
    /// frame, and a Manual trigger renders nothing until played. Every simulation space draws at the
    /// element, child systems draw beside the root unless their renderer is off, and a system denser
    /// than one mesh allocation draws every particle. What a renderer builds is what draws: meshes
    /// culled as their material says, stretched billboards, texture-sheet frames, trails with their own
    /// material, and particles no larger than the renderer lets them be against the element. Materials
    /// tint and blend what they draw, and systems draw in their renderers' sorting order. The host
    /// stands where the source does, so collision and trigger sub-emitters fire against the source's
    /// colliders. A VisualEffect is shown through the texture a camera renders of it.
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
        private readonly List<UnityEngine.Object> _owned = new();
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
            if (_effectGo != null) UnityEngine.Object.Destroy(_effectGo);
            if (_customSpaceGo != null) UnityEngine.Object.Destroy(_customSpaceGo);
            foreach (var owned in _owned)
            {
                if (owned != null) UnityEngine.Object.Destroy(owned);
            }
            _owned.Clear();
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

        // GREEN_ON_BASE(characterization): a world-space source away from the origin draws at the element, as it did.
        [UnityTest]
        public IEnumerator Given_AWorldSpaceSource_When_FramesAdvance_Then_ParticlesRenderInsideTheElement()
        {
            // Arrange — the source stands five units from the origin, so a world-space position read as if
            // it were local lands 500px outside the element.
            var effect = CreateEmitter();
            effect.transform.position = new Vector3(5f, 0f, 0f);
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

        [UnityTest]
        public IEnumerator Given_ADenseSystemThatShrinks_When_Drawn_Then_OnlyTheParticlesStillAliveRender()
        {
            // Arrange — 4000 red 4px squares at a 5px pitch, two pixels per unit, filling a 400x250px
            // block; after they have drawn, all but the first 3000 (the left 300px) are ended by a
            // negative remaining lifetime. The buffer the draw reads keeps the ended ones past its live
            // count.
            _effectGo = new GameObject("fx-shrinking");
            var effect = _effectGo.AddComponent<ParticleSystem>();
            var main = effect.main;
            main.maxParticles = 4000;
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
            // The host's panel leaves its target uncleared between frames, so without this the ended
            // particles' pixels from the frames before the write would still read red.
            ((IRuntimePanel)_host.Root.panel).panelSettings.clearColor = true;
            var host = _mounted.Root.Reconciler.Context.ParticlesBindings[element].Host;
            for (var column = 0; column < 80; column++)
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
            yield return WaitRealtimeDraining(0.3, _host.TargetTexture);
            var particles = new ParticleSystem.Particle[4000];
            host.GetParticles(particles);
            for (var ended = 3000; ended < 4000; ended++)
            {
                particles[ended].remainingLifetime = -1f;
            }

            // Act
            host.SetParticles(particles, 4000);
            yield return WaitRealtimeDraining(0.3, _host.TargetTexture);

            // Assert — ReadPixels is bottom-origin, so the block's 250 rows are the texture's top 250.
            Assert.That((CountRed(new RectInt(0, 150, 300, 250)) >= 40000, CountRed(new RectInt(300, 150, 100, 250))),
                Is.EqualTo((true, 0)));
        }

        private T Own<T>(T owned) where T : UnityEngine.Object
        {
            _owned.Add(owned);
            return owned;
        }

        private static bool IsBlue(Color32 p) => p.b > 140 && p.r < 90 && p.g < 90;

        private static bool IsGreen(Color32 p) => p.g > 140 && p.r < 90 && p.b < 90;

        private int CountWhere(RectInt region, Func<Color32, bool> match)
        {
            var count = 0;
            foreach (var p in RenderTexturePixelReader.ReadPixels(_host.TargetTexture, region))
            {
                if (match(p)) count++;
            }
            return count;
        }

        // A point-filtered texture whose left half is one color and right half another.
        private Texture2D TwoTone(Color left, Color right)
        {
            var texture = Own(new Texture2D(64, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point });
            var pixels = new Color[64 * 32];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = i % 64 < 32 ? left : right;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // A particle material whose shader declares _Cull, culling as cull says.
        private Material ParticleMaterial(Texture texture, CullMode cull)
        {
            var material = Own(new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")));
            material.mainTexture = texture;
            material.SetFloat("_Cull", (float)cull);
            return material;
        }

        // A system that emits nothing on its own; the case emits into its host.
        private ParticleSystem CreateQuietSystem(string name)
        {
            var system = new GameObject(name).AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.startLifetime = 100f;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            return system;
        }

        private static void EmitAt(ParticleSystem system, Vector3 position, Vector3 velocity, float size, Color color)
        {
            system.Emit(new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = 100f,
                startColor = color,
            }, 1);
        }

        private ParticleSystem MountedHost() =>
            _mounted.Root.Reconciler.Context.ParticlesBindings[_host.Root.Q<ParticlesElement>()].Host;

        // Two triangles left of the origin, a unit tall each: the upper one's corners turn clockwise in the
        // xy plane seen from -z, the lower one's counterclockwise.
        private Mesh UpperFrontLowerBackMesh()
        {
            var mesh = Own(new Mesh());
            mesh.vertices = new[]
            {
                new Vector3(-1f, 1f, 0f), new Vector3(-0.2f, 1f, 0f), new Vector3(-1f, 0.2f, 0f),
                new Vector3(-1f, -1f, 0f), new Vector3(-0.2f, -1f, 0f), new Vector3(-1f, -0.2f, 0f),
            };
            mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
            return mesh;
        }

        // Mounts one red mesh particle at the element's center, drawn with a material culling as cull says.
        private IEnumerator MountMeshParticle(CullMode cull)
        {
            var effect = CreateQuietSystem("fx-mesh");
            _effectGo = effect.gameObject;
            var renderer = effect.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.mesh = UpperFrontLowerBackMesh();
            renderer.sharedMaterial = ParticleMaterial(null, cull);
            MountPanel(effect, PlayTrigger.Mount);
            EmitAt(MountedHost(), Vector3.zero, Vector3.zero, 1f, Color.red);
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
        }

        // The upper triangle spans 50..130px from the element's top-left corner, the lower one 170..250px
        // down, both 50..130px across; ReadPixels is bottom-origin.
        private int UpperTriangleRed() => CountRed(new RectInt(55, 220, 25, 25));

        private int LowerTriangleRed() => CountRed(new RectInt(55, 55, 25, 25));

        [UnityTest]
        public IEnumerator Given_AMeshParticleUnderABackCullingMaterial_When_Drawn_Then_OnlyItsFrontFaceRenders()
        {
            // Arrange & Act
            yield return MountMeshParticle(CullMode.Back);

            // Assert — a billboard in the mesh's place would fill neither triangle's corner.
            Assert.That((UpperTriangleRed() > 300, LowerTriangleRed()), Is.EqualTo((true, 0)));
        }

        [UnityTest]
        public IEnumerator Given_AMeshParticleUnderAFrontCullingMaterial_When_Drawn_Then_OnlyItsBackFaceRenders()
        {
            // Arrange & Act
            yield return MountMeshParticle(CullMode.Front);

            // Assert
            Assert.That((UpperTriangleRed(), LowerTriangleRed() > 300), Is.EqualTo((0, true)));
        }

        [UnityTest]
        public IEnumerator Given_AMeshParticleUnderANonCullingMaterial_When_Drawn_Then_BothFacesAndNothingElseRender()
        {
            // Arrange & Act
            yield return MountMeshParticle(CullMode.Off);

            // Assert — the right half holds no triangle, and the 100px billboard of a unit-sized particle
            // would reach into it.
            Assert.That((UpperTriangleRed() > 300, LowerTriangleRed() > 300, CountRed(new RectInt(180, 20, 100, 260))),
                Is.EqualTo((true, true, 0)));
        }

        [UnityTest]
        public IEnumerator Given_AMeshModeRendererWithNoMesh_When_Drawn_Then_ItDrawsNothingBesideADrawingChild()
        {
            // Arrange — the root renders meshes and has none; its billboard child, a unit left of it, draws.
            var root = CreateQuietSystem("fx-meshless");
            _effectGo = root.gameObject;
            var rootRenderer = root.GetComponent<ParticleSystemRenderer>();
            rootRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            rootRenderer.mesh = null;
            var child = CreateQuietSystem("fx-meshless-child");
            child.transform.SetParent(root.transform, false);
            child.GetComponent<ParticleSystemRenderer>().sharedMaterial = null;
            MountPanel(root, PlayTrigger.Mount);
            var host = MountedHost();
            EmitAt(host, Vector3.zero, Vector3.zero, 0.5f, Color.red);
            EmitAt(host.GetComponentsInChildren<ParticleSystem>()[1], new Vector3(-1f, 0f, 0f), Vector3.zero, 0.5f, Color.red);

            // Act
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert
            Assert.That((CountRed(new RectInt(35, 140, 30, 20)) > 200, CountRed(new RectInt(135, 135, 30, 30))),
                Is.EqualTo((true, 0)));
        }

        [UnityTest]
        public IEnumerator Given_AStretchedBillboard_When_Drawn_Then_ItReachesAlongItsVelocity()
        {
            // Arrange — a 20px particle drifting right, stretched to five times its size: a unit, 100px,
            // along x. Unstretched, it reaches 10px either side of its center and neither strip below.
            var effect = CreateQuietSystem("fx-stretch");
            _effectGo = effect.gameObject;
            var renderer = effect.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 5f;
            renderer.velocityScale = 0f;
            renderer.sharedMaterial = null;
            MountPanel(effect, PlayTrigger.Mount);
            EmitAt(MountedHost(), Vector3.zero, new Vector3(0.05f, 0f, 0f), 0.2f, Color.red);

            // Act
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert
            Assert.That(CountRed(new RectInt(105, 146, 30, 8)) + CountRed(new RectInt(170, 146, 30, 8)),
                Is.GreaterThan(150));
        }

        [UnityTest]
        public IEnumerator Given_AGridTextureSheet_When_Drawn_Then_TheParticleShowsOneFrame()
        {
            // Arrange — a two-frame sheet, red then blue, held on its first frame. The sheet's sprite list
            // carries a green sprite the grid does not read.
            var effect = CreateQuietSystem("fx-grid-sheet");
            _effectGo = effect.gameObject;
            var sheet = effect.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 2;
            sheet.numTilesY = 1;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            var green = TwoTone(Color.green, Color.green);
            sheet.AddSprite(Own(Sprite.Create(green, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f))));
            effect.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMaterial(TwoTone(Color.red, Color.blue), CullMode.Off);
            MountPanel(effect, PlayTrigger.Mount);
            EmitAt(MountedHost(), Vector3.zero, Vector3.zero, 1f, Color.white);

            // Act
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert — the whole sheet across the 100px particle would put blue in its right half.
            var inner = new RectInt(110, 110, 80, 80);
            Assert.That((CountRed(inner) > 1000, CountWhere(inner, IsBlue)), Is.EqualTo((true, 0)));
        }

        [UnityTest]
        public IEnumerator Given_ASpritesTextureSheet_When_Drawn_Then_TheParticleShowsTheSpriteNotTheMaterial()
        {
            // Arrange — two sprites cut from one red-and-blue texture, held on the red one, over a green
            // material texture.
            var effect = CreateQuietSystem("fx-sprite-sheet");
            _effectGo = effect.gameObject;
            var atlas = TwoTone(Color.red, Color.blue);
            var sheet = effect.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Sprites;
            sheet.AddSprite(Own(Sprite.Create(atlas, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f))));
            sheet.AddSprite(Own(Sprite.Create(atlas, new Rect(32f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f))));
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            effect.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMaterial(TwoTone(Color.green, Color.green), CullMode.Off);
            MountPanel(effect, PlayTrigger.Mount);
            EmitAt(MountedHost(), Vector3.zero, Vector3.zero, 1f, Color.white);

            // Act
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert
            var inner = new RectInt(110, 110, 80, 80);
            Assert.That((CountRed(inner) > 1000, CountWhere(inner, IsGreen)), Is.EqualTo((true, 0)));
        }

        [UnityTest]
        public IEnumerator Given_ATrailedParticle_When_ItMoves_Then_ItsTrailRendersWithTheTrailMaterial()
        {
            // Arrange — a 5px untextured particle crossing from a unit left of center at two units a
            // second, leaving a 30px-wide trail whose material is blue and whose streams carry no color.
            var effect = CreateQuietSystem("fx-trail");
            _effectGo = effect.gameObject;
            var trails = effect.trails;
            trails.enabled = true;
            trails.sizeAffectsWidth = false;
            trails.widthOverTrail = 0.3f;
            trails.minVertexDistance = 0.05f;
            var renderer = effect.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = null;
            var blue = TwoTone(Color.blue, Color.blue);
            renderer.trailMaterial = ParticleMaterial(blue, CullMode.Off);
            renderer.SetActiveTrailVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.UV,
            });
            MountPanel(effect, PlayTrigger.Mount);
            EmitAt(MountedHost(), new Vector3(-1f, 0f, 0f), new Vector3(2f, 0f, 0f), 0.05f, Color.white);

            // Act
            yield return WaitRealtimeDraining(0.6, _host.TargetTexture);

            // Assert — the strip the particle crossed in its first half unit.
            Assert.That(CountWhere(new RectInt(60, 145, 40, 10), IsBlue), Is.GreaterThan(200));
        }

        [UnityTest]
        public IEnumerator Given_ATrailedParticleRenderingInModeNone_When_ItMoves_Then_ItsTrailRenders()
        {
            // Arrange — the trailed particle above with its renderer drawing no particles, only trails.
            var effect = CreateQuietSystem("fx-trail-only");
            _effectGo = effect.gameObject;
            var trails = effect.trails;
            trails.enabled = true;
            trails.sizeAffectsWidth = false;
            trails.widthOverTrail = 0.3f;
            trails.minVertexDistance = 0.05f;
            var renderer = effect.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.None;
            renderer.trailMaterial = ParticleMaterial(TwoTone(Color.blue, Color.blue), CullMode.Off);
            MountPanel(effect, PlayTrigger.Mount);
            EmitAt(MountedHost(), new Vector3(-1f, 0f, 0f), new Vector3(2f, 0f, 0f), 0.05f, Color.white);

            // Act
            yield return WaitRealtimeDraining(0.6, _host.TargetTexture);

            // Assert
            Assert.That(CountWhere(new RectInt(60, 145, 40, 10), IsBlue), Is.GreaterThan(200));
        }

        // A unit quad drawn in one flat colour from both sides, destroyed with the fixture.
        private GameObject UnlitQuad(Color color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var material = Own(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Cull", (float)CullMode.Off);
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            return Own(quad);
        }

        // The 80px square inside the 100px billboard a unit-sized particle at the element's center draws.
        private static readonly RectInt Core = new(110, 110, 80, 80);

        private static bool IsYellow(Color32 p) => p.r > 140 && p.g > 140 && p.b < 90;

        // A system that draws one untextured particle of its own color at the element's center.
        private ParticleSystem CreateCenteredSystem(string name, Material material)
        {
            var system = CreateQuietSystem(name);
            system.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return system;
        }

        private ParticleSystem HostedChild(string name) => MountedHost().transform.Find(name).GetComponent<ParticleSystem>();

        [UnityTest]
        public IEnumerator Given_AWhiteParticleUnderABlueBaseColor_When_Drawn_Then_ItRendersBlue()
        {
            // Arrange
            var material = ParticleMaterial(null, CullMode.Off);
            material.SetColor("_BaseColor", Color.blue);
            var effect = CreateCenteredSystem("fx-tint", material);
            _effectGo = effect.gameObject;
            MountPanel(effect, PlayTrigger.Mount);
            EmitAt(MountedHost(), Vector3.zero, Vector3.zero, 1f, Color.white);

            // Act
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert
            Assert.That(CountWhere(Core, IsBlue), Is.GreaterThan(5000));
        }

        [UnityTest]
        public IEnumerator Given_ARedParticleUnderAnAdditiveMaterialOverAGreenElement_When_Drawn_Then_TheRedAddsToTheGreen()
        {
            // Arrange — the blend factors URP's Additive mode sets.
            var material = ParticleMaterial(null, CullMode.Off);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            var effect = CreateCenteredSystem("fx-additive", material);
            _effectGo = effect.gameObject;
            _host = new RenderTexturePanelHost("ParticlesPanel", 300, 300);
            _mounted = V.Mount(_host.Root, V.Particles(effect, className: "w-[300px] h-[300px] bg-[#00ff00]"));
            EmitAt(MountedHost(), Vector3.zero, Vector3.zero, 1f, Color.red);

            // Act
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert
            Assert.That(CountWhere(Core, IsYellow), Is.GreaterThan(5000));
        }

        // Mounts a red system first in the hierarchy and a blue one after it, each drawing a particle at the
        // element's center, after configure sets their renderers up.
        private IEnumerator MountRedOverBlue(Action<ParticleSystemRenderer, ParticleSystemRenderer> configure)
        {
            var red = CreateCenteredSystem("fx-red", ParticleMaterial(null, CullMode.Off));
            var blue = CreateCenteredSystem("fx-blue", ParticleMaterial(null, CullMode.Off));
            configure(red.GetComponent<ParticleSystemRenderer>(), blue.GetComponent<ParticleSystemRenderer>());
            MountPanel(CreateSilentRootOver(red, blue), PlayTrigger.Mount);
            EmitAt(HostedChild("fx-red"), Vector3.zero, Vector3.zero, 1f, Color.red);
            EmitAt(HostedChild("fx-blue"), Vector3.zero, Vector3.zero, 1f, Color.blue);
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
        }

        [UnityTest]
        public IEnumerator Given_AnEarlierSystemWithTheHigherOrderInLayer_When_Drawn_Then_ItDrawsOverTheLaterOne()
        {
            // Arrange & Act — drawn in hierarchy order, the blue system would cover the red one.
            yield return MountRedOverBlue((red, blue) => red.sortingOrder = 1);

            // Assert
            Assert.That(CountRed(Core), Is.GreaterThan(5000));
        }

        [UnityTest]
        public IEnumerator Given_ALaterSystemWithTheLargerSortingFudge_When_Drawn_Then_TheEarlierOneDrawsOverIt()
        {
            // Arrange & Act — both stand at the same distance from the camera, so only the fudge separates them.
            yield return MountRedOverBlue((red, blue) => blue.sortingFudge = 100f);

            // Assert
            Assert.That(CountRed(Core), Is.GreaterThan(5000));
        }

        [UnityTest]
        public IEnumerator Given_AnEarlierSystemOnAHigherSortingLayer_When_Drawn_Then_ItDrawsOverALaterOneHigherInItsLayer()
        {
            // Arrange & Act — by order in layer alone, the blue system would cover the red one.
            yield return MountRedOverBlue((red, blue) =>
            {
                red.sortingLayerName = "VelvetTestFront";
                blue.sortingOrder = 5;
            });

            // Assert
            Assert.That(CountRed(Core), Is.GreaterThan(5000));
        }

        [UnityTest]
        public IEnumerator Given_AnEarlierSystemWithTheLaterRenderQueue_When_Drawn_Then_ItDrawsOverTheLaterOne()
        {
            // Arrange & Act — both stand at the same distance in one sorting layer and order, so only the queue
            // separates them.
            yield return MountRedOverBlue((red, blue) => red.sharedMaterial.renderQueue = 3100);

            // Assert
            Assert.That(CountRed(Core), Is.GreaterThan(5000));
        }

        private static bool IsBlack(Color32 p) => p.r < 60 && p.g < 60 && p.b < 60;

        [UnityTest]
        public IEnumerator Given_ABlueParticleUnderAMultiplyMaterialOverAYellowElement_When_Drawn_Then_TheProductIsBlack()
        {
            // Arrange — the blend factors URP's Multiply mode sets; alpha-blended, the particle would show blue.
            var material = ParticleMaterial(null, CullMode.Off);
            material.SetFloat("_SrcBlend", (float)BlendMode.DstColor);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            var effect = CreateCenteredSystem("fx-multiply", material);
            _effectGo = effect.gameObject;
            _host = new RenderTexturePanelHost("ParticlesPanel", 300, 300);
            _mounted = V.Mount(_host.Root, V.Particles(effect, className: "w-[300px] h-[300px] bg-[#ffff00]"));
            EmitAt(MountedHost(), Vector3.zero, Vector3.zero, 1f, Color.blue);

            // Act
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert
            Assert.That(CountWhere(Core, IsBlack), Is.GreaterThan(5000));
        }

        // GREEN_ON_BASE(characterization): the base draws an additive particle as its own content, beneath a later sibling, as this keeps doing through a layer.
        [UnityTest]
        public IEnumerator Given_AnAdditiveParticleFirstDrawnUnderAMovingAncestor_When_ALaterSiblingCoversIt_Then_TheSiblingStaysOnTop()
        {
            // Arrange — an additive red particle at the element's centre, a blue square after it covering the centre,
            // and their container moved a pixel every frame from before the particle's first paint on.
            var material = ParticleMaterial(null, CullMode.Off);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            var effect = CreateCenteredSystem("fx-additive-moving", material);
            _effectGo = effect.gameObject;
            _host = new RenderTexturePanelHost("ParticlesPanel", 300, 300);
            _mounted = V.Mount(_host.Root, V.Div(name: "moving", className: "w-[300px] h-[300px]", children: new VNode[]
            {
                V.Particles(effect, className: "w-[300px] h-[300px]"),
                V.Div(className: "w-[100px] h-[100px] ml-[100px] mt-[-200px] bg-[#0000ff]"),
            }));
            var moving = _host.Root.Q<VisualElement>("moving");
            EmitAt(MountedHost(), Vector3.zero, Vector3.zero, 1f, Color.red);

            // Act
            for (var frame = 0; frame < 20; frame++)
            {
                moving.style.translate = new Translate(frame % 2, 0f);
                yield return null;
            }
            moving.style.translate = new Translate(0f, 0f);
            yield return WaitRealtimeDraining(0.3, _host.TargetTexture);

            // Assert — red added over the blue square would read magenta rather than blue.
            Assert.That(CountWhere(Core, IsBlue), Is.GreaterThan(5000));
        }

        // The Simple Loop template VFX Graph ships, copied beside this fixture so the package's own template is
        // not what the case depends on.
        private const string LoopEffectPath = "Packages/com.velvet.core/Runtime/Reconciler/Tests/PlayMode/VelvetTestLoop.vfx";

        private VisualEffect CreateLoopEffect()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("VFX Graph simulates in compute shaders, which this device does not run.");
            }
            _effectGo = new GameObject("fx-vfx-loop");
            var effect = _effectGo.AddComponent<VisualEffect>();
#if UNITY_EDITOR
            effect.visualEffectAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(LoopEffectPath);
#endif
            return effect;
        }

        private int NotGreen() => CountWhere(new RectInt(0, 0, 300, 300), p => !IsGreen(p));

        [UnityTest]
        public IEnumerator Given_AManualVisualEffectGraph_When_PlayedThenStopped_Then_ItDrawsOnlyWhilePlaying()
        {
            // Arrange
            var effect = CreateLoopEffect();
            var capability = "";
            yield return VfxGraphCapability.IgnoreIfCannotDraw(effect, reading => capability = reading);
            _host = new RenderTexturePanelHost("ParticlesPanel", 300, 300);
            _mounted = V.Mount(_host.Root,
                V.Particles(effect, className: "w-[300px] h-[300px] bg-[#00ff00]", playOn: PlayTrigger.Manual));
            var element = _host.Root.Q<ParticlesElement>();
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
            var beforePlay = NotGreen();

            // Act
            element.Play();
            yield return WaitRealtimeDraining(1.0, _host.TargetTexture);
            var playing = NotGreen();
            element.Stop();
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert
            Assert.That((beforePlay, playing > 50, NotGreen()), Is.EqualTo((0, true, 0)), capability);
        }

        [UnityTest]
        public IEnumerator Given_AVisualEffectWithAQuadUnderItAndASceneQuadInFront_When_FramesAdvance_Then_OnlyItsQuadRendersWithTheElementShowingAroundIt()
        {
            // Arrange — a VisualEffect with no graph, and a red unit quad under it standing in for what a graph draws;
            // the element's green background is what the texture must leave showing around the quad. A blue scene
            // quad on the Default layer stands between the effect and its camera, which renders the clone's layer
            // alone.
            _effectGo = new GameObject("fx-vfx");
            var effect = _effectGo.AddComponent<VisualEffect>();
            UnlitQuad(Color.red).transform.SetParent(_effectGo.transform, false);
            UnlitQuad(Color.blue).transform.position = new Vector3(0f, 0f, -1f);
            _host = new RenderTexturePanelHost("ParticlesPanel", 300, 300);
            _mounted = V.Mount(_host.Root, V.Particles(effect, className: "w-[300px] h-[300px] bg-[#00ff00]"));

            // Act
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert — the corner lies outside the 100px quad at the element's center.
            Assert.That((CountRed(Core) > 5000, CountWhere(new RectInt(10, 10, 60, 60), IsGreen) > 3000),
                Is.EqualTo((true, true)));
        }

        [UnityTest]
        public IEnumerator Given_AParticleLargerThanItsRendererAllows_When_Drawn_Then_ItIsClampedAgainstTheElement()
        {
            // Arrange — a 160px particle in a 200px element, under the renderer's default limit of half the
            // view: 100px, 50px either side of the center, so nothing reaches 60..75px right of it.
            var effect = CreateQuietSystem("fx-clamped");
            _effectGo = effect.gameObject;
            effect.GetComponent<ParticleSystemRenderer>().sharedMaterial = null;
            _host = new RenderTexturePanelHost("ParticlesPanel", 200, 200);
            _mounted = V.Mount(_host.Root, V.Particles(effect, className: "w-[200px] h-[200px]"));
            EmitAt(MountedHost(), Vector3.zero, Vector3.zero, 1.6f, Color.red);

            // Act
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert
            Assert.That((CountRed(new RectInt(90, 90, 20, 20)) > 300, CountRed(new RectInt(160, 90, 15, 20))),
                Is.EqualTo((true, 0)));
        }

        // GREEN_ON_BASE(characterization): a source turned a quarter about its up axis still draws its particles, as it did.
        [UnityTest]
        public IEnumerator Given_ASourceTurnedAboutItsUpAxis_When_FramesAdvance_Then_ParticlesRenderInsideTheElement()
        {
            // Arrange
            var effect = CreateEmitter();
            effect.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            // Act
            MountPanel(effect, PlayTrigger.Mount);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert
            Assert.That(CountParticlePixels(), Is.GreaterThan(20));
        }

        // GREEN_ON_BASE(characterization): a particle facing the camera's position a unit off center draws face-on, as it did.
        [UnityTest]
        public IEnumerator Given_AFacingAlignedParticleOffCenter_When_Drawn_Then_ItRendersFaceOn()
        {
            // Arrange — a 50px particle a unit, 100px, right of the element's center.
            var effect = CreateQuietSystem("fx-facing");
            _effectGo = effect.gameObject;
            var renderer = effect.GetComponent<ParticleSystemRenderer>();
            renderer.alignment = ParticleSystemRenderSpace.Facing;
            renderer.sharedMaterial = null;
            MountPanel(effect, PlayTrigger.Mount);
            EmitAt(MountedHost(), new Vector3(1f, 0f, 0f), Vector3.zero, 0.5f, Color.red);

            // Act
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert
            Assert.That(CountRed(new RectInt(235, 140, 30, 20)), Is.GreaterThan(400));
        }

        // GREEN_ON_BASE(characterization): a renderer whose custom vertex streams leave out color draws its particles' color, as it did.
        [UnityTest]
        public IEnumerator Given_ARendererStreamingNoColor_When_FramesAdvance_Then_ParticlesRenderInTheirColor()
        {
            // Arrange
            var effect = CreateEmitter();
            effect.GetComponent<ParticleSystemRenderer>().SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.UV,
            });

            // Act
            MountPanel(effect, PlayTrigger.Mount);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert
            Assert.That(CountParticlePixels(), Is.GreaterThan(20));
        }

        // A root whose particles fall from half a unit above the effect, and a blue burst sub-emitter under it
        // that fires as type says.
        private (ParticleSystem Root, ParticleSystem Burst) CreateFallingRootWithBurst(ParticleSystemSubEmitterType type)
        {
            var root = CreateQuietSystem("fx-falling");
            _effectGo = root.gameObject;
            root.GetComponent<ParticleSystemRenderer>().sharedMaterial = null;
            var burst = CreateQuietSystem("fx-burst");
            burst.transform.SetParent(root.transform, false);
            var burstMain = burst.main;
            burstMain.startColor = Color.blue;
            burstMain.startSize = 0.3f;
            burstMain.simulationSpace = ParticleSystemSimulationSpace.World;
            var burstEmission = burst.emission;
            burstEmission.enabled = true;
            burstEmission.rateOverTime = 0f;
            burstEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });
            burst.GetComponent<ParticleSystemRenderer>().sharedMaterial = null;
            var subEmitters = root.subEmitters;
            subEmitters.enabled = true;
            subEmitters.AddSubEmitter(burst, type, ParticleSystemSubEmitterProperties.InheritNothing);
            return (root, burst);
        }

        private void EmitFalling(ParticleSystem host)
        {
            for (var i = -2; i <= 2; i++)
            {
                EmitAt(host, new Vector3(0.2f * i, 0.5f, 0f), new Vector3(0f, -2f, 0f), 0.05f, Color.red);
            }
        }

        // The band 0.25..0.65 units below the effect, where the falling particles meet the colliders below.
        private int BlueBelowTheEffect() => CountWhere(new RectInt(60, 85, 180, 40), IsBlue);

        [UnityTest]
        public IEnumerator Given_ACollisionSubEmitter_When_TheParentsParticlesHitACollider_Then_ItsBurstRenders()
        {
            // Arrange — a floor collider half a unit below the effect, where the source stands.
            var (root, _) = CreateFallingRootWithBurst(ParticleSystemSubEmitterType.Collision);
            var collision = root.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            var floor = Own(new GameObject("fx-floor"));
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.AddComponent<BoxCollider>().size = new Vector3(10f, 0.2f, 10f);
            MountPanel(root, PlayTrigger.Mount);
            EmitFalling(MountedHost());

            // Act
            yield return WaitRealtimeDraining(1.0, _host.TargetTexture);

            // Assert
            Assert.That(BlueBelowTheEffect(), Is.GreaterThan(100));
        }

        [UnityTest]
        public IEnumerator Given_ATriggerSubEmitter_When_TheParentsParticlesEnterATriggerVolume_Then_ItsBurstRenders()
        {
            // Arrange — a sphere whose top the falling particles enter 0.4..0.55 units below the effect,
            // where the source stands.
            var (root, _) = CreateFallingRootWithBurst(ParticleSystemSubEmitterType.Trigger);
            var sphere = Own(new GameObject("fx-volume"));
            sphere.transform.position = new Vector3(0f, -1f, 0f);
            var volume = sphere.AddComponent<SphereCollider>();
            volume.radius = 0.6f;
            volume.isTrigger = true;
            var trigger = root.trigger;
            trigger.enabled = true;
            trigger.SetCollider(0, volume);
            trigger.enter = ParticleSystemOverlapAction.Callback;
            MountPanel(root, PlayTrigger.Mount);
            EmitFalling(MountedHost());

            // Act
            yield return WaitRealtimeDraining(1.0, _host.TargetTexture);

            // Assert
            Assert.That(BlueBelowTheEffect(), Is.GreaterThan(100));
        }

        private int CountRed(RectInt region)
        {
            var red = 0;
            foreach (var p in RenderTexturePixelReader.ReadPixels(_host.TargetTexture, region))
            {
                if (RenderTexturePixelReader.IsRedPixel(p)) red++;
            }
            return red;
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
