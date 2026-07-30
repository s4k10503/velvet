using UnityEngine;

namespace Velvet
{
    // Every knob the two bakers' current setups actually differ on (RT read/write mode, texture name, hide
    // flags) is carried here rather than defaulted inside SilhouetteBakeReadback, so centralizing the
    // bake→readback recipe cannot silently coerce one baker's texture settings toward the other's.
    internal readonly struct SilhouetteBakeSettings
    {
        public RenderTextureFormat RtFormat { get; init; }
        public RenderTextureReadWrite RtReadWrite { get; init; }
        public TextureFormat TextureFormat { get; init; }
        public bool Linear { get; init; }
        public string? Name { get; init; }
        public TextureWrapMode WrapMode { get; init; }
        public FilterMode FilterMode { get; init; }
        public HideFlags HideFlags { get; init; }
    }

    // Shared bake→readback recipe for a shader-driven silhouette texture (GradientSilhouetteBaker,
    // DropShadowBaker): render a material into a temporary RT, read it back into a persistent Texture2D, then
    // restore whichever RT was active on entry.
    internal static class SilhouetteBakeReadback
    {
        internal static Texture2D Bake(Material material, int width, int height,
            in SilhouetteBakeSettings settings)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, settings.RtFormat, settings.RtReadWrite);
            var prev = RenderTexture.active;
            Graphics.Blit(null, rt, material);
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, settings.TextureFormat, mipChain: false, settings.Linear)
            {
                wrapMode = settings.WrapMode,
                filterMode = settings.FilterMode,
                hideFlags = settings.HideFlags,
            };
            if (settings.Name != null)
            {
                tex.name = settings.Name;
            }
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }
    }
}
