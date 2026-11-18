Shader "PryingEyes/DesertProp"
{
    // Le cose che stanno sulla sabbia (sassi, cactus, cespugli, alberi morti, ossa, colonne): una sola materia per tutte, il colore sta nei vertici (rgb) e la
    // parte che si piega al vento nell'alfa (0 alla base, 1 in punta). Si disegnano a migliaia con l'istanziamento (Graphics.RenderMeshInstanced): ogni copia ha una
    // luminosita' sua, ricavata dalla sua posizione, cosi' due sassi uguali non sono mai uguali. La luce e' quella di URP (sole con le ombre, luci puntuali,
    // ambiente, nebbia): UniversalFragmentPBR come il Lit. Le passate delle ombre e della profondita' sono quelle del Lit (UsePass).
    // Il canale UV0.x dei vertici e' la luce propria: da 0 a 0.5 un bagliore fisso (una lampada), da 0.5 a 1 una fiamma, che vibra e di giorno si ritira nella brace
    // (_DSNight, 0 di giorno e 1 di notte, lo scrive DesertSky). Con _Fixed = 1 la luminosita' non dipende dalla posizione (per le cose che si muovono: uccelli, animali, sterpaglie).
    // Il canale UV0.y e' la materia (PropMeshes.Surf): 0 pietra, 1 organico (legno, piante, tela), 2 liscio. Nel fragment la grana e' tutta procedurale e sta attaccata all'oggetto
    // (posizione nello spazio dell'oggetto, quindi non scivola se la cosa si muove): chiazze larghe e grana fine con il loro gradiente (che muove la normale), strati e polvere sulle
    // facce rivolte in su e alla base per la pietra, venature lungo l'altezza per il legno e le piante, e un'ombra di contatto alla base.
    Properties
    {
        _Smooth ("Levigatezza", Range(0, 1)) = 0.12
        _Fixed ("Luminosita' fissa", Range(0, 1)) = 0
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
            #pragma vertex PropVertex
            #pragma fragment PropFragment
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

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
                half _Smooth;
                half _Fixed;
            CBUFFER_END

            float _DSWind;      // 0..1: il vento (le raffiche piegano i cespugli); lo scrive DesertSky
            float _DSNight;     // 0 di giorno, 1 di notte (le fiamme dei fuochi si accendono)
            float4 _DSCloudShadow;      // z: quanto l'ombra di una nuvola scurisce le cose al sole (0 di notte e nella tempesta), w: la copertura del cielo; lo scrive DesertSky (come per la sabbia)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4 color       : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half3 albedo      : TEXCOORD3;
                half emit         : TEXCOORD4;
                float4 detail     : TEXCOORD0;      // xyz: posizione nell'oggetto, w: la fase della copia (0..1)
                half surf         : TEXCOORD7;      // la materia (UV0.y dei vertici)
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

            float H21(float2 p)
            {
                float3 q = frac(float3(p.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float VNoise2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H21(i), H21(i + float2(1, 0)), f.x), lerp(H21(i + float2(0, 1)), H21(i + float2(1, 1)), f.x), f.y);
            }

            // l'ombra delle nuvole: la stessa funzione della sabbia (DesertSand.shader), con le stesse macchie, cosi' un sasso e la sabbia attorno scuriscono insieme (quattro ottave, come la' per avere
            // gli stessi numeri)
            half3 CloudShade(float2 p)
            {
                float2 q = (p + float2(0.82, 0.57) * (_Time.y * 5.5)) / 430.0 + 9.3;
                float nn = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { nn += VNoise2(q) * a; q = q * 2.03 + 17.1; a *= 0.5; }
                float centre = 0.69 - _DSCloudShadow.w * 0.62;
                float dens = smoothstep(centre - 0.07, centre + 0.07, nn);
                return 1.0 - (_DSCloudShadow.z * dens) * half3(1.0, 0.88, 0.62);
            }

            // la nebbia sta in basso (la stessa funzione di DesertSand.shader): la densita' cala con la quota e l'esponente della nebbia di URP si moltiplica per la media di e^(-k*y) lungo il raggio
            float HeightFog(float3 P)
            {
                const float k = 0.015;
                float hc = max(_WorldSpaceCameraPos.y + 2.4, 0.0), hp = max(P.y + 2.4, 0.0);
                float a = exp(-k * hc), b = exp(-k * hp);
                float dh = k * (hp - hc);
                return max(abs(dh) < 0.002 ? a : (a - b) / dh, 0.45);
            }

            // rumore di valore 3D con le derivate analitiche: xyz = gradiente, w = valore (0..1)
            float4 NoiseD(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float3 du = 30.0 * f * f * (f * (f - 2.0) + 1.0);
                float a = Hash3(i), b = Hash3(i + float3(1, 0, 0)), c = Hash3(i + float3(0, 1, 0)), d = Hash3(i + float3(1, 1, 0));
                float e = Hash3(i + float3(0, 0, 1)), g = Hash3(i + float3(1, 0, 1)), h = Hash3(i + float3(0, 1, 1)), k = Hash3(i + float3(1, 1, 1));
                float k1 = b - a, k2 = c - a, k3 = e - a, k4 = a - b - c + d, k5 = a - c - e + h, k6 = a - b - e + g, k7 = -a + b + c - d + e - g - h + k;
                float v = a + k1 * u.x + k2 * u.y + k3 * u.z + k4 * u.x * u.y + k5 * u.y * u.z + k6 * u.z * u.x + k7 * u.x * u.y * u.z;
                float3 dv = du * float3(k1 + k4 * u.y + k6 * u.z + k7 * u.y * u.z, k2 + k5 * u.z + k4 * u.x + k7 * u.z * u.x, k3 + k6 * u.x + k5 * u.y + k7 * u.x * u.y);
                return float4(dv, v);
            }

            // tre ottave (xyz = gradiente rispetto a p, w = valore, in 0..0.875)
            float4 FbmD(float3 p)
            {
                float4 s = 0;
                float a = 0.5, fq = 1.0;
                for (int o = 0; o < 3; o++)
                {
                    float4 n = NoiseD(p * fq + o * 17.3);
                    s += float4(n.xyz * fq, n.w) * a;
                    fq *= 2.03; a *= 0.5;
                }
                return s;
            }

            Varyings PropVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                // ogni copia ha la sua fase e la sua luminosita' (dalla posizione dell'oggetto; fissa per le cose che si muovono)
                float2 origin = float2(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m23);
                float h = lerp(H21(origin * 0.37), 0.5, step(0.5, _Fixed));
                // luce propria: una fiamma vibra, si accorcia fino a sparire di giorno; un bagliore resta fisso
                float emitK = input.uv.x;
                float flame = step(0.5, emitK);
                float flick = sin(_Time.y * 13.0 + h * 40.0 + input.positionOS.y * 7.0);
                float3 posOS = input.positionOS.xyz;
                posOS.y *= lerp(1.0, _DSNight * (0.92 + 0.10 * flick), flame);
                posOS.xz *= 1.0 + flame * 0.10 * sin(_Time.y * 9.0 + posOS.y * 11.0 + h * 30.0);
                float3 posWS = TransformObjectToWorld(posOS);
                float sway = input.color.a;
                float wind = max(_DSWind, 0.22);
                float w = sin(_Time.y * 1.6 + h * 6.283 + posWS.x * 0.21) * 0.6 + sin(_Time.y * 0.83 + h * 11.0 + posWS.z * 0.17) * 0.4;
                posWS.xz += float2(0.82, 0.57) * (w * sway * sway * 0.11 * wind);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                VertexPositionInputs vertexInput = (VertexPositionInputs)0;
                vertexInput.positionWS = posWS;
                vertexInput.positionVS = TransformWorldToView(posWS);
                vertexInput.positionCS = TransformWorldToHClip(posWS);
                float4 ndc = vertexInput.positionCS * 0.5f;
                vertexInput.positionNDC.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
                vertexInput.positionNDC.zw = vertexInput.positionCS.zw;
                half3 vertexLight = VertexLighting(posWS, normalWS);
                half fogFactor = 0;
            #if !defined(_FOG_FRAGMENT)
                fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif
                output.normalWS = normalWS;
                output.albedo = input.color.rgb * (0.80 + 0.40 * h);
                output.detail = float4(input.positionOS.xyz, h);
                output.surf = input.uv.y;
                output.emit = emitK * lerp(1.0, 2.2 + 0.9 * flick, flame) * lerp(1.0, _DSNight, flame);
                OUTPUT_SH4(posWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(posWS), output.vertexSH, output.probeOcclusion);
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
            #else
                output.fogFactor = fogFactor;
            #endif
                output.positionWS = posWS;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(vertexInput);
            #endif
                output.positionCS = vertexInput.positionCS;
                return output;
            }

            void PropFragment(Varyings input
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                float3 P = input.positionWS;
                float3 N = normalize(input.normalWS);
                float dist = distance(P, _WorldSpaceCameraPos);

                // la grana: tutta dal rumore nello spazio dell'oggetto
                float3 q = input.detail.xyz;
                float hh = input.detail.w;
                float s = input.surf;
                float stoneW = 1.0 - step(0.5, s);
                float orgW = step(0.5, s) * (1.0 - step(1.5, s));
                float3 off = float3(hh * 31.7, hh * 17.3, hh * 53.1);
                float fadeNear = saturate(1.0 - dist / 45.0);
                float4 big = FbmD(q * 2.6 + off);                          // chiazze larghe
                float4 fine = FbmD(q * 9.5 + off + 7.7);                   // grana
                float bigV = big.w / 0.875, fineV = fine.w / 0.875;
                float3 base = input.albedo;
                const float3 sandCol = float3(0.58, 0.43, 0.26);

                // pietra: chiazze, strati orizzontali un po' ondulati, polvere di sabbia sulle facce rivolte in su e in basso dove tocca terra, grana che muove la normale
                float strata = sin((q.y + (bigV - 0.5) * 0.45) * 24.0);
                float3 stoneAlb = base * lerp(0.70, 1.24, bigV) * lerp(0.86, 1.14, lerp(0.5, fineV, fadeNear));
                stoneAlb *= 1.0 - 0.11 * smoothstep(0.5, 1.0, strata) * fadeNear;
                // la polvere copre al piu' un terzo (il resto e' il colore della pietra: con un coprire del 60% tutti i sassi, rossi, scuri o chiari, venivano color crema); la pietra poi e' un poco
                // piu' scura di quel che dicono i vertici (la luce radente la schiarisce molto sulle facce rivolte al sole)
                float dust = smoothstep(0.50, 0.92, saturate(N.y) + (fineV - 0.5) * 0.5 + (bigV - 0.5) * 0.3) * 0.34;
                dust = max(dust, (1.0 - smoothstep(0.08, 0.42, q.y + (bigV - 0.5) * 0.2)) * 0.30);
                stoneAlb = lerp(stoneAlb, sandCol * (0.80 + 0.40 * bigV), dust) * 0.82;
                float3 gS = big.xyz * 0.5 + fine.xyz * 0.16 * fadeNear;
                float3 NS = normalize(N - 0.30 * (gS - dot(gS, N) * N));

                // organico (legno, piante, tela): venature lungo l'altezza
                float4 streak = FbmD(float3(q.x * 9.0, q.y * 1.5, q.z * 9.0) + off);
                float3 orgAlb = base * lerp(0.66, 1.28, streak.w / 0.875) * lerp(0.90, 1.10, fineV);
                float3 gO = streak.xyz * float3(1.0, 0.15, 1.0) * 0.10;
                float3 NO = normalize(N - 0.30 * (gO - dot(gO, N) * N));

                float3 plainAlb = base * lerp(0.90, 1.10, fineV);
                float3 albedo = lerp(lerp(plainAlb, orgAlb, orgW), stoneAlb, stoneW);
                float3 Nd = normalize(lerp(lerp(N, NO, orgW), NS, stoneW));
                // l'ombra di contatto: scuro dove la cosa tocca terra (la sabbia vicina la nasconde alla luce del cielo)
                float contact = lerp(0.55, 1.0, smoothstep(0.05, 0.40, q.y + (bigV - 0.5) * 0.25));
                albedo *= lerp(1.0, contact, saturate(stoneW + orgW));

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = 1;
                surfaceData.metallic = 0;
                surfaceData.specular = 0;
                surfaceData.smoothness = _Smooth * (1.0 - 0.6 * dust * stoneW);
                surfaceData.occlusion = lerp(1.0, contact, saturate(stoneW + orgW));
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = input.albedo * input.emit;

                InputData inputData = (InputData)0;
                inputData.positionWS = P;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = Nd;
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
                color.rgb *= CloudShade(P.xz);
                color.rgb = MixFog(color.rgb, inputData.fogCoord * HeightFog(P));
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
