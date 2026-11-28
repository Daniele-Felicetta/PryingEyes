Shader "PryingEyes/LakeLife"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _WaterLevel ("Water level", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LakeLighting.hlsl"

            float4 _DSSunDir, _DSSunColor;
            float _DSNight;
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _WaterLevel;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 data : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 world : TEXCOORD0;
                half3 normal : TEXCOORD1;
                half4 color : COLOR;
                float kind : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = v.positionOS;
                if (v.data.y > 1.5)
                {
                    float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                    float beat = _Time.y * 12.0 + origin.x * 1.3 + origin.z * 0.67 + p.z * 5.0;
                    p.x += sin(beat) * 0.15 * v.data.x * v.data.x;
                }
                else if (v.data.y > 0.5)
                {
                    float3 world = TransformObjectToWorld(p);
                    float weight = v.data.x * v.data.x;
                    p.x += sin(world.z * 0.58 + world.x * 0.19 + _Time.y * 1.25) * weight * 0.26;
                    p.z += sin(world.x * 0.47 - world.z * 0.28 + _Time.y * 0.88) * weight * 0.17;
                }
                o.world = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.world);
                o.normal = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.data.y > 0.5 ? v.color : half4(1, 1, 1, 1);
                o.kind = v.data.y;
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 n = normalize(i.normal) * IS_FRONT_VFACE(face, 1.0, -1.0);
                float depth = max(0.0, _WaterLevel - i.world.y);
                float time = _Time.y;
                float2 p = i.world.xz + _DSSunDir.xz * depth * 0.55;
                float caustic = LakeCaustics(p, time, depth);
                half3 color = i.color.rgb * _Tint.rgb;
                if (i.kind < 0.5)
                {
                    float2 rockUV = i.world.xz + i.world.y * float2(0.64, 1.27);
                    float crust = LakeNoise(rockUV * 3.5) * 0.67 + LakeNoise(rockUV * 11.0 + 5.7) * 0.33;
                    float grain = LakeNoise(rockUV * 57.0);
                    float strata = 0.88 + 0.12 * sin(i.world.y * 18.0 + crust * 4.5);
                    float algae = smoothstep(0.34, 0.69, crust + 0.14 * n.y + min(0.12, depth * 0.08));
                    color *= strata * (0.62 + 0.51 * crust + 0.23 * grain);
                    color = lerp(color, color * half3(0.46, 0.67, 0.36), algae * 0.79);
                    color *= lerp(1.0, 0.68, smoothstep(0.00, 0.5, _WaterLevel + 0.12 - i.world.y));
                }
                float ndl = saturate(dot(n, _DSSunDir.xyz));
                float light = lerp(0.87, 0.23, _DSNight) * (0.49 + ndl * 0.65 + caustic * _DSSunDir.w * 0.61);
                half3 view = GetWorldSpaceNormalizeViewDir(i.world);
                float sheen = pow(saturate(dot(n, normalize(view + _DSSunDir.xyz))), 19.0);
                color = color * light + sheen * _DSSunColor.rgb * lerp(0.028, 0.19, step(1.5, i.kind)) * _DSSunDir.w;
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // Depth prepass for the water's fog, refraction and reflections.
        Pass
        {
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 depthVert(float3 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS); }
            half depthFrag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }
    }
}
