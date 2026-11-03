// The spaceship's speed streaks in the stylized style (ADR-0019 decision 2), drawn by SpeedCues.cs only while that
// effect is on and the ship goes fast. The mesh is a set of thin quads around the camera's view axis, in the camera's
// own space (the renderer is a child of the camera); the vertex shader moves each streak toward the camera, wraps it
// back to the far end, and stretches it with the intensity. The fragment adds light, so the order does not matter,
// and the streaks are depth-tested, so a body in front of them hides them. Nothing here is a real object.
Shader "UniverseSim/SpeedStreaks"
{
    Properties
    {
        _Color ("Color", Color) = (0.75, 0.85, 1, 1)
        _Intensity ("Intensity, 0 to 1 (set per frame)", Float) = 0
        _Phase ("Motion phase (set per frame)", Float) = 0
        _Shape ("Shape: nearest depth, depth range, longest length, half width (render units)", Vector) = (0.12, 4.5, 1.6, 0.0035)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Phase;
                float4 _Shape;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 streak : TEXCOORD0; // angle (rad), distance from the axis, along (0 head, 1 tail), side (-1, 1)
                float4 motion : TEXCOORD1; // starting depth (0 to 1), speed factor, brightness
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 shape : TEXCOORD0; // x along, y side
                float fade : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float nearest = _Shape.x;
                float range = _Shape.y;
                // Each streak comes toward the camera and starts again at the far end.
                float depth = nearest + frac(input.motion.x - _Phase * input.motion.y) * range;
                float streakLength = _Shape.z * (0.25 + 0.75 * _Intensity) * input.motion.y;
                float z = depth + input.streak.z * streakLength;
                float2 radial = float2(cos(input.streak.x), sin(input.streak.x));
                float2 across = float2(-radial.y, radial.x);
                float2 xy = radial * input.streak.y + across * (input.streak.w * _Shape.w);
                output.positionHCS = TransformObjectToHClip(float3(xy, z));
                output.shape = float2(input.streak.z, input.streak.w);
                // In from the far end, out before the camera; brighter for the faster and brighter streaks.
                float farFade = saturate((nearest + range - depth) / (0.3 * range));
                float nearFade = saturate((depth - nearest) / 0.25);
                output.fade = _Intensity * input.motion.z * farFade * nearFade;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Bright at the head, fading along the tail, soft across.
                float along = 1.0 - input.shape.x;
                float across = 1.0 - input.shape.y * input.shape.y;
                float a = input.fade * along * along * across;
                return half4(_Color.rgb * a, 0);
            }
            ENDHLSL
        }
    }
}
