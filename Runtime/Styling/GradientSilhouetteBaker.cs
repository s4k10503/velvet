using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Velvet
{
    // Bakes the Velvet/GradientSilhouette shader (a sheared, rounded, gradient-filled, SDF-antialiased
    // silhouette) into a Texture2D sized to the element's sheared bounding box. SkewSilhouette calls this
    // once per element when a skew-* element also carries a bg-gradient-* (it owns and destroys the result),
    // then draws the texture as a quad in its own generateVisualContent — so the slant can poke beyond the
    // box, which a clipped background-image cannot. The AA is baked into the texture's alpha (the shader's
    // SDF smoothstep), replacing the earlier vertex-textured fan whose triangle edges were not antialiased.
    //
    // Bake-then-draw (not a live material) for the same reason DropShadow bakes: UITK freezes a custom
    // material's draw order under an animating ancestor transform. Returns null on a headless device or a
    // missing shader (non-URP / stripped), so the caller simply paints nothing that frame.
    internal static class GradientSilhouetteBaker
    {
        private const string ShaderPath = "Velvet/GradientSilhouette";

        // AA edge half-width (px) and the bleed margin added around the sheared bounding box so the soft
        // edge is not clipped at the texture border.
        private const float AaHalfWidth = 1f;
        private const float Margin = 2f;

        private static Shader s_shader;
        private static Material s_material;
        private static bool s_warned;

        // Leak guard: every baked texture is HideAndDontSave and survives a play-mode exit without a Domain
        // Reload. The owning binding releases its texture on teardown (Release), but if a play session exits
        // without the reconciler disposing, this registry lets the editor reset destroy the survivors —
        // mirroring GradientBackground / DropShadowBaker, whose textures live in a static cache.
        private static readonly HashSet<Texture2D> s_baked = new();

        private static readonly int FromId = Shader.PropertyToID("_From");
        private static readonly int ViaId = Shader.PropertyToID("_Via");
        private static readonly int ToId = Shader.PropertyToID("_To");
        private static readonly int HasViaId = Shader.PropertyToID("_HasVia");
        private static readonly int FromPosId = Shader.PropertyToID("_FromPos");
        private static readonly int ViaPosId = Shader.PropertyToID("_ViaPos");
        private static readonly int ToPosId = Shader.PropertyToID("_ToPos");
        private static readonly int TypeId = Shader.PropertyToID("_Type");
        private static readonly int CenterId = Shader.PropertyToID("_Center");
        private static readonly int ConicStartId = Shader.PropertyToID("_ConicStart");
        private static readonly int InterpId = Shader.PropertyToID("_Interp");
        private static readonly int AxisStartId = Shader.PropertyToID("_AxisStart");
        private static readonly int AxisEndId = Shader.PropertyToID("_AxisEnd");
        private static readonly int ElementSizeId = Shader.PropertyToID("_ElementSize");
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        private static readonly int RadiiId = Shader.PropertyToID("_Radii");
        private static readonly int SkewXId = Shader.PropertyToID("_SkewX");
        private static readonly int SkewYId = Shader.PropertyToID("_SkewY");
        private static readonly int AaId = Shader.PropertyToID("_AAWidth");

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (s_material != null)
            {
                Object.DestroyImmediate(s_material);
                s_material = null;
            }
            foreach (var tex in s_baked)
            {
                if (tex != null)
                {
                    Object.DestroyImmediate(tex);
                }
            }
            s_baked.Clear();
            s_shader = null;
            s_warned = false;
        }
#endif

        // The sheared bounding-box pixel size for an element of (w,h) skewed by (tanX,tanY): a skew-x shifts
        // the top/bottom edges by ±tanX·h/2, widening x by |tanX|·h (and symmetrically for y). The AA margin
        // keeps the soft edge inside the texture. Public so the caller can size the draw quad to match.
        internal static void QuadSize(float w, float h, float tanX, float tanY, out int quadW, out int quadH)
        {
            quadW = Mathf.Clamp(Mathf.CeilToInt(w + (Mathf.Abs(tanX) * h) + (2f * Margin)), 8, 2048);
            quadH = Mathf.Clamp(Mathf.CeilToInt(h + (Mathf.Abs(tanY) * w) + (2f * Margin)), 8, 2048);
        }

        // Bakes the silhouette for one element. Returns null (caller paints nothing) when there is no
        // graphics device or the shader is unavailable. The caller owns the returned texture.
        internal static Texture2D Bake(GradientSpec spec, float w, float h, float tanX, float tanY, Vector4 radii)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return null;
            }
            if (s_shader == null)
            {
                s_shader = Shader.Find(ShaderPath);
            }
            if (s_shader == null)
            {
                if (!s_warned)
                {
                    FiberLogger.LogWarning("Gradient", $"Shader not found: {ShaderPath}. " +
                        "Ensure the project uses URP and the shader is included in the build.");
                    s_warned = true;
                }
                return null;
            }
            if (s_material == null)
            {
                s_material = new Material(s_shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            QuadSize(w, h, tanX, tanY, out var quadW, out var quadH);

            var m = s_material;
            m.SetVector(FromId, (Vector4)spec.From);
            m.SetVector(ToId, (Vector4)spec.To);
            m.SetVector(ViaId, (Vector4)(spec.HasVia ? spec.Via : spec.From));
            m.SetFloat(HasViaId, spec.HasVia ? 1f : 0f);
            m.SetFloat(FromPosId, spec.FromPos);
            m.SetFloat(ViaPosId, spec.ViaPos);
            m.SetFloat(ToPosId, spec.ToPos);
            m.SetFloat(TypeId, (float)(int)spec.Type);
            m.SetVector(CenterId, new Vector4(spec.CenterX, spec.CenterY, 0f, 0f));
            m.SetFloat(ConicStartId, spec.AngleDeg); // used only for conic
            m.SetFloat(InterpId, spec.Interp == GradientInterp.Oklab ? 1f : 0f);
            GradientBackground.GetAxis(spec.AngleDeg, out var sx, out var sy, out var ex, out var ey);
            m.SetVector(AxisStartId, new Vector4(sx, sy, 0f, 0f));
            m.SetVector(AxisEndId, new Vector4(ex, ey, 0f, 0f));
            m.SetVector(ElementSizeId, new Vector4(w, h, 0f, 0f));
            m.SetVector(QuadSizeId, new Vector4(quadW, quadH, 0f, 0f));
            m.SetVector(RadiiId, new Vector4(
                Mathf.Max(0f, radii.x), Mathf.Max(0f, radii.y), Mathf.Max(0f, radii.z), Mathf.Max(0f, radii.w)));
            m.SetFloat(SkewXId, tanX);
            m.SetFloat(SkewYId, tanY);
            m.SetFloat(AaId, AaHalfWidth);

            var rt = RenderTexture.GetTemporary(
                quadW, quadH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Graphics.Blit(null, rt, m);
            RenderTexture.active = rt;
            // sRGB texture (linear:false) to match GradientBackground's C# bake encoding, so a skewed and a
            // non-skewed element with the same stops display identically regardless of the consuming
            // project's color space (the Linear RT stores the shader's raw output; ReadPixels copies the
            // bytes unchanged; the texture flag only governs how UITK samples them).
            var tex = new Texture2D(quadW, quadH, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.ReadPixels(new Rect(0, 0, quadW, quadH), 0, 0);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            s_baked.Add(tex);
            return tex;
        }

        // Destroys a baked texture and drops it from the leak-guard registry. Called by the owning binding
        // on teardown (SkewSilhouette.Detach), a re-bake, or a gradient clear.
        internal static void Release(Texture2D tex)
        {
            if (tex == null)
            {
                return;
            }
            s_baked.Remove(tex);
            VelvetObjectUtil.Destroy(tex);
        }
    }
}
