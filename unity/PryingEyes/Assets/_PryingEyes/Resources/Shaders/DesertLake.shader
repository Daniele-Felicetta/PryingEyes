Shader "PryingEyes/DesertLake"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-60" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "LakeLighting.hlsl"

            float4 _DSHorizon, _DSZenith, _DSFog, _DSSunDir, _DSSunColor, _DSMoonDir, _DSParams, _DSCloud;
            float _DSNight, _DSWind, _DesertLakeRefraction;
            float4 _Swimmers[4];
            int _SwimmerCount;
            float4 _Impacts[12];
            int _ImpactCount;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float radius : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.radius = v.uv.x;
                o.eyeDepth = -TransformWorldToView(o.positionWS).z;
                return o;
            }

            half3 ReflectShore(float3 surface, float3 ray, half3 sky, float distanceToEye)
            {
                if (ray.y < 0.016 || distanceToEye > 45.0) return sky;
                float previousStep = 0.22;
                [loop] for (int tap = 0; tap < 8; tap++)
                {
                    float stride = 0.36 + 0.30 * tap * tap;
                    float3 rayPosition = surface + ray * stride;
                    float4 clip = TransformWorldToHClip(rayPosition);
                    if (clip.w < 0.04) break;
                    float4 screen = ComputeScreenPos(clip);
                    float2 uv = screen.xy / screen.w;
                    if (any(uv < 0.0) || any(uv > 1.0)) break;
                    float rawDepth = SampleSceneDepth(uv);
                    float behind = clip.w - LinearEyeDepth(rawDepth, _ZBufferParams);
                    if (behind > 0.0)
                    {
                        #if !UNITY_REVERSED_Z
                        rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                        #endif
                        float3 hit = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                        float margin = min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y));
                        float confidence = smoothstep(0.015, 0.095, margin) *
                            smoothstep(0.02, 0.22, hit.y - surface.y) *
                            (1.0 - smoothstep(0.1, stride - previousStep + 0.5, behind)) *
                            (1.0 - smoothstep(29.0, 45.0, distanceToEye));
                        float2 colorUv = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(uv), _CameraOpaqueTexture_TexelSize.xy);
                        half3 reflectionColor = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, colorUv, 0).rgb;
                        return lerp(sky, reflectionColor * 0.78, confidence * 0.9);
                    }
                    previousStep = stride;
                }
                return sky;
            }

            half3 ReflectedSky(float3 reflected)
            {
                float height = saturate(reflected.y);
                half3 sky = lerp(_DSHorizon.rgb * half3(0.41, 0.56, 0.67), _DSZenith.rgb * 0.84, 1.0 - exp(-height * 2.2));
                float2 drift = float2(_DSParams.y * 0.004, _DSParams.y * 0.0016);
                float2 p = reflected.xz / (height + 0.19) + drift;
                float cloud = LakeNoise(p * 0.41) * 0.65 + LakeNoise(p * 1.63 + 3.2) * 0.35;
                float cloudCut = 0.71 - _DSCloud.a * 0.63;
                float cover = smoothstep(cloudCut - 0.09, cloudCut + 0.14, cloud) * smoothstep(0.0, 0.15, height);
                half3 cloudColor = lerp(_DSZenith.rgb, _DSCloud.rgb * 0.83, 0.78 + 0.22 * cloud);
                return lerp(sky, cloudColor, cover * 0.84);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.positionWS.xz;
                float t = _Time.y;
                float3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float2 warp = float2(sin(p.x * 0.73 + p.y * 0.45 - t * 0.37), cos(p.x * 0.38 - p.y * 0.69 + t * 0.43));
                warp += float2(sin(p.y * 0.32 - t * 0.71), cos(p.x * 0.41 + t * 0.61)) * 0.69;
                float detail = 1.0 - saturate(max(length(ddx(p)), length(ddy(p))) * 3.1);
                float2 slope = float2(0.58, 0.83) * cos(dot(p + warp * 0.86, float2(1.72, 2.44)) - t * 1.6) * 0.65;
                slope += float2(0.91, -0.41) * cos(dot(p + warp * 0.31, float2(4.1, -1.9)) + t * 2.4) * 0.58;
                slope += float2(-0.43, 0.90) * cos(dot(p, float2(-4.7, 9.8)) - t * 3.2) * (0.48 * detail);
                slope += float2(0.96, 0.29) * cos(dot(p + warp * 0.09, float2(17.0, 5.1)) + t * 5.6) * (0.26 * detail * detail);
                slope += float2(-0.48, 0.88) * cos(dot(p + warp * 0.05, float2(-13.6, 24.8)) - t * 6.1) * (0.20 * detail * detail);
                float wake = 0.0;
                for (int k = 0; k < _SwimmerCount; k++)
                {
                    float4 swimmer = _Swimmers[k];
                    float strength = length(swimmer.zw);
                    float2 direction = swimmer.zw / max(0.01, strength);
                    float2 delta = p - swimmer.xy;
                    float behind = -dot(delta, direction);
                    float lateral = abs(dot(delta, float2(direction.y, -direction.x)));
                    float v = lateral - 0.28 - 0.39 * behind;
                    float trail = smoothstep(-0.1, 0.48, behind) * (1.0 - smoothstep(0.6, 3.7, behind)) * exp(-v * v * 14.0);
                    float radius = length(delta);
                    float ripple = sin(radius * 18.0 - t * 8.0);
                    float ring = smoothstep(0.16, 0.45, radius) * (1.0 - smoothstep(0.68, 1.8, radius));
                    slope += delta / max(0.1, radius) * ripple * ring * 1.2;
                    wake += trail * strength * (0.19 + 0.58 * pow(saturate(sin(v * 23.0 + t * 7.0)), 3.0));
                    wake += ring * pow(saturate(ripple), 5.0) * (0.18 + 0.22 * strength);
                }
                for (int rippleIndex = 0; rippleIndex < _ImpactCount; rippleIndex++)
                {
                    float4 impact = _Impacts[rippleIndex];
                    float age = t - impact.z;
                    if (age > 1.65 || age < 0.0) continue;
                    float2 delta = p - impact.xy;
                    float r = length(delta);
                    float front = r - (0.035 + age * (1.26 + impact.w * 0.42));
                    float ripple = sin(front * (24.0 - age * 7.0));
                    float envelope = exp(-abs(front + 0.075) * (8.0 - age * 2.4)) * (1.0 - smoothstep(0.18, 1.65, age)) * impact.w;
                    slope += delta / max(0.08, r) * ripple * envelope * 0.96;
                    wake += envelope * (pow(saturate(ripple), 3.0) * 0.47 + 0.12);
                }
                float3 normal = normalize(float3(slope.x * (0.046 + 0.047 * _DSWind), 1.0, slope.y * (0.046 + 0.047 * _DSWind)));
                float3 macroNormal = normalize(float3(normal.x * 0.48, 1.0, normal.z * 0.48));
                float3 reflected = reflect(-view, macroNormal);
                float3 spark = reflect(-view, normal);
                half3 sky = ReflectedSky(reflected);
                float fresnel = 0.0204 + 0.89 * pow(1.0 - saturate(abs(dot(view, macroNormal))), 5.0);
                float metres = max(0.0, 3.5 * (1.0 - i.radius * i.radius) + 4.7 * (1.0 - smoothstep(0.13, 0.80, i.radius)));
                float distanceToEye = distance(i.positionWS, _WorldSpaceCameraPos);
                if (_WorldSpaceCameraPos.y < i.positionWS.y - 0.024)
                {
                    // From below, the surface reflects around the edge of the clear window above.
                    float snellWindow = smoothstep(0.62, 0.76, abs(dot(view, normal)));
                    half3 mirror = half3(0.034, 0.14, 0.117);
                    if (_DesertLakeRefraction > 0.5 && snellWindow < 0.98)
                    {
                        float4 clip = TransformWorldToHClip(i.positionWS + spark * min(0.55 + metres * 0.63, 5.0));
                        float4 screen = ComputeScreenPos(clip);
                        float2 uv = screen.xy / max(0.001, screen.w);
                        if (clip.w > 0.04 && all(uv > 0.01) && all(uv < 0.99))
                        {
                            float surfaceDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams) - i.eyeDepth;
                            float reflectedBed = smoothstep(-1.5, 0.32, surfaceDepth) * (1.0 - smoothstep(4.0, 12.0, surfaceDepth));
                            mirror = lerp(mirror, SampleSceneColor(uv) * half3(0.30, 0.52, 0.48), reflectedBed * 0.64);
                        }
                    }
                    half3 overhead = lerp(mirror, sky * 0.66, snellWindow);
                    float flecks = pow(saturate(dot(spark, -_DSSunDir.xyz)), 26.0) * _DSSunDir.w;
                    overhead += flecks * half3(0.26, 0.32, 0.26);
                    return half4(overhead, lerp(0.87, 0.10, snellWindow));
                }
                float pathLength = metres * lerp(1.5, 0.75, saturate(abs(view.y)));
                half3 water = lerp(half3(0.063, 0.262, 0.198), half3(0.017, 0.093, 0.111), smoothstep(0.0, 4.7, metres));
                water *= lerp(1.0, 0.16, _DSNight);
                half3 transmitted = water;
                float shore = 1.0 - smoothstep(0.0, 0.32, metres);
                if (_DesertLakeRefraction > 0.5)
                {
                    float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                    float depth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams) - i.eyeDepth;
                    pathLength = clamp(depth * distanceToEye / max(0.1, i.eyeDepth), 0.0, 9.5);
                    float2 normalVS = TransformWorldToViewDir(normal - float3(0.0, 1.0, 0.0), false).xy;
                    float2 bend = normalVS * (0.076 * saturate(pathLength) / (0.85 + sqrt(distanceToEye)));
                    float2 refractedUV = saturate(uv + bend);
                    float refractedDepth = LinearEyeDepth(SampleSceneDepth(refractedUV), _ZBufferParams) - i.eyeDepth;
                    half3 background = SampleSceneColor(refractedDepth < 0.045 ? uv : refractedUV);
                    half3 transmission = exp(-half3(0.74, 0.41, 0.30) * pathLength);
                    transmitted = background * transmission + water * (1.0 - transmission);
                    shore *= 0.32 + 0.68 * (1.0 - smoothstep(0.03, 0.65, pathLength));
                    if (fresnel > 0.115) sky = ReflectShore(i.positionWS, reflected, sky, distanceToEye);
                }
                half3 color = lerp(transmitted, sky, fresnel);
                float glint = pow(saturate(dot(spark, _DSSunDir.xyz)), 220.0) * _DSSunDir.w;
                color += glint * _DSSunColor.rgb * (2.0 * _DSParams.w);
                color += pow(saturate(dot(spark, _DSMoonDir.xyz)), 170.0) * _DSMoonDir.w * half3(0.09, 0.15, 0.23);

                float shoreWave = 0.56 + 0.25 * sin(dot(p + warp * 0.44, float2(2.2, 1.4)) - t * 1.3)
                    + 0.19 * sin(dot(p + warp * 0.38, float2(-4.2, 5.4)) + t * 2.1);
                float lapping = 1.0 - smoothstep(0.025, 0.19, abs(metres - 0.10 - shoreWave * 0.18));
                float brokenEdge = lapping * smoothstep(0.43, 0.83, shoreWave + slope.x * 0.078);
                half3 whiteWater = lerp(half3(0.64, 0.80, 0.77), half3(0.032, 0.080, 0.10), _DSNight);
                color = lerp(color, whiteWater, saturate(wake * 0.76 + brokenEdge * (0.16 + shore * 0.33)));
                float edge = 1.0 - smoothstep(0.988, 1.038, i.radius);
                float fog = 1.0 - exp(-distanceToEye * 0.0022);
                float alpha = _DesertLakeRefraction > 0.5 ? 0.97 : saturate(0.21 + pathLength * 0.23 + fresnel + wake * 0.24);
                return half4(lerp(color, _DSFog.rgb, fog), edge * alpha);
            }
            ENDHLSL
        }
    }
}
