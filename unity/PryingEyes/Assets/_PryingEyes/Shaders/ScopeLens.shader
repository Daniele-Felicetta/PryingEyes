Shader "PryingEyes/ScopeLens"
{
    // Lente dell'ottica ingrandita: mostra l'immagine di una seconda camera (PiP) dentro un cerchio, con il reticolo
    // inciso sul vetro, l'ombra del cannocchiale che scivola se l'occhio esce dall'asse e un riflesso quando non si mira.
    // Disco sul piano XZ dell'oggetto (modelli FBX d'arma: avanti = +Y, destra = +X, alto = -Z), perno al centro della lente.
    Properties
    {
        _ScopeTex ("Immagine", 2D) = "black" {}
        _Active ("Attiva (0 vetro spento, 1 immagine)", Range(0, 1)) = 0
        _Radius ("Raggio della lente (m)", Float) = 0.017
        _Reticle ("Reticolo (3 chevron, 4 croce)", Float) = 4
        _RetColor ("Colore del reticolo", Color) = (0.015, 0.015, 0.015, 1)
        _Lit ("Reticolo acceso (0 nero, 1 rosso luminoso)", Range(0, 1)) = 0
        _Zoom ("Ingrandimento", Float) = 4
        [HideInInspector] _AimOrigin ("Origine del tiro", Vector) = (0,0,0,0)
        [HideInInspector] _AimForward ("Asse del tiro", Vector) = (0,0,0,0)
        [HideInInspector] _AimRight ("Destra del tiro", Vector) = (0,0,0,0)
        [HideInInspector] _AimUp ("Alto del tiro", Vector) = (0,0,0,0)
        [HideInInspector] _AimScale ("Scala angolare", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+5" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ScopeLens"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_ScopeTex); SAMPLER(sampler_ScopeTex);
            CBUFFER_START(UnityPerMaterial)
                float _Active;
                float _Radius;
                float _Reticle;
                half4 _RetColor;
                float _Lit;
                float _Zoom;
                float4 _AimOrigin, _AimForward, _AimRight, _AimUp;
                float _AimScale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 pos : TEXCOORD0; float3 eye : TEXCOORD1; float3 nrmWS : TEXCOORD2; float3 posWS : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.pos = v.positionOS.xyz;
                o.eye = TransformWorldToObject(GetCameraPositionWS());
                o.nrmWS = TransformObjectToWorldNormal(float3(0, -1, 0));
                o.posWS = TransformObjectToWorld(v.positionOS.xyz);
                return o;
            }

            float Seg(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 lensP = float2(i.pos.x, -i.pos.z) / _Radius;
                // ombra: se l'occhio e' fuori asse la lente si sposta e compare una mezzaluna scura
                float2 shift = clamp(float2(i.eye.x, -i.eye.z) / (_Radius * 1.6), -1.2, 1.2);
                float r = length(lensP - shift * 0.55);
                float shadow = smoothstep(1.02, 0.80, r);
                float edge = smoothstep(1.0, 0.94, length(lensP));
                float2 p = lensP;
                if (_AimScale > 0.0)
                {
                    // Coordina immagine e reticolo nello spazio del tiro, indipendentemente dalla posa della lente.
                    float3 v = i.posWS - _AimOrigin.xyz;
                    float z = max(dot(v, _AimForward.xyz), 1e-4);
                    p = float2(dot(v, _AimRight.xyz), dot(v, _AimUp.xyz)) / z * _AimScale;
                }
                float2 uv = p * 0.5 + 0.5;
                half3 img = SAMPLE_TEXTURE2D(_ScopeTex, sampler_ScopeTex, uv).rgb;
                // un'ottica raccoglie luce: nel buio dell'albergo l'immagine alzata di esposizione (curva esponenziale: i bianchi non bruciano)
                img = 1.0 - exp(-img * 2.7);
                // leggero viraggio freddo e vignettatura interna
                img *= half3(0.94, 1.0, 1.04) * (0.82 + 0.18 * smoothstep(1.0, 0.2, length(p)));
                half lum = dot(img, half3(0.299, 0.587, 0.114));

                // reticolo: coordinate in raggi della lente, spessore minimo in pixel
                float px = max(fwidth(p.x), 1e-4);
                half ret = 0;
                float ax = abs(p.x), ay = abs(p.y);
                if (_Reticle > 3.5)
                {
                    // croce duplex: sottile al centro, grossa verso i bordi, punti-mil sull'asse
                    float thick = lerp(px * 0.9, px * 5.0, smoothstep(0.40, 0.62, max(ax, ay)));
                    float gapC = 0.035;
                    float lineH = (ay < thick) && (ax > gapC) ? 1.0 : 0.0;
                    float lineV = (ax < thick) && (ay > gapC) ? 1.0 : 0.0;
                    float cut = step(max(ax, ay), 0.94);
                    ret = (lineH + lineV) * cut;
                    float dotsH = step(abs(frac(ax * 5.0 + 0.5) - 0.5), px * 3.4 * 1.0) * step(ax, 0.40) * step(0.1, ax) * step(ay, px * 2.4);
                    float dotsV = step(abs(frac(ay * 5.0 + 0.5) - 0.5), px * 3.4) * step(ay, 0.40) * step(0.1, ay) * step(ax, px * 2.4);
                    ret = max(ret, max(dotsH, dotsV));
                    ret = max(ret, 1.0 - smoothstep(px * 1.2, px * 2.2, length(p)));
                }
                else
                {
                    // chevron (prisma): apice al centro, braccia in basso, tacche di caduta sotto
                    float2 q = float2(ax, p.y);
                    float d = Seg(q, float2(0, 0.0), float2(0.26, -0.30));
                    d = min(d, Seg(q, float2(0, -0.30), float2(0.0, -0.30)));
                    ret = 1.0 - smoothstep(px * 1.1, px * 2.2, d);
                    for (int k = 1; k <= 3; k++)
                    {
                        float y = -0.30 - 0.22 * k;
                        float w = 0.10 + 0.03 * k;
                        ret = max(ret, (1.0 - smoothstep(px * 1.0, px * 2.0, abs(p.y - y))) * step(ax, w));
                    }
                    ret = max(ret, 1.0 - smoothstep(px * 1.2, px * 2.4, length(p)));
                    ret *= step(length(p), 0.96);
                }
                half3 retCol = lerp(_RetColor.rgb, half3(1.0, 0.10, 0.05) * 3.0, _Lit);
                // reticolo nero su immagine scura sparisce: dove la scena e' buia si accende di rosso fioco (come un reticolo illuminato)
                half dark = 1.0 - smoothstep(0.05, 0.26, lum);
                retCol = lerp(retCol, half3(0.85, 0.07, 0.03), dark * (1.0 - _Lit));
                img = lerp(img, retCol, saturate(ret) * (_Lit > 0.5 ? 1.0 : 0.92));

                half3 final = img * shadow;
                // spento: vetro scuro con un riflesso dell'ambiente
                float3 V = normalize(GetCameraPositionWS() - i.posWS);
                float3 N = normalize(i.nrmWS);
                half3 refl = GlossyEnvironmentReflection(reflect(-V, N), 0.03, 1.0) * 0.55;
                float fres = pow(1.0 - saturate(dot(N, V)), 3.0);
                half3 off = half3(0.006, 0.012, 0.016) + refl * (0.25 + 0.75 * fres);
                final = lerp(off, final, _Active);
                clip(edge - 0.001);
                return half4(final, 1);
            }
            ENDHLSL
        }
    }
}
