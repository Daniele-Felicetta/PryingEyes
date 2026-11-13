using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Proc
{
    /// <summary>Bot di prova rigoroso (-autotest -procbot -botstrict): come BotTick, ma senza teletrasporti di recupero e con le
    /// interazioni che passano da Interact (raggio, direzione dello sguardo, visuale libera) come il tasto F del giocatore.
    /// Cammina col CharacterController vero. Ogni incastro, candela irraggiungibile, caduta, morte o piano troppo lento finisce nel
    /// file di -botlog; l'ultima riga ("ESITO: ok" o "ESITO: FALLITO") decide anche il codice di uscita del processo.
    /// Opzioni: -botfloors K (piani da completare, 1), -botcap S (secondi di gioco per piano, 420), -procsecs S (tetto totale, 900),
    /// -botscale N (moltiplica il tempo di gioco, 1..8), -botlog FILE, -noenemies (piano vuoto: misura la sola navigazione).</summary>
    public partial class FloorDirector
    {
        const float StuckSecs = 4f, StuckMove = 0.35f, StartGrace = 8f;

        bool botSInit, botSDone;
        int botSFloor = -1, botSChain, botSAvvisi, botSFails, botSFloorsDone, botSFloorStuck, botSFalls0, botSDeathsSeen, botSTries;
        int botSScale = 1, botSFloorsGoal = 1, botSCap = 420, botSLimit = 900;
        float botSFloorT, botSWaitT, botSProgT, botSCrouchT, botSNearT, botSBadPathT, botSLitT;
        Vector3 botSProgPos;
        string botSFile;
        readonly HashSet<Candelabro> botSBlack = new HashSet<Candelabro>();
        // ultimi fotogrammi (x, z, direzione x, direzione z): se il bot risulta fermo, il tracciato dice se oscilla fra due strade o se e' bloccato davvero
        readonly List<Vector4> botSTrace = new List<Vector4>();
        Vector2 botSPrevHeading, botSHold;
        float botSHoldT;
        int botSFlips, botSGaps;
        readonly List<GameObject> botSCarves = new List<GameObject>();

        /// <summary>Un varco che la navmesh promette ma il controller non passa (fra arredi a meno di 0,92 m): lo si toglie dalla navmesh con un ostacolo
        /// che scava, cosi' il bot cerca un'altra strada come farebbe una persona. Se un'altra strada c'e' il piano si completa con un avviso VARCO;
        /// se non c'e', e' un vero soft-lock e la prova fallisce.</summary>
        void BotCarve(Vector3 at)
        {
            var go = new GameObject("BotVarco") { hideFlags = HideFlags.DontSave };
            go.transform.position = at;
            var ob = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            ob.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            ob.size = new Vector3(1.4f, 2f, 1.4f);
            ob.carving = true;
            ob.carveOnlyStationary = false;
            botSCarves.Add(go);
            botSGaps++;
        }

        string BotFile => botSFile ??= ArgStr("-botlog", BotLog);

        void BotLine(string s)
        {
            Debug.Log("[Bot] " + s);
            try { System.IO.File.AppendAllText(BotFile, s + "\n"); } catch { }
        }

        void BotEnd(bool ok, string why)
        {
            if (botSDone) return;
            botSDone = true;
            BotLine($"{(ok ? "ESITO: ok" : "ESITO: FALLITO")} | {why} | piani completati {botSFloorsDone}/{botSFloorsGoal}, avvisi {botSAvvisi}, varchi stretti {botSGaps}, fallimenti {botSFails}, tempo di gioco {botT:F0} s");
            Application.Quit(ok ? 0 : 1);
        }

        void BotFloorStart()
        {
            botSFloor = floor;
            botSFloorT = botSWaitT = botSProgT = botSNearT = botSBadPathT = 0f;
            botSChain = botSFloorStuck = botSTries = 0;
            botSLitT = -1f;
            botSHoldT = 0f; botSPrevHeading = Vector2.zero; botSFlips = 0; botSTrace.Clear();
            foreach (var g in botSCarves) if (g != null) Destroy(g);
            botSCarves.Clear();
            botSProgPos = gm.player.transform.position;
            botSBlack.Clear();
            var plan = cur.plan;
            BotLine($"PIANO {floor} seme={plan.seed} tema={plan.theme} griglia={(plan.grid ? "si" : "no")} stanze={plan.rooms.Count} candele={cur.candles.Count}");
            int navDataCount = Resources.FindObjectsOfTypeAll<NavMeshData>().Length;
            if (navDataCount != 1)
            {
                botSFails++;
                BotLine($"NAVMESH dati vivi {navDataCount}, atteso 1 al piano {floor}: risorse di un piano precedente non liberate");
            }
        }

        void BotGiveUp(Candelabro tc, string why)
        {
            botSFails++;
            if (tc == null) { BotEnd(false, $"USCITA irraggiungibile al piano {floor} ({why})"); return; }
            botSBlack.Add(tc);
            botSChain = botSTries = 0;
            botSBadPathT = botSNearT = botSProgT = 0f;
            botSProgPos = gm.player.transform.position;
            BotLine($"IRRAGGIUNGIBILE candela ({tc.transform.position.x:F1},{tc.transform.position.z:F1}) al piano {floor}: {why}");
        }

        /// <summary>Cosa tocca la capsula del giocatore: dice quale arredo o muro lo ferma.</summary>
        static string BotTouching(Vector3 pp)
        {
            var cols = Physics.OverlapSphere(pp + Vector3.up * 0.9f, 0.8f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var c in cols)
            {
                if (c.transform.IsChildOf(GameManager.I.player.transform)) continue;
                var par = c.transform.parent;
                if (n++ > 0) sb.Append(", ");
                sb.Append(c.name);
                if (par != null) sb.Append('<').Append(par.name);
                if (n >= 5) break;
            }
            return n == 0 ? "nessuno" : sb.ToString();
        }

        /// <summary>Quando BotTouching dice "nessuno" la causa e' altrove: qui si guarda la capsula con ogni livello (anche trigger e livello 2),
        /// il suolo sotto i piedi, lo stato del CharacterController e cosa sta chiedendo il bot.</summary>
        static string BotDeep(Vector3 pp, Vector3 next)
        {
            var pl = GameManager.I.player;
            var cc = pl.GetComponent<CharacterController>();
            float r = (cc != null ? cc.radius : 0.3f) + 0.3f, h = cc != null ? cc.height : 1.8f;
            var cols = Physics.OverlapCapsule(pp + Vector3.up * r, pp + Vector3.up * Mathf.Max(r, h - r), r, ~0, QueryTriggerInteraction.Collide);
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var c in cols)
            {
                if (c.transform.IsChildOf(pl.transform)) continue;
                if (n++ > 0) sb.Append(", ");
                var b = c.bounds;
                sb.Append(c.name).Append(c.transform.parent != null ? "<" + c.transform.parent.name : "").Append("[L").Append(c.gameObject.layer).Append(c.isTrigger ? ",trigger" : "").Append(c.attachedRigidbody != null ? ",rb" : "")
                  .Append(" ").Append(c.GetType().Name).Append(" x ").Append(b.min.x.ToString("F2")).Append("..").Append(b.max.x.ToString("F2")).Append(" z ").Append(b.min.z.ToString("F2")).Append("..").Append(b.max.z.ToString("F2"))
                  .Append(" y ").Append(b.min.y.ToString("F2")).Append("..").Append(b.max.y.ToString("F2")).Append(']');
                if (n >= 8) break;
            }
            string ground = Physics.Raycast(pp + Vector3.up * 0.5f, Vector3.down, out RaycastHit gh, 2f, ~0, QueryTriggerInteraction.Ignore)
                ? $"{gh.collider.name}[L{gh.collider.gameObject.layer}] a {gh.distance - 0.5f:F2} m" : "nessuno";
            Vector3 v = cc != null ? cc.velocity : Vector3.zero;
            var bd = PlayerController.botDir;
            return $"capsula allargata: {(n == 0 ? "vuota" : sb.ToString())}; cc raggio {(cc != null ? cc.radius : 0f):F2} pelle {(cc != null ? cc.skinWidth : 0f):F2} gradino {(cc != null ? cc.stepOffset : 0f):F2}, navmesh raggio {HotelNav.AgentRadius:F2} gradino {HotelNav.AgentClimb:F2} voxel {HotelNav.VoxelSize:F2}; " +
                   $"suolo: {ground}; cc a terra={(cc != null && cc.isGrounded)} flag={(cc != null ? cc.collisionFlags.ToString() : "-")} " +
                   $"v=({v.x:F1},{v.z:F1}); botDir=({bd.x:F1},{bd.z:F1}); prossimo angolo ({next.x:F1},{next.z:F1}) a {new Vector2(next.x - pp.x, next.z - pp.z).magnitude:F1} m; scala tempo {Time.timeScale:F1}, dt {Time.deltaTime:F3}";
        }

        /// <summary>Mappa dall'alto (celle da 25 cm, 12 m x 12 m) di cio' che il giocatore puo' davvero calpestare. '#' = il centro del controller non puo'
        /// stare li' (a meno di 46 cm = raggio + pelle da un collider tra 30 cm e 1,6 m), '.' = libero, 'o' = libero e sulla navmesh, 'X' = sulla navmesh ma
        /// vietato al controller (la navmesh promette un passaggio che non c'e'), 'P' = il giocatore, lettere = angoli del percorso.</summary>
        void BotMap(Vector3 pp)
        {
            const float step = 0.25f, half = 6f, r = 0.46f;
            int n = Mathf.RoundToInt(half * 2f / step);
            var corners = botPath != null ? botPath.corners : null;
            for (int row = n; row >= 0; row--)
            {
                var sb = new System.Text.StringBuilder();
                float z = pp.z - half + row * step;
                for (int col = 0; col <= n; col++)
                {
                    float x = pp.x - half + col * step;
                    char ch = '.';
                    var c = new Vector3(x, pp.y, z);
                    bool blocked = Physics.CheckCapsule(c + Vector3.up * (0.3f + r), c + Vector3.up * (1.6f - r), r, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
                    bool nav = UnityEngine.AI.NavMesh.SamplePosition(c, out _, 0.12f, UnityEngine.AI.NavMesh.AllAreas);
                    if (blocked) ch = nav ? 'X' : '#'; else if (nav) ch = 'o';
                    if (Mathf.Abs(x - pp.x) < step * 0.5f && Mathf.Abs(z - pp.z) < step * 0.5f) ch = 'P';
                    if (corners != null)
                        for (int i = 1; i < corners.Length && i < 20; i++)
                            if (Mathf.Abs(x - corners[i].x) < step * 0.5f && Mathf.Abs(z - corners[i].z) < step * 0.5f) ch = (char)('a' + i - 1);
                    sb.Append(ch);
                }
                BotLine($"    z{z,6:F2} {sb}");
            }
            BotLine($"    (x da {pp.x - half:F1} a {pp.x + half:F1}, una cella = {step} m)");
        }

        /// <summary>Gli ultimi fotogrammi in una riga (x,z>dirx,dirz, ogni terzo fotogramma) e gli angoli del percorso.</summary>
        string BotTraceText()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < botSTrace.Count; i += 6)
            {
                var t = botSTrace[i];
                sb.Append(t.x.ToString("F1", ci)).Append(',').Append(t.y.ToString("F1", ci)).Append('>').Append(t.z.ToString("F1", ci)).Append(',').Append(t.w.ToString("F1", ci)).Append(' ');
            }
            sb.Append("| angoli:");
            if (botPath != null && botPath.corners != null)
                for (int i = 0; i < botPath.corners.Length && i < 7; i++) sb.Append(' ').Append(botPath.corners[i].x.ToString("F1", ci)).Append(',').Append(botPath.corners[i].z.ToString("F1", ci));
            sb.Append(" | cambi di strada nella finestra: ").Append(botSFlips);
            return sb.ToString();
        }

        void BotStuck(Vector3 pp, Vector3 target, Candelabro tc, float horiz, Vector3 next, float moved)
        {
            botSChain++; botSFloorStuck++; botSAvvisi++;
            BotLine($"INCASTRATO piano {floor} #{botSChain} a ({pp.x:F1},{pp.y:F1},{pp.z:F1}) stanza={(playerRoom != null ? playerRoom.kind.ToString() : "-")} " +
                    $"verso {(tc != null ? "candela" : "uscita")} ({target.x:F1},{target.z:F1}) a {horiz:F1} m, mosso {moved:F2} m in {StuckSecs:F0} s; contatti: {BotTouching(pp)}; " +
                    $"percorso {botPath.status}, {(botPath.corners != null ? botPath.corners.Length : 0)} angoli");
            BotLine("    " + BotDeep(pp, next));
            BotLine("    tracciato: " + BotTraceText());
            if (botSChain == 1) BotMap(pp);
            if (botSChain == 1) botSCrouchT = 2.5f;          // forse e' un passaggio basso: ci si prova da accovacciati
            else if (botSChain == 2 && botPath != null && botPath.corners != null && botPath.corners.Length > 1 && (next - target).sqrMagnitude > 4f)
            {
                // solo con un percorso vero e un angolo a piu' di 2 m dal bersaglio: altrimenti si toglierebbe dalla navmesh la candela o l'uscita stessa
                BotCarve(next);
                BotLine($"VARCO stretto al piano {floor}: la navmesh passa da ({next.x:F1},{next.z:F1}) ma il giocatore, fermo in ({pp.x:F1},{pp.z:F1}), no; area tolta dalla navmesh, si cerca un'altra strada");
                botSBadPathT = 0f;
            }
            else if (botSChain >= 4) BotGiveUp(tc, $"incastrato 4 volte di fila in ({pp.x:F1},{pp.z:F1}), anche dopo aver escluso il varco");
        }

        void BotStrictTick(float dt)
        {
            if (botSDone) return;
            var pl = gm.player;
            botT += dt;
            PlayerController.botDir = Vector3.zero;
            PlayerController.botLook = Vector3.zero;
            PlayerController.botCrouch = botSCrouchT > 0f;
            if (botSCrouchT > 0f) botSCrouchT -= dt;
            if (!botSInit)
            {
                botSInit = true;
                botSScale = Mathf.Clamp(ArgInt("-botscale", 1), 1, 8);
                botSFloorsGoal = Mathf.Max(1, ArgInt("-botfloors", 1));
                botSCap = Mathf.Max(30, ArgInt("-botcap", 420));
                botSLimit = Mathf.Max(60, ArgInt("-procsecs", 900));
                BotLine($"AVVIO bot rigoroso: seme={runSeed} piano iniziale={floor} piani da completare={botSFloorsGoal}, tetto per piano={botSCap} s, tetto totale={botSLimit} s, tempo x{botSScale}, nemici={(NoEnemies ? "no" : "si")}");
            }
            if (!GameMenu.Blocked) Time.timeScale = botSScale;
            if (botT > botSLimit) { BotEnd(false, $"TEMPO TOTALE: {botSLimit} s esauriti al piano {floor}"); return; }
            if (transitioning || cur == null) return;
            if (deaths > botSDeathsSeen)
            {
                botSDeathsSeen = deaths; botSFails++;
                BotLine($"MORTE al piano {floor} (senza nemici: qualcosa uccide il giocatore), morti totali {deaths}");
            }
            if (!pl.alive) return;
            if (floor != botSFloor) BotFloorStart();
            botSFloorT += dt;
            if (botSFloorT > botSCap)
            {
                BotEnd(false, $"TEMPO PIANO {floor}: oltre {botSCap} s (candele {lit}/{total}, uscita {(exitOpen ? "aperta" : "chiusa")}, stanza {(playerRoom != null ? playerRoom.kind.ToString() : "-")})");
                return;
            }
            if (statFalls > botSFalls0)
            {
                botSFalls0 = statFalls; botSFails++;
                BotLine($"CADUTA nel vuoto al piano {floor} (FallGuard ha riportato il giocatore al sicuro)");
            }

            Vector3 pp = pl.transform.position;
            Candelabro tc = null;
            IInteract it;
            Vector3 target;
            if (!exitOpen)
            {
                float bd = float.MaxValue;
                foreach (var c in cur.candles)
                    if (c != null && c.objective && !c.lit && !botSBlack.Contains(c))
                    {
                        float d = (c.transform.position - pp).sqrMagnitude;
                        if (d < bd) { bd = d; tc = c; }
                    }
                if (tc == null)
                {
                    botSWaitT += dt;
                    if (botSBlack.Count > 0) BotEnd(false, $"SOFTLOCK piano {floor}: uscita chiusa, restano solo candele irraggiungibili ({botSBlack.Count})");
                    else if (botSWaitT > 8f) BotEnd(false, $"SOFTLOCK piano {floor}: candele tutte accese ({lit}/{total}) ma l'uscita non si apre");
                    return;
                }
                botSWaitT = 0f;
                target = tc.transform.position; it = tc;
            }
            else
            {
                if (cur.exit == null) { BotEnd(false, $"piano {floor}: manca l'ascensore di uscita"); return; }
                target = cur.exit.frontPoint; it = cur.exit;
            }

            // --- uso, come il tasto F: conta solo se la selezione vera (raggio, sguardo, visuale libera) cade proprio su questo oggetto
            float horiz = new Vector2(target.x - pp.x, target.z - pp.z).magnitude;
            Interact.Tick(pl.cam);
            Vector3 toIt = it.Point - pl.cam.position;
            if (toIt.magnitude <= it.Radius + 1f) PlayerController.botLook = toIt;      // guarda cio' che vuole usare, come farebbe il giocatore
            if (Interact.Current == it)
            {
                botSTries++;
                Interact.TryUse();
                if (tc != null)
                {
                    if (tc.lit)
                    {
                        // la finestra del controllo "fermo" riparte da qui, con la posizione di adesso: se resta quella di prima e il bot torna indietro
                        // per la strada da cui era venuto (candela in fondo a un ramo), 4 secondi dopo risulterebbe fermo senza esserlo
                        botSLitT = botSFloorT; botSTries = 0; botSNearT = 0f; botSProgT = 0f; botSChain = 0; botSProgPos = pp;
                        BotLine($"  candela {lit}/{total} accesa a {botSFloorT:F0} s da ({pp.x:F1},{pp.z:F1}), a {horiz:F1} m");
                    }
                    else if (botSTries > 30) BotGiveUp(tc, "F premuto 30 volte senza che la candela si accenda");
                }
                else if (transitioning)
                {
                    botSFloorsDone++;
                    BotLine($"PIANO {floor} COMPLETATO in {botSFloorT:F0} s (ultima candela a {botSLitT:F0} s, avvisi {botSFloorStuck})");
                    if (botSFloorsDone >= botSFloorsGoal) BotEnd(botSFails == 0, botSFails == 0 ? "obiettivo raggiunto" : "obiettivo raggiunto ma con fallimenti");
                }
                else if (botSTries > 30) BotEnd(false, $"USCITA al piano {floor}: F premuto 30 volte senza scendere");
                return;
            }

            // --- sul posto ma la selezione non scatta: ostacolo tra occhio e punto, prompt nullo (ascensore non aperto) o sguardo storto
            if (horiz < 1.3f)
            {
                botSNearT += dt;
                if (botSNearT > 5f)
                {
                    Vector3 to = it.Point - pl.cam.position;
                    BotLine($"NONSELEZIONABILE piano {floor} a ({pp.x:F1},{pp.z:F1}): {(tc != null ? "candela" : "uscita")} a {to.magnitude:F2} m (raggio {it.Radius:F1}), " +
                            $"coseno dello sguardo {Vector3.Dot(pl.cam.forward, to.normalized):F2}, visuale libera={Interact.Visible(pl.cam, it.Point, (it as Component)?.transform)}, prompt={(it.Prompt ?? "nullo")}");
                    BotGiveUp(tc, "oggetto non selezionabile da vicino");
                    return;
                }
            }
            else botSNearT = 0f;

            // --- percorso sulla navmesh
            botRepath -= dt;
            if (botPath == null) botPath = new NavMeshPath();
            if (botRepath <= 0f)
            {
                botRepath = 0.4f;
                bool okT = NavMesh.SamplePosition(target, out NavMeshHit th, 3f, NavMesh.AllAreas);
                bool okF = NavMesh.SamplePosition(pp, out NavMeshHit fh, 2f, NavMesh.AllAreas);
                if (okT && okF) NavMesh.CalculatePath(fh.position, th.position, NavMesh.AllAreas, botPath); else botPath.ClearCorners();
                if (botPath.corners != null && botPath.corners.Length > 1)
                {
                    var h = new Vector2(botPath.corners[1].x - pp.x, botPath.corners[1].z - pp.z);
                    if (h.sqrMagnitude > 0.01f)
                    {
                        h.Normalize();
                        if (botSPrevHeading != Vector2.zero && Vector2.Dot(h, botSPrevHeading) < -0.3f)
                        {
                            botSFlips++;                    // la strada scelta si e' girata di piu' di 100 gradi: due percorsi quasi alla pari
                            if (botSHoldT <= 0f) { botSHold = botSPrevHeading; botSHoldT = 1f; }     // al bivio un giocatore ne sceglie una e la tiene: un secondo dritto sulla strada di prima
                        }
                        if (botSHoldT <= 0f) botSPrevHeading = h;
                    }
                }
                if (okT && okF && botPath.status == NavMeshPathStatus.PathComplete) botSBadPathT = 0f;
                else if (botSFloorT > StartGrace)
                {
                    botSBadPathT += 0.4f;
                    if (botSBadPathT > 3f)
                    {
                        BotLine($"NAVMESH piano {floor}: nessun percorso completo da ({pp.x:F1},{pp.z:F1}) a {(tc != null ? "candela" : "uscita")} ({target.x:F1},{target.z:F1}): " +
                                $"partenza sulla navmesh={okF}, arrivo={okT}, stato={botPath.status}");
                        BotGiveUp(tc, "la navmesh non collega partenza e bersaglio");
                        return;
                    }
                }
            }
            Vector3 next = target;
            if (botPath.corners != null && botPath.corners.Length > 1)
            {
                next = botPath.corners[1];
                if (new Vector2(next.x - pp.x, next.z - pp.z).magnitude < 0.5f && botPath.corners.Length > 2) next = botPath.corners[2];
            }
            Vector3 dir = new Vector3(next.x - pp.x, 0f, next.z - pp.z);
            if (botSHoldT > 0f)
            {
                dir = new Vector3(botSHold.x, 0f, botSHold.y);
                botSHoldT -= dt;
                if (botSHoldT <= 0f) botSPrevHeading = Vector2.zero;       // finita la scelta: il prossimo percorso vale senza confronti
            }
            if (horiz > 0.8f && dir.sqrMagnitude > 0.01f) PlayerController.botDir = dir.normalized;
            var dn = PlayerController.botDir;
            botSTrace.Add(new Vector4(pp.x, pp.z, dn.x, dn.z));
            if (botSTrace.Count > 240) botSTrace.RemoveAt(0);

            // --- incastrato? niente teletrasporto: si registra, ci si prova accovacciati, dopo tre volte si rinuncia al bersaglio
            if (botSFloorT < StartGrace) { botSProgT = 0f; botSProgPos = pp; return; }        // l'ascensore di arrivo si sta ancora aprendo
            if (HasArg("-botfreeze")) PlayerController.botDir = Vector3.zero;                 // controllo negativo: un giocatore fermo DEVE risultare incastrato
            botSProgT += dt;
            if (botSProgT > StuckSecs)
            {
                botSProgT = 0f;
                float moved = (pp - botSProgPos).magnitude;
                botSProgPos = pp;
                if (moved >= StuckMove) botSChain = 0;
                else if (horiz > 1.3f) BotStuck(pp, target, tc, horiz, next, moved);
                botSFlips = 0;
            }
        }
    }
}
