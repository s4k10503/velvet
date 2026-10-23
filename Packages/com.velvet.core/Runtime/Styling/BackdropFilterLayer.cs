#nullable enable
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one element carrying backdrop-* utilities, keyed in
    // ReconcilerContext.BackdropBindings by the element itself.
    internal sealed class BackdropBinding
    {
        public readonly BackdropFilterLayer Layer;
        public EventCallback<GeometryChangedEvent> OnGeometry = null!;
        public EventCallback<AttachToPanelEvent> OnAttach = null!;
        public IVisualElementScheduledItem StateTick = null!;

        public BackdropBinding(BackdropFilterLayer layer) => Layer = layer;
    }

    /// <summary>
    /// Paints an element's <c>backdrop-*</c> utilities: an immediate-mode sibling placed directly BEFORE the element
    /// and over its box, which, when the renderer reaches it, copies what the target holds beneath that box and
    /// paints it back filtered.
    /// </summary>
    /// <remarks>
    /// The layer is a sibling rather than a child because a child paints after its parent's background and border,
    /// which the backdrop must not hold.
    /// <para>
    /// A transform, an opacity, a visibility or a display on the element reaches its own subtree only, so the layer is
    /// given each of them itself, as <see cref="RingOverlay"/> gives its band.
    /// </para>
    /// </remarks>
    internal sealed class BackdropFilterLayer : ImmediateModeElement
    {
        // Marks the layer as a reconciler-invisible child of its host (SilhouetteBoundsSpacer.IsSpacer).
        internal const string MarkerClass = "velvet-backdrop-layer";

        private readonly VisualElement _element;
        private BackdropSpec _spec;

        private BackdropFilterLayer(VisualElement element, BackdropSpec spec)
        {
            _element = element;
            _spec = spec;
            name = MarkerClass;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            AddToClassList(MarkerClass);
        }

        public static BackdropBinding Attach(VisualElement element, BackdropSpec spec)
        {
            var binding = new BackdropBinding(new BackdropFilterLayer(element, spec));
            binding.OnGeometry = _ => Place(element, binding);
            element.RegisterCallback(binding.OnGeometry);
            // A keyed move or a z-* relocation re-parents the element without moving its rect (RingOverlay.Attach).
            binding.OnAttach = _ => Place(element, binding);
            element.RegisterCallback(binding.OnAttach);
            // Each frame: an animated transform or opacity reaches the layer, and a sibling the reconciler inserts
            // at the element's slot (LogicalChildSlots.ToPhysical) lands between the layer and the element without the
            // element's box having to move.
            binding.StateTick = element.schedule.Execute(() => Place(element, binding)).Every(StyleAnimateDriver.TickMs);
            Place(element, binding);
            return binding;
        }

        // Queues element for a placement retry at the reconcile boundary, for the create path where it has no parent
        // yet (RingOverlay.RequestPlacement).
        public static void RequestPlacement(ReconcilerContext ctx, VisualElement element)
        {
            if (element.parent == null)
            {
                ctx.PendingBackdropPlacements.Add(element);
            }
        }

        public static void DrainPendingPlacements(ReconcilerContext ctx)
        {
            if (ctx.PendingBackdropPlacements.Count == 0)
            {
                return;
            }
            foreach (var element in ctx.PendingBackdropPlacements)
            {
                if (ctx.BackdropBindings.TryGetValue(element, out var binding))
                {
                    Place(element, binding);
                }
            }
            ctx.PendingBackdropPlacements.Clear();
        }

        public static void Sync(VisualElement element, BackdropBinding binding, BackdropSpec spec)
        {
            binding.Layer._spec = spec;
            Place(element, binding);
        }

        public static void Detach(VisualElement element, BackdropBinding binding)
        {
            element.UnregisterCallback(binding.OnGeometry);
            element.UnregisterCallback(binding.OnAttach);
            binding.StateTick.Pause();
            // The layer lives in the element's parent, so it does not leave with the element's own subtree.
            binding.Layer.RemoveFromHierarchy();
        }

        // Directly BEFORE the element, re-derived on every call: paint order is child order, and the layer has to
        // paint after everything behind the element and before the element itself.
        private static void Place(VisualElement element, BackdropBinding binding)
        {
            var host = element.parent;
            var layer = binding.Layer;
            if (host == null)
            {
                return;
            }
            if (layer.parent != host || host.IndexOf(layer) != host.IndexOf(element) - 1)
            {
                layer.RemoveFromHierarchy();
                host.Insert(host.IndexOf(element), layer);
            }
            var x = element.layout.x;
            var y = element.layout.y;
            // An absolute child's left and top resolve against the host's padding box (RingOverlay.SyncGeometry).
            var hostLeft = host.resolvedStyle.borderLeftWidth;
            var hostTop = host.resolvedStyle.borderTopWidth;
            var style = layer.style;
            style.left = (float.IsNaN(x) ? 0f : x) - (float.IsNaN(hostLeft) ? 0f : hostLeft);
            style.top = (float.IsNaN(y) ? 0f : y) - (float.IsNaN(hostTop) ? 0f : hostTop);
            style.width = float.IsNaN(element.layout.width) ? 0f : element.layout.width;
            style.height = float.IsNaN(element.layout.height) ? 0f : element.layout.height;
            SyncElementState(element, layer);
        }

        private static void SyncElementState(VisualElement element, BackdropFilterLayer layer)
        {
            var rs = element.resolvedStyle;
            var style = layer.style;
            style.opacity = rs.opacity;
            style.visibility = rs.visibility;
            // A world-space panel draws no backdrop (styling-filters.md).
            style.display = DrawnByCameras(element) ? DisplayStyle.None : rs.display;
            var origin = rs.transformOrigin;
            style.transformOrigin = new TransformOrigin(origin.x, origin.y, origin.z);
            style.translate = new Translate(rs.translate.x, rs.translate.y, rs.translate.z);
            style.scale = rs.scale;
            style.rotate = rs.rotate;
        }

        private static bool DrawnByCameras(VisualElement element)
            => element.panel is IRuntimePanel { panelSettings: { renderMode: PanelRenderMode.WorldSpace } };

        protected override void ImmediateRepaint()
        {
            // Between the reconciler inserting a sibling at the element's slot and the next placement, the layer
            // stands before that sibling, which would put the sibling in front of the filtered backdrop.
            var host = parent;
            var next = host == null ? -1 : host.IndexOf(this) + 1;
            if (host == null || next >= host.childCount || host[next] != _element)
            {
                return;
            }
            BackdropRenderer.Draw(this, _element, _spec);
        }
    }

    // Copies the target's pixels beneath a layer and paints them back filtered. The viewport and projection the
    // renderer has set are what map the layer's box onto the target's pixels.
    internal static class BackdropRenderer
    {
        private static readonly Func<RectInt>? s_viewport = Bind<Func<RectInt>>(EngineMember.RendererActiveViewport.ResolveMethod());
        private static readonly Func<Matrix4x4>? s_projection = Bind<Func<Matrix4x4>>(EngineMember.RendererProjection.ResolveMethod());
        private static readonly PropertyInfo? s_worldClip = EngineMember.ElementWorldClip.ResolveProperty();

        private static readonly int s_backdrop = Shader.PropertyToID("_Backdrop");
        private static readonly int s_lod = Shader.PropertyToID("_Lod");
        private static readonly int s_sigma = Shader.PropertyToID("_Sigma");
        private static readonly int s_texel = Shader.PropertyToID("_Texel");
        private static readonly int s_amountsA = Shader.PropertyToID("_AmountsA");
        private static readonly int s_amountsB = Shader.PropertyToID("_AmountsB");
        private static readonly int s_size = Shader.PropertyToID("_Size");
        private static readonly int s_radii = Shader.PropertyToID("_Radii");
        private static readonly int s_clip = Shader.PropertyToID("_Clip");
        private static readonly int s_opacity = Shader.PropertyToID("_Opacity");
        private static readonly int s_linear = Shader.PropertyToID("_Linear");

        // The blur's deviation in the texels of the mip level it reads stays at or under this, which keeps the
        // shader's kernel inside its six-texel reach.
        private const float MaxLevelSigma = 2f;

        private static readonly Vector2[] s_local = new Vector2[4];
        private static readonly Vector2[] s_pixel = new Vector2[4];

        private static Material? s_material;
        private static CommandBuffer? s_commands;

        private static T? Bind<T>(MethodInfo? method) where T : Delegate
        {
            if (method == null)
            {
                return null;
            }
            try
            {
                return (T)Delegate.CreateDelegate(typeof(T), method);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static Material? Material()
        {
            if (s_material != null)
            {
                return s_material;
            }
            var shader = VelvetShaders.Find(VelvetShaders.BackdropFilter, "Backdrop", "backdrop filter");
            if (shader == null)
            {
                return null;
            }
            s_material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return s_material;
        }

        public static void Draw(VisualElement layer, VisualElement element, BackdropSpec spec)
        {
            var width = layer.layout.width;
            var height = layer.layout.height;
            if (!(width > 0f) || !(height > 0f) || s_viewport == null || s_projection == null || s_worldClip == null)
            {
                return;
            }
            var material = Material();
            if (material == null)
            {
                return;
            }
            var viewport = s_viewport();
            var projection = s_projection();
            var modelview = GL.modelview;
            var target = RenderTexture.active;
            var targetWidth = target != null ? target.width : viewport.xMax;
            var targetHeight = target != null ? target.height : viewport.yMax;
            if (!TryProject(projection * modelview, viewport, width, height, out var region, targetWidth, targetHeight))
            {
                return;
            }

            var pixelScale = 0.5f * (((s_pixel[1] - s_pixel[0]).magnitude / width) + ((s_pixel[3] - s_pixel[0]).magnitude / height));
            var blur = spec.AmountOr(BackdropFunction.Blur, 0f);
            if (spec.MeasuredBlur != null)
            {
                blur = StyleRelativeLengths.TryMeasure(element, spec.MeasuredBlur, out var measured) ? Mathf.Max(0f, measured) : 0f;
            }

            var texture = Capture(target, region, targetWidth, targetHeight, viewport, projection, modelview);
            try
            {
                var lod = 0;
                var sigma = blur * pixelScale;
                var levels = texture.mipmapCount;
                while (sigma > MaxLevelSigma && lod < levels - 1)
                {
                    sigma *= 0.5f;
                    lod++;
                }
                material.SetTexture(s_backdrop, texture);
                material.SetFloat(s_lod, lod);
                material.SetFloat(s_sigma, sigma);
                material.SetVector(s_texel, new Vector4(
                    1f / Mathf.Max(1, region.width >> lod), 1f / Mathf.Max(1, region.height >> lod), 0f, 0f));
                material.SetVector(s_amountsA, new Vector4(
                    spec.AmountOr(BackdropFunction.Brightness, 1f),
                    spec.AmountOr(BackdropFunction.Contrast, 1f),
                    spec.AmountOr(BackdropFunction.Grayscale, 0f),
                    spec.AmountOr(BackdropFunction.HueRotate, 0f) * Mathf.Deg2Rad));
                material.SetVector(s_amountsB, new Vector4(
                    spec.AmountOr(BackdropFunction.Invert, 0f),
                    spec.AmountOr(BackdropFunction.Opacity, 1f),
                    spec.AmountOr(BackdropFunction.Saturate, 1f),
                    spec.AmountOr(BackdropFunction.Sepia, 0f)));
                material.SetVector(s_size, new Vector4(width, height, 0f, 0f));
                material.SetVector(s_radii, Radii(element, width, height));
                var clip = (Rect)s_worldClip.GetValue(layer);
                material.SetVector(s_clip, new Vector4(clip.xMin, clip.yMin, clip.xMax, clip.yMax));
                material.SetFloat(s_opacity, CompositeOpacity(layer));
                material.SetFloat(s_linear, QualitySettings.activeColorSpace == ColorSpace.Linear ? 1f : 0f);
                material.SetPass(0);

                GL.Begin(GL.QUADS);
                for (var i = 0; i < 4; i++)
                {
                    var local = s_local[i];
                    var tree = modelview.MultiplyPoint3x4(local);
                    GL.MultiTexCoord2(0, (s_pixel[i].x - region.x) / region.width, (s_pixel[i].y - region.y) / region.height);
                    GL.MultiTexCoord2(1, local.x, local.y);
                    GL.MultiTexCoord2(2, tree.x, tree.y);
                    GL.Vertex3(local.x, local.y, 0f);
                }
                GL.End();
            }
            finally
            {
                RenderTexture.ReleaseTemporary(texture);
            }
        }

        // The layer's corners in its own points and in the target's pixels (origin bottom-left, as the viewport is),
        // and the whole pixels of the target they span. False where none of the box lands on the target.
        private static bool TryProject(Matrix4x4 toClip, RectInt viewport, float width, float height, out RectInt region,
            int targetWidth, int targetHeight)
        {
            region = default;
            s_local[0] = new Vector2(0f, 0f);
            s_local[1] = new Vector2(width, 0f);
            s_local[2] = new Vector2(width, height);
            s_local[3] = new Vector2(0f, height);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < 4; i++)
            {
                var clip = toClip * new Vector4(s_local[i].x, s_local[i].y, 0f, 1f);
                if (!(clip.w > 0f))
                {
                    return false;
                }
                var pixel = new Vector2(
                    viewport.x + (((clip.x / clip.w) * 0.5f) + 0.5f) * viewport.width,
                    viewport.y + (((clip.y / clip.w) * 0.5f) + 0.5f) * viewport.height);
                s_pixel[i] = pixel;
                min = Vector2.Min(min, pixel);
                max = Vector2.Max(max, pixel);
            }
            var x0 = Mathf.Max(0, Mathf.FloorToInt(min.x));
            var y0 = Mathf.Max(0, Mathf.FloorToInt(min.y));
            var x1 = Mathf.Min(targetWidth, Mathf.CeilToInt(max.x));
            var y1 = Mathf.Min(targetHeight, Mathf.CeilToInt(max.y));
            if (x1 <= x0 || y1 <= y0)
            {
                return false;
            }
            region = new RectInt(x0, y0, x1 - x0, y1 - y0);
            return true;
        }

        // The target's pixels in region, with every mip level, sampled mirrored past their edges so a blur reads none
        // of the target outside the box. Copied where the target can be; where it cannot (the back buffer, a
        // multisampled target) blitted, which binds another target, so the blit path binds the renderer's target,
        // viewport and matrices again.
        private static RenderTexture Capture(RenderTexture? target, RectInt region, int targetWidth, int targetHeight,
            RectInt viewport, Matrix4x4 projection, Matrix4x4 modelview)
        {
            var copies = target != null && target.antiAliasing <= 1
                && SystemInfo.copyTextureSupport != CopyTextureSupport.None;
            var format = copies ? target!.graphicsFormat : SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            var descriptor = new RenderTextureDescriptor(region.width, region.height, format, 0)
            {
                useMipMap = true,
                autoGenerateMips = false,
                msaaSamples = 1,
            };
            var texture = RenderTexture.GetTemporary(descriptor);
            texture.wrapMode = TextureWrapMode.Mirror;
            texture.filterMode = FilterMode.Trilinear;
            if (copies)
            {
                Graphics.CopyTexture(target, 0, 0, region.x, region.y, region.width, region.height, texture, 0, 0, 0, 0);
            }
            else
            {
                var commands = s_commands ??= new CommandBuffer { name = "Velvet backdrop" };
                commands.Clear();
                commands.DisableScissorRect();
                commands.Blit(BuiltinRenderTextureType.CurrentActive, texture,
                    new Vector2((float)region.width / targetWidth, (float)region.height / targetHeight),
                    new Vector2((float)region.x / targetWidth, (float)region.y / targetHeight));
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = target;
                GL.Viewport(new Rect(viewport.x, viewport.y, viewport.width, viewport.height));
                GL.LoadProjectionMatrix(projection);
                GL.modelview = modelview;
            }
            texture.GenerateMips();
            return texture;
        }

        // The element's corner radii, scaled as CSS scales radii that overlap: top-left, top-right, bottom-right,
        // bottom-left.
        private static Vector4 Radii(VisualElement element, float width, float height)
        {
            var rs = element.resolvedStyle;
            var fitted = BorderBandPainter.Fitted(CornerRadii.Circular(Sane(rs.borderTopLeftRadius),
                Sane(rs.borderTopRightRadius), Sane(rs.borderBottomRightRadius), Sane(rs.borderBottomLeftRadius)), width, height);
            return new Vector4(fitted.TopLeft.x, fitted.TopRight.x, fitted.BottomRight.x, fitted.BottomLeft.x);
        }

        private static float Sane(float value) => float.IsNaN(value) ? 0f : value;

        // The layer's opacity, which is the element's, times every ancestor's.
        private static float CompositeOpacity(VisualElement layer)
        {
            var opacity = 1f;
            for (var current = layer; current != null; current = current.hierarchy.parent)
            {
                opacity *= current.resolvedStyle.opacity;
            }
            return opacity;
        }
    }
}
