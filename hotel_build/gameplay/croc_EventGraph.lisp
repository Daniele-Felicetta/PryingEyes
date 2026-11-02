(event EventTick (DeltaSeconds)
  (Variables|Default|SetPhase (+ (Variables|Default|GetPhase) (* DeltaSeconds 1.5)))
  (bind jaw (Variables|Default|GetJaw))
  (Utilities|IsValid jaw
    (:"Is Valid"
      (bind r (Transformation|GetActorRotation :self jaw))
      (Transformation|SetActorRotation :self jaw :NewRotation (Math|Rotator|MakeRotator :Roll (.roll r) :Pitch (+ -8.0 (* (Math|Trig|Sin(Radians) :A (Variables|Default|GetPhase)) 10.0)) :Yaw (.yaw r))))
    (:"Is Not Valid")))
