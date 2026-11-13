using UnityEngine;

namespace PryingEyes
{
    public partial class Enemy
    {
        public const float FlightWindup = 0.55f, FlightContact = 0.71f, FlightDuration = 1.13f;
        const float FlightTravel = 0.85f, FlightReach = 1.2f;
        Vector3 flightDirection;
        float flightDistance;
        public bool PreparingFlight => IsFlying(type) && strikeAt >= 0;
        public bool RecoveringFlight => IsFlying(type) && atkLock > 0 && strikeAt < 0;
        float FlightElapsed => IsFlying(type) && atkLock > 0 ? FlightDuration - atkLock : -1;

        static Vector3 FlightChest(ITarget target) => target.LeanedPoint(Mathf.Min(1.3f,target.BodyHeight*0.65f));

        void BeginFlight()
        {
            meleeVictim = tgt;
            Vector3 delta = FlightChest(tgt)-transform.position;
            flightDirection = delta.normalized;
            flightDistance = Mathf.Min(FlightTravel,Mathf.Max(0,delta.magnitude-0.75f));
            if(flightDirection.sqrMagnitude<0.01f)flightDirection=transform.forward;
            transform.rotation=Quaternion.LookRotation(flightDirection,Vector3.up);
            strikeAt=FlightContact;atkLock=FlightDuration;vel=Vector3.zero;
            atkCd=type==EnemyType.Veglia?1.5f:type==EnemyType.Medusa?2.6f:1.8f;
            FlightAnticipation();
            Coop.Game?.EnemyEvent(this,EnemyEvt.Attack,flightDirection);
        }

        void FlightAnticipation()
        {
            if(eye!=null){eye.lookAt=null;eye.Stare(FlightContact);}
            Sfx.At(type==EnemyType.Veglia?SoundBank.Whisper:SoundBank.EyeStare,transform.position,0.55f,basePitch,2,22);
        }

        // L'affondo ha una traiettoria impegnata: niente inseguimento né scorrimento laterale sul muro.
        void UpdateFlight(float dt)
        {
            float before=FlightDuration-atkLock;
            atkLock=Mathf.Max(0,atkLock-dt);
            float after=FlightDuration-atkLock;
            float Curve(float t)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(FlightWindup,FlightContact,t));
            Vector3 start=transform.position;
            MoveFlying(flightDirection*(flightDistance*(Curve(after)-Curve(before))),false);
            vel=dt>0?(transform.position-start)/dt:Vector3.zero;
            if(eye!=null)eye.lookAt=null;
            if(strikeAt>=0)
            {
                strikeAt-=dt;
                if(strikeAt<=0){strikeAt=-1;ResolveFlight();}
            }
        }

        void ResolveFlight()
        {
            var victim=meleeVictim;
            Vector3 contact=victim!=null?FlightChest(victim):transform.position+flightDirection*FlightReach;
            Vector3 delta=contact-transform.position;
            bool hit=victim!=null&&victim.Alive&&delta.sqrMagnitude<FlightReach*FlightReach&&
                (delta.sqrMagnitude<0.01f||Vector3.Dot(flightDirection,delta.normalized)>=0.57f)&&
                HotelNav.ClearLine(transform.position,contact);
            if(hit)
            {
                victim.Hurt((type==EnemyType.Medusa?15:8)+Random.value*4,transform.position);
                if(victim is LocalTarget)GameManager.damageTaken++;
            }
            Vector3 effect=hit?MeleeContactPoint(victim):contact;
            PlayMeleeContact(effect,hit);
            Coop.Game?.EnemyEvent(this,EnemyEvt.Strike,effect,hit?1:0);
            if(GameManager.autoTest)Debug.Log($"[PryingEyes] affondo {type}: {(hit?"contatto":"a vuoto")} distanza={delta.magnitude:F2}");
            meleeVictim=null;
        }

        void MoveFlying(Vector3 step,bool slide)
        {
            Vector3 position=transform.position;
            float length=step.magnitude;
            const float radius=0.42f;
            if(length>1e-4f&&Physics.SphereCast(position,radius,step/length,out var hit,length+0.05f,HotelNav.EnvMask,QueryTriggerInteraction.Ignore))
            {
                if(slide)
                {
                    vel=Vector3.ProjectOnPlane(vel,hit.normal);
                    step=Vector3.ProjectOnPlane(step,hit.normal);length=step.magnitude;
                    if(length>1e-4f&&Physics.SphereCast(position,radius,step/length,out hit,length+0.05f,HotelNav.EnvMask,QueryTriggerInteraction.Ignore))
                        step=step/length*Mathf.Max(0,hit.distance-0.02f);
                }
                else step=step/length*Mathf.Max(0,hit.distance-0.02f);
            }
            position+=step;
            position.y=Mathf.Clamp(position.y,0.95f,FloorDirectorCeiling(position));
            transform.position=position;
        }
    }
}
