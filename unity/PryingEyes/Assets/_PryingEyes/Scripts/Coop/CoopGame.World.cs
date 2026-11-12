using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Il mondo condiviso: nemici (l'ospite li simula, gli altri vedono pupazzi), candele, uscita, round, gocce.
    ///
    /// Tutto quello che cambia il mondo lo decide l'ospite e lo racconta con messaggi che portano l'epoca del piano (primo byte): un
    /// ospitato li applica solo se il suo piano e' costruito ed e' quello giusto, quelli per un piano che non ha ancora costruito li tiene da
    /// parte (ReplayBuffered), quelli di un piano vecchio li butta. Chi entra a partita in corso, o cambia piano, riceve tutto lo stato
    /// del piano appena dice di averlo costruito (HostWorldClientReady).
    /// Cosa e' di tutti: nemici, candele, uscita, round e gocce. Cosa e' personale (ognuno usa il suo): punti, armi, perk, libri, scrigno,
    /// altare, armi sui muri, pile.</summary>
    public sealed partial class CoopGame
    {
        // ------------------------------------------------------------------ messaggi con epoca
        struct Buffered { public CoopKind kind; public byte[] data; }
        readonly List<Buffered> buffered = new List<Buffered>();
        const int MaxBuffered = 400;

        /// <summary>Messaggi dell'ospite il cui primo byte e' l'epoca del piano.</summary>
        static bool CarriesEpoch(CoopKind k)
        {
            switch (k)
            {
                case CoopKind.EnemySpawn: case CoopKind.EnemyGone: case CoopKind.EnemyState: case CoopKind.EnemyEvent:
                case CoopKind.CandleLit: case CoopKind.ExitState: case CoopKind.RoundState:
                case CoopKind.DropSpawn: case CoopKind.DropTaken: case CoopKind.Bolt: case CoopKind.Teleport:
                    return true;
            }
            return false;
        }

        /// <summary>Quelli che se arrivano troppo presto si tengono da parte (gli altri, effimeri, si buttano).</summary>
        static bool Keep(CoopKind k) => k != CoopKind.EnemyState && k != CoopKind.Bolt;

        /// <summary>0 vale adesso; 1 riguarda un piano che non ho ancora costruito (da tenere da parte); -1 e' vecchio (da buttare).</summary>
        int EpochState(int epByte)
        {
            int d = (epByte - EpochByte) & 0xFF;
            if (d == 0) return FloorBuilt ? 0 : 1;
            return d < 128 ? 1 : -1;
        }

        /// <summary>Ospitato: smista un messaggio dell'ospite; se e' per un piano che non c'e' ancora lo tiene da parte.</summary>
        bool Defer(CoopKind kind, byte[] data)
        {
            if (host || !CarriesEpoch(kind)) return false;
            int st = data != null && data.Length > 0 ? EpochState(data[0]) : -1;
            if (st == 0) return false;
            if (st > 0 && Keep(kind))
            {
                if (buffered.Count >= MaxBuffered) buffered.RemoveAt(0);
                buffered.Add(new Buffered { kind = kind, data = data });
            }
            return true;
        }

        void ReplayBuffered()
        {
            if (buffered.Count == 0) return;
            var list = new List<Buffered>(buffered);
            buffered.Clear();
            foreach (var b in list) OnMessage(0, b.kind, b.data);       // se e' ancora presto torna in coda
        }

        // ------------------------------------------------------------------ stato del mondo (ospite)
        readonly Dictionary<int, Enemy> enemies = new Dictionary<int, Enemy>();
        readonly List<Enemy> spawnQueue = new List<Enemy>();
        readonly List<Enemy> tmpEnemies = new List<Enemy>();
        readonly Dictionary<int, DropPickup> drops = new Dictionary<int, DropPickup>();
        readonly HashSet<int> readyPending = new HashSet<int>();         // ospitati pronti, in attesa che l'ospite finisca di costruire
        int nextEnemyId = 1, nextDropId = 1;
        float enemyStateT, pruneT;
        static readonly int EnemyTypeCount = System.Enum.GetValues(typeof(Enemy.EnemyType)).Length;

        public Enemy EnemyById(int id) => enemies.TryGetValue(id, out var e) && e != null ? e : null;

        /// <summary>Per la prova a due processi: i nemici (ospite) o i pupazzi (ospitato) per id di rete.</summary>
        internal Dictionary<int, Enemy> EnemyMap => enemies;

        /// <summary>L'ospitato ha costruito il piano dell'epoca corrente e l'ospite ha finito di costruire il suo.</summary>
        bool IsReady(int slot) => readyEpoch.TryGetValue(slot, out int ep) && ep == (Epoch & 0xFFFF) && !readyPending.Contains(slot);

        bool AnyReady()
        {
            if (net == null) return false;
            foreach (int s in net.RemoteSlots) if (IsReady(s)) return true;
            return false;
        }

        void SendToReady(CoopKind kind, byte[] data, bool reliable = true)
        {
            if (net == null) return;
            foreach (int s in net.RemoteSlots) if (IsReady(s)) net.SendToSlot(s, kind, data, reliable);
        }

        void OnFloorStartWorld()
        {
            enemies.Clear(); spawnQueue.Clear(); drops.Clear(); readyPending.Clear();
        }

        void OnFloorBuiltWorld()
        {
            if (!host) return;
            // gli ospitati che erano pronti prima dell'ospite ricevono adesso lo stato del piano
            var list = new List<int>(readyPending);
            readyPending.Clear();
            foreach (int s in list) if (IsReady(s)) HostWorldClientReady(s);
        }

        void HostWorldWelcome(int slot) { }

        void HostPlayerGone(int slot) { readyPending.Remove(slot); }

        /// <summary>Un ospitato ha costruito il piano dell'epoca corrente.</summary>
        void HostWorldReady(int from)
        {
            if (!host || net == null) return;
            if (!FloorBuilt) { readyPending.Add(from); return; }
            HostWorldClientReady(from);
        }

        /// <summary>Tutto lo stato del piano per chi e' appena pronto: nemici, candele, uscita, round in corso, gocce a terra.</summary>
        void HostWorldClientReady(int slot)
        {
            var fd = FloorDirector.I;
            int n = 0;
            foreach (var e in enemies.Values)
                if (e != null && !e.IsDead) { SendEnemySpawn(slot, e); n++; }
            if (fd != null && fd.cur != null)
            {
                var cs = fd.cur.candles;
                for (int i = 0; i < cs.Count; i++)
                    if (cs[i] != null && cs[i].lit && cs[i].objective)
                        net.SendToSlot(slot, CoopKind.CandleLit, w.Reset().U8(EpochByte).U8(i).U8(cs[i].litBy).Bool(true).ToArray());
                if (fd.ExitOpen) net.SendToSlot(slot, CoopKind.ExitState, w.Reset().U8(EpochByte).Bool(true).ToArray());
            }
            var rd = RoundDirector.I;
            if (rd != null && rd.Sealed && rd.CurrentZone >= 0)
            {
                net.SendToSlot(slot, CoopKind.RoundState, w.Reset().U8(EpochByte).U8(0).U8(rd.CurrentZone).U16(RoundDirector.Round).Bool(rd.Moth).U8((int)rd.Mood).ToArray());
                if (rd.Fighting) net.SendToSlot(slot, CoopKind.RoundState, w.Reset().U8(EpochByte).U8(1).U16(rd.Target).U16(rd.Spawned).U16(rd.Alive).ToArray());
            }
            int d = 0;
            foreach (var kv in drops)
            {
                var pk = kv.Value;
                if (pk == null) continue;
                net.SendToSlot(slot, CoopKind.DropSpawn, w.Reset().U8(EpochByte).U16(kv.Key).U8((int)pk.kind).V3(pk.transform.position).F32(pk.Age).ToArray());
                d++;
            }
            Debug.Log($"[PryingEyes] coop: stato del piano a {NameOf(slot)}: {n} nemici, {d} gocce, uscita {(fd != null && fd.ExitOpen ? "aperta" : "chiusa")}");
        }

        // ------------------------------------------------------------------ nemici: ospite -> ospitati
        /// <summary>Ospite: un nemico e' nato. Gli da' un id e lo annuncia a fine fotogramma, quando chi l'ha creato ha finito di prepararlo
        /// (vita, round, casa).</summary>
        public void EnemySpawned(Enemy e)
        {
            if (!host || e == null) return;
            e.netId = nextEnemyId;
            nextEnemyId = nextEnemyId >= 0xFFFE ? 1 : nextEnemyId + 1;
            enemies[e.netId] = e;
            spawnQueue.Add(e);
        }

        void SendEnemySpawn(int slot, Enemy e)
        {
            w.Reset().U8(EpochByte).U16(e.netId).U8((int)e.type).F32(e.maxHp);
            e.NetState().Write(w);
            byte[] data = w.ToArray();
            if (slot < 0) SendToReady(CoopKind.EnemySpawn, data);
            else net.SendToSlot(slot, CoopKind.EnemySpawn, data);
        }

        void LateUpdate()
        {
            if (!host || spawnQueue.Count == 0) return;
            bool any = AnyReady();
            foreach (var e in spawnQueue)
                if (any && e != null && !e.IsDead && enemies.ContainsKey(e.netId)) SendEnemySpawn(-1, e);
            spawnQueue.Clear();
        }

        /// <summary>Ospite: un attacco, un lancio o un grido, da mostrare agli altri.</summary>
        public void EnemyEvent(Enemy e, EnemyEvt evt, Vector3 pos, float extra = 0f)
        {
            if (!host || e == null || e.netId < 0 || !AnyReady()) return;
            SendToReady(CoopKind.EnemyEvent, w.Reset().U8(EpochByte).U16(e.netId).U8((int)evt).V3(pos).F32(extra).ToArray());
        }

        /// <summary>Ospite: un nemico e' morto (i punti li ha gia' dati Enemy.Damage).</summary>
        public void EnemyDied(Enemy e, bool headshot, DamageSource src, int claimer)
        {
            if (!host || e == null || e.netId < 0) return;
            enemies.Remove(e.netId);
            if (AnyReady()) SendToReady(CoopKind.EnemyGone, w.Reset().U8(EpochByte).U16(e.netId).U8(1).Bool(headshot).U8((int)src).ToArray());
        }

        /// <summary>Ospite: un nemico sparisce senza morire (round annullato o scaduto).</summary>
        public void EnemyRemoved(Enemy e)
        {
            if (!host || e == null || e.netId < 0) return;
            enemies.Remove(e.netId);
            if (AnyReady()) SendToReady(CoopKind.EnemyGone, w.Reset().U8(EpochByte).U16(e.netId).U8(0).Bool(false).U8(0).ToArray());
        }

        /// <summary>Via tutti i nemici (cambio piano): vale su ogni macchina.</summary>
        public void OnEnemiesCleared() { enemies.Clear(); spawnQueue.Clear(); }

        /// <summary>Ospite: ~10 volte al secondo la posa di tutti i nemici vivi, in pacchetti non affidabili da 40.</summary>
        void SendEnemyStates()
        {
            if (net == null || enemies.Count == 0 || !AnyReady()) return;
            tmpEnemies.Clear();
            foreach (var e in enemies.Values) if (e != null && !e.IsDead) tmpEnemies.Add(e);
            const int Per = 40;
            for (int i = 0; i < tmpEnemies.Count; i += Per)
            {
                int n = Mathf.Min(Per, tmpEnemies.Count - i);
                w.Reset().U8(EpochByte).U8(n);
                for (int k = 0; k < n; k++) { var e = tmpEnemies[i + k]; w.U16(e.netId); e.NetState().Write(w); }
                SendToReady(CoopKind.EnemyState, w.ToArray(), false);
            }
        }

        /// <summary>Ospite: un dardo magico e' partito (agli altri solo grafica).</summary>
        public void BoltLaunched(Vector3 origin, Vector3 target, float dmg)
        {
            if (!host || !AnyReady()) return;
            SendToReady(CoopKind.Bolt, w.Reset().U8(EpochByte).V3(origin).V3(target).F32(dmg).ToArray());
        }

        // ------------------------------------------------------------------ danni, punti, rumori
        /// <summary>Ospite: un nemico ferisce un compagno (il suo gioco lo applica: e' lui che conosce scudo, perk e invulnerabilita').</summary>
        public void HurtRemote(int slot, float dmg)
        {
            if (!host || net == null) return;
            net.SendToSlot(slot, CoopKind.HurtPlayer, w.Reset().F32(dmg).ToArray());
        }

        public void SedateRemote(int slot, float seconds)
        {
            if (!host || net == null) return;
            net.SendToSlot(slot, CoopKind.SedatePlayer, w.Reset().F32(seconds).ToArray());
        }

        /// <summary>Ospite: sposta un compagno (una serranda che cala non lo deve schiacciare).</summary>
        public void TeleportRemote(int slot, Vector3 pos)
        {
            if (!host || net == null) return;
            net.SendToSlot(slot, CoopKind.Teleport, w.Reset().U8(EpochByte).V3(pos).ToArray());
        }

        /// <summary>Punti (e un po' di munizioni) a un giocatore: se sono io li prendo, se e' un compagno li manda l'ospite.</summary>
        public void AwardPoints(int slot, int pts, float ammo = 0f)
        {
            if (slot == Coop.Slot)
            {
                var gm = GameManager.I;
                if (gm == null) return;
                gm.AddPoints(pts);
                if (ammo > 0f && gm.player != null) gm.player.weapon.AddAmmoFraction(ammo);
                return;
            }
            if (host && net != null) net.SendToSlot(slot, CoopKind.AwardPoints, w.Reset().I32(pts).U8(Mathf.Clamp(Mathf.RoundToInt(ammo * 100f), 0, 100)).ToArray());
        }

        /// <summary>L'uccisione e' di un compagno: gli dico fonte e testa, punti, conteggio e cura li fa il suo gioco (GameManager.CreditKill).</summary>
        public void KillCredit(int slot, DamageSource src, bool headshot)
        {
            if (slot == Coop.Slot) { GameManager.I?.CreditKill(src, headshot); return; }
            if (host && net != null) net.SendToSlot(slot, CoopKind.KillCredit, w.Reset().U8((int)src).Bool(headshot).ToArray());
        }

        /// <summary>Riga di testo per un giocatore (slot) o per tutti (-1). mode: 0 avviso, 1 striscione; +2 con il suono delle monete.</summary>
        public void Announce(int slot, int mode, string a, string b)
        {
            if (!host || net == null) return;
            var data = w.Reset().U8(mode).Str(a).Str(b).ToArray();
            if (slot < 0) net.SendToAll(CoopKind.Announce, data);
            else if (slot == Coop.Slot) ShowAnnounce(mode, a, b);
            else net.SendToSlot(slot, CoopKind.Announce, data);
        }

        static void ShowAnnounce(int mode, string a, string b)
        {
            var hud = GameManager.I != null ? GameManager.I.hud : null;
            if ((mode & 2) != 0) Sfx.UI(SoundBank.PickCoins, 0.5f, 0.85f);
            if (hud == null) return;
            if ((mode & 1) != 0) hud.Banner(a, b); else hud.Toast(a);
        }

        /// <summary>Ospitato: ho colpito un nemico (un pupazzo): lo dico all'ospite, che applica il danno e mi paga i punti.</summary>
        public void SendHitClaim(Enemy e, float dmg, Vector3 point, bool headshot, bool award, DamageSource src)
        {
            if (host || net == null || !Started || e == null || e.netId < 0) return;
            net.SendToHost(CoopKind.HitClaim, w.Reset().U8(EpochByte).U16(e.netId).F32(dmg).Bool(headshot).U8((int)src).Bool(award).V3(point).ToArray());
        }

        /// <summary>Ospitato: ho fatto un rumore (passi, spari, ping, candele): lo sentono i nemici dell'ospite.</summary>
        public void NoiseHeard(Vector3 pos, float radius, NoiseKind kind)
        {
            if (host || net == null || !Started || !FloorBuilt) return;
            net.SendToHost(CoopKind.NoiseEvt, w.Reset().U8(EpochByte).V3(pos).U8(Mathf.Clamp(Mathf.RoundToInt(radius), 0, 255)).U8((int)kind).ToArray(), kind != NoiseKind.Step);
        }

        // ------------------------------------------------------------------ candele, uscita
        /// <summary>Ospitato: voglio accendere questa candela (decide l'ospite).</summary>
        public void RequestCandle(Candelabro c)
        {
            var fd = FloorDirector.I;
            if (host || net == null || !FloorBuilt || fd == null || fd.cur == null) return;
            int idx = fd.cur.candles.IndexOf(c);
            if (idx < 0) return;
            net.SendToHost(CoopKind.UseCandle, w.Reset().U8(EpochByte).U8(idx).ToArray());
        }

        /// <summary>Ospite: una candela e' stata accesa da 'by'.</summary>
        public void CandleLit(Candelabro c, int by)
        {
            var fd = FloorDirector.I;
            if (!host || fd == null || fd.cur == null || !AnyReady()) return;
            int idx = fd.cur.candles.IndexOf(c);
            if (idx < 0) return;
            SendToReady(CoopKind.CandleLit, w.Reset().U8(EpochByte).U8(idx).U8(by).Bool(false).ToArray());
        }

        public void ExitOpened()
        {
            if (!host || !AnyReady()) return;
            SendToReady(CoopKind.ExitState, w.Reset().U8(EpochByte).Bool(true).ToArray());
        }

        float exitAsked = -99f;

        /// <summary>Ospitato: sono entrato nell'ascensore aperto, scendiamo (decide l'ospite).</summary>
        public void RequestDescend()
        {
            if (host || !Started || net == null || Time.unscaledTime - exitAsked < 2f) return;
            exitAsked = Time.unscaledTime;
            net.SendToHost(CoopKind.UseExit, w.Reset().U16(Epoch).ToArray());
        }

        // ------------------------------------------------------------------ round
        public void RoundStarted(int zone, int round, bool moth, int mood)
        {
            if (!host || !AnyReady()) return;
            SendToReady(CoopKind.RoundState, w.Reset().U8(EpochByte).U8(0).U8(zone).U16(round).Bool(moth).U8(mood).ToArray());
        }

        public void RoundStatus(int target, int spawned, int alive)
        {
            if (!host || !AnyReady()) return;
            SendToReady(CoopKind.RoundState, w.Reset().U8(EpochByte).U8(1).U16(target).U16(spawned).U16(alive).ToArray());
        }

        public void RoundComplete(bool reward, int pts)
        {
            if (!host || !AnyReady()) return;
            SendToReady(CoopKind.RoundState, w.Reset().U8(EpochByte).U8(2).Bool(reward).I32(pts).ToArray());
        }

        public void RoundAborted()
        {
            if (!host || !AnyReady()) return;
            SendToReady(CoopKind.RoundState, w.Reset().U8(EpochByte).U8(3).ToArray());
        }

        // ------------------------------------------------------------------ gocce
        public void DropSpawned(DropPickup pk)
        {
            if (!host || pk == null) return;
            pk.netId = nextDropId++;
            drops[pk.netId] = pk;
            if (AnyReady()) SendToReady(CoopKind.DropSpawn, w.Reset().U8(EpochByte).U16(pk.netId).U8((int)pk.kind).V3(pk.transform.position).F32(0f).ToArray());
        }

        /// <summary>Ospite: una goccia e' stata presa da 'by' (slot); vale per tutti.</summary>
        public void DropTaken(DropPickup pk, int by, Vector3 at)
        {
            if (!host || pk == null || pk.netId < 0) return;
            drops.Remove(pk.netId);
            if (AnyReady()) SendToReady(CoopKind.DropTaken, w.Reset().U8(EpochByte).U16(pk.netId).U8((int)pk.kind).U8(by).V3(at).ToArray());
        }

        /// <summary>Ospitato: sono vicino a una goccia (decide l'ospite chi la prende).</summary>
        public void ClaimDrop(DropPickup pk)
        {
            if (host || net == null || !Started || pk == null || pk.netId < 0) return;
            net.SendToHost(CoopKind.DropClaim, w.Reset().U8(EpochByte).U16(pk.netId).ToArray());
        }

        // ------------------------------------------------------------------ ogni fotogramma
        void WorldUpdate()
        {
            if (!host) return;
            enemyStateT -= Time.unscaledDeltaTime;
            if (enemyStateT <= 0f) { enemyStateT = 0.1f; SendEnemyStates(); }
            pruneT -= Time.unscaledDeltaTime;
            if (pruneT <= 0f)
            {
                pruneT = 2f;
                List<int> dead = null;
                foreach (var kv in enemies) if (kv.Value == null) (dead ??= new List<int>()).Add(kv.Key);
                if (dead != null) foreach (int id in dead) enemies.Remove(id);
                dead = null;
                foreach (var kv in drops) if (kv.Value == null) (dead ??= new List<int>()).Add(kv.Key);
                if (dead != null) foreach (int id in dead) drops.Remove(id);
            }
        }

        // ------------------------------------------------------------------ ospite: messaggi degli ospitati
        void HostWorldMessage(int from, CoopKind kind, ref NetReader r)
        {
            var fd = FloorDirector.I;
            switch (kind)
            {
                case CoopKind.UseExit:
                {
                    int ep = r.U16();
                    if (r.Ok && ep == Epoch && fd != null && fd.ExitOpen && !fd.transitioning) fd.CoopDescend();
                    break;
                }
                case CoopKind.UseCandle:
                {
                    int ep = r.U8(), idx = r.U8();
                    if (!r.Ok || ep != EpochByte || fd == null || fd.cur == null || idx >= fd.cur.candles.Count) break;
                    var c = fd.cur.candles[idx];
                    if (c != null && !c.lit) c.LightBy(from);
                    break;
                }
                case CoopKind.HitClaim:
                {
                    int ep = r.U8(), id = r.U16();
                    float dmg = r.F32();
                    bool head = r.Bool();
                    int src = r.U8();
                    bool award = r.Bool();
                    Vector3 pt = r.V3();
                    if (!r.Ok || ep != EpochByte || src > (int)DamageSource.Nuke) break;
                    var e = EnemyById(id);
                    if (e == null || e.IsDead) break;
                    var ds = (DamageSource)src;
                    if (ds == DamageSource.Nuke) ds = DamageSource.Gun;            // la Campana non la chiama un compagno
                    e.Damage(Mathf.Clamp(dmg, 0f, 100000f), pt, head, award, ds, from);
                    if (ds == DamageSource.Knife && !e.IsDead) e.Provoke(Targets.OfSlot(from));     // come Knife.Strike: chi sopravvive sa dov'e'
                    break;
                }
                case CoopKind.NoiseEvt:
                {
                    int ep = r.U8();
                    Vector3 pos = r.V3();
                    int radius = r.U8(), k = r.U8();
                    if (!r.Ok || ep != EpochByte || k > (int)NoiseKind.Alarm || !IsReady(from)) break;
                    Noise.Hear(pos, radius, (NoiseKind)k);
                    break;
                }
                case CoopKind.DropClaim:
                {
                    int ep = r.U8(), id = r.U16();
                    if (!r.Ok || ep != EpochByte || !drops.TryGetValue(id, out var pk) || pk == null) break;
                    var t = Targets.OfSlot(from);
                    if (t == null || !t.Alive || (t.Feet + Vector3.up - pk.Center).sqrMagnitude > 4.5f * 4.5f) break;
                    drops.Remove(id);
                    Drops.Take(pk, from);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ ospitato: messaggi dell'ospite
        void ClientWorldMessage(CoopKind kind, ref NetReader r)
        {
            var fd = FloorDirector.I;
            var gm = GameManager.I;
            switch (kind)
            {
                case CoopKind.EnemySpawn:
                {
                    r.U8();
                    int id = r.U16(), type = r.U8();
                    float maxHp = r.F32();
                    var s = EnemyNet.Read(ref r);
                    if (!r.Ok || type >= EnemyTypeCount) return;
                    if (enemies.TryGetValue(id, out var ex) && ex != null) { ex.ApplyNet(s); return; }
                    var e = Enemy.SpawnPuppet(id, (Enemy.EnemyType)type, s);
                    if (maxHp > 1f) { e.maxHp = maxHp; e.hp = Mathf.Max(0.01f, s.hp * maxHp); }
                    enemies[id] = e;
                    break;
                }
                case CoopKind.EnemyState:
                {
                    r.U8();
                    int n = r.U8();
                    for (int i = 0; i < n; i++)
                    {
                        int id = r.U16();
                        var s = EnemyNet.Read(ref r);
                        if (!r.Ok) return;
                        if (enemies.TryGetValue(id, out var e) && e != null) e.ApplyNet(s);
                    }
                    break;
                }
                case CoopKind.EnemyEvent:
                {
                    r.U8();
                    int id = r.U16(), evt = r.U8();
                    Vector3 pos = r.V3();
                    float extra = r.F32();
                    if (!r.Ok) return;
                    if (enemies.TryGetValue(id, out var e) && e != null) e.PuppetEvent((EnemyEvt)evt, pos, extra);
                    break;
                }
                case CoopKind.EnemyGone:
                {
                    r.U8();
                    int id = r.U16(), mode = r.U8();
                    bool head = r.Bool();
                    int src = r.U8();
                    if (!r.Ok) return;
                    if (!enemies.TryGetValue(id, out var e)) return;
                    enemies.Remove(id);
                    if (e == null) return;
                    if (mode == 1) e.PuppetDie(head, (DamageSource)Mathf.Clamp(src, 0, (int)DamageSource.Nuke));
                    else Enemy.Remove(e);
                    break;
                }
                case CoopKind.Bolt:
                {
                    r.U8();
                    Vector3 a = r.V3(), b = r.V3();
                    float dmg = r.F32();
                    if (r.Ok) MagicBolt.Launch(a, b, dmg, true);
                    break;
                }
                case CoopKind.AwardPoints:
                {
                    int pts = r.I32(), ammo = r.U8();
                    if (!r.Ok || gm == null) return;
                    gm.AddPoints(pts);
                    if (ammo > 0 && gm.player != null) gm.player.weapon.AddAmmoFraction(ammo / 100f);
                    break;
                }
                case CoopKind.KillCredit:
                {
                    int src = r.U8();
                    bool head = r.Bool();
                    if (r.Ok && gm != null) gm.CreditKill((DamageSource)Mathf.Clamp(src, 0, (int)DamageSource.Nuke), head);
                    break;
                }
                case CoopKind.HurtPlayer:
                {
                    float dmg = r.F32();
                    if (!r.Ok || gm == null || gm.player == null || !gm.player.alive) return;
                    gm.player.Hurt(Mathf.Clamp(dmg, 0f, 500f));
                    GameManager.damageTaken++;
                    break;
                }
                case CoopKind.SedatePlayer:
                {
                    float sec = r.F32();
                    if (r.Ok && gm != null && gm.player != null) gm.player.Sedate(Mathf.Clamp(sec, 0f, 20f));
                    break;
                }
                case CoopKind.Teleport:
                {
                    r.U8();
                    Vector3 p = r.V3();
                    if (r.Ok && gm != null && gm.player != null) gm.player.Teleport(p);
                    break;
                }
                case CoopKind.Announce:
                {
                    int mode = r.U8();
                    string a = r.Str(), b = r.Str();
                    if (r.Ok) ShowAnnounce(mode, a, b);
                    break;
                }
                case CoopKind.CandleLit:
                {
                    r.U8();
                    int idx = r.U8(), by = r.U8();
                    bool quiet = r.Bool();
                    if (!r.Ok || fd == null || fd.cur == null || idx >= fd.cur.candles.Count) return;
                    fd.CoopCandleLit(fd.cur.candles[idx], by, quiet);
                    break;
                }
                case CoopKind.ExitState:
                {
                    r.U8();
                    bool open = r.Bool();
                    if (r.Ok && open && fd != null) fd.CoopOpenExit();
                    break;
                }
                case CoopKind.RoundState:
                {
                    r.U8();
                    int sub = r.U8();
                    var rd = RoundDirector.I;
                    if (rd == null) return;
                    if (sub == 0)
                    {
                        int zone = r.U8(), round = r.U16();
                        bool moth = r.Bool();
                        int mood = r.U8();
                        if (r.Ok) rd.CoopStart(zone, round, moth, mood);
                    }
                    else if (sub == 1)
                    {
                        int t = r.U16(), s = r.U16(), a = r.U16();
                        if (r.Ok) rd.CoopStatus(t, s, a);
                    }
                    else if (sub == 2)
                    {
                        bool reward = r.Bool();
                        int pts = r.I32();
                        if (r.Ok) rd.CoopComplete(reward, pts);
                    }
                    else if (sub == 3) rd.CoopAbort();
                    break;
                }
                case CoopKind.DropSpawn:
                {
                    r.U8();
                    int id = r.U16(), k = r.U8();
                    Vector3 p = r.V3();
                    float age = r.F32();
                    if (!r.Ok || k > (int)DropKind.Nuke || drops.ContainsKey(id)) return;
                    drops[id] = Drops.SpawnPuppet(id, (DropKind)k, p, age);
                    break;
                }
                case CoopKind.DropTaken:
                {
                    r.U8();
                    int id = r.U16(), k = r.U8(), by = r.U8();
                    Vector3 at = r.V3();
                    if (!r.Ok || k > (int)DropKind.Nuke) return;
                    if (drops.TryGetValue(id, out var pk))
                    {
                        drops.Remove(id);
                        if (pk != null) { Drops.Forget(pk); Destroy(pk.gameObject); }
                    }
                    Drops.Apply((DropKind)k, at, false);          // vale per tutti
                    break;
                }
            }
        }
    }
}
