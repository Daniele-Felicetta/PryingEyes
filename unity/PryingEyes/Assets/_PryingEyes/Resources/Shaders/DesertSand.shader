Shader "PryingEyes/DesertSand"
{
    // La sabbia del deserto: tutta procedurale, nessuna texture. Il colore cambia piano piano con la posizione (zone piu' chiare, piu' rosse e piu'
    // scure), sui pendii ripidi diventa roccia, e le dune hanno increspature del vento (ondine piccole, spostate dal rumore) che muovono
    // la normale: al sole radente di mattina e di sera ogni cresta ha la sua ombra. Da vicino si vede anche il grano. Il resto e' la luce di URP
    // (sole con le ombre, luci puntuali, ambiente, nebbia): UniversalFragmentPBR come il Lit.
    // Le passate delle ombre e della profondita' sono quelle del Lit (UsePass): la sabbia proietta ombre e finisce nella profondita' come tutto.
    Properties
    {
        _SandLight ("Sabbia chiara", Color) = (0.80, 0.64, 0.42, 1)
        _SandDark ("Sabbia scura", Color) = (0.62, 0.43, 0.26, 1)
        _SandRed ("Sabbia rossa", Color) = (0.70, 0.40, 0.22, 1)
        _Rock ("Roccia", Color) = (0.45, 0.33, 0.24, 1)
        _Wind ("Vento (xz)", Vector) = (0.82, 0.57, 0, 0)
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
            #pragma target 2.0
            #pragma vertex SandVertex
            #pragma fragment SandFragment

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
                half4 _SandLight;
                half4 _SandDark;
                half4 _SandRed;
                half4 _Rock;
                float4 _Wind;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
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

            Varyings SandVertex(Attributes input)
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

            void SandFragment(Varyings input
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                float3 P = input.positionWS;
                float3 N = normalize(input.normalWS);
                float dist = distance(P, _WorldSpaceCameraPos);
                float2 p = P.xz;

                // colore: chiazze larghe di sabbia chiara, scura e rossastra
                float broad = Fbm2(p * 0.0035 + 3.7);
                float mid = Fbm2(p * 0.021 + 11.3);
                float red = smoothstep(0.52, 0.78, Fbm2(p * 0.0021 - 8.9));
                half3 sand = lerp(_SandDark.rgb, _SandLight.rgb, smoothstep(0.30, 0.70, broad));
                sand = lerp(sand, _SandRed.rgb, red * 0.7);
                sand *= 0.88 + 0.24 * mid;

                // increspature del vento: onde sottili perpendicolari al vento, deformate dal rumore; sfumano con la distanza
                float2 wd = normalize(_Wind.xy);
                float warp = Fbm2(p * 0.07) * 9.0;
                float phase = dot(p, wd) * 7.2 + warp;
                float along = dot(p, float2(-wd.y, wd.x));
                float ripple = sin(phase) * 0.5 + 0.5;
                float rip = ripple * ripple * (1.0 - ripple) * 4.0;                       // dorsi stretti, valli larghe
                float fade = saturate(1.0 - dist / 70.0);
                float slopeFlat = smoothstep(0.62, 0.93, N.y);                           // sulle pendenze ripide non ci sono
                float amp = 0.20 * fade * slopeFlat;
                float dphase = cos(phase) * 7.2;
                float3 Np = N;
                Np.xz += wd * (dphase * 0.035 * amp * 4.0) * (1.0 - 0.5 * rip);
                Np.xz += float2(-wd.y, wd.x) * (VNoise(float2(along * 0.9, phase * 0.15)) - 0.5) * 0.10 * fade;
                // il grano: rumore fine che si perde con la distanza
                float grain = H21(floor(p * 90.0)) - 0.5;
                float grainFade = saturate(1.0 - dist / 14.0);
                sand *= 1.0 + grain * 0.22 * grainFade;
                sand *= 1.0 - 0.10 * rip * fade;                                          // le valli delle increspature sono un po' piu' scure
                Np = normalize(Np);

                // roccia sui pendii ripidi (mesa, fianchi delle dune alte)
                float rock = 1.0 - smoothstep(0.40, 0.62, N.y);
                float strata = 0.85 + 0.30 * VNoise(float2(P.y * 1.7, dot(p, wd) * 0.02));
                half3 albedo = lerp(sand, _Rock.rgb * strata, rock);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = 1;
                surfaceData.metallic = 0;
                surfaceData.specular = 0;
                surfaceData.smoothness = 0.06 + 0.10 * saturate(grain * 2.0 + 0.5) * grainFade;
                surfaceData.occlusion = 1;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = 0;

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
