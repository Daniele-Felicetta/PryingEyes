// Editor only (it lives in an Editor folder, so no build includes it): draws a cubemap as a 2:1 latitude-longitude image.
// UnrealExport uses it to hand the scene's reflection cube to the Unreal prototype. Longitude 0 looks along +z, the top row
// is straight up. Nothing in the game uses this.
Shader "Hidden/PryingEyes/Equirect"
{
    Properties { _Cube ("Cube", CUBE) = "" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _Cube;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float lon = (i.uv.x - 0.5) * 6.2831853;
                float lat = (i.uv.y - 0.5) * 3.1415927;      // uv.y grows upwards in the target; the encoder writes the top row first
                float3 d = float3(cos(lat) * sin(lon), sin(lat), cos(lat) * cos(lon));
                return float4(texCUBElod(_Cube, float4(d, 0)).rgb, 1);      // alpha 1: an importer may premultiply
            }
            ENDCG
        }
    }
}
