#nullable enable
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.VFX;

namespace Velvet
{
    // The VisualEffect half of ParticlesDriver. A VisualEffect simulates and draws on the GPU and leaves no
    // geometry to bake, so a camera under its hidden clone renders the clone into a texture on the element's tick
    // and a layer over the element draws that texture.
    //
    // The clone and every object under it are moved onto a layer the project has not named, the one layer the
    // camera renders, and their renderers are forced off except while that camera renders, so no other camera
    // draws the clone.
    internal static class ParticlesEffectView
    {
        // The camera looks down the clone's forward axis from this far behind it, as ParticlesDriver's bake camera
        // looks at its host, so the element shows the clone's local xy plane.
        private const float CameraDistance = 1000f;

        private static bool s_warnedNoLayer;

        internal static void Create(VisualElement element, ParticlesBinding binding, VisualEffect source)
        {
            var host = Object.Instantiate(source, source.transform.position, source.transform.rotation);
            // Cloning preserves activeSelf; a pooled prefab kept inactive until spawned must still drive a live
            // element, as ParticlesDriver's host does.
            host.gameObject.SetActive(true);
            VelvetObjectUtil.HideFrameworkSceneObject(host.gameObject);
            var layer = UnnamedLayer(source.gameObject.layer);
            foreach (var t in host.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
            }
            var renderers = host.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.forceRenderingOff = true;
            }
            binding.EffectHost = host;
            binding.EffectRenderers = renderers;
            binding.EffectInitialEvent = host.initialEventName;
            binding.EffectCamera = CreateCamera(host, layer);
            var view = ParticlesDriver.CreateLayer();
            view.generateVisualContent += mgc => Paint(mgc, view, element, binding);
            var material = ParticleBlendMaterials.For(ParticleBlend.Premultiplied);
            if (material != null)
            {
                view.style.unityMaterial = new StyleMaterialDefinition(material);
            }
            element.Insert(0, view);
            binding.EffectLayer = view;
        }

        // The highest layer the project has not named, which nothing in the project is expected to put an object
        // on. A project that names every layer leaves the source's own, and the camera then also draws whatever
        // else stands on it in its view.
        private static int UnnamedLayer(int fallback)
        {
            for (var layer = 31; layer >= 8; layer--)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(layer)))
                {
                    return layer;
                }
            }
            if (!s_warnedNoLayer)
            {
                s_warnedNoLayer = true;
                FiberLogger.LogWarning("Particles",
                    "Every layer from 8 to 31 is named, so a VisualEffect is rendered on its source's own layer, beside what else stands on it.");
            }
            return fallback;
        }

        private static Camera CreateCamera(VisualEffect host, int layer)
        {
            var go = new GameObject("velvet-particles-effect-camera");
            VelvetObjectUtil.HideFrameworkSceneObject(go);
            go.transform.SetParent(host.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -CameraDistance);
            var camera = go.AddComponent<Camera>();
            // Rendered by the tick alone.
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << layer;
            camera.farClipPlane = 2f * CameraDistance;
            // The texture holds no range above one to render into.
            camera.allowHDR = false;
            return camera;
        }

        // Mount replays the clone from the start with the source's initial event; Manual clears it with no initial
        // event named, so nothing plays it until Play.
        internal static void ApplyPlayTrigger(ParticlesBinding binding)
        {
            var host = binding.EffectHost!;
            host.initialEventName = binding.Settings.PlayOn == PlayTrigger.Mount ? binding.EffectInitialEvent : string.Empty;
            host.Reinit();
        }

        internal static void Play(ParticlesBinding binding) => binding.EffectHost!.Play();

        // Clears the clone's particles without replaying it.
        internal static void Stop(ParticlesBinding binding)
        {
            var host = binding.EffectHost!;
            host.initialEventName = string.Empty;
            host.Reinit();
        }

        internal static void Destroy(ParticlesBinding binding)
        {
            if (binding.EffectHost != null)
            {
                VelvetObjectUtil.Destroy(binding.EffectHost.gameObject);
            }
            if (binding.EffectTexture != null)
            {
                binding.EffectTexture.Release();
                VelvetObjectUtil.Destroy(binding.EffectTexture);
            }
            binding.EffectLayer?.RemoveFromHierarchy();
            binding.EffectHost = null;
            binding.EffectCamera = null;
            binding.EffectTexture = null;
            binding.EffectRenderers = null;
            binding.EffectLayer = null;
        }

        // Renders the clone into a texture the element's laid-out size in pixels, as tall in world units as the
        // element is over PixelsPerUnit, so the clone's units land where V.Particles maps a ParticleSystem's.
        internal static void Render(VisualElement element, ParticlesBinding binding)
        {
            var camera = binding.EffectCamera;
            var w = element.layout.width;
            var h = element.layout.height;
            if (camera == null || w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h))
            {
                return;
            }
            SceneViewDriver.DevicePixels(w, h, element.scaledPixelsPerPoint, out var pw, out var ph);
            var texture = binding.EffectTexture;
            if (texture == null || texture.width != pw || texture.height != ph)
            {
                if (texture != null)
                {
                    texture.Release();
                    VelvetObjectUtil.Destroy(texture);
                }
                texture = new RenderTexture(pw, ph, 24) { name = "velvet-particles-effect" };
                binding.EffectTexture = texture;
            }
            camera.orthographicSize = h / (2f * binding.Settings.PixelsPerUnit);
            camera.aspect = w / h;
            camera.targetTexture = texture;
            var renderers = binding.EffectRenderers!;
            try
            {
                foreach (var renderer in renderers)
                {
                    if (renderer != null)
                    {
                        renderer.forceRenderingOff = false;
                    }
                }
                camera.Render();
            }
            finally
            {
                foreach (var renderer in renderers)
                {
                    if (renderer != null)
                    {
                        renderer.forceRenderingOff = true;
                    }
                }
            }
            binding.EffectLayer!.MarkDirtyRepaint();
        }

        // The texture over the element's laid-out box, drawn with ParticleBlend.Premultiplied rather than UI Toolkit's
        // alpha blend, which would weight each texel's colour by its alpha a second time: the camera clears to
        // transparent black, and a material that blends by its source alpha leaves its colour there weighted by it.
        private static void Paint(MeshGenerationContext mgc, VisualElement view, VisualElement element, ParticlesBinding binding)
        {
            var texture = binding.EffectTexture;
            var w = element.layout.width;
            var h = element.layout.height;
            if (texture == null || w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h))
            {
                return;
            }
            var quad = new Rect(-view.layout.position, new Vector2(w, h));
            var white = (Color32)Color.white;
            var mwd = mgc.Allocate(4, 6, texture);
            mwd.SetNextVertex(new Vertex { position = new Vector3(quad.xMin, quad.yMax, Vertex.nearZ), tint = white, uv = new Vector2(0f, 0f) });
            mwd.SetNextVertex(new Vertex { position = new Vector3(quad.xMin, quad.yMin, Vertex.nearZ), tint = white, uv = new Vector2(0f, 1f) });
            mwd.SetNextVertex(new Vertex { position = new Vector3(quad.xMax, quad.yMin, Vertex.nearZ), tint = white, uv = new Vector2(1f, 1f) });
            mwd.SetNextVertex(new Vertex { position = new Vector3(quad.xMax, quad.yMax, Vertex.nearZ), tint = white, uv = new Vector2(1f, 0f) });
            mwd.SetNextIndex(0);
            mwd.SetNextIndex(1);
            mwd.SetNextIndex(2);
            mwd.SetNextIndex(2);
            mwd.SetNextIndex(3);
            mwd.SetNextIndex(0);
        }
    }
}
