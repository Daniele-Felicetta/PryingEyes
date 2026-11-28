Shader "PryingEyes/LakeSpray"
{
    Properties
    {
        _WaterLevel ("Water level", Float) = 0
        _BubbleMode ("Draw underwater bubbles", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _WaterLevel, _DSNight, _BubbleMode;

            struct Attributes { float3 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 heightAndRange : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(world);
                o.heightAndRange = float2(world.y, distance(world, _WorldSpaceCameraPos));
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv * 2.0 - 1.0) * float2(1.13, 1.0);
                float shape = 1.0 - smoothstep(0.18, 0.97, dot(p, p));
                float glint = 1.0 - smoothstep(0.04, 0.38, length(p - float2(-0.22, 0.24)));
                float above = smoothstep(_WaterLevel - 0.015, _WaterLevel + 0.095, i.heightAndRange.x);
                float nearFade = smoothstep(0.10, 0.55, i.heightAndRange.y);
                if (_BubbleMode > 0.5)
                {
                    float r = length(p);
                    float rim = smoothstep(0.58, 0.83, r) * (1.0 - smoothstep(0.87, 1.0, r));
                    shape = rim * 0.82 + shape * (0.07 + glint * 0.64);
                    above = 1.0 - smoothstep(_WaterLevel - 0.22, _WaterLevel - 0.007, i.heightAndRange.x);
                }
                return half4(i.color.rgb * (0.62 + glint * 0.62) * lerp(1.0, 0.34, _DSNight), shape * above * nearFade * i.color.a);
            }
            ENDHLSL
        }
    }
}
