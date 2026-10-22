#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Velvet
{
    // What one of a renderer's materials draws its triangles with: the texture they sample and the faces its
    // shader culls.
    internal readonly struct ParticleLayer
    {
        public readonly Texture? Texture;
        public readonly CullMode Cull;

        public ParticleLayer(Texture? texture, CullMode cull)
        {
            Texture = texture;
            Cull = cull;
        }
    }

    // One particle system inside the hidden host — the root or a child, a sub-emitter among them — as
    // captured at clone time: the framework owns the clone, so no one changes these settings on it
    // afterwards.
    internal sealed class HostedParticleSystem
    {
        public readonly ParticleSystem System;
        // Null for a system that draws nothing: its renderer was off in the source, rendered in
        // ParticleSystemRenderMode.None, or rendered meshes without one.
        public readonly ParticleSystemRenderer? Renderer;
        public readonly ParticleLayer Particles;
        public readonly bool DrawsTrails;
        public readonly ParticleLayer Trails;
        // A sub-emitter emits only when its parent's particles trigger it, and a disabled emission
        // module never does, so neither keeps the effect alive on its own clock.
        public readonly bool EmitsOnItsOwn;
        // Seconds after play at which the system's own emission ends: never, for a looping one.
        public readonly float EmitsUntil;

        public HostedParticleSystem(ParticleSystem system, ParticleSystemRenderer? renderer, ParticleLayer particles,
            ParticleLayer trails, bool emitsOnItsOwn, float emitsUntil)
        {
            System = system;
            Renderer = renderer;
            Particles = particles;
            DrawsTrails = system.trails.enabled;
            Trails = trails;
            EmitsOnItsOwn = emitsOnItsOwn;
            EmitsUntil = emitsUntil;
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
        // The disabled camera the renderers bake their geometry for, a child of the host, and the mesh they
        // bake into.
        public Camera? BakeCamera;
        public Mesh? BakedMesh;
        public Action<MeshGenerationContext>? OnGenerate;
        public EventCallback<AttachToPanelEvent>? OnAttach;
        public IVisualElementScheduledItem? RepaintTick;
        // Logical play state as the DRIVER last set it (Mount trigger, element Play/Stop): outside
        // Play Mode the engine never advances a system's clock, so the repaint tick steps the
        // simulation manually exactly while this is set — the native isPlaying flag cannot serve,
        // because ParticleSystem.Simulate itself flips the system to paused.
        public bool LogicallyPlaying;
        // Seconds the repaint tick has stepped the host since it last started playing, outside Play
        // Mode — the clock HostedParticleSystem.EmitsUntil is read against there.
        public float SimulatedSeconds;
        // An inline filter renders the element through an offscreen tree sized to its layout boundingBox, so
        // the particle geometry drawn beyond the host rect clips; a last-child spacer widens the boundingBox to
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
    /// framework-owned simulation host (renderers disabled — the source system is never mutated), each
    /// renderer's geometry is baked every repaint, and the baked triangles are drawn in the element's own
    /// visual content — no RenderTexture, no world-space canvas, no render-pipeline coupling.
    /// </summary>
    internal static class ParticlesDriver
    {
        // The triangles one mesh allocation carries, at three vertices each. A UI Toolkit mesh allocation is
        // bounded by its vertex budget, so a denser bake is drawn as several allocations of at most this many
        // triangles rather than one unbounded one.
        private const int MaxTrianglesPerAllocation = 4096;

        private const float BakeCameraDistance = 1000f;

        private const ParticleSystemBakeMeshOptions WorldSpace =
            ParticleSystemBakeMeshOptions.BakePosition | ParticleSystemBakeMeshOptions.BakeRotationAndScale;

        private const string CullProperty = "_Cull";

        // What the draw reads of each baked vertex, whatever streams the source's shader asked for.
        private static readonly List<ParticleSystemVertexStream> s_bakedStreams = new()
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
        };

        // Scratch for Draw, reused rather than allocated on every repaint.
        private static readonly List<Vector3> s_positions = new();
        private static readonly List<Color32> s_colors = new();
        private static readonly List<Vector2> s_uvs = new();
        private static readonly List<int> s_triangles = new();
        private static readonly List<Vector2> s_local = new();
        private static readonly List<int> s_kept = new();

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

        // Clones the source effect into the hidden simulation host, at the source's world position and
        // rotation so its collisions and triggers meet the colliders and planes the source's would: each
        // particle system's renderer disabled (only its baked geometry is drawn), hidden from the hierarchy
        // and excluded from editor scene saves. A disabled renderer keeps its materials readable.
        private static void CreateHost(ParticlesBinding binding)
        {
            var source = binding.Settings.Effect!;
            var host = UnityEngine.Object.Instantiate(source, source.transform.position, source.transform.rotation);
            // Cloning preserves activeSelf, and an inactive host never simulates; a pooled prefab kept
            // inactive until spawned must still drive a live element.
            host.gameObject.SetActive(true);
            VelvetObjectUtil.HideFrameworkSceneObject(host.gameObject);
            CloneExternalSubEmitters(host);
            var systems = host.GetComponentsInChildren<ParticleSystem>();
            var subEmitters = new HashSet<ParticleSystem>();
            foreach (var system in systems)
            {
                var module = system.subEmitters;
                for (var s = 0; s < module.subEmittersCount; s++)
                {
                    subEmitters.Add(module.GetSubEmitterSystem(s));
                }
            }
            var hosted = new HostedParticleSystem[systems.Length];
            for (var i = 0; i < systems.Length; i++)
            {
                var system = systems[i];
                var main = system.main;
                // The renderers are disabled, and automatic culling must not pause a looping simulation and
                // freeze the drawn output, so every system always simulates.
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                ParticleSystemRenderer? drawn = null;
                var particles = default(ParticleLayer);
                var trails = default(ParticleLayer);
                if (system.TryGetComponent<ParticleSystemRenderer>(out var renderer))
                {
                    if (Draws(renderer))
                    {
                        drawn = renderer;
                        renderer.SetActiveVertexStreams(s_bakedStreams);
                        renderer.SetActiveTrailVertexStreams(s_bakedStreams);
                        particles = new ParticleLayer(ParticleTexture(system, renderer.sharedMaterial), CullOf(renderer.sharedMaterial));
                        trails = LayerOf(renderer.trailMaterial);
                    }
                    renderer.enabled = false;
                }
                var emitsOnItsOwn = system.emission.enabled && !subEmitters.Contains(system);
                var emitsUntil = main.loop ? float.PositiveInfinity : main.startDelay.constantMax + main.duration;
                hosted[i] = new HostedParticleSystem(system, drawn, particles, trails, emitsOnItsOwn, emitsUntil);
            }
            binding.Systems = hosted;
            binding.BakeCamera = CreateBakeCamera(host);
            binding.BakedMesh = new Mesh
            {
                name = "velvet-particles-baked-mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt32,
            };
            binding.SourceId = source.GetEntityId();
            binding.Host = host;
            ApplyPlayTrigger(binding);
        }

        // Looks down the host's forward axis with the host's up as its own, so a billboard facing it lies in
        // the host's local xy plane, the plane the element draws.
        private static Camera CreateBakeCamera(ParticleSystem host)
        {
            var go = new GameObject("velvet-particles-bake-camera");
            VelvetObjectUtil.HideFrameworkSceneObject(go);
            go.transform.SetParent(host.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -BakeCameraDistance);
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            return camera;
        }

        // A texture-sheet animation in Sprites mode draws from its first sprite's texture in place of the
        // material's.
        private static Texture? ParticleTexture(ParticleSystem system, Material? material)
        {
            var sheet = system.textureSheetAnimation;
            var sprite = sheet.enabled && sheet.mode == ParticleSystemAnimationMode.Sprites && sheet.spriteCount != 0
                ? sheet.GetSprite(0)
                : null;
            if (sprite != null)
            {
                return sprite.texture;
            }
            return material != null ? material.mainTexture : null;
        }

        private static ParticleLayer LayerOf(Material? material)
            => new(material != null ? material.mainTexture : null, CullOf(material));

        // A shader declaring no _Cull property is read as culling nothing.
        private static CullMode CullOf(Material? material)
            => material != null && material.HasProperty(CullProperty)
                ? (CullMode)Mathf.RoundToInt(material.GetFloat(CullProperty))
                : CullMode.Off;

        // A sub-emitter outside the host's hierarchy is cloned under the host (once, however many
        // systems name it), at the target's own world position and rotation, and the reference
        // re-pointed at the clone, so the clone's particles are the ones drawn. The queue also visits
        // the clones, so a sub-emitter's own external sub-emitters follow.
        private static void CloneExternalSubEmitters(ParticleSystem host)
        {
            var clones = new Dictionary<ParticleSystem, ParticleSystem>();
            var pending = new Queue<ParticleSystem>(host.GetComponentsInChildren<ParticleSystem>());
            while (pending.Count > 0)
            {
                var module = pending.Dequeue().subEmitters;
                for (var s = 0; s < module.subEmittersCount; s++)
                {
                    var target = module.GetSubEmitterSystem(s);
                    if (target == null || target.transform.IsChildOf(host.transform))
                    {
                        continue;
                    }
                    if (!clones.TryGetValue(target, out var clone))
                    {
                        clone = CloneUnderHost(target, host);
                        clones.Add(target, clone);
                        foreach (var system in clone.GetComponentsInChildren<ParticleSystem>())
                        {
                            pending.Enqueue(system);
                        }
                    }
                    module.SetSubEmitterSystem(s, clone);
                }
            }
        }

        private static ParticleSystem CloneUnderHost(ParticleSystem target, ParticleSystem host)
        {
            var clone = UnityEngine.Object.Instantiate(target, target.transform.position, target.transform.rotation, host.transform);
            clone.gameObject.SetActive(true);
            VelvetObjectUtil.HideFrameworkSceneObject(clone.gameObject);
            return clone;
        }

        // Nothing is drawn of a system whose renderer is off or set to render nothing, as in the scene, nor
        // of one rendering meshes with no mesh assigned, which is never baked.
        private static bool Draws(ParticleSystemRenderer renderer)
        {
            return renderer.enabled && renderer.renderMode != ParticleSystemRenderMode.None
                && (renderer.renderMode != ParticleSystemRenderMode.Mesh || renderer.mesh != null);
        }

        // Applies the CURRENT settings' play trigger to the live host and records it so Sync can
        // detect a later flip. Manual stops-and-clears so a play-on-awake source stays quiet.
        private static void ApplyPlayTrigger(ParticlesBinding binding)
        {
            if (binding.Settings.PlayOn == PlayTrigger.Mount)
            {
                binding.Host!.Play();
                binding.LogicallyPlaying = true;
                binding.SimulatedSeconds = 0f;
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
            VelvetObjectUtil.Destroy(binding.BakedMesh);
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
            if (!Application.isPlaying && !AnySystemLive(binding, false))
            {
                binding.Host.Simulate(0f, withChildren: true, restart: true, fixedTimeStep: false);
                binding.SimulatedSeconds = 0f;
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

        // Draws every drawn system's baked geometry into the element's own visual content: its particles,
        // then its trails. Vertices are taken into the root's local space, centered on the element rect
        // center, x/y mapped through PixelsPerUnit with y flipped (up = element up; UI Toolkit y grows
        // downward). Triangles are NOT clipped to the rect — overflow is visible by default like any
        // painted content, and overflow-hidden composes through the usual utilities on the element.
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

            var ppu = binding.Settings.PixelsPerUnit;
            var camera = binding.BakeCamera!;
            var mesh = binding.BakedMesh!;
            // The bake camera's view is as tall as the element, so a renderer's limits on a particle's share
            // of the view are taken against the element; ParticlesPlaybackTests'
            // Given_AParticleLargerThanItsRendererAllows_When_Drawn_Then_ItIsClampedAgainstTheElement pins that.
            camera.orthographicSize = h / (2f * ppu);
            // With nothing drawn the bounds stay inverted, a negative size SyncBoundsSpacer treats as no extent.
            var frame = new DrawFrame
            {
                ToHost = host.transform.worldToLocalMatrix,
                Cx = w * 0.5f,
                Cy = h * 0.5f,
                Ppu = ppu,
                MinX = float.MaxValue,
                MinY = float.MaxValue,
                MaxX = float.MinValue,
                MaxY = float.MinValue,
            };
            foreach (var hosted in binding.Systems!)
            {
                // Null for a system that draws nothing, and for a child whose stop action destroyed it
                // while the host lives on.
                if (hosted.Renderer == null)
                {
                    continue;
                }
                hosted.Renderer.BakeMesh(mesh, camera, WorldSpace);
                DrawBaked(mgc, mesh, hosted.Particles, ref frame);
                if (hosted.DrawsTrails)
                {
                    hosted.Renderer.BakeTrailsMesh(mesh, camera, WorldSpace);
                    DrawBaked(mgc, mesh, hosted.Trails, ref frame);
                }
            }
            binding.LiveExtentLocal = Rect.MinMaxRect(frame.MinX, frame.MinY, frame.MaxX, frame.MaxY);
        }

        // The element-space mapping every system shares within one Draw, and the extent it accumulates.
        private struct DrawFrame
        {
            public Matrix4x4 ToHost;
            public float Cx;
            public float Cy;
            public float Ppu;
            public float MinX;
            public float MinY;
            public float MaxX;
            public float MaxY;
        }

        private static void DrawBaked(MeshGenerationContext mgc, Mesh mesh, ParticleLayer layer, ref DrawFrame frame)
        {
            mesh.GetVertices(s_positions);
            mesh.GetColors(s_colors);
            mesh.GetUVs(0, s_uvs);
            s_local.Clear();
            foreach (var position in s_positions)
            {
                s_local.Add(frame.ToHost.MultiplyPoint3x4(position));
            }
            for (var sub = 0; sub < mesh.subMeshCount; sub++)
            {
                mesh.GetTriangles(s_triangles, sub);
                s_kept.Clear();
                for (var t = 0; t < s_triangles.Count; t += 3)
                {
                    if (Keeps(layer.Cull, s_local[s_triangles[t]], s_local[s_triangles[t + 1]], s_local[s_triangles[t + 2]]))
                    {
                        s_kept.Add(t);
                    }
                }
                // MUTANT_SURVIVES(equivalent, boundary): the extra pass `<=` adds allocates zero triangles, which Allocate returns without drawing.
                for (var first = 0; first < s_kept.Count; first += MaxTrianglesPerAllocation)
                {
                    DrawTriangles(mgc, layer.Texture, first, Mathf.Min(s_kept.Count - first, MaxTrianglesPerAllocation), ref frame);
                }
            }
        }

        // Whether a triangle survives its material's culling. A front face's corners turn clockwise in the
        // host's local xy plane, which is how the bake camera sees it; ParticlesPlaybackTests'
        // Given_AMeshParticleUnderABackCullingMaterial_When_Drawn_Then_OnlyItsFrontFaceRenders pins that.
        private static bool Keeps(CullMode cull, Vector2 a, Vector2 b, Vector2 c)
        {
            var turn = ((b.x - a.x) * (c.y - a.y)) - ((b.y - a.y) * (c.x - a.x));
            // MUTANT_SURVIVES(equivalent, boundary): a triangle whose corners turn neither way has no area, so keeping it draws no pixel.
            return cull switch
            {
                CullMode.Back => turn < 0f,
                CullMode.Front => turn > 0f,
                _ => true,
            };
        }

        private static void DrawTriangles(MeshGenerationContext mgc, Texture? texture, int first, int triangles, ref DrawFrame frame)
        {
            var cx = frame.Cx;
            var cy = frame.Cy;
            var ppu = frame.Ppu;
            var minX = frame.MinX;
            var minY = frame.MinY;
            var maxX = frame.MaxX;
            var maxY = frame.MaxY;
            var mwd = mgc.Allocate(3 * triangles, 3 * triangles, texture);
            for (var k = 0; k < triangles; k++)
            {
                var t = s_kept[first + k];
                for (var corner = 0; corner < 3; corner++)
                {
                    var v = s_triangles[t + corner];
                    var local = s_local[v];
                    var x = cx + (local.x * ppu);
                    var y = cy - (local.y * ppu);
                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                    mwd.SetNextVertex(new Vertex { position = new Vector3(x, y, Vertex.nearZ), tint = s_colors[v], uv = s_uvs[v] });
                    mwd.SetNextIndex((ushort)((3 * k) + corner));
                }
            }
            frame.MinX = minX;
            frame.MinY = minY;
            frame.MaxX = maxX;
            frame.MaxY = maxY;
        }

        // The triangles are REBUILT inside generateVisualContent, so the element must be marked dirty
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
            // and Play() re-arm it) after one final dirty, so the last live frame's triangles are
            // regenerated away instead of lingering. Liveness spans the children, since the draw
            // samples their particles too.
            if (host == null || !AnySystemLive(binding, playing))
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
                    binding.SimulatedSeconds += Mathf.Min(dt, Time.maximumDeltaTime);
                }
                // Draw stashed the extent last repaint, one frame behind the particles — invisible after the
                // quantize + slack, and it saves a second bake here.
                SyncBoundsSpacer(element, binding);
            }
            element.MarkDirtyRepaint();
        }

        // Whether any system still holds a particle or will emit one on its own clock. In Play Mode
        // that clock is the engine's; outside it the engine leaves a Simulate()-driven system PAUSED,
        // and a paused system reads IsAlive forever, so the clock is the one the tick keeps.
        private static bool AnySystemLive(ParticlesBinding binding, bool playing)
        {
            // MUTANT_SURVIVES(unreachable): every caller has found the host alive, and the host's own system is an entry, so the loop assigns live before it returns.
            var live = false;
            foreach (var hosted in binding.Systems!)
            {
                if (hosted.System == null)
                {
                    continue;
                }
                live = hosted.System.particleCount > 0 || (hosted.EmitsOnItsOwn && EmissionPending(binding, hosted, playing));
                if (live)
                {
                    break;
                }
            }
            return live;
        }

        private static bool EmissionPending(ParticlesBinding binding, HostedParticleSystem hosted, bool playing)
        {
            return playing
                ? hosted.System.IsAlive(false)
                : binding.LogicallyPlaying && binding.SimulatedSeconds < hosted.EmitsUntil;
        }

        private static void StopRepaintTick(ParticlesBinding binding)
        {
            binding.RepaintTick?.Pause();
            binding.RepaintTick = null;
        }
    }
}
