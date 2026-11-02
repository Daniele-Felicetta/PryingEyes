(fn TickLook (DeltaSeconds)
  ;; Mouse look via GetMousePosition delta (clamp 60px/frame, sens, pitch clamp +/-1.5)
  ;; Q/E lean (roll +/-12, smoothing 10/s), C = slide (sprint+cooldown) o crouch toggle
  ;; TestYawRate preservato per bot playtest; flag test OFF in produzione
  ;; NOTA: chiamata da TickMove come (CallFunction|TickLook :DeltaSeconds DeltaSeconds)
  (bind pctrl (Game|GetPlayerController 0))
  (bind ppc (Game|GetPlayerCharacter 0))
  (bind sprinting (or (Game|Player|IsInputKeyDown :self pctrl :Key "LeftShift")
                      (Variables|Default|GetTestAutoWalk)))
  (if (Game|Player|WasInputKeyJustPressed :self pctrl :Key "C")
    (if (and sprinting (<= (Variables|Default|GetSlideTimer) 0.0))
      (Character|LaunchCharacter :self ppc
        :LaunchVelocity (Math|Vector|MakeVector :X (* (.x (Transformation|GetActorForwardVector :self ppc)) 1300.0)
                                              :Y (* (.y (Transformation|GetActorForwardVector :self ppc)) 1300.0)
                                              :Z 150.0)
        :bXYOverride true :bZOverride true)
      (Variables|Default|SetSlideTimer 1.2))
    (else
      (if (Variables|Default|GetCrouched)
        (Character|UnCrouch :self ppc)
        (Variables|Default|SetCrouched false)
      (else
        (Character|Crouch :self ppc)
        (Variables|Default|SetCrouched true)))))
  (bind leanTgt (+ (select (Game|Player|IsInputKeyDown :self pctrl :Key "Q") -12.0 0.0)
                   (select (Game|Player|IsInputKeyDown :self pctrl :Key "E") 12.0 0.0)))
  (bind leanStep (select (> (* 10.0 DeltaSeconds) 1.0) 1.0 (* 10.0 DeltaSeconds)))
  (bind leanNew (+ (Variables|Default|GetLeanRoll)
                   (* (- leanTgt (Variables|Default|GetLeanRoll)) leanStep)))
  (Variables|Default|SetLeanRoll leanNew)
  (bind (mx my mok) (Game|Player|GetMousePosition :self pctrl))
  (bind cdx (select (> (- mx (Variables|Default|GetMouseX)) 60.0) 60.0
              (select (< (- mx (Variables|Default|GetMouseX)) -60.0) -60.0
                      (- mx (Variables|Default|GetMouseX)))))
  (bind cdy (select (> (- my (Variables|Default|GetMouseY)) 60.0) 60.0
              (select (< (- my (Variables|Default|GetMouseY)) -60.0) -60.0
                      (- my (Variables|Default|GetMouseY)))))
  (Variables|Default|SetMouseX mx)
  (Variables|Default|SetMouseY my)
  (bind trot (Pawn|GetControlRotation :self pctrl))
  (bind fyaw (+ (.yaw trot) (+ (* (Variables|Default|GetTestYawRate) DeltaSeconds)
                               (* (* cdx 0.15) (Variables|Default|GetSens)))))
  (bind fp0 (+ (.pitch trot) (* (* cdy 0.15) (Variables|Default|GetSens))))
  (bind fpitch (select (> fp0 1.5) 1.5 (select (< fp0 -1.5) -1.5 fp0)))
  (Pawn|SetControlRotation :self pctrl
    :NewRotation (Math|Rotator|MakeRotator :Roll leanNew :Pitch fpitch :Yaw fyaw)))
