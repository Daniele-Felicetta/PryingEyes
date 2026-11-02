(event EventBeginPlay
  (bind _outactors (Actor|GetAllActorsOfClass "/Game/Hotel/Gameplay/BP_GameDirector.BP_GameDirector_C"))
  (if (> (Utilities|Array|Length _outactors) 0)
    (Variables|Default|SetManager (Utilities|Array|Get(acopy) _outactors))))

(event Game|Damage|EventAnyDamage (Damage DamageType InstigatedBy DamageCauser)
  (CallFunction|TakeHit :Damage Damage))

(event EventTick (DeltaSeconds)
  (bind pc (Game|GetPlayerCharacter :PlayerIndex 0))
  (Utilities|IsValid pc
    (:"Is Valid"
      (bind myLoc (Transformation|GetActorLocation :self self))
      (bind plLoc (Transformation|GetActorLocation :self pc))
      (bind dist (Math|Vector|Distance(Vector) :V1 myLoc :V2 plLoc))
      (bind d (Utilities|Operators|Subtract :A plLoc :B myLoc))
      (bind flat (Math|Vector|MakeVector :X (.x d) :Y (.y d) :Z (select (Variables|Default|GetFlying) (.z d) 0.0)))
      (bind dir (Math|Vector|Normalize :A flat))
      (bind slack (- dist 140.0))
      (bind want (* (Variables|Default|GetSpeed) DeltaSeconds))
      (bind len (select (> slack 0.0) (select (> want slack) slack want) 0.0))
      (bind step (Utilities|Operators|Multiply :A dir :B len))
      (Transformation|AddActorWorldOffset :self self :DeltaLocation step :bSweep false)
      (Variables|Default|SetAttackTimer (- (Variables|Default|GetAttackTimer) DeltaSeconds))
      (if (and (< dist 170.0) (<= (Variables|Default|GetAttackTimer) 0.0))
        (Variables|Default|SetAttackTimer 1.6)
        (Game|Damage|ApplyDamage :DamagedActor (Variables|Default|GetManager) :BaseDamage (Variables|Default|GetAttackDamage))))
    (:"Is Not Valid")))
