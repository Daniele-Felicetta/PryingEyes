(fn TakeHit (Damage)
  (Variables|Default|SetHP (- (Variables|Default|GetHP) Damage))
  (if (<= (Variables|Default|GetHP) 0.0)
    (Actor|DestroyActor :self self)))
