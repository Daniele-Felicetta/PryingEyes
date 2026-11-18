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
            float4 _DSParams;       // x: stelle (0..1), y: tempo, z: crepuscolo (0..1)

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

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                // il gradiente dall'orizzonte allo zenit; sotto l'orizzonte la nebbia
                half3 col = lerp(_DSHorizon.rgb, _DSZenith.rgb, pow(saturate(h), 0.42));
                col = lerp(col, _DSFog.rgb, saturate(-h * 6.0));

                // il sole: un alone largo, uno stretto e il disco (HDR: il bagliore lo prende)
                float sd = dot(d, _DSSunDir.xyz);
                float glow = pow(saturate(sd), 5.0) * 0.28 + pow(saturate(sd), 48.0) * 0.55;
                float disc = smoothstep(0.99965, 0.99985, sd);
                col += _DSSunColor.rgb * (glow * _DSSunDir.w + disc * 14.0 * _DSSunDir.w);

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

                // le nuvole: un velo di rumore su un piano alto che scorre piano
                if (h > 0.01)
                {
                    float2 cuv = d.xz / (h + 0.16);
                    float n = Fbm3(float3(cuv * 0.85 + float2(_DSParams.y * 0.004, 0.0), 3.0));
                    float cover = smoothstep(1.0 - _DSCloud.a, 1.0 - _DSCloud.a + 0.22, n) * saturate(h * 5.0);
                    half3 cloud = _DSCloud.rgb * (0.78 + 0.22 * n) + _DSSunColor.rgb * pow(saturate(sd), 6.0) * 0.5 * _DSSunDir.w;
                    col = lerp(col, cloud, cover * 0.85);
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
