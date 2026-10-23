Shader "Velvet/BackdropFilter"
{
    // Paints a backdrop-filter (BackdropRenderer). Pass 0 runs one stage of the layer's functions from a texture the
    // size of the captured region into another; pass 1 runs the last stage and paints it over the target, inside the
    // element's rounded border box, the renderer's clip rectangle and its stencil mask.
    //
    // A stage applies the colour functions before it to every tap, a Gaussian along one axis, and the colour
    // functions after it. Each colour function is an affine map on (r, g, b, a) with its result clamped to 0..1.
    //
    // Drawn inside UI Toolkit's own renderer, so it follows the Built-in-style contract the filter shaders beside it
    // follow (CGPROGRAM + UnityCG.cginc), whatever the render pipeline. The arithmetic runs on the encoded (gamma)
    // value, and the Linear-output conversion comes last, as in Velvet/FilterSaturate.
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _Shadow ("Shadow", 2D) = "black" {}
        _StencilRef ("Stencil reference", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    #define MAX_STEPS 16
    #define MAX_REACH 1024.0

    sampler2D _MainTex;
    sampler2D _Shadow;
    float _Decode;
    float _Mode;
    float2 _Dir;
    float _Sigma;
    float2 _Offset;
    float4 _ShadowColor;
    float4x4 _BeforeM[MAX_STEPS];
    float4 _BeforeB[MAX_STEPS];
    float _BeforeCount;
    float4x4 _AfterM[MAX_STEPS];
    float4 _AfterB[MAX_STEPS];
    float _AfterCount;

    float4 Fetch(float2 uv)
    {
        float4 c = tex2Dlod(_MainTex, float4(uv, 0, 0));
        if (_Decode > 0.5)
        {
            c.rgb = LinearToGammaSpace(c.rgb);
        }
        return c;
    }

    float4 Before(float4 c)
    {
        [loop]
        for (int i = 0; i < MAX_STEPS; i++)
        {
            if (i >= (int)_BeforeCount) break;
            c = saturate(mul(_BeforeM[i], c) + _BeforeB[i]);
        }
        return c;
    }

    float4 After(float4 c)
    {
        [loop]
        for (int i = 0; i < MAX_STEPS; i++)
        {
            if (i >= (int)_AfterCount) break;
            c = saturate(mul(_AfterM[i], c) + _AfterB[i]);
        }
        return c;
    }

    float4 Tap(float2 uv)
    {
        return Before(Fetch(uv));
    }

    float Weight(float x)
    {
        return exp(-(x * x) / (2.0 * _Sigma * _Sigma));
    }

    // Three deviations either side, the reach held to MAX_REACH texels. Over premultiplied colour, so a transparent
    // texel does not darken its neighbours.
    float4 BlurredColour(float2 uv)
    {
        float4 c = Tap(uv);
        if (_Sigma <= 0.0)
        {
            return c;
        }
        float reach = min(ceil(3.0 * _Sigma), MAX_REACH);
        float4 sum = float4(c.rgb * c.a, c.a);
        float total = 1.0;
        [loop]
        for (float i = 1.0; i <= reach; i += 1.0)
        {
            float w = Weight(i);
            float4 a = Tap(uv + i * _Dir);
            float4 b = Tap(uv - i * _Dir);
            sum += (float4(a.rgb * a.a, a.a) + float4(b.rgb * b.a, b.a)) * w;
            total += 2.0 * w;
        }
        sum /= total;
        return sum.a > 0.0 ? float4(sum.rgb / sum.a, sum.a) : float4(0, 0, 0, 0);
    }

    // The alpha of the source at uv - _Offset (mode 1), or of _Shadow at uv (mode 2), blurred along _Dir.
    float BlurredAlpha(float2 uv)
    {
        bool own = _Mode < 1.5;
        float2 at = own ? uv - _Offset : uv;
        float sum = own ? Tap(at).a : tex2Dlod(_Shadow, float4(at, 0, 0)).a;
        float total = 1.0;
        float reach = _Sigma <= 0.0 ? 0.0 : min(ceil(3.0 * _Sigma), MAX_REACH);
        [loop]
        for (float i = 1.0; i <= reach; i += 1.0)
        {
            float w = Weight(i);
            float a = own ? Tap(at + i * _Dir).a : tex2Dlod(_Shadow, float4(at + i * _Dir, 0, 0)).a;
            float b = own ? Tap(at - i * _Dir).a : tex2Dlod(_Shadow, float4(at - i * _Dir, 0, 0)).a;
            sum += (a + b) * w;
            total += 2.0 * w;
        }
        return sum / total;
    }

    // The source over its shadow, both straight colour.
    float4 OverShadow(float2 uv)
    {
        float4 src = Tap(uv);
        float shadowAlpha = _ShadowColor.a * BlurredAlpha(uv);
        float alpha = src.a + shadowAlpha * (1.0 - src.a);
        float3 rgb = src.rgb * src.a + _ShadowColor.rgb * shadowAlpha * (1.0 - src.a);
        return alpha > 0.0 ? float4(rgb / alpha, alpha) : float4(0, 0, 0, 0);
    }

    float4 Stage(float2 uv)
    {
        float4 c = _Mode < 0.5 ? BlurredColour(uv)
            : _Mode < 1.5 ? float4(0, 0, 0, BlurredAlpha(uv))
            : OverShadow(uv);
        return After(c);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        ZWrite Off
        ZTest Always
        Cull Off

        // 0: one stage, into a texture the size of the captured region.
        Pass
        {
            Blend One Zero

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0

            float4 frag (v2f_img i) : SV_Target
            {
                return Stage(i.uv);
            }
            ENDCG
        }

        // 1: the last stage, painted over the target.
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 local : TEXCOORD1;
                float2 tree : TEXCOORD2;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 local : TEXCOORD1;
                float2 tree : TEXCOORD2;
            };

            // The element's border box in its own points, and its corner radii: top-left, top-right, bottom-right,
            // bottom-left.
            float2 _Size;
            float4 _Radii;
            // The renderer's clip rectangle in the render tree's points: xMin, yMin, xMax, yMax.
            float4 _Clip;
            float _Opacity;
            float _Encode;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.local = v.local;
                o.tree = v.tree;
                return o;
            }

            // How much of the pixel the rounded border box covers, anti-aliased over one pixel.
            float Coverage(float2 p)
            {
                float2 halfSize = _Size * 0.5;
                float2 q = p - halfSize;
                float radius = q.y < 0.0 ? (q.x < 0.0 ? _Radii.x : _Radii.y) : (q.x < 0.0 ? _Radii.w : _Radii.z);
                float2 d = abs(q) - halfSize + radius;
                float edge = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - radius;
                float aa = max(fwidth(edge), 1e-4);
                return saturate(0.5 - edge / aa);
            }

            float4 frag (v2f i) : SV_Target
            {
                if (i.tree.x < _Clip.x || i.tree.y < _Clip.y || i.tree.x > _Clip.z || i.tree.y > _Clip.w)
                {
                    discard;
                }
                float4 c = Stage(i.uv);
                if (_Encode > 0.5)
                {
                    c.rgb = GammaToLinearSpace(c.rgb);
                }
                c.a *= Coverage(i.local) * _Opacity;
                return c;
            }
            ENDCG
        }
    }
}
