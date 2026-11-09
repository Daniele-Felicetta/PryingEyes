Shader "PryingEyes/Grime"
{
    // Sporco e aloni d'acqua: quad appoggiati a muri, soffitto e pavimento che MOLTIPLICANO cio' che c'e' sotto.
    // Scuriscono la superficie gia' illuminata (come un'albedo piu' scura) e non si ripetono come una texture.
    Properties
    {
        _MainTex ("Forma (alfa)", 2D) = "white" {}
        _Color ("Tinta", Color) = (0.40, 0.34, 0.28, 1)
        _Strength ("Intensita", Range(0, 1)) = 0.7
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Grime"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Strength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * _Strength;
                return half4(lerp(half3(1, 1, 1), _Color.rgb, a), 1);
            }
            ENDHLSL
        }
    }
}
