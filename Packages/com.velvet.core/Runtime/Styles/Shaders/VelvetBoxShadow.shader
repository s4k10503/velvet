Shader "Velvet/BoxShadow"
{
    // One layer of a CSS box-shadow: the caster's border box moved by the offset and grown (or shrunk) by the
    // spread, blurred by a Gaussian whose standard deviation is half the blur radius, and cut out where the
    // caster's own border box lies, since CSS draws an outer shadow only outside it. The C# side passes the
    // caster's size in _ElementSize.xy and the quad's size in .zw: the shape padded by three standard deviations
    // per side, grown by the shear's overhang. It outputs white RGB with the coverage in alpha (the layer's
    // colour is applied later as a tint).
    //
    // This pass is BAKED once into a texture (BoxShadowBaker uses Graphics.Blit) and that texture is drawn as
    // a single quad in the caster element's OWN generateVisualContent, before the binding repaints the caster's
    // upright fill + border. It is deliberately NOT used as a live per-element material
    // (style.unityMaterial): UITK freezes a custom-material element's draw-command order at first generation, so
    // a shadow first generated under an animating ancestor transform would composite in FRONT of its caster.
    // Going through the normal textured-quad path keeps plain depth-first order, with no custom-material command
    // to misorder. The shadow is a NON-structural paint: no wrapper, so it does not alter the caster's layout
    // and it follows a transform on the caster (CSS box-shadow parity).
    //
    // URP only: SubShader is tagged "UniversalPipeline". The showcase project is converted to URP for
    // this reason (see Assets/Editor/URPSetup.cs).
    Properties
    {
        [MainTexture] _MainTex("Texture", 2D) = "white" {}
        _BlurRadius("Blur Radius", Range(0, 100)) = 40
        // The caster's corner radius, and the shadow shape's after the spread (CSS's spread-radius rule).
        _CornerRadius("Corner Radius", Range(0, 100)) = 32
        _ShapeRadius("Shape Corner Radius", Range(0, 150)) = 32
        _Spread("Spread", Range(-50, 50)) = 0
        _Offset("Offset", Vector) = (0, 0, 0, 0)
        _ElementSize("Element Size", Vector) = (100, 100, 0, 0)
        // tan() of the caster's skew-x and skew-y angles. Non-zero shears the silhouette so the shadow
        // follows a skewed caster (baked full-size by BoxShadowBaker).
        _SkewX("Skew X (tan)", Range(-2, 2)) = 0
        _SkewY("Skew Y (tan)", Range(-2, 2)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent-1"
            "RenderType" = "Transparent"
            "isCustomUITKShader" = "true"
        }

        Pass
        {
            Name "VelvetBoxShadow"
            Cull Off
            ZWrite Off
            ZTest Always
            // Baked via Graphics.Blit into an offscreen texture, so write the frag's raw RGBA (white, alpha =
            // coverage) directly with no blending — blending here would mix the alpha with the uninitialized
            // render-target and corrupt the bake. Compositing happens later on the in-element shadow quad.
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            float _BlurRadius;
            float _CornerRadius;
            float _ShapeRadius;
            float _Spread;
            float4 _Offset;
            float4 _ElementSize;
            float _SkewX;
            float _SkewY;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            // Signed distance to a rounded box.
            // Ref: Inigo Quilez - https://iquilezles.org/articles/distfunctions2d/
            float sdRoundedBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            // A Gaussian-blurred rounded box, after Evan Wallace's "Fast Rounded Rectangle Shadows": exact along
            // x through erf, integrated along y in four samples spanning three standard deviations.
            float gaussian(float x, float sigma)
            {
                return exp(-(x * x) / (2.0 * sigma * sigma)) / (2.50662827463 * sigma);
            }

            float2 erf2(float2 x)
            {
                float2 s = sign(x);
                float2 a = abs(x);
                x = 1.0 + (0.278393 + (0.230389 + 0.078108 * (a * a)) * a) * a;
                x *= x;
                return s - s / (x * x);
            }

            float blurredRowX(float x, float y, float sigma, float corner, float2 halfSize)
            {
                float delta = min(halfSize.y - corner - abs(y), 0.0);
                float curved = halfSize.x - corner + sqrt(max(0.0, corner * corner - delta * delta));
                float2 integral = 0.5 + 0.5 * erf2((x + float2(-curved, curved)) * (0.70710678 / sigma));
                return integral.y - integral.x;
            }

            float blurredRoundedBox(float2 p, float2 halfSize, float corner, float sigma)
            {
                float low = p.y - halfSize.y;
                float high = p.y + halfSize.y;
                float start = clamp(-3.0 * sigma, low, high);
                float end = clamp(3.0 * sigma, low, high);
                float step = (end - start) / 4.0;
                float y = start + step * 0.5;
                float value = 0.0;
                for (int i = 0; i < 4; i++)
                {
                    value += blurredRowX(p.x, p.y - y, sigma, corner, halfSize) * gaussian(y, sigma) * step;
                    y += step;
                }
                return value;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 targetSize = _ElementSize.xy;
                float2 quadSize = _ElementSize.zw;

                // UV -> pixel position centered on the shadow quad, y-down as the caster's box is.
                float2 c = (float2(input.uv.x, 1.0 - input.uv.y) - 0.5) * quadSize;

                // Unshear: evaluate the upright shape at the unsheared coordinate so the rendered alpha follows
                // the skewed caster's silhouette. The shear is Tailwind's transform, skewX() skewY():
                // M = [[1 + skewX*skewY, skewX],[skewY, 1]], whose determinant is 1, so its inverse is
                // [[1, -skewX],[-skewY, 1 + skewX*skewY]]. The result is relative to the shape's centre.
                float2 p = float2(c.x - (_SkewX * c.y), ((1.0 + (_SkewX * _SkewY)) * c.y) - (_SkewY * c.x));

                float2 shapeHalf = max(targetSize * 0.5 + _Spread, 0.0);
                float shapeCorner = min(_ShapeRadius, min(shapeHalf.x, shapeHalf.y));
                float sigma = _BlurRadius * 0.5;
                float coverage;
                if (sigma < 0.01)
                {
                    coverage = saturate(0.5 - sdRoundedBox(p, shapeHalf, shapeCorner));
                }
                else
                {
                    coverage = blurredRoundedBox(p, shapeHalf, shapeCorner, sigma);
                }

                // The caster's border box sits at -offset from the shape's centre; nothing is drawn inside it.
                float2 boxHalf = targetSize * 0.5;
                float boxCorner = min(_CornerRadius, min(boxHalf.x, boxHalf.y));
                float inside = saturate(0.5 - sdRoundedBox(p + _Offset.xy, boxHalf, boxCorner));

                return float4(1.0, 1.0, 1.0, saturate(coverage) * (1.0 - inside));
            }
            ENDHLSL
        }
    }
}
