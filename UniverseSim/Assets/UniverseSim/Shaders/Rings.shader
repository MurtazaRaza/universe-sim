// Planetary rings as visual-only geometry (SPEC FR-REN-10, P6-04): a flat annulus in the planet's equatorial
// plane (the mesh's local XZ plane, outer edge at radius 1). The radial optical depth profile (_Profile, red
// channel, over [_InnerFraction, 1]) comes from the core's ring data; RingSystems.cs builds it.
//
// Opacity follows the slant optical depth along the line of sight, 1 - exp(-tau / |mu|) with mu the sine of
// the viewing elevation, so a ring seen edge-on becomes opaque and thin and a sparse ring stays translucent.
// The side facing the light is lit; the other side gets a fraction of it (light that passed through), a
// display choice. The planet's shadow is the ray from the ring toward the light hitting the planet, an
// ellipsoid given by its world-to-local matrix (the body mesh is a unit sphere).
//
// In the stylized style (A-09, keyword USIM_STYLE_STYLIZED) the optical depth also sets the color, from a sparse to
// a dense palette color, and the opacity and brightness are raised (docs/ART_DIRECTION.md section 6). The radii,
// profile, slant opacity, lit and unlit sides and the shadow are as above. The parameters are globals set by
// StylizedRings.cs, neutral at zero (x = 0 draws the realistic rings):
// - _UsimRings: x the optical depth where the dense color is reached, y optical depth gain - 1, z brightness - 1.
// - _UsimRingSparse, _UsimRingDense: the palette's two colors (linear).
// Color luminance, opacity and brightness all rise with the optical depth, so a denser region never looks thinner.
Shader "UniverseSim/Rings"
{
    Properties
    {
        _BaseColor ("Base color (neutral, not a measured albedo)", Color) = (0.75, 0.72, 0.66, 1)
        _InnerFraction ("Inner edge / outer edge (set per ring system)", Float) = 0.5
        _LightDirection ("Light direction (set per ring system)", Vector) = (0, 1, 0, 0)
        _UnlitSide ("Brightness of the side away from the light (display)", Range(0, 1)) = 0.3
        _Profile ("Optical depth profile (set per ring system)", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ USIM_STYLE_STYLIZED
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _InnerFraction;
                float4 _LightDirection;
                half _UnlitSide;
                float4x4 _PlanetWorldToLocal;
            CBUFFER_END

            TEXTURE2D(_Profile);
            SAMPLER(sampler_Profile);

            // The stylized rings (see the top of this file); global, neutral at zero.
            float4 _UsimRings;
            half4 _UsimRingSparse;
            half4 _UsimRingDense;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            // 1 in sunlight, 0 in the planet's shadow (with a soft edge).
            float PlanetShadow(float3 positionWS, float3 light)
            {
                float3 p = mul(_PlanetWorldToLocal, float4(positionWS, 1)).xyz;
                float3 d = mul(_PlanetWorldToLocal, float4(light, 0)).xyz;
                float a = dot(d, d);
                float t = -dot(p, d) / a; // closest approach of the ray to the planet's center
                if (t <= 0.0)
                {
                    return 1.0;
                }
                float miss = length(p + t * d); // in planet radii
                return smoothstep(0.97, 1.03, miss);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float radius = length(input.positionOS.xz);
                if (radius < _InnerFraction || radius > 1.0)
                {
                    discard;
                }
                float u = (radius - _InnerFraction) / (1.0 - _InnerFraction);
                float tau = SAMPLE_TEXTURE2D_LOD(_Profile, sampler_Profile, float2(u, 0.5), 0).r;
                float3 normal = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float3 toCamera = normalize(GetCameraPositionWS() - input.positionWS);
                float3 light = _LightDirection.xyz;
                float muView = max(abs(dot(normal, toCamera)), 1e-3);
                bool litSide = dot(normal, light) * dot(normal, toCamera) > 0.0;
                float brightness = (litSide ? 1.0 : _UnlitSide) * PlanetShadow(input.positionWS, light);
            #if defined(USIM_STYLE_STYLIZED)
                if (_UsimRings.x > 0.0)
                {
                    float stylizedAlpha = 1.0 - exp(-tau * (1.0 + _UsimRings.y) / muView);
                    half3 color = lerp(_UsimRingSparse.rgb, _UsimRingDense.rgb, (half)saturate(tau / _UsimRings.x));
                    return half4(color * ((half)(1.0 + _UsimRings.z) * brightness + 0.02), stylizedAlpha);
                }
            #endif
                float alpha = 1.0 - exp(-tau / muView);
                return half4(_BaseColor.rgb * (brightness + 0.02), alpha);
            }
            ENDHLSL
        }
    }
}
