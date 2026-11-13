using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Proc
{
    /// <summary>Le ondate della Torre (SurvivalMode): partono da sole a intervalli, nascono solo nelle aree gia' aperte coi punti, sempre
    /// all'attacco e piu' veloci. Tre ondate superate su un piano aprono l'ascensore; si puo' restare a combattere o scendere. Se muore
    /// chi gioca (o tutti, in cooperativa) l'ondata salta: nemici via, il numero non conta, la prossima parte dopo qualche secondo.</summary>
    public partial class RoundDirector
    {
        /// <summary>Secondi prima della prima ondata di un piano, e fra un'ondata e la successiva (per rifornirsi e comprare porte).</summary>
        public const float FirstWaveDelay = 8f, WaveBreak = 10f;
        /// <summary>Ondate superate in un piano per aprire l'ascensore.</summary>
        public const int WavesForExit = 3;
        /// <summary>Senza nascite ne' morti, a nascite finite, per tanti secondi i nemici rimasti sono incastrati: via loro, l'ondata finisce senza premio.</summary>
        const float StallLimit = 120f;
        /// <summary>Tick di nascite andati a vuoto, con nessun nemico vivo, oltre i quali l'ondata si chiude con quelli gia' nati.</summary>
        const int SpawnGiveUp = 80;

        float nextWave;
        int floorWaves;
        float stallT;           // secondi di stallo (vedi StallLimit)
        int lastAlive;          // nemici vivi all'ultimo controllo: se cambia, lo stallo riparte
        bool teamDown;          // tutti a terra: le ondate aspettano
        public int FloorWaves => floorWaves;
        public float NextWave => nextWave;
        readonly NavMeshPath spawnPath = new NavMeshPath();

        void ArmSurvival()
        {
            floorWaves = 0; nextWave = FirstWaveDelay; stallT = 0f; lastAlive = 0; teamDown = false;
        }

        /// <summary>Per le prove: la prossima ondata parte tra 'seconds' secondi invece che al suo turno.</summary>
        public void SurvivalNextWaveIn(float seconds) { if (phase == Phase.Idle) nextWave = Mathf.Max(0f, seconds); }

        /// <summary>Quanti nemici l'ondata in corso deve far nascere in tutto, e quanti insieme al massimo.</summary>
        public static int SurvivalTarget(int round, int players) => Mathf.Clamp(16 + round * 4 + (players - 1) * 8, 20, 100);
        public static int SurvivalCap(int round, int players) => Mathf.Clamp(12 + round + (players - 1) * 3, 14, 28);

        void BeginSurvivalWave()
        {
            Round++; moth = RoundRules.IsMoth(Round); mood = RoundMood.Normal;
            target = Mathf.Min(100, Mathf.RoundToInt(SurvivalTarget(Round, Targets.Count) * (moth ? RoundRules.MothCountMul : 1f)));
            cap = SurvivalCap(Round, Targets.Count) + (moth ? RoundRules.MothAliveBonus : 0);
            spawned = fails = 0; clock = spawnT = endT = statusT = 0; stallT = 0f; lastAlive = 0;
            gap = Mathf.Max(0.28f, 0.65f - Round * 0.018f); delay = 0;
            phase = Phase.Fighting;
            ShowSurvivalWave();
            Debug.Log($"[PryingEyes] ondata {Round} (piano {FloorDirector.I.floor}): {target} nemici, {cap} insieme, aree aperte {RoomAccessDoors.I?.Access.OpenRooms.Count}{(moth ? ", Processione" : "")}");
            Coop.Game?.RoundStarted(0, Round, moth, (int)mood);
        }

        void ShowSurvivalWave()
        {
            GameManager.I.hud?.SetRound(Round);
            GameManager.I.hud?.Banner(moth ? "PROCESSIONE DEI LUMI " + Round : "ONDATA " + Round, moth ? "Le candele hanno lasciato i loro altari." : "Muoviti. Tieni libera una via di fuga.");
            Sfx.UI(SoundBank.Toll, 0.6f, 0.65f);
            if (moth) Sfx.UI(SoundBank.LumiProcession, 0.7f);
        }

        /// <summary>Un'area aperta a caso, pesata sulla superficie (un atrio vale piu' di uno sgabuzzino); mai un ascensore.</summary>
        Room PickOpenRoom(RoomAccessState access)
        {
            float total = 0f;
            Room last = null;
            foreach (var r in fi.plan.rooms)
                if (!r.IsLift && access.IsOpen(r.id)) { total += r.W * r.D; last = r; }
            if (last == null) return null;
            float pick = Random.value * total;
            foreach (var r in fi.plan.rooms)
            {
                if (r.IsLift || !access.IsOpen(r.id)) continue;
                pick -= r.W * r.D;
                if (pick <= 0f) return r;
            }
            return last;
        }

        /// <summary>Fa nascere un nemico in un'area aperta, lontano da chi gioca ma raggiungibile a piedi (la navmesh senza le porte chiuse) in
        /// pochi secondi. 'relax' allarga via via i limiti quando i punti buoni finiscono (0 = lontano e fuori vista, 2 = ovunque si arrivi).</summary>
        bool SpawnSurvivalEnemy(int relax)
        {
            var access = RoomAccessDoors.I;
            if (access == null) return false;
            float dMin = relax == 0 ? 7f : relax == 1 ? 5f : 3.5f;
            float dMax = relax == 0 ? 28f : relax == 1 ? 38f : 70f;
            float pathMax = relax == 0 ? 38f : relax == 1 ? 55f : 100f;
            int unseenTries = relax == 0 ? 40 : relax == 1 ? 24 : 0;
            for (int attempt = 0; attempt < 72; attempt++)
            {
                var room = PickOpenRoom(access.Access);
                if (room == null) return false;
                var q = new Vector3(Random.Range(room.x0 * 0.1f + 1, room.x1 * 0.1f - 1), 0, Random.Range(room.z0 * 0.1f + 1, room.z1 * 0.1f - 1));
                // Use the nearby player's actual floor: balcony combat must not leave a whole wave downstairs.
                var nearest = Targets.Nearest(q);
                if (nearest == null) continue;
                if (room.upperGallery && Random.value < 0.65f) q.y = nearest.Feet.y;
                if (!NavMesh.SamplePosition(q, out var hit, 2f, HotelNav.EnemyMask)) continue;
                q = hit.position;
                var owner = fi.RoomAt(q);
                if (owner == null || !access.Access.IsOpen(owner.id)) continue;
                float distance = Targets.NearestDistance(q);
                if (distance < dMin || distance > dMax) continue;
                nearest = Targets.Nearest(q);
                if (!NavMesh.SamplePosition(nearest.Feet, out var dest, 2, HotelNav.EnemyMask) ||
                    !NavMesh.CalculatePath(q, dest.position, HotelNav.EnemyMask, spawnPath) || spawnPath.status != NavMeshPathStatus.PathComplete) continue;
                float length = 0; var corners = spawnPath.corners;
                for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i-1], corners[i]);
                if (length > pathMax || (attempt < unseenTries && Targets.AnySees(q + Vector3.up))) continue;
                var type = moth ? Enemy.EnemyType.Veglia : FloorDirector.I.PickRoundType(room);
                if (Enemy.IsFlying(type)) q.y += 1.8f;
                if (Physics.CheckSphere(q + Vector3.up * 0.6f, 0.4f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                var e = Enemy.Spawn(type, q);
                if (e == null) continue;
                Scale(e); e.MakeRelentless(); mine.Add(e); RoundFlare.At(q);
                return true;
            }
            return false;
        }

        void TickSurvival(float dt)
        {
            if (fi == null || FloorDirector.I == null || FloorDirector.I.transitioning || !Enabled || SurvivalMode.HoldWaves) return;
            if (Coop.IsClient) { clock += dt; nextWave = Mathf.Max(0, nextWave - dt); return; }
            if (!Targets.AnyAlive)
            {
                // sono a terra tutti: una volta sola l'ondata salta (a ogni fotogramma sarebbe un messaggio di rete per fotogramma)
                if (!teamDown) { teamDown = true; AbortSurvival(); }
                return;
            }
            teamDown = false;
            if (phase == Phase.Idle)
            {
                nextWave -= dt;
                if (nextWave <= 0) BeginSurvivalWave();
                return;
            }
            clock += dt;
            mine.RemoveAll(e => e == null || e.IsDead);
            statusT -= dt;
            if (statusT <= 0) { statusT = 0.5f; Coop.Game?.RoundStatus(target, spawned, mine.Count); }
            spawnT -= dt;
            if (spawned < target && mine.Count < cap && spawnT <= 0)
            {
                // i punti migliori finiscono (stanza piccola, tutti nella stessa parte): si allargano i limiti prima di rinunciare
                int relax = fails < 6 ? 0 : fails < 16 ? 1 : 2;
                int batch = Mathf.Min(mine.Count < cap / 2 ? 4 : 2, Mathf.Min(target - spawned, cap - mine.Count));
                int made = 0;
                for (int i = 0; i < batch; i++) { if (!SpawnSurvivalEnemy(relax)) break; spawned++; made++; }
                if (made > 0) { fails = 0; spawnT = gap; }
                else
                {
                    spawnT = 0.5f;
                    if (++fails % 40 == 0) Debug.LogWarning($"[PryingEyes] ondata {Round}: nessun punto di nascita raggiungibile da {fails} tentativi, {spawned}/{target} nati, {mine.Count} vivi");
                    // nessuno e' vivo e non nasce piu' nessuno: chiusura con quelli nati, per non lasciare il piano senza ondate
                    if (fails >= SpawnGiveUp && mine.Count == 0)
                    {
                        Debug.LogWarning($"[PryingEyes] ondata {Round}: nessun punto di nascita libero, finisce con {spawned}/{target}");
                        target = spawned;
                    }
                }
            }
            if (spawned >= target && mine.Count == 0)
            {
                endT += dt;
                if (endT >= 1.5f) CompleteSurvivalWave(spawned > 0);
            }
            else endT = 0;
            // rete di sicurezza: a nascite finite, restano pochi nemici e da tanto non succede nulla (uno e' caduto fuori dalla navmesh, uno e' incastrato)
            if (spawned >= target && mine.Count > 0)
            {
                if (mine.Count != lastAlive) { lastAlive = mine.Count; stallT = 0f; }
                else if ((stallT += dt) > StallLimit) SurvivalTimeout();
            }
            else { stallT = 0f; lastAlive = mine.Count; }
        }

        /// <summary>Solo host o da soli: l'ondata e' finita. Premio, regalo della Processione e racconto agli altri.</summary>
        void CompleteSurvivalWave(bool reward)
        {
            int points = reward ? RoundRules.Reward(Round) : 0;
            bool gift = reward && moth;
            FinishSurvivalWave(reward, points);
            Coop.Game?.RoundComplete(reward, points);
            if (!gift) return;
            // il regalo della Processione dei Lumi: un Reliquiario dove e' caduta l'ultima (non conta nel tetto del piano)
            Vector3 at = Drops.lastKillPos;
            if (at == Vector3.zero) at = GameManager.I.player.transform.position;
            Drops.Spawn(DropKind.Ammo, at, false);
        }

        void SurvivalTimeout()
        {
            Debug.LogWarning($"[PryingEyes] ondata {Round}: stallo di {StallLimit:F0} s con {mine.Count} nemici vivi, {spawned}/{target} nati");
            foreach (var e in mine.ToArray())
                if (e != null && !e.IsDead) { RoundFlare.At(e.transform.position); Enemy.Remove(e); }
            mine.Clear();
            CompleteSurvivalWave(false);
        }

        void FinishSurvivalWave(bool reward, int points)
        {
            var gm = GameManager.I;
            if (reward) { gm.AddPoints(points); gm.player.shield = 50; gm.player.weapon.AddAmmoFraction(0.25f); floorWaves++; }
            phase = Phase.Idle; nextWave = WaveBreak; mine.Clear(); stallT = 0f; lastAlive = 0;
            if (floorWaves >= WavesForExit) FloorDirector.I?.SurvivalExitReady();
            string head = (moth ? "PROCESSIONE DEI LUMI " : "ONDATA ") + Round + " SUPERATA";
            gm.hud?.Banner(head, reward ? $"+{points} punti  ·  {WaveBreak:0} secondi per rifornirti e aprire nuove aree" + (moth ? "  ·  un Reliquiario è caduto" : "") : "Le presenze si sono dissolte.");
            gm.hud?.SetRound(Round);
            FloorDirector.I?.RefreshObjectiveNow();
            Debug.Log($"[PryingEyes] ondata {Round} finita in {clock:F0} s ({(reward ? "+" + points + " punti" : "senza premio")}), ondate del piano {floorWaves}/{WavesForExit}");
        }

        void AbortSurvival()
        {
            bool active = phase == Phase.Fighting || mine.Count > 0;
            foreach (var e in mine) if (e != null && !e.IsDead) Enemy.Remove(e);
            mine.Clear();
            if (phase == Phase.Fighting) Round = Mathf.Max(0, Round - 1);
            phase = Phase.Idle; nextWave = FirstWaveDelay; stallT = 0f; lastAlive = 0;
            if (!active) return;
            GameManager.I.hud?.SetRound(Round, false);
            Debug.Log($"[PryingEyes] ondata annullata: chi giocava è morto dopo {clock:F0} s, {spawned}/{target} nati");
            Coop.Game?.RoundAborted();
        }

        string SurvivalStatus()
        {
            if (SurvivalMode.HoldWaves) return "ONDATE FERME";
            if (phase == Phase.Idle) return $"PROSSIMA ONDATA IN {Mathf.CeilToInt(nextWave)} s  ·  F per comprare aree e rifornimenti";
            int left = Coop.IsClient ? Mathf.Max(0, netTarget - netSpawned) + netAlive : target - spawned + mine.Count;
            return $"{(moth ? "PROCESSIONE " : "ONDATA ")}{Round}  ·  {left} nemici rimasti  ·  Aree aperte {RoomAccessDoors.I?.Access.OpenRooms.Count}";
        }

        public void CoopSurvivalSnapshot(int round, bool fighting, float next, int completed, bool procession)
        {
            Round = round; phase = fighting ? Phase.Fighting : Phase.Idle;
            nextWave = Mathf.Clamp(next, 0, WaveBreak); floorWaves = completed; moth = procession;
            GameManager.I.hud?.SetRound(round, false);
        }
    }
}
