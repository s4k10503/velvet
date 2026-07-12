using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

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
        private GameObject _docGo;
        private PanelSettings _settings;
        private RenderTexture _panelRt;
        private MountedTree _mounted;
        private int _savedTargetFrameRate;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _savedTargetFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 120;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Application.targetFrameRate = _savedTargetFrameRate;
            _mounted?.Dispose();
            _mounted = null;
            if (_docGo != null) Object.Destroy(_docGo);
            if (_effectGo != null) Object.Destroy(_effectGo);
            if (_settings != null) Object.Destroy(_settings);
            if (_panelRt != null) { _panelRt.Release(); Object.Destroy(_panelRt); }
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
            _panelRt = new RenderTexture(300, 300, 32);
            _docGo = new GameObject("ParticlesPanel");
            var doc = _docGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.targetTexture = _panelRt;
            doc.panelSettings = _settings;
            _mounted = V.Mount(doc.rootVisualElement,
                V.Particles(effect, className: "w-[300px] h-[300px]", playOn: playOn));
        }

        // Counts red-dominant pixels in the panel's center region (bottom-origin irrelevant: centered).
        private int CountParticlePixels()
        {
            var prev = RenderTexture.active;
            RenderTexture.active = _panelRt;
            var tex = new Texture2D(100, 100, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(100, 100, 100, 100), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var count = 0;
            var pixels = tex.GetPixels32();
            foreach (var p in pixels)
            {
                if (p.r > 140 && p.g < 90 && p.b < 90) count++;
            }
            Object.Destroy(tex);
            return count;
        }

        private static IEnumerator WaitRealtime(double seconds)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }
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
            var element = _docGo.GetComponent<UIDocument>().rootVisualElement.Q<ParticlesElement>();
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
            _panelRt = new RenderTexture(300, 300, 32);
            _docGo = new GameObject("ParticlesPanel");
            var doc = _docGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.targetTexture = _panelRt;
            doc.panelSettings = _settings;
            _mounted = V.Mount(doc.rootVisualElement, V.Component(TriggerFlipHost, key: "root"));
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
            // Arrange — a keyed reorder re-inserts the element, which silently drops element-bound
            // scheduled items; the repaint driver must survive the move or the drawn output freezes.
            var effect = CreateEmitter();
            s_effect = effect;
            _panelRt = new RenderTexture(300, 300, 32);
            _docGo = new GameObject("ParticlesPanel");
            var doc = _docGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.targetTexture = _panelRt;
            doc.panelSettings = _settings;
            _mounted = V.Mount(doc.rootVisualElement, V.Component(ReorderHost, key: "root"));
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
            return V.Div(className: "flex-col", children: swapped
                ? new VNode[] { spacer, particles }
                : new VNode[] { particles, spacer });
        }

        private Color32[] Snapshot()
        {
            var prev = RenderTexture.active;
            RenderTexture.active = _panelRt;
            var tex = new Texture2D(300, 300, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 300, 300), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var pixels = tex.GetPixels32();
            Object.Destroy(tex);
            return pixels;
        }
    }
}
