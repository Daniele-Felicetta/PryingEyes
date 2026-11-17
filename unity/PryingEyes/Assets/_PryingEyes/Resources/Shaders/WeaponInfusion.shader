Shader "PryingEyes/WeaponInfusion"
{
    // Il sigillo sull'arma in prima persona (WeaponAura): una seconda pelle tracciata sulle stesse mesh dell'arma, a mezzo millimetro
    // dalla superficie, che si scalda DAL BASSO. Per ogni punto si calcola quanto sta in alto nel profilo dell'arma in quel punto della
    // sua lunghezza (h: 0 sotto, 1 sopra; il profilo sotto/sopra a ogni z sta in _Profile, canali R e G) e il livello del calore a
    // quel z (canale B, lo aggiorna WeaponAura: sale dove la mano passa e poi dappertutto). Dove il livello supera h l'elemento e'
    // acceso; sul fronte (livello - h vicino a zero) c'e' una riga viva; piu' in basso e' piu' forte che in alto, e il bordo
    // del fronte e' irregolare (lingue) perche' il rumore lo sposta.
    // _Mode: 0 critico (crepe rosse), 1 sangue (colature), 2 fuoco (metallo arroventato), 3 acido (fanghiglia che ribolle),
    // 4 fulmine (vene di corrente), 5 gelo (brina che avanza), 6 maledetto (fuoco nero con crepe), 9 niente (vetri).
    // Un'uscita premoltiplicata (One, OneMinusSrcAlpha) fa sia l'additivo (alfa 0: luce che si somma) sia la copertura (alfa > 0:
    // brina, fango, sangue); i valori oltre 1 vanno al bloom del post-processing.
    // Sta in Resources perche' Shader.Find lo trovi nella build. Tutte le proprieta' stanno nel blocco UnityPerMaterial (SRP Batcher);
    // le righe della matrice mondo->arma sono vettori, aggiornate a ogni fotogramma da WeaponAura sul materiale del giocatore.
    Properties
    {
        _Profile ("Profilo: sotto, sopra e livello a ogni z", 2D) = "black" {}
        _Mode ("Elemento 0..6", Float) = 2
        _Strength ("Intensita", Float) = 1
        _Pulse ("Lampo del colpo", Float) = 0
        _Shell ("Scarto dalla superficie (m)", Float) = 0.0006
        _Bounds ("z minima, 1/lunghezza", Vector) = (0, 1.5, 0, 0)
        _W2L0 ("Mondo->arma, riga 0", Vector) = (1, 0, 0, 0)
        _W2L1 ("Mondo->arma, riga 1", Vector) = (0, 1, 0, 0)
        _W2L2 ("Mondo->arma, riga 2", Vector) = (0, 0, 1, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-1" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "WeaponInfusion"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Profile);
            SAMPLER(sampler_Profile);
            CBUFFER_START(UnityPerMaterial)
                float4 _Profile_ST;
                float _Mode, _Strength, _Pulse, _Shell;
                float4 _Bounds, _W2L0, _W2L1, _W2L2;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 lp : TEXCOORD0;      // il punto nello spazio dell'arma (x destra, y su, z verso la bocca)
                float3 nws : TEXCOORD1;
                float3 vws : TEXCOORD2;     // dalla superficie all'occhio
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                ws += n * _Shell;
                o.positionCS = TransformWorldToHClip(ws);
                float4 w4 = float4(ws, 1.0);
                o.lp = float3(dot(_W2L0, w4), dot(_W2L1, w4), dot(_W2L2, w4));
                o.nws = n;
                o.vws = _WorldSpaceCameraPos - ws;
                return o;
            }

            // ---------------------------------------------------------------- rumore
            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float vnoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash13(i);
                float n100 = hash13(i + float3(1, 0, 0));
                float n010 = hash13(i + float3(0, 1, 0));
                float n110 = hash13(i + float3(1, 1, 0));
                float n001 = hash13(i + float3(0, 0, 1));
                float n101 = hash13(i + float3(1, 0, 1));
                float n011 = hash13(i + float3(0, 1, 1));
                float n111 = hash13(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float fbm(float3 p)
            {
                float a = 0.5;
                float s = 0.0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    s += a * vnoise(p);
                    p = p * 2.03 + 17.0;
                    a *= 0.5;
                }
                return s / 0.9375;
            }

            // rumore cellulare: x = distanza dal punto caratteristico piu' vicino, y = dal secondo (unita' di cella). I bordi delle celle
            // (y - x vicino a 0) sono vere crepe, non linee di livello del rumore che fanno anelli chiusi.
            float2 cellular(float3 p)
            {
                float3 ip = floor(p);
                float3 fp = frac(p);
                float f1 = 8.0;
                float f2 = 8.0;
                [unroll] for (int k = -1; k <= 1; k++)
                [unroll] for (int j = -1; j <= 1; j++)
                [unroll] for (int i = -1; i <= 1; i++)
                {
                    float3 g = float3(i, j, k);
                    float3 c = ip + g;
                    float3 o = float3(hash13(c), hash13(c + 19.19), hash13(c + 47.47));
                    float3 r = g + o - fp;
                    float d = dot(r, r);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }
                return sqrt(float2(f1, f2));
            }

            // ---------------------------------------------------------------- i sette elementi
            // Luce fissa nello spazio dell'occhio (in alto a sinistra e verso chi guarda), calcolata dal fragment: i rivestimenti (sangue, acido,
            // brina) la usano per dare forma all'arma e un riflesso lucido; il fuoco, le crepe e le vene sono luce propria e non la usano.
            // g_shade: coseno tra la normale e la luce (0..1); g_spec: riflesso di Blinn stretto.
            static float g_shade = 1.0;
            static float g_spec = 0.0;

            // p: punto nello spazio dell'arma; h: 0 sotto, 1 sopra; front: livello - h (> 0 dove il calore e' gia' arrivato);
            // rim: bordo verso l'occhio. Il risultato e' premoltiplicato: rgb = luce aggiunta + colore di copertura, a = copertura.
            // Esposizione: la scena e' in HDR con tonemapping ACES (uscite oltre 0,5 sbiadiscono gia' verso il giallo-bianco) e bloom con
            // soglia 1. Quindi il corpo dell'effetto sta tra 0,1 e 0,6 (colori pieni: rosso, arancio, ambra) e solo le righe sottili
            // (il fronte, le vene, le crepe) salgono oltre 1: quelle diventano bianco-calde e fanno il bagliore.
            float3 HeatRamp(float x)
            {
                float3 c = lerp(float3(0.30, 0.012, 0.0), float3(0.95, 0.06, 0.0), saturate(x / 0.30));
                c = lerp(c, float3(1.0, 0.26, 0.01), saturate((x - 0.30) / 0.25));
                c = lerp(c, float3(1.0, 0.55, 0.08), saturate((x - 0.55) / 0.22));
                c = lerp(c, float3(1.0, 0.85, 0.40), saturate((x - 0.77) / 0.23));
                return c;
            }

            float4 CritLook(float3 p, float h, float front, float rim)
            {
                float t = _Time.y;
                float2 cw = cellular(p * 40.0 + float3(0.0, -t * 0.25, 0.0));
                float crack = 1.0 - smoothstep(0.0, 0.075, cw.y - cw.x);                     // crepe sui bordi delle celle
                float climb = smoothstep(0.0, 0.18, front);
                float edge = exp(-front * front / 0.0036);
                float surge = 0.5 + 0.5 * sin(h * 9.0 - t * 6.0 + vnoise(p * 14.0) * 5.0);   // ondate che risalgono lungo le crepe
                float glow = climb * (0.04 + crack * (0.4 + 0.7 * surge)) * (1.0 - 0.35 * h) + edge * (0.15 + crack * 0.7) + _Pulse * climb * crack * 0.5;
                float3 c = lerp(float3(0.95, 0.05, 0.06), float3(1.0, 0.40, 0.25), crack * crack);
                return float4(c * glow * (0.7 + 0.35 * rim) * _Strength, 0.0);
            }

            float4 BloodLook(float3 p, float h, float front, float rim)
            {
                float t = _Time.y;
                float n = fbm(float3(p.x * 40.0, p.y * 9.0, p.z * 40.0) + float3(0.0, t * 0.10, 0.0));
                float f2 = front + (n - 0.5) * 0.36;
                float reach = smoothstep(-0.03, 0.20, f2);
                float edge = exp(-f2 * f2 / 0.0030);
                float streak = vnoise(float3(p.x * 70.0, p.y * 7.0, p.z * 70.0) + float3(0.0, t * 0.5, 0.0));   // colature: strisce lungo y, scendono
                // il sangue ha forma: la luce fissa lo schiarisce sulle facce rivolte a lei e lo lascia quasi nero nell'ombra, e la faccia
                // bagnata riflette un lampo piccolo e duro. Senza questo l'arma sembrava una sagoma di plastica rossa.
                float3 coat = lerp(float3(0.020, 0.0, 0.002), float3(0.085, 0.003, 0.008), saturate(n * streak * 1.8));
                float shade = 0.22 + 0.78 * g_shade;
                float a = reach * (0.90 - 0.18 * h) * saturate(_Strength);                      // piu' fitto in basso, dove cola
                float wet = g_spec * smoothstep(0.30, 0.75, vnoise(p * 42.0 + float3(0.0, t * 0.3, 0.0))) * reach;   // a chiazze, come i riflessi del sangue bagnato
                float3 add = float3(0.95, 0.06, 0.07) * (edge * 0.7 + rim * 0.12 * reach + _Pulse * reach * 0.10) * (1.0 - 0.3 * h) + float3(1.0, 0.5, 0.45) * wet * 0.26;
                return float4(coat * shade * a + add * _Strength, a);
            }

            float4 FireLook(float3 p, float h, float front, float rim)
            {
                float t = _Time.y;
                float3 q = p * 26.0 + float3(0.0, -t * 5.0, 0.0);              // il rumore scende, quindi le lingue salgono
                float n1 = fbm(q);
                float n2 = fbm(q * 2.1 + float3(3.1, -t * 7.0, 1.7));
                float f2 = front + (n1 - 0.5) * 0.34;                           // il fronte e' un poco irregolare: lingue di fuoco
                float reach = smoothstep(-0.02, 0.18, f2);                      // 0 dove il calore non e' arrivato, 1 dietro al fronte
                float edge = exp(-f2 * f2 / 0.0040) * (0.5 + 0.8 * n2);         // la riga viva sul fronte
                float age = saturate(f2 / 1.9);                                 // da quanto e' passato il fronte: la sorgente (il fondo) e' la piu' calda
                float Tb = saturate(0.06 + 0.82 * age + 0.45 * (n2 - 0.45));    // temperatura del corpo rovente: rosso scuro in cima, ambra sul fondo
                float3 col = HeatRamp(Tb) * (0.04 + 0.26 * Tb) * reach;
                float spark = reach * smoothstep(0.64, 0.92, n2) * (1.0 - 0.75 * h);   // chiazze piu' calde vicino al fondo
                col += HeatRamp(0.8) * (edge * 0.55 + spark * 0.4);             // il fronte e le chiazze sono la parte piu' viva
                col *= (1.0 + 0.5 * rim) * (1.0 + 0.35 * _Pulse);               // il colpo la ravviva un attimo, senza accecarla
                return float4(col * _Strength, 0.0);
            }

            float4 AcidLook(float3 p, float h, float front, float rim)
            {
                float t = _Time.y;
                float n = fbm(p * 28.0 + float3(0.0, -t * 0.32, 0.0));
                float f2 = front + (n - 0.5) * 0.38;
                float reach = smoothstep(-0.03, 0.22, f2);
                float edge = exp(-f2 * f2 / 0.0036);
                float b = vnoise(p * 80.0 + float3(0.0, -t * 1.4, 0.0));
                float bubble = smoothstep(0.85, 0.94, b) * reach;               // bolle che salgono
                float drip = vnoise(float3(p.x * 55.0, p.y * 5.0 + t * 0.9, p.z * 55.0));   // colature: strisce lungo y che scivolano in basso
                float3 mud = lerp(float3(0.002, 0.007, 0.0), float3(0.012, 0.030, 0.0015), n);   // il verde pesa tre volte il rosso nella luminosita': a pari aspetto del sangue vuole un terzo del valore
                float shade = 0.12 + 0.88 * g_shade;                            // forma: facce al buio quasi nere, facce alla luce verde scuro
                float a = reach * (0.88 - 0.18 * h) * saturate(_Strength);
                float run = smoothstep(0.70, 0.86, drip) * reach;
                // il fango e' scuro: la luce tossica sta sulle colature vive, nelle bolle, sul bordo (rim) e sul fronte, non su tutta la faccia
                // il riflesso sta a chiazze (smoothstep sul rumore): un piano largo e piatto che guarda la luce lo accenderebbe tutto
                // d'un colpo e il fango diventerebbe una vernice chiara
                float wet = g_spec * smoothstep(0.35, 0.75, vnoise(p * 38.0 + float3(0.0, t * 0.15, 0.0))) * reach;
                // (con l'esposizione dell'albergo e l'ACES, 0,01 di verde puro e' gia' un verde visibile: il corpo scuro lo fanno questi
                // termini piccoli, non il fango; misurato sulle foto, il fango da solo pesava il 13%)
                float3 add = float3(0.42, 1.0, 0.10) * (edge * 0.7 + bubble * 0.45 + run * 0.20 + rim * 0.16 * reach + (1.0 - h) * 0.012 * reach * n + _Pulse * reach * 0.10)
                           + float3(0.75, 1.0, 0.50) * wet * 0.10;
                return float4(mud * shade * a + add * _Strength, a);
            }

            float4 ShockLook(float3 p, float h, float front, float rim)
            {
                float t = _Time.y;
                float tick = floor(t * 16.0);                                   // la corrente ricomincia 16 volte al secondo: sfarfalla
                float3 q = p * 28.0;
                float v1 = abs(vnoise(q + tick * 7.13) - 0.5);
                float v2 = abs(vnoise(q * 2.3 + 11.7 + tick * 3.31) - 0.5);
                float vein = max(1.0 - smoothstep(0.0, 0.055, v1), (1.0 - smoothstep(0.0, 0.038, v2)) * 0.75);
                float climb = smoothstep(0.0, 0.18, front);
                float run = 0.5 + 0.5 * sin(h * 26.0 - t * 24.0 + vnoise(q * 0.5) * 6.0);   // impulsi che salgono lungo le vene
                float edge = exp(-front * front / 0.0036);
                float glow = climb * (0.035 + 0.6 * vein * (0.3 + run)) * (1.0 - 0.25 * h) + edge * (0.25 + vein * 0.9) + _Pulse * climb * vein * 0.6;
                float3 c = lerp(float3(0.22, 0.45, 1.0), float3(0.82, 0.93, 1.0), vein * vein);
                return float4(c * glow * (0.85 + rim * 0.4) * _Strength, 0.0);
            }

            float4 FrostLook(float3 p, float h, float front, float rim)
            {
                float t = _Time.y;
                float c1 = fbm(p * 46.0);
                float facets = 1.0 - abs(2.0 * vnoise(p * 130.0) - 1.0);        // sfaccettature dei cristalli
                float f2 = front + (c1 - 0.5) * 0.40;
                float grow = smoothstep(-0.05, 0.14, f2);                       // la brina avanza a lingue
                float thick = saturate(0.20 + 0.45 * saturate(f2 / 1.2) + 0.7 * (c1 - 0.5));   // piu' fitta in basso, dove nasce
                float cover = grow * thick;
                float3 ice = lerp(float3(0.045, 0.10, 0.19), float3(0.27, 0.42, 0.62), facets * facets);
                float edge = exp(-f2 * f2 / 0.0036);
                float glint = step(0.95, vnoise(p * 180.0 + floor(t * 5.0))) * grow;   // brillii che si accendono e si spengono
                float3 add = float3(0.12, 0.42, 0.80) * (edge * 0.8 + rim * 0.18 * grow + _Pulse * grow * 0.10) + float3(1.0, 1.0, 1.0) * glint * 1.0
                           + float3(0.55, 0.80, 1.0) * g_spec * 0.20 * grow;
                float a = cover * 0.60 * saturate(_Strength);
                float shade = 0.45 + 0.75 * g_shade;                            // la brina ha forma: piu' chiara alla luce, azzurro cupo all'ombra
                return float4(ice * shade * a + add * _Strength, a);
            }

            float4 CursedLook(float3 p, float h, float front, float rim)
            {
                float t = _Time.y;
                float3 q = p * 28.0 + float3(0.0, -t * 3.0, 0.0);
                float n1 = fbm(q);
                float n2 = fbm(q * 1.8 + 5.2);
                float f2 = front + (n1 - 0.5) * 0.36;
                float reach = smoothstep(-0.03, 0.2, f2);
                float edge = exp(-f2 * f2 / 0.0046);
                float2 cw = cellular(p * 30.0 + float3(0.0, -t * 0.3, 0.0));
                float crack = 1.0 - smoothstep(0.0, 0.09, cw.y - cw.x);        // crepe di brace sui bordi delle celle
                float beat = 0.7 + 0.3 * sin(t * 6.3);
                float a = reach * 0.58 * (0.5 + 0.5 * n2) * saturate(_Strength);   // il fuoco scuro copre il metallo
                float3 dark = float3(0.03, 0.0, 0.02);
                float3 add = float3(1.0, 0.18, 0.06) * (crack * reach * 0.8 * beat + edge * 0.7 + rim * 0.28 * reach * n2 + _Pulse * reach * 0.15) * (1.0 - 0.3 * h)
                           + float3(0.55, 0.05, 0.85) * edge * 0.35;
                return float4(dark * a + add * _Strength, a);
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Strength < 0.002 || _Mode > 8.0) return half4(0, 0, 0, 0);
                float3 p = i.lp;
                float3 N = normalize(i.nws);
                float3 V = normalize(i.vws);
                float rim = pow(saturate(1.0 - dot(N, V)), 2.5);
                float3 Lk = normalize(-0.55 * UNITY_MATRIX_V[0].xyz + 0.85 * UNITY_MATRIX_V[1].xyz + 0.50 * UNITY_MATRIX_V[2].xyz);   // sinistra, su, indietro (verso l'occhio)
                g_shade = saturate(dot(N, Lk));
                g_spec = pow(saturate(dot(N, normalize(Lk + V))), 40.0);
                float along = saturate((p.z - _Bounds.x) * _Bounds.y);
                float3 prof = SAMPLE_TEXTURE2D_LOD(_Profile, sampler_Profile, float2(along, 0.5), 0).rgb;
                float h = saturate((p.y - prof.r) / max(0.02, prof.g - prof.r));
                float front = prof.b - h;
                if (front < -0.4) return half4(0, 0, 0, 0);                // il calore non e' ancora arrivato qui
                float4 r;
                if (_Mode < 0.5) r = CritLook(p, h, front, rim);
                else if (_Mode < 1.5) r = BloodLook(p, h, front, rim);
                else if (_Mode < 2.5) r = FireLook(p, h, front, rim);
                else if (_Mode < 3.5) r = AcidLook(p, h, front, rim);
                else if (_Mode < 4.5) r = ShockLook(p, h, front, rim);
                else if (_Mode < 5.5) r = FrostLook(p, h, front, rim);
                else r = CursedLook(p, h, front, rim);
                return half4(r);
            }
            ENDHLSL
        }
    }
}
