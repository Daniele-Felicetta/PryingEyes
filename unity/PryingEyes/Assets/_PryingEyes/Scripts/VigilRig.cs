using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    public sealed partial class AlienRig
    {
        Material vigilMetal, vigilFire, vigilHeart;
        Transform vigilHalo, vigilIris;
        readonly Transform[] vigilFlames = new Transform[3];
        float vigilAttack;

        Vector3 Vigil()
        {
            vigilMetal = MaterialFor(new Color(0.26f, 0.18f, 0.085f), 0.38f);
            vigilMetal.SetFloat("_Metallic", 0.78f);
            vigilFire = EnemyMats.Glow(new Color(1,0.34f,0.045f),2.2f);
            vigilHeart = EnemyMats.Glow(new Color(1,0.8f,0.42f),4f);
            var mask = new MeshBuilder();
            // Una maschera concava attorno all'orbita: il foro e il bordo hanno spessore reale.
            Vector3 Face(float angle, float r)
            {
                float x = Mathf.Cos(angle) * Mathf.Lerp(0.068f, 0.167f, r);
                float y = Mathf.Sin(angle) * Mathf.Lerp(0.061f, 0.238f, r);
                x *= 1f + Mathf.Min(0, y) * 1.1f;
                float cheek = 0.036f * Mathf.Sin(r * Mathf.PI);
                float nose = 0.055f * Mathf.Exp(-x * x * 900 - (y + 0.086f) * (y + 0.086f) * 1100);
                return new Vector3(x, y + 0.025f, 0.135f + cheek + nose - r * 0.045f);
            }
            Vector3 Normal(float a, float r)
            {
                var n = Vector3.Cross((Face(a + 0.002f, r) - Face(a - 0.002f, r)).normalized,
                    (Face(a, r + 0.002f) - Face(a, r - 0.002f)).normalized).normalized;
                return n.z < 0 ? -n : n;
            }
            const int around = 48, rows = 8;
            for (int row = 0; row < rows; row++)
                for (int i = 0; i < around; i++)
                {
                    float a = i * Mathf.PI * 2 / around, b = (i + 1) * Mathf.PI * 2 / around;
                    float r = (float)row / rows, next = (row + 1f) / rows;
                    mask.QuadSmooth(Shell, Face(a,r), Face(b,r), Face(b,next), Face(a,next),
                        Normal(a,r), Normal(b,r), Normal(b,next), Normal(a,next),
                        new Vector2(a,r), new Vector2(b,r), new Vector2(b,next), new Vector2(a,next));
                    if (row == 0 || row == rows - 1)
                    {
                        float edge = row == 0 ? r : next;
                        Vector3 p = Face(a,edge), q = Face(b,edge);
                        mask.QuadW(Shell,p,q,q-Vector3.forward*0.065f,p-Vector3.forward*0.065f,
                            row == 0 ? new Vector3(-p.x,-p.y,0) : new Vector3(p.x,p.y,0));
                    }
                }
            // Labbro rotto e colature sottili sotto il mento, senza denti triangolari sul velo.
            for (int i = 0; i < 8; i++)
            {
                float x = -0.055f + i * 0.014f;
                mask.Beam(membrane, new Vector3(x,-0.121f+0.008f*Mathf.Cos(i),0.153f),
                    new Vector3(x+0.014f,-0.121f+0.008f*Mathf.Cos(i+1),0.153f),0.006f,0.008f,Vector3.forward);
            }
            for (int i = 0; i < 7; i++)
            {
                float x = (i-3)*0.032f, y = -0.17f - 0.03f*Mathf.Cos(i*0.7f);
                float length = 0.045f + 0.08f * (0.5f+0.5f*Mathf.Sin(i*2.1f));
                Turn(mask,Shell,new Vector3(x,y,0.09f),new[] {new Vector2(0.014f,0),new Vector2(0.011f,-length),new Vector2(0.004f,-length-0.014f)},12);
            }
            RelicPart(model,"Maschera votiva di cera",mask);
            Piece(model,new Vector3(0,0.025f,0.06f),new Vector3(0.135f,0.12f,0.11f),membrane);
            var iris = new MeshBuilder();
            Ring(iris,Glow,Vector3.zero,0.031f,0.008f);
            vigilIris = RelicPart(model,"Iride della Veglia",iris);
            vigilIris.localPosition = new Vector3(0,0.025f,0.123f);

            var halo = new MeshBuilder();
            Ring(halo,vigilMetal,new Vector3(0,0,0),0.268f,0.014f);
            Ring(halo,vigilMetal,new Vector3(0,0,0),0.292f,0.007f);
            for (int i = 0; i < 16; i++)
            {
                float a = i*Mathf.PI*2/16;
                var radial = new Vector3(Mathf.Sin(a),Mathf.Cos(a),0);
                halo.Beam(vigilMetal,radial*0.27f,radial*(i%2==0?0.345f:0.318f),0.012f,0.009f,Vector3.forward);
            }
            vigilHalo = RelicPart(model,"Aureola in ottone brunito",halo);
            vigilHalo.localPosition = new Vector3(0,0.035f,-0.045f);
            vigilHalo.localScale = new Vector3(1,1.19f,1);

            for (int i = 0; i < 3; i++)
            {
                float x = (i-1)*0.225f, bottom = i==1?0.29f:0.13f, height = i==0?0.19f:i==1?0.12f:0.12f;
                var candle = new MeshBuilder();
                Turn(candle,vigilMetal,new Vector3(x,bottom,0),new[] {new Vector2(0.023f,-0.05f),new Vector2(0.05f,-0.018f),new Vector2(0.049f,0),new Vector2(0.033f,0.018f)},20);
                Turn(candle,Shell,new Vector3(x,bottom,0),new[] {new Vector2(0.024f,0),new Vector2(0.026f,height-0.018f),new Vector2(0.019f,height),new Vector2(0.009f,height-0.008f)},20);
                candle.Beam(membrane,new Vector3(x,bottom+height-0.006f,0),new Vector3(x,bottom+height+0.025f,0),0.005f,0.005f,Vector3.forward);
                RelicPart(model,"Cero votivo "+i,candle);
                var flame = new MeshBuilder();
                Turn(flame,vigilFire,Vector3.zero,new[] {new Vector2(0.003f,0),new Vector2(0.014f,0.012f),new Vector2(0.018f,0.035f),new Vector2(0.013f,0.061f),new Vector2(0.006f,0.09f),new Vector2(0.001f,0.112f)},16);
                Turn(flame,vigilHeart,new Vector3(0,0.004f,0.006f),new[] {new Vector2(0.002f,0),new Vector2(0.009f,0.015f),new Vector2(0.005f,0.039f),new Vector2(0.001f,0.06f)},16);
                vigilFlames[i] = RelicPart(model,"Fiamma "+i,flame);
                vigilFlames[i].localPosition = new Vector3(x,bottom+height+0.015f,0);
            }
            for (int panel = 0; panel < 3; panel++)
            {
                int panelId = panel;
                var cloth = new MeshBuilder();
                Vector3 P(float u,float v)
                {
                    float t=v/14, a=(55+panelId*84+u*80/24)*Mathf.Deg2Rad;
                    float radius=0.207f+0.075f*Mathf.Sin(t*Mathf.PI)-0.025f*t;
                    radius+=0.018f*Mathf.Cos(u*Mathf.PI/3)*(0.25f+t);
                    return new Vector3(Mathf.Sin(a)*radius+0.05f*t*t*Mathf.Sin(panelId*2),
                        0.27f-t*0.94f+t*t*0.025f*Mathf.Sin(u*0.65f+panelId),Mathf.Cos(a)*radius-0.13f*t*t);
                }
                Vector3 N(float u,float v)
                {
                    var n=Vector3.Cross((P(u+0.01f,v)-P(u-0.01f,v)).normalized,(P(u,v+0.01f)-P(u,v-0.01f)).normalized).normalized;
                    var p=P(u,v);return Vector3.Dot(n,new Vector3(p.x,0,p.z))<0?-n:n;
                }
                for (int v=0;v<14;v++)
                    for (int u=0;u<24;u++)
                    {
                        cloth.QuadSmooth(membrane,P(u,v),P(u+1,v),P(u+1,v+1),P(u,v+1),N(u,v),N(u+1,v),N(u+1,v+1),N(u,v+1),
                            new Vector2(u*0.012f,v*0.025f),new Vector2((u+1)*0.012f,v*0.025f),new Vector2((u+1)*0.012f,(v+1)*0.025f),new Vector2(u*0.012f,(v+1)*0.025f));
                        if(u==0||u==23)cloth.Beam(vigilMetal,P(u==0?0:24,v),P(u==0?0:24,v+1),0.003f,0.003f,Vector3.forward);
                    }
                var veil=Limb(Vector3.zero,Vector3.forward,panel*1.9f,panel==1?2:5);
                RelicPart(veil,"Velo funebre "+panel,cloth);
            }
            return new Vector3(0,0.025f,0.123f);
        }

        void AnimateVigil(float t,bool striking,float flightTime = -1)
        {
            vigilAttack=flightTime>=0?FlightTension(flightTime):Mathf.MoveTowards(vigilAttack,striking?1:0,Time.deltaTime*(striking?3.6f:6));
            model.localRotation=Quaternion.Euler((flightTime>=0?FlightPitch(flightTime):-vigilAttack*16)+Mathf.Sin(t*0.5f)*2,0,Mathf.Sin(t*0.7f)*2);
            vigilHalo.localRotation=Quaternion.Euler(0,Mathf.Sin(t*0.3f)*5,vigilAttack*12);
            vigilIris.localScale=Vector3.one*(1+vigilAttack*0.45f);
            for(int i=0;i<vigilFlames.Length;i++)
            {
                float flutter=1+0.09f*Mathf.Sin(Time.time*17+i*1.8f);
                vigilFlames[i].localScale=new Vector3(1-vigilAttack*0.18f,flutter+vigilAttack*0.7f,1);
                vigilFlames[i].localRotation=Quaternion.Euler(Mathf.Sin(t+i)*4,0,Mathf.Sin(Time.time*11+i)*5);
            }
        }
    }
}
