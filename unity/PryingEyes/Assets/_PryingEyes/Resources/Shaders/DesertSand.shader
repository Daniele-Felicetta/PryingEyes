Shader "PryingEyes/DesertSand"
{
    // La sabbia del deserto: tutta procedurale, nessuna texture. Il colore cambia piano piano con la posizione (zone piu' chiare, piu' rosse e piu'
    // scure), sui pendii ripidi diventa roccia, e le dune hanno increspature del vento (ondine piccole, spostate dal rumore) che muovono
    // la normale: al sole radente di mattina e di sera ogni cresta ha la sua ombra. Da vicino si vede anche il grano. Il resto e' la luce di URP
    // (sole con le ombre, luci puntuali, ambiente, nebbia): UniversalFragmentPBR come il Lit.
    // Le passate delle ombre e della profondita' sono quelle del Lit (UsePass): la sabbia proietta ombre e finisce nella profondita' come tutto.
    Properties
    {
        _SandLight ("Sabbia chiara", Color) = (0.80, 0.64, 0.42, 1)
        _SandDark ("Sabbia scura", Color) = (0.62, 0.43, 0.26, 1)
        _SandRed ("Sabbia rossa", Color) = (0.67, 0.42, 0.26, 1)
        _Rock ("Roccia", Color) = (0.45, 0.33, 0.24, 1)
        _Wind ("Vento (xz)", Vector) = (0.82, 0.57, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex SandVertex
            #pragma fragment SandFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SandLight;
                half4 _SandDark;
                half4 _SandRed;
                half4 _Rock;
                float4 _Wind;
            CBUFFER_END

            float4 _DSCloudShadow;      // z: quanto l'ombra di una nuvola scurisce il terreno al sole (0 di notte e nella tempesta), w: la copertura del cielo (come _DSCloud.a); lo scrive DesertSky
            float _DSPebbleBoost;       // 0 in gioco; la prova GroundShots lo porta a 1 per fotografare il pavimento di ciottoli anche dove il rumore non ne mette
            // il cielo, per lo specchio delle pozze sulle saline (li scrive DesertSky, come per la cupola del cielo)
            float4 _DSHorizon, _DSZenith, _DSFog, _DSSunDir, _DSSunColor, _DSMoonDir, _DSCloud, _DSParams;
            float _DSSunGlow;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4 color       : COLOR;          // il colore della regione: rgb = moltiplicatore (0..2, in meta'), a = quanto colore perde
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half4 tint        : TEXCOORD3;
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half4 fogFactorAndVertexLight : TEXCOORD5;
            #else
                half fogFactor : TEXCOORD5;
            #endif
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD6;
            #endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD10;
            #endif
                float4 positionCS : SV_POSITION;
            };

            Varyings SandVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
                half fogFactor = 0;
            #if !defined(_FOG_FRAGMENT)
                fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif
                output.normalWS = normalInput.normalWS;
                output.tint = input.color;
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
            #else
                output.fogFactor = fogFactor;
            #endif
                output.positionWS = vertexInput.positionWS;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(vertexInput);
            #endif
                output.positionCS = vertexInput.positionCS;
                return output;
            }

            float H21(float2 p)
            {
                float3 q = frac(float3(p.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H21(i), H21(i + float2(1, 0)), f.x), lerp(H21(i + float2(0, 1)), H21(i + float2(1, 1)), f.x), f.y);
            }

            float Fbm2(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += VNoise(p) * a; p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            // il vento non e' lo stesso dappertutto: la direzione e la distanza delle creste cambiano piano su scale di cento metri (due ottave bastano). Senza, le increspature erano righe tutte parallele
            // e alla stessa distanza fino all'orizzonte.
            float WarpN(float2 q)
            {
                return VNoise(q * 0.008 + 13.7) * 0.65 + VNoise(q * 0.021 + 3.3) * 0.35;
            }

            // e a scala di pochi metri (un massimo ogni nove, tre di ampiezza) le creste si piegano, si stringono e si allargano da un punto all'altro, come quelle vere: con il solo spostamento a
            // cento metri erano righe lunghe e dritte, un campo arato
            float2 RippleBend(float2 q)
            {
                return (float2(VNoise(q * 0.11 + 3.1), VNoise(q * 0.11 + 9.7)) - 0.5) * 7.0;
            }

            // l'ombra delle nuvole: macchie larghe (qualche centinaio di metri) dal bordo morbido, che scorrono piano col vento. Stessa soglia del cielo (la copertura decide quante), ma non
            // sono le nuvole che si vedono: la sabbia e' vicina e le nuvole lontane, nessuno controlla che combacino. 1 = al sole, meno di 1 = all'ombra di una nuvola.
            // All'ombra manca il sole (giallo) e resta il cielo (azzurro): la sabbia scurisce piu' nel rosso che nel blu, l'ombra e' fredda e non una macchia di fango.
            half3 CloudShade(float2 p, float dist)
            {
                float2 q = (p + float2(0.82, 0.57) * (_Time.y * 5.5)) / 430.0;
                float nn = Fbm2(q + 9.3);
                float centre = 0.69 - _DSCloudShadow.w * 0.62;
                float dens = smoothstep(centre - 0.07, centre + 0.07, nn);
                return 1.0 - (_DSCloudShadow.z * dens * saturate(1.3 - dist / 1100.0)) * half3(1.0, 0.88, 0.62);
            }

            // la nebbia sta in basso: la sua densita' cala con la quota (e^(-k*y), y sopra il suolo). L'esponente della nebbia esponenziale di URP e' proporzionale alla lunghezza del raggio, quindi
            // basta moltiplicarlo per la media di e^(-k*y) lungo il raggio dalla camera al punto (l'integrale e' esatto): in pianura non cambia nulla, le cime dei rilievi e delle torri escono dalla
            // foschia, piu' scure e piu' vicine, e la base resta velata. Uguale in DesertProp.shader (un sasso e la sabbia attorno prendono la stessa nebbia).
            float HeightFog(float3 P)
            {
                const float k = 0.015;
                float hc = max(_WorldSpaceCameraPos.y + 2.4, 0.0), hp = max(P.y + 2.4, 0.0);
                float a = exp(-k * hc), b = exp(-k * hp);
                float dh = k * (hp - hc);
                return max(abs(dh) < 0.002 ? a : (a - b) / dh, 0.45);
            }

            // il cielo visto in uno specchio d'acqua (le pozze delle saline): la stessa formula di DesertSky.shader per il gradiente, l'alone e il disco del sole, la luna, le stelle e lo strato
            // basso di nuvole (stesso rumore, stessi numeri: la nuvola riflessa sta dove sta quella vera), senza i cirri e la Via Lattea. L'orizzonte e' il colore della nebbia come in cielo, quindi
            // una pozza lontana si perde nella foschia con la sabbia.
            float SkyHash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float SkyNoise3(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(SkyHash3(i + float3(0, 0, 0)), SkyHash3(i + float3(1, 0, 0)), f.x),
                                 lerp(SkyHash3(i + float3(0, 1, 0)), SkyHash3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(SkyHash3(i + float3(0, 0, 1)), SkyHash3(i + float3(1, 0, 1)), f.x),
                                 lerp(SkyHash3(i + float3(0, 1, 1)), SkyHash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float SkyFbm3(float3 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += SkyNoise3(p) * a; p = p * 2.07 + 11.3; a *= 0.5; }
                return s;
            }

            // il campo delle nuvole basse: grandi masse (celle di circa due unita' di cuv) con un dettaglio piu' fine. La densita' e' un'altezza: dove sta sopra la soglia c'e' nuvola
            // (e quanto sta sopra e' lo spessore); la luce e' la pendenza verso il sole.
            float SkyCloudField(float2 uv, float2 w)
            {
                return SkyFbm3(float3(uv * 0.45 + w, 3.0)) * 0.66 + SkyFbm3(float3(uv * 1.53 + w * 1.6, 8.0)) * 0.34;
            }

            half3 MirrorSky(float3 d)
            {
                float h = d.y, t = saturate(h);
                float vis = _DSParams.w;
                float g = 1.0 - exp(-t * 3.0);
                half3 col = lerp(_DSHorizon.rgb, _DSZenith.rgb, pow(g, 1.15));
                col = lerp(col, _DSFog.rgb, exp(-t * 9.0));
                float sd = dot(d, _DSSunDir.xyz);
                float sdp = saturate(sd);
                float hiSun = saturate(_DSSunDir.y * 2.2);
                float haloW = (pow(sdp, lerp(5.0, 9.0, hiSun)) * 0.50 + pow(sdp, 36.0) * 0.40) * _DSSunDir.w * (0.35 + 0.65 * vis) * _DSSunGlow;
                col = lerp(col, _DSSunColor.rgb, saturate(haloW));
                float md = dot(d, _DSMoonDir.xyz);
                col += half3(0.78, 0.85, 1.0) * (smoothstep(0.99950, 0.99968, md) * 2.6 + pow(saturate(md), 120.0) * 0.10) * _DSMoonDir.w;
                if (_DSParams.x > 0.02)
                {
                    float3 sp = d * 190.0;
                    float3 cell = floor(sp);
                    float r = SkyHash3(cell);
                    float3 jitter = float3(SkyHash3(cell + 3.1), SkyHash3(cell + 7.7), SkyHash3(cell + 9.3)) - 0.5;
                    float core = saturate(1.0 - length(frac(sp) - 0.5 - jitter * 0.45) * 2.6);
                    float star = step(0.9962, r) * core * core * (0.45 + 0.55 * SkyHash3(cell + 1.9)) * (0.78 + 0.22 * sin(_DSParams.y * 2.0 + r * 60.0));
                    col += half3(0.9, 0.93, 1.0) * star * _DSParams.x * saturate(h * 4.0 + 0.2) * 1.6;
                }
                float cover = 0.0;                                   // quanto le nuvole coprono questa direzione (per il sole)
                if (h > 0.005)
                {
                    float2 w = float2(_DSParams.y * 0.004, _DSParams.y * 0.0016);
                    float2 cuv = d.xz / (h + 0.10);
                    float centre = 0.69 - _DSCloud.a * 0.62;                 // la copertura 0,1 e' circa il 6% del cielo, 0,2 il 22%, 0,3 il 38% (il rumore sta attorno a 0,47)
                    float f0 = SkyCloudField(cuv, w);
                    float er = SkyNoise3(float3(cuv * 4.05 + w * 2.2, 14.0)) - 0.5;      // l'erosione dei bordi: gli orli si sfilacciano
                    float body = f0 + er * 0.09;
                    float dens = smoothstep(centre - 0.05, centre + 0.07, body);
                    float fadeH = smoothstep(0.0, 0.16, h);
                    if (dens > 0.003)
                    {
                        float2 toSun = normalize(_DSSunDir.xz + float2(1e-4, 0.0));
                        float thick = saturate((body - centre) / 0.22);          // 0 sull'orlo, 1 nel cuore della massa
                        float f1 = SkyCloudField(cuv + toSun * 0.06, w);
                        float f2 = SkyCloudField(cuv + toSun * 0.16, w);
                        float lit = saturate(0.60 + (f0 - f1) * 6.0 + (f0 - f2) * 2.5);        // dove il campo cala verso il sole il bordo e' illuminato
                        half3 shadowCol = lerp(_DSCloud.rgb * 0.46, _DSZenith.rgb * 0.95 + _DSCloud.rgb * 0.15, 0.35);
                        float k = saturate(lit * (1.0 - 0.55 * thick) + 0.33 * (1.0 - thick));
                        half3 cc = lerp(shadowCol, _DSCloud.rgb * 1.04, k);
                        cc += _DSSunColor.rgb * (pow(sdp, 6.0) * (1.0 - thick) * 0.9 + pow(sdp, 8.0) * 0.30 * (1.0 - dens * 0.5)) * _DSSunDir.w;       // il filo d'argento sugli orli verso il sole
                        col = lerp(col, cc, dens * fadeH * 0.92);
                        cover = dens * fadeH;
                    }
                }
                float sunCore = pow(sdp, 220.0) * 1.2 + smoothstep(0.99965, 0.99985, sd) * 14.0;
                col += _DSSunColor.rgb * (sunCore * _DSSunDir.w * vis * (1.0 - 0.85 * cover));
                return col;
            }

            void SandFragment(Varyings input
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                float3 P = input.positionWS;
                float3 N = normalize(input.normalWS);
                float dist = distance(P, _WorldSpaceCameraPos);
                float2 p = P.xz;

                float2 wd = normalize(_Wind.xy);
                float2 perpW = float2(-wd.y, wd.x);

                // colore: chiazze larghe di sabbia chiara, scura e rossastra
                float broad = Fbm2(p * 0.0035 + 3.7);
                float mid = Fbm2(p * 0.021 + 11.3);
                float red = smoothstep(0.52, 0.78, Fbm2(p * 0.0021 - 8.9));
                half3 sand = lerp(_SandDark.rgb, _SandLight.rgb, smoothstep(0.30, 0.70, broad));
                sand = lerp(sand, _SandRed.rgb, red * 0.55);
                sand *= 0.88 + 0.24 * mid;
                // le strisce del vento: macchie allungate lungo il vento (qualche decina di metri di lunghezza, pochi di larghezza) che a media distanza rompono la monotonia
                sand *= 0.92 + 0.16 * Fbm2(float2(dot(p, wd) * 0.007, dot(p, perpW) * 0.13) + 4.4);
                // la regione: la sabbia perde colore (sale, cenere, gesso) e prende la sua tinta
                half3 regionMul = input.tint.rgb * 2.0;
                half lum = dot(sand, half3(0.30, 0.59, 0.11));
                sand = lerp(sand, half3(lum, lum, lum), input.tint.a) * regionMul;
                // a mezzogiorno, col sole alto, la sabbia e' piu' pallida e meno gialla (luce bianca, cielo azzurro che riempie le ombre: il giallo pieno delle dune sembrava un fumetto); col sole
                // basso resta com'e', ricca di colore
                float noonK = smoothstep(0.40, 0.90, _DSSunDir.y);
                half lumN = dot(sand, half3(0.30, 0.59, 0.11));
                sand = lerp(sand, half3(lumN, lumN, lumN * 1.04), 0.30 * noonK);

                // increspature del vento, a due scale. Le creste sono linee storte perpendicolari al vento: la fase e' la distanza lungo il vento piu' uno spostamento a rumore a grande
                // scala (le creste si piegano, si fondono, si biforcano) e a media scala (le rende irregolari); l'altezza della cresta cambia piano nello spazio (zone quasi lisce,
                // zone ben solcate) e il profilo e' asimmetrico (il lato sottovento e' piu' ripido). Nessun rumore a celle sulla normale: a sole radente le celle si leggevano come una griglia.
                float wide = Fbm2(p * 0.045 + 31.7);
                // lo spostamento di fase a grande scala (le creste girano di una ventina di gradi e si allargano o si stringono) e il suo gradiente: la normale delle increspature inclina nel verso
                // in cui la fase cresce, che non e' piu' quello del vento
                const float warpAmp = 100.0;
                float wN0 = WarpN(p);
                float2 gWarp = float2(WarpN(p + float2(0.7, 0.0)) - wN0, WarpN(p + float2(0.0, 0.7)) - wN0) * (warpAmp / 0.7);
                float warp = (wN0 - 0.5) * warpAmp;
                float2 bend0 = RippleBend(p), bendX = RippleBend(p + float2(0.5, 0.0)), bendZ = RippleBend(p + float2(0.0, 0.5));
                float2 gBend = float2(dot(bendX - bend0, wd), dot(bendZ - bend0, wd)) * 2.0;           // la derivata di dot(spostamento, vento)
                float2 gdir = normalize((wd + gBend) * 5.4 + gWarp);
                float2 pb = p + bend0;
                float phaseA = dot(pb, wd) * 5.4 + warp + wide * 22.0 + Fbm2(p * 0.19 + 5.3) * 2.6;
                float ampA = lerp(0.25, 1.0, smoothstep(0.30, 0.72, Fbm2(p * 0.021 + 9.1)));
                float dA = cos(phaseA) + 0.45 * cos(2.0 * phaseA + 1.3);                 // derivata del profilo: sinusoide con la seconda armonica = dorso asimmetrico
                float ripple = sin(phaseA) + 0.45 * 0.5 * sin(2.0 * phaseA + 1.3);
                float rip = saturate(ripple * 0.5 + 0.5);                                  // 0 nelle valli, 1 sulle creste
                float fade = saturate(1.0 - dist / 85.0);
                float slopeFlat = smoothstep(0.62, 0.93, N.y);                           // sulle pendenze ripide non ci sono
                float3 Np = N;
                Np.xz += gdir * (dA * 0.15 * ampA * fade * slopeFlat);
                // le increspature fini (qualche centimetro), solo da vicino: stesse creste, piu' fitte e piu' corte
                float fineFade = saturate(1.0 - dist / 26.0) * slopeFlat;
                float phaseB = dot(pb, wd) * 21.0 + warp * 3.9 + Fbm2(p * 0.55 + 2.2) * 9.0;
                Np.xz += gdir * (cos(phaseB) * 0.075 * fineFade * (0.4 + 0.6 * ampA));
                // le onde larghe (una decina di metri tra due creste) si vedono fino a mezzo chilometro: lontano il terreno non e' liscio ma segnato come visto da un aereo
                float midFade = saturate(1.0 - dist / 450.0) * slopeFlat;
                Np.xz += gdir * (cos(dot(p, wd) * 0.62 + warp * 0.115 + wide * 7.0) * 0.060 * midFade);
                // il grano: rumore fine (continuo, non a celle) che si perde con la distanza
                float grain = VNoise(p * 95.0) * 0.6 + VNoise(p * 41.0 + 7.3) * 0.4 - 0.5;
                float grainFade = saturate(1.0 - dist / 14.0);
                sand *= 1.0 + grain * 0.30 * grainFade;
                sand *= 1.0 - 0.13 * (1.0 - rip) * fade * (0.5 + 0.5 * ampA);            // le valli delle increspature sono un po' piu' scure e le creste un po' piu' calde
                sand *= 1.0 + 0.05 * rip * fade * ampA;
                // la forma delle dune anche a mezzogiorno, col sole alto che non da' ombre: la faccia sottovento (la parete di scivolamento, che guarda dove va il vento) e' piu' scura e un poco piu'
                // ricca di colore (i grani pesanti e scuri si raccolgono li'), la faccia sopravvento un filo piu' chiara; la normale e' quella liscia del terreno, senza le increspature
                float lee = saturate(dot(N.xz, wd) * 2.4), windw = saturate(-dot(N.xz, wd) * 2.4);
                sand *= (1.0 - 0.24 * lee + 0.06 * windw) * lerp(half3(1, 1, 1), half3(1.05, 0.96, 0.88), lee);
                // le saline: una crosta spaccata in placche di qualche metro (celle di Voronoi), con le fessure piu' scure; si vede da vicino
                float salt = smoothstep(0.55, 0.80, input.tint.a);
                if (salt > 0.01 && dist < 90.0)
                {
                    float2 q = p * 0.22, g = floor(q), f = frac(q);
                    float d1 = 8.0, d2 = 8.0;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            float2 o = float2(i, j);
                            float dd = length(o + float2(H21(g + o), H21(g + o + 31.7)) - f);
                            if (dd < d1) { d2 = d1; d1 = dd; } else if (dd < d2) d2 = dd;
                        }
                    float crack = 1.0 - smoothstep(0.0, 0.07, d2 - d1);
                    sand *= 1.0 - 0.38 * crack * salt * saturate(1.0 - dist / 90.0);
                }
                // le pozze delle saline: dove la tinta dei vertici porta l'acqua (alfa tra 0,8 e 1: DesertWorld.BrineAlpha) c'e' una pellicola di salamoia sul sale. La riva e' una soglia sul valore dei
                // vertici con un po' di rumore, cosi' e' frastagliata e non ha il profilo dei triangoli
                float wetRaw = saturate((input.tint.a - 0.8) * 5.0);
                float wet = 0.0, damp = 0.0;
                if (wetRaw > 0.001)
                {
                    float shore = wetRaw + (Fbm2(p * 0.09 + 61.0) - 0.47) * 0.25 + (VNoise(p * 0.8 + 7.0) - 0.5) * 0.08;
                    wet = smoothstep(0.44, 0.50, shore) * slopeFlat;
                    damp = smoothstep(0.10, 0.44, shore) * (1.0 - wet);               // il sale umido attorno alla pozza e' piu' scuro: la riva si legge anche da lontano
                }
                // il deserto di ciottoli: a chiazze larghe (decine di metri) la sabbia e' cosparsa di sassi scuri, a due scale (qualche centimetro e una decina); si vedono da vicino, ognuno con la sua
                // cupola sulla normale (prende la luce dal suo lato) e un'ombra di contatto tutt'attorno. Non sui pendii, non sulle saline
                float pebbleMask = max(smoothstep(0.50, 0.64, Fbm2(p * 0.011 + 71.3)), _DSPebbleBoost) * slopeFlat * (1.0 - salt);
                float pebbleFade = saturate(1.0 - dist / 34.0);
                if (pebbleMask * pebbleFade > 0.01)
                {
                    for (int sc = 0; sc < 2; sc++)
                    {
                        float2 g = p * (sc == 0 ? 6.0 : 2.0) + sc * 17.0;
                        float2 gi = floor(g), gf = frac(g);
                        float has = step(sc == 0 ? 0.30 : 0.60, H21(gi + 5.3));
                        float2 c = float2(H21(gi + 1.7), H21(gi + 9.1)) * 0.5 + 0.25;
                        float rad = lerp(0.14, 0.30, H21(gi + 3.9)) * (sc == 0 ? 1.0 : 1.2);
                        float2 dv = gf - c;
                        // un sasso non e' un disco: allungato in una direzione a caso, con il bordo che ondeggia (tre e cinque lobi), ognuno diverso dall'altro
                        float rr = H21(gi + 11.3);
                        float ang = atan2(dv.y, dv.x);
                        float wob = 1.0 + 0.12 * sin(ang * 3.0 + rr * 6.283) + 0.07 * sin(ang * 5.0 + rr * 17.0);
                        float stretch = lerp(1.0, 1.35, H21(gi + 13.1));
                        float cr = cos(rr * 6.283), sr = sin(rr * 6.283);
                        float2 dr = float2(cr * dv.x + sr * dv.y, -sr * dv.x + cr * dv.y);
                        float d2 = length(float2(dr.x / stretch, dr.y * sqrt(stretch))) / wob;
                        float body = has * (1.0 - smoothstep(rad * 0.82, rad, d2));
                        float k = pebbleMask * pebbleFade;
                        // la tinta: bruno scuro, ruggine, grigio o selce chiara (il bruno e' la meta' dei sassi); prima erano tutti quasi neri, e a sole dietro le spalle, senza ombra da una parte, si
                        // leggevano come chiazze d'olio piatte sulla sabbia
                        float tone = H21(gi + 7.7), hue = H21(gi + 19.3);
                        half3 stoneCol;
                        if (hue < 0.45) stoneCol = lerp(half3(0.16, 0.12, 0.09), half3(0.27, 0.19, 0.13), tone);
                        else if (hue < 0.72) stoneCol = lerp(half3(0.26, 0.15, 0.10), half3(0.40, 0.22, 0.13), tone);
                        else if (hue < 0.92) stoneCol = lerp(half3(0.20, 0.19, 0.18), half3(0.33, 0.31, 0.28), tone);
                        else stoneCol = lerp(half3(0.40, 0.34, 0.26), half3(0.58, 0.51, 0.40), tone);
                        float topK = saturate(1.0 - d2 / rad);
                        stoneCol *= lerp(0.78, 1.18, topK);                                           // piu' chiaro in cima, scuro al bordo: un sasso tondo, non un disco
                        sand = lerp(sand, stoneCol, body * k * 0.92);
                        sand *= 1.0 - 0.26 * has * k * smoothstep(rad * 1.45, rad * 0.95, d2) * (1.0 - body);
                        Np.xz += (dv / rad) * (topK * 0.85 * body * k);
                    }
                }
                Np = normalize(Np);
                if (wet > 0.001)
                {
                    // l'acqua e' piatta, con un'increspatura minima mossa dal vento (piu' forte da vicino) che spezza il riflesso del sole in una scia di scintille
                    float2 rq = p * 1.9 - wd * (_Time.y * 0.55);
                    float ra = lerp(0.006, 0.026, saturate(1.0 - dist / 70.0));
                    float3 Nw = normalize(float3((VNoise(rq) - 0.5) * 2.0 * ra, 1.0, (VNoise(rq + 17.3) - 0.5) * 2.0 * ra));
                    Np = normalize(lerp(Np, Nw, wet));
                }

                // roccia sui pendii ripidi (le pareti delle mesa e dei rilievi, i fianchi delle dune piu' alte): strati larghi (una decina di metri) che sfumano uno nell'altro, ondulati, dal bruno
                // all'ocra, al rosso e al crema; un filetto scuro ogni metro circa; striature verticali larghe e sottili (l'acqua che cola, la ruggine). Prima erano strati di tre metri di colori scelti
                // a caso, netti uno dall'altro: una torta a righe. La sabbia resta sui fianchi delle dune (30 gradi), la roccia comincia a 37.
                float rock = 1.0 - smoothstep(0.55, 0.80, N.y);
                half3 albedo = sand;
                if (rock > 0.002)
                {
                    float yy = P.y + (VNoise(p * 0.012) - 0.5) * 7.0 + (VNoise(p * 0.07) - 0.5) * 1.5;
                    float coarse = yy * 0.115;
                    float cI = floor(coarse), cF = frac(coarse);
                    float pick = lerp(H21(float2(cI, 7.3)), H21(float2(cI + 1.0, 7.3)), smoothstep(0.55, 1.0, cF));
                    float tp = pick * 3.0;
                    half3 pal1 = half3(0.62, 0.43, 0.28), pal2 = half3(0.55, 0.30, 0.20), pal3 = half3(0.74, 0.60, 0.44);
                    half3 layer = tp < 1.0 ? lerp(_Rock.rgb, pal1, tp) : tp < 2.0 ? lerp(pal1, pal2, tp - 1.0) : lerp(pal2, pal3, tp - 2.0);
                    layer *= 0.88 + 0.24 * H21(float2(cI, 3.1));
                    float fn = frac(yy * 1.1);
                    layer *= 0.84 + 0.16 * smoothstep(0.0, 0.16, fn) * smoothstep(1.0, 0.84, fn);
                    float2 tang = normalize(float2(-N.z, N.x) + float2(1e-4, 0.0));
                    float along = dot(p, tang);
                    layer *= 0.74 + 0.40 * VNoise(float2(along * 0.45, P.y * 0.03));
                    layer *= 0.86 + 0.26 * VNoise(float2(along * 2.6, P.y * 0.10));
                    layer *= 0.90 + 0.18 * VNoise(float2(p.x * 0.8 + p.y * 0.35, P.y * 6.0));
                    albedo = lerp(sand, layer * lerp(half3(1, 1, 1), regionMul, 0.6), rock);
                }

                albedo *= 1.0 - 0.34 * damp;
                albedo = lerp(albedo, albedo * half3(0.45, 0.52, 0.55), wet);                // il sale sotto l'acqua e' molto piu' scuro, e un po' verde-azzurro: il lago non e' una distesa bianca

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = 1;
                surfaceData.metallic = 0;
                surfaceData.specular = 0;
                surfaceData.smoothness = lerp(0.06 + 0.10 * saturate(grain * 2.0 + 0.5) * grainFade, 0.55, wet);
                surfaceData.occlusion = 1;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = 0;

                InputData inputData = (InputData)0;
                inputData.positionWS = P;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = Np;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(P);
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                inputData.fogCoord = InitializeInputDataFog(float4(P, 1.0), input.fogFactorAndVertexLight.x);
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
            #else
                inputData.fogCoord = InitializeInputDataFog(float4(P, 1.0), input.fogFactor);
            #endif
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            #if defined(_SCREEN_SPACE_IRRADIANCE)
                inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy, inputData.normalWS);
            #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                inputData.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(inputData.positionWS), inputData.normalWS, inputData.viewDirectionWS,
                                              input.positionCS.xy, input.probeOcclusion, inputData.shadowMask);
            #else
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            #endif

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                // il luccichio dei grani di quarzo: pochi puntini che prendono il sole (ogni cella minuscola ha una faccetta a caso e brilla solo quando e' a specchio tra sole e occhio);
                // si vede da vicino e dove la luce arriva (la sabbia in ombra non luccica)
                float sparkFade = saturate(1.0 - dist / 13.0) * slopeFlat * (1.0 - rock) * (1.0 - wet);
                if (sparkFade > 0.01)
                {
                    Light sun = GetMainLight(inputData.shadowCoord);
                    float2 sc = floor(p * 170.0);
                    float3 fn = normalize(float3(H21(sc) - 0.5, 0.45 + 0.55 * H21(sc + 17.3), H21(sc + 41.9) - 0.5) * float3(1.3, 1.0, 1.3));
                    float3 hv = normalize(sun.direction + inputData.viewDirectionWS);
                    float glint = pow(saturate(dot(fn, hv)), 500.0);
                    color.rgb += sun.color * (sun.shadowAttenuation * sun.distanceAttenuation * glint * 1.8 * sparkFade);
                }
                color.rgb *= CloudShade(p, dist);
                if (wet > 0.001)
                {
                    // lo specchio: il cielo riflesso con Fresnel (da vicino si vede il fondo, a bassa incidenza e' quasi tutto cielo); dopo l'ombra delle nuvole, che sul riflesso non c'entra
                    float3 Vw = inputData.viewDirectionWS;
                    // Fresnel un po' meno ripido di Schlick e mai del tutto 1 (a incidenza radente l'acqua e' quasi uno specchio, ma il fondo scuro resta un poco: senza, il lago lontano era
                    // bianco come il cielo dell'orizzonte e sembrava neve); striature del vento, che il riflesso mostra come strisce piu' chiare e piu' scure
                    float streak = 0.84 + 0.16 * VNoise(float2(dot(p, wd) * 0.018, dot(p, perpW) * 0.33) + 5.0);
                    float F = (0.03 + 0.80 * pow(1.0 - saturate(dot(Np, Vw)), 4.5)) * streak;
                    float3 Rw = reflect(-Vw, Np);
                    Rw.y = max(Rw.y, 0.004);
                    half3 mirror = MirrorSky(normalize(Rw)) * half3(0.92, 0.98, 1.0);
                    color.rgb = lerp(color.rgb, mirror, saturate(F * wet));
                }
                color.rgb = MixFog(color.rgb, inputData.fogCoord * HeightFog(P));
                color.a = 1;
                outColor = color;
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
    FallBack "Universal Render Pipeline/Lit"
}
