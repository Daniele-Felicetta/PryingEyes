using UnityEngine;

namespace PryingEyes.Proc
{
    public static partial class FloorBuilder
    {
        // Tre quote: due U contrapposte e quattro scale. La corsia interna del primo anello resta libera sotto la seconda rampa.
        static void BuildAtrium(FloorInstance fi,Room room,GameObject parent)
        {
            float left=room.x0*0.1f+2, end=room.x1*0.1f-1.2f;
            float middle=left+7.2f, secondBase=end-1.4f, upper=secondBase-7.2f;
            float south=room.z0*0.1f+2,north=room.z1*0.1f-7;
            const float wide=5,stairWidth=2.4f,h=GalleryHeight;
            float southCenter=south+1.2f,northCenter=north+3.8f;
            var mb=new MeshBuilder();var stone=Mat("mur_pietra");var floor=Mat("pav_parquet");var iron=Mat("ferro");
            void Slab(float x0,float x1,float z0,float z1,float y)
            {
                mb.Box(stone,new Vector3((x0+x1)*0.5f,y-0.14f,(z0+z1)*0.5f),new Vector3(x1-x0,0.26f,z1-z0));
                mb.QuadW(floor,new Vector3(x0,y,z0),new Vector3(x0,y,z1),new Vector3(x1,y,z1),new Vector3(x1,y,z0),Vector3.up);
            }
            void Rail(Vector3 a,Vector3 b)
            {
                mb.Beam(iron,a+Vector3.up*1.02f,b+Vector3.up*1.02f,0.065f,0.065f,Vector3.up);
                mb.Beam(iron,a+Vector3.up*0.44f,b+Vector3.up*0.44f,0.03f,0.03f,Vector3.up);
                int posts=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/1.2f));
                for(int i=0;i<=posts;i++)mb.Box(iron,Vector3.Lerp(a,b,(float)i/posts)+Vector3.up*0.5f,new Vector3(0.045f,1.04f,0.045f));
            }
            void Stair(float start,float direction,float z,float y)
            {
                for(int i=0;i<18;i++)
                {
                    float rise=(i+1)*0.2f;
                    mb.Box(stone,new Vector3(start+direction*(i+0.5f)*0.4f,y+rise*0.5f,z+1.2f),new Vector3(0.4f,rise,stairWidth));
                }
                Rail(new Vector3(start,y,z),new Vector3(start+direction*7.2f,y+h,z));
                Rail(new Vector3(start,y,z+stairWidth),new Vector3(start+direction*7.2f,y+h,z+stairWidth));
            }
            Slab(middle,end,south,south+wide,h);Slab(middle,end,north,north+wide,h);
            Slab(end-stairWidth,end,south+wide,north,h);
            Slab(left,upper,south,south+stairWidth,h*2);Slab(left,upper,north+2.6f,north+wide,h*2);
            Slab(left,left+stairWidth,south+stairWidth,north+2.6f,h*2);
            foreach(float z in new[]{south,north+2.6f}){Stair(left,1,z,0);Stair(secondBase,-1,z,h);}
            Rail(new Vector3(middle,h,south+wide),new Vector3(end-stairWidth,h,south+wide));
            Rail(new Vector3(middle,h,north),new Vector3(end-stairWidth,h,north));
            Rail(new Vector3(end-stairWidth,h,south+wide),new Vector3(end-stairWidth,h,north));
            Rail(new Vector3(end,h,south),new Vector3(end,h,north+wide));
            Rail(new Vector3(middle,h,south),new Vector3(upper,h,south));
            Rail(new Vector3(middle,h,north+wide),new Vector3(upper,h,north+wide));
            Rail(new Vector3(secondBase,h,south),new Vector3(end,h,south));
            Rail(new Vector3(secondBase,h,north+wide),new Vector3(end,h,north+wide));
            Rail(new Vector3(left,h*2,south),new Vector3(upper,h*2,south));
            Rail(new Vector3(left,h*2,north+wide),new Vector3(upper,h*2,north+wide));
            Rail(new Vector3(left,h*2,south),new Vector3(left,h*2,north+wide));
            Rail(new Vector3(left+stairWidth,h*2,south+stairWidth),new Vector3(upper,h*2,south+stairWidth));
            Rail(new Vector3(left+stairWidth,h*2,north+2.6f),new Vector3(upper,h*2,north+2.6f));
            Rail(new Vector3(left+stairWidth,h*2,south+stairWidth),new Vector3(left+stairWidth,h*2,north+2.6f));
            foreach(float x in new[]{middle+0.3f,end-0.3f})
                foreach(float z in new[]{south+0.3f,north+wide-0.3f})
                    mb.Box(stone,new Vector3(x,h*0.5f-0.15f,z),new Vector3(0.35f,h-0.3f,0.35f));
            foreach(float x in new[]{left+0.3f,upper-0.3f})
                foreach(float z in new[]{south+0.3f,north+wide-0.3f})
                    mb.Box(stone,new Vector3(x,h-0.15f,z),new Vector3(0.35f,h*2-0.3f,0.35f));
            Flush(parent,"Atrio su tre piani",mb,true,null);
            var coverRoot=new GameObject("Coperture balconata");coverRoot.transform.SetParent(parent.transform,false);
            fi.propRoots.Add(coverRoot.transform);
            var covers=new MeshBuilder();
            for(float x=left+4;x<upper-2.5f;x+=5.5f)
                foreach(float z in new[]{south+2.1f,north+2.9f})
                {
                    covers.Box(stone,new Vector3(x,h*2+0.43f,z),new Vector3(1.6f,0.86f,0.6f));
                    covers.Box(floor,new Vector3(x,h*2+0.89f,z),new Vector3(1.7f,0.08f,0.65f));
                }
            Flush(coverRoot,"Parapetti di riparo",covers,true,null);
            Vector3 P(float x,float y,float z)=>new Vector3(x,y+0.05f,z);
            var midRoute=new[]{P(left-0.8f,0,southCenter),P(middle+0.6f,h,southCenter),P(middle+0.6f,h,south+3.7f),
                P(end-0.6f,h,south+3.7f),P(end-0.6f,h,north+1.3f),P(middle+0.6f,h,north+1.3f),P(middle+0.6f,h,northCenter),P(left-0.8f,0,northCenter)};
            var highRoute=new[]{P(left-0.8f,0,southCenter),P(middle+0.6f,h,southCenter),P(middle+0.6f,h,south+3.7f),
                P(end-0.6f,h,south+3.7f),P(end-0.6f,h,southCenter),P(upper-0.6f,h*2,southCenter),P(left+1.2f,h*2,southCenter),
                P(left+1.2f,h*2,northCenter),P(upper-0.6f,h*2,northCenter),P(end-0.6f,h,northCenter),
                P(end-0.6f,h,north+1.3f),P(middle+0.6f,h,north+1.3f),P(middle+0.6f,h,northCenter),P(left-0.8f,0,northCenter)};
            fi.galleries.Add(new GalleryRoute{room=room,points=midRoute});fi.galleries.Add(new GalleryRoute{room=room,points=highRoute});
            fi.groundSpawns.Add(midRoute[3]);fi.groundSpawns.Add(midRoute[4]);fi.groundSpawns.Add(highRoute[6]);fi.groundSpawns.Add(highRoute[7]);
            foreach(float level in new[]{h,h*2})foreach(float z in new[]{southCenter,northCenter})
            {
                var lamp=new GameObject("Luce atrio");lamp.transform.SetParent(parent.transform,false);
                lamp.transform.position=new Vector3((left+end)*0.5f,level+1.3f,z);
                var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.range=13;light.intensity=8f;
                light.color=new Color(1,0.78f,0.52f);LightPool.Register(light,false,true);
            }
        }
    }
}
