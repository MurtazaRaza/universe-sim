// Art direction mockups only (A-01, OQ-09): BodyLit with the candidate stylizations of SPEC section 11.1 as
// parameters, so the candidate looks can be rendered from the real scene. Not used by the viewer; A-05 and
// A-06 build the chosen look into BodyLit, and this folder goes away then.
//
// What stays as in BodyLit: the light direction per body from the core, the surface map, the ring shadow.
// The terminator stays where n.l = 0 puts it: the day side starts at n.l = 0 (antialiased over one pixel),
// the bands only divide the day side, and the rim light fades out before the terminator.
//
// Stylizations (each off at its neutral value):
// - _PaintRadius, _PaintLevel: a Kuwahara filter over the map (the mean of the calmest of four neighborhoods),
//   which flattens it into painted patches along its real features; _PaintLevel is the mip it reads.
// - _Saturation, _Contrast (about _ContrastPivot), _Posterize (luminance levels): the map's color. Contrast and
//   posterization change only the luminance, so each texel keeps its hue.
// - _InkStrength, _InkColor: lines where the posterized luminance changes level, so they follow map features.
// - _Bands: the day side in that many bands of brightness from _BandFloor to 1 (0 = smooth Lambert);
//   _BandSoftness widens the steps between bands.
// - _DayTint, _ShadeTint: the tint of the brightest and the darkest day band (a shading ramp).
// - _NightLift, _NightTint: the night side shows the map dimmed to this brightness and tint (0 = black).
// - _RimStrength, _RimPower, _RimColor: light along the sunlit limb; _RimCut > 0 makes it a hard line where
//   the falloff passes the cut.
// - _OutlineWidth (pixels), _OutlineColor (alpha = strength), _OutlineFromMap (0 = the color, 1 = a dark shade
//   of the map): a line inside the silhouette, so the body keeps its size.
// - _LitCurve > 1: a smooth day side that brightens faster than Lambert, 1 - (1 - n.l)^_LitCurve; still 0 at
//   the terminator.
// - _TerminatorWidth, _TerminatorColor: a tint of the day side over that range of n.l next to the terminator.
// - _SheenStrength, _SheenPower: a broad highlight of the light on the day side, in a bright shade of the
//   surface's color (a satin, toy-like finish).
// - _GlintStrength, _GlintPower: a sharp highlight where the map looks like open water (dark and blue), the
//   sun glint; set only on bodies with oceans.
// - _RimFromMap: the day rim takes a bright shade of the map's color instead of _RimColor.
// - _NightRimStrength, _NightRimPower, _NightRimColor: a cool rim along the night limb for readability,
//   fading out before the terminator.
// - _BodyMax: the brightest a surface may get (its color scaled down as a whole, so the hue holds). Below the
//   bloom threshold, it keeps bloom to halos, stars and the Sun, so an airless body never gets a haze.
Shader "UniverseSim/Mockups/Body"
{
    Properties
    {
        _BaseColor ("Base color (neutral, not a measured albedo)", Color) = (0.75, 0.75, 0.75, 1)
        _BaseMap ("Surface map (set per body)", 2D) = "white" {}
        _Ambient ("Ambient", Range(0, 0.2)) = 0.015
        _LightDirection ("Light direction (set per body)", Vector) = (0, 1, 0, 0)
        _RingParams ("Ring parameters (set per body)", Vector) = (0, 0, 0, 0)
        _RingProfile ("Ring optical depth profile (set per body)", 2D) = "black" {}
        _PaintRadius ("Painted patches (0 = off)", Range(0, 3)) = 0
        _PaintLevel ("Painted patch mip level", Range(0, 5)) = 1
        _Saturation ("Map saturation", Range(0, 3)) = 1
        _Contrast ("Map contrast", Range(0, 3)) = 1
        _ContrastPivot ("Map contrast pivot", Range(0, 1)) = 0.4
        _Posterize ("Map luminance levels (0 = off)", Float) = 0
        _InkStrength ("Feature ink strength", Range(0, 1)) = 0
        _InkColor ("Feature ink color", Color) = (0.02, 0.03, 0.08, 1)
        _Bands ("Bands on the day side (0 = smooth)", Float) = 0
        _BandFloor ("Brightness of the darkest day band", Range(0, 1)) = 0.35
        _BandSoftness ("Band step softness", Range(0, 1)) = 0
        _DayTint ("Day side tint (brightest band)", Color) = (1, 1, 1, 1)
        _ShadeTint ("Day side tint (darkest band)", Color) = (1, 1, 1, 1)
        _NightLift ("Night side brightness", Range(0, 0.5)) = 0
        _NightTint ("Night side tint", Color) = (1, 1, 1, 1)
        _RimStrength ("Rim light strength", Range(0, 2)) = 0
        _RimPower ("Rim light falloff", Range(0.5, 8)) = 3
        _RimCut ("Rim line cut (0 = soft rim)", Range(0, 1)) = 0
        _RimColor ("Rim light color", Color) = (1, 1, 1, 1)
        _OutlineWidth ("Outline width, pixels", Range(0, 6)) = 0
        _OutlineColor ("Outline color (alpha = strength)", Color) = (0, 0, 0, 1)
        _OutlineFromMap ("Outline color from the map", Range(0, 1)) = 0
        _LitCurve ("Day side curve (1 = Lambert)", Range(1, 4)) = 1
        _TerminatorWidth ("Terminator band width, n.l", Range(0, 1)) = 0
        _TerminatorColor ("Terminator band tint", Color) = (1, 1, 1, 1)
        _SheenStrength ("Sheen strength", Range(0, 1)) = 0
        _SheenPower ("Sheen falloff", Range(1, 64)) = 8
        _GlintStrength ("Water glint strength", Range(0, 4)) = 0
        _GlintPower ("Water glint falloff", Range(8, 512)) = 120
        _RimFromMap ("Rim color from the map", Range(0, 1)) = 0
        _NightRimStrength ("Night rim strength", Range(0, 1)) = 0
        _NightRimPower ("Night rim falloff", Range(0.5, 8)) = 3
        _NightRimColor ("Night rim color", Color) = (0.4, 0.6, 1, 1)
        _BodyMax ("Brightest surface value", Float) = 100
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _BaseMap_TexelSize;
                half _Ambient;
                float4 _LightDirection;
                float4 _RingParams;
                float _PaintRadius;
                float _PaintLevel;
                float _Saturation;
                float _Contrast;
                float _ContrastPivot;
                float _Posterize;
                float _InkStrength;
                half4 _InkColor;
                float _Bands;
                float _BandFloor;
                float _BandSoftness;
                half4 _DayTint;
                half4 _ShadeTint;
                float _NightLift;
                half4 _NightTint;
                float _RimStrength;
                float _RimPower;
                float _RimCut;
                half4 _RimColor;
                float _OutlineWidth;
                half4 _OutlineColor;
                float _OutlineFromMap;
                float _LitCurve;
                float _TerminatorWidth;
                half4 _TerminatorColor;
                float _SheenStrength;
                float _SheenPower;
                float _GlintStrength;
                float _GlintPower;
                float _RimFromMap;
                float _NightRimStrength;
                float _NightRimPower;
                half4 _NightRimColor;
                float _BodyMax;
            CBUFFER_END

            TEXTURE2D(_RingProfile);
            SAMPLER(sampler_RingProfile);
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

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

            // As in BodyLit.
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

            // The map, or with _PaintRadius > 0 a Kuwahara filter of it: of four 4 x 4 neighborhoods that meet at
            // the texel (spaced _PaintRadius texels of mip _PaintLevel), the mean of the one that varies least.
            half3 SampleMap(float2 uv)
            {
                if (_PaintRadius <= 0.0)
                {
                    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                }
                float2 step = _BaseMap_TexelSize.xy * exp2(_PaintLevel) * _PaintRadius / 3.0;
                half3 best = 0;
                half bestVariance = 1e6;
                [unroll] for (int q = 0; q < 4; ++q)
                {
                    float2 direction = float2((q & 1) ? 1.0 : -1.0, (q & 2) ? 1.0 : -1.0);
                    half3 sum = 0;
                    half3 squares = 0;
                    [unroll] for (int j = 0; j < 4; ++j)
                    {
                        [unroll] for (int i = 0; i < 4; ++i)
                        {
                            float2 at = uv + direction * float2(i, j) * step;
                            half3 c = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, at, _PaintLevel).rgb;
                            sum += c;
                            squares += c * c;
                        }
                    }
                    half3 mean = sum / 16.0;
                    half3 variance = squares / 16.0 - mean * mean;
                    half v = variance.r + variance.g + variance.b;
                    if (v < bestVariance)
                    {
                        bestVariance = v;
                        best = mean;
                    }
                }
                return best;
            }

            // The map's color treatment; `level` returns the posterized luminance level for the ink lines.
            half3 Graded(half3 albedo, out float level)
            {
                const half3 weights = half3(0.2126, 0.7152, 0.0722);
                half luminance = dot(albedo, weights);
                albedo = max(lerp(luminance.xxx, albedo, _Saturation), 0.0);
                half before = max(dot(albedo, weights), 1e-4);
                half after = _ContrastPivot * pow(before / _ContrastPivot, _Contrast);
                level = round(sqrt(after) * max(_Posterize, 1.0));
                if (_Posterize > 0.5)
                {
                    half quantized = level / _Posterize;
                    after = clamp(quantized * quantized, 0.6 * after, 1.6 * after);
                }
                return albedo * (after / before);
            }

            // Brightness of the day side for n.l in (0, 1], and where it lies on the shading ramp (0 for the
            // darkest band, 1 for the brightest): _Bands equal steps of n.l from _BandFloor up to 1.
            float Banded(float ndl, out float ramp)
            {
                if (_Bands < 0.5)
                {
                    float smooth = 1.0 - pow(1.0 - ndl, _LitCurve);
                    ramp = smooth;
                    return smooth;
                }
                float bands = max(_Bands, 1.0);
                float steps = 0.0;
                float halfWidth = max(_BandSoftness * 0.5 / bands, 1e-4);
                for (int k = 1; k < 8; ++k)
                {
                    if (k < bands)
                    {
                        steps += smoothstep(k / bands - halfWidth, k / bands + halfWidth, ndl);
                    }
                }
                ramp = bands > 1.5 ? steps / (bands - 1.0) : 1.0;
                return lerp(_BandFloor, 1.0, ramp);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 light = _LightDirection.xyz;
                float3 toCamera = normalize(GetCameraPositionWS() - input.positionWS);
                float ndl = dot(n, light);
                float day = saturate(ndl / max(fwidth(ndl), 1e-6)); // the terminator, within one pixel
                float ramp;
                float lit = Banded(saturate(ndl), ramp);
                if (ndl > 0.0)
                {
                    lit *= RingTransmission(input.positionWS, light);
                }
                float level;
                half3 texel = _BaseColor.rgb * SampleMap(input.uv);
                half3 albedo = Graded(texel, level);
                float ink = saturate(fwidth(level)) * _InkStrength;
                albedo = lerp(albedo, _InkColor.rgb, ink);

                half3 tint = lerp(_ShadeTint.rgb, _DayTint.rgb, ramp);
                if (_TerminatorWidth > 0.0)
                {
                    tint *= lerp(_TerminatorColor.rgb, 1.0, smoothstep(0.0, _TerminatorWidth, ndl));
                }
                half3 dayLight = tint * max(lit, _NightLift);
                half3 nightLight = _NightTint.rgb * _NightLift;
                half3 color = albedo * (lerp(nightLight, dayLight, day) + _Ambient);

                // Highlights of the light: a broad sheen everywhere on the day side, a sharp glint on open water.
                float3 halfway = normalize(light + toCamera);
                float ndh = saturate(dot(n, halfway));
                float dayMask = day * smoothstep(0.0, 0.15, ndl);
                half texelLuminance = dot(texel, half3(0.2126, 0.7152, 0.0722));
                half water = saturate((texel.b - 0.9 * max(texel.r, texel.g)) * 8.0) *
                             saturate(1.0 - 4.0 * texelLuminance);
                // The sheen takes a bright shade of the surface's own color (a white sheen would wash dark
                // surfaces grey); the glint on water is white.
                half3 sheenColor = saturate(albedo * 1.8 + 0.08);
                color += dayMask * (sheenColor * (_SheenStrength * pow(ndh, _SheenPower)) +
                                    _DayTint.rgb * (_GlintStrength * water * pow(ndh, _GlintPower)));

                float ndv = saturate(dot(n, toCamera));
                float rim = pow(1.0 - ndv, _RimPower);
                if (_RimCut > 0.0)
                {
                    float aa = max(fwidth(rim), 1e-4);
                    rim = smoothstep(_RimCut - aa, _RimCut + aa, rim);
                }
                half3 rimColor = lerp(_RimColor.rgb, saturate(albedo * 1.6 + 0.25), _RimFromMap);
                color += rimColor * (rim * _RimStrength * smoothstep(0.0, 0.25, ndl));
                float nightRim = pow(1.0 - ndv, _NightRimPower) * smoothstep(0.0, 0.3, -ndl);
                color += _NightRimColor.rgb * (nightRim * _NightRimStrength);

                if (_OutlineWidth > 0.0)
                {
                    // On a sphere, n.v / fwidth(n.v) is about twice the distance to the silhouette in pixels.
                    float pixels = 0.5 * ndv / max(fwidth(ndv), 1e-6);
                    float edge = 1.0 - smoothstep(_OutlineWidth - 0.5, _OutlineWidth + 0.5, pixels);
                    half3 outline = lerp(_OutlineColor.rgb, albedo * 0.3, _OutlineFromMap);
                    color = lerp(color, outline, edge * _OutlineColor.a);
                }
                half peak = max(color.r, max(color.g, color.b));
                return half4(color * min(1.0, _BodyMax / max(peak, 1e-4)), 1);
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
