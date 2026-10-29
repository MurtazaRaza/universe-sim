// Test helper (A-06): draws a surface map into a texture, either as published or with the stylized treatment
// of SurfaceMapTreatment.hlsl, the same code BodyLit runs, so SurfaceMapTreatmentTests can measure what the
// treatment does to each map's average hue. Drawn with Graphics.Blit (the map as _MainTex) over a quad whose
// positions span 0 to 1. _ProbeMip is the mip the screen would need; _MapTexelSize the map's texel size.
Shader "Hidden/UniverseSim/MapTreatmentProbe"
{
    Properties
    {
        _MainTex ("Map", 2D) = "white" {}
        _Treat ("Treated (1) or as published (0)", Float) = 1
        _ProbeMip ("Screen mip", Float) = 0
        _MapTexelSize ("Map texel size", Vector) = (1, 1, 1, 1)
    }
    SubShader
    {
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/UniverseSim/Shaders/SurfaceMapTreatment.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _Treat;
            float _ProbeMip;
            float4 _MapTexelSize;

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

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = float4(input.positionOS.xy * 2.0 - 1.0, 0.5, 1.0);
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // As BodyLit draws a mission map in each style, at the mip a screen of this size needs.
                if (_Treat < 0.5)
                {
                    return float4(SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, input.uv, _ProbeMip).rgb, 1.0);
                }
                float3 color = _UsimMapPaint.x > 0.0
                    ? UsimPaintedMap(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), _MapTexelSize, input.uv, _ProbeMip)
                    : SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, input.uv, _ProbeMip).rgb;
                return float4(UsimGradedMap(color), 1.0);
            }
            ENDHLSL
        }
    }
}
