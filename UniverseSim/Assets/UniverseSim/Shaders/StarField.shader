// The sky background (SPEC FR-REN-09). Each star is a quad of four vertices at the same position (its
// direction from the core times the sky distance); the vertex shader spreads the corners (TEXCOORD0.xy) by
// a size in pixels (TEXCOORD0.z), so a star covers the same pixels at any zoom. The fragment is a soft disc,
// added to what is behind it. Drawn first (queue Background) without writing depth: every body covers it.
// Brightness and color come from the vertex color, a display mapping of the magnitude and color index made
// by StarField.cs.
//
// In the stylized style (A-09, keyword USIM_STYLE_STYLIZED) the stars are brighter and larger, and the brightest
// get a four-point sparkle (docs/ART_DIRECTION.md section 6). The parameters are globals set by StylizedStars.cs,
// neutral at zero:
// - _UsimStars: x exposure - 1, y size - 1, z sparkle strength, w the display intensity where sparkles start.
// - _UsimStarSparkle: x the sparkle's length in star radii at full strength.
// Every part grows with the display intensity, so a brighter star never looks fainter than a dimmer one, and the
// color is only scaled, so a bluer star never looks redder.
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
            #pragma multi_compile _ USIM_STYLE_STYLIZED
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Exposure;
            CBUFFER_END

            // The stylized stars (see the top of this file); global, neutral at zero.
            float4 _UsimStars;
            float4 _UsimStarSparkle;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 corner : TEXCOORD0; // xy: corner in [-1, 1]; z: half size in pixels
                half4 color : COLOR;       // rgb: display color; a: display intensity
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 corner : TEXCOORD0;  // in star radii
                float2 sparkle : TEXCOORD1; // x: the quad's half size in star radii; y: sparkle weight
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float4 clip = TransformObjectToHClip(input.positionOS.xyz);
            #if defined(USIM_STYLE_STYLIZED)
                // The sparkle grows with the display intensity above its threshold (the brightest stars reach about
                // 1.6), and the quad grows with it so the sparkle fits; the disc keeps its size in star radii.
                float weight = saturate((input.color.a - _UsimStars.w) / max(2.0 - _UsimStars.w, 1e-3)) * _UsimStars.z;
                float spread = 1.0 + _UsimStarSparkle.x * saturate(weight * 4.0);
                float halfSize = input.corner.z * (1.0 + _UsimStars.y) * spread;
            #else
                float weight = 0.0;
                float spread = 1.0;
                float halfSize = input.corner.z;
            #endif
                // One pixel is 2 / screen size in normalized device coordinates.
                clip.xy += input.corner.xy * halfSize * 2.0 / _ScreenParams.xy * clip.w;
                output.positionHCS = clip;
                output.corner = input.corner.xy * spread;
                output.sparkle = float2(spread, weight);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 c = input.corner;
                float r2 = dot(c, c);
                half disc = (half)exp(-4.0 * r2) * (half)saturate(1.0 - r2);
            #if defined(USIM_STYLE_STYLIZED)
                // Four points along the screen axes, fading toward the quad's edge.
                float spread = input.sparkle.x;
                float along = saturate(1.0 - abs(c.x) / spread) * exp(-abs(c.y) * 5.0);
                float across = saturate(1.0 - abs(c.y) / spread) * exp(-abs(c.x) * 5.0);
                half sparkle = (half)((along * along + across * across) * input.sparkle.y);
                half exposure = _Exposure * (half)(1.0 + _UsimStars.x);
                return half4(input.color.rgb * (input.color.a * (disc + sparkle) * exposure), 1);
            #else
                return half4(input.color.rgb * (input.color.a * disc * _Exposure), 1);
            #endif
            }
            ENDHLSL
        }
    }
}
