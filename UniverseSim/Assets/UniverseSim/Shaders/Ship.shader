// The spaceship of the stylized style (ADR-0016, docs/ART_DIRECTION.md section 11). It is shaded by the bodies' own
// rules of section 3, from the same global properties that StylizedBodyShading sets for BodyLit (see there): the day
// curve from the cool shade to the warm light, the antialiased terminator, the lifted and tinted night side, the satin
// sheen, the day rim on the sunlit side and the night rim on the dark side, and the brightness cap, so no part of the
// hull blooms. Only the rims' fade near the ground is left out: they outline the ship's own silhouette, which is
// always near. The ship exists only in the stylized style, so there is no realistic variant.
//
// The ship's own inputs: lit by the sunlight direction the core gives the body it is near (_LightDirection, render
// space), dimmed by that body's shadow (_Sunlit). Under a sky (StylizedSky) the sky's light fills it from above and
// catches its edges (_SkyLight, linear, zero elsewhere; _SkyUp). Vertex colors carry the paint (rgb, sRGB) and the
// engine glow (alpha), which blooms; the first texture coordinate carries the baked occlusion (x, 1 = open) and the
// finish (y, 1 = the canopy's glass, with a tight highlight and stronger rims).
Shader "UniverseSim/Ship"
{
    Properties
    {
        _LightDirection ("Unit vector toward the light, render space (set per frame)", Vector) = (0, 1, 0, 0)
        _Sunlit ("Sunlight reaching the ship, 0 in a body's shadow (set per frame)", Range(0, 1)) = 1
        _SkyLight ("Light of the sky around the ship, linear rgb (set per frame)", Vector) = (0, 0, 0, 0)
        _SkyUp ("Up at the ship under a sky, render space (set per frame)", Vector) = (0, 1, 0, 0)
        _EngineGlow ("Engine glow, 0 to 1 (set per frame)", Range(0, 1)) = 0.4
        [HDR] _EngineColor ("Engine color", Color) = (1.8, 3.4, 4.0, 1)
        _NightLift ("Night side, at least the bodies' (a small craft needs a little more)", Range(0, 0.5)) = 0.15
        _Occlusion ("Strength of the baked occlusion", Range(0, 1)) = 0.7
        _Glass ("Glass: highlight strength, exponent; rim gain", Vector) = (0.8, 90, 1.6, 0)
        _SkyEdge ("Sky light on the edges: strength, exponent", Vector) = (0.5, 3, 0, 0)
    }
    SubShader
    {
        // After the bodies and the contact shade (ShipShadow.shader, Geometry+1), which it covers.
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+2" }

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
                float4 _SkyLight;
                float4 _SkyUp;
                float _EngineGlow;
                half4 _EngineColor;
                float _NightLift;
                float _Occlusion;
                float4 _Glass;
                float4 _SkyEdge;
            CBUFFER_END

            // The bodies' stylized look (BodyLit, set by StylizedBodyShading).
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
                half4 color : COLOR;
                float2 surface : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 surface : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.surface = input.surface;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Vertex colors are sRGB paint; the pipeline works in linear.
                half3 albedo = SRGBToLinear(input.color.rgb);
                float glass = input.surface.y;
                float3 n = normalize(input.normalWS);
                float3 l = normalize(_LightDirection.xyz);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                float ndl = dot(n, l);

                // Day side, terminator and night side as on the bodies; in a body's shadow all of it is night side.
                float day = saturate(ndl / max(fwidth(ndl), 1e-6)) * _Sunlit;
                float ramp = 1.0 - pow(1.0 - saturate(ndl), 1.0 + _UsimDayCurve);
                float night = max(_UsimNight.x, _NightLift);
                half3 tint = 1.0 + lerp(_UsimShadeTint.rgb, _UsimDayTint.rgb, ramp);
                half3 dayLight = tint * max(ramp, night);
                half3 nightLight = _UsimNightTint.rgb * night;
                // The sky's light, strongest on faces turned up toward it.
                float up = 0.5 + 0.5 * dot(n, normalize(_SkyUp.xyz));
                half3 skyLight = _SkyLight.rgb * lerp(0.3, 1.0, up);
                half3 color = albedo * (lerp(nightLight, dayLight, day) + skyLight);

                // The satin sheen of the paint; glass has a tight white highlight instead.
                float ndh = saturate(dot(n, normalize(l + v)));
                float dayMask = day * smoothstep(0.0, 0.15, ndl);
                half3 satin = saturate(albedo * 1.8 + 0.08) * (_UsimSheen.x * pow(ndh, max(_UsimSheen.y, 1.0)));
                half3 glint = _Glass.x * pow(ndh, max(_Glass.y, 1.0));
                color += dayMask * lerp(satin, glint, glass);

                // Rims as on the bodies: along the sunlit edge after the terminator, along the dark edge before it
                // (and all around in a body's shadow); the glass a little stronger. Under a sky the edges catch it.
                float edge = 1.0 - saturate(dot(n, v));
                float gain = lerp(1.0, _Glass.z, glass);
                half3 rimColor = lerp(1.0, saturate(albedo * 1.6 + 0.25), _UsimRim.z);
                color += rimColor * (pow(edge, max(_UsimRim.y, 0.5)) * _UsimRim.x * smoothstep(0.0, 0.25, ndl) * _Sunlit * gain);
                float dark = max(smoothstep(0.0, 0.3, -ndl), 1.0 - _Sunlit);
                color += _UsimNightRimColor.rgb * (pow(edge, max(_UsimNightRim.y, 0.5)) * _UsimNightRim.x * dark * gain);
                color += _SkyLight.rgb * (pow(edge, max(_SkyEdge.y, 0.5)) * _SkyEdge.x * up);

                // Softly darker where the parts meet.
                color *= lerp(1.0, input.surface.x, _Occlusion);

                // The bodies' cap: the color scaled down as a whole, so its hue holds and the hull never blooms.
                half peak = max(color.r, max(color.g, color.b));
                if (_UsimBodyCap > 0.0)
                {
                    color *= min(1.0, _UsimBodyCap / max(peak, 1e-4));
                }

                // Only the engines glow past it.
                color += _EngineColor.rgb * (input.color.a * _EngineGlow);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
