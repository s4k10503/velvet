Shader "Velvet/FilterBrightness"
{
    // CSS brightness(N): a uniform per-channel multiply of the element's rendered colour. UI Toolkit has
    // no built-in filter type for it, so it is bound as a live custom-filter pass (FilterFunctionType.Custom)
    // rather than approximated through the built-in Tint. Tint would clamp the multiplier to [0,1] and could
    // never over-brighten (N>1); this pass multiplies unclamped and only clamps the final output, so
    // brightness-150 genuinely brightens.
    //
    // A live custom-filter pass runs inside UI Toolkit's own renderer, independent of the active render
    // pipeline, so it follows Unity's Built-in-style filter contract (CGPROGRAM + UnityCG.cginc +
    // UnityUIEFilter.cginc) — NOT the URP Core.hlsl contract the DropShadow/GradientSilhouette shaders use,
    // because those BAKE through Graphics.Blit and this does not.
    //
    // The multiply is done on the ENCODED (gamma) sample and the Linear-output conversion is applied LAST,
    // matching browser semantics: CSS multiplies in gamma space. Linearizing first (as the old Tint path did
    // in a Linear-colorspace project) over-darkens relative to CSS.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Brightness ("Brightness", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Blend One Zero
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _UIE_OUTPUT_LINEAR

            #include "UnityCG.cginc"
            #include "UnityUIEFilter.cginc"

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Brightness;

            v2f vert (FilterVertexInput v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                half4 col = tex2D(_MainTex, i.uv);

                // Alpha is a CSS RGB-only op, so it passes through untouched. The multiply is linear in rgb,
                // so it holds whether the sample is straight or premultiplied alpha.
                col.rgb = saturate(col.rgb * _Brightness);

                #if _UIE_OUTPUT_LINEAR
                col.rgb = GammaToLinearSpace(col.rgb);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
