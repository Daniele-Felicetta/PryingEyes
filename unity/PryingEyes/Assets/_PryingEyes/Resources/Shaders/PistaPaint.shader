Shader "PryingEyes/PistaPaint"
{
    // The paint, the leathers and the helmet of the bikes and riders of the track mode: URP's own light (sun and shadows, ambient, reflections, fog) through UniversalFragmentPBR with its CLEAR COAT
    // switched on (a glossy layer over the base one: the paint of a bike, the lacquer of a helmet, the sheen of a leather suit), and the livery DRAWN HERE from where the pixel is on the part, because the
    // bodies are lofted by code and have no texture coordinates to paint on.
    //
    // Which part a mesh is, is in the red channel of its vertex colour (PistaBikeMesh tags every mesh): 0 the paint of the bike, 1 its accent paint, 2 the torso of the suit, 3 the hump and shoulders,
    // 4 a leg, 5 a knee slider, 6 an upper arm, 7 a forearm, 8 the helmet. The tubes (legs and arms) also carry their own coordinates: uv.x round the tube, uv.y along it. The rest is the position of the
    // pixel in the frame of the part, which is a metre of bike, hip or head.
    //   _Pattern, for the bike: 0 two stripes along the top, 1 a two-tone split, 2 a diagonal flash on the sides, 3 a roundel, 4 a nose and a tail cap, 5 plain.
    //   For the suit: 0 plain with seams, 1 stripes down the back and the legs, 2 bands over the shoulders and the arms, 3 stripes across the chest and the thighs, then the same again.
    //   For the helmet: 0 a stripe on the crown, 1 two thin stripes and a chin bar, 2 a band along the side, 3 a ring round the crown, then plain.
    // The models drawn in Blender (modelli/moto: Resources/Moto/*.fbx) have no vertex tags: the part is the material's own (_Part, from the name of the material in the FBX), and the baked occlusion of every
    // vertex is in the green of its colour (_AO = 1; the file writes the same value in the three channels, and the meshes drawn by code, which tag the red, keep the green at 1). The new rider: 20 the torso (the frame of the hips), 21 the hump, 22 a thigh, 23 a shin, 24 an upper arm, 25 a forearm (their uv.x runs round them, uv.y along
    // each part), 26 a knee slider, 27 the leather of a glove or a boot. The helmet keeps 8 (the frame of the head).
    // Part 30 is a PLAIN part of a bike (the frame, the engine, the exhaust, the plastics, the seat): its own colour, its smoothness (_Smooth) and its metal (_Metal), no livery, no relief, no clear coat
    // (PistaTex.Plain); it is here, and not in the Lit shader, so that the baked occlusion of the model (_AO = 1) darkens the ambient light and the reflections in every crease of the bike.
    // Part 31 is a plain part that is also a DRILLED BRAKE DISC: two staggered rows of holes cut out (clip) in the band between _R0 and _R1 metres from the axle.
    // The shadow, depth and depth-normal passes are the Lit's (UsePass), so the bike casts, receives and feeds the ambient occlusion like everything else.
    Properties
    {
        _BaseColor ("Colour", Color) = (0.70, 0.10, 0.10, 1)
        _AccentColor ("Accent colour", Color) = (0.95, 0.95, 0.94, 1)
        _Pattern ("Pattern (see above)", Float) = 5
        _Coat ("Clear coat: 0 matt .. 1 gloss", Float) = 1
        _Flake ("Metallic flake: 0 solid .. 1 full", Float) = 0
        _Smooth ("Smoothness of the base layer", Float) = 0.5
        _Metal ("Metallic of a plain part (30) or a disc (31)", Float) = 0
        _R0 ("Inner radius of a drilled disc (31)", Float) = 0.1
        _R1 ("Outer radius of a drilled disc (31)", Float) = 0.16
        _Part ("Part of the livery when the mesh has no tag (-1: read the vertex colour)", Float) = -1
        _AO ("1: the red of the vertex colour is baked occlusion", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PaintVertex
            #pragma fragment PaintFragment
            #define _CLEARCOAT 1

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _AccentColor;
                float _Pattern;
                float _Coat;
                float _Flake;
                float _Smooth;
                float _Metal;
                float _R0;
                float _R1;
                float _Part;
                float _AO;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 posOS      : TEXCOORD3;
                float3 nrmOS      : TEXCOORD4;
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half4 fogFactorAndVertexLight : TEXCOORD5;
            #else
                half fogFactor : TEXCOORD5;
            #endif
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD6;
            #endif
                float4 tagUV      : TEXCOORD7;      // x: the part (vertex colour red), y: the baked occlusion (vertex colour green), zw: the coordinates of a tube
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD10;
            #endif
                float4 positionCS : SV_POSITION;
            };

            Varyings PaintVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
                half fogFactor = 0;
            #if !defined(_FOG_FRAGMENT)
                fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif
                output.normalWS = normalInput.normalWS;
                output.posOS = input.positionOS.xyz;
                output.nrmOS = input.normalOS;
                output.tagUV = float4(input.color.r, input.color.g, input.uv);
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
            #else
                output.fogFactor = fogFactor;
            #endif
                output.positionWS = vertexInput.positionWS;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(vertexInput);
            #endif
                output.positionCS = vertexInput.positionCS;
                return output;
            }

            float H31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float3 H33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float VNoise3(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(H31(i), H31(i + float3(1, 0, 0)), f.x), lerp(H31(i + float3(0, 1, 0)), H31(i + float3(1, 1, 0)), f.x), f.y);
                float b = lerp(lerp(H31(i + float3(0, 0, 1)), H31(i + float3(1, 0, 1)), f.x), lerp(H31(i + float3(0, 1, 1)), H31(i + float3(1, 1, 1)), f.x), f.y);
                return lerp(a, b, f.z);
            }

            // 1 inside a band of half width 'hw' round d = 0, and 0 outside it, with an edge as soft as one pixel ('aa': how far the position moves for one pixel)
            float Band(float d, float hw, float aa)
            {
                return saturate((hw - abs(d)) / aa + 0.5);
            }

            // 1 where d > 0, 0 where d < 0, soft over one pixel
            float Side(float d, float aa)
            {
                return saturate(d / aa + 0.5);
            }

            // The normal bent by a height field (Mikkelsen's bump mapping of an unparametrised surface): no tangents needed
            float3 BumpNormal(float3 N, float3 P, float height)
            {
                float3 dpdx = ddx(P), dpdy = ddy(P);
                float dhdx = ddx(height), dhdy = ddy(height);
                float3 r1 = cross(dpdy, N);
                float3 r2 = cross(N, dpdx);
                float det = dot(dpdx, r1);
                float3 grad = sign(det) * (dhdx * r1 + dhdy * r2);
                float3 v = abs(det) * N - grad;
                float l = length(v);
                return l > 1e-12 ? v / l : N;
            }

            void PaintFragment(Varyings input
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                float3 P = input.positionWS;
                float3 N = normalize(input.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(P);
                float3 O = input.posOS;
                float3 NO = normalize(input.nrmOS);
                float2 tuv = input.tagUV.zw;
                int part = (int)(input.tagUV.x * 255.0 + 0.5);
                float ao = 1.0;
                if (_Part >= 0.0) { part = (int)(_Part + 0.5); ao = lerp(1.0, input.tagUV.y, saturate(_AO)); }      // an imported model: the part is the material's, the green of the colour is occlusion (the meshes drawn by code tag the red and keep the green at 1)
                else if (part > 9) part = 1;                                                    // a mesh nobody tagged (no vertex colour reads 255): plain paint
                int pat = (int)(_Pattern + 0.5);
                // how far the position on the part moves for one pixel: the width of the soft edge of a line (derivatives are taken here, outside every branch)
                float3 fw3 = fwidth(O);
                float aa = max(fw3.x, max(fw3.y, fw3.z)) + 1e-5;
                float aau = fwidth(tuv.x) + 1e-4, aav = fwidth(tuv.y) + 1e-4;

                bool isPaint = part <= 1;
                bool isLeather = (part >= 2 && part <= 7 && part != 5) || (part >= 20 && part <= 25) || part == 27;
                bool isRubber = part == 9;

                // ---- the fine relief: the orange peel of the lacquer, the grain of the leather, the pores of the rubber. It fades out when its wavelength gets down to a pixel (far away, or a small picture), where it would only shimmer
                float freq = isLeather ? 230.0 : (isPaint ? 85.0 : (isRubber ? 150.0 : 60.0));
                float relief = saturate(1.0 - aa * freq);
                float hh = VNoise3(O * freq) * 0.65 + VNoise3(O * freq * 2.3 + 7.1) * 0.35;
                float amp = (part >= 30 ? 0.0 : (isLeather ? 0.0008 : (isPaint ? 0.00024 : (isRubber ? 0.0004 : 0.00018)))) * relief;
                float3 Nb = BumpNormal(N, P, hh * amp);

                float3 baseC = _BaseColor.rgb;
                float3 accC = _AccentColor.rgb;
                float acc = 0.0;            // how much of the accent colour is on this pixel
                float dark = 0.0;           // seams and gaps: how much darker it is
                float coat = _Coat;
                float smo = _Smooth;
                float coatSmo = 0.97;
                float metal = 0.0;
                float flake = 0.0;

                if (part <= 1)
                {
                    // ---- the bike: x to the right, y up, z forward, in metres of bike (the fairing runs from z = -0.03 to 0.86, the tank from 0.44 to -0.22, the tail from -0.46 to -0.96)
                    if (part == 0)
                    {
                        float top = smoothstep(0.10, 0.50, NO.y);
                        float side = smoothstep(0.35, 0.65, abs(NO.x));
                        float ax = abs(O.x);
                        if (pat == 0) acc = Band(ax - 0.055, 0.020, aa) * top;
                        else if (pat == 1) acc = Side(0.735 + 0.12 * saturate(O.z / 0.8) - O.y, aa) * smoothstep(-0.02, 0.05, O.z);
                        else if (pat == 2)
                        {
                            float d = (O.y - 0.74) - 0.45 * (O.z - 0.32);
                            acc = saturate(Band(d, 0.045, aa) + Band(d - 0.12, 0.012, aa)) * side * step(-0.05, O.z);
                        }
                        else if (pat == 3)
                        {
                            float r = length(float2(O.y - 0.74, O.z - 0.34));
                            float r2 = length(float2(O.y - 0.90, O.z + 0.72));
                            acc = max(Side(0.10 - r, aa), Side(0.055 - r2, aa)) * side;
                            acc *= 1.0 - 0.85 * Band(r - 0.075, 0.006, aa) * step(r, 0.12);
                        }
                        else if (pat == 4)
                        {
                            acc = max(Side(O.z - 0.62, aa), Side(-0.80 - O.z, aa));
                            acc = max(acc, max(Band(O.z - 0.55, 0.008, aa), Band(O.z + 0.74, 0.008, aa)));
                        }
                        // the joints between the panels of the bike drawn by code: the nose cone, the lower edge of the fairing, the tail (the bike of the model has its own shapes)
                        if (_Part < 0.0)
                        {
                            dark = Band(O.z - 0.60, 0.0035, aa) * smoothstep(0.30, 0.55, O.y);
                            dark = max(dark, Band(O.y - 0.70, 0.0035, aa) * smoothstep(0.02, 0.12, O.z) * (1.0 - smoothstep(0.52, 0.60, O.z)));
                            dark = max(dark, Band(O.z + 0.62, 0.003, aa) * step(O.z, -0.30));
                        }
                        // metallic flake: a few cells of a fine lattice catch the sun at their own angle
                        float3 fr = H33(floor(O * 380.0));
                        flake = step(0.88, fr.x) * _Flake * saturate(1.0 - aa * 380.0 * 0.9);
                        float3 fn = normalize(fr - 0.5 + 1e-4);
                        Nb = normalize(Nb + (fn - dot(fn, Nb) * Nb) * flake * 0.55);
                        metal = _Flake * 0.45;
                    }
                    else
                    {
                        metal = _Flake * 0.30;
                    }
                }
                else if (part == 2)
                {
                    // ---- the torso of the suit: the hip frame with the chest leaning forward (t runs from the hip, 0, up the back to the neck, 0.45)
                    float t = dot(O, float3(0.0, 0.616, 0.788));
                    float back = smoothstep(0.10, 0.60, NO.y);
                    float ax = abs(O.x);
                    if (pat == 1) acc = Band(ax - 0.045, 0.016, aa) * back;
                    else if (pat == 2) acc = Band(t - 0.31, 0.022, aa);
                    else if (pat == 3) acc = saturate(Band(t - 0.20, 0.014, aa) + Band(t - 0.245, 0.014, aa)) * (1.0 - back * 0.4);
                    else if (pat == 4) acc = Band(ax - 0.10, 0.012, aa) * back;
                    dark = Band(t - 0.10, 0.003, aa);
                    dark = max(dark, Band(ax - 0.115, 0.003, aa) * back);
                    smo = _Smooth;
                }
                else if (part == 3)
                {
                    // ---- the hump and the shoulders
                    float top = smoothstep(0.0, 0.5, NO.y);
                    if (pat == 1) acc = Band(abs(O.x) - 0.03, 0.010, aa) * top;
                    dark = Band(abs(O.x) - 0.075, 0.003, aa) * top;
                }
                else if (part == 4)
                {
                    // ---- a leg: the thigh and the shin are tubes, uv.x runs round them and uv.y along them
                    float shin = step(O.y, -0.17);
                    dark = Band(tuv.x - 0.5, 0.012, aau);
                    if (pat == 1) acc = Band(tuv.x - 0.25, 0.045, aau);
                    else if (pat == 2) acc = Band(tuv.y - 0.55, 0.06, aav) * shin;
                    else if (pat == 3) acc = Band(tuv.y - 0.40, 0.05, aav) * (1.0 - shin);
                    else if (pat == 4) acc = Band(tuv.x - 0.75, 0.03, aau);
                }
                else if (part == 9)
                {
                    // ---- a tyre: the axle runs along x. Two grooves round the tread, a pale line on each sidewall, scuffed rubber
                    float rr = length(O.yz);
                    float ax = abs(O.x);
                    dark = saturate(Band(ax - 0.034, 0.0032, aa) + Band(ax - 0.060, 0.0022, aa) * 0.6);
                    float sideW = smoothstep(0.5, 0.85, abs(NO.x));
                    acc = Band(rr - 0.268, 0.0035, aa) * sideW * 0.35;
                    baseC = baseC * (0.80 + 0.55 * hh);
                    smo = 0.22;
                    coat = 0.0;
                }
                else if (part == 30 || part == 31)
                {
                    // ---- a plain part of a model: the colour, the smoothness and the metal of the material, with the occlusion baked in its vertices (above)
                    metal = _Metal;
                    smo = _Smooth;
                    coat = 0.0;
                    if (part == 31)
                    {
                        // a brake disc (the axle along x, the disc in the plane y-z of the wheel's own frame): two staggered rows of 22 holes in the band between _R0 and _R1, cut out of the surface
                        // (the holes show the rim and the caliper behind them; the shadow and the depth keep the whole disc, which nobody can tell at that size)
                        float band = max(_R1 - _R0, 1e-4);
                        float rr = length(O.yz);
                        float t = (rr - _R0) / band;
                        float turn = atan2(O.z, O.y) * 0.15915494;                    // one turn is 1
                        float hole = 1.0;
                        for (int row = 0; row < 2; row++)
                        {
                            float cells = 22.0;
                            float a = frac(turn * cells + (row == 0 ? 0.0 : 0.5)) - 0.5;
                            float dx = a * (6.2831853 * rr / cells);                  // metres along the arc
                            float dy = (t - (row == 0 ? 0.28 : 0.72)) * band;         // metres along the radius
                            hole = min(hole, length(float2(dx, dy)) - 0.075 * band);
                        }
                        clip(hole);
                        // the track the pads sweep is a little duller and darker than the edges
                        float track = smoothstep(0.0, 0.12, t) * (1.0 - smoothstep(0.88, 1.0, t));
                        smo = lerp(smo, smo * 0.65, track);
                        baseC *= lerp(1.0, 0.82, track);
                    }
                }
                else if (part == 5)
                {
                    // ---- a knee slider: a worn puck of plastic, scuffed where it has been dragged
                    // (grey whatever the colours of the suit: mixed with a red accent it came out a dull mauve)
                    float s = VNoise3(O * 260.0) * 0.55 + VNoise3(O * 620.0) * 0.45;
                    float streak = smoothstep(0.62, 0.80, VNoise3(float3(O.x * 90.0, O.y * 700.0, O.z * 90.0)));        // fine streaks where it has been dragged (a coarse noise looked like a ball of felt)
                    baseC = lerp(float3(0.46, 0.46, 0.48), accC, 0.10) * (0.84 + 0.26 * s) * (1.0 - 0.25 * streak);
                    smo = 0.40;
                    coat = 0.0;
                }
                else if (part == 6)
                {
                    // ---- an upper arm
                    dark = Band(tuv.x - 0.5, 0.012, aau);
                    if (pat == 2) acc = Band(tuv.y - 0.72, 0.07, aav);
                    else if (pat == 1) acc = Band(tuv.x - 0.25, 0.045, aau);
                    else if (pat == 3) acc = Band(tuv.y - 0.50, 0.05, aav);
                }
                else if (part == 7)
                {
                    // ---- a forearm: the cuff of the glove at the wrist
                    dark = Band(tuv.x - 0.5, 0.012, aau);
                    acc = Side(tuv.y - 0.88, aav) * (pat == 0 ? 0.0 : 1.0);
                    if (pat == 2) acc = max(acc, Band(tuv.y - 0.35, 0.06, aav));
                    else if (pat == 1) acc = max(acc, Band(tuv.x - 0.25, 0.045, aau));
                }
                else if (part >= 20 && part <= 27)
                {
                    // ---- the rider of modelli/moto (hips frame: x right, y up, z forward; the back leans 28 degrees above the horizontal)
                    const float3 BACK = float3(0.0, 0.469, 0.883);                  // along the back, toward the head
                    const float3 OUT = float3(0.0, 0.883, -0.469);                  // out of the back
                    float shin = part == 23 ? 1.0 : 0.0;
                    if (part == 20)
                    {
                        float t = dot(O, BACK);
                        float back = smoothstep(0.15, 0.70, dot(NO, OUT));
                        float ax = abs(O.x);
                        if (pat == 1) acc = Band(ax - 0.050, 0.018, aa) * back;
                        else if (pat == 2) acc = Band(t - 0.43, 0.026, aa);
                        else if (pat == 3) acc = saturate(Band(t - 0.30, 0.016, aa) + Band(t - 0.352, 0.016, aa)) * (1.0 - back * 0.4);
                        else if (pat == 4) acc = Band(ax - 0.12, 0.014, aa) * back;
                        dark = Band(t - 0.17, 0.0035, aa);
                        dark = max(dark, Band(ax - 0.13, 0.0035, aa) * back);
                    }
                    else if (part == 21)
                    {
                        float top = smoothstep(0.0, 0.5, dot(NO, OUT));
                        if (pat == 1) acc = Band(abs(O.x) - 0.03, 0.010, aa) * top;
                        dark = Band(abs(O.x) - 0.075, 0.003, aa) * top;
                    }
                    else if (part == 22 || part == 23)
                    {
                        dark = Band(tuv.x - 0.5, 0.012, aau);
                        if (pat == 1) acc = Band(tuv.x - 0.25, 0.045, aau);
                        else if (pat == 2) acc = Band(tuv.y - 0.55, 0.06, aav) * shin;
                        else if (pat == 3) acc = Band(tuv.y - 0.40, 0.05, aav) * (1.0 - shin);
                        else if (pat == 4) acc = Band(tuv.x - 0.75, 0.03, aau);
                    }
                    else if (part == 24)
                    {
                        dark = Band(tuv.x - 0.5, 0.012, aau);
                        if (pat == 2) acc = Band(tuv.y - 0.72, 0.07, aav);
                        else if (pat == 1) acc = Band(tuv.x - 0.25, 0.045, aau);
                        else if (pat == 3) acc = Band(tuv.y - 0.50, 0.05, aav);
                    }
                    else if (part == 25)
                    {
                        dark = Band(tuv.x - 0.5, 0.012, aau);
                        acc = Side(tuv.y - 0.88, aav) * (pat == 0 ? 0.0 : 1.0);
                        if (pat == 2) acc = max(acc, Band(tuv.y - 0.35, 0.06, aav));
                        else if (pat == 1) acc = max(acc, Band(tuv.x - 0.25, 0.045, aau));
                    }
                    else if (part == 26)
                    {
                        float sl = VNoise3(O * 260.0) * 0.55 + VNoise3(O * 620.0) * 0.45;
                        float streak = smoothstep(0.62, 0.80, VNoise3(float3(O.x * 90.0, O.y * 700.0, O.z * 90.0)));
                        baseC = lerp(float3(0.46, 0.46, 0.48), accC, 0.10) * (0.84 + 0.26 * sl) * (1.0 - 0.25 * streak);
                        smo = 0.40;
                        coat = 0.0;
                    }
                    else
                    {
                        // a glove or a boot: black leather, a little lacquer
                        smo = _Smooth;
                    }
                }
                else
                {
                    // ---- the helmet: a lacquered shell, in the frame of the head (z to the front, y up)
                    float top = smoothstep(0.0, 0.4, NO.y);
                    float side = smoothstep(0.35, 0.70, abs(NO.x));
                    if (pat == 0) acc = Band(abs(O.x), 0.020, aa) * top;
                    else if (pat == 1)
                    {
                        acc = Band(abs(O.x) - 0.045, 0.012, aa) * top;
                        acc = max(acc, Side(-0.045 - O.y, aa) * smoothstep(0.02, 0.10, O.z));
                    }
                    else if (pat == 2) acc = Band(O.y - 0.02 + 0.12 * O.z, 0.028, aa) * side;
                    else if (pat == 3) acc = Band(O.z - 0.02, 0.024, aa);
                    metal = _Flake * 0.4;
                    smo = _Smooth;
                }

                float3 col = lerp(baseC, accC, saturate(acc));
                col *= 1.0 - 0.72 * saturate(dark);
                if (isLeather) { col *= 1.0 + (hh - 0.5) * 0.14 * relief; coatSmo = 0.60; }
                smo = saturate(smo * (1.0 - 0.6 * saturate(dark)));

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = col;
                surfaceData.alpha = 1;
                surfaceData.metallic = saturate(metal + flake * 0.3);
                surfaceData.specular = 0;
                surfaceData.smoothness = smo;
                surfaceData.occlusion = ao;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = 0;
                surfaceData.clearCoatMask = saturate(coat);
                surfaceData.clearCoatSmoothness = coatSmo;

                InputData inputData = (InputData)0;
                inputData.positionWS = P;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = Nb;
                inputData.viewDirectionWS = V;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                inputData.fogCoord = InitializeInputDataFog(float4(P, 1.0), input.fogFactorAndVertexLight.x);
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
            #else
                inputData.fogCoord = InitializeInputDataFog(float4(P, 1.0), input.fogFactor);
            #endif
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            #if defined(_SCREEN_SPACE_IRRADIANCE)
                inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy, inputData.normalWS);
            #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                inputData.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(inputData.positionWS), inputData.normalWS, inputData.viewDirectionWS,
                                              input.positionCS.xy, input.probeOcclusion, inputData.shadowMask);
            #else
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            #endif

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                outColor = color;
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
    FallBack "Universal Render Pipeline/Lit"
}
