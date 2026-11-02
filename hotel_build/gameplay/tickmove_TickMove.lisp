(fn TickMove (DeltaSeconds)
  ;; Perf/scalability once + mouse init (EventGraph non riscrivibile: init qui)
  ;; PerfApplied(bool, default false), MouseX/MouseY(float), Sens(float)
  (if (not (Variables|Default|GetPerfApplied))
    (Variables|Default|SetPerfApplied true)
    (Variables|Default|SetMouseX -1.0)
    (Variables|Default|SetMouseY -1.0)
    (Variables|Default|SetSens 1.0)
    (Development|ExecuteConsoleCommand "sg.ResolutionQuality 70")
    (Development|ExecuteConsoleCommand "sg.ShadowQuality 1")
    (Development|ExecuteConsoleCommand "sg.PostProcessQuality 1")
    (Development|ExecuteConsoleCommand "r.VolumetricFog 0"))
  ;; Sprint (LeftShift o bot) -> max speed
  ;; WASD diretto via IsInputKeyDown (bypassa IMC vuoti), relativo al control rotation
  ;; SpaceBar = jump
  ;; Chiama TickLook con sintassi NAMED (:DeltaSeconds) - quella posizionale fallisce
  (CallFunction|TickLook :DeltaSeconds DeltaSeconds))
