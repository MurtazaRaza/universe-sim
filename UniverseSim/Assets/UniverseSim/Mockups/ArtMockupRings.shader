// Art direction mockups only (A-01, OQ-09): the Rings shader with a candidate color and contrast treatment
// (A-09). Everything that comes from the data is as in Rings.shader: the radii, the optical depth profile,
// the slant opacity, the lit and unlit sides and the planet's shadow. The treatment only maps the optical
// depth to color and opacity, and every mapping is monotonic, so a denser region never looks thinner:
// - _DensityGain scales the optical depth before the slant opacity (1 = as in Rings.shader);
// - the color goes from _ThinColor to _DenseColor as the optical depth grows (_DenseAt is where it arrives);
// - _AlphaLevels quantizes the opacity into that many steps (0 = off);
// - _InkStrength draws a line where the optical depth changes, at the edges of the ring regions and gaps.
Shader "UniverseSim/Mockups/Rings"
{
    Properties
    {
        _ThinColor ("Color of sparse regions", Color) = (0.75, 0.72, 0.66, 1)
        _DenseColor ("Color of dense regions", Color) = (0.75, 0.72, 0.66, 1)
        _DenseAt ("Optical depth of the dense color", Float) = 2
        _DensityGain ("Optical depth gain", Range(0.5, 4)) = 1
        _AlphaLevels ("Opacity levels (0 = off)", Float) = 0
        _Brightness ("Brightness", Range(0, 3)) = 1
        _InkStrength ("Region edge ink strength", Range(0, 1)) = 0
        _InkColor ("Region edge ink color", Color) = (0.05, 0.04, 0.03, 1)
        _InnerFraction ("Inner edge / outer edge (set per ring system)", Float) = 0.5
        _LightDirection ("Light direction (set per ring system)", Vector) = (0, 1, 0, 0)
        _UnlitSide ("Brightness of the side away from the light (display)", Range(0, 1)) = 0.3
        _Profile ("Optical depth profile (set per ring system)", 2D) = "black" {}
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
                half4 _ThinColor;
                half4 _DenseColor;
                float _DenseAt;
                float _DensityGain;
                float _AlphaLevels;
                half _Brightness;
                float _InkStrength;
                half4 _InkColor;
                float _InnerFraction;
                float4 _LightDirection;
                half _UnlitSide;
                float4x4 _PlanetWorldToLocal;
            CBUFFER_END

            TEXTURE2D(_Profile);
            SAMPLER(sampler_Profile);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            // As in Rings.shader.
            float PlanetShadow(float3 positionWS, float3 light)
            {
                float3 p = mul(_PlanetWorldToLocal, float4(positionWS, 1)).xyz;
                float3 d = mul(_PlanetWorldToLocal, float4(light, 0)).xyz;
                float a = dot(d, d);
                float t = -dot(p, d) / a;
                if (t <= 0.0)
                {
                    return 1.0;
                }
                float miss = length(p + t * d);
                return smoothstep(0.97, 1.03, miss);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float radius = length(input.positionOS.xz);
                if (radius < _InnerFraction || radius > 1.0)
                {
                    discard;
                }
                float u = (radius - _InnerFraction) / (1.0 - _InnerFraction);
                float tau = SAMPLE_TEXTURE2D_LOD(_Profile, sampler_Profile, float2(u, 0.5), 0).r;
                float3 normal = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float3 toCamera = normalize(GetCameraPositionWS() - input.positionWS);
                float3 light = _LightDirection.xyz;
                float muView = max(abs(dot(normal, toCamera)), 1e-3);
                float alpha = 1.0 - exp(-tau * _DensityGain / muView);
                if (_AlphaLevels > 0.5 && alpha > 0.0)
                {
                    alpha = ceil(alpha * _AlphaLevels) / _AlphaLevels;
                }
                bool litSide = dot(normal, light) * dot(normal, toCamera) > 0.0;
                float brightness = (litSide ? 1.0 : _UnlitSide) * PlanetShadow(input.positionWS, light);
                half3 color = lerp(_ThinColor.rgb, _DenseColor.rgb, saturate(tau / max(_DenseAt, 1e-3)));
                color *= _Brightness * brightness + 0.02;
                float ink = fwidth(tau) > 1e-3 ? _InkStrength : 0.0;
                return half4(lerp(color, _InkColor.rgb, ink), max(alpha, ink));
            }
            ENDHLSL
        }
    }
}
