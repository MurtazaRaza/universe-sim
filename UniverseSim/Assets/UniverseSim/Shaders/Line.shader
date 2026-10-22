// Orbit lines with a constant width in pixels at any distance (P5-10). Each point of the path is two
// vertices; the vertex shader moves them apart across the screen-space direction to the neighbouring point
// (TEXCOORD0.xyz), by _HalfWidthPixels on the side given by TEXCOORD0.w. A width in world units would make
// a line passing near the camera cover the whole screen.
Shader "UniverseSim/Line"
{
    Properties
    {
        _Color ("Color", Color) = (0.45, 0.6, 0.85, 0.55)
        _HalfWidthPixels ("Half width (pixels)", Float) = 0.75
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _HalfWidthPixels;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 neighbour : TEXCOORD0; // xyz: the neighbouring point; w: side (+1 or -1)
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float4 here = TransformObjectToHClip(input.positionOS.xyz);
                float4 there = TransformObjectToHClip(input.neighbour.xyz);
                if (here.w > 1e-6 && there.w > 1e-6)
                {
                    float2 screen = _ScreenParams.xy;
                    float2 direction = (there.xy / there.w - here.xy / here.w) * screen;
                    float len = length(direction);
                    if (len > 1e-6)
                    {
                        direction /= len;
                        float2 normal = float2(-direction.y, direction.x);
                        // One pixel is 2 / screen size in normalized device coordinates.
                        here.xy += normal * input.neighbour.w * _HalfWidthPixels * 2.0 / screen * here.w;
                    }
                }
                output.positionHCS = here;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
