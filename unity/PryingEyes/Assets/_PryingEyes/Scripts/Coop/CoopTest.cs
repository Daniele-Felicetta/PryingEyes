using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Prova della cooperativa a due processi: un ospite e un ospitato (senza grafica) fanno lo stesso copione, passo per passo, con una
    /// sincronia a ogni passo (CoopKind.Test), e ognuno verifica la sua parte. Parte solo con -autotest -cooptest (che spegne anche lo sparo
    /// automatico) e finisce da sola: uscita 0 se tutto e' andato bene, 1 altrimenti. Si lancia con run_coop.ps1:
    ///   PryingEyes.exe -batchmode -nographics -autotest -cooptest -coophost -cooplocal -seed N -floor 1
    ///   PryingEyes.exe -batchmode -nographics -autotest -cooptest -coopjoin 127.0.0.1
    /// Passi: 0 piano e compagno in vista; 1 nemici del piano = pupazzi; 2 un nemico nato sull'ospite compare e si muove; 3 colpi e uccisione
    /// dell'ospitato (punti a chi spara); 4 un nemico dell'ospite ferisce l'ospitato; 5 candela accesa dall'ospitato; 6 round (serrande,
    /// nemici, premio per tutti); 7 goccia presa dall'ospitato (effetto per tutti); 8 uscita e discesa insieme (stessa firma del piano).
    /// Altri copioni: -cooplate (entrata a round in corso), -coopleave / -coopcrash (un ospitato se ne va, pulito o ucciso), -coophostleave (se ne va
    /// l'ospite), -cooppics (foto del compagno, solo con la grafica), -coopplayers N (N giocatori in tutto).</summary>
    public sealed partial class CoopTest : MonoBehaviour
    {
        public static CoopTest I;
        static readonly bool Flag = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-cooptest") >= 0;
        public static bool Wanted => Flag && GameManager.autoTest;

        /// <summary>Su un oggetto a parte: se la connessione cade, CoopGame sparisce, ma la prova deve poterlo verificare e finire.</summary>
        public static void Ensure(CoopGame g)
        {
            if (I != null || g == null) return;
            new GameObject("CoopTest").AddComponent<CoopTest>();
        }

        /// <summary>Un messaggio di prova dell'altro processo (sempre dal canale di rete; senza -cooptest nessuno lo ascolta).</summary>
        public static void OnNet(int from, byte[] data) { if (I != null) I.Receive(from, data); }

        // ------------------------------------------------------------------ stato
        readonly NetWriter w = new NetWriter();
        readonly Dictionary<int, int> peerStep = new Dictionary<int, int>();
        readonly List<(int id, Vector3 pos)> report = new List<(int, Vector3)>();
        int reportSeq, expectSeq;
        int sigValue, sigFloor, sigSeq;
        int checks, failed, exceptions;
        float startT;
        bool aborted, finished;
        string Tag => Coop.IsHost ? "ospite" : "ospitato";
        CoopGame G => Coop.Game;

        // dati passati da un passo all'altro
        Enemy spawned;
        int hostScore, hostKills;
        int roundNo;

        void Awake()
        {
            I = this;
            startT = Time.time;
            Application.logMessageReceived += OnLog;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            if (I == this) I = null;
        }

        void Start() { StartCoroutine(Run()); }

        void Update()
        {
            if (!finished && Time.time - startT > 480f) { Check("tempo massimo della prova", false, "480 s"); aborted = true; Finish(); }
        }

        /// <summary>Un'eccezione che nasce nel nostro codice fa fallire la prova (quelle di Unity/URP in -nographics no).</summary>
        void OnLog(string cond, string stack, LogType type)
        {
            if (type != LogType.Exception) return;
            if (!(stack ?? "").Contains("PryingEyes") && !(cond ?? "").Contains("PryingEyes")) return;
            exceptions++;
            if (exceptions <= 3) Debug.Log($"[PryingEyes] cooptest {Tag}: eccezione nel gioco: {cond}");
        }

        void Log(string s) => Debug.Log($"[PryingEyes] cooptest {Tag}: {s}");

        void Check(string what, bool ok, string detail = "")
        {
            checks++;
            if (!ok) failed++;
            Log($"{(ok ? "ok      " : "FALLITO ")} {what}{(detail.Length > 0 ? "   [" + detail + "]" : "")}");
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            Check("nessuna eccezione nel codice del gioco", exceptions == 0, exceptions + " eccezioni");
            Log($"RISULTATO {(failed == 0 && !aborted ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks} verifiche ok{(aborted ? " (interrotta)" : "")}");
            Application.Quit(failed == 0 && !aborted ? 0 : 1);
        }

        // ------------------------------------------------------------------ messaggi tra i due processi
        void Receive(int from, byte[] data)
        {
            var r = new NetReader(data);
            int sub = r.U8();
            if (sub == 1)                       // un ospitato e' arrivato alla sincronia n (sull'ospite)
            {
                int step = r.U8();
                if (r.Ok) peerStep[from] = Mathf.Max(peerStep.TryGetValue(from, out int o) ? o : -1, step);
            }
            else if (sub == 4)                  // l'ospite ha visto arrivare tutti: la sincronia n e' aperta (sugli ospitati)
            {
                int step = r.U8();
                if (r.Ok) releaseStep = Mathf.Max(releaseStep, step);
            }
            else if (sub == 2)
            {
                int n = r.U8();
                var tmp = new List<(int, Vector3)>();
                for (int i = 0; i < n; i++) { int id = r.U16(); var p = r.V3(); tmp.Add((id, p)); }
                if (!r.Ok) return;
                report.Clear();
                report.AddRange(tmp);
                reportSeq++;
            }
            else if (sub == 3)
            {
                int sig = r.I32(), fl = r.I32();
                if (r.Ok) { sigValue = sig; sigFloor = fl; sigSeq++; }
            }
            else if (sub == 5)                  // l'ospite racconta le aree aperte e l'ultima comprata (ciclo a ondate, SendRooms)
            {
                int open = r.U8(), bought = r.U8();
                if (r.Ok) { peerOpenRooms = open; peerBoughtRoom = bought - 1; roomsSeq++; }
            }
        }

        /// <summary>Sull'ospite: tutti gli ospitati collegati sono arrivati alla sincronia 'step'.</summary>
        bool AllArrived(int step)
        {
            var g = G;
            if (g == null) return false;
            bool any = false;
            foreach (var kv in g.Players)
            {
                if (kv.Value == null) continue;
                any = true;
                if (!peerStep.TryGetValue(kv.Key, out int s) || s < step) return false;
            }
            return any;
        }

        /// <summary>Sincronia a due fasi: gli ospitati dicono "sono arrivato" all'ospite, che quando li ha visti arrivare tutti manda "via" a tutti.
        /// I processi chiamano Barrier lo stesso numero di volte nello stesso ordine. Il numero e' un contatore (non quello del passo): un numero
        /// alto non deve far saltare le sincronie dopo.</summary>
        IEnumerator Barrier()
        {
            int n = ++barrierN;
            float t0 = Time.time;
            bool ok;
            if (Coop.IsHost)
            {
                while (!AllArrived(n) && Time.time - t0 < 45f) yield return null;
                ok = AllArrived(n);
                G.SendTest(w.Reset().U8(4).U8(n).ToArray());
            }
            else
            {
                G.SendTest(w.Reset().U8(1).U8(n).ToArray());
                while (releaseStep < n && Time.time - t0 < 45f) yield return null;
                ok = releaseStep >= n;
            }
            if (!ok) { Check($"sincronia con gli altri processi ({n}a)", false, "scaduta: qualcuno non risponde"); aborted = true; }
        }
        int barrierN, releaseStep;

        /// <summary>L'ospite racconta i nemici che ha (id e posizione); l'ospitato li confronta con i suoi pupazzi (ExpectEnemies).</summary>
        void SendEnemies()
        {
            var list = new List<KeyValuePair<int, Enemy>>();
            foreach (var kv in G.EnemyMap) if (kv.Value != null && !kv.Value.IsDead) list.Add(kv);
            int n = Mathf.Min(list.Count, 200);
            w.Reset().U8(2).U8(n);
            for (int i = 0; i < n; i++) w.U16(list[i].Key).V3(list[i].Value.transform.position);
            G.SendTest(w.ToArray());
            Log($"rapporto: {list.Count} nemici vivi");
        }

        IEnumerator ExpectEnemies(string what, float timeout)
        {
            expectSeq++;
            float t0 = Time.time;
            while (reportSeq < expectSeq && Time.time - t0 < timeout) yield return null;
            string detail = "nessun rapporto";
            bool ok = false;
            while (reportSeq >= expectSeq && Time.time - t0 < timeout)
            {
                ok = CompareReport(out detail);
                if (ok) break;
                yield return new WaitForSeconds(0.25f);
            }
            Check(what, ok, detail);
        }

        bool CompareReport(out string detail)
        {
            var map = G.EnemyMap;
            int missing = 0, far = 0, extra = 0;
            float worst = 0f;
            var ids = new HashSet<int>();
            foreach (var (id, pos) in report)
            {
                ids.Add(id);
                if (!map.TryGetValue(id, out var e) || e == null || e.IsDead) { missing++; continue; }
                float d = Vector3.Distance(e.transform.position, pos);
                worst = Mathf.Max(worst, d);
                if (d > 3f) far++;
            }
            foreach (var kv in map) if (kv.Value != null && !kv.Value.IsDead && !ids.Contains(kv.Key)) extra++;
            detail = $"{report.Count} sull'ospite, mancanti {missing}, lontani {far}, in piu' {extra}, scarto massimo {worst:F1} m";
            return missing == 0 && far == 0 && extra == 0;
        }

        // ------------------------------------------------------------------ aiuti
        static Vector3 NavPoint(Vector3 p) => NavMesh.SamplePosition(p, out var h, 4f, NavMesh.AllAreas) ? h.position : p;
        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        bool PeerShown()
        {
            foreach (var p in G.Players.Values) if (p != null && p.Shown) return true;
            return false;
        }

        static int ArgInt(string name, int def)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name && int.TryParse(a[i + 1], out int v)) return v;
            return def;
        }

        /// <summary>Quanti giocatori in tutto (-coopplayers N, da 2 a 4): ognuno deve vedere gli altri N-1.</summary>
        static readonly int Expected = Mathf.Clamp(ArgInt("-coopplayers", 2), 2, 4);

        /// <summary>Con piu' ospitati agisce solo il primo (slot 1); gli altri guardano e verificano che i suoi atti non cambino cio' che e' loro.</summary>
        static bool Primary => Coop.Slot == 1;

        int ShownCount()
        {
            int n = 0;
            foreach (var p in G.Players.Values) if (p != null && p.Shown) n++;
            return n;
        }

        /// <summary>Dove sembra stare l'altro giocatore che conta per i passi (l'ospitato primario visto dall'ospite, l'ospite visto dagli ospitati).</summary>
        Vector3 PeerPos()
        {
            if (G.Players.TryGetValue(Coop.IsHost ? 1 : 0, out var want) && want != null && want.Shown) return want.transform.position;
            foreach (var p in G.Players.Values) if (p != null && p.Shown) return p.transform.position;
            return GameManager.I.player.transform.position;
        }

        static bool PlaceNear(Vector3 center, float dist, out Vector3 p)
        {
            p = center;
            if (!NavMesh.SamplePosition(center, out var origin, 2f, HotelNav.EnemyMask)) return false;
            var path = new NavMeshPath();
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI * 2f / 16f, d = k < 8 ? dist : dist * 0.6f;
                var q = center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * d;
                if (NavMesh.SamplePosition(q, out var h, 1.0f, HotelNav.EnemyMask) && Flat(h.position - center) > d * 0.7f &&
                    NavMesh.CalculatePath(h.position, origin.position, HotelNav.EnemyMask, path) && path.status == NavMeshPathStatus.PathComplete)
                { p = h.position; return true; }
            }
            p = center;
            return false;
        }

        /// <summary>Il centro navigabile della stanza piu' lontana da 'from' (almeno una ventina di metri, se il piano li ha).</summary>
        static Vector3 FarPoint(Vector3 from)
        {
            Vector3 best = from;
            float bd = -1f;
            foreach (var room in FloorDirector.I.cur.plan.rooms)
            {
                var p = new Vector3(room.CenterX, from.y, room.CenterZ);
                if (!NavMesh.SamplePosition(p, out var h, 3f, NavMesh.AllAreas)) continue;
                float d = Flat(h.position - from);
                if (d > bd) { bd = d; best = h.position; }
            }
            return best;
        }

        static void PlaceAt(Candelabro c)
        {
            Vector3 cp = c.transform.position;
            var room = FloorDirector.I.cur.RoomAt(cp);
            Vector3 toCenter = room != null ? new Vector3(room.CenterX - cp.x, 0f, room.CenterZ - cp.z) : Vector3.zero;
            Vector3 pos = NavPoint(cp + (toCenter.sqrMagnitude > 0.5f ? toCenter.normalized : Vector3.right) * 1.3f);
            Vector3 look = cp - pos;
            var pl = GameManager.I.player;
            pl.Teleport(pos);
            pl.yaw = Mathf.Atan2(look.x, look.z);
            pl.pitch = 0f;
        }

        /// <summary>Il giocatore di questa macchina non muore ne' si gira da solo (solo la prova decide).</summary>
        static void Safe(bool on = true)
        {
            PlayerController.holdLook = true;
            var pl = GameManager.I.player;
            if (pl != null) pl.invulnerable = on;
        }

        IEnumerator Until(System.Func<bool> cond, float timeout)
        {
            float t0 = Time.time;
            while (!cond() && Time.time - t0 < timeout) yield return null;
        }

        /// <summary>Uccide (con colpi a distanza, dai pupazzi come dai nemici veri) i nemici del round che da 'minAge' secondi sono in vista,
        /// finche' il round non finisce o scade 'timeout'. Lascia in fightKills quanti colpi mortali ha dato.</summary>
        int fightKills;
        IEnumerator Fight(RoundDirector rd, float minAge, float timeout)
        {
            var seen = new Dictionary<Enemy, float>();
            var claimed = new Dictionary<Enemy, float>();
            fightKills = 0;
            float t0 = Time.time;
            while ((SurvivalMode.Enabled ? rd.Fighting : rd.Sealed) && Time.time - t0 < timeout)
            {
                foreach (var e in new List<Enemy>(Enemy.All))
                {
                    if (e == null || e.IsDead || !e.relentless) continue;
                    if (!seen.TryGetValue(e, out float t1)) { seen[e] = Time.time; continue; }
                    if (Time.time - t1 < minAge) continue;
                    if (claimed.TryGetValue(e, out float tc) && Time.time - tc < 1.5f) continue;
                    claimed[e] = Time.time;
                    e.Damage(100000f, e.AimPoint, false, true, DamageSource.Gun);
                    fightKills++;
                }
                yield return new WaitForSeconds(0.3f);
            }
        }

        static bool HasArg(string a) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), a) >= 0;
        static readonly bool Late = HasArg("-cooplate"), Leave = HasArg("-coopleave"), Crash = HasArg("-coopcrash"), HostLeave = HasArg("-coophostleave"), Pics = HasArg("-cooppics");

        // ------------------------------------------------------------------ il copione
        IEnumerator Run()
        {
            // col ciclo a ondate (predefinito) le candele non ci sono: al posto di candela e round (S5, S6) si compra un'area e si giocano tre ondate
            bool survival = SurvivalMode.Enabled;
            System.Func<IEnumerator>[] steps =
                Leave || Crash ? new System.Func<IEnumerator>[] { S0, S1, E2 } :
                HostLeave ? new System.Func<IEnumerator>[] { S0, S1, H2 } :
                Pics ? new System.Func<IEnumerator>[] { S0, S1, P0 } :
                Late ? (survival ? new System.Func<IEnumerator>[] { L0s, L1s, L2s } : new System.Func<IEnumerator>[] { L0, L1, L2 }) :
                survival ? new System.Func<IEnumerator>[] { S0, S1, S2, S3, S4, S4Flight, V5, V6, S7, V7, S8 } :
                new System.Func<IEnumerator>[] { S0, S1, S2, S3, S4, S4Flight, S5, S6, S7, S8 };
            var it = Scenario(steps);
            while (true)
            {
                object cur;
                try { if (!it.MoveNext()) break; cur = it.Current; }
                catch (System.Exception ex) { Debug.LogError("[PryingEyes] cooptest: eccezione nella prova: " + ex); failed++; aborted = true; break; }
                yield return cur;
            }
            // un attimo prima di chiudere, perche' l'ultimo messaggio di sincronia parta davvero; l'ospite chiude per ultimo
            yield return new WaitForSeconds(Coop.IsClient ? 1.5f : 3f);
            Finish();
        }

        IEnumerator Scenario(System.Func<IEnumerator>[] steps)
        {
            for (int i = 0; i < steps.Length; i++)
            {
                Log($"--- passo {i}");
                yield return steps[i]();
                if (aborted) yield break;
                if (i == steps.Length - 1 && (Leave || Crash || HostLeave)) yield break;      // chi doveva andarsene e' gia' andato
                yield return Barrier();
                if (aborted) yield break;
            }
        }

        // 0 ------------------------------------------------------------------ piano costruito, compagno in vista
        IEnumerator S0()
        {
            var g = G;
            float t0 = Time.time;
            while (Time.time - t0 < 90f && !(g.FloorBuilt && (Coop.IsHost || g.Started) && ShownCount() >= Expected - 1 && !FloorDirector.I.transitioning)) yield return null;
            bool ok = g.FloorBuilt && ShownCount() >= Expected - 1;
            Check("piano costruito e compagni in vista", ok, $"{Time.time - t0:F1} s, piano {FloorDirector.I.floor}, epoca {g.Epoch}, vedo {ShownCount()} compagni su {Expected - 1}");
            if (!ok) { aborted = true; yield break; }
            if (Coop.IsHost) { string a = Coop.LocalAddresses(); Log($"info: indirizzi che il menu di pausa mostra agli amici: {(a.Length == 0 ? 0 : a.Split(',').Length)}"); }
            Safe();
            Drops.disabled = true;
            yield return new WaitForSeconds(2f);
        }

        // P ------------------------------------------------------------------ foto del compagno (-cooppics; l'ospite dev'essere senza -nographics)
        /// <summary>Dove vanno le foto: -coopout cartella (lo passa run_coop.ps1), altrimenti persistentDataPath/coop.</summary>
        static string PicDir
        {
            get
            {
                var a = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-coopout") return a[i + 1];
                return System.IO.Path.Combine(Application.persistentDataPath, "coop");
            }
        }

        /// <summary>L'ospitato accende la torcia e sta fermo; l'ospite lo fotografa di lato, da vicino e da dietro (PicDir/coop_*.png):
        /// serve a vedere com'e' fatto un compagno (corpo, nome sopra la testa, fascio della torcia) che le prove senza grafica non mostrano.</summary>
        IEnumerator P0()
        {
            Safe();
            if (!Coop.IsHost)
            {
                if (Torch.I != null && !Torch.I.Lit) Torch.I.Toggle();
                GameManager.I.player.pitch = 0.35f;         // guarda un po' in basso (positivo = giu'): il fascio deve scendere, non salire
                yield return new WaitForSeconds(1f);
                yield break;
            }
            yield return new WaitForSeconds(2.5f);          // la torcia dell'ospitato arriva e la posa si assesta
            G.Players.TryGetValue(1, out var rp);
            bool seen = rp != null && rp.Shown;
            Check("il compagno e' in vista", seen);
            if (!seen) { aborted = true; yield break; }
            Check("la torcia del compagno e' accesa (lo dice la rete)", rp.last.torch);
            var bt = rp.BeamT;
            Check("il fascio del compagno scende se lui guarda in basso", bt != null && bt.forward.y < -0.2f, bt != null ? $"componente verticale {bt.forward.y:F2}" : "nessun fascio");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Log("info: senza grafica (-nographics) niente foto"); yield break; }
            System.IO.Directory.CreateDirectory(PicDir);
            Vector3 peer = rp.transform.position;
            Vector3 fwd = rp.transform.forward; fwd.y = 0f; fwd.Normalize();
            yield return Photo(StandAt(peer, Vector3.Cross(Vector3.up, fwd), 3f), peer + Vector3.up * 1.2f, "lato");
            yield return Photo(StandAt(peer, fwd + Vector3.Cross(Vector3.up, fwd) * 0.3f, 1.6f), peer + Vector3.up * 1.55f, "vicino");
            yield return Photo(StandAt(peer, -fwd, 2.6f), peer + Vector3.up * 1.3f, "dietro");
        }

        /// <summary>Un punto navigabile a 'dist' metri da 'center' verso 'dir' (o il piu' vicino a quella direzione che si possa raggiungere).</summary>
        static Vector3 StandAt(Vector3 center, Vector3 dir, float dist)
        {
            dir.y = 0f;
            dir = dir.sqrMagnitude < 0.01f ? Vector3.forward : dir.normalized;
            foreach (float turn in new[] { 0f, 25f, -25f, 50f, -50f, 80f, -80f, 120f, -120f, 180f })
            {
                var q = center + Quaternion.Euler(0f, turn, 0f) * dir * dist;
                if (NavMesh.SamplePosition(q, out var h, 0.6f, NavMesh.AllAreas) && Flat(h.position - center) > dist * 0.7f) return h.position;
            }
            return PlaceNear(center, dist, out Vector3 p) ? p : center;
        }

        IEnumerator Photo(Vector3 stand, Vector3 look, string name)
        {
            var pl = GameManager.I.player;
            pl.Teleport(stand);
            Vector3 d = look - (stand + Vector3.up * 1.6f);
            pl.yaw = Mathf.Atan2(d.x, d.z);
            pl.pitch = -Mathf.Atan2(d.y, Mathf.Sqrt(d.x * d.x + d.z * d.z));      // pitch positivo = guarda in basso
            yield return new WaitForSeconds(0.8f);
            string path = System.IO.Path.Combine(PicDir, "coop_" + name + ".png");
            GameManager.CamShot(path);
            Check("foto " + name, System.IO.File.Exists(path), $"a {Flat(stand - look):F1} m dal compagno");
        }

        // 1 ------------------------------------------------------------------ i nemici del piano sono pupazzi uguali
        IEnumerator S1()
        {
            Safe();
            if (Coop.IsHost) SendEnemies();
            else
            {
                yield return ExpectEnemies("i pupazzi coincidono con i nemici dell'ospite", 10f);
                bool allPuppets = true;
                foreach (var e in Enemy.All) if (e != null && (!e.puppet || e.GetComponent<NavMeshAgent>() != null)) allPuppets = false;
                Check("sull'ospitato i nemici sono pupazzi senza intelligenza ne' navmesh", allPuppets, Enemy.All.Count + " pupazzi");
            }
        }

        // 2 ------------------------------------------------------------------ un nemico nato sull'ospite compare e si muove
        IEnumerator S2()
        {
            Safe();
            if (Coop.IsHost)
            {
                bool placed = PlaceNear(PeerPos(), 8f, out Vector3 at);
                Check("nemico della prova su un percorso aperto verso il compagno", placed);
                if (!placed) { aborted = true; yield break; }
                spawned = Enemy.Spawn(Enemy.EnemyType.Monaco, at);
                Log($"nemico {spawned.netId} nato a {at.x:F1} {at.z:F1}");
                yield return new WaitForSeconds(2.5f);
                SendEnemies();
            }
            else
            {
                var before = new Dictionary<int, Vector3>();
                yield return ExpectEnemies("il nemico nato sull'ospite compare qui come pupazzo", 10f);
                foreach (var kv in G.EnemyMap) if (kv.Value != null) before[kv.Key] = kv.Value.transform.position;
                float t0 = Time.time, moved = 0f;
                while (Time.time - t0 < 8f && moved < 0.6f)
                {
                    foreach (var kv in G.EnemyMap)
                        if (kv.Value != null && before.TryGetValue(kv.Key, out var p0)) moved = Mathf.Max(moved, Vector3.Distance(p0, kv.Value.transform.position));
                    yield return new WaitForSeconds(0.25f);
                }
                Check("i pupazzi si muovono con l'ospite", moved >= 0.6f, $"spostamento massimo {moved:F1} m");
            }
        }

        // 3 ------------------------------------------------------------------ colpi e uccisione dell'ospitato
        IEnumerator S3()
        {
            Safe();
            var gm = GameManager.I;
            if (Coop.IsHost)
            {
                hostScore = gm.score; hostKills = gm.kills;
                // il resto lo fa l'ospitato; il controllo sull'ospite viene dopo la sincronia (vedi S4)
                yield return new WaitForSeconds(0.5f);
                yield break;
            }
            if (!Primary)
            {
                // gli altri ospitati guardano: i colpi e i punti di un compagno non cambiano i loro
                int p0 = gm.score, q0 = gm.kills;
                yield return new WaitForSeconds(8f);
                Check("i colpi e le uccisioni di un compagno non cambiano i miei punti", gm.score == p0 && gm.kills == q0, $"{gm.score - p0:+0;-0;0} punti, {gm.kills - q0:+0;-0;0} uccisioni");
                yield break;
            }
            Enemy target = null;
            int tid = -1;
            foreach (var kv in G.EnemyMap) if (kv.Key > tid && kv.Value != null && !kv.Value.IsDead) { tid = kv.Key; target = kv.Value; }
            Check("c'e' un pupazzo da colpire", target != null, "id " + tid);
            if (target == null) yield break;
            int s0 = gm.score, k0 = gm.kills;
            for (int i = 0; i < 3; i++)
            {
                target.Damage(10f, target.AimPoint, false, true, DamageSource.Gun);
                yield return new WaitForSeconds(0.35f);
            }
            yield return new WaitForSeconds(1.0f);
            Check("tre colpi a segno: +10 punti l'uno a chi spara", gm.score - s0 == 3 * Punti.Colpo, $"+{gm.score - s0} punti");
            target.Damage(100000f, target.AimPoint, true, true, DamageSource.Gun);
            yield return Until(() => target == null || target.IsDead, 5f);
            yield return new WaitForSeconds(1.0f);
            Check("il colpo mortale: il pupazzo muore (lo dice l'ospite)", target == null || target.IsDead);
            Check("l'uccisione alla testa paga 100 a chi ha sparato e conta", gm.score - s0 == 3 * Punti.Colpo + Punti.Testa && gm.kills == k0 + 1, $"+{gm.score - s0} punti, uccisioni +{gm.kills - k0}");
        }

        // 4 ------------------------------------------------------------------ un nemico dell'ospite ferisce l'ospitato
        IEnumerator S4()
        {
            var gm = GameManager.I;
            var pl = gm.player;
            Enemy biter = null;
            if (Coop.IsHost)
            {
                Check("il nemico colpito dal compagno e' morto anche sull'ospite", spawned == null || spawned.IsDead);
                Check("i punti e le uccisioni del compagno non sono dell'ospite", gm.score == hostScore && gm.kills == hostKills, $"punti {gm.score - hostScore:+0;-0;0}, uccisioni {gm.kills - hostKills:+0;-0;0}");
                Safe();
                Vector3 peer = PeerPos();
                pl.Teleport(FarPoint(peer));                       // lontano: il bersaglio piu' vicino del nemico e' il compagno
                yield return new WaitForSeconds(1.5f);
                if (PlaceNear(PeerPos(), 1.4f, out Vector3 at))
                {
                    biter = Enemy.Spawn(Enemy.EnemyType.Monaco, at);
                    biter.MakeRelentless();
                }
                Check("nemico piazzato accanto al compagno", biter != null);
                yield return new WaitForSeconds(14f);              // il tempo di colpire (l'ospitato misura dall'altra parte)
            }
            else if (!Primary)
            {
                Safe();
                yield return new WaitForSeconds(3f);
            }
            else
            {
                Safe(false);
                float hp0 = pl.hp + pl.shield;
                bool sawMelee = false;
                yield return Until(() =>
                {
                    foreach (var enemy in Enemy.All)
                    {
                        if (enemy == null || !enemy.PreparingMelee) continue;
                        var animator = enemy.GetComponentInChildren<Animator>();
                        if (animator != null)
                            foreach (var clip in animator.GetCurrentAnimatorClipInfo(0)) if (clip.clip.name == "mischia") sawMelee = true;
                    }
                    return pl.hp + pl.shield < hp0 - 0.01f;
                }, 20f);
                Check("il client vede la preparazione della nuova mischia", sawMelee);
                bool hurt = pl.hp + pl.shield < hp0 - 0.01f;
                Check("il nemico dell'ospite mi ferisce (HurtPlayer)", hurt, $"da {hp0:F0} a {pl.hp + pl.shield:F0}");
                Check("la provenienza del danno arriva al client", gm.hud != null && gm.hud.DamageDirectionCount > 0);
                Safe();
                pl.Heal(1000f);
                yield return new WaitForSeconds(2f);
            }
            if (Coop.IsHost && biter != null) Enemy.Remove(biter);
        }

        IEnumerator S4Flight()
        {
            var player=GameManager.I.player;
            Enemy attacker=null;
            if(Coop.IsHost)
            {
                Safe();player.Teleport(FarPoint(PeerPos()));
                yield return new WaitForSeconds(1);
                if(PlaceNear(PeerPos(),1.5f,out var point))
                {
                    attacker=Enemy.Spawn(Enemy.EnemyType.Veglia,point+Vector3.up*1.35f);
                    attacker.MakeRelentless();
                }
                Check("Veglia piazzata accanto al compagno",attacker!=null);
                yield return new WaitForSeconds(8);
                if(attacker!=null)Enemy.Remove(attacker);
            }
            else if(!Primary){Safe();yield return new WaitForSeconds(2);}
            else
            {
                Safe(false);float health=player.hp+player.shield;
                int previousId=-1;
                foreach(var enemy in Enemy.All)if(enemy!=null)previousId=Mathf.Max(previousId,enemy.netId);
                bool sawPreparation=false,hasStart=false;Vector3 start=Vector3.zero;
                yield return Until(()=>
                {
                    foreach(var enemy in Enemy.All)
                    {
                        if(enemy==null||enemy.type!=Enemy.EnemyType.Veglia||enemy.netId<=previousId)continue;
                        attacker=enemy;
                        if(!hasStart){start=enemy.transform.position;hasStart=true;}
                        sawPreparation|=enemy.PreparingFlight;
                    }
                    return player.hp+player.shield<health-0.01f;
                },12);
                Check("il client vede la preparazione dell'affondo volante",sawPreparation);
                Check("l'affondo dell'ospite ferisce il client",player.hp+player.shield<health-0.01f);
                yield return new WaitForSeconds(0.25f);
                Check("il client vede lo spostamento fisico dell'affondo",attacker!=null&&hasStart&&Vector3.Distance(start,attacker.transform.position)>0.3f);
                Check("l'affondo ricevuto mostra la provenienza del danno",GameManager.I.hud.DamageDirectionCount>0);
                Safe();player.Heal(1000);
            }
        }

        // 5 ------------------------------------------------------------------ candela accesa dall'ospitato
        IEnumerator S5()
        {
            Safe();
            var fd = FloorDirector.I;
            var rd = RoundDirector.I;
            var gm = GameManager.I;
            int idx = fd.cur.candles.FindIndex(c => c != null && c.objective && !c.lit && (rd == null || !rd.HasZone(c)));
            if (idx < 0) { Log("info: nessuna candela senza zona su questo piano, passo saltato"); yield break; }
            var cand = fd.cur.candles[idx];
            int s0 = gm.score;
            if (Coop.IsHost)
            {
                // l'ospite torna dal compagno e aspetta che accenda la candela
                yield return Until(() => cand.lit, 25f);
                Check("la candela accesa dal compagno e' accesa anche sull'ospite, a suo nome", cand.lit && cand.litBy == 1, $"litBy {cand.litBy}");
                yield return new WaitForSeconds(1.5f);
                Check("il premio della candela non e' dell'ospite", gm.score == s0, $"{gm.score - s0:+0;-0;0} punti");
            }
            else if (!Primary)
            {
                // gli altri ospitati: la candela del compagno si accende anche da me, ma il premio non e' mio
                yield return Until(() => cand.lit, 25f);
                Check("la candela accesa dal compagno e' accesa anche qui, a suo nome", cand.lit && cand.litBy == 1, $"litBy {cand.litBy}");
                yield return new WaitForSeconds(1.5f);
                Check("il premio della candela non e' mio", gm.score == s0, $"{gm.score - s0:+0;-0;0} punti");
            }
            else
            {
                PlaceAt(cand);
                yield return new WaitForSeconds(1.5f);
                cand.Use();                                         // dall'ospitato: la chiede, decide l'ospite
                yield return Until(() => cand.lit, 10f);
                Check("la candela si accende (la chiedo all'ospite, lui la accende e me lo dice)", cand.lit && cand.litBy == Coop.Slot, $"lit {cand.lit}, litBy {cand.litBy}");
                yield return new WaitForSeconds(1.5f);
                Check("il premio della candela e' mio (60 punti e piu')", gm.score - s0 >= 60, $"+{gm.score - s0} punti");
            }
        }

        // 6 ------------------------------------------------------------------ un round: serrande, nemici, premio per tutti
        IEnumerator S6()
        {
            Safe();
            var fd = FloorDirector.I;
            var rd = RoundDirector.I;
            var gm = GameManager.I;
            int zi = rd == null ? -1 : fd.cur.candles.FindIndex(c => c != null && c.objective && !c.lit && rd.HasZone(c));
            if (zi < 0) { Log("info: nessuna candela con zona sigillabile su questo piano, passo saltato"); yield break; }
            var zc = fd.cur.candles[zi];
            var reg = rd.RegionOf(zc);

            // via tutti i nemici dall'ospite (anche quelli richiamati dalla candela di prima): spariscono anche sugli ospitati
            if (Coop.IsHost)
            {
                foreach (var e in new List<Enemy>(Enemy.All)) if (e != null) Enemy.Remove(e);
                yield return new WaitForSeconds(1.5f);
                SendEnemies();
            }
            else yield return ExpectEnemies("tolti tutti i nemici dall'ospite, spariscono anche i pupazzi", 10f);

            PlaceAt(zc);                                            // tutti e due dentro la zona, accanto alla candela
            yield return new WaitForSeconds(1.5f);
            int round0 = RoundDirector.Round, s0 = gm.score;        // prima della sincronia: dopo, l'ospitato puo' gia' averla accesa
            yield return Barrier();
            if (aborted) yield break;

            if (!Coop.IsHost && Primary) zc.Use();                  // la accende l'ospitato: parte il round dell'ospite
            yield return Until(() => rd.Sealed, 12f);
            Check("il round parte su entrambi (serrande calate)", rd.Sealed, $"round {RoundDirector.Round}");
            if (!rd.Sealed) yield break;
            roundNo = RoundDirector.Round;
            Check("stesso numero di round su entrambi", RoundDirector.Round == round0 + 1, $"round {RoundDirector.Round} (prima {round0})");
            yield return new WaitForSeconds(2.5f);
            bool down = true;
            foreach (var s in rd.Shutters) if (!s.IsDown) down = false;
            Check("serrande calate su tutte le aperture della zona", rd.Shutters.Count == reg.cuts.Count && down, $"{rd.Shutters.Count}/{reg.cuts.Count}");

            // si combatte: l'ospitato uccide i pupazzi (colpi a distanza), l'ospite solo quelli che restano troppo
            float t0 = Time.time;
            yield return Fight(rd, Coop.IsHost ? 14f : 0.8f, 150f);
            Check("il round finisce da solo, serrande su, su entrambi", !rd.Sealed && rd.Shutters.Count == 0, $"{Time.time - t0:F0} s, colpi mortali dati qui {fightKills}");
            yield return new WaitForSeconds(2f);
            int reward = RoundRules.Reward(roundNo);
            // il premio del round e' di tutti; la candela (60 punti) e le uccisioni solo di chi le ha fatte (l'ospitato)
            int expectMin = Coop.IsHost || !Primary ? reward : reward + 60;
            Check("il premio del round e' di tutti, quello della candela di chi l'ha accesa", gm.score - s0 >= expectMin, $"+{gm.score - s0} punti (premio {reward})");
            Check("la candela e' accesa su entrambi, a nome dell'ospitato", zc.lit && zc.litBy == 1, $"lit {zc.lit}, litBy {zc.litBy}");
        }

        // 7 ------------------------------------------------------------------ una goccia presa dall'ospitato vale per tutti
        IEnumerator S7()
        {
            Safe();
            if (Coop.IsHost)
            {
                Vector3 peer = PeerPos();
                GameManager.I.player.Teleport(FarPoint(peer));
                yield return new WaitForSeconds(1.5f);
                var pk = Drops.Spawn(DropKind.Double, PeerPos(), false);
                Log($"goccia {pk.netId} ({pk.kind}) a {pk.transform.position.x:F1} {pk.transform.position.z:F1}, il compagno e' a {Flat(pk.transform.position - PeerPos()):F1} m");
                yield return Until(() => pk == null, 15f);
                Check("la goccia presa dal compagno sparisce sull'ospite", pk == null);
                yield return Until(() => Drops.DoublePoints, 3f);
                Check("l'effetto della goccia vale anche per l'ospite", Drops.DoublePoints, $"{Drops.DoubleLeft:F0} s");
            }
            else
            {
                yield return Until(() => Drops.Live.Count > 0, 10f);
                Check("la goccia nata sull'ospite compare anche qui", Drops.Live.Count > 0 || Drops.DoublePoints);
                yield return Until(() => Drops.DoublePoints, 15f);
                Check("la prendo e l'effetto vale anche per me", Drops.DoublePoints, $"{Drops.DoubleLeft:F0} s");
                yield return Until(() => Drops.Live.Count == 0, 3f);
                Check("la goccia sparisce anche qui", Drops.Live.Count == 0, Drops.Live.Count + " rimaste");
            }
        }

        // L ------------------------------------------------------------------ -cooplate: il compagno entra quando il round e' gia' in corso
        IEnumerator L0()
        {
            var g = G;
            var fd = FloorDirector.I;
            var rd = RoundDirector.I;
            if (Coop.IsHost)
            {
                yield return Until(() => g.FloorBuilt && !fd.transitioning, 90f);
                Safe();
                Drops.disabled = true;
                yield return new WaitForSeconds(1f);
                int zi = rd == null ? -1 : fd.cur.candles.FindIndex(c => c != null && c.objective && !c.lit && rd.HasZone(c));
                if (zi < 0) { Log("nessuna candela con zona sigillabile su questo piano: prova non applicabile"); aborted = true; yield break; }
                var zc = fd.cur.candles[zi];
                PlaceAt(zc);
                yield return new WaitForSeconds(1f);
                zc.Use();
                yield return Until(() => rd.Sealed, 6f);
                Check("il round parte con l'ospite da solo", rd.Sealed, $"round {RoundDirector.Round}");
                yield return Until(PeerShown, 90f);
                Check("il compagno entra a round in corso", PeerShown() && rd.Sealed, $"round sigillato {rd.Sealed}");
            }
            else
            {
                yield return Until(() => g.FloorBuilt && g.Started && PeerShown() && !fd.transitioning, 90f);
                Check("entrato: piano costruito e ospite in vista", g.FloorBuilt && PeerShown());
                Safe();
            }
        }

        IEnumerator L1()
        {
            var rd = RoundDirector.I;
            Safe();
            if (Coop.IsHost)
            {
                yield return new WaitForSeconds(3f);
                SendEnemies();
                yield break;
            }
            yield return Until(() => rd.Sealed, 10f);
            Check("arrivato a round in corso: le serrande sono calate", rd.Sealed, $"round {RoundDirector.Round}");
            yield return new WaitForSeconds(2.5f);
            bool down = rd.Shutters.Count > 0;
            foreach (var s in rd.Shutters) if (!s.IsDown) down = false;
            Check("tutte le serrande sono gia' giu'", down, rd.Shutters.Count + " serrande");
            Check("il numero del round e' quello dell'ospite (il primo)", RoundDirector.Round == 1, "round " + RoundDirector.Round);
            yield return ExpectEnemies("i nemici del round in corso sono qui come pupazzi uguali", 10f);
        }

        IEnumerator L2()
        {
            var rd = RoundDirector.I;
            Safe();
            float t0 = Time.time;
            yield return Fight(rd, Coop.IsHost ? 14f : 0.8f, 150f);
            Check("il round finisce da solo, serrande su, anche per chi e' entrato dopo", !rd.Sealed && rd.Shutters.Count == 0, $"{Time.time - t0:F0} s, colpi mortali {fightKills}");
        }

        // E ------------------------------------------------------------------ -coopleave / -coopcrash: l'ospitato se ne va, l'ospite prosegue da solo
        IEnumerator E2()
        {
            var gm = GameManager.I;
            if (Coop.IsClient)
            {
                yield return new WaitForSeconds(1.5f);                 // l'ultima sincronia deve partire davvero prima che io sparisca
                Log(Crash ? "me ne vado di colpo (processo ucciso)" : "me ne vado (chiusura normale)");
                Finish();
                if (Crash) { System.Threading.Thread.Sleep(400); System.Diagnostics.Process.GetCurrentProcess().Kill(); }
                yield break;
            }
            var g = G;
            var fd = FloorDirector.I;
            var rd = RoundDirector.I;
            yield return Until(() => g.Players.Count == 0, 40f);
            Check("il compagno se ne e' andato: il suo pupazzo non c'e' piu' sull'ospite", g.Players.Count == 0, g.Players.Count + " giocatori");
            yield return new WaitForSeconds(0.3f);                  // Destroy lavora a fine fotogramma: il bersaglio esce dall'elenco subito dopo
            Check("i nemici hanno di nuovo un solo bersaglio", Targets.Count == 1 && Targets.AnyAlive, "bersagli " + Targets.Count);
            // da solo il gioco prosegue: un nemico da' la caccia all'ospite, poi un round intero
            var pl = gm.player;
            Safe();
            Enemy chaser = null;
            if (PlaceNear(pl.transform.position, 7f, out Vector3 at)) { chaser = Enemy.Spawn(Enemy.EnemyType.Monaco, at); chaser.MakeRelentless(); }
            float d0 = chaser != null ? Flat(chaser.transform.position - pl.transform.position) : 0f;
            yield return new WaitForSeconds(6f);
            float d1 = chaser != null && !chaser.IsDead ? Flat(chaser.transform.position - pl.transform.position) : 0f;
            Check("un nemico caccia l'ospite da solo (si avvicina)", chaser != null && d1 < d0 - 1.5f, $"da {d0:F1} a {d1:F1} m");
            if (chaser != null) Enemy.Remove(chaser);
            if (SurvivalMode.Enabled) { yield return SoloWave(); yield break; }       // col ciclo a ondate: un'ondata intera da soli
            int zi = rd == null ? -1 : fd.cur.candles.FindIndex(c => c != null && c.objective && !c.lit && rd.HasZone(c));
            if (zi < 0) { Log("info: nessuna candela con zona sigillabile: round saltato"); yield break; }
            PlaceAt(fd.cur.candles[zi]);
            yield return new WaitForSeconds(1f);
            fd.cur.candles[zi].Use();
            yield return Until(() => rd.Sealed, 6f);
            Check("il round parte da solo dopo l'uscita del compagno", rd.Sealed);
            yield return Fight(rd, 1f, 120f);
            Check("e finisce", !rd.Sealed, $"colpi mortali {fightKills}");
        }

        // H ------------------------------------------------------------------ -coophostleave: l'ospite se ne va, l'ospitato prosegue da solo
        IEnumerator H2()
        {
            var gm = GameManager.I;
            if (Coop.IsHost)
            {
                yield return new WaitForSeconds(1.5f);                 // l'ultima sincronia deve partire davvero prima che io chiuda
                Log("chiudo la partita");
                Finish();
                yield break;
            }
            yield return Until(() => !Coop.Active, 40f);
            Check("persa la connessione: si prosegue da solo", !Coop.Active && Coop.Game == null, "ruolo " + Coop.Role);
            yield return new WaitForSeconds(1f);
            int pupazzi = 0;
            foreach (var e in Enemy.All) if (e != null && e.puppet) pupazzi++;
            Check("i pupazzi dell'ospite sono spariti", pupazzi == 0, pupazzi + " rimasti");
            float f0 = Time.frameCount;
            yield return new WaitForSeconds(3f);
            Check("il gioco continua a girare e il giocatore e' vivo", Time.frameCount > f0 + 10 && gm.player.alive);
            Finish();
        }

        // 8 ------------------------------------------------------------------ uscita e discesa insieme
        IEnumerator S8()
        {
            Safe();
            var fd = FloorDirector.I;
            var g = G;
            int floor0 = fd.floor, epoch0 = g.Epoch;
            if (Coop.IsHost) fd.DebugOpenExit();
            yield return Until(() => fd.ExitOpen, 10f);
            Check("l'uscita e' aperta (anche sull'ospitato, lo dice l'ospite)", fd.ExitOpen && fd.cur.exit != null && fd.cur.exit.unlocked);
            yield return Barrier();
            if (aborted) yield break;
            if (!Coop.IsHost && Primary) fd.cur.exit.Use();         // l'ospitato entra nell'ascensore: la discesa la decide l'ospite
            yield return Until(() => fd.floor == floor0 + 1 && g.FloorBuilt && !fd.transitioning && ShownCount() >= Expected - 1, 150f);
            Check("scendiamo insieme: piano nuovo e nuova epoca", fd.floor == floor0 + 1 && g.Epoch == epoch0 + 1, $"piano {fd.floor}, epoca {g.Epoch}");
            if (fd.floor != floor0 + 1) { aborted = true; yield break; }
            Safe();
            int mine = fd.FloorSignature();
            if (Coop.IsHost) g.SendTest(w.Reset().U8(3).I32(mine).I32(fd.floor).ToArray());
            else
            {
                yield return Until(() => sigSeq > 0 && sigFloor == fd.floor, 10f);
                Check("il piano nuovo ha la stessa firma sull'ospite e sull'ospitato", sigSeq > 0 && sigValue == mine, $"mia {mine:X8}, dell'ospite {sigValue:X8}");
            }
            yield return new WaitForSeconds(3f);
            Check("a destinazione i compagni sono vicini", Flat(PeerPos() - GameManager.I.player.transform.position) < 4f, $"{Flat(PeerPos() - GameManager.I.player.transform.position):F1} m");
            if (SurvivalMode.Enabled) CheckFreshFloor();
            if (Coop.IsHost) SendEnemies();
            else yield return ExpectEnemies("i pupazzi del piano nuovo coincidono con i nemici dell'ospite", 12f);
        }
    }
}
