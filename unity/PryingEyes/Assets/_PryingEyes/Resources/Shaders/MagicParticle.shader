Shader "PryingEyes/MagicParticle"
{
    // Le particelle delle magie e dei sigilli (MagicVfx): fuoco, fumo, scintille, brina, sangue, rune, fulmini. Una sola fusione
    // premoltiplicata (One, OneMinusSrcAlpha) fa sia l'additivo (_Additive = 1: si somma alla luce, il fuoco) sia la trasparenza
    // (_Additive = 0: copre, il fumo), e i valori oltre 1 (_Intensity) vanno al bloom. Flipbook con la sfumatura tra due fotogrammi
    // (flussi UV, UV2 e AnimBlend del ParticleSystem); erosione: l'alfa del vertice e' la soglia sotto cui la trama sparisce, cosi'
    // una lingua di fuoco si consuma dai bordi invece di sbiadire; particelle morbide contro la scena (la profondita' della camera) e
    // sfumate vicino all'occhio; la nebbia fatta a mano (_PEMagicFog, da MagicVfx), senza varianti da tenere nella build.
    // Sta in Resources perche' Shader.Find lo trovi nella build: le trame si fanno a runtime (VfxTextures).
    Properties
    {
        _MainTex ("Trama", 2D) = "white" {}
        _Intensity ("Intensita", Float) = 1
        _Additive ("Additivo (1) o trasparente (0)", Range(0, 1)) = 1
        _Erosion ("Erosione dall'alfa del vertice", Range(0, 1)) = 0
        _EdgeSoft ("Morbidezza del bordo eroso", Range(0.02, 1)) = 0.3
        _SoftDist ("Morbidezza contro la scena (m)", Float) = 0.12
        _Flipbook ("Sfuma i fotogrammi", Float) = 0
        _ScrollU ("Scorrimento lungo U (al secondo)", Float) = 0
        _CamFade ("Sparisce vicino all'occhio (da, a)", Vector) = (0.05, 0.14, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "MagicParticle"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _PEMagicFog;
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _CamFade;
                half _Intensity, _Additive, _Erosion, _EdgeSoft, _SoftDist, _Flipbook, _ScrollU;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;      // xy fotogramma, zw fotogramma seguente (ParticleSystem: UV, UV2)
                float4 uv1 : TEXCOORD1;     // x sfumatura tra i due (AnimBlend)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;
                float2 depthBlend : TEXCOORD1;  // x profondita' dall'occhio, y sfumatura tra i fotogrammi
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.color = v.color;
                float2 a = v.uv.xy * _MainTex_ST.xy + _MainTex_ST.zw;
                a.x += _Time.y * _ScrollU;
                o.uv = float4(a, v.uv.zw);
                o.depthBlend = float2(-TransformWorldToView(ws).z, _Flipbook > 0.5 ? v.uv1.x : 0.0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy);
                if (i.depthBlend.y > 0.001)
                    t = lerp(t, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.zw), (half)i.depthBlend.y);
                half a;
                if (_Erosion > 0.5)
                {
                    // la soglia sale con l'eta' (l'alfa del vertice scende): prima spariscono le parti rade, poi il cuore
                    half th = 1.0h - i.color.a;
                    a = t.a * smoothstep(th, th + _EdgeSoft, t.a);
                }
                else a = t.a * i.color.a;
                float eye = i.depthBlend.x;
                if (_SoftDist > 0.001)
                {
                    float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                    float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    a *= (half)saturate((scene - eye) / _SoftDist);
                }
                a *= (half)saturate((eye - _CamFade.x) / max(1e-4, _CamFade.y - _CamFade.x));
                half3 rgb = t.rgb * i.color.rgb * _Intensity * a;
                half cover = a * (1.0h - _Additive);
                if (_PEMagicFog > 0.5)
                {
                    // la nebbia di Unity fatta a mano (1 lineare, 2 esponenziale, 3 esponenziale quadrata; unity_FogParams come in
                    // URP): l'additivo si spegne nella nebbia, il trasparente prende il suo colore
                    float fi;
                    if (_PEMagicFog < 1.5) fi = saturate(eye * unity_FogParams.z + unity_FogParams.w);
                    else if (_PEMagicFog < 2.5) fi = saturate(exp2(-unity_FogParams.y * eye));
                    else { float f = unity_FogParams.x * eye; fi = saturate(exp2(-f * f)); }
                    rgb = rgb * (half)fi + unity_FogColor.rgb * cover * (half)(1.0 - fi);
                }
                return half4(rgb, cover);
            }
            ENDHLSL
        }
    }
}
