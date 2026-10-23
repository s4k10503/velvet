#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Velvet
{
    // How a Particles element composites what it draws over what lies beneath it.
    internal enum ParticleBlend
    {
        Alpha,
        Additive,
        Multiply,
        // A picture whose colour is already multiplied by its alpha: a VisualEffect rendered over a clear texture.
        Premultiplied,
    }

    // The material each blend but Alpha draws under, a Velvet/ParticleBlend instance whose factors are that blend's,
    // and the reading of a particle material's blend that picks one.
    internal static class ParticleBlendMaterials
    {
        private static readonly Material?[] s_materials = new Material?[4];
        private static Shader? s_shader;

        // Shaders that declare no blend factors as properties, read by name: Unity's legacy and mobile particle
        // shaders that add or multiply.
        private static readonly Dictionary<string, ParticleBlend> s_namedBlends = new()
        {
            ["Legacy Shaders/Particles/Additive"] = ParticleBlend.Additive,
            ["Mobile/Particles/Additive"] = ParticleBlend.Additive,
            ["Legacy Shaders/Particles/Multiply"] = ParticleBlend.Multiply,
            ["Mobile/Particles/Multiply"] = ParticleBlend.Multiply,
        };

        // Null for Alpha, which UI Toolkit's own blend draws, and while the shader cannot be found, which leaves the
        // layer alpha-blended.
        internal static Material? For(ParticleBlend blend)
        {
            if (blend == ParticleBlend.Alpha)
            {
                return null;
            }
            var material = s_materials[(int)blend];
            if (material != null)
            {
                return material;
            }
            s_shader ??= VelvetShaders.Find(VelvetShaders.ParticleBlend, "Particles", "blend other than alpha");
            if (s_shader == null)
            {
                return null;
            }
            material = new Material(s_shader) { name = "velvet-particle-blend-" + blend, hideFlags = HideFlags.HideAndDontSave };
            // The colour factors URP's particle materials set for each blend, and the alpha factors its UI shaders set.
            switch (blend)
            {
                case ParticleBlend.Additive:
                    SetFactors(material, BlendMode.SrcAlpha, BlendMode.One, BlendMode.One, BlendMode.OneMinusSrcAlpha);
                    break;
                case ParticleBlend.Multiply:
                    SetFactors(material, BlendMode.DstColor, BlendMode.Zero, BlendMode.Zero, BlendMode.One);
                    material.SetFloat("_Multiply", 1f);
                    break;
                default:
                    SetFactors(material, BlendMode.One, BlendMode.OneMinusSrcAlpha, BlendMode.One, BlendMode.OneMinusSrcAlpha);
                    break;
            }
            s_materials[(int)blend] = material;
            return material;
        }

        private static void SetFactors(Material material, BlendMode src, BlendMode dst, BlendMode srcAlpha, BlendMode dstAlpha)
        {
            material.SetFloat("_SrcBlend", (float)src);
            material.SetFloat("_DstBlend", (float)dst);
            material.SetFloat("_SrcBlendAlpha", (float)srcAlpha);
            material.SetFloat("_DstBlendAlpha", (float)dstAlpha);
        }

        // A material that adds is one whose destination factor is One, and one that multiplies scales the destination
        // by the source colour; a shader that declares no factors is read by name, and every other material,
        // Premultiply among them, draws alpha-blended.
        internal static ParticleBlend Of(Material? material)
        {
            if (material == null)
            {
                return ParticleBlend.Alpha;
            }
            if (material.HasProperty("_SrcBlend") && material.HasProperty("_DstBlend"))
            {
                var src = (BlendMode)Mathf.RoundToInt(material.GetFloat("_SrcBlend"));
                var dst = (BlendMode)Mathf.RoundToInt(material.GetFloat("_DstBlend"));
                if (dst == BlendMode.One)
                {
                    return ParticleBlend.Additive;
                }
                return (src == BlendMode.DstColor && dst == BlendMode.Zero) || (src == BlendMode.Zero && dst == BlendMode.SrcColor)
                    ? ParticleBlend.Multiply
                    : ParticleBlend.Alpha;
            }
            if (material.shader == null)
            {
                return ParticleBlend.Alpha;
            }
            return s_namedBlends.TryGetValue(material.shader.name, out var named) ? named : ParticleBlend.Alpha;
        }

        // The colour a particle shader multiplies its vertex colour by: URP's base colour, a legacy shader's tint, which
        // those shaders double, or the Built-in Standard Particle shaders' colour.
        internal static Color TintOf(Material? material)
        {
            if (material == null)
            {
                return Color.white;
            }
            if (material.HasProperty("_BaseColor"))
            {
                return material.GetColor("_BaseColor");
            }
            if (material.HasProperty("_TintColor"))
            {
                return material.GetColor("_TintColor") * 2f;
            }
            return material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        }
    }
}
