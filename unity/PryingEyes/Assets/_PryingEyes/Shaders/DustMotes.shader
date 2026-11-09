Shader "PryingEyes/DustMotes"
{
    // Polvere in sospensione: puntini additivi che si vedono solo dove arriva la luna (leggono la sua shadow map),
    // cosi' brillano dentro le lame e spariscono nel buio.
    Properties
    {
        _Color ("Colore", Color) = (0.78, 0.85, 1, 1)
        _Intensity ("Intensita", Float) = 2.0
        _FadeStart ("Sparisce da (m)", Float) = 5
        _FadeEnd ("Sparita a (m)", Float) = 12
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "DustMotes"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv : TEXCOORD1; half4 color : COLOR; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 d = i.uv * 2.0 - 1.0;
                float a = saturate(1.0 - dot(d, d));
                a *= a;                                          // puntino morbido, niente texture
                // da lontano la polvere non si vede: senza questo, in fondo alla hall sembrava un cielo stellato
                float dist = distance(GetCameraPositionWS(), i.positionWS);
                a *= saturate((_FadeEnd - dist) / max(_FadeEnd - _FadeStart, 0.01));
                half sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.positionWS));
                Light ml = GetMainLight();
                half3 c = _Color.rgb * i.color.rgb * ml.color * (_Intensity * a * sh * i.color.a);
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
