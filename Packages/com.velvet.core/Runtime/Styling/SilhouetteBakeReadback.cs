using UnityEngine;

namespace Velvet
{
    // Shared bake→readback recipe for a shader-driven silhouette texture (GradientSilhouetteBaker,
    // DropShadowBaker): render a material into a temporary RT, read it back into a persistent Texture2D, then
    // restore whichever RT was active on entry. Every knob the two bakers' current setups actually differ on
    // (RT read/write mode, texture name, hide flags) is a parameter here rather than a hardcoded assumption, so
    // centralizing this cannot silently coerce one baker's texture settings toward the other's.
    internal static class SilhouetteBakeReadback
    {
        internal static Texture2D Bake(Material material, int width, int height,
            RenderTextureFormat rtFormat, RenderTextureReadWrite rtReadWrite,
            TextureFormat textureFormat, bool linear, string? name,
            TextureWrapMode wrapMode, FilterMode filterMode, HideFlags hideFlags)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, rtFormat, rtReadWrite);
            var prev = RenderTexture.active;
            Graphics.Blit(null, rt, material);
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, textureFormat, mipChain: false, linear)
            {
                wrapMode = wrapMode,
                filterMode = filterMode,
                hideFlags = hideFlags,
            };
            if (name != null)
            {
                tex.name = name;
            }
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }
    }
}
