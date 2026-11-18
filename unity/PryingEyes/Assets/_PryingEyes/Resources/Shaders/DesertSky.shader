Shader "PryingEyes/DesertSky"
{
    // Il cielo del deserto: una cupola intera che segue la camera e sta sul piano lontano (come NightSky). Il colore dell'orizzonte e dello zenit,
    // il sole, la luna, le stelle e le nuvole vengono dall'orologio del deserto (DesertSky.cs) attraverso variabili globali: nessuna texture.
    // Sotto l'orizzonte e' il colore della nebbia, cosi' dove finisce il terreno la sabbia si perde nella foschia senza uno stacco.
    // Sta in Resources perche' Shader.Find lo trovi nella build.
    Properties { }
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

            float4 _DSHorizon;      // rgb: colore sull'orizzonte
            float4 _DSZenith;       // rgb: colore allo zenit
            float4 _DSFog;          // rgb: colore sotto l'orizzonte (= la nebbia)
            float4 _DSSunDir;       // xyz: verso il sole, w: quanto si vede il disco (0..1)
            float4 _DSSunColor;     // rgb: colore del sole
            float4 _DSMoonDir;      // xyz: verso la luna, w: quanto si vede (0..1)
            float4 _DSCloud;        // rgb: colore delle nuvole, a: copertura (0..1)
            float4 _DSParams;       // x: stelle (0..1), y: tempo, z: crepuscolo (0..1), w: visibilita' (1 aria limpida, 0 tempesta di sabbia)
            float _DSSunGlow;       // quanto e' forte il bagliore attorno al sole (1 com'era, meno e' piu' discreto; il disco resta); lo scrive DesertSky

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

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
                o.dir = dirWS;
                return o;
            }

            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash3(i + float3(0, 0, 0)), Hash3(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float Fbm3(float3 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += Noise3(p) * a; p = p * 2.07 + 11.3; a *= 0.5; }
                return s;
            }

            // il campo delle nuvole basse: grandi masse (celle di circa due unita' di cuv) con un dettaglio piu' fine. La densita' e' un'altezza: dove sta sopra la soglia c'e' nuvola
            // (e quanto sta sopra e' lo spessore); la luce e' la pendenza verso il sole.
            float CloudField(float2 uv, float2 w)
            {
                return Fbm3(float3(uv * 0.45 + w, 3.0)) * 0.66 + Fbm3(float3(uv * 1.53 + w * 1.6, 8.0)) * 0.34;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float t = saturate(h);
                float vis = _DSParams.w;                    // 1 aria limpida, 0 nella tempesta di sabbia

                // il gradiente: cambia in fretta vicino all'orizzonte e piano verso lo zenit (il blu profondo sta in alto); sulla linea dell'orizzonte il cielo e' esattamente
                // il colore della nebbia, cosi' la sabbia lontana ci si scioglie dentro; sotto l'orizzonte e' tutta nebbia
                float g = 1.0 - exp(-t * 3.0);
                half3 col = lerp(_DSHorizon.rgb, _DSZenith.rgb, pow(g, 1.15));
                col = lerp(col, _DSFog.rgb, exp(-t * 9.0));
                col = lerp(col, _DSFog.rgb, saturate(-h * 6.0));

                // il sole: un alone largo che prende il colore del sole (arancio carico quando e' basso, quasi bianco quando e' alto), uno stretto, e il disco (HDR: il bagliore lo prende); l'aria piena di
                // sabbia lo spegne. L'alone si mescola al cielo (lerp) invece di sommarsi: sommato, un arancio sopra un cielo chiaro sbianca tutto (il tone mapping porta al bianco quello che supera 1) e il
                // cielo attorno al sole diventava lattiginoso; mescolato, il cielo vicino al sole e' del suo colore e non piu' chiaro di lui. La stessa formula e' in DesertSky.cs per la nebbia.
                float sd = dot(d, _DSSunDir.xyz);
                float sdp = saturate(sd);
                float hiSun = saturate(_DSSunDir.y * 2.2);                                              // 0 col sole sull'orizzonte, 1 quando e' alto: alto, l'alone largo si stringe
                float haloW = (pow(sdp, lerp(5.0, 9.0, hiSun)) * 0.50 + pow(sdp, 36.0) * 0.40) * _DSSunDir.w * (0.35 + 0.65 * vis) * _DSSunGlow;
                float sunCore = pow(sdp, 220.0) * 1.2 + smoothstep(0.99965, 0.99985, sd) * 14.0;      // il nucleo e il disco si aggiungono dopo le nuvole: una nuvola sottile non lo spegne del tutto
                col = lerp(col, _DSSunColor.rgb, saturate(haloW));
                // all'alba e al tramonto l'orizzonte dalla parte del sole si accende
                col += _DSSunColor.rgb * pow(sdp, 2.0) * exp(-t * 7.0) * _DSParams.z * 0.45 * vis * _DSSunGlow;

                // i raggi di sole: strisce che partono dal sole (rumore sulla direzione dello scarto dal sole), piu' evidenti col sole basso; non aggiungono luce lontano dal sole
                {
                    float3 sT = normalize(cross(_DSSunDir.xyz, float3(0.0, 1.0, 0.0)) + float3(1e-5, 0.0, 0.0));
                    float3 sB = cross(_DSSunDir.xyz, sT);
                    float3 rel = d - _DSSunDir.xyz * sd;
                    float2 u2 = float2(dot(rel, sT), dot(rel, sB));
                    float2 dir2 = u2 / (length(u2) + 1e-5);
                    float tt = _DSParams.y * 0.01;
                    float b1 = Noise3(float3(dir2 * 3.2 + float2(5.0, 0.0), tt));
                    float b2 = Noise3(float3(dir2 * 8.5 + float2(1.0, 9.0), tt * 1.7 + 4.0));
                    float beams = smoothstep(0.42, 0.88, b1 * 0.6 + b2 * 0.4);
                    float fall = pow(saturate((sd - 0.50) / 0.50), 2.0);
                    float low = 1.0 - saturate(_DSSunDir.y * 1.6);
                    col += _DSSunColor.rgb * (beams * fall * (0.035 + 0.15 * low) * _DSSunDir.w * (0.35 + 0.65 * vis) * _DSSunGlow);
                }

                // le catene lontane: due creste di montagne azzurrine sull'orizzonte (la cresta e' il seno dell'elevazione, fino a sei gradi), perse nella foschia
                float2 circ = normalize(d.xz + float2(1e-5, 0.0));
                float r1 = Fbm3(float3(circ * 2.2, 4.0));
                float r2 = Fbm3(float3(circ * 4.6, 11.0));
                float ridgeFar = 0.018 + 0.085 * pow(saturate(r1 * 1.25 - 0.18), 1.5);
                float ridgeNear = 0.004 + 0.045 * pow(saturate(r2 * 1.25 - 0.20), 1.4);
                float base = smoothstep(-0.05, 0.0, h);
                half3 mFar = lerp(_DSFog.rgb, _DSZenith.rgb * 0.8, 0.42);
                half3 mNear = lerp(_DSFog.rgb, _DSZenith.rgb * 0.6, 0.55);
                col = lerp(col, mFar, smoothstep(ridgeFar + 0.0015, ridgeFar - 0.0015, h) * base * 0.80 * vis);
                col = lerp(col, mNear, smoothstep(ridgeNear + 0.0015, ridgeNear - 0.0015, h) * base * 0.85 * vis);
                float occl = max(smoothstep(ridgeFar + 0.0015, ridgeFar - 0.0015, h), smoothstep(ridgeNear + 0.0015, ridgeNear - 0.0015, h));      // le montagne coprono il disco quando il sole e' basso

                // la luna
                float md = dot(d, _DSMoonDir.xyz);
                float moon = smoothstep(0.99950, 0.99968, md);
                col += half3(0.78, 0.85, 1.0) * (moon * 2.6 + pow(saturate(md), 120.0) * 0.10) * _DSMoonDir.w;

                // le stelle: puntini in celle di circa un terzo di grado, che brillano piano
                float3 sp = d * 190.0;
                float3 cell = floor(sp);
                float r = Hash3(cell);
                float3 jitter = float3(Hash3(cell + 3.1), Hash3(cell + 7.7), Hash3(cell + 9.3)) - 0.5;
                float dist = length(frac(sp) - 0.5 - jitter * 0.45);
                float core = saturate(1.0 - dist * 2.6);
                float star = step(0.9962, r) * core * core * (0.45 + 0.55 * Hash3(cell + 1.9)) * (0.78 + 0.22 * sin(_DSParams.y * 2.0 + r * 60.0));
                col += half3(0.9, 0.93, 1.0) * star * _DSParams.x * saturate(h * 4.0 + 0.2) * 1.6;

                // la Via Lattea: una fascia larga e pallida lungo un cerchio massimo inclinato, piu' densa da un lato, con macchie di polvere scura e un velo di stelle minute. Si vede solo di notte con
                // l'aria limpida (_DSParams.x: le stelle, 0 di giorno), un po' meno con la luna; le nuvole la coprono perche' vengono dopo
                if (_DSParams.x > 0.02 && h > -0.05)
                {
                    float3 galN = normalize(float3(0.35, 0.62, -0.70));
                    float gb = dot(d, galN);
                    float band = exp(-gb * gb / 0.022);
                    if (band > 0.01)
                    {
                        float3 gA = normalize(cross(galN, float3(0.0, 1.0, 0.0)));
                        float3 gB = cross(galN, gA);
                        float2 gp = float2(dot(d, gA), dot(d, gB));
                        float along = atan2(gp.y, gp.x);
                        float bulge = 0.50 + 0.50 * exp(-pow(along - 0.6, 2.0) * 1.2);
                        float cloudN = Fbm3(float3(gp * 3.0 + 3.0, 17.0));
                        float lane = smoothstep(0.42, 0.70, Fbm3(float3(gp * 7.0 + 1.0, 31.0)));
                        float mw = band * bulge * (0.35 + 0.65 * cloudN) * (1.0 - 0.7 * lane);
                        float3 sp2 = d * 420.0;
                        float3 cell2 = floor(sp2);
                        float tiny = step(0.93, Hash3(cell2 + 5.5)) * pow(saturate(1.0 - length(frac(sp2) - 0.5) * 3.0), 2.0);
                        float vis2 = _DSParams.x * saturate(h * 3.0 + 0.15) * (1.0 - 0.5 * _DSMoonDir.w);
                        col += half3(0.50, 0.58, 0.82) * mw * 0.28 * vis2;
                        col += half3(0.85, 0.90, 1.0) * tiny * band * (1.0 - 0.6 * lane) * 0.8 * vis2;
                    }
                }

                // le nuvole: due strati che scorrono piano, a pezzi (la copertura _DSCloud.a: 0,1 quasi sereno, 0,3 ben coperto). Il basso, di cumuli: il rumore a grandi celle, con un po' di
                // dettaglio, soglia attorno alla copertura voluta; il lato verso il sole e' piu' chiaro (si campiona la densita' un passo verso il sole: dove cala il bordo e' illuminato)
                // e ha un filo d'argento. L'alto, di cirri: strisce sottili allungate col vento. Svaniscono nella foschia dell'orizzonte.
                float cover = 0.0;                                   // quanto le nuvole coprono questa direzione (per il sole)
                if (h > 0.005)
                {
                    float2 w = float2(_DSParams.y * 0.004, _DSParams.y * 0.0016);
                    float2 cuv = d.xz / (h + 0.10);
                    float centre = 0.69 - _DSCloud.a * 0.62;                 // la copertura 0,1 e' circa il 6% del cielo, 0,2 il 22%, 0,3 il 38% (il rumore sta attorno a 0,47)
                    float f0 = CloudField(cuv, w);
                    float er = Noise3(float3(cuv * 4.05 + w * 2.2, 14.0)) - 0.5;      // l'erosione dei bordi: gli orli si sfilacciano
                    float body = f0 + er * 0.09;
                    float dens = smoothstep(centre - 0.05, centre + 0.07, body);
                    float fadeH = smoothstep(0.0, 0.16, h);
                    if (dens > 0.003)
                    {
                        float2 toSun = normalize(_DSSunDir.xz + float2(1e-4, 0.0));
                        float thick = saturate((body - centre) / 0.22);          // 0 sull'orlo, 1 nel cuore della massa
                        float f1 = CloudField(cuv + toSun * 0.06, w);
                        float f2 = CloudField(cuv + toSun * 0.16, w);
                        float lit = saturate(0.60 + (f0 - f1) * 6.0 + (f0 - f2) * 2.5);        // dove il campo cala verso il sole il bordo e' illuminato
                        half3 shadowCol = lerp(_DSCloud.rgb * 0.46, _DSZenith.rgb * 0.95 + _DSCloud.rgb * 0.15, 0.35);
                        float k = saturate(lit * (1.0 - 0.55 * thick) + 0.33 * (1.0 - thick));
                        half3 cc = lerp(shadowCol, _DSCloud.rgb * 1.04, k);
                        cc += _DSSunColor.rgb * (pow(sdp, 6.0) * (1.0 - thick) * 0.9 + pow(sdp, 8.0) * 0.30 * (1.0 - dens * 0.5)) * _DSSunDir.w;       // il filo d'argento sugli orli verso il sole
                        col = lerp(col, cc, dens * fadeH * 0.92);
                        cover = dens * fadeH;
                    }
                    float2 cu2 = d.xz / (h + 0.45) * float2(0.35, 2.4) + w * 0.5;
                    float cirrus = smoothstep(0.50, 0.78, Fbm3(float3(cu2, 21.0))) * smoothstep(0.02, 0.25, h) * (1.0 - 0.6 * _DSCloud.a);
                    col = lerp(col, lerp(_DSCloud.rgb, col, 0.35), cirrus * 0.22 * vis);
                }
                col += _DSSunColor.rgb * (sunCore * _DSSunDir.w * vis * (1.0 - 0.85 * cover) * (1.0 - occl));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
