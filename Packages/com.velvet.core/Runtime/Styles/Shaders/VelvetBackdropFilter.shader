Shader "Velvet/BackdropFilter"
{
    // Paints a backdrop-filter: _Backdrop holds what the target held beneath the element's box when the element's
    // turn to paint came (BackdropFilterLayer), and this filters it and paints it back over the same pixels, inside
    // the element's rounded border box and the renderer's clip rectangle.
    //
    // Drawn with GL inside UI Toolkit's own renderer, so it follows the Built-in-style contract the filter shaders
    // beside it follow (CGPROGRAM + UnityCG.cginc), whatever the render pipeline.
    //
    // The blur and every colour function run on the encoded (gamma) value, and the Linear-output conversion comes
    // last, as in Velvet/FilterSaturate. The colour functions are the Filter Effects matrices and transfer
    // functions, each result clamped to 0..1, applied in Tailwind's composition order.
    Properties
    {
        _Backdrop ("Backdrop", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

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

            sampler2D _Backdrop;
            // The mip level the blur reads, its standard deviation in that level's texels, and one texel in uv.
            float _Lod;
            float _Sigma;
            float2 _Texel;
            // brightness, contrast, grayscale, hue-rotate (radians); invert, opacity, saturate, sepia.
            float4 _AmountsA;
            float4 _AmountsB;
            // The element's border box in its own points, and its corner radii: top-left, top-right, bottom-right,
            // bottom-left.
            float2 _Size;
            float4 _Radii;
            // The renderer's clip rectangle in the render tree's points: xMin, yMin, xMax, yMax.
            float4 _Clip;
            float _Opacity;
            float _Linear;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.local = v.local;
                o.tree = v.tree;
                return o;
            }

            float4 Encoded(float2 uv)
            {
                float4 c = tex2Dlod(_Backdrop, float4(uv, 0, _Lod));
                if (_Linear > 0.5)
                {
                    c.rgb = LinearToGammaSpace(c.rgb);
                }
                return c;
            }

            // A Gaussian of _Sigma texels at _Lod, cut at three deviations and at six texels, over premultiplied
            // colour so a transparent texel does not darken its neighbours.
            float4 Blurred(float2 uv)
            {
                if (_Sigma <= 0.0)
                {
                    return Encoded(uv);
                }
                float reach = min(6.0, ceil(3.0 * _Sigma));
                float4 sum = 0;
                float weights = 0;
                [loop]
                for (float y = -reach; y <= reach; y += 1.0)
                {
                    [loop]
                    for (float x = -reach; x <= reach; x += 1.0)
                    {
                        float w = exp(-(x * x + y * y) / (2.0 * _Sigma * _Sigma));
                        float4 c = Encoded(uv + float2(x, y) * _Texel);
                        sum += float4(c.rgb * c.a, c.a) * w;
                        weights += w;
                    }
                }
                sum /= weights;
                return sum.a > 0.0 ? float4(sum.rgb / sum.a, sum.a) : 0;
            }

            float3 ColourMatrix(float3 c, float3 r0, float3 r1, float3 r2)
            {
                return saturate(float3(dot(r0, c), dot(r1, c), dot(r2, c)));
            }

            float4 Filtered(float4 c)
            {
                float brightness = _AmountsA.x;
                float contrast = _AmountsA.y;
                float grayscale = 1.0 - _AmountsA.z;
                float hue = _AmountsA.w;
                float invert = _AmountsB.x;
                float opacity = _AmountsB.y;
                float saturation = _AmountsB.z;
                float sepia = 1.0 - _AmountsB.w;

                c.rgb = saturate(c.rgb * brightness);
                c.rgb = saturate((c.rgb - 0.5) * contrast + 0.5);
                c.rgb = ColourMatrix(c.rgb,
                    float3(0.2126 + 0.7874 * grayscale, 0.7152 - 0.7152 * grayscale, 0.0722 - 0.0722 * grayscale),
                    float3(0.2126 - 0.2126 * grayscale, 0.7152 + 0.2848 * grayscale, 0.0722 - 0.0722 * grayscale),
                    float3(0.2126 - 0.2126 * grayscale, 0.7152 - 0.7152 * grayscale, 0.0722 + 0.9278 * grayscale));
                float cosH = cos(hue);
                float sinH = sin(hue);
                c.rgb = ColourMatrix(c.rgb,
                    float3(0.213 + cosH * 0.787 - sinH * 0.213, 0.715 - cosH * 0.715 - sinH * 0.715, 0.072 - cosH * 0.072 + sinH * 0.928),
                    float3(0.213 - cosH * 0.213 + sinH * 0.143, 0.715 + cosH * 0.285 + sinH * 0.140, 0.072 - cosH * 0.072 - sinH * 0.283),
                    float3(0.213 - cosH * 0.213 - sinH * 0.787, 0.715 - cosH * 0.715 + sinH * 0.715, 0.072 + cosH * 0.928 + sinH * 0.072));
                c.rgb = saturate(c.rgb * (1.0 - 2.0 * invert) + invert);
                c.a = saturate(c.a * opacity);
                c.rgb = ColourMatrix(c.rgb,
                    float3(0.213 + 0.787 * saturation, 0.715 - 0.715 * saturation, 0.072 - 0.072 * saturation),
                    float3(0.213 - 0.213 * saturation, 0.715 + 0.285 * saturation, 0.072 - 0.072 * saturation),
                    float3(0.213 - 0.213 * saturation, 0.715 - 0.715 * saturation, 0.072 + 0.928 * saturation));
                c.rgb = ColourMatrix(c.rgb,
                    float3(0.393 + 0.607 * sepia, 0.769 - 0.769 * sepia, 0.189 - 0.189 * sepia),
                    float3(0.349 - 0.349 * sepia, 0.686 + 0.314 * sepia, 0.168 - 0.168 * sepia),
                    float3(0.272 - 0.272 * sepia, 0.534 - 0.534 * sepia, 0.131 + 0.869 * sepia));
                return c;
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
                float4 c = Filtered(Blurred(i.uv));
                if (_Linear > 0.5)
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
