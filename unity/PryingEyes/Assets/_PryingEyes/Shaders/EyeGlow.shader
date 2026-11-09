Shader "PryingEyes/EyeGlow"
{
    // Occhio che brilla nel buio: mandorla luminosa con pupilla a fessura, si schiaccia con _Open (palpebre).
    // Additivo e senza nebbia: da lontano restano due punti accesi nel nero.
    Properties
    {
        _Color ("Colore", Color) = (2.4, 1.7, 0.55, 1)
        _Open ("Apertura", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+30" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "EyeGlow"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Open;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float open = max(_Open, 0.001);
                float h = 0.6 * open;                                 // altezza della mandorla rispetto alla larghezza
                float e = p.x * p.x + (p.y * p.y) / (h * h);
                float core = saturate(1.0 - e);
                float pupil = saturate(1.0 - abs(p.x) / 0.12) * step(e, 1.0);
                float glow = core * core * (1.0 - 0.85 * pupil);
                float halo = exp(-e * 2.2) * 0.25;
                return half4(_Color.rgb * (glow + halo) * open, 0);
            }
            ENDHLSL
        }
    }
}
