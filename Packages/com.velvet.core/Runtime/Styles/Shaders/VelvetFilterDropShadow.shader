Shader "Velvet/FilterDropShadow"
{
    // CSS drop-shadow(): the input's alpha, offset, blurred by a Gaussian whose standard deviation is the
    // function's third length, drawn in the shadow colour and composited below the input. Bound as a live
    // custom-filter definition (BuiltInFilterDefinitions.DropShadow), written against the same Built-in-style
    // filter contract as Velvet/FilterBrightness.
    //
    // The blur is separable and runs as two passes, horizontal then vertical. Pass 1 composites the input it
    // reads as _DropShadowSource, which pass 0's settings callback carries over from its own input: a pass
    // reads only its predecessor's output, and pass 0 replaces the colour with the shadow's alpha. Pass 0 owns
    // every margin, so both passes and the input cover one rect at one size and share its UVs. Both rest on
    // UI Toolkit's compositor, which DropShadowFilterPlaybackTests' hard offset case renders through.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Blend One Zero
        ZWrite Off
        ZTest Always
        Cull Off

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "UnityUIEFilter.cginc"

        sampler2D _MainTex;
        float4 _MainTex_ST;
        sampler2D _DropShadowSource;
        // xy: the offset in points.
        float4 _DropShadowOffset;
        // The standard deviation in pixels.
        float _DropShadowSigma;
        // Premultiplied, in the space the pass reads.
        float4 _DropShadowColor;

        struct v2f
        {
            float4 vertex : SV_POSITION;
            float2 uv : TEXCOORD0;
            float4 rect : TEXCOORD1;
            // The element-space position, in points, which the offset is measured in.
            float2 pos : TEXCOORD2;
        };

        v2f vert (FilterVertexInput v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.uv = TRANSFORM_TEX(v.uv, _MainTex);
            o.rect = GetFilterUVRect(GetFilterRectIndex(v));
            o.pos = v.vertex.xy;
            return o;
        }

        // The input's alpha at uv, transparent outside its rect. A texel past the rect is left in: the atlas
        // block clears the band around it, and dropping it would cut the bilinear read of an edge texel.
        float AlphaAt(sampler2D tex, float2 uv, float4 rect, float2 texel)
        {
            float2 lo = rect.xy - texel;
            float2 hi = rect.xy + rect.zw + texel;
            if (any(uv < lo) || any(uv > hi))
            {
                return 0;
            }
            return tex2Dlod(tex, float4(uv, 0, 0)).a;
        }

        // A Gaussian of the input's alpha along stepUv, one pixel per step, out to three deviations. Each read
        // falls between two taps so the bilinear filter weighs them, and past 64 taps a side the taps spread.
        float BlurredAlpha(sampler2D tex, float2 uv, float2 stepUv, float4 rect, float2 texel)
        {
            if (_DropShadowSigma < 0.05)
            {
                return AlphaAt(tex, uv, rect, texel);
            }
            float radius = ceil(3.0 * _DropShadowSigma);
            float spacing = max(1.0, radius / 64.0);
            int taps = (int)ceil(radius / spacing);
            float inv2s2 = 0.5 / (_DropShadowSigma * _DropShadowSigma);
            float sum = 0;
            float total = 0;
            [loop]
            for (int k = -taps; k <= taps; k += 2)
            {
                float x0 = k * spacing;
                float x1 = (k + 1) * spacing;
                float w0 = exp(-x0 * x0 * inv2s2);
                float w1 = k + 1 <= taps ? exp(-x1 * x1 * inv2s2) : 0;
                float w = w0 + w1;
                float x = x0 + (x1 - x0) * (w1 / w);
                sum += w * AlphaAt(tex, uv + stepUv * x, rect, texel);
                total += w;
            }
            return sum / total;
        }
        ENDCG

        // 0: the input's alpha, offset and blurred along x.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 frag (v2f i) : SV_Target
            {
                float2 texel = float2(abs(ddx(i.uv.x)), abs(ddy(i.uv.y)));
                // The ratio keeps the sign of the uv axis against the element's, which may run either way.
                float2 uvPerPoint = float2(ddx(i.uv.x) / ddx(i.pos.x), ddy(i.uv.y) / ddy(i.pos.y));
                float2 source = i.uv - _DropShadowOffset.xy * uvPerPoint;
                float a = BlurredAlpha(_MainTex, source, float2(texel.x, 0), i.rect, texel);
                return float4(0, 0, 0, a);
            }
            ENDCG
        }

        // 1: pass 0's alpha blurred along y, tinted, with the input composited over it.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _UIE_OUTPUT_LINEAR

            float4 frag (v2f i) : SV_Target
            {
                float2 texel = float2(abs(ddx(i.uv.x)), abs(ddy(i.uv.y)));
                float a = BlurredAlpha(_MainTex, i.uv, float2(0, texel.y), i.rect, texel);
                float4 src = tex2D(_DropShadowSource, i.uv);
                float4 col = src + _DropShadowColor * a * (1 - src.a);

                #if _UIE_OUTPUT_LINEAR
                // The colour is premultiplied, and the conversion applies to the straight colour.
                if (col.a > 0)
                {
                    col.rgb = GammaToLinearSpace(col.rgb / col.a) * col.a;
                }
                #endif

                return col;
            }
            ENDCG
        }
    }
}
