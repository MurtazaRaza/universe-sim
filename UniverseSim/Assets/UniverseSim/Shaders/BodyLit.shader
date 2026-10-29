// Bodies lit by the star from its true direction (SPEC FR-REN-07, P5-07). The direction comes per body from
// the core's snapshot (_LightDirection, unit vector in render space), not from a scene light, so phases
// stay correct at every scale and for bodies whose distance is compressed (ADR-0009). Lambert diffuse
// with a small ambient term. The albedo is the body's surface map where it has one (P6-07, ADR-0014: set per
// body with a white base color, on a mesh whose UVs are planetocentric longitude and latitude), else neutral.
//
// A body with rings (P6-04) also gets their shadow: the ray from the surface toward the light crosses the
// ring plane (the body's equatorial plane, its local XZ plane) at some radius, and the light is dimmed by
// the ring's transmission there, exp(-tau / |sin(elevation of the light)|). _RingParams: x the inner edge as
// a fraction of the outer edge, y the outer edge in render units, z 1 when the body has rings. The optical
// depth profile (_RingProfile, red channel, over [inner, outer]) is the one the ring itself is drawn with.
//
// In the stylized style (A-02, keyword USIM_STYLE_STYLIZED) the body is shaded as in docs/ART_DIRECTION.md
// section 3 (A-05) and its surface map treated as in section 4 (A-06, SurfaceMapTreatment.hlsl). The terminator
// stays where n.l = 0 puts it: the day side starts there (antialiased over one pixel), and the rim light fades
// out before it on both sides. The look comes from global properties set by the StylizedBodyShading and
// SurfaceMapTreatment components; each is neutral at zero, so without those components the stylized variant draws
// the realistic look:
// - _UsimDayCurve: the day side is 1 - (1 - n.l)^(1 + this); 0 is Lambert.
// - _UsimDayTint, _UsimShadeTint: added to white at the subsolar point and next to the terminator, along the
//   same curve.
// - _UsimNight: x the night side's brightness (0 = black, as Lambert); _UsimNightTint its tint.
// - _UsimSheen: x strength, y exponent of a Blinn-Phong sheen in a bright shade of the surface's color.
// - _UsimRim: x strength, y exponent of the day rim, z how far its color goes from white to the surface's.
// - _UsimNightRim: x strength, y exponent of the night rim, in _UsimNightRimColor.
// - _UsimBodyCap: the largest channel a surface may reach, its color scaled down as a whole (0 = no cap).
Shader "UniverseSim/BodyLit"
{
    Properties
    {
        _BaseColor ("Base color (neutral, not a measured albedo)", Color) = (0.75, 0.75, 0.75, 1)
        _BaseMap ("Surface map (set per body)", 2D) = "white" {}
        _Ambient ("Ambient", Range(0, 0.2)) = 0.015
        _LightDirection ("Light direction (set per body)", Vector) = (0, 1, 0, 0)
        _RingParams ("Ring parameters (set per body)", Vector) = (0, 0, 0, 0)
        _RingProfile ("Ring optical depth profile (set per body)", 2D) = "black" {}
        _MapTreatment ("The surface map is a mission map, treated in the stylized style (set per body)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ USIM_STYLE_STYLIZED
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SurfaceMapTreatment.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _BaseMap_TexelSize;
                half _Ambient;
                float4 _LightDirection;
                float4 _RingParams;
                float _MapTreatment;
            CBUFFER_END

            TEXTURE2D(_RingProfile);
            SAMPLER(sampler_RingProfile);
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // The stylized look (see the top of this file); global, neutral at zero.
            float _UsimDayCurve;
            half4 _UsimDayTint;
            half4 _UsimShadeTint;
            float4 _UsimNight;
            half4 _UsimNightTint;
            float4 _UsimSheen;
            float4 _UsimRim;
            float4 _UsimNightRim;
            half4 _UsimNightRimColor;
            float _UsimBodyCap;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // Fraction of the light that passes the rings on its way to positionWS.
            float RingTransmission(float3 positionWS, float3 light)
            {
                if (_RingParams.z < 0.5)
                {
                    return 1.0;
                }
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float3 pole = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float sine = dot(light, pole);
                if (abs(sine) < 1e-4)
                {
                    return 1.0;
                }
                float t = dot(center - positionWS, pole) / sine;
                if (t <= 0.0)
                {
                    return 1.0;
                }
                float radius = length(positionWS + t * light - center) / _RingParams.y;
                if (radius < _RingParams.x || radius > 1.0)
                {
                    return 1.0;
                }
                float u = (radius - _RingParams.x) / (1.0 - _RingParams.x);
                float tau = SAMPLE_TEXTURE2D_LOD(_RingProfile, sampler_RingProfile, float2(u, 0.5), 0).r;
                return exp(-tau / abs(sine));
            }

            // The stylized shading (A-05) and map treatment (A-06).
            half4 Stylized(Varyings input, float3 n, float3 light)
            {
                // A mission map is smoothed and graded; with the smoothing off it is sampled as in the realistic style.
                half3 texel;
                if (_MapTreatment > 0.5 && _UsimMapPaint.x > 0.0)
                {
                    float mip = UsimScreenMip(input.uv, _BaseMap_TexelSize);
                    texel = UsimPaintedMap(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), _BaseMap_TexelSize, input.uv,
                                           mip);
                }
                else
                {
                    texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                }
                if (_MapTreatment > 0.5)
                {
                    texel = UsimGradedMap(texel);
                }
                half3 albedo = _BaseColor.rgb * texel;

                float ndl = dot(n, light);
                // The terminator, within one pixel; only a lifted night side makes it an edge that needs antialiasing.
                float day = _UsimNight.x > 0.0 ? saturate(ndl / max(fwidth(ndl), 1e-6)) : 1.0;
                float ramp = 1.0 - pow(1.0 - saturate(ndl), 1.0 + _UsimDayCurve);
                float lit = ramp;
                if (ndl > 0.0)
                {
                    lit *= RingTransmission(input.positionWS, light);
                }
                half3 tint = 1.0 + lerp(_UsimShadeTint.rgb, _UsimDayTint.rgb, ramp);
                half3 dayLight = tint * max(lit, _UsimNight.x);
                half3 nightLight = _UsimNightTint.rgb * _UsimNight.x;
                half3 color = albedo * (lerp(nightLight, dayLight, day) + _Ambient);

                // A satin sheen on the day side, in a bright shade of the surface's own color.
                float3 toCamera = normalize(GetCameraPositionWS() - input.positionWS);
                float ndh = saturate(dot(n, normalize(light + toCamera)));
                float dayMask = day * smoothstep(0.0, 0.15, ndl);
                color += saturate(albedo * 1.8 + 0.08) * (dayMask * _UsimSheen.x * pow(ndh, max(_UsimSheen.y, 1.0)));

                // Rims: along the sunlit limb, fading in after the terminator; along the night limb, fading out
                // before it.
                float ndv = saturate(dot(n, toCamera));
                half3 rimColor = lerp(1.0, saturate(albedo * 1.6 + 0.25), _UsimRim.z);
                color += rimColor * (pow(1.0 - ndv, max(_UsimRim.y, 0.5)) * _UsimRim.x * smoothstep(0.0, 0.25, ndl));
                float nightRim = pow(1.0 - ndv, max(_UsimNightRim.y, 0.5)) * smoothstep(0.0, 0.3, -ndl);
                color += _UsimNightRimColor.rgb * (nightRim * _UsimNightRim.x);

                // The cap keeps surfaces below the bloom threshold: the color scaled down as a whole, so its hue holds.
                half peak = max(color.r, max(color.g, color.b));
                if (_UsimBodyCap > 0.0)
                {
                    color *= min(1.0, _UsimBodyCap / max(peak, 1e-4));
                }
                return half4(color, 1);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 light = _LightDirection.xyz;
            #if defined(USIM_STYLE_STYLIZED)
                return Stylized(input, n, light);
            #else
                float lit = saturate(dot(n, light));
                if (lit > 0.0)
                {
                    lit *= RingTransmission(input.positionWS, light);
                }
                half3 albedo = _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                return half4(albedo * (lit + _Ambient), 1);
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _BaseMap_TexelSize;
                half _Ambient;
                float4 _LightDirection;
                float4 _RingParams;
                float _MapTreatment;
            CBUFFER_END

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
