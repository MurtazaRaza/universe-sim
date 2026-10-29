// Art direction mockups only (A-01, OQ-09): an atmosphere halo (a candidate for A-07). A shell around the
// body, added to what is behind it. The glow is computed from the view ray rather than from the shell's
// surface: in the body's local space (the body is the unit sphere there, _BodyWorldToLocal), the ray's
// closest approach b to the center gives a glow that peaks at the limb (b = 1), fades out at the top of the
// shell (b = 1 + _Thickness) and reaches a little way onto the disc (_DiscReach; 0 keeps the disc clear, so
// an outline stays visible). The glow is brightest on the sunlit side and fades out past the terminator,
// following the core's light direction (_LightDirection, render space).
// _Steps > 0 quantizes the glow into that many hard-edged rings (a toon halo). The color goes from _CoreColor at
// the limb to _HaloColor at the top of the shell, and toward _TwilightColor where the limb meets the
// terminator (_TwilightStrength; the twilight hue is the body's own, set per body).
// The body itself keeps its size: the shell is an effect, not a larger body (SPEC section 11.1).
Shader "UniverseSim/Mockups/Halo"
{
    Properties
    {
        _HaloColor ("Halo color (outer)", Color) = (0.45, 0.7, 1.0, 1)
        _CoreColor ("Halo color at the limb", Color) = (0.45, 0.7, 1.0, 1)
        _TwilightColor ("Twilight color", Color) = (1.0, 0.55, 0.3, 1)
        _TwilightStrength ("Twilight strength", Range(0, 1)) = 0
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Thickness ("Shell thickness, body radii", Range(0, 0.5)) = 0.08
        _Falloff ("Falloff above the limb", Range(0.5, 6)) = 2
        _DiscReach ("Reach onto the disc (0 = none)", Range(0, 64)) = 12
        _Steps ("Glow steps (0 = smooth)", Float) = 0
        _LightDirection ("Light direction (set per body)", Vector) = (0, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }
        Blend One One
        ZWrite Off
        Cull Back

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
                float _Steps;
                float4 _LightDirection;
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
                float3 closest = cameraOS - dot(cameraOS, ray) * ray;
                float b = length(closest);
                float glow = b >= 1.0
                    ? pow(saturate(1.0 - (b - 1.0) / max(_Thickness, 1e-4)), _Falloff)
                    : (_DiscReach > 0.0 ? pow(b, _DiscReach) : 0.0);
                float3 lightOS = normalize(mul((float3x3)_BodyWorldToLocal, _LightDirection.xyz));
                float3 side = closest / max(b, 1e-4);
                float facing = dot(side, lightOS);
                float sunlit = smoothstep(-0.25, 0.45, facing);
                if (_Steps > 0.5)
                {
                    // Steps of the radial profile only: each ring keeps its thickness around the limb and fades
                    // out smoothly past the terminator, so the halo hugs the body instead of forming a crescent.
                    // Rounds glow * _Steps, with the steps antialiased over a pixel.
                    float y = glow * _Steps;
                    float aa = max(fwidth(y), 1e-4);
                    glow = (floor(y) + smoothstep(0.5 - aa, 0.5 + aa, frac(y))) / _Steps;
                }
                half3 color = lerp(_CoreColor.rgb, _HaloColor.rgb, saturate((b - 1.0) / max(_Thickness, 1e-4)));
                color = lerp(color, _TwilightColor.rgb, _TwilightStrength * (1.0 - smoothstep(0.0, 0.45, abs(facing))));
                return half4(color * (_Intensity * glow * sunlit), 1);
            }
            ENDHLSL
        }
    }
}
