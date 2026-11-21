Shader "PryingEyes/PistaSurface"
{
    // The ground of the track mode: asphalt, paved run-off, gravel, grass and kerbs in one lit shader, driven by two small tileable textures that the game makes at the start (PistaDetail:
    // grain with its normal, and stones with their normal) and by the position of the pixel. Everything else is URP's own light (sun and shadows, ambient, fog): UniversalFragmentPBR like the Lit.
    //
    // The road mesh carries a second set of coordinates (uv1 = lateral offset from the centre line, distance along the lap, lateral offset of the rubbered racing line, half width of the road),
    // which is what lets the asphalt show the line the bikes wear into it, the marbles beside it, the joints between the paving patches and the dust at the edges.
    // The shadow, depth and depth-normal passes are the Lit's (UsePass), so the ground casts, receives and feeds the ambient occlusion like everything else.
    Properties
    {
        _Kind ("Kind: 0 asphalt, 1 paved run-off, 2 gravel, 3 grass, 4 kerb", Float) = 0
        _ColorA ("Colour A (base)", Color) = (0.15, 0.15, 0.16, 1)
        _ColorB ("Colour B (lighter, older)", Color) = (0.27, 0.27, 0.28, 1)
        _ColorC ("Colour C (dry grass)", Color) = (0.34, 0.31, 0.14, 1)
        _Scales ("Texture frequencies per metre: fine grain, mid grain, macro, stones", Vector) = (0.7, 0.11, 0.013, 0.85)
        _Smooth ("Smoothness", Float) = 0.2
        [NoScaleOffset] _Grain ("Grain (R height, GB normal, A second field)", 2D) = "gray" {}
        [NoScaleOffset] _Stones ("Stones (R dome, GB normal, A cell id)", 2D) = "gray" {}
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
            #pragma vertex PistaVertex
            #pragma fragment PistaFragment

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

            TEXTURE2D(_Grain);
            SAMPLER(sampler_Grain);
            TEXTURE2D(_Stones);
            SAMPLER(sampler_Stones);

            CBUFFER_START(UnityPerMaterial)
                float _Kind;
                half4 _ColorA;
                half4 _ColorB;
                half4 _ColorC;
                float4 _Scales;
                float _Smooth;
            CBUFFER_END

            float4 _DSCloudShadow;      // z: how much the shadow of a cloud darkens the ground in the sun (0 at night and in a storm), w: the cover of the sky; written by DesertSky
            float _PistaWet;            // 0 dry .. 1 soaked: darker, smoother ground (written by the track director)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 road       : TEXCOORD1;      // x lateral offset (m), y distance along the lap (m), z lateral offset of the racing line (m), w half width of the road (m)
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 road       : TEXCOORD3;
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half4 fogFactorAndVertexLight : TEXCOORD5;
            #else
                half fogFactor : TEXCOORD5;
            #endif
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD6;
            #endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD10;
            #endif
                float4 positionCS : SV_POSITION;
            };

            Varyings PistaVertex(Attributes input)
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
                output.road = input.road;
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

            float H21(float2 p)
            {
                float3 q = frac(float3(p.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H21(i), H21(i + float2(1, 0)), f.x), lerp(H21(i + float2(0, 1)), H21(i + float2(1, 1)), f.x), f.y);
            }

            float Fbm2(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += VNoise(p) * a; p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            // The shadow of the clouds: wide patches (a few hundred metres) with a soft edge, drifting slowly with the wind. 1 = in the sun, less = under a cloud; the shade is cooler than the light.
            half3 CloudShade(float2 p, float dist)
            {
                float2 q = (p + float2(0.82, 0.57) * (_Time.y * 5.5)) / 430.0;
                float nn = Fbm2(q + 9.3);
                float centre = 0.69 - _DSCloudShadow.w * 0.62;
                float dens = smoothstep(centre - 0.07, centre + 0.07, nn);
                return 1.0 - (_DSCloudShadow.z * dens * saturate(1.3 - dist / 1100.0)) * half3(1.0, 0.88, 0.62);
            }

            void PistaFragment(Varyings input
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                float3 P = input.positionWS;
                float3 N = normalize(input.normalWS);
                float dist = distance(P, _WorldSpaceCameraPos);
                int kind = (int)(_Kind + 0.5);
                float flatK = smoothstep(0.55, 0.70, N.y);                                   // 1 on the ground, 0 on a wall (the skirts under the road)
                float2 p = N.y > 0.62 ? P.xz : (abs(N.x) > abs(N.z) ? P.zy : P.xy);

                // the textures are always read (a read in a branch has no derivatives); what each kind does with them is below
                float4 g0 = SAMPLE_TEXTURE2D(_Grain, sampler_Grain, p * _Scales.x);
                float4 g1 = SAMPLE_TEXTURE2D(_Grain, sampler_Grain, p * _Scales.y + float2(0.37, 0.11));
                float4 g2 = SAMPLE_TEXTURE2D(_Grain, sampler_Grain, p * _Scales.z + float2(0.71, 0.53));
                float4 s0 = SAMPLE_TEXTURE2D(_Stones, sampler_Stones, p * _Scales.w);
                float4 s1 = SAMPLE_TEXTURE2D(_Stones, sampler_Stones, p * 0.05 + float2(0.21, 0.67));
                float fine = g0.r - 0.5, mid = g1.r - 0.5, mac = g2.a - 0.5;
                float2 nFine = g0.gb * 2.0 - 1.0, nMid = g1.gb * 2.0 - 1.0;
                float fineFade = saturate(1.0 - dist / 26.0), midFade = saturate(1.0 - dist / 150.0);

                half3 col;
                float smoo;
                float2 bump;
                float occl = 1.0;
                float wetK = 1.0;                                                            // how much the water of the rain takes on this kind of ground

                if (kind == 0)
                {
                    // ---- asphalt
                    float lat = input.road.x, along = input.road.y, lineD = input.road.z, hw = max(input.road.w, 1.0);
                    float aLat = abs(lat);
                    // the paving patches (a joint every 37 m, each patch a little lighter or darker than its neighbours)
                    float seg = along / 37.0, id = floor(seg);
                    float pat = H21(float2(id, 3.7)) - 0.5;
                    float dj = min(frac(seg), 1.0 - frac(seg)) * 37.0;
                    float joint = (1.0 - smoothstep(0.012, 0.07, dj)) * step(0.5, input.road.w);
                    // the line the bikes wear into the surface: the rubber makes it darker and a little shiny, and throws the marbles to both sides
                    float rel = lat - lineD;
                    float groove = exp(-rel * rel / 0.77) * step(0.5, input.road.w);
                    float marbles = smoothstep(0.9, 1.7, abs(rel)) * (1.0 - smoothstep(2.4, 4.2, abs(rel))) * step(0.5, input.road.w);
                    float edge = smoothstep(hw - 1.5, hw - 0.1, aLat) * step(0.5, input.road.w);
                    col = lerp(_ColorA.rgb, _ColorB.rgb, saturate(0.5 + mac * 1.1 + pat * 0.9));
                    float stoneHi = smoothstep(0.58, 0.88, g0.r);                      // the bright pebbles of the aggregate in the dark binder
                    col *= 1.0 + fine * 0.30 + stoneHi * 0.55 - 0.12 + mid * 0.30;
                    float gv = groove * (0.45 + 1.1 * g2.a);                              // the worn line is not one even band: it comes and goes along the road
                    col *= 1.0 - 0.17 * gv * (0.75 + 0.5 * g1.r);
                    col *= 1.0 + 0.30 * marbles * (g0.r - 0.35);
                    col = lerp(col, col * 1.35 + 0.012, edge * 0.55);
                    col *= 1.0 - 0.45 * joint;
                    float crack = (1.0 - smoothstep(0.0, 0.035, s1.r)) * step(0.60, s1.a) * saturate(1.0 - dist / 80.0) * (1.0 - groove);
                    col *= 1.0 - 0.60 * crack;
                    smoo = _Smooth + 0.16 * gv + 0.10 * fine - 0.05 * marbles;
                    bump = nFine * 0.50 * fineFade + nMid * 0.25 * midFade;
                }
                else if (kind == 1)
                {
                    // ---- paved run-off: lighter and flatter, broken into plates
                    col = lerp(_ColorA.rgb, _ColorB.rgb, saturate(0.5 + mac * 1.2 + mid * 0.5));
                    col *= 1.0 + fine * 0.45;
                    float plate = (1.0 - smoothstep(0.0, 0.03, s1.r)) * step(0.30, s1.a) * saturate(1.0 - dist / 90.0);
                    col *= 1.0 - 0.50 * plate;
                    smoo = _Smooth + 0.06 * fine;
                    bump = nFine * 0.45 * fineFade + nMid * 0.20 * midFade;
                }
                else if (kind == 2)
                {
                    // ---- gravel: every stone its own tone, dark in the gaps between them
                    float id = s0.a;
                    half3 tanC = half3(0.62, 0.54, 0.40), greyC = half3(0.48, 0.47, 0.45), paleC = half3(0.78, 0.74, 0.66), darkC = half3(0.36, 0.33, 0.28);
                    half3 stone = id < 0.35 ? lerp(tanC, paleC, id / 0.35) : (id < 0.75 ? lerp(greyC, tanC, (id - 0.35) / 0.40) : lerp(darkC, greyC, (id - 0.75) / 0.25));
                    stone *= 0.80 + 0.50 * g0.r;
                    float gap = smoothstep(0.0, 0.30, s0.r);
                    col = stone * lerp(0.35, 1.0, gap) * (0.85 + 0.30 * g2.a);
                    smoo = _Smooth + 0.12 * gap;
                    occl = lerp(0.55, 1.0, gap);
                    bump = (s0.gb * 2.0 - 1.0) * 1.1 * saturate(1.0 - dist / 30.0) + nMid * 0.20 * midFade;
                    wetK = 0.5;
                }
                else if (kind == 3)
                {
                    // ---- grass: two greens, patches of dry grass, a speckle that stands for the blades, earth on the steep banks
                    float dry = smoothstep(0.55, 0.80, g2.a * 0.7 + g1.a * 0.5);
                    half3 lush = lerp(_ColorA.rgb, _ColorB.rgb, saturate(0.5 + mid * 1.4 + mac * 0.5));
                    col = lerp(lush, _ColorC.rgb, dry * 0.65);
                    col *= 1.0 + fine * 0.65;
                    float steep = smoothstep(0.86, 0.72, N.y);
                    col = lerp(col, half3(0.20, 0.15, 0.09) * (0.8 + 0.4 * g0.r), steep * 0.8);
                    smoo = _Smooth + 0.05 * fine;
                    bump = nFine * 0.80 * fineFade + nMid * 0.50 * midFade;
                    wetK = 0.3;
                }
                else
                {
                    // ---- kerb: painted concrete, dirtier and darker toward the track (rubber and dust)
                    float lat = input.road.x, hw = max(input.road.w, 1.0);
                    float into = saturate((abs(lat) - hw) / 1.4);
                    col = _ColorA.rgb * (1.0 + fine * 0.35 + mid * 0.25);
                    col *= 1.0 - 0.28 * (1.0 - into);
                    col *= 1.0 - 0.22 * smoothstep(0.55, 0.90, g2.a);
                    smoo = _Smooth + 0.10 * fine;
                    bump = nFine * 0.35 * fineFade;
                }

                float wet = _PistaWet * wetK;
                col *= 1.0 - 0.38 * wet;
                smoo = saturate(smoo + 0.55 * wet);
                float3 Np = N;
                Np.xz += bump * (flatK * (1.0 - 0.7 * wet));
                Np = normalize(Np);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = col;
                surfaceData.alpha = 1;
                surfaceData.metallic = 0;
                surfaceData.specular = 0;
                surfaceData.smoothness = smoo;
                surfaceData.occlusion = occl;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = col * ((1.0 - flatK) * 0.45);                 // the banks of the skirts under the road are never in the sun on their outer side: some fill, so they are earth and not black

                InputData inputData = (InputData)0;
                inputData.positionWS = P;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = Np;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(P);
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
                color.rgb *= CloudShade(P.xz, dist);
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
