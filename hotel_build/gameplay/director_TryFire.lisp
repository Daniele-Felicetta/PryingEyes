(fn TryFire ()
  (if (and (not (Variables|Default|GetGameOver)) (and (> (Variables|Default|GetAmmo) 0) (<= (Variables|Default|GetFireTimer) 0.0)))
    (Variables|Default|SetAmmo (- (Variables|Default|GetAmmo) 1))
    (Variables|Default|SetFireTimer 0.13)
    (Variables|Default|SetFlashTimer 0.06)
    (Variables|Default|SetKickPitch (+ (Variables|Default|GetKickPitch) 2.0))
    (bind cam (Game|GetPlayerCameraManager :PlayerIndex 0))
    (bind crot2 (Camera|GetCameraRotation :self cam))
    (bind start0 (Camera|GetCameraLocation :self cam))
    (bind fwd (Math|Vector|GetForwardVector :InRot crot2))
    (bind start (Math|Vector|MakeVector :X (+ (.x start0) (* (.x fwd) 60.0)) :Y (+ (.y start0) (* (.y fwd) 60.0)) :Z (+ (.z start0) (* (.z fwd) 60.0))))
    (bind end (+ start0 (Utilities|Operators|Multiply :A fwd :B 6000.0)))
    (Collision|LineTraceByChannel :Start start :End end :DrawDebugType "ForOneFrame"
      (:then
        (if _return_value
          (bind (bh bo tm ds lc ip no inn pm ha hc hbn bn hi ei fi ts te) (Collision|BreakHitResult :Hit _outhit))
          (bind e (Utilities|Casting|CastToBP_Enemy :Object ha)
            (:then
              (bind hp (Class|BPEnemy|GetHP :self e))
              (Game|Damage|ApplyDamage :DamagedActor ha :BaseDamage 34.0)
              (if (<= hp 34.0)
                (Variables|Default|SetGameScore (+ (Variables|Default|GetGameScore) (Class|BPEnemy|GetScoreValue :self e)))
                (Variables|Default|SetKills (+ (Variables|Default|GetKills) 1))
                (Development|PrintString :InString "UCCISO" :Duration 2.0)))))))))
