// Orbit lines with a constant width in pixels at any distance (P5-10). Each point of the path is two
// vertices; the vertex shader moves them apart across the screen-space direction to the neighbouring point
// (TEXCOORD0.xyz), by _HalfWidthPixels on the side given by TEXCOORD0.w. A width in world units would make
// a line passing near the camera cover the whole screen. Every line is in one mesh (OrbitLines.cs), so the
// color comes per vertex (COLOR, already linear).
//
// In the stylized style (A-10, keyword USIM_STYLE_STYLIZED) the lines are bolder, colored by the kind of orbit,
// brightest just behind each body and fading back along its path (the core samples paths in time order, so the
// fade shows the direction of motion), brighter for the focused body's orbit and the orbits around it, and dashed
// where the positions are approximate (docs/ART_DIRECTION.md section 8). Positions are unchanged: every line still
// follows the core's path. TEXCOORD1 (UNorm16) carries, per vertex: x the fraction of the path behind the body
// (0 at the body), y the point's index / 65535, z the OrbitCategory / 65535, w 1 near the focus. The parameters are
// globals set by StylizedOrbitLines.cs, neutral at zero (x = 0 draws the realistic lines):
// - _UsimOrbitStyle: x half width in pixels, y opacity at the far end of the fade, z opacity multiplier near the
//   focus, w opacity multiplier elsewhere.
// - _UsimOrbitDash: x points per dash period of approximate lines (0 = solid), y the drawn part of a period.
// - _UsimOrbitPlanet, _UsimOrbitDwarf, _UsimOrbitMoon, _UsimOrbitApproximate: rgb color (linear), a opacity.
Shader "UniverseSim/Line"
{
    Properties
    {
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
            #pragma multi_compile _ USIM_STYLE_STYLIZED
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _HalfWidthPixels;
            CBUFFER_END

            // The stylized lines (see the top of this file); global, neutral at zero.
            float4 _UsimOrbitStyle;
            float4 _UsimOrbitDash;
            half4 _UsimOrbitPlanet;
            half4 _UsimOrbitDwarf;
            half4 _UsimOrbitMoon;
            half4 _UsimOrbitApproximate;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 neighbour : TEXCOORD0; // xyz: the neighbouring point; w: side (+1 or -1)
                float4 path : TEXCOORD1;      // x: fraction behind the body; y: point / 65535; z: category / 65535; w: near focus
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color : COLOR;
                float4 path : TEXCOORD0;
            };

            bool StylizedLines()
            {
            #if defined(USIM_STYLE_STYLIZED)
                return _UsimOrbitStyle.x > 0.0;
            #else
                return false;
            #endif
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float halfWidth = StylizedLines() ? _UsimOrbitStyle.x : _HalfWidthPixels;
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
                        here.xy += normal * input.neighbour.w * halfWidth * 2.0 / screen * here.w;
                    }
                }
                output.positionHCS = here;
                output.color = input.color;
                output.path = float4(input.path.x, input.path.y * 65535.0, input.path.z * 65535.0, input.path.w);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (!StylizedLines())
                {
                    return input.color;
                }
                float category = input.path.z;
                half4 c = category < 0.5 ? _UsimOrbitPlanet
                        : category < 1.5 ? _UsimOrbitDwarf
                        : category < 2.5 ? _UsimOrbitMoon : _UsimOrbitApproximate;
                if (category > 2.5 && _UsimOrbitDash.x > 0.0 && frac(input.path.y / _UsimOrbitDash.x) > _UsimOrbitDash.y)
                {
                    discard;
                }
                // Brightest just behind the body, fading back along the path to its far end just ahead of the body.
                float behind = saturate(input.path.x);
                float trail = lerp(_UsimOrbitStyle.y, 1.0, pow(1.0 - behind, 1.5));
                float focus = input.path.w > 0.5 ? _UsimOrbitStyle.z : _UsimOrbitStyle.w;
                return half4(c.rgb, saturate(c.a * trail * focus));
            }
            ENDHLSL
        }
    }
}
