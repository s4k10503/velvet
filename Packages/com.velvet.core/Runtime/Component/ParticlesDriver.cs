#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // One particle system inside the hidden host — the root or a child, a sub-emitter among them — as
    // captured at clone time: the framework owns the clone, so no one changes these settings on it
    // afterwards. A system whose renderer was off in the source draws nothing, and holds no buffer.
    internal readonly struct HostedParticleSystem
    {
        public readonly ParticleSystem System;
        public readonly ParticleSystem.Particle[]? Buffer;
        public readonly Texture? Texture;
        public readonly ParticleSystemSimulationSpace Space;
        public readonly Transform? CustomSpace;

        public HostedParticleSystem(ParticleSystem system, ParticleSystem.Particle[]? buffer, Texture? texture,
            ParticleSystemSimulationSpace space, Transform? customSpace)
        {
            System = system;
            Buffer = buffer;
            Texture = texture;
            Space = space;
            CustomSpace = customSpace;
        }
    }

    // Reconciler-side bookkeeping for one Particles element, keyed in
    // ReconcilerContext.ParticlesBindings by the element itself. Holds the current settings, the
    // hidden framework-owned simulation host cloned from the source effect (plus the source's
    // instance id, so a swap is detected even after the old source object dies), every particle
    // system inside that host, the registered callbacks (so they can be unregistered on detach), and
    // the recurring repaint tick so it can be paused whenever nothing simulates.
    internal sealed class ParticlesBinding
    {
        public ParticlesSettings Settings;
        public ParticleSystem? Host;
        // GetEntityId of the source the current Host was cloned from — an id (not the object
        // reference) so a source destroyed after the clone still compares meaningfully.
        public EntityId SourceId;
        // The play trigger last applied to the live Host, so a settings change applies a playOn flip
        // exactly once instead of re-triggering on every unrelated diff.
        public PlayTrigger AppliedPlayOn;
        public HostedParticleSystem[]? Systems;
        public Action<MeshGenerationContext>? OnGenerate;
        public EventCallback<AttachToPanelEvent>? OnAttach;
        public IVisualElementScheduledItem? RepaintTick;
        // Logical play state as the DRIVER last set it (Mount trigger, element Play/Stop): outside
        // Play Mode the engine never advances a system's clock, so the repaint tick steps the
        // simulation manually exactly while this is set — the native isPlaying flag cannot serve,
        // because ParticleSystem.Simulate itself flips the system to paused.
        public bool LogicallyPlaying;
        // The root's own loop flag and duration, captured at clone time like HostedParticleSystem's
        // fields: the editor drained-probe reads them every tick and each ParticleSystem property
        // access is a native call.
        public bool HostLoops;
        public float HostDuration;
        // An inline filter renders the element through an offscreen tree sized to its layout boundingBox, so
        // the particle quads drawn beyond the host rect clip; a last-child spacer widens the boundingBox to
        // cover them (shared with the skew / shadow paints via SilhouetteBoundsSpacer). Unlike those static
        // AABBs the particles move every frame, hence the quantize + last-applied cache below: without them
        // the spacer would re-layout (and the filter texture reallocate) on every tick.
        public bool WantSpacer;
        public VisualElement? BoundsSpacer;
        public Rect LiveExtentLocal;
        public Rect AppliedSpacerAabb;
        // The caster's left/top border, parsed from the class list, to shift the spacer origin into padding-box
        // space (see SilhouetteBoundsSpacer.BorderInset).
        public float BorderLeft;
        public float BorderTop;

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
        // The quads one mesh allocation carries. A UI Toolkit mesh allocation is bounded by its vertex
        // budget and each particle costs 4 vertices, so a denser system is drawn as several
        // allocations of at most this many quads rather than one unbounded one.
        private const int MaxQuadsPerAllocation = 2048;

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

        // A filter comes and goes independent of the effect (a class swap, a state variant), so the reconciler
        // drives this from the class list on every patch, not only on a Particles-settings change. The extent
        // itself follows the live particles through the repaint tick, not this call.
        public static void SetWantSpacer(VisualElement element, ParticlesBinding binding, bool want, string[] classNames)
        {
            binding.WantSpacer = want;
            SilhouetteBoundsSpacer.BorderInset(classNames, out binding.BorderLeft, out binding.BorderTop);
            SyncBoundsSpacer(element, binding);
        }

        // So an animating burst that grows a few pixels a frame re-lays-out the spacer (and reallocates the
        // filter texture) only when its extent crosses a bucket, not every tick. Over-covering by up to a
        // bucket is invisible — the spacer paints nothing.
        private const float SpacerQuantum = 32f;

        // Off-panel / pre-layout there is no extent yet, but the spacer still attaches unsized: the reconciler
        // must count it as a trailing child from the moment a filter wants it, and the tick sizes it once
        // layout and a live simulation resolve a real extent.
        private static void SyncBoundsSpacer(VisualElement element, ParticlesBinding binding)
        {
            if (!binding.WantSpacer)
            {
                SilhouetteBoundsSpacer.Remove(element, ref binding.BoundsSpacer);
                binding.AppliedSpacerAabb = default;
                return;
            }
            if (!SilhouetteBoundsSpacer.TryGetLayoutSize(element, out var w, out var h))
            {
                SilhouetteBoundsSpacer.Sync(element, ref binding.BoundsSpacer, true, default);
                return;
            }
            var box = new Rect(0f, 0f, w, h);
            var live = binding.LiveExtentLocal;
            Rect reserved;
            if (live.width > 0f && live.height > 0f)
            {
                // Lead the live extent by a quantum: the extent this reads was stashed by Draw one frame ago,
                // so a fast-growing burst would otherwise outrun the reserved bounds between frames and clip
                // its leading edge. The headroom keeps growth up to a quantum per frame covered. No lookahead
                // for the box-only fallback (nothing overflows there).
                var u = SilhouetteBoundsSpacer.Union(box, live);
                reserved = new Rect(u.xMin - SpacerQuantum, u.yMin - SpacerQuantum,
                    u.width + (2f * SpacerQuantum), u.height + (2f * SpacerQuantum));
            }
            else
            {
                reserved = box;
            }
            var quantized = SilhouetteBoundsSpacer.ShiftToPaddingBox(
                QuantizeOutward(reserved), binding.BorderLeft, binding.BorderTop);
            if (quantized == binding.AppliedSpacerAabb)
            {
                return;
            }
            binding.AppliedSpacerAabb = quantized;
            SilhouetteBoundsSpacer.Sync(element, ref binding.BoundsSpacer, true, quantized);
        }

        // Outward, not nearest, so the reserved bounds never fall short of the live extent between buckets.
        private static Rect QuantizeOutward(Rect r)
        {
            var xMin = Mathf.Floor(r.xMin / SpacerQuantum) * SpacerQuantum;
            var yMin = Mathf.Floor(r.yMin / SpacerQuantum) * SpacerQuantum;
            var xMax = Mathf.Ceil(r.xMax / SpacerQuantum) * SpacerQuantum;
            var yMax = Mathf.Ceil(r.yMax / SpacerQuantum) * SpacerQuantum;
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
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
            SilhouetteBoundsSpacer.Remove(element, ref binding.BoundsSpacer);
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
            else if (binding.Host == null || binding.SourceId != effect.GetEntityId())
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
            if (binding.Host == null)
            {
                // A null / destroyed effect draws nothing, and the tick that would otherwise collapse the
                // reserved bounds on drain is now parked — so return them to the box here, else the filter
                // texture stays oversized at the last live extent until the effect returns or unmount.
                binding.LiveExtentLocal = default;
                SyncBoundsSpacer(element, binding);
            }
        }

        // Clones the source effect into the hidden simulation host: each particle system's renderer
        // disabled (no camera may draw it — only GetParticles is consumed), hidden from the hierarchy
        // and excluded from editor scene saves, parked far out of scene content. Each system's draw
        // texture is its renderer material's main texture (a disabled renderer keeps sharedMaterial
        // readable).
        private static void CreateHost(ParticlesBinding binding)
        {
            var source = binding.Settings.Effect!;
            var host = UnityEngine.Object.Instantiate(source);
            // Cloning preserves activeSelf, and an inactive host never simulates; a pooled prefab kept
            // inactive until spawned must still drive a live element.
            host.gameObject.SetActive(true);
            VelvetObjectUtil.HideFrameworkSceneObject(host.gameObject);
            host.transform.position = HostParkingPosition;
            var rootMain = host.main;
            binding.HostLoops = rootMain.loop;
            binding.HostDuration = rootMain.duration;
            var systems = host.GetComponentsInChildren<ParticleSystem>();
            var hosted = new HostedParticleSystem[systems.Length];
            for (var i = 0; i < systems.Length; i++)
            {
                var system = systems[i];
                var main = system.main;
                // The renderers are disabled and the host sits far from every camera, so Unity's
                // automatic culling would judge a system offscreen and PAUSE a looping simulation,
                // freezing the drawn output — every system must always simulate.
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                ParticleSystem.Particle[]? buffer = null;
                Texture? texture = null;
                if (system.TryGetComponent<ParticleSystemRenderer>(out var renderer))
                {
                    if (renderer.enabled)
                    {
                        buffer = new ParticleSystem.Particle[Mathf.Max(1, main.maxParticles)];
                        texture = renderer.sharedMaterial != null ? renderer.sharedMaterial.mainTexture : null;
                    }
                    renderer.enabled = false;
                }
                hosted[i] = new HostedParticleSystem(system, buffer, texture, main.simulationSpace, main.customSimulationSpace);
            }
            binding.Systems = hosted;
            binding.SourceId = source.GetEntityId();
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
            binding.SourceId = EntityId.None;
            binding.Systems = null;
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

        // Emits one textured quad per live particle of every drawn system into the element's own visual
        // content: positions taken into the root's local space, centered on the element rect center,
        // x/y mapped through PixelsPerUnit with y flipped (up = element up; UI Toolkit y grows downward),
        // scaled by the particle's current size, rotated by its rotation, tinted by its current color.
        // Quads are NOT clipped to the rect — overflow is visible by default like any painted content,
        // and overflow-hidden composes through the usual utilities on the element.
        private static void Draw(MeshGenerationContext mgc, VisualElement element, ParticlesBinding binding)
        {
            // Reset first so every early return leaves no extent: the spacer collapses back to the box on
            // the next tick.
            binding.LiveExtentLocal = default;
            var host = binding.Host;
            if (host == null)
            {
                return;
            }
            var w = element.layout.width;
            var h = element.layout.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h))
            {
                return;
            }

            var toHost = host.transform.worldToLocalMatrix;
            // Only a filtered element consumes the extent, so the far more common no-filter case skips the
            // per-particle reach work and the Rect write entirely. With nothing drawn the bounds stay
            // inverted, a negative size SyncBoundsSpacer treats as no extent.
            var frame = new QuadFrame
            {
                Cx = w * 0.5f,
                Cy = h * 0.5f,
                Ppu = binding.Settings.PixelsPerUnit,
                Track = binding.WantSpacer,
                MinX = float.MaxValue,
                MinY = float.MaxValue,
                MaxX = float.MinValue,
                MaxY = float.MinValue,
            };
            foreach (var hosted in binding.Systems!)
            {
                if (hosted.Buffer != null)
                {
                    DrawSystem(mgc, hosted, toHost * SimulationToWorld(hosted), ref frame);
                }
            }
            if (frame.Track)
            {
                binding.LiveExtentLocal = Rect.MinMaxRect(frame.MinX, frame.MinY, frame.MaxX, frame.MaxY);
            }
        }

        // The element-space mapping every system shares within one Draw, and the extent it accumulates.
        private struct QuadFrame
        {
            public float Cx;
            public float Cy;
            public float Ppu;
            public bool Track;
            public float MinX;
            public float MinY;
            public float MaxX;
            public float MaxY;
        }

        private static void DrawSystem(MeshGenerationContext mgc, HostedParticleSystem hosted, Matrix4x4 toDrawSpace, ref QuadFrame frame)
        {
            var count = hosted.System.GetParticles(hosted.Buffer!);
            // MUTANT_SURVIVES(equivalent, boundary): the extra pass `<=` adds allocates zero quads, which Allocate returns without drawing.
            for (var first = 0; first < count; first += MaxQuadsPerAllocation)
            {
                DrawQuads(mgc, hosted, toDrawSpace, first, Mathf.Min(count - first, MaxQuadsPerAllocation), ref frame);
            }
        }

        private static void DrawQuads(MeshGenerationContext mgc, HostedParticleSystem hosted, Matrix4x4 toDrawSpace, int first, int quads, ref QuadFrame frame)
        {
            var system = hosted.System;
            var buffer = hosted.Buffer!;
            var cx = frame.Cx;
            var cy = frame.Cy;
            var ppu = frame.Ppu;
            var track = frame.Track;
            var minX = frame.MinX;
            var minY = frame.MinY;
            var maxX = frame.MaxX;
            var maxY = frame.MaxY;
            var mwd = mgc.Allocate(4 * quads, 6 * quads, hosted.Texture);
            for (var q = 0; q < quads; q++)
            {
                var p = buffer[first + q];
                p.position = toDrawSpace.MultiplyPoint3x4(p.position);
                var center = new Vector2(cx + (p.position.x * ppu), cy - (p.position.y * ppu));
                var half = p.GetCurrentSize(system) * ppu * 0.5f;
                var rad = p.rotation * Mathf.Deg2Rad;
                var cos = Mathf.Cos(rad);
                var sin = Mathf.Sin(rad);
                Color32 tint = p.GetCurrentColor(system);

                if (track)
                {
                    // A `half`-half-extent square rotated by the particle's rotation reaches half*(|cos| + |sin|)
                    // on each axis — the bound the filter spacer must cover to keep the quad from clipping.
                    var reach = half * (Mathf.Abs(cos) + Mathf.Abs(sin));
                    if (center.x - reach < minX) { minX = center.x - reach; }
                    if (center.y - reach < minY) { minY = center.y - reach; }
                    if (center.x + reach > maxX) { maxX = center.x + reach; }
                    if (center.y + reach > maxY) { maxY = center.y + reach; }
                }

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
            frame.MinX = minX;
            frame.MinY = minY;
            frame.MaxX = maxX;
            frame.MaxY = maxY;
        }

        // The frame a system's particle positions are expressed in, as a matrix into world space. A
        // Custom space with no transform assigned is read as World.
        private static Matrix4x4 SimulationToWorld(HostedParticleSystem hosted)
        {
            switch (hosted.Space)
            {
                case ParticleSystemSimulationSpace.Local:
                    return hosted.System.transform.localToWorldMatrix;
                case ParticleSystemSimulationSpace.Custom:
                    return hosted.CustomSpace != null ? hosted.CustomSpace.localToWorldMatrix : Matrix4x4.identity;
                default:
                    return Matrix4x4.identity;
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
            // regenerated away instead of lingering. Liveness spans the children, since the draw
            // samples their particles too.
            if (host == null || !host.IsAlive(true)
                || (!playing && binding.LogicallyPlaying && EditorSimulationDrained(host, binding)))
            {
                StopRepaintTick(binding);
                // Collapse the reserved bounds before the tick parks, else a one-shot burst leaves the filter
                // texture oversized until unmount (Play() / Sync re-arm the tick and the extent re-grows).
                binding.LiveExtentLocal = default;
                SyncBoundsSpacer(element, binding);
            }
            else
            {
                if (!playing && binding.LogicallyPlaying && dt > 0f)
                {
                    // Outside Play Mode the engine never steps a particle system's clock on its own, so an
                    // editor-context panel (preview tooling, EditMode fixtures) would repaint one frozen
                    // frame forever. Advance the hidden host by the tick's real elapsed time, clamped the
                    // way frame deltas are clamped, children with it.
                    host.Simulate(Mathf.Min(dt, Time.maximumDeltaTime), withChildren: true, restart: false, fixedTimeStep: false);
                }
                // Draw stashed the extent last repaint, one frame behind the particles — invisible after the
                // quantize + slack, and it saves a second GetParticles here.
                SyncBoundsSpacer(element, binding);
            }
            element.MarkDirtyRepaint();
        }

        // The editor-side twin of the IsAlive park above: a Simulate()-driven system is left PAUSED,
        // and a paused system reads IsAlive forever (it never transitions to stopped on its own, and
        // its clock clamps at the end of a non-looping timeline), so "drained" is derived directly —
        // a non-looping root whose clock reached its end, with no live particles in any system, has
        // nothing left to draw or emit. A system whose renderer is off counts too: its particles can
        // still spawn a drawn sub-emitter's. Callers gate on !Application.isPlaying.
        private static bool EditorSimulationDrained(ParticleSystem host, ParticlesBinding binding)
        {
            foreach (var hosted in binding.Systems!)
            {
                if (hosted.System.particleCount > 0)
                {
                    return false;
                }
            }
            return !binding.HostLoops && host.time >= binding.HostDuration;
        }

        private static void StopRepaintTick(ParticlesBinding binding)
        {
            binding.RepaintTick?.Pause();
            binding.RepaintTick = null;
        }
    }
}
