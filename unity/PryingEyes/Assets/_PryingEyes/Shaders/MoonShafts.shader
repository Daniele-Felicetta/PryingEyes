Shader "PryingEyes/MoonShafts"
{
    // Lame di luna volumetriche. Il mesh e' un cubo che contiene tutte le lame: per ogni pixel si cammina lungo
    // il raggio dentro il cubo e ogni campione legge la shadow map della luna. La forma delle lame quindi la
    // decide la scena: apertura del lucernario, sbarre del telaio, ringhiere, colonne, chi passa sotto.
    Properties
    {
        _Color ("Colore", Color) = (0.62, 0.74, 0.95, 1)
        _Density ("Densita", Float) = 0.035
        _Steps ("Passi", Range(8, 64)) = 28
        [NoScaleOffset] _NoiseTex ("Rumore 3D", 3D) = "white" {}
        _NoiseScale ("Scala rumore", Float) = 0.22
        _NoiseAmount ("Quantita rumore", Range(0, 1)) = 0.65
        _Drift ("Deriva (m/s)", Vector) = (0.06, -0.015, 0.035, 0)
        _Anisotropy ("Anisotropia", Range(0, 0.9)) = 0.35
        _MaxBrightness ("Luminosita massima", Float) = 1.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "MoonShafts"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest Always   // la profondita' la confronta lo shader: serve anche il tratto davanti a cio' che copre il cubo
            Cull Front     // facce posteriori: funziona anche con la camera dentro il volume

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Density;
                float _Steps;
                float _NoiseScale;
                float _NoiseAmount;
                float4 _Drift;
                float _Anisotropy;
                float _MaxBrightness;
            CBUFFER_END
            TEXTURE3D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 camWS = GetCameraPositionWS();
                float3 rd = i.positionWS - camWS;                 // non normalizzato: t = 1 sulla faccia posteriore

                // ingresso/uscita dal cubo [-0.5, 0.5]^3 nello spazio dell'oggetto (affine: t resta lo stesso)
                float3 ro = TransformWorldToObject(camWS);
                float3 rdo = TransformWorldToObjectDir(rd, false);
                float3 inv = rcp(rdo + 1e-7);
                float3 ta = (-0.5 - ro) * inv;
                float3 tb = (0.5 - ro) * inv;
                float3 tmin = min(ta, tb);
                float3 tmax = max(ta, tb);
                float tEnter = max(max(tmin.x, tmin.y), max(tmin.z, 0.0));
                float tExit = min(min(tmax.x, tmax.y), tmax.z);

                // ci si ferma sulla prima superficie opaca
                float2 uv = i.positionCS.xy / _ScaledScreenParams.xy;
                float eye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float3 fwd = -UNITY_MATRIX_V[2].xyz;
                tExit = min(tExit, eye / max(dot(rd, fwd), 1e-5));
                if (tExit <= tEnter) return 0;

                Light ml = GetMainLight();
                float3 dirN = normalize(rd);
                // Henyey-Greenstein (vale 1 se isotropo): piu' luce guardando verso la luna
                float g = _Anisotropy;
                float cosT = dot(dirN, ml.direction);
                float phase = (1.0 - g * g) / pow(abs(1.0 + g * g - 2.0 * g * cosT), 1.5);

                int n = (int)_Steps;
                float dt = (tExit - tEnter) / n;
                float jitter = InterleavedGradientNoise(i.positionCS.xy, 0);
                float acc = 0.0;
                [loop] for (int k = 0; k < n; k++)
                {
                    float3 p = camWS + rd * (tEnter + (k + jitter) * dt);
                    half sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
                    float nz = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, p * _NoiseScale + _Drift.xyz * _Time.y, 0).r;
                    acc += sh * lerp(1.0, nz * 2.0, _NoiseAmount);
                }
                acc *= dt * length(rd) * _Density;                // dt * |rd| = passo in metri

                half3 col = _Color.rgb * ml.color * (acc * phase);
                return half4(min(col, _MaxBrightness), 0);
            }
            ENDHLSL
        }
    }
}
