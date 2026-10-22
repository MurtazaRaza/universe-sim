// The sky background (SPEC FR-REN-09). Each star is a quad of four vertices at the same position (its
// direction from the core times the sky distance); the vertex shader spreads the corners (TEXCOORD0.xy) by
// a size in pixels (TEXCOORD0.z), so a star covers the same pixels at any zoom. The fragment is a soft disc,
// added to what is behind it. Drawn first (queue Background) without writing depth: every body covers it.
// Brightness and color come from the vertex color, a display mapping of the magnitude and color index made
// by StarField.cs.
Shader "UniverseSim/StarField"
{
    Properties
    {
        _Exposure ("Exposure (display)", Range(0, 4)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" }
        Blend One One
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
                half _Exposure;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 corner : TEXCOORD0; // xy: corner in [-1, 1]; z: half size in pixels
                half4 color : COLOR;       // rgb: display color; a: display intensity
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float4 clip = TransformObjectToHClip(input.positionOS.xyz);
                // One pixel is 2 / screen size in normalized device coordinates.
                clip.xy += input.corner.xy * input.corner.z * 2.0 / _ScreenParams.xy * clip.w;
                output.positionHCS = clip;
                output.corner = input.corner.xy;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float r2 = dot(input.corner, input.corner);
                half disc = (half)exp(-4.0 * r2) * (half)saturate(1.0 - r2);
                return half4(input.color.rgb * (input.color.a * disc * _Exposure), 1);
            }
            ENDHLSL
        }
    }
}
