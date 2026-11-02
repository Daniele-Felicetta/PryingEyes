(event EventTick (DeltaSeconds)
  (Variables|Default|SetLife (- (Variables|Default|GetLife) DeltaSeconds))
  (if (<= (Variables|Default|GetLife) 0.0)
    (Actor|DestroyActor :self self)))
