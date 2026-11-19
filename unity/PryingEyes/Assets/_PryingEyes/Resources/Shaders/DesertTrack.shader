Shader "PryingEyes/DesertTrack"
{
    // Le tracce delle gomme sulla sabbia (DesertTracks.cs): un nastro di quadrati piatti posati sul terreno per ruota, e qui il solco che lasciano. Come le orme (DesertPrint.shader) non e' un colore
    // sopra la sabbia ma la luce che cambia: il solco e' una buca di due centimetri con il fondo piatto, le pareti dolci e due bordi di sabbia spinta di lato; dentro il solco i tasselli (tacche oblique
    // a V, col vertice nel verso di marcia) sono piu' fondi. Le pareti prendono il sole o restano all'ombra secondo il lato (stesso sole del cielo, _DSSunDir) e il fondo e' un poco piu' scuro;
    // il risultato moltiplica quello che c'e' gia' sullo schermo (la sabbia con la sua luce e la sua nebbia), cosi' il solco e' un fattore di luminosita' e non una macchia: al sole radente di sera
    // ogni tacca ha un lato chiaro e uno scuro, a mezzogiorno si vede appena, di notte e' solo piu' scuro. Il vento ammorbidisce il solco man mano che invecchia (poi sparisce) e i tasselli
    // si perdono con la distanza, dove sarebbero solo rumore. Sta in Resources perche' Shader.Find lo trovi nella build.
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Track"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _DSSunDir;       // xyz: verso il sole, w: quanto si vede (0..1)

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                o.col = v.color;
                return o;
            }

            // la profondita' del solco in un punto del nastro (p.x di traverso da -1 a 1, p.y i metri lungo la traccia): 1 e' il fondo del solco, 0 la sabbia intatta, sotto zero i bordi rialzati;
            // 'lug' (0..1) e' quanto si vedono i tasselli
            float Depth(float2 p, float lug)
            {
                float ax = abs(p.x);
                float groove = 1.0 - smoothstep(0.28, 0.60, ax);                           // il solco: fondo piatto, pareti dolci
                float q = (ax - 0.74) / 0.15;
                float berm = exp(-q * q) * (1.0 - smoothstep(0.86, 1.0, ax));              // la sabbia spinta di lato
                float t = frac(p.y * 9.0 + ax * 1.1);
                float block = smoothstep(0.28, 0.42, t) * (1.0 - smoothstep(0.78, 0.92, t));   // un tassello ogni undici centimetri, a V
                return groove * (0.62 + 0.38 * block * lug) - 0.38 * berm;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv;
                float age = i.col.b;                                                  // 0 appena fatta, 1 cancellata dal vento
                float deep = i.col.a;                                                 // 0..1: una ruota che spinge o frena a fondo scava di piu'
                float2 F = float2(i.col.r, i.col.g) * 2.0 - 1.0;                      // il verso della traccia sul piano orizzontale
                float2 R = float2(F.y, -F.x);                                         // di traverso
                float dist = distance(i.positionWS, _WorldSpaceCameraPos);
                float lug = 1.0 - smoothstep(5.0, 18.0, dist);
                float d = Depth(p, lug);
                const float ex = 0.04, ey = 0.004;
                float gx = (Depth(p + float2(ex, 0.0), lug) - Depth(p - float2(ex, 0.0), lug)) / (2.0 * ex) / 0.15;       // la pendenza al metro (semi-larghezza del nastro 0,15 m)
                float gy = (Depth(p + float2(0.0, ey), lug) - Depth(p - float2(0.0, ey), lug)) / (2.0 * ey);
                float amp = 0.022 * lerp(0.55, 1.6, deep) * (1.0 - 0.7 * age);        // due centimetri di solco, che il vento riempie
                float2 g = (R * gx + F * gy) * amp;
                float3 n = normalize(float3(g.x, 1.0, g.y));
                float3 L = normalize(_DSSunDir.xyz);
                float fl = max(L.y, 0.03);
                float nl = saturate(dot(n, L));
                float sunK = saturate(L.y * 5.0 + 0.2) * _DSSunDir.w;                  // di notte non c'e' il sole: resta il fondo piu' scuro e basta
                const float amb = 0.38;
                float lit = (nl + amb) / (fl + amb);
                float shade = lerp(1.0, lit, sunK) * (1.0 - 0.26 * saturate(d) * lerp(0.6, 1.2, deep) * (1.0 - 0.6 * age));
                float fade = (1.0 - smoothstep(0.55, 1.0, age)) * (1.0 - smoothstep(35.0, 75.0, dist));
                return half4(half3(lerp(1.0, shade, fade).xxx), 1);
            }
            ENDHLSL
        }
    }
}
