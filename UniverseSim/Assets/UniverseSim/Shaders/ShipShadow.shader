// The spaceship's contact shade (docs/ART_DIRECTION.md section 11): a soft, cool darkening of the ground right under
// a ship that has landed or is about to, so it sits on the surface instead of floating over it. It is ambient
// occlusion, not a cast shadow: it lies straight below the ship whatever the light's direction, and never changes
// where the day side or the terminator is. A quad in the ship's ground plane (UVs 0 to 1), multiplied onto what is
// drawn below: lerp(1, _ShadeColor, a), with a = _Strength (1 - r^2)^1.5 falling from the center (r = 0) to nothing
// at the quad's inscribed ellipse (r = 1).
//
// No depth test: the drawn sphere's facets lie a little above or below the ideal ground plane, and the ground hid the
// quad. Instead it draws right after the bodies (Geometry+1) and before the ship (Geometry+2, Ship.shader), which
// covers it. A quad on the ground below the camera projects below the horizon, so it can only fall on the ground and
// on the ship.
Shader "UniverseSim/ShipShadow"
{
    Properties
    {
        _Strength ("Darkening at the center, 0 to 1 (set per frame)", Range(0, 1)) = 0
        _ShadeColor ("Multiplier at full shade, linear", Vector) = (0.30, 0.32, 0.46, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+1" }

        Pass
        {
            Name "ShipShadow"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Strength;
                float4 _ShadeColor;
            CBUFFER_END

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
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float falloff = saturate(1.0 - dot(p, p));
                float a = _Strength * falloff * sqrt(falloff);
                return half4(lerp(1.0, _ShadeColor.rgb, a), 1.0);
            }
            ENDHLSL
        }
    }
}
