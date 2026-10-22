Shader "Velvet/BoxShadow"
{
    // One layer of a CSS box-shadow. An outer layer is the caster's border box moved by the offset and grown (or
    // shrunk) by the spread, blurred by a Gaussian whose standard deviation is half the blur radius, and cut out
    // where the border box lies, since CSS draws an outer shadow only outside it. An inset layer is the padding
    // box with a hole — the padding box moved by the offset and shrunk by the spread, blurred the same way —
    // drawn only inside the padding box. The C# side passes the clip box (border box or padding box) and the
    // shape (blurred box or hole) as half-sizes, centres relative to the quad's centre and per-corner radii,
    // and the quad's size. It outputs white RGB with the coverage in alpha (the layer's colour is applied later
    // as a tint).
    //
    // This pass is BAKED once into a texture (BoxShadowBaker uses Graphics.Blit) and that texture is drawn as
    // a single quad in the caster element's OWN generateVisualContent. It is deliberately NOT used as a live
    // per-element material (style.unityMaterial): UITK freezes a custom-material element's draw-command order at
    // first generation, so a shadow first generated under an animating ancestor transform would composite in
    // FRONT of its caster. Going through the normal textured-quad path keeps plain depth-first order, with no
    // custom-material command to misorder. The shadow is a NON-structural paint: no wrapper, so it does not
    // alter the caster's layout and it follows a transform on the caster (CSS box-shadow parity).
    //
    // URP only: SubShader is tagged "UniversalPipeline". The showcase project is converted to URP for
    // this reason (see Assets/Editor/URPSetup.cs).
    Properties
    {
        [MainTexture] _MainTex("Texture", 2D) = "white" {}
        _BlurRadius("Blur Radius", Range(0, 100)) = 40
        // xy: the shape's half-size; zw: its centre relative to the quad's centre.
        _Shape("Shape", Vector) = (50, 50, 0, 0)
        // Per-corner radii, top-left, top-right, bottom-right, bottom-left: horizontal, then vertical.
        _ShapeRadii("Shape Radii", Vector) = (0, 0, 0, 0)
        _ShapeRadiiY("Shape Radii Y", Vector) = (0, 0, 0, 0)
        // xy: the clip box's half-size; zw: its centre relative to the quad's centre.
        _Clip("Clip", Vector) = (50, 50, 0, 0)
        _ClipRadii("Clip Radii", Vector) = (0, 0, 0, 0)
        _ClipRadiiY("Clip Radii Y", Vector) = (0, 0, 0, 0)
        // xy: the quad's size.
        _ElementSize("Element Size", Vector) = (100, 100, 0, 0)
        // 1 for an inset layer, 0 for an outer one.
        _Inset("Inset", Float) = 0
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
            float4 _Shape;
            float4 _ShapeRadii;
            float4 _ShapeRadiiY;
            float4 _Clip;
            float4 _ClipRadii;
            float4 _ClipRadiiY;
            float4 _ElementSize;
            float _Inset;
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

            // The radius of the corner p lies towards, y-down: radii are (top-left, top-right, bottom-right,
            // bottom-left).
            float cornerOf(float2 p, float4 radii)
            {
                float top = p.x < 0.0 ? radii.x : radii.y;
                float bottom = p.x < 0.0 ? radii.w : radii.z;
                return p.y < 0.0 ? top : bottom;
            }

            // Signed distance to a rounded box with a radius per corner, each corner's horizontal radius in radii and
            // its vertical one in radiiY. A corner round on both axes alike is measured exactly; one rounded
            // differently on each, by the first-order approximation to an ellipse's distance, which is zero on the
            // curve itself.
            // Ref: Inigo Quilez - https://iquilezles.org/articles/distfunctions2d/
            float sdRoundedBox(float2 p, float2 b, float4 radii, float4 radiiY)
            {
                float rx = cornerOf(p, radii);
                float ry = cornerOf(p, radiiY);
                if (rx == ry)
                {
                    float r = min(rx, min(b.x, b.y));
                    float2 q = abs(p) - b + r;
                    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
                }
                float2 e = min(float2(rx, ry), b);
                float2 d = abs(p) - b + e;
                if (d.x > 0.0 && d.y > 0.0 && e.x > 0.0 && e.y > 0.0)
                {
                    float k0 = length(d / e);
                    float k1 = length(d / (e * e));
                    return k0 * (k0 - 1.0) / k1;
                }
                float2 o = abs(p) - b;
                return length(max(o, 0.0)) + min(max(o.x, o.y), 0.0);
            }

            // A Gaussian-blurred rounded box, after Evan Wallace's "Fast Rounded Rectangle Shadows": exact along
            // x through erf, integrated along y in four samples spanning three standard deviations. A row takes
            // its left and right extents from the corners on its own half of the box.
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

            float extentAt(float y, float corner, float2 halfSize)
            {
                float delta = min(halfSize.y - corner - abs(y), 0.0);
                return halfSize.x - corner + sqrt(max(0.0, corner * corner - delta * delta));
            }

            // As extentAt, for a corner of horizontal radius cx and vertical radius cy.
            float ellipticalExtentAt(float y, float cx, float cy, float2 halfSize)
            {
                if (cx == cy) return extentAt(y, min(cx, min(halfSize.x, halfSize.y)), halfSize);
                cx = min(cx, halfSize.x);
                cy = min(cy, halfSize.y);
                if (cx <= 0.0 || cy <= 0.0) return halfSize.x;
                float delta = min(halfSize.y - cy - abs(y), 0.0) / cy;
                return halfSize.x - cx + cx * sqrt(max(0.0, 1.0 - delta * delta));
            }

            float blurredRowX(float x, float y, float sigma, float4 radii, float4 radiiY, float2 halfSize)
            {
                float left = ellipticalExtentAt(y, y < 0.0 ? radii.x : radii.w, y < 0.0 ? radiiY.x : radiiY.w, halfSize);
                float right = ellipticalExtentAt(y, y < 0.0 ? radii.y : radii.z, y < 0.0 ? radiiY.y : radiiY.z, halfSize);
                float2 integral = 0.5 + 0.5 * erf2(float2(x - right, x + left) * (0.70710678 / sigma));
                return integral.y - integral.x;
            }

            float blurredRoundedBox(float2 p, float2 halfSize, float4 radii, float4 radiiY, float sigma)
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
                    value += blurredRowX(p.x, p.y - y, sigma, radii, radiiY, halfSize) * gaussian(y, sigma) * step;
                    y += step;
                }
                return value;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // UV -> pixel position centered on the quad, y-down as the caster's box is.
                float2 c = (float2(input.uv.x, 1.0 - input.uv.y) - 0.5) * _ElementSize.xy;

                // Unshear: evaluate the upright boxes at the unsheared coordinate so the rendered alpha follows
                // the skewed caster's silhouette. The shear is Tailwind's transform, skewX() skewY():
                // M = [[1 + skewX*skewY, skewX],[skewY, 1]], whose determinant is 1, so its inverse is
                // [[1, -skewX],[-skewY, 1 + skewX*skewY]].
                float2 p = float2(c.x - (_SkewX * c.y), ((1.0 + (_SkewX * _SkewY)) * c.y) - (_SkewY * c.x));

                float2 shapeP = p - _Shape.zw;
                float sigma = _BlurRadius * 0.5;
                float shape;
                if (sigma < 0.01)
                {
                    shape = saturate(0.5 - sdRoundedBox(shapeP, _Shape.xy, _ShapeRadii, _ShapeRadiiY));
                }
                else
                {
                    shape = saturate(blurredRoundedBox(shapeP, _Shape.xy, _ShapeRadii, _ShapeRadiiY, sigma));
                }
                float clip = saturate(0.5 - sdRoundedBox(p - _Clip.zw, _Clip.xy, _ClipRadii, _ClipRadiiY));

                float alpha = _Inset > 0.5 ? clip * (1.0 - shape) : shape * (1.0 - clip);
                return float4(1.0, 1.0, 1.0, alpha);
            }
            ENDHLSL
        }
    }
}
