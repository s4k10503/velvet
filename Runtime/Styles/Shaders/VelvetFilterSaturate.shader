Shader "Velvet/FilterSaturate"
{
    // CSS saturate(N): lerp each channel toward the pixel's luminance by (1 - N). UI Toolkit has no built-in
    // filter type for it, so it is bound as a live custom-filter pass (FilterFunctionType.Custom) rather than
    // approximated as grayscale(1 - N). grayscale can only desaturate (N in 0..1); this pass lerps unclamped,
    // so saturate-150 over-saturates past the original, which grayscale can never do.
    //
    // The 0.213/0.715/0.072 weights are the exact luminance coefficients the CSS Filter Effects saturate()
    // 3x3 matrix reduces to (its R-row is 0.213+0.787s, 0.715-0.715s, 0.072-0.072s, which factors to
    // luma + s*(channel - luma)), so this is byte-for-byte the browser's saturate(), not merely similar.
    //
    // A live custom-filter pass runs inside UI Toolkit's own renderer, independent of the active render
    // pipeline, so it follows Unity's Built-in-style filter contract (CGPROGRAM + UnityCG.cginc +
    // UnityUIEFilter.cginc) — NOT the URP Core.hlsl contract the DropShadow/GradientSilhouette shaders use,
    // because those BAKE through Graphics.Blit and this does not.
    //
    // The lerp is done on the ENCODED (gamma) sample and the Linear-output conversion is applied LAST, so it
    // matches the browser, which mixes in gamma space.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Saturate ("Saturate", Float) = 1
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
            float _Saturate;

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

                // Alpha is a CSS RGB-only op, so it passes through untouched. The lerp is linear in rgb, so it
                // holds whether the sample is straight or premultiplied alpha.
                half luma = dot(col.rgb, half3(0.213, 0.715, 0.072));
                col.rgb = saturate(lerp(luma.xxx, col.rgb, _Saturate));

                #if _UIE_OUTPUT_LINEAR
                col.rgb = GammaToLinearSpace(col.rgb);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
