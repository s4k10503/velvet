#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one Particles element, keyed in
    // ReconcilerContext.ParticlesBindings by the element itself. Holds the current settings, the
    // hidden framework-owned simulation host cloned from the source effect, the particle read buffer
    // (allocated once per host), the draw texture resolved from the host's renderer material, the
    // registered generateVisualContent callback (so it can be unregistered on detach), and the
    // recurring repaint tick so it can be paused whenever the host is destroyed.
    internal sealed class ParticlesBinding
    {
        public ParticlesSettings Settings;
        public ParticleSystem? Host;
        public ParticleSystem.Particle[]? Buffer;
        public Texture? Texture;
        public Action<MeshGenerationContext>? OnGenerate;
        public IVisualElementScheduledItem? RepaintTick;

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
        // Repaint cadence (~60fps), matching the recurring-tick interval the style animation drivers
        // schedule with.
        private const long RepaintIntervalMs = 16;

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
            // Prepend so the quads render behind any later-registered paint, mirroring the other
            // wrapper-less paint layers (generateVisualContent callbacks fire in registration order,
            // later ones painting over earlier).
            element.generateVisualContent = binding.OnGenerate + element.generateVisualContent;
            if (settings.Effect != null)
            {
                CreateHost(binding);
            }
            SyncRepaintTick(element, binding);
            element.MarkDirtyRepaint();
            return binding;
        }

        // Applies changed settings to a live binding: an effect swap destroys the old host and clones a
        // fresh one from the new source; effect-to-null destroys the host and the element goes inert
        // (the binding stays). A PlayOn- or PixelsPerUnit-only change reuses the host: the pixel scale
        // is read at draw time, and the play trigger applies at instantiation. NB the props diff gating
        // this call compares the settings RECORDS (value equality) while the effect fields here compare
        // through Unity's overloaded ==; the two disagree when one side is a DESTROYED effect and the
        // other is literal null (distinct record values, both "null" to Unity) — the host re-derivation
        // below tolerates either verdict.
        public static void Update(VisualElement element, ParticlesBinding binding, ParticlesSettings settings)
        {
            var previousEffect = binding.Settings.Effect;
            binding.Settings = settings;
            if (previousEffect != settings.Effect)
            {
                DestroyHost(binding);
                if (settings.Effect != null)
                {
                    CreateHost(binding);
                }
            }
            SyncRepaintTick(element, binding);
            element.MarkDirtyRepaint();
        }

        // Full teardown: unregisters the painter, pauses the repaint tick, and destroys the hidden host.
        public static void Detach(VisualElement element, ParticlesBinding binding)
        {
            element.generateVisualContent -= binding.OnGenerate;
            StopRepaintTick(binding);
            DestroyHost(binding);
            element.MarkDirtyRepaint();
        }

        // Clones the source effect into the hidden simulation host: renderer disabled (no camera may
        // draw it — only GetParticles is consumed; sub-emitter renderers are not reached, out of scope),
        // hidden from the hierarchy and never saved, parked far out of scene content. The draw texture
        // is the renderer material's main texture (a disabled renderer keeps sharedMaterial readable).
        private static void CreateHost(ParticlesBinding binding)
        {
            var source = binding.Settings.Effect!;
            // Particle positions are read in the simulation's LOCAL space (the element rect is the
            // canvas); a world-space source would draw at meaningless offsets, so say so up front
            // instead of rendering garbage silently. Advisory: the host still simulates.
            if (source.main.simulationSpace == ParticleSystemSimulationSpace.World)
            {
                Debug.LogWarning($"[Velvet] V.Particles: \"{source.name}\" simulates in world space; particle positions are read in local space, so the drawn layout will not match. Use local simulation space.");
            }

            var host = UnityEngine.Object.Instantiate(source);
            // Hidden from the hierarchy and excluded from editor scene saves, but deliberately NOT the
            // full HideAndDontSave: the DontSaveInBuild flag pulls a GameObject out of its scene
            // entirely (scene.IsValid() turns false), and the host must stay an ordinary scene object —
            // it is framework-owned, destroyed with its element, and a runtime-instantiated object
            // never reaches a build's serialized data anyway.
            host.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
            host.transform.position = HostParkingPosition;
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                binding.Texture = renderer.sharedMaterial != null ? renderer.sharedMaterial.mainTexture : null;
                renderer.enabled = false;
            }
            binding.Buffer = new ParticleSystem.Particle[Mathf.Clamp(host.main.maxParticles, 1, MaxDrawnParticles)];
            if (binding.Settings.PlayOn == PlayTrigger.Mount)
            {
                host.Play();
            }
            else
            {
                // Manual: instantiated quiet even when the source prefab plays on awake.
                host.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            binding.Host = host;
        }

        private static void DestroyHost(ParticlesBinding binding)
        {
            if (binding.Host != null)
            {
                VelvetObjectUtil.Destroy(binding.Host.gameObject);
            }
            binding.Host = null;
            binding.Buffer = null;
            binding.Texture = null;
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

        // The quads are REBUILT inside generateVisualContent, so the element must be marked dirty every
        // frame while a host simulates — on BOTH panel context types: even a runtime panel that renders
        // every frame reuses the cached mesh unless something dirties it (unlike SceneView, whose static
        // mesh samples a texture that updates underneath). The scheduled item fires only while the
        // element is attached to a ticking panel, so a headless (batch) editor panel schedules it
        // inertly. Paused whenever the host is destroyed or the binding detaches.
        private static void SyncRepaintTick(VisualElement element, ParticlesBinding binding)
        {
            if (binding.Host != null && binding.RepaintTick == null)
            {
                binding.RepaintTick = element.schedule.Execute(element.MarkDirtyRepaint).Every(RepaintIntervalMs);
            }
            else if (binding.Host == null)
            {
                StopRepaintTick(binding);
            }
        }

        private static void StopRepaintTick(ParticlesBinding binding)
        {
            binding.RepaintTick?.Pause();
            binding.RepaintTick = null;
        }
    }
}
