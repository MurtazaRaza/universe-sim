// The Sun's corona and glare in the stylized style (A-08, docs/ART_DIRECTION.md section 7), drawn by StylizedSun.cs
// only while that effect is on. A quad at the Sun's center (four vertices at the object origin) is spread by the
// vertex shader to cover the glow on screen; the fragment adds light, so it is order-independent:
// - corona: around the disk, in solar radii, so it scales with the disk's true size;
// - halo and rays: falling off with the distance from the limb in pixels, over lengths that grow with the disk's
//   radius on screen between a floor and a ceiling, so a distant Sun reads as a sparkling star and a near one
//   glares; scaled by the part of the disk no body covers (_Glare.x), so a covered Sun has no glare.
// Everything fades to zero before the quad's edge, so the quad never shows. The quad is depth-tested at the Sun's
// center: the disk's own front surface hides it, so nothing is drawn over the disk and the disk keeps its true size,
// and a body in front of the Sun hides the glow behind it.
Shader "UniverseSim/SunGlow"
{
    Properties
    {
        _SunRadius ("Disk radius, render units (set per frame)", Float) = 1
        _Corona ("Corona: strength, reach (solar radii), falloff exponent", Vector) = (1.6, 4, 2.2, 0)
        _CoronaColor ("Corona color", Color) = (1, 0.78, 0.48, 1)
        _Halo ("Halo: strength, length per disk radius, shortest and longest length (pixels)", Vector) = (0.9, 0.5, 4, 16)
        _Rays ("Rays: strength, count, sharpness", Vector) = (0.8, 6, 48, 0)
        _RayLength ("Ray length: per disk radius, shortest and longest (pixels)", Vector) = (3, 12, 90, 0)
        _GlareColor ("Halo and ray color", Color) = (1, 0.9, 0.72, 1)
        _Glare ("Glare: x the visible part of the disk (set per frame)", Vector) = (1, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Blend One One
        ZWrite Off
        ZTest LEqual
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
                float _SunRadius;
                float4 _Corona;
                half4 _CoronaColor;
                float4 _Halo;
                float4 _Rays;
                float4 _RayLength;
                half4 _GlareColor;
                float4 _Glare;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 corner : TEXCOORD0; // in [-1, 1]
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 offset : TEXCOORD0; // pixels from the disk's center
                float4 sizes : TEXCOORD1;  // pixels: x the disk's radius, y the quad's half size, z halo, w ray length
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float4 clip = TransformObjectToHClip(float3(0, 0, 0));
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float d = length(centerWS - GetCameraPositionWS());
                float r = _SunRadius;
                // The disk's radius on screen: a sphere's silhouette seen from distance d, times the focal length in
                // pixels (the projection's y scale; its sign flips when rendering into a texture).
                float focal = abs(UNITY_MATRIX_P[1][1]) * 0.5 * _ScreenParams.y;
                float radiusPx = focal * r / sqrt(max(d * d - r * r, 1e-20));
                float halo = clamp(_Halo.y * radiusPx, _Halo.z, _Halo.w);
                float rays = clamp(_RayLength.x * radiusPx, _RayLength.y, _RayLength.z);
                // Past five falloff lengths the glare is under 1 % of its peak; the last fifth of the quad fades it out.
                float extent = 1.25 * max(radiusPx * _Corona.y, radiusPx + 5.0 * max(halo, rays));
                output.offset = input.corner * extent;
                output.sizes = float4(radiusPx, extent, halo, rays);
                if (clip.w <= 0.0)
                {
                    // The Sun is behind the camera: outside the clip volume, so the quad is culled.
                    output.positionHCS = float4(0, 0, -2, 1);
                    return output;
                }
                // One pixel is 2 / screen size in normalized device coordinates.
                clip.xy += input.corner * extent * 2.0 / _ScreenParams.xy * clip.w;
                output.positionHCS = clip;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float px = length(input.offset);
                float r = max(input.sizes.x, 1e-3);
                float extent = input.sizes.y;
                float fromLimb = max(px - r, 0.0);
                float d = max(px / r, 1.0);
                float reach = max(_Corona.y, 1.001);
                float corona = _Corona.x * pow(d, -_Corona.z) * (1.0 - smoothstep(1.0, reach, d));
                float halo = _Halo.x * exp(-fromLimb / max(input.sizes.z, 1e-3));
                // Rays along fixed screen directions: |cos(n/2 angle)| peaks n times around the disk.
                float angle = atan2(input.offset.y, input.offset.x);
                float rays = _Rays.x * pow(abs(cos(0.5 * _Rays.y * angle)), max(_Rays.z, 1.0)) *
                             exp(-fromLimb / max(input.sizes.w, 1e-3));
                float window = 1.0 - smoothstep(0.8 * extent, extent, px);
                float3 color = _CoronaColor.rgb * corona + _GlareColor.rgb * ((halo + rays) * _Glare.x);
                return half4(color * window, 1);
            }
            ENDHLSL
        }
    }
}
