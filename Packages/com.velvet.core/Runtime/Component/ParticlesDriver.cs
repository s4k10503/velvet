#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one Particles element, keyed in
    // ReconcilerContext.ParticlesBindings by the element itself. Holds the current settings, the
    // hidden framework-owned simulation host cloned from the source effect (plus the source's
    // instance id, so a swap is detected even after the old source object dies), the particle read
    // buffer (allocated once per host), the draw texture resolved from the host's renderer material,
    // the registered callbacks (so they can be unregistered on detach), and the recurring repaint
    // tick so it can be paused whenever nothing simulates.
    internal sealed class ParticlesBinding
    {
        public ParticlesSettings Settings;
        public ParticleSystem? Host;
        // GetInstanceID of the source the current Host was cloned from — an id (not the object
        // reference) so a source destroyed after the clone still compares meaningfully.
        public int SourceId;
        // The play trigger last applied to the live Host, so a settings change applies a playOn flip
        // exactly once instead of re-triggering on every unrelated diff.
        public PlayTrigger AppliedPlayOn;
        public ParticleSystem.Particle[]? Buffer;
        public Texture? Texture;
        public Action<MeshGenerationContext>? OnGenerate;
        public EventCallback<AttachToPanelEvent>? OnAttach;
        public IVisualElementScheduledItem? RepaintTick;
        // Logical play state as the DRIVER last set it (Mount trigger, element Play/Stop): outside
        // Play Mode the engine never advances a system's clock, so the repaint tick steps the
        // simulation manually exactly while this is set — the native isPlaying flag cannot serve,
        // because ParticleSystem.Simulate itself flips the system to paused.
        public bool LogicallyPlaying;
        // Advisory warn-once flags, per mounted element: an unstable effect reference that rebuilds
        // its source every render must not repeat the advice per rebuild, while two elements whose
        // sources merely share a name stay independent problems. Dying with the binding, the flags
        // need no static registry and no domain-reload reset.
        public bool WarnedSimulationSpace;
        public bool WarnedDrawCap;
        // The host's own loop flag and duration, captured at clone time (the framework owns the
        // clone, so they cannot change underneath): the editor drained-probe reads them every tick
        // and each ParticleSystem property access is a native call.
        public bool HostLoops;
        public float HostDuration;

        public ParticlesBinding(ParticlesSettings settings)
        {
            Settings = settings;
        }
    }

    /// <summary>
    /// Drives a <see cref="ParticlesElement"/>'s display: the source effect is cloned into a hidden,
    /// framework-owned simulation host (renderer disabled — only the simulation is consumed, the
    /// source system is never mutated) and the live particles are drawn as textured quads in the
    /// element's own visual content — no camera, no world-space canvas, no render-pipeline coupling.
    /// </summary>
    internal static class ParticlesDriver
    {
        // The particle draw cap. A UI Toolkit mesh allocation is bounded by its vertex budget and each
        // particle costs 4 vertices, so an effect declaring an enormous maxParticles must not translate
        // into an unbounded per-frame allocation; 2048 quads (8192 vertices) stays well inside it.
        private const int MaxDrawnParticles = 2048;

        // Where the hidden host parks: far below any plausible scene content so a stray scene camera
        // (or the scene view) never composes the simulation twice even before the renderer disable
        // lands on unusual setups.
        private static readonly Vector3 HostParkingPosition = new(0f, -10000f, 0f);

        // Wires the particle painter onto the element and returns the binding; a non-null effect gets
        // its hidden host immediately (the simulation needs no panel or layout — only the DRAW does,
        // and generateVisualContent starts running once the element is on a panel).
        public static ParticlesBinding Attach(VisualElement element, ParticlesSettings settings)
        {
            var binding = new ParticlesBinding(settings);
            binding.OnGenerate = mgc => Draw(mgc, element, binding);
            // Append: the particles are the element's CONTENT, drawn over any backdrop paint — a
            // co-resident shadow / skew silhouette prepends its callback precisely so that content
            // registered later covers it.
            element.generateVisualContent += binding.OnGenerate;
            // A recurring item like this repaint tick survives a keyed reorder's detach/re-attach on
            // its own: UI Toolkit's own per-item attach/detach handling pauses it when the element
            // leaves the panel and reschedules the SAME item when it returns, with no meaningful
            // restart of the interval (unlike a one-shot item, whose delay genuinely restarts in full
            // on every re-attach). SyncRepaintTick already guards on RepaintTick == null, so re-attach
            // just confirms the tick is present rather than tearing it down and recreating it; Detach
            // (the true unmount path, not this transient per-attach event) still pauses and releases it.
            binding.OnAttach = _ => SyncRepaintTick(element, binding);
            element.RegisterCallback(binding.OnAttach);
            // The imperative half of PlayTrigger.Manual: Play/Stop on the element reach the live host
            // through these handlers, cleared again on detach.
            if (element is ParticlesElement particlesElement)
            {
                particlesElement.PlayHandler = () => PlayHost(element, binding);
                particlesElement.StopHandler = () => StopHost(binding);
            }
            Sync(element, binding);
            element.MarkDirtyRepaint();
            return binding;
        }

        // Applies changed settings to a live binding; Sync re-derives everything from current state.
        public static void Update(VisualElement element, ParticlesBinding binding, ParticlesSettings settings)
        {
            binding.Settings = settings;
            Sync(element, binding);
            element.MarkDirtyRepaint();
        }

        // Full teardown: clears the imperative handlers, unregisters the painter and the panel
        // callbacks, pauses the repaint tick, and destroys the hidden host.
        public static void Detach(VisualElement element, ParticlesBinding binding)
        {
            if (element is ParticlesElement particlesElement)
            {
                particlesElement.PlayHandler = null;
                particlesElement.StopHandler = null;
            }
            element.generateVisualContent -= binding.OnGenerate;
            if (binding.OnAttach != null)
            {
                element.UnregisterCallback(binding.OnAttach);
            }
            StopRepaintTick(binding);
            DestroyHost(binding);
            element.MarkDirtyRepaint();
        }

        // Re-derives the host from the CURRENT settings and host state — the one sync point shared by
        // attach and every settings change. NB the props diff gating Update compares the settings
        // RECORDS (value equality) while everything here compares through Unity's overloaded ==, and
        // the two disagree when one side is a DESTROYED object and the other is literal null (distinct
        // record values, both "null" to Unity) — so this never trusts the diff's verdict of WHAT
        // changed and re-checks from its own nulls.
        private static void Sync(VisualElement element, ParticlesBinding binding)
        {
            var effect = binding.Settings.Effect;
            if (effect == null)
            {
                // No (or a destroyed) source: the element goes inert. The host is an independent
                // clone, unaffected by the source's death, so it is destroyed explicitly either way.
                DestroyHost(binding);
            }
            else if (binding.Host == null || binding.SourceId != effect.GetInstanceID())
            {
                // A first effect, a swapped source, or a host killed underneath us (a scene unload —
                // Host reads as null then): rebuild from the current source.
                DestroyHost(binding);
                CreateHost(binding);
            }
            else if (binding.AppliedPlayOn != binding.Settings.PlayOn)
            {
                // The host survives (same live source) but the trigger flipped: apply it — gating the
                // play state on effect identity alone would make the flip a silent no-op. Applied
                // exactly once per flip so an unrelated settings change (a pixel-scale tweak) cannot
                // restart a manually played host.
                ApplyPlayTrigger(binding);
            }
            SyncRepaintTick(element, binding);
        }

        // Clones the source effect into the hidden simulation host: renderer disabled (no camera may
        // draw it — only GetParticles is consumed; sub-emitter renderers are not reached, out of
        // scope), hidden from the hierarchy and excluded from editor scene saves, parked far out of
        // scene content. The draw texture is the renderer material's main texture (a disabled renderer
        // keeps sharedMaterial readable).
        private static void CreateHost(ParticlesBinding binding)
        {
            var source = binding.Settings.Effect!;
            WarnOnceForSource(binding, source);

            var host = UnityEngine.Object.Instantiate(source);
            // Cloning preserves activeSelf, and an inactive host never simulates; a pooled prefab kept
            // inactive until spawned must still drive a live element.
            host.gameObject.SetActive(true);
            VelvetObjectUtil.HideFrameworkSceneObject(host.gameObject);
            host.transform.position = HostParkingPosition;
            // The renderer is disabled and the host sits far from every camera, so Unity's automatic
            // culling would judge it offscreen and PAUSE a looping simulation, freezing the drawn
            // output — the host must always simulate; only the element consumes it.
            var main = host.main;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            binding.HostLoops = main.loop;
            binding.HostDuration = main.duration;
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                binding.Texture = renderer.sharedMaterial != null ? renderer.sharedMaterial.mainTexture : null;
                renderer.enabled = false;
            }
            binding.Buffer = new ParticleSystem.Particle[Mathf.Clamp(main.maxParticles, 1, MaxDrawnParticles)];
            binding.SourceId = source.GetInstanceID();
            binding.Host = host;
            ApplyPlayTrigger(binding);
        }

        // Applies the CURRENT settings' play trigger to the live host and records it so Sync can
        // detect a later flip. Manual stops-and-clears so a play-on-awake source stays quiet.
        private static void ApplyPlayTrigger(ParticlesBinding binding)
        {
            if (binding.Settings.PlayOn == PlayTrigger.Mount)
            {
                binding.Host!.Play();
                binding.LogicallyPlaying = true;
            }
            else
            {
                binding.Host!.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                binding.LogicallyPlaying = false;
            }
            binding.AppliedPlayOn = binding.Settings.PlayOn;
        }

        private static void DestroyHost(ParticlesBinding binding)
        {
            if (binding.Host != null)
            {
                VelvetObjectUtil.Destroy(binding.Host.gameObject);
            }
            binding.Host = null;
            binding.SourceId = 0;
            binding.Buffer = null;
            binding.Texture = null;
            binding.LogicallyPlaying = false;
        }

        // ParticlesElement.Play: the imperative half of PlayTrigger.Manual (and a replay for a
        // finished Mount burst). A no-op while no effect is bound.
        private static void PlayHost(VisualElement element, ParticlesBinding binding)
        {
            if (binding.Host == null)
            {
                return;
            }
            // Editor-side replay: a Simulate()-driven clock clamps at a finished non-looping timeline
            // and Play() merely resumes the pause there, so a drained host restarts from zero first.
            if (!Application.isPlaying && EditorSimulationDrained(binding.Host, binding))
            {
                binding.Host.Simulate(0f, withChildren: true, restart: true, fixedTimeStep: false);
            }
            binding.Host.Play();
            binding.LogicallyPlaying = true;
            // The idle tick parks itself while nothing simulates; a fresh play must resume dirtying.
            SyncRepaintTick(element, binding);
            element.MarkDirtyRepaint();
        }

        // ParticlesElement.Stop: stops emission and clears live particles; the idle tick then parks
        // itself on its next firing. A no-op while no effect is bound.
        private static void StopHost(ParticlesBinding binding)
        {
            if (binding.Host != null)
            {
                binding.Host.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                binding.LogicallyPlaying = false;
            }
        }

        // Advisory mount warnings, once per mounted element (see the binding's warn-once flags).
        private static void WarnOnceForSource(ParticlesBinding binding, ParticleSystem source)
        {
            var main = source.main;
            // Particle positions are read in the simulation's LOCAL space (the element rect is the
            // canvas); any other space draws at meaningless offsets, so say so up front instead of
            // rendering garbage silently. Advisory: the host still simulates.
            if (main.simulationSpace != ParticleSystemSimulationSpace.Local && !binding.WarnedSimulationSpace)
            {
                binding.WarnedSimulationSpace = true;
                Debug.LogWarning($"[Velvet] V.Particles: \"{source.name}\" does not use local simulation space; particle positions are read locally, so a world- or custom-space simulation will not match the drawn layout. Use local simulation space.");
            }
            // The draw path truncates at its particle cap; a denser effect must say so once instead of
            // silently thinning out compared to everywhere else the same source is used.
            if (main.maxParticles > MaxDrawnParticles && !binding.WarnedDrawCap)
            {
                binding.WarnedDrawCap = true;
                Debug.LogWarning($"[Velvet] V.Particles: \"{source.name}\" declares {main.maxParticles} max particles, above the {MaxDrawnParticles} the element draws; the densest frames will render truncated.");
            }
        }

        // Emits one textured quad per live particle into the element's own visual content: centered on
        // the element rect center, world x/y mapped through PixelsPerUnit with y flipped (world up =
        // element up; UI Toolkit y grows downward), scaled by the particle's current size, rotated by
        // its rotation, tinted by its current color. Quads are NOT clipped to the rect — overflow is
        // visible by default like any painted content, and overflow-hidden composes through the usual
        // utilities on the element.
        private static void Draw(MeshGenerationContext mgc, VisualElement element, ParticlesBinding binding)
        {
            var host = binding.Host;
            var buffer = binding.Buffer;
            if (host == null || buffer == null)
            {
                return;
            }
            var w = element.layout.width;
            var h = element.layout.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h))
            {
                return;
            }
            var count = host.GetParticles(buffer);
            if (count <= 0)
            {
                return;
            }

            var ppu = binding.Settings.PixelsPerUnit;
            var cx = w * 0.5f;
            var cy = h * 0.5f;
            var mwd = mgc.Allocate(4 * count, 6 * count, binding.Texture);
            for (var q = 0; q < count; q++)
            {
                var p = buffer[q];
                var center = new Vector2(cx + (p.position.x * ppu), cy - (p.position.y * ppu));
                var half = p.GetCurrentSize(host) * ppu * 0.5f;
                var rad = p.rotation * Mathf.Deg2Rad;
                var cos = Mathf.Cos(rad);
                var sin = Mathf.Sin(rad);
                Color32 tint = p.GetCurrentColor(host);

                // Corner offsets rotated around the particle center; UVs raw 0..1 with v flipped at the
                // top (UI Toolkit remaps them into the atlas slot), matching the sibling quad painters.
                mwd.SetNextVertex(new Vertex { position = Corner(center, -half, -half, cos, sin), tint = tint, uv = new Vector2(0f, 1f) }); // top-left
                mwd.SetNextVertex(new Vertex { position = Corner(center, half, -half, cos, sin), tint = tint, uv = new Vector2(1f, 1f) }); // top-right
                mwd.SetNextVertex(new Vertex { position = Corner(center, half, half, cos, sin), tint = tint, uv = new Vector2(1f, 0f) }); // bottom-right
                mwd.SetNextVertex(new Vertex { position = Corner(center, -half, half, cos, sin), tint = tint, uv = new Vector2(0f, 0f) }); // bottom-left
                var b = q * 4;
                mwd.SetNextIndex((ushort)(b + 0));
                mwd.SetNextIndex((ushort)(b + 1));
                mwd.SetNextIndex((ushort)(b + 2));
                mwd.SetNextIndex((ushort)(b + 0));
                mwd.SetNextIndex((ushort)(b + 2));
                mwd.SetNextIndex((ushort)(b + 3));
            }
        }

        private static Vector3 Corner(Vector2 center, float dx, float dy, float cos, float sin)
            => new(center.x + (dx * cos) - (dy * sin), center.y + (dx * sin) + (dy * cos), Vertex.nearZ);

        // The quads are REBUILT inside generateVisualContent, so the element must be marked dirty
        // every frame while the simulation can move — on BOTH panel context types: even an
        // every-frame-rendering runtime panel reuses the cached mesh unless something dirties it
        // (unlike SceneView, whose static mesh samples a texture that updates underneath). The
        // scheduled item fires only while the element is attached to a ticking panel, so a headless
        // (batch) editor panel schedules it inertly.
        private static void SyncRepaintTick(VisualElement element, ParticlesBinding binding)
        {
            if (binding.Host != null && binding.RepaintTick == null)
            {
                binding.RepaintTick = element.schedule
                    .Execute((TimerState ts) => OnRepaintTick(element, binding, ts.deltaTime / 1000f))
                    .Every(SceneViewDriver.RepaintIntervalMs);
            }
            else if (binding.Host == null)
            {
                StopRepaintTick(binding);
            }
        }

        private static void OnRepaintTick(VisualElement element, ParticlesBinding binding, float dt)
        {
            var host = binding.Host;
            var playing = Application.isPlaying;
            // A drained simulation — a finished burst, a stopped Manual host, a host killed by a scene
            // unload — must not keep dirtying the element at tick rate forever: park the tick (Sync
            // and Play() re-arm it) after one final dirty, so the last live frame's quads are
            // regenerated away instead of lingering. Root-only liveness: the draw samples only the
            // root's particles, so a longer-lived child sub-emitter must not hold the tick open after
            // the drawn output is already empty.
            if (host == null || !host.IsAlive(false)
                || (!playing && binding.LogicallyPlaying && EditorSimulationDrained(host, binding)))
            {
                StopRepaintTick(binding);
            }
            else if (!playing && binding.LogicallyPlaying && dt > 0f)
            {
                // Outside Play Mode the engine never steps a particle system's clock on its own, so an
                // editor-context panel (preview tooling, EditMode fixtures) would repaint one frozen
                // frame forever. Advance the hidden host by the tick's real elapsed time, clamped the
                // way frame deltas are clamped; children advance in step for coherence even though
                // only the root is drawn.
                host.Simulate(Mathf.Min(dt, Time.maximumDeltaTime), withChildren: true, restart: false, fixedTimeStep: false);
            }
            element.MarkDirtyRepaint();
        }

        // The editor-side twin of the IsAlive park above: a Simulate()-driven system is left PAUSED,
        // and a paused system reads IsAlive forever (it never transitions to stopped on its own, and
        // its clock clamps at the end of a non-looping timeline), so "drained" is derived directly —
        // a non-looping root whose clock reached its end with no live particles has nothing left to
        // draw or emit. Root-only on purpose, like the draw. Callers gate on !Application.isPlaying.
        private static bool EditorSimulationDrained(ParticleSystem host, ParticlesBinding binding)
        {
            return !binding.HostLoops && host.particleCount == 0 && host.time >= binding.HostDuration;
        }

        private static void StopRepaintTick(ParticlesBinding binding)
        {
            binding.RepaintTick?.Pause();
            binding.RepaintTick = null;
        }
    }
}
