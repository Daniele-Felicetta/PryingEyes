Shader "PryingEyes/Reticle"
{
    // Reticolo collimato (punto rosso, olografico, anello): e' disegnato "all'infinito", cioe' in funzione della
    // direzione di sguardo rispetto all'asse dell'ottica e non della posizione dell'occhio. Il disco sta sulla finestra
    // (assi dei modelli FBX d'arma nel riferimento locale: avanti = +Y, destra = +X, alto = -Z): se l'occhio non e' sull'asse il punto scivola e sparisce dal bordo, come
    // nei mirini veri; da davanti non si vede. Additivo, con aloni per il bloom.
    Properties
    {
        _Color ("Colore", Color) = (1, 0.04, 0.02, 1)
        _Intensity ("Intensita", Float) = 6
        _Pattern ("Reticolo (0 punto, 1 anello+punto, 2 anello)", Float) = 0
        _Dot ("Diametro del punto (gradi)", Float) = 0.16
        _Ring ("Raggio dell'anello (gradi)", Float) = 0.54
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+15" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Reticle"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Pattern;
                float _Dot;
                float _Ring;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; float3 fwd : TEXCOORD1; float3 up : TEXCOORD2; float3 right : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.fwd = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                o.up = normalize(TransformObjectToWorldDir(float3(0, 0, -1)));
                o.right = normalize(TransformObjectToWorldDir(float3(1, 0, 0)));
                return o;
            }

            float Disc(float d, float r, float aa) { return 1.0 - smoothstep(r - aa, r + aa, d); }

            half4 frag(Varyings i) : SV_Target
            {
                float3 v = i.posWS - GetCameraPositionWS();                // dall'occhio alla finestra
                float z = dot(v, i.fwd);
                if (z <= 1e-4) discard;                                     // da davanti il punto non si vede
                float2 a = degrees(float2(atan2(dot(v, i.right), z), atan2(dot(v, i.up), z)));   // angoli (gradi) rispetto all'asse
                float d = length(a);
                float aa = max(fwidth(d), 1e-4);
                float dotR = _Dot * 0.5;
                float m = Disc(d, dotR, aa);
                float halo = exp(-d * d / (dotR * dotR * 10.0 + 1e-5)) * 0.35;
                if (_Pattern > 0.5)
                {
                    float w = max(0.022, aa * 1.1);                         // spessore dell'anello
                    float ring = 1.0 - smoothstep(w, w + aa * 1.6, abs(d - _Ring));
                    m = max(m, ring);
                    if (_Pattern < 1.5)
                    {
                        // olografico: quattro trattini sull'anello (alto, sinistra, destra) piu' il punto
                        float horiz = step(_Ring, abs(a.x)) * step(abs(a.x), _Ring * 1.28) * (1.0 - smoothstep(w, w + aa, abs(a.y)));
                        float top = step(_Ring, a.y) * step(a.y, _Ring * 1.28) * (1.0 - smoothstep(w, w + aa, abs(a.x)));
                        m = max(m, max(horiz, top));
                    }
                    else
                    {
                        // anello pieno col punto piccolo al centro
                        m = max(m, Disc(d, 0.05, aa));
                    }
                    halo *= 0.4;
                }
                float alpha = saturate(m + halo);
                return half4(_Color.rgb * _Intensity, alpha * _Color.a);
            }
            ENDHLSL
        }
    }
}
