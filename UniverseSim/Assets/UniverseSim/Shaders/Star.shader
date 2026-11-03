// The star: emissive and unlit (P5-07). Its color is a neutral white, not a measured spectrum.
//
// In the stylized style (A-08, keyword USIM_STYLE_STYLIZED) the disk is a warm color above the bloom threshold, so
// it glows (docs/ART_DIRECTION.md section 7). The disk keeps its size: only its color changes. The parameter is a
// global set by StylizedSun.cs, neutral at zero:
// - _UsimSunDisk: rgb the disk's color (linear, HDR); a 1 when set, 0 for the material's color.
Shader "UniverseSim/Star"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _LightDirection ("Unused (set per body)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

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
                half4 _Color;
                float4 _LightDirection;
            CBUFFER_END

            // The stylized disk (see the top of this file); global, neutral at zero.
            half4 _UsimSunDisk;

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 frag() : SV_Target
            {
            #if defined(USIM_STYLE_STYLIZED)
                if (_UsimSunDisk.a > 0.5)
                {
                    return half4(_UsimSunDisk.rgb, 1);
                }
            #endif
                return _Color;
            }
            ENDHLSL
        }
    }
}
