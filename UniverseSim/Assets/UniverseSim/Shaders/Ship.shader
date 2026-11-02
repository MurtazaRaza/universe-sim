// The spaceship of the stylized style (ADR-0016), shaded like the bodies' stylized variant (docs/ART_DIRECTION.md
// section 3): a bright day curve from a cool shade to warm light, a satin sheen, a rim that keeps the hull readable,
// and a lift on the night side. Lit by the sunlight direction the core gives the body the ship is near
// (_LightDirection, render space), dimmed by that body's shadow (_Sunlit). Vertex colors carry the paint (rgb) and
// the engine glow (alpha), which blooms. The ship exists only in the stylized style, so there is no realistic
// variant.
Shader "UniverseSim/Ship"
{
    Properties
    {
        _LightDirection ("Unit vector toward the light, render space (set per frame)", Vector) = (0, 1, 0, 0)
        _Sunlit ("Sunlight reaching the ship, 0 in a body's shadow (set per frame)", Range(0, 1)) = 1
        _EngineGlow ("Engine glow, 0 to 1 (set per frame)", Range(0, 1)) = 0.4
        [HDR] _EngineColor ("Engine color", Color) = (1.8, 3.4, 4.0, 1)
        _DayCurve ("Day curve exponent", Range(1, 4)) = 2.2
        _LightTint ("Tint in full light", Color) = (1.10, 1.04, 0.95, 1)
        _ShadeTint ("Tint next to the terminator", Color) = (0.75, 0.80, 1.05, 1)
        _NightTint ("Night lift tint", Color) = (0.40, 0.45, 1.00, 1)
        _NightLift ("Night lift", Range(0, 0.5)) = 0.18
        _Sheen ("Sheen strength, exponent", Vector) = (0.25, 12, 0, 0)
        _Rim ("Rim strength, exponent", Vector) = (0.45, 3, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Ship"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _LightDirection;
                float _Sunlit;
                float _EngineGlow;
                half4 _EngineColor;
                float _DayCurve;
                half4 _LightTint;
                half4 _ShadeTint;
                half4 _NightTint;
                float _NightLift;
                float4 _Sheen;
                float4 _Rim;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Vertex colors are sRGB paint; the pipeline works in linear.
                half3 albedo = SRGBToLinear(input.color.rgb);
                float3 n = normalize(input.normalWS);
                float3 l = normalize(_LightDirection.xyz);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                float ndl = dot(n, l);

                float ramp = (1.0 - pow(1.0 - saturate(ndl), _DayCurve)) * _Sunlit;
                half3 tint = lerp(_ShadeTint.rgb, _LightTint.rgb, ramp);
                half3 color = albedo * (tint * ramp + _NightTint.rgb * _NightLift);

                float3 h = normalize(l + v);
                half3 bright = saturate(albedo * 1.8 + 0.08);
                color += bright * (_Sheen.x * pow(saturate(dot(n, h)), max(_Sheen.y, 1.0)) * smoothstep(0.0, 0.15, ndl) * _Sunlit);
                color += lerp(1.0, bright, 0.6) * (_Rim.x * pow(1.0 - saturate(dot(n, v)), max(_Rim.y, 0.5)) * lerp(0.4, 1.0, _Sunlit));

                color += _EngineColor.rgb * (input.color.a * _EngineGlow);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
