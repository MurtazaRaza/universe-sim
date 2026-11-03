// The sky at a surface site in the stylized style (B-06, ADR-0018, docs/ART_DIRECTION.md section 5), drawn by
// StylizedSky as one full-screen triangle after the star field and before the bodies, blended over the stars. By day
// it takes the body's daytime sky hue and hides the stars; near a low Sun it takes the body's twilight hue around the
// Sun; at night it is clear. _SkyUp is the local vertical at the camera and _SkySun the direction toward the Sun
// (render space, the core's light direction). _SkyParams: x strength (the camera's height in the halo and the
// pressure mapping), y twilight strength (0 without a sourced twilight hue), z the largest opacity.
Shader "UniverseSim/Sky"
{
    Properties
    {
        _SkyDayColor ("Daytime sky color", Color) = (0.42, 0.66, 1.0, 1)
        _SkyTwilightColor ("Twilight color", Color) = (1.0, 0.52, 0.25, 1)
        _SkyUp ("Local vertical (render space)", Vector) = (0, 1, 0, 0)
        _SkySun ("Toward the Sun (render space)", Vector) = (0, 1, 0, 0)
        _SkyParams ("Strength, twilight, opacity", Vector) = (0, 0, 0.97, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background+10" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
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
                half4 _SkyDayColor;
                half4 _SkyTwilightColor;
                float4 _SkyUp;
                float4 _SkySun;
                float4 _SkyParams;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionHCS);
                float3 pointWS = ComputeWorldSpacePosition(uv, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 view = normalize(pointWS - GetCameraPositionWS());
                float3 up = normalize(_SkyUp.xyz);
                float3 sun = normalize(_SkySun.xyz);

                float elevation = dot(up, sun);                       // sine of the Sun's elevation
                float day = smoothstep(-0.12, 0.2, elevation);        // night to day
                float low = 1.0 - smoothstep(0.0, 0.35, abs(elevation + 0.03)); // the Sun near the horizon
                float nearSun = pow(saturate(dot(view, sun)), 6.0);
                float horizon = pow(1.0 - saturate(abs(dot(view, up))), 3.0);

                half3 color = _SkyDayColor.rgb * (0.8 + 0.2 * horizon) * lerp(0.55, 1.0, day);
                float twilight = _SkyParams.y * low * (0.3 + 0.7 * nearSun);
                color = lerp(color, _SkyTwilightColor.rgb, twilight);
                float alpha = saturate(day * (0.85 + 0.15 * horizon) + 0.5 * _SkyParams.y * low * nearSun);
                return half4(color, alpha * _SkyParams.x * _SkyParams.z);
            }
            ENDHLSL
        }
    }
}
