// Atmosphere halo of the stylized style (A-07, ADR-0018, docs/ART_DIRECTION.md section 5), drawn by
// StylizedAtmospheres on a shell around each body the core reports with an atmosphere, added to what is behind it.
// The glow is computed from the view ray, not from the shell's surface: in the body's local space (the body is the
// unit sphere there, _BodyWorldToLocal), the ray's closest approach b to the center gives a glow that peaks at the
// limb (b = 1), fades out at the top of the shell (b = 1 + _Thickness) and reaches a little way onto the disc as
// b^_DiscReach. It is brightest on the sunlit side and fades out past the terminator, following the core's light
// direction (_LightDirection, render space). The color goes from _CoreColor at the limb to _HaloColor at the top of
// the shell, and toward _TwilightColor where the limb meets the terminator (_TwilightStrength, 0 for a body whose
// twilight hue no source gives). The body keeps its size: the shell is an effect, not a larger body (SPEC 11.1).
// Seen from inside the shell, the back faces are drawn instead (_Cull) and _Fade hands over to the sky (StylizedSky).
Shader "UniverseSim/AtmosphereHalo"
{
    Properties
    {
        _HaloColor ("Halo color (outer)", Color) = (0.2, 0.45, 1.0, 1)
        _CoreColor ("Halo color at the limb", Color) = (0.55, 0.8, 1.0, 1)
        _TwilightColor ("Twilight color", Color) = (1.0, 0.5, 0.25, 1)
        _TwilightStrength ("Twilight strength", Range(0, 1)) = 0
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Thickness ("Shell thickness, body radii", Range(0, 0.5)) = 0.12
        _Falloff ("Falloff above the limb", Range(0.5, 6)) = 2.2
        _DiscReach ("Reach onto the disc (0 = none)", Range(0, 64)) = 16
        _Fade ("Fade (inside the shell)", Range(0, 1)) = 1
        _LightDirection ("Light direction (set per body)", Vector) = (0, 1, 0, 0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }
        Blend One One
        ZWrite Off
        Cull [_Cull]

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _HaloColor;
                half4 _CoreColor;
                half4 _TwilightColor;
                half _TwilightStrength;
                half _Intensity;
                float _Thickness;
                float _Falloff;
                float _DiscReach;
                half _Fade;
                float4 _LightDirection;
                float _Cull;
                float4x4 _BodyWorldToLocal;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 cameraOS = mul(_BodyWorldToLocal, float4(GetCameraPositionWS(), 1)).xyz;
                float3 pointOS = mul(_BodyWorldToLocal, float4(input.positionWS, 1)).xyz;
                float3 ray = normalize(pointOS - cameraOS);
                // The closest approach along the ray ahead of the camera (from inside the shell, looking away from
                // the body, that is the camera itself).
                float3 closest = cameraOS - min(dot(cameraOS, ray), 0.0) * ray;
                float b = length(closest);
                float thickness = max(_Thickness, 1e-4);
                float glow = b >= 1.0
                    ? pow(saturate(1.0 - (b - 1.0) / thickness), _Falloff)
                    : (_DiscReach > 0.0 ? pow(b, _DiscReach) : 0.0);
                float3 lightOS = normalize(mul((float3x3)_BodyWorldToLocal, _LightDirection.xyz));
                float3 side = closest / max(b, 1e-4);
                float facing = dot(side, lightOS);
                float sunlit = smoothstep(-0.25, 0.45, facing);
                half3 color = lerp(_CoreColor.rgb, _HaloColor.rgb, saturate((b - 1.0) / thickness));
                color = lerp(color, _TwilightColor.rgb, _TwilightStrength * (1.0 - smoothstep(0.0, 0.45, abs(facing))));
                return half4(color * (_Intensity * _Fade * glow * sunlit), 1);
            }
            ENDHLSL
        }
    }
}
