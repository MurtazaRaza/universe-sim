// Art direction mockups only (A-01, OQ-09): the StarField shader with a candidate game-like treatment (A-09).
// The data part is as in StarField.shader: each star's direction, display color and display intensity come
// from StarField.cs. The treatment: _SizeScale enlarges every star alike, and stars brighter than
// _SparkleFrom get a four-point sparkle that grows with their intensity, so a brighter star never looks
// fainter than a dimmer one.
Shader "UniverseSim/Mockups/StarField"
{
    Properties
    {
        _Exposure ("Exposure (display)", Range(0, 4)) = 1
        _SizeScale ("Size scale", Range(0.5, 4)) = 1
        _Sparkle ("Sparkle strength", Range(0, 2)) = 0
        _SparkleFrom ("Sparkle from display intensity", Range(0, 2)) = 0.9
        _SparkleReach ("Sparkle length, star radii", Range(0, 12)) = 5
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Exposure;
                float _SizeScale;
                float _Sparkle;
                float _SparkleFrom;
                float _SparkleReach;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 corner : TEXCOORD0; // xy: corner in [-1, 1]; z: half size in pixels
                half4 color : COLOR;       // rgb: display color; a: display intensity
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 corner : TEXCOORD0; // in star radii
                float2 sparkle : TEXCOORD1; // x: the quad's half size in star radii; y: sparkle weight
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float weight = saturate((input.color.a - _SparkleFrom) / max(2.0 - _SparkleFrom, 1e-3)) * _Sparkle;
                float spread = 1.0 + _SparkleReach * saturate(weight * 4.0);
                float4 clip = TransformObjectToHClip(input.positionOS.xyz);
                clip.xy += input.corner.xy * input.corner.z * _SizeScale * spread * 2.0 / _ScreenParams.xy * clip.w;
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
                float spread = input.sparkle.x;
                float along = saturate(1.0 - abs(c.x) / spread) * exp(-abs(c.y) * 5.0);
                float across = saturate(1.0 - abs(c.y) / spread) * exp(-abs(c.x) * 5.0);
                half sparkle = (half)((along * along + across * across) * input.sparkle.y);
                return half4(input.color.rgb * (input.color.a * (disc + sparkle) * _Exposure), 1);
            }
            ENDHLSL
        }
    }
}
