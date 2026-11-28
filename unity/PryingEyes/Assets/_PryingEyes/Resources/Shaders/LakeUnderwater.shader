Shader "PryingEyes/LakeUnderwater"
{
    Properties
    {
        _LakeVolume ("Lake centre and surface", Vector) = (0, 0, 0, 0)
        _LakeRadii ("Inverse lake radii", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+430" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "LakeLighting.hlsl"

            float4 _LakeVolume, _LakeRadii, _DSSunDir, _DSSunColor, _DSParams;
            float _DSNight;

            struct Attributes { float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = float4(v.uv * 2.0 - 1.0, 0.4, 1.0);
                return o;
            }

            float SunShaft(float3 position, float time)
            {
                float depth = max(0.0, _LakeVolume.y - position.y);
                float2 uv = position.xz + depth * _DSSunDir.xz / max(0.32, _DSSunDir.y + 0.28);
                float phase = sin(uv.y * 0.77 + time * 0.56) * 1.22 + sin(uv.x * 0.24 - uv.y * 0.45) * 0.81;
                float fan = 0.50 + 0.28 * sin(dot(uv, float2(1.39, 0.82)) + phase) +
                    0.22 * sin(dot(uv, float2(-0.48, 2.09)) - phase * 0.6 - time * 0.67);
                return pow(saturate(fan), 5.0) * exp(-depth * 0.32);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 eye = _WorldSpaceCameraPos;
                float depth = _LakeVolume.y - eye.y;
                clip(depth - 0.007);
                float2 offset = (eye.xz - _LakeVolume.xz) * _LakeRadii.xy;
                float radius = LakeRadius(offset, _LakeVolume.w);
                clip(1.003 - radius);

                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float raw = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, raw);
                #endif
                float3 world = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 ray = normalize(world - eye);
                float travel = min(length(world - eye), 45.0);
                float exitTravel = ray.y > 0.005 ? depth / ray.y : 45.0;
                travel = min(travel, exitTravel);
                float onset = smoothstep(0.007, 0.13, depth);
                float turbidity = 0.047 + depth * 0.009;
                float opacity = (1.0 - exp(-travel * turbidity)) * onset;
                half3 haze = lerp(half3(0.038, 0.139, 0.129), half3(0.017, 0.066, 0.090), saturate(depth * 0.17));
                haze *= lerp(1.0, 0.20, _DSNight);

                float time = _Time.y;
                float scatterDistance = min(travel, 16.0);
                float shafts = SunShaft(eye + ray * (scatterDistance * 0.23), time) * 0.52
                    + SunShaft(eye + ray * (scatterDistance * 0.54), time) * 0.32
                    + SunShaft(eye + ray * (scatterDistance * 0.85), time) * 0.16;
                float viewLight = saturate(dot(ray, normalize(float3(_DSSunDir.x, _DSSunDir.y + 0.35, _DSSunDir.z))));
                float glow = shafts * (0.23 + viewLight * viewLight * 0.77) * _DSSunDir.w * _DSParams.w * onset *
                    (1.0 - exp(-scatterDistance * 0.28)) * 0.28;
                return half4(haze * opacity + glow * _DSSunColor.rgb * half3(0.33, 0.90, 0.79), opacity);
            }
            ENDHLSL
        }
    }
}
