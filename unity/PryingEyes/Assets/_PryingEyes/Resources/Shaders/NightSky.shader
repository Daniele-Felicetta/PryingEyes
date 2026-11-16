Shader "PryingEyes/NightSky"
{
    // Il cielo del Guardaroba: una cupola intera (da nadir a zenit) che segue la camera e sta sul piano lontano. E' sempre dietro a
    // tutto, non prende la nebbia (il resto del piano ne e' avvolto) e si vede solo dove la scena non lo copre: dalle terrazze,
    // dalle finestre sul vuoto. La texture e' fatta a runtime (GuardarobaKit.Sky). Sta in Resources perche' Shader.Find lo trovi nella build.
    Properties
    {
        [NoScaleOffset] _MainTex ("Cielo", 2D) = "black" {}
        _Tint ("Colore", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Sky"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // solo la direzione dell'oggetto, attaccata alla camera: la cupola e' infinitamente lontana
                float3 dirWS = TransformObjectToWorldDir(v.positionOS.xyz, false);
                float4 cs = TransformWorldToHClip(GetCameraPositionWS() + dirWS);
            #if UNITY_REVERSED_Z
                cs.z = cs.w * 0.00001;
            #else
                cs.z = cs.w * 0.99999;
            #endif
                o.positionCS = cs;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb * _Tint.rgb;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
