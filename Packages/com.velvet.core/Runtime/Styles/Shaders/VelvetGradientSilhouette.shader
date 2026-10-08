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
        // Interpolation space (0 = sRGB, 1 = OKLab, 2 = sRGB-linear, 3 = OKLCH, 4 = Lab, 5 = LCH, 6 = HSL) and
        // the hue method of a polar one (0 = shorter, 1 = longer, 2 = increasing, 3 = decreasing).
        _Interp("Interp", Float) = 0
        _HueMethod("Hue Method", Float) = 0
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
            #define VELVET_MAX_STOPS 64
            float4 _StopColors[VELVET_MAX_STOPS];
            float _StopPositions[VELVET_MAX_STOPS];
            float _StopHints[VELVET_MAX_STOPS];
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
            float _HueMethod;

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

            // The interpolation spaces — mirrors GradientBackground's C# path so the skew bake and the non-skew
            // bake agree, constants included. Each works on LINEAR rgb where it needs light, so sRGB is
            // decoded and encoded around it. _Interp numbers them as GradientInterp does: 0 sRGB, 1 OKLab,
            // 2 sRGB-linear, 3 OKLCH, 4 Lab, 5 LCH, 6 HSL. A polar space is (c0, c1, hue in degrees).
            float v_srgbToLinear(float c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }
            float v_linearToSrgb(float c) { return c <= 0.0031308 ? c * 12.92 : (1.055 * pow(c, 1.0 / 2.4)) - 0.055; }
            float v_cbrt(float x) { return sign(x) * pow(abs(x), 1.0 / 3.0); }

            float3 v_toLinear(float3 c)
            {
                return float3(v_srgbToLinear(c.r), v_srgbToLinear(c.g), v_srgbToLinear(c.b));
            }

            float3 v_fromLinear(float3 c)
            {
                return float3(v_linearToSrgb(c.r), v_linearToSrgb(c.g), v_linearToSrgb(c.b));
            }

            float3 v_toOklab(float3 c)
            {
                float3 lin = v_toLinear(c);
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
                return v_fromLinear(float3(lr, lg, lb));
            }

            float v_labF(float t) { return t > 0.008856452 ? v_cbrt(t) : ((903.2963 * t) + 16.0) / 116.0; }

            float v_labFInverse(float f)
            {
                float cubed = f * f * f;
                return cubed > 0.008856452 ? cubed : ((116.0 * f) - 16.0) / 903.2963;
            }

            // CIE Lab with a D50 white, as CSS Color 4 defines lab().
            float3 v_toLab(float3 c)
            {
                float3 lin = v_toLinear(c);
                float x = dot(lin, float3(0.4360747, 0.3850649, 0.1430804)) / 0.9642957;
                float y = dot(lin, float3(0.2225045, 0.7168786, 0.0606169));
                float z = dot(lin, float3(0.0139322, 0.0971045, 0.7141733)) / 0.8251046;
                float fx = v_labF(x);
                float fy = v_labF(y);
                float fz = v_labF(z);
                return float3((116.0 * fy) - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
            }

            float3 v_fromLab(float3 lab)
            {
                float fy = (lab.x + 16.0) / 116.0;
                float fx = (lab.y / 500.0) + fy;
                float fz = fy - (lab.z / 200.0);
                float x = v_labFInverse(fx) * 0.9642957;
                float y = lab.x > 8.0 ? fy * fy * fy : lab.x / 903.2963;
                float z = v_labFInverse(fz) * 0.8251046;
                float lr = (3.1338561 * x) - (1.6168667 * y) - (0.4906146 * z);
                float lg = (-0.9787684 * x) + (1.9161415 * y) + (0.0334540 * z);
                float lb = (0.0719453 * x) - (0.2289914 * y) + (1.4052427 * z);
                return v_fromLinear(float3(lr, lg, lb));
            }

            float3 v_toPolar(float3 lab)
            {
                float hue = degrees(atan2(lab.z, lab.y));
                return float3(lab.x, length(lab.yz), hue - 360.0 * floor(hue / 360.0));
            }

            float3 v_fromPolar(float3 lch)
            {
                float hue = radians(lch.z);
                return float3(lch.x, lch.y * cos(hue), lch.y * sin(hue));
            }

            // sRGB → (saturation, lightness, hue in degrees).
            float3 v_toHsl(float3 c)
            {
                float mx = max(c.r, max(c.g, c.b));
                float mn = min(c.r, min(c.g, c.b));
                float d = mx - mn;
                float l = (mx + mn) * 0.5;
                float s = d < 1e-6 ? 0.0 : d / (1.0 - abs((2.0 * l) - 1.0));
                float h = 0.0;
                if (d >= 1e-6)
                {
                    if (mx == c.r)
                    {
                        float k = (c.g - c.b) / d;
                        h = (k - 6.0 * floor(k / 6.0)) * 60.0;
                    }
                    else if (mx == c.g)
                    {
                        h = (((c.b - c.r) / d) + 2.0) * 60.0;
                    }
                    else
                    {
                        h = (((c.r - c.g) / d) + 4.0) * 60.0;
                    }
                }
                return float3(s, l, h);
            }

            float3 v_fromHsl(float3 v)
            {
                float chroma = (1.0 - abs((2.0 * v.y) - 1.0)) * v.x;
                float h = (v.z - 360.0 * floor(v.z / 360.0)) / 60.0;
                float k = h - 2.0 * floor(h / 2.0);
                float x = chroma * (1.0 - abs(k - 1.0));
                float m = v.y - (chroma * 0.5);
                float3 rgb;
                if (h < 1.0) rgb = float3(chroma, x, 0.0);
                else if (h < 2.0) rgb = float3(x, chroma, 0.0);
                else if (h < 3.0) rgb = float3(0.0, chroma, x);
                else if (h < 4.0) rgb = float3(0.0, x, chroma);
                else if (h < 5.0) rgb = float3(x, 0.0, chroma);
                else rgb = float3(chroma, 0.0, x);
                return rgb + m;
            }

            bool v_isPolar(int space) { return space == 3 || space == 5 || space == 6; }

            float3 v_toSpace(float3 c, int space)
            {
                if (space == 1) return v_toOklab(c);
                if (space == 2) return v_toLinear(c);
                if (space == 3) return v_toPolar(v_toOklab(c));
                if (space == 4) return v_toLab(c);
                if (space == 5) return v_toPolar(v_toLab(c));
                if (space == 6) return v_toHsl(c);
                return c;
            }

            float3 v_fromSpace(float3 v, int space)
            {
                float3 rgb = v;
                if (space == 1) rgb = v_fromOklab(v);
                else if (space == 2) rgb = v_fromLinear(v);
                else if (space == 3) rgb = v_fromOklab(v_fromPolar(v));
                else if (space == 4) rgb = v_fromLab(v);
                else if (space == 5) rgb = v_fromLab(v_fromPolar(v));
                else if (space == 6) rgb = v_fromHsl(v);
                return saturate(rgb);
            }

            // Whether a colour in a polar space has no hue: its chroma (saturation for hsl) is nil.
            bool v_hueMissing(float3 c, int space)
            {
                if (space == 6) return c.x < 1e-4;
                if (space == 5) return c.y < 0.01;
                return c.y < 2e-4;
            }

            // The hue between two colours along the arc _HueMethod names: 0 shorter, 1 longer, 2 increasing,
            // 3 decreasing; a colour with no chroma takes the other's hue.
            float v_lerpHue(float3 ca, float3 cb, float t, int space)
            {
                bool missA = v_hueMissing(ca, space);
                bool missB = v_hueMissing(cb, space);
                float from = missA ? (missB ? 0.0 : cb.z) : ca.z;
                float to = missB ? (missA ? 0.0 : ca.z) : cb.z;
                float delta = to - from;
                int method = (int)(_HueMethod + 0.5);
                if (method == 1)
                {
                    if (delta > 0.0 && delta < 180.0) from += 360.0;
                    else if (delta > -180.0 && delta <= 0.0) to += 360.0;
                }
                else if (method == 2)
                {
                    if (delta < 0.0) to += 360.0;
                }
                else if (method == 3)
                {
                    if (delta > 0.0) from += 360.0;
                }
                else
                {
                    if (delta > 180.0) from += 360.0;
                    else if (delta < -180.0) to += 360.0;
                }
                float h = lerp(from, to, t);
                return h - 360.0 * floor(h / 360.0);
            }

            // Lerp two stops in the gradient's interpolation space, the colour weighted by alpha as
            // GradientBackground.Lerp weights it; a fully transparent result keeps the unweighted colour.
            float4 v_gradLerp(float4 a, float4 b, float t)
            {
                int space = (int)(_Interp + 0.5);
                float3 ca = v_toSpace(a.rgb, space);
                float3 cb = v_toSpace(b.rgb, space);
                float alpha = lerp(a.a, b.a, t);
                float3 mixed = alpha > 0.0 ? lerp(ca * a.a, cb * b.a, t) / alpha : lerp(ca, cb, t);
                if (v_isPolar(space))
                {
                    mixed.z = v_lerpHue(ca, cb, t, space);
                }
                return float4(v_fromSpace(mixed, space), alpha);
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
                // The same walk and the same ceiling on t (none for a radial) as GradientBackground.ColorAt
                // (whose comment says what the ceiling settles), in the gradient's interpolation space.
                t = (_Type > 0.5 && _Type < 1.5) ? max(t, 0.0) : clamp(t, 0.0, 1.0 - 1e-6);
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
                        float span = _StopPositions[next] - p0;
                        float mixAmount = (t - p0) / span;
                        // A colour hint moves the half-way mix of the two stops to where it was written.
                        float hint = _StopHints[next - 1];
                        if (hint > -500.0)
                        {
                            float at = (hint - p0) / span;
                            mixAmount = at <= 0.0 ? 1.0 : (at >= 1.0 ? 0.0 : pow(mixAmount, log(0.5) / log(at)));
                        }
                        col = v_gradLerp(_StopColors[next - 1], _StopColors[next], mixAmount);
                    }
                }

                return half4(col.rgb, col.a * mask);
            }
            ENDHLSL
        }
    }
}
