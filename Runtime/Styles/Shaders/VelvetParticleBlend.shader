Shader "Velvet/ParticleBlend"
{
    // UI Toolkit's own vertex and fragment stages, drawn under the blend factors the material sets instead of
    // UI Toolkit's alpha blend. ParticleBlendMaterials makes one material per blend and ParticlesDriver binds it
    // as the unity-material of a layer it stacks over a Particles element.
    //
    // The tag is what UI Toolkit checks a custom material for, and the three keyword sets are the ones its
    // renderer switches and Shader Graph's UI target declares; the include is the one that target's shim reads.
    Properties
    {
        _SrcBlend ("Source blend", Float) = 5
        _DstBlend ("Destination blend", Float) = 10
        _SrcBlendAlpha ("Source alpha blend", Float) = 1
        _DstBlendAlpha ("Destination alpha blend", Float) = 10
        // 1 composites as a Multiply particle material does: the colour is faded toward white by its alpha first.
        _Multiply ("Multiply", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "isCustomUITKShader" = "true"
        }

        Pass
        {
            Cull Off
            ZWrite Off
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex uie_std_vert
            #pragma fragment frag
            #pragma multi_compile_local _ _UIE_FORCE_GAMMA
            #pragma multi_compile_local _ _UIE_TEXTURE_SLOT_COUNT_4 _UIE_TEXTURE_SLOT_COUNT_2 _UIE_TEXTURE_SLOT_COUNT_1
            #pragma multi_compile_local _ _UIE_RENDER_TYPE_SOLID _UIE_RENDER_TYPE_TEXTURE _UIE_RENDER_TYPE_TEXT _UIE_RENDER_TYPE_GRADIENT
            #include "Internal/UnityUIE.cginc"

            float _Multiply;

            UIE_FRAG_T frag(v2f input) : SV_Target
            {
                UIE_FRAG_T color = uie_std_frag(input);
                if (_Multiply > 0.5)
                {
                    color.rgb = lerp(float3(1, 1, 1), color.rgb, color.a);
                }
                return color;
            }
            ENDCG
        }
    }
}
