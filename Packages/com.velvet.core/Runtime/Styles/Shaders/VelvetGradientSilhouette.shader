Shader "Velvet/GradientSilhouette"
{
    // A sheared, rounded, gradient-filled silhouette with crisp SDF edge antialiasing — the skew-* ×
    // bg-gradient-* fill. SkewSilhouette bakes this once per element (Graphics.Blit at the element's
    // sheared bounding-box size) and draws the result as a textured quad in the element's own
    // generateVisualContent, so the slant pokes beyond the box (a background-image would clip it). It
    // supersedes the earlier vertex-textured fan mesh, whose triangle edges had no antialiasing.
    //
    // The fragment unshears each pixel (the inverse of the [[1,skewX],[skewY,1]] shear about the box
    // centre, matching SkewSilhouette.Shear), evaluates an upright rounded-box SDF for the shape + AA,
    // and fills with the gradient evaluated on the UPRIGHT box (so the gradient runs the same way as the
    // non-skew background, then shears with the geometry). Stop colours arrive as raw
    // Vectors (no gamma conversion); the bake target is a Linear RenderTexture.
    //
    // Baked, NOT a live per-element material: UITK freezes a custom-material element's draw order at first
    // generation, mis-ordering it under an animating ancestor transform (the same reason DropShadow bakes).
    // URP only ("UniversalPipeline").
    Properties
    {
        [MainTexture] _MainTex("Texture", 2D) = "white" {}
        // A linear gradient's unit direction (xy; x right, y down) and the box proportions its line is laid
        // out over (zw), from GradientBackground.LinearDirection.
        _AxisDir("Axis Direction", Vector) = (0, 1, 1, 1)
        _ElementSize("Element Size (px)", Vector) = (100, 100, 0, 0)
        _QuadSize("Quad Size (px)", Vector) = (120, 120, 0, 0)
        // Per-corner radii (px) in (top-left, top-right, bottom-right, bottom-left) order, matching the
        // four corners the Painter2D border stroke (BuildShearedRoundedRect) honors.
        _Radii("Corner Radii (tl,tr,br,bl px)", Vector) = (12, 12, 12, 12)
        _SkewX("Skew X (tan)", Float) = 0
        _SkewY("Skew Y (tan)", Float) = 0
        _AAWidth("AA half-width (px)", Float) = 1
        // Gradient shape: 0 = linear (axis), 1 = radial (centre out to its radii), 2 = conic (sweep).
        _Type("Type", Float) = 0
        // Radial/conic centre in the box UV (0..1), and the conic start angle (CSS degrees, 0 = up).
        _Center("Center", Vector) = (0.5, 0.5, 0, 0)
        _ConicStart("Conic Start (deg)", Float) = 0
        // A radial's radii in the box's pixels, from GradientBackground.RadialRadii.
        _RadialRadii("Radial Radii (px)", Vector) = (1, 1, 0, 0)
        // Interpolation space: 0 = sRGB, 1 = OKLab.
        _Interp("Interp", Float) = 0
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
            Name "VelvetGradientSilhouette"
            Cull Off
            ZWrite Off
            ZTest Always
            // Baked via Graphics.Blit into an offscreen target: write the frag's raw RGBA (straight alpha)
            // with no blending. Compositing happens later when UI Toolkit draws the textured quad.
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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // The colour stops, of which the first _StopCount are in use. GradientSilhouetteBaker sets all
            // three. VELVET_MAX_STOPS is GradientSpec.MaxStops; GradientStopCapacityTests holds the two equal.
            #define VELVET_MAX_STOPS 16
            float4 _StopColors[VELVET_MAX_STOPS];
            float _StopPositions[VELVET_MAX_STOPS];
            float _StopCount;
            float4 _AxisDir;
            float4 _ElementSize;
            float4 _QuadSize;
            float4 _Radii;
            float _SkewX;
            float _SkewY;
            float _AAWidth;
            float _Type;
            float4 _Center;
            float _ConicStart;
            float4 _RadialRadii;
            float _Interp;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // Signed distance to a rounded box (Inigo Quilez, https://iquilezles.org/articles/distfunctions2d/).
            float sdRoundedBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            // OKLab interpolation (Björn Ottosson) — mirrors GradientBackground's C# path so the skew bake
            // and the non-skew bake agree. Operates on LINEAR rgb, so sRGB is decoded/encoded around it.
            float v_srgbToLinear(float c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }
            float v_linearToSrgb(float c) { return c <= 0.0031308 ? c * 12.92 : (1.055 * pow(c, 1.0 / 2.4)) - 0.055; }

            float3 v_toOklab(float3 c)
            {
                float3 lin = float3(v_srgbToLinear(c.r), v_srgbToLinear(c.g), v_srgbToLinear(c.b));
                float l = dot(lin, float3(0.4122214708, 0.5363325363, 0.0514459929));
                float m = dot(lin, float3(0.2119034982, 0.6806995451, 0.1073969566));
                float s = dot(lin, float3(0.0883024619, 0.2817188376, 0.6299787005));
                float3 lms = float3(l, m, s);
                float3 lms_ = sign(lms) * pow(abs(lms), 1.0 / 3.0);
                return float3(
                    dot(lms_, float3(0.2104542553, 0.7936177850, -0.0040720468)),
                    dot(lms_, float3(1.9779984951, -2.4285922050, 0.4505937099)),
                    dot(lms_, float3(0.0259040371, 0.7827717662, -0.8086757660)));
            }

            float3 v_fromOklab(float3 lab)
            {
                float l_ = lab.x + (0.3963377774 * lab.y) + (0.2158037573 * lab.z);
                float m_ = lab.x - (0.1055613458 * lab.y) - (0.0638541728 * lab.z);
                float s_ = lab.x - (0.0894841775 * lab.y) - (1.2914855480 * lab.z);
                float3 lms = float3(l_ * l_ * l_, m_ * m_ * m_, s_ * s_ * s_);
                float lr = dot(lms, float3(4.0767416621, -3.3077115913, 0.2309699292));
                float lg = dot(lms, float3(-1.2684380046, 2.6097574011, -0.3413193965));
                float lb = dot(lms, float3(-0.0041960863, -0.7034186147, 1.7076147010));
                return saturate(float3(v_linearToSrgb(lr), v_linearToSrgb(lg), v_linearToSrgb(lb)));
            }

            // Lerp two stops in the gradient's interpolation space (sRGB channels or OKLab), the colour
            // weighted by alpha as GradientBackground.Lerp weights it; a fully transparent result keeps the
            // unweighted colour.
            float4 v_gradLerp(float4 a, float4 b, float t)
            {
                bool oklab = _Interp > 0.5;
                float3 ca = oklab ? v_toOklab(a.rgb) : a.rgb;
                float3 cb = oklab ? v_toOklab(b.rgb) : b.rgb;
                float alpha = lerp(a.a, b.a, t);
                float3 mixed = alpha > 0.0 ? lerp(ca * a.a, cb * b.a, t) / alpha : lerp(ca, cb, t);
                return float4(oklab ? v_fromOklab(mixed) : mixed, alpha);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 quad = _QuadSize.xy;
                // Blit UV is bottom-left origin; flip Y into the top-left, y-down box space the shear is
                // expressed in. Centre on the box centre (the quad is centred on the element box).
                float2 buv = float2(input.uv.x, 1.0 - input.uv.y);
                float2 c = (buv - 0.5) * quad;

                // Unshear: inverse of M = [[1, skewX],[skewY, 1]] (shear about the box centre).
                float det = 1.0 - (_SkewX * _SkewY);
                det = (abs(det) < 1e-4) ? ((det < 0.0) ? -1e-4 : 1e-4) : det;
                float2 upright = float2(c.x - (_SkewX * c.y), c.y - (_SkewY * c.x)) / det;

                float2 halfSize = _ElementSize.xy * 0.5;
                // Select the corner radius by the quadrant in the UPRIGHT (unsheared) box. y-down: y<0 is
                // the top. _Radii = (tl, tr, br, bl); the SDF near each corner depends only on that radius.
                float2 rr = (upright.x < 0.0) ? float2(_Radii.x, _Radii.w) : float2(_Radii.y, _Radii.z);
                float radius = (upright.y < 0.0) ? rr.x : rr.y;
                radius = min(radius, min(halfSize.x, halfSize.y));
                float dist = sdRoundedBox(upright, halfSize, radius);

                float aa = max(_AAWidth, 1e-3);
                float mask = 1.0 - smoothstep(-aa, aa, dist);

                // Gradient parameter t in the box UV, per type (mirrors GradientBackground.ComputeT).
                float2 size = max(_ElementSize.xy, float2(1.0, 1.0));
                float2 guv = (upright + halfSize) / size;
                float t;
                if (_Type < 0.5) // linear: project the offset from the centre onto the gradient line
                {
                    float lineLength = max(dot(abs(_AxisDir.xy), _AxisDir.zw), 1e-6);
                    t = dot((guv - 0.5) * _AxisDir.zw, _AxisDir.xy) / lineLength + 0.5;
                }
                else if (_Type < 1.5) // radial: elliptical distance over the radii
                {
                    t = length((guv - _Center.xy) * size / _RadialRadii.xy);
                }
                else // conic: clockwise angle from up (0°) in pixel space, minus the start angle, over 360°
                {
                    float2 a = (guv - _Center.xy) * size;
                    float ang = degrees(atan2(a.x, -a.y));
                    // frac(x) = x - floor(x) ∈ [0,1) for any real (incl. negative), so it wraps the angle
                    // exactly like the C# (((x % 360) + 360) % 360) idiom.
                    t = frac((ang - _ConicStart) / 360.0);
                }
                // The same walk and the same ceiling on t as GradientBackground.ColorAt (whose comment says
                // what the ceiling settles), in the gradient's interpolation space.
                t = clamp(t, 0.0, 1.0 - 1e-6);
                int last = (int)_StopCount - 1;
                float4 col;
                if (t < _StopPositions[0])
                {
                    col = _StopColors[0];
                }
                else
                {
                    // The first stop after t, or one past the last when none follows.
                    int next = last + 1;
                    for (int i = 1; i < VELVET_MAX_STOPS; i++)
                    {
                        if (i > last || t < _StopPositions[i])
                        {
                            next = i;
                            break;
                        }
                    }
                    if (next > last)
                    {
                        col = _StopColors[last];
                    }
                    else
                    {
                        float p0 = _StopPositions[next - 1];
                        col = v_gradLerp(_StopColors[next - 1], _StopColors[next], (t - p0) / (_StopPositions[next] - p0));
                    }
                }

                return half4(col.rgb, col.a * mask);
            }
            ENDHLSL
        }
    }
}
