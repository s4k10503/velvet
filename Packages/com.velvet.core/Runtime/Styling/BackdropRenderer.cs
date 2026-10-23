#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Velvet
{
    // Copies the target's pixels beneath a backdrop layer and paints them back through the layer's functions, in
    // their order: each blur and drop shadow is a separable Gaussian run as a horizontal and a vertical pass, and a
    // colour function runs on every tap of the pass after it, or on the pixel in the last one. The viewport and
    // projection the renderer has set map the layer's box onto the target's pixels.
    internal static class BackdropRenderer
    {
        private static readonly Func<RectInt>? s_viewport = Bind<Func<RectInt>>(EngineMember.RendererActiveViewport.ResolveMethod());
        private static readonly Func<Matrix4x4>? s_projection = Bind<Func<Matrix4x4>>(EngineMember.RendererProjection.ResolveMethod());
        private static readonly PropertyInfo? s_worldClip = EngineMember.ElementWorldClip.ResolveProperty();
        private static readonly FieldInfo? s_renderData = EngineMember.ElementRenderData.ResolveField();
        private static readonly FieldInfo? s_nestedRenderData = EngineMember.ElementNestedRenderData.ResolveField();
        private static readonly FieldInfo? s_childrenStencilRef = EngineMember.RenderDataChildrenStencilRef.ResolveField();
        private static readonly FieldInfo? s_childrenMaskDepth = EngineMember.RenderDataChildrenMaskDepth.ResolveField();

        // The colour functions one pass applies, before its taps and after them: the shader's array length.
        internal const int MaxColorSteps = 16;

        private static Material? s_material;
        private static CommandBuffer? s_commands;
        private static readonly BackdropFrame s_frame = new();

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

        // ImmediateModeElement refuses a panel cameras draw, with an error (WorldSpaceImmediateModeTests).
        internal static bool DrawnByCameras(VisualElement element)
            => element.panel is IRuntimePanel { panelSettings: { renderMode: PanelRenderMode.WorldSpace } };

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

        public static void Draw(VisualElement layer, VisualElement element, BackdropStep[] steps)
        {
            var frame = s_frame;
            var material = Material();
            if (material == null || s_viewport == null || s_projection == null || s_worldClip == null
                || !frame.TryBegin(layer, s_viewport(), s_projection()))
            {
                return;
            }
            var captured = frame.Capture();
            var temporaries = new List<RenderTexture> { captured };
            try
            {
                var passes = new BackdropPasses(frame, material, temporaries);
                var last = passes.Run(new BackdropSource(captured, frame.DecodesCapture), steps);
                frame.Rebind();
                DrawFinal(layer, element, material, last);
            }
            finally
            {
                foreach (var texture in temporaries)
                {
                    RenderTexture.ReleaseTemporary(texture);
                }
            }
        }

        private static void DrawFinal(VisualElement layer, VisualElement element, Material material, BackdropPass last)
        {
            var frame = s_frame;
            last.SetOn(material);
            material.SetVector(BackdropShader.Size, new Vector4(frame.Width, frame.Height, 0f, 0f));
            material.SetVector(BackdropShader.Radii, Radii(element, frame.Width, frame.Height));
            var clip = (Rect)s_worldClip!.GetValue(layer);
            material.SetVector(BackdropShader.Clip, new Vector4(clip.xMin, clip.yMin, clip.xMax, clip.yMax));
            material.SetFloat(BackdropShader.Opacity, CompositeOpacity(layer));
            material.SetFloat(BackdropShader.Encode, QualitySettings.activeColorSpace == ColorSpace.Linear ? 1f : 0f);
            var (stencilRef, compare) = StencilTest(layer.hierarchy.parent);
            material.SetFloat(BackdropShader.StencilRef, stencilRef);
            material.SetFloat(BackdropShader.StencilCompare, (float)compare);
            material.SetPass(BackdropShader.FinalPass);
            frame.DrawQuad();
        }

        // The stencil test the renderer draws the layer's siblings under. A mask draws one past the reference into the
        // stencil inside it, and where the children's mask depth stands one past their reference the renderer draws
        // their shapes with the winding reversed, which its stencil state tests with Less where it tests front faces
        // with Equal (BackdropFilterPlaybackTests' rounded-clip and clip-path cases): reference less than the stencil.
        // Equal against 0, as for an element under no mask, where the engine's members do not resolve.
        private static (int Reference, CompareFunction Compare) StencilTest(VisualElement? host)
        {
            if (host == null || s_renderData == null || s_nestedRenderData == null || s_childrenStencilRef == null
                || s_childrenMaskDepth == null)
            {
                return (0, CompareFunction.Equal);
            }
            var data = s_nestedRenderData.GetValue(host) ?? s_renderData.GetValue(host);
            if (data == null)
            {
                return (0, CompareFunction.Equal);
            }
            var reference = (int)s_childrenStencilRef.GetValue(data);
            var depth = (int)s_childrenMaskDepth.GetValue(data);
            return (reference, depth > reference ? CompareFunction.Less : CompareFunction.Equal);
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

        // Runs a pass into a texture the size of the captured region: binds another target, so the frame binds the
        // renderer's again before the layer paints (BackdropFrame.Rebind).
        internal static void Blit(Material material, Texture source, RenderTexture destination)
        {
            var commands = Commands;
            commands.Clear();
            commands.DisableScissorRect();
            commands.Blit(source, destination, material, BackdropShader.StagePass);
            Graphics.ExecuteCommandBuffer(commands);
            s_frame.Rebound = true;
        }

        internal static CommandBuffer Commands => s_commands ??= new CommandBuffer { name = "Velvet backdrop" };
    }

    // The renderer's state while one layer draws, and the region of the target beneath the layer's box.
    internal sealed class BackdropFrame
    {
        private readonly Vector2[] _local = new Vector2[4];
        private readonly Vector2[] _pixel = new Vector2[4];
        private RenderTexture? _target;
        private RectInt _viewport;
        private Matrix4x4 _projection;
        private Matrix4x4 _modelview;
        private int _targetWidth;
        private int _targetHeight;

        public RectInt Region;
        public float Width;
        public float Height;
        // Target pixels per point of the layer's own box, which a blur's deviation and a shadow's offset are in.
        public float PixelScale;
        public bool DecodesCapture;
        public bool Rebound;

        // False where none of the layer's box lands on the target.
        public bool TryBegin(VisualElement layer, RectInt viewport, Matrix4x4 projection)
        {
            Width = layer.layout.width;
            Height = layer.layout.height;
            if (!(Width > 0f) || !(Height > 0f))
            {
                return false;
            }
            _viewport = viewport;
            _projection = projection;
            _modelview = GL.modelview;
            _target = RenderTexture.active;
            _targetWidth = _target != null ? _target.width : viewport.xMax;
            _targetHeight = _target != null ? _target.height : viewport.yMax;
            Rebound = false;
            return TryProject();
        }

        // The corners in the target's pixels (origin bottom-left, as the viewport is) and the whole pixels they span.
        private bool TryProject()
        {
            var toClip = _projection * _modelview;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < 4; i++)
            {
                _local[i] = new Vector2(i is 1 or 2 ? Width : 0f, i >= 2 ? Height : 0f);
                var clip = toClip * new Vector4(_local[i].x, _local[i].y, 0f, 1f);
                if (!(clip.w > 0f))
                {
                    return false;
                }
                _pixel[i] = new Vector2(_viewport.x + (((clip.x / clip.w) * 0.5f) + 0.5f) * _viewport.width,
                    _viewport.y + (((clip.y / clip.w) * 0.5f) + 0.5f) * _viewport.height);
                min = Vector2.Min(min, _pixel[i]);
                max = Vector2.Max(max, _pixel[i]);
            }
            var x0 = Mathf.Max(0, Mathf.FloorToInt(min.x));
            var y0 = Mathf.Max(0, Mathf.FloorToInt(min.y));
            var x1 = Mathf.Min(_targetWidth, Mathf.CeilToInt(max.x));
            var y1 = Mathf.Min(_targetHeight, Mathf.CeilToInt(max.y));
            Region = new RectInt(x0, y0, x1 - x0, y1 - y0);
            PixelScale = 0.5f * (((_pixel[1] - _pixel[0]).magnitude / Width) + ((_pixel[3] - _pixel[0]).magnitude / Height));
            return x1 > x0 && y1 > y0;
        }

        // The target's pixels in the region, sampled mirrored past their edges so a blur reads none of the target
        // outside it. Copied where the target can be; blitted where it cannot (the back buffer, a multisampled
        // target).
        public RenderTexture Capture()
        {
            var copies = _target != null && _target.antiAliasing <= 1
                && SystemInfo.copyTextureSupport != CopyTextureSupport.None;
            var format = copies ? _target!.graphicsFormat : SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            DecodesCapture = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var texture = Temporary(format);
            if (copies)
            {
                Graphics.CopyTexture(_target, 0, 0, Region.x, Region.y, Region.width, Region.height, texture, 0, 0, 0, 0);
                return texture;
            }
            var flips = _target == null;
            var commands = BackdropRenderer.Commands;
            commands.Clear();
            commands.DisableScissorRect();
            commands.Blit(BuiltinRenderTextureType.CurrentActive, texture,
                new Vector2((float)Region.width / _targetWidth, (flips ? -1f : 1f) * Region.height / _targetHeight),
                new Vector2((float)Region.x / _targetWidth,
                    flips ? 1f - ((float)Region.y / _targetHeight) : (float)Region.y / _targetHeight));
            Graphics.ExecuteCommandBuffer(commands);
            Rebound = true;
            return texture;
        }

        public RenderTexture Temporary(GraphicsFormat format)
        {
            var texture = RenderTexture.GetTemporary(new RenderTextureDescriptor(Region.width, Region.height, format, 0)
            {
                msaaSamples = 1,
            });
            texture.wrapMode = TextureWrapMode.Mirror;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        // Binds the renderer's target, viewport and matrices again where a pass bound another target.
        public void Rebind()
        {
            if (!Rebound)
            {
                return;
            }
            RenderTexture.active = _target;
            GL.Viewport(new Rect(_viewport.x, _viewport.y, _viewport.width, _viewport.height));
            GL.LoadProjectionMatrix(_projection);
            GL.modelview = _modelview;
        }

        // The layer's box, each corner carrying where it lies in the region, in its own points, and in the render
        // tree's points, which the renderer's clip rectangle is in.
        public void DrawQuad()
        {
            GL.Begin(GL.QUADS);
            for (var i = 0; i < 4; i++)
            {
                var local = _local[i];
                var tree = _modelview.MultiplyPoint3x4(local);
                GL.MultiTexCoord2(0, (_pixel[i].x - Region.x) / Region.width, (_pixel[i].y - Region.y) / Region.height);
                GL.MultiTexCoord2(1, local.x, local.y);
                GL.MultiTexCoord2(2, tree.x, tree.y);
                GL.Vertex3(local.x, local.y, 0f);
            }
            GL.End();
        }
    }

    // A texture a pass reads, and whether its values are linear ones the encoded arithmetic has to encode first.
    internal readonly struct BackdropSource
    {
        public readonly Texture Texture;
        public readonly bool Decodes;

        public BackdropSource(Texture texture, bool decodes)
        {
            Texture = texture;
            Decodes = decodes;
        }
    }

    // One pass of the shader: what it reads, the colour functions before its taps, its Gaussian along one axis, and
    // the colour functions after them.
    internal sealed class BackdropPass
    {
        public BackdropSource Source;
        public readonly List<BackdropStep> Before = new();
        public readonly List<BackdropStep> After = new();
        public int Mode;
        public Vector2 Direction;
        public float Sigma;
        public Vector2 Offset;
        public Texture? Shadow;
        public Color ShadowColor;

        private static readonly Matrix4x4[] s_matrices = new Matrix4x4[BackdropRenderer.MaxColorSteps];
        private static readonly Vector4[] s_offsets = new Vector4[BackdropRenderer.MaxColorSteps];

        public void SetOn(Material material)
        {
            material.SetTexture(BackdropShader.MainTex, Source.Texture);
            material.SetFloat(BackdropShader.Decode, Source.Decodes ? 1f : 0f);
            material.SetFloat(BackdropShader.Mode, Mode);
            material.SetVector(BackdropShader.Direction, Direction);
            material.SetFloat(BackdropShader.Sigma, Sigma);
            material.SetVector(BackdropShader.Offset, Offset);
            material.SetTexture(BackdropShader.Shadow, Shadow != null ? Shadow : Texture2D.blackTexture);
            material.SetColor(BackdropShader.ShadowColor, ShadowColor);
            SetSteps(material, Before, BackdropShader.BeforeMatrices, BackdropShader.BeforeOffsets, BackdropShader.BeforeCount);
            SetSteps(material, After, BackdropShader.AfterMatrices, BackdropShader.AfterOffsets, BackdropShader.AfterCount);
        }

        private static void SetSteps(Material material, List<BackdropStep> steps, int matrices, int offsets, int count)
        {
            var used = Math.Min(steps.Count, BackdropRenderer.MaxColorSteps);
            for (var i = 0; i < BackdropRenderer.MaxColorSteps; i++)
            {
                (s_matrices[i], s_offsets[i]) = i < used ? BackdropColorMatrix.Of(steps[i]) : (Matrix4x4.identity, Vector4.zero);
            }
            material.SetMatrixArray(matrices, s_matrices);
            material.SetVectorArray(offsets, s_offsets);
            material.SetFloat(count, used);
        }
    }

    // Splits a layer's functions into passes and runs every one but the last, which the layer paints.
    internal sealed class BackdropPasses
    {
        // An intermediate result holds the encoded values the arithmetic runs on, so it is no sRGB texture.
        private const GraphicsFormat Intermediate = GraphicsFormat.R8G8B8A8_UNorm;

        private readonly BackdropFrame _frame;
        private readonly Material _material;
        private readonly List<RenderTexture> _temporaries;
        private BackdropPass _pass = new();

        public BackdropPasses(BackdropFrame frame, Material material, List<RenderTexture> temporaries)
        {
            _frame = frame;
            _material = material;
            _temporaries = temporaries;
        }

        public BackdropPass Run(BackdropSource source, BackdropStep[] steps)
        {
            _pass.Source = source;
            foreach (var step in steps)
            {
                if (step.Function == BackdropFunction.Blur)
                {
                    Blur(step.A * _frame.PixelScale);
                }
                else if (step.Function == BackdropFunction.DropShadow)
                {
                    DropShadow(step);
                }
                else
                {
                    AddColor(step);
                }
            }
            return _pass;
        }

        // Before a pending blur's vertical half, a colour function runs after its taps, and otherwise before the next
        // pass's; a list already holding as many as the shader takes is run first.
        private void AddColor(in BackdropStep step)
        {
            var list = _pass.Sigma > 0f ? _pass.After : _pass.Before;
            if (list.Count == BackdropRenderer.MaxColorSteps)
            {
                _pass = new BackdropPass { Source = Execute() };
                list = _pass.Before;
            }
            list.Add(step);
        }

        // A pending vertical half is finished first, so the horizontal half reads the result of everything before it.
        private void Blur(float sigma)
        {
            if (sigma <= 0f)
            {
                return;
            }
            Finish();
            _pass.Direction = new Vector2(1f / _frame.Region.width, 0f);
            _pass.Sigma = sigma;
            var horizontal = Execute();
            _pass = new BackdropPass
            {
                Source = horizontal,
                Direction = new Vector2(0f, 1f / _frame.Region.height),
                Sigma = sigma,
            };
        }

        // The shadow's alpha blurred along x from the offset source, then along y under the source.
        private void DropShadow(in BackdropStep step)
        {
            Finish();
            var source = _pass.Source;
            var before = new List<BackdropStep>(_pass.Before);
            var sigma = step.C * _frame.PixelScale;
            _pass.Mode = BackdropShader.ShadowAlpha;
            _pass.Direction = new Vector2(1f / _frame.Region.width, 0f);
            _pass.Sigma = sigma;
            _pass.Offset = new Vector2(step.A * _frame.PixelScale / _frame.Region.width,
                -step.B * _frame.PixelScale / _frame.Region.height);
            var alpha = Execute();
            _pass = new BackdropPass
            {
                Source = source,
                Mode = BackdropShader.ShadowCombine,
                Direction = new Vector2(0f, 1f / _frame.Region.height),
                Sigma = sigma,
                Shadow = alpha.Texture,
                ShadowColor = step.Color,
            };
            _pass.Before.AddRange(before);
            _pass = new BackdropPass { Source = Execute() };
        }

        // Runs the pending pass where it blurs or combines, leaving a fresh pass reading its result.
        private void Finish()
        {
            if (_pass.Sigma > 0f || _pass.Mode != BackdropShader.Color)
            {
                _pass = new BackdropPass { Source = Execute() };
            }
        }

        private BackdropSource Execute()
        {
            var destination = _frame.Temporary(Intermediate);
            _temporaries.Add(destination);
            _pass.SetOn(_material);
            BackdropRenderer.Blit(_material, _pass.Source.Texture, destination);
            return new BackdropSource(destination, false);
        }
    }

    // The colour functions as the affine maps the Filter Effects specification gives each, on (r, g, b, a).
    internal static class BackdropColorMatrix
    {
        public static (Matrix4x4 Matrix, Vector4 Offset) Of(in BackdropStep step)
        {
            var a = step.A;
            switch (step.Function)
            {
                case BackdropFunction.Brightness:
                    return (Diagonal(a, 1f), Vector4.zero);
                case BackdropFunction.Contrast:
                    return (Diagonal(a, 1f), Uniform(0.5f - (0.5f * a)));
                case BackdropFunction.Invert:
                    return (Diagonal(1f - (2f * a), 1f), Uniform(a));
                case BackdropFunction.Opacity:
                    return (Diagonal(1f, a), Vector4.zero);
                case BackdropFunction.Grayscale:
                    return (Saturation(1f - a, 0.2126f, 0.7152f, 0.0722f), Vector4.zero);
                case BackdropFunction.Saturate:
                    return (Saturation(a, 0.213f, 0.715f, 0.072f), Vector4.zero);
                case BackdropFunction.Sepia:
                    return (Sepia(1f - a), Vector4.zero);
                default:
                    return (HueRotate(a * Mathf.Deg2Rad), Vector4.zero);
            }
        }

        private static Matrix4x4 Diagonal(float rgb, float alpha)
        {
            var m = Matrix4x4.identity;
            m.m00 = m.m11 = m.m22 = rgb;
            m.m33 = alpha;
            return m;
        }

        private static Vector4 Uniform(float value) => new(value, value, value, 0f);

        // grayscale(1 - s) and saturate(s) share this shape: each row lerps from the luminance weights to its
        // channel by s.
        private static Matrix4x4 Saturation(float s, float r, float g, float b)
            => Rows(new Vector3(r + ((1f - r) * s), g - (g * s), b - (b * s)),
                new Vector3(r - (r * s), g + ((1f - g) * s), b - (b * s)),
                new Vector3(r - (r * s), g - (g * s), b + ((1f - b) * s)));

        private static Matrix4x4 Sepia(float s)
            => Rows(new Vector3(0.393f + (0.607f * s), 0.769f - (0.769f * s), 0.189f - (0.189f * s)),
                new Vector3(0.349f - (0.349f * s), 0.686f + (0.314f * s), 0.168f - (0.168f * s)),
                new Vector3(0.272f - (0.272f * s), 0.534f - (0.534f * s), 0.131f + (0.869f * s)));

        private static Matrix4x4 HueRotate(float radians)
        {
            var c = Mathf.Cos(radians);
            var s = Mathf.Sin(radians);
            return Rows(new Vector3(0.213f + (c * 0.787f) - (s * 0.213f), 0.715f - (c * 0.715f) - (s * 0.715f),
                    0.072f - (c * 0.072f) + (s * 0.928f)),
                new Vector3(0.213f - (c * 0.213f) + (s * 0.143f), 0.715f + (c * 0.285f) + (s * 0.140f),
                    0.072f - (c * 0.072f) - (s * 0.283f)),
                new Vector3(0.213f - (c * 0.213f) - (s * 0.787f), 0.715f - (c * 0.715f) + (s * 0.715f),
                    0.072f + (c * 0.928f) + (s * 0.072f)));
        }

        private static Matrix4x4 Rows(Vector3 r, Vector3 g, Vector3 b)
        {
            var m = Matrix4x4.identity;
            m.SetRow(0, new Vector4(r.x, r.y, r.z, 0f));
            m.SetRow(1, new Vector4(g.x, g.y, g.z, 0f));
            m.SetRow(2, new Vector4(b.x, b.y, b.z, 0f));
            return m;
        }
    }

    // The shader's passes, modes and properties.
    internal static class BackdropShader
    {
        public const int StagePass = 0;
        public const int FinalPass = 1;

        // What a pass computes along its axis: colour, a shadow's alpha, or a shadow's alpha under the source.
        public const int Color = 0;
        public const int ShadowAlpha = 1;
        public const int ShadowCombine = 2;

        public static readonly int MainTex = Shader.PropertyToID("_MainTex");
        public static readonly int Shadow = Shader.PropertyToID("_Shadow");
        public static readonly int Decode = Shader.PropertyToID("_Decode");
        public static readonly int Mode = Shader.PropertyToID("_Mode");
        public static readonly int Direction = Shader.PropertyToID("_Dir");
        public static readonly int Sigma = Shader.PropertyToID("_Sigma");
        public static readonly int Offset = Shader.PropertyToID("_Offset");
        public static readonly int ShadowColor = Shader.PropertyToID("_ShadowColor");
        public static readonly int BeforeMatrices = Shader.PropertyToID("_BeforeM");
        public static readonly int BeforeOffsets = Shader.PropertyToID("_BeforeB");
        public static readonly int BeforeCount = Shader.PropertyToID("_BeforeCount");
        public static readonly int AfterMatrices = Shader.PropertyToID("_AfterM");
        public static readonly int AfterOffsets = Shader.PropertyToID("_AfterB");
        public static readonly int AfterCount = Shader.PropertyToID("_AfterCount");
        public static readonly int Size = Shader.PropertyToID("_Size");
        public static readonly int Radii = Shader.PropertyToID("_Radii");
        public static readonly int Clip = Shader.PropertyToID("_Clip");
        public static readonly int Opacity = Shader.PropertyToID("_Opacity");
        public static readonly int Encode = Shader.PropertyToID("_Encode");
        public static readonly int StencilRef = Shader.PropertyToID("_StencilRef");
        public static readonly int StencilCompare = Shader.PropertyToID("_StencilComp");
    }
}
