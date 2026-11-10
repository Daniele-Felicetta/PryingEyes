Shader "PryingEyes/ChurchHaze"
{
    Properties
    {
        _Tint ("Luce diffusa", Color) = (0.155, 0.177, 0.185, 1)
        _Density ("Densita alla base", Float) = 0.056
        _HeightFalloff ("Dissolvenza verticale", Float) = 0.48
        [HideInInspector] _WindowPosition ("Finestra: posizione e intensita", Vector) = (0, 0, 0, 0)
        [HideInInspector] _WindowDirection ("Finestra: direzione e apertura", Vector) = (0, 0, 1, 1)
        [HideInInspector] _WindowTint ("Finestra: tinta", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20" }
        Pass
        {
            Name "ChurchHaze"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Density;
                float _HeightFalloff;
                float4 _WindowPosition;
                float4 _WindowDirection;
                half4 _WindowTint;
            CBUFFER_END

            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings Vert(float4 positionOS : POSITION)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                if (unity_OrthoParams.w > 0.5) return 0;
                float3 origin = GetCameraPositionWS();
                float3 ray = i.positionWS - origin;
                float3 localOrigin = TransformWorldToObject(origin);
                float3 localRay = TransformWorldToObjectDir(ray, false);
                float3 inverseRay = rcp(localRay + 1e-7);
                float3 a = (-0.5 - localOrigin) * inverseRay;
                float3 b = ( 0.5 - localOrigin) * inverseRay;
                float3 nearHit = min(a, b), farHit = max(a, b);
                float entry = max(0.0, max(nearHit.x, max(nearHit.y, nearHit.z)));
                float exit = min(farHit.x, min(farHit.y, farHit.z));
                float depth = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS)), _ZBufferParams);
                exit = min(exit, depth / max(dot(ray, -UNITY_MATRIX_V[2].xyz), 1e-5));
                if (exit <= entry) return 0;

                // Integra esattamente il profilo esponenziale lungo il tratto visibile.
                float y0 = origin.y + ray.y * entry;
                float y1 = origin.y + ray.y * exit;
                float k = abs(y1 - y0) * _HeightFalloff;
                float meanDensity = exp(-min(y0, y1) * _HeightFalloff) * (k < 1e-3 ? 1.0 : (1.0 - exp(-k)) / k);
                float opacity = 1.0 - exp(-_Density * meanDensity * (exit - entry) * length(ray));
                float windowScatter = 0.0;
                if (_WindowPosition.w > 0.0)
                {
                    // Pochi campioni nel tratto visibile, con bordo del fascio sfumato e caduta con la distanza.
                    [unroll] for (int iSample = 0; iSample < 4; ++iSample)
                    {
                        float3 offset = origin + ray * lerp(entry, exit, (iSample + 0.5) * 0.25) - _WindowPosition.xyz;
                        float d2 = dot(offset, offset);
                        float angleCos = dot(offset, _WindowDirection.xyz) * rsqrt(max(0.001, d2));
                        float cone = smoothstep(_WindowDirection.w, min(0.998, _WindowDirection.w + 0.17), angleCos);
                        windowScatter += cone / (4.0 + d2);
                    }
                }
                half3 light = _Tint.rgb + _WindowTint.rgb * (windowScatter * _WindowPosition.w * 0.25);
                return half4(light * opacity, opacity);
            }
            ENDHLSL
        }
    }
}
