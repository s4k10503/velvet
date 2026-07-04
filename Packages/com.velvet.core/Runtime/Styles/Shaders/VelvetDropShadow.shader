Shader "Velvet/DropShadow"
{
    // Soft, iOS/macOS-style drop shadow rendered with an SDF rounded box so the blur follows the
    // target's corner radius. The C# side passes a small TARGET size in _ElementSize; this pass inflates it by
    // (_BlurRadius + extraPadding) so the blur has room to fade inside the quad, and outputs white RGB with the
    // falloff in alpha (the shadow color/strength is applied later as a tint).
    //
    // This pass is BAKED once into a texture (DropShadowBaker uses Graphics.Blit) and that texture is drawn as
    // a single quad in the caster element's OWN generateVisualContent — FIRST, before the binding repaints the
    // caster's upright fill + border, so the silhouette sits behind the caster's chrome. The bake is the FULL
    // soft silhouette (interior opaque); the repainted opaque fill covers the interior and the offset-up
    // overlap, leaving only the outer halo. It is deliberately NOT used as a live per-element material
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
        _ShadowColor("Shadow Color", Color) = (0, 0, 0, 0.35)
        _BlurRadius("Blur Radius", Range(0, 100)) = 40
        _CornerRadius("Corner Radius", Range(0, 100)) = 32
        _Spread("Spread", Range(0, 50)) = 0
        _ElementSize("Element Size", Vector) = (100, 100, 0, 0)
        // tan() of the caster's skew-x angle. Non-zero shears the silhouette so the shadow follows
        // a skew-x-* caster (baked full-size by DropShadowBaker).
        _SkewX("Skew X (tan)", Range(-2, 2)) = 0
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
            Name "VelvetDropShadow"
            Cull Off
            ZWrite Off
            ZTest Always
            // Baked via Graphics.Blit into an offscreen texture, so write the frag's raw RGBA (white, alpha =
            // SDF falloff) directly with no blending — blending here would mix the alpha with the uninitialized
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

            half4 _ShadowColor;
            half _BlurRadius;
            half _CornerRadius;
            half _Spread;
            float4 _ElementSize;
            float _SkewX;

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
            half sdRoundedBox(float2 p, float2 b, half r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // _ElementSize carries the TARGET element's size (set from C# via SetTargetSize),
                // not this shadow quad's size. The shadow quad is target + padding*2.
                float2 targetSize = _ElementSize.xy;

                float extraPadding = 5.0;
                float padding = _BlurRadius + extraPadding;
                float2 elementSize = targetSize + float2(padding * 2.0, padding * 2.0);

                // UV -> pixel position centered on the shadow quad.
                float2 pixelPos = (input.uv - 0.5) * elementSize;

                // Unshear: evaluate the upright rounded-box SDF at the unsheared coordinate so the
                // rendered alpha follows the skewed caster's silhouette. The quad is padded enough
                // (blur + ExtraPadding per side) for small slant angles to stay inside it.
                pixelPos.x += pixelPos.y * _SkewX;

                // Half the target size is the SDF box's core extent.
                float2 halfSize = targetSize * 0.5 - _Spread;
                halfSize = max(halfSize, float2(1.0, 1.0)); // guard against negative extents

                half dist = sdRoundedBox(pixelPos, halfSize, _CornerRadius);
                dist -= _Spread;

                // Blur radius drives the soft-edge fade range. The silhouette is FULL: the alpha fades
                // SYMMETRICALLY around the box boundary — 1 deep inside, ~0.5 AT the edge, ramping to 0 at
                // +blur/2 outside — a soft edge with NO interior cut and NO hard rim. The interior is left
                // OPAQUE on purpose: the in-element paint draws this offset silhouette FIRST, then repaints the
                // caster's UPRIGHT fill + border over it, so the opaque fill covers the silhouette's interior
                // and its offset-up overlap, leaving only the outer soft halo (mostly below the offset-down
                // box) — exactly the visible part of a behind-the-element drop shadow, with no top shadow and
                // no hard edge.
                half softness = max(_BlurRadius, 1.0);
                half alpha = 1.0 - smoothstep(-softness * 0.5, softness * 0.5, dist);

                // This pass is NOT applied as a live per-element UITK material (style.unityMaterial): UI Toolkit
                // freezes a custom-material element's draw-command order at first generation and never re-derives
                // it, so a shadow whose subtree is first generated under an animating ancestor transform composites
                // IN FRONT of its caster and stays wrong. Instead DropShadowBaker BAKES this pass once into an
                // RGBA texture (Graphics.Blit) and DropShadowSilhouette draws it as a single quad in the caster's
                // own generateVisualContent — FIRST, before it repaints the caster's upright fill + border, so the
                // silhouette renders BEHIND the caster's chrome in plain depth-first order with no custom-material
                // command to misorder. The baked texture is the FULL silhouette (interior included and opaque);
                // the repainted opaque fill covers the interior, leaving only the outer halo. RGB is white and
                // alpha is the falloff, so the runtime shadow color/strength is the quad's vertex tint — letting a
                // color change retint without re-baking.
                half4 shadowColor = half4(1.0, 1.0, 1.0, alpha);

                return shadowColor;
            }
            ENDHLSL
        }
    }
}
