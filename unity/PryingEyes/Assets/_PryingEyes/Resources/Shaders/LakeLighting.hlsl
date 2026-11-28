#ifndef PRYING_EYES_LAKE_LIGHTING
#define PRYING_EYES_LAKE_LIGHTING

// Same polar shoreline as DesertLake.Outline in the world rules.
float LakeRadius(float2 offset, float turn)
{
    float angle = atan2(offset.y, offset.x + 1e-8) + turn;
    float edge = 1.0 + 0.132 * sin(3.0 * angle + 0.7) + 0.065 * cos(2.0 * angle + 0.2) + 0.038 * cos(5.0 * angle - 0.45);
    return length(offset) / edge;
}

float2 LakeCellSite(float2 lattice)
{
    return frac(sin(float2(dot(lattice, float2(127.1, 311.7)), dot(lattice, float2(269.5, 183.3)))) * 43758.5453);
}

float LakeNoise(float2 uv)
{
    float2 cell = floor(uv), local = frac(uv);
    local = local * local * (3.0 - 2.0 * local);
    float lo = lerp(LakeCellSite(cell).x, LakeCellSite(cell + float2(1, 0)).x, local.x);
    float hi = lerp(LakeCellSite(cell + float2(0, 1)).x, LakeCellSite(cell + 1.0).x, local.x);
    return lerp(lo, hi, local.y);
}

float LakeCaustics(float2 bed, float time, float depth)
{
    float2 p = bed * 1.46 + float2(sin(bed.y * 1.45 + time * 0.82), sin(bed.x * 1.63 - time * 0.72)) * 0.25;
    float2 cell = floor(p), within = frac(p);
    float nearest = 9.0, second = 9.0;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 neighbour = float2(x, y);
            float2 site = LakeCellSite(cell + neighbour);
            float2 delta = neighbour + 0.5 + 0.37 * sin(site * 6.2831853 + time * 0.78) - within;
            float distance2 = dot(delta, delta);
            second = min(second, max(nearest, distance2));
            nearest = min(nearest, distance2);
        }
    }
    float width = 0.052 + saturate(max(length(ddx(p)), length(ddy(p)))) * 0.23;
    float ridge = 1.0 - smoothstep(0.009, width, sqrt(second) - sqrt(nearest));
    float focus = 0.72 + 0.28 * sin(dot(bed, float2(2.14, -1.23)) + sin(bed.y * 1.78 + time * 0.66) - time);
    return ridge * ridge * focus * exp(-depth * 0.19);
}
#endif
