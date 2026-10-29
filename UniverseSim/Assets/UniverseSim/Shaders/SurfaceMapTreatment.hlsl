// The stylized treatment of the real surface maps (A-06, SPEC FR-REN-11 and section 11.1, docs/ART_DIRECTION.md
// section 4): a Kuwahara smoothing that flattens the map into soft patches along its real features, then more
// saturation and a softer luminance contrast. Each texel keeps its hue, so the map's average hue stays within the
// tolerance of ART_DIRECTION.md (checked by SurfaceMapTreatmentTests through MapTreatmentProbe.shader, which uses
// this same code).
//
// Global properties, set by the SurfaceMapTreatment component; each is neutral at zero, so the treatment shows
// only while that component is enabled (A-03):
// - _UsimMapPaint: x the Kuwahara radius in texels of the mip it reads (0 = off), y how many mips coarser than
//   the screen needs it reads, z the finest mip it reads.
// - _UsimMapGrade: x the saturation gain minus 1, y the contrast exponent minus 1, z the contrast pivot.
#ifndef USIM_SURFACE_MAP_TREATMENT_INCLUDED
#define USIM_SURFACE_MAP_TREATMENT_INCLUDED

float4 _UsimMapPaint;
float4 _UsimMapGrade;

// The mip a texture would be sampled at for uv on screen (texelSize as Unity's _TexelSize: 1/w, 1/h, w, h).
float UsimScreenMip(float2 uv, float4 texelSize)
{
    float2 dx = ddx(uv * texelSize.zw);
    float2 dy = ddy(uv * texelSize.zw);
    return 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8));
}

// A Kuwahara filter of the map at uv: of four 4 x 4 neighborhoods that meet at the texel, spaced
// _UsimMapPaint.x / 3 texels of the mip read, the mean of the one that varies least. The mip is _UsimMapPaint.y
// coarser than screenMip and never finer than _UsimMapPaint.z, so the patches keep about the same size on screen
// once the map is smaller than the screen needs, and never shimmer.
half3 UsimPaintedMap(TEXTURE2D_PARAM(map, samplerMap), float4 texelSize, float2 uv, float screenMip)
{
    float level = max(max(screenMip, 0.0) + _UsimMapPaint.y, _UsimMapPaint.z);
    float2 step = texelSize.xy * exp2(level) * _UsimMapPaint.x / 3.0;
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
                half3 c = SAMPLE_TEXTURE2D_LOD(map, samplerMap, uv + direction * float2(i, j) * step, level).rgb;
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

// Saturation about the Rec. 709 luminance, then a power law on the luminance alone about the pivot (exponent
// below 1: shadows lift, highlights soften); the color is scaled as a whole, so its hue holds.
half3 UsimGradedMap(half3 albedo)
{
    const half3 weights = half3(0.2126, 0.7152, 0.0722);
    half luminance = dot(albedo, weights);
    albedo = max(lerp(luminance.xxx, albedo, 1.0 + _UsimMapGrade.x), 0.0);
    half before = max(dot(albedo, weights), 1e-4);
    half pivot = max(_UsimMapGrade.z, 1e-3);
    half after = pivot * pow(before / pivot, 1.0 + _UsimMapGrade.y);
    return albedo * (after / before);
}

#endif
