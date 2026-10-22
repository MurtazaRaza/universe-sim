// Bodies lit by the star from its true direction (SPEC FR-REN-07, P5-07). The direction comes per body from
// the core's snapshot (_LightDirection, unit vector in render space), not from a scene light, so phases
// stay correct at every scale and for bodies whose distance is compressed (ADR-0009). Lambert diffuse
// with a small ambient term; no textures in v1 (P5-12 deferred), so the albedo is neutral.
//
// A body with rings (P6-04) also gets their shadow: the ray from the surface toward the light crosses the
// ring plane (the body's equatorial plane, its local XZ plane) at some radius, and the light is dimmed by
// the ring's transmission there, exp(-tau / |sin(elevation of the light)|). _RingParams: x the inner edge as
// a fraction of the outer edge, y the outer edge in render units, z 1 when the body has rings. The optical
// depth profile (_RingProfile, red channel, over [inner, outer]) is the one the ring itself is drawn with.
Shader "UniverseSim/BodyLit"
{
    Properties
    {
        _BaseColor ("Base color (neutral, not a measured albedo)", Color) = (0.75, 0.75, 0.75, 1)
        _Ambient ("Ambient", Range(0, 0.2)) = 0.015
        _LightDirection ("Light direction (set per body)", Vector) = (0, 1, 0, 0)
        _RingParams ("Ring parameters (set per body)", Vector) = (0, 0, 0, 0)
        _RingProfile ("Ring optical depth profile (set per body)", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Ambient;
                float4 _LightDirection;
                float4 _RingParams;
            CBUFFER_END

            TEXTURE2D(_RingProfile);
            SAMPLER(sampler_RingProfile);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            // Fraction of the light that passes the rings on its way to positionWS.
            float RingTransmission(float3 positionWS, float3 light)
            {
                if (_RingParams.z < 0.5)
                {
                    return 1.0;
                }
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float3 pole = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float sine = dot(light, pole);
                if (abs(sine) < 1e-4)
                {
                    return 1.0;
                }
                float t = dot(center - positionWS, pole) / sine;
                if (t <= 0.0)
                {
                    return 1.0;
                }
                float radius = length(positionWS + t * light - center) / _RingParams.y;
                if (radius < _RingParams.x || radius > 1.0)
                {
                    return 1.0;
                }
                float u = (radius - _RingParams.x) / (1.0 - _RingParams.x);
                float tau = SAMPLE_TEXTURE2D_LOD(_RingProfile, sampler_RingProfile, float2(u, 0.5), 0).r;
                return exp(-tau / abs(sine));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 light = _LightDirection.xyz;
                float lit = saturate(dot(n, light));
                if (lit > 0.0)
                {
                    lit *= RingTransmission(input.positionWS, light);
                }
                return half4(_BaseColor.rgb * (lit + _Ambient), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Ambient;
                float4 _LightDirection;
                float4 _RingParams;
            CBUFFER_END

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
