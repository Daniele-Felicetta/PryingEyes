Shader "PryingEyes/DesertPrint"
{
    // Le orme sulla sabbia (DesertPrints.cs): ogni impronta e' un quadrato piatto posato sul terreno, con la forma di uno scarpone (tallone e suola) calcolata qui, non in una texture.
    // Non e' un colore sopra la sabbia: e' la luce che cambia. L'impronta e' una buca di due o tre centimetri; le sue pareti prendono il sole o restano all'ombra secondo il lato (stesso
    // sole del cielo, _DSSunDir), e il fondo e' un poco piu' scuro. Il risultato moltiplica quello che c'e' gia' sullo schermo (la sabbia con la sua luce e la sua nebbia), cosi' una buca
    // e' un fattore di luminosita' e non una macchia: al sole radente di sera ogni impronta ha un lato chiaro e uno scuro, a mezzogiorno si vede appena, di notte e' solo piu' scura.
    // Sta in Resources perche' Shader.Find lo trovi nella build.
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Print"
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

            // la profondita' dell'impronta in coordinate locali (x di traverso, y lungo il piede, tra -1 e 1): una suola larga e un tallone, tra i due la vita; 0 fuori, 1 dentro
            float Pit(float2 p)
            {
                float2 a = (p - float2(0.0, 0.30)) / float2(0.58, 0.50);
                float2 b = (p - float2(0.0, -0.58)) / float2(0.44, 0.30);
                float sole = 1.0 - smoothstep(0.50, 1.0, length(a));
                float heel = 1.0 - smoothstep(0.50, 1.0, length(b));
                return max(sole, heel);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv;
                float age = i.col.b;                                                  // 0 appena fatta, 1 cancellata dal vento
                float2 F = float2(i.col.r, i.col.g) * 2.0 - 1.0;                      // il verso del passo sul piano orizzontale
                float2 R = float2(F.y, -F.x);                                         // di traverso
                float pit = Pit(p);
                const float e = 0.05;
                float gx = (Pit(p + float2(e, 0.0)) - Pit(p - float2(e, 0.0))) / (2.0 * e) / 0.15;       // la pendenza in metri (semi-larghezza 0,15 m)
                float gy = (Pit(p + float2(0.0, e)) - Pit(p - float2(0.0, e))) / (2.0 * e) / 0.24;       // (semi-lunghezza 0,24 m)
                float2 g = (R * gx + F * gy) * 0.030;                                  // buca di tre centimetri: la normale gira verso il centro della buca
                float3 n = normalize(float3(g.x, 1.0, g.y));
                float3 L = normalize(_DSSunDir.xyz);
                float fl = max(L.y, 0.03);
                float nl = saturate(dot(n, L));
                float sunK = saturate(L.y * 5.0 + 0.2) * _DSSunDir.w;                  // di notte non c'e' il sole: resta il fondo piu' scuro e basta
                const float amb = 0.38;
                float lit = (nl + amb) / (fl + amb);
                float shade = lerp(1.0, lit, sunK) * (1.0 - 0.20 * pit);
                float fade = (1.0 - smoothstep(0.50, 1.0, age)) * (1.0 - smoothstep(28.0, 60.0, distance(i.positionWS, _WorldSpaceCameraPos)));
                return half4(half3(lerp(1.0, shade, fade).xxx), 1);
            }
            ENDHLSL
        }
    }
}
