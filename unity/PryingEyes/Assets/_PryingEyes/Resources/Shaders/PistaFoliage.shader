Shader "PryingEyes/PistaFoliage"
{
    // The leaves and fir fronds of the trees of a circuit: cards cut out by a texture (PistaFoliage), lit by URP's own light (sun and shadows, ambient, fog) with these differences from the Lit:
    //  - the normals of the mesh are the ones of the crown (pointing away from its middle), not of the flat card, so a tree is lit like a ball of leaves and not like a pile of paper;
    //  - the light that passes through a leaf: seen against the sun the leaves glow (the sun behind a thin leaf is the green that makes a wood in the evening);
    //  - the colour of the mesh (rgb) darkens the inside of the crown and varies from tree to tree, and its alpha says how much the card sways in the wind (the tips more than the middle);
    //  - cards are seen from both sides (Cull Off), and the cut is made in the shadow and depth passes too, so the shadow of a tree is a tree and not a set of squares.
    // There is no screen-space occlusion on the leaves (it would darken them with the occlusion of what lies behind the cut-out parts).
    Properties
    {
        [NoScaleOffset] _LeafMap ("Card (RGB colour, A coverage)", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Sway ("Wind sway (m)", Float) = 0.10
        _Trans ("Light through the leaf", Float) = 0.45
        _Smooth ("Smoothness", Float) = 0.10
        _Fill ("Fill in the shade (share of the colour)", Float) = 0.18
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }
        LOD 300

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_LeafMap);
            SAMPLER(sampler_LeafMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Cutoff;
                float _Sway;
                float _Trans;
                float _Smooth;
                float _Fill;
            CBUFFER_END

            // the wind: a slow sway and a faster flutter, out of step from place to place; 'weight' (0..1) is how free the point is to move
            float3 WindOffset(float3 positionWS, float weight)
            {
                float t = _Time.y;
                float ph = positionWS.x * 0.31 + positionWS.z * 0.23;
                float3 d = float3(sin(t * 1.7 + ph) + 0.5 * sin(t * 3.9 + ph * 2.3), 0.15 * sin(t * 2.3 + ph), cos(t * 1.3 + ph * 1.4) + 0.4 * sin(t * 4.3 + ph * 3.1));
                return d * (_Sway * weight);
            }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex FoliageVertex
            #pragma fragment FoliageFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _DSCloudShadow;      // z: how much the shadow of a cloud darkens the ground in the sun, w: the cover of the sky; written by DesertSky

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half4 color       : TEXCOORD3;
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

            Varyings FoliageVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                vertexInput.positionWS += WindOffset(vertexInput.positionWS, input.color.a);
                vertexInput.positionCS = TransformWorldToHClip(vertexInput.positionWS);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
                half fogFactor = 0;
            #if !defined(_FOG_FRAGMENT)
                fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif
                output.uv = input.uv;
                output.color = input.color;
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

            half3 CloudShade(float2 p, float dist)
            {
                float2 q = (p + float2(0.82, 0.57) * (_Time.y * 5.5)) / 430.0;
                float nn = Fbm2(q + 9.3);
                float centre = 0.69 - _DSCloudShadow.w * 0.62;
                float dens = smoothstep(centre - 0.07, centre + 0.07, nn);
                return 1.0 - (_DSCloudShadow.z * dens * saturate(1.3 - dist / 1100.0)) * half3(1.0, 0.88, 0.62);
            }

            half4 FoliageFragment(Varyings input) : SV_Target
            {
                half4 leaf = SAMPLE_TEXTURE2D(_LeafMap, sampler_LeafMap, input.uv);
                clip(leaf.a - _Cutoff);
                float3 P = input.positionWS;
                float3 N = normalize(input.normalWS);
                float dist = distance(P, _WorldSpaceCameraPos);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = leaf.rgb * _Tint.rgb * input.color.rgb;
                surfaceData.alpha = 1;
                surfaceData.metallic = 0;
                surfaceData.specular = 0;
                surfaceData.smoothness = _Smooth;
                surfaceData.occlusion = 1;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = surfaceData.albedo * _Fill;                  // the underside of a crown is never in the sun and the ambient is low: a little of its own colour, so shade is green and not black

                InputData inputData = (InputData)0;
                inputData.positionWS = P;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = N;
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
                // the sun through the leaf: strongest when the sun is behind the card seen from the eye, and only where the sun reaches (the shadow of other leaves puts it out)
                Light sun = GetMainLight(inputData.shadowCoord);
                float back = saturate(-dot(inputData.viewDirectionWS, sun.direction));
                color.rgb += surfaceData.albedo * sun.color * (sun.shadowAttenuation * sun.distanceAttenuation * pow(back, 3.0) * _Trans);
                color.rgb *= CloudShade(P.xz, dist);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;
            };

            struct ShadowVaryings
            {
                float2 uv         : TEXCOORD0;
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVertex(ShadowAttributes input)
            {
                ShadowVaryings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += WindOffset(positionWS, input.color.a);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(positionCS);
                output.uv = input.uv;
                return output;
            }

            half4 ShadowFragment(ShadowVaryings input) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_LeafMap, sampler_LeafMap, input.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;
            };

            struct DepthVaryings
            {
                float2 uv         : TEXCOORD0;
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVertex(DepthAttributes input)
            {
                DepthVaryings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += WindOffset(positionWS, input.color.a);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                return output;
            }

            half DepthFragment(DepthVaryings input) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_LeafMap, sampler_LeafMap, input.uv).a - _Cutoff);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
