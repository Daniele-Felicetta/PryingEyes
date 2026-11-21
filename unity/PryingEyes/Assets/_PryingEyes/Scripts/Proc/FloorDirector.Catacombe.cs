using System.Collections;
using System.Collections.Generic;
using Production.Economy;
using PryingEyes.Cattedrale;
using PryingEyes.Deserto;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Proc
{
    /// <summary>Le due zone del gioco: la cattedrale (zona sicura, da ricostruire, con il mercato sul sagrato) e le catacombe sotto (i piani,
    /// divisi in rami). Da soli si parte dalla cattedrale; dalla cripta si sceglie il ramo, si scende di livello in livello e si risale con lo
    /// zaino: dalla scala d'ingresso tra un'ondata e l'altra, dopo il boss del terzo livello, o con la campanella del Campanile. Morire costa
    /// parte dello zaino (le spoglie restano nel primo livello del ramo). Le regole e i numeri stanno in Cattedrale/ (Fabbrica, Catalogo).</summary>
    public partial class FloorDirector
    {
        /// <summary>Si e' nella cattedrale (nessuna ondata, nessun nemico).</summary>
        public bool InHub { get; private set; }
        Fabbrica.Esito deathOutcome;            // calcolato alla morte (DeathPenalty), mostrato al ritorno nella cattedrale
        bool revealMaps;                        // la Mappa del cartografo: ogni livello della spedizione si vede tutto

        static readonly string[] CatacombLines =
        {
            "La scala scende piu' di quanto dovrebbe.",
            "Le ossa nelle nicchie sono state contate. Ne manca una.",
            "Qui sotto qualcuno ha costruito un albergo. E una chiesa. E poi li ha seppelliti.",
            "Le torce si accendono prima che tu arrivi.",
            "Sopra di te, lontano, suona la campana della tua cattedrale.",
            "Le catacombe ricordano chi e' gia' sceso.",
            "L'aria sa di cera e di terra bagnata.",
            "Ogni cancello si apre solo verso il basso.",
        };

        /// <summary>Il tema di un piano nelle catacombe: quello del suo ramo (null nell'Abisso: a caso, come la Torre).</summary>
        public static Theme? ThemeOfFloor(int floor)
        {
            int t = Rami.Tutti[Rami.RamoDi(floor)].tema;
            if (t < 0) return null;
            // Safety net, not the normal path. The Guardaroba props (gu_*) reach the game through the kit saved in Torre.unity
            // (TorreBuilder.FillKit, run by BuildPlayer.Torre without -scriptsOnly), and the committed scene lists them. A scene saved
            // before they existed would make RoomKits.Prop draw grey boxes, so the branch then uses one of the three complete themes in
            // turn (same floor, same theme on every machine of a co-op game). If a test shows this fallback, the scene is stale:
            // regenerate it; PerfProbe -perfprops on a Guardaroba floor reports any prop that is still a grey box (SEGNAPOSTO).
            if ((Theme)t == Theme.Guardaroba && ProcKit.Prefab("gu_armadio") == null) return (Theme)(floor % 3);
            return (Theme)t;
        }

        /// <summary>Questo livello finisce risalendo alla cattedrale (il boss di un ramo, da soli). Nell'Abisso si scende sempre.</summary>
        bool ReturnsUp => Catacombe.Hub && floor >= 1 && Rami.LivelloDelBoss(floor) && Rami.RamoDi(floor) < Rami.Abisso;

        // ------------------------------------------------------------------ la cattedrale
        IEnumerator GoToHub(Fabbrica.Esito outcome, bool first, Vector3? keepAt = null, float keepYaw = 0f, string bannerHead = null, string bannerSub = null)
        {
            transitioning = true;
            gm = GameManager.I;
            SetInvulnerable(true);
            if (!first) { FadeAll(1f); }
            if (cur != null)
            {
                RoundDirector.I?.Disarm();
                Destroy(cur.root);
                var gone = cur.bounds;
                cur = null;
                if (DesertMode.Active)
                {
                    // la cattedrale si rifa' (un'opera costruita) ma il deserto resta com'e': via solo i pannelli senza padre (quelli dell'armeria;
                    // quelli dei luoghi sono figli del luogo). Le luci morte le toglie da sola la LightPool.
                    WallBuy.ClearUnparented();
                    DesertNav.Dirty(gone);
                }
                else
                {
                    Enemy.ClearAll();
                    Drops.NewFloor();
                    WallBuy.ClearAll();
                    Interact.Clear();
                    LightPool.Clear();
                    RisorsaPickup.ClearAll();
                }
                yield return null;
            }
            InHub = true;
            floor = 0;
            Fabbrica.Carica();
            Biblioteca.Clear();
            FloorMap.Clear(); map = null; System.Array.Clear(mapOpenA, 0, mapOpenA.Length);      // la cattedrale non ha la mappa (Proc/Map/FloorMap.cs)
            LightPool.Dim = 1f;
            exitOpen = false; lit = total = 0;
            bool desert = DesertMode.Active;
            var plan = HubPlan.Build(Fabbrica.Costruita, desert);
            var faults = HubPlan.Validate(plan);
            if (faults.Count > 0) Debug.LogWarning("[PryingEyes] cattedrale: " + string.Join("; ", faults));
            float t0 = Time.realtimeSinceStartup;
            cur = FloorBuilder.Build(plan, null);
            if (desert) { DesertCathedral.Build(cur); DesertCathedral.PlaceStations(cur); }      // basamento, scalinata, leggii e altare (Scripts/Deserto)
            Physics.SyncTransforms();
            yield return null;
            if (desert) { DesertNav.Dirty(cur.bounds); Desert?.SetCathedral(cur); }      // la navmesh del deserto segue il giocatore e comprende la cattedrale (DesertNav.cs)
            else HotelNav.Rebuild(cur.bounds, cur.propRoots);
            ApplyLook(plan, 1);
            if (desert) DesertSky.I?.Resync();          // ApplyLook ha scritto l'esposizione di una stanza: il cielo la riscrive
            Power.Reset(null);
            LightShafts.Build(cur);
            StartCoroutine(RoomProbes.Bake(cur));
            Seats.Players(everyone);
            // in cooperativa nel deserto ognuno nasce al suo posto sulla scalinata, a fianco degli altri (non sulla stessa mattonella)
            float slotShift = desert && Coop.Active ? (Coop.Slot - 1.5f) * 0.9f : 0f;
            for (int i = 0; i < everyone.Count; i++)
            {
                var one = everyone[i];
                // nel deserto si nasce in cima alla scalinata, a guardare la sabbia (yaw e' in radianti: pi greco = verso -z)
                Vector3 spot = keepAt ?? (desert ? new Vector3(18f + slotShift, 0.06f, 2.2f) : new Vector3(HubPlan.SpawnX, 0.06f, HubPlan.SpawnZ));
                if (desert && keepAt.HasValue)
                {
                    // una ricostruzione lascia dove si era, ma la rinascita resta in cima alla scalinata: dopo una morte non ci si ritrova in fondo alla navata
                    one.SetSpawn(SideBySide(new Vector3(18f + slotShift, 0.06f, 2.2f), Vector3.forward, i, everyone.Count));
                    one.Teleport(SideBySide(spot, Vector3.forward, i, everyone.Count));
                }
                else one.SetSpawn(SideBySide(spot, Vector3.forward, i, everyone.Count));
                one.yaw = keepAt.HasValue ? keepYaw : desert ? Mathf.PI : 0f;
                one.pitch = 0f;
            }
            playerRoom = cur.RoomAt(gm.player.transform.position);
            ShowHubHud();
            if (outcome != null) Seats.ForHuds(h => h.Banner(outcome.Titolo, outcome.Riga()));
            else if (bannerHead != null) Seats.ForHuds(h => h.Banner(bannerHead, bannerSub ?? ""));
            else Seats.ForHuds(h => h.Banner("La Cattedrale", Fabbrica.Albero.BuiltCount == 0 ? "Un rudere sopra le catacombe. Ricostruiscila con quello che trovi laggiù." : "La tua cattedrale"));
            RefreshObjective();
            Debug.Log($"[PryingEyes] cattedrale costruita in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms: {Fabbrica.Albero.BuiltCount}/{Fabbrica.Albero.All.Count} opere, {LightPool.Count} luci");
            if (desert) yield return DesertWaitReady();         // si resta al nero finche' la sabbia sotto la scalinata non c'e'
            if (desert && Coop.Active) Coop.OnFloorBuilt();     // cooperativa: il deserto attorno all'arrivo c'e' (l'ospitato lo dice all'ospite, l'ospite saluta chi aspettava)
            for (float k = 1f; k > 0f; k -= Time.deltaTime / 0.9f) { FadeAll(k); yield return null; }
            FadeAll(0f);
            SetInvulnerable(false);
            transitioning = false;
        }

        void ShowHubHud()
        {
            if (DesertMode.Active) { ShowDesertHud(); return; }
            int oboli = Fabbrica.Banca.Get(Risorse.Oboli);
            Seats.ForHuds(h =>
            {
                h.SetWaveLabel("cattedrale");
                h.SetWave(Mathf.Max(1, Fabbrica.Albero.BuiltCount));
                h.SetRound(0, false);
                h.SetScoreLabel("OBOLI");
                h.SetScore(oboli);
            });
        }

        /// <summary>Un'opera e' stata costruita: la cattedrale si rifa' con l'opera al suo posto (una dissolvenza breve, si resta dove si e').</summary>
        public void HubBuilt(OperaDef opera)
        {
            if (!InHub || transitioning) return;
            StartCoroutine(RebuildHub(opera));
        }

        IEnumerator RebuildHub(OperaDef opera)
        {
            transitioning = true;
            var pl = gm.player;
            Vector3 at = pl.transform.position + Vector3.up * 0.06f;
            float yaw = pl.yaw;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.5f) { FadeAll(k); yield return null; }
            yield return GoToHub(null, false, at, yaw, opera.nome, opera.effetto);
        }

        /// <summary>Solo per le prove: salta a un livello delle catacombe della spedizione in corso (con la dissolvenza di un cambio livello).</summary>
        public void DebugGoTo(int n)
        {
            if (transitioning) return;
            FadeAll(1f);
            StartCoroutine(GoTo(n, false));
        }

        /// <summary>Dal cancello di un ramo: si scende al suo primo livello.</summary>
        public void BeginExpedition(int ramo)
        {
            if (!InHub || transitioning || DesertMode.Active || !Fabbrica.RamoAperto(ramo)) return;
            StartCoroutine(ExpeditionCo(ramo));
        }

        IEnumerator ExpeditionCo(int ramo)
        {
            transitioning = true;
            SetInvulnerable(true);
            RegistroFabbrica.Aperto = false;
            Sfx.UI(SoundBank.StoneGrind, 0.6f, 0.9f);
            for (float k = 0f; k < 1f; k += Time.deltaTime / 1.0f) { FadeAll(k); yield return null; }
            FadeAll(1f);
            var supplies = Fabbrica.Inizia(ramo, false);
            runSeed = ArgInt("-seed", 0) != 0 ? ArgInt("-seed", 0) + Fabbrica.D.spedizioni * 7 : Random.Range(1, 1 << 28);
            ResetForExpedition(supplies);
            InHub = false;
            yield return new WaitForSeconds(0.4f);
            yield return GoTo(Rami.Piano(ramo, 1), false);
            var r = Rami.Tutti[ramo];
            Seats.ForHuds(h => h.Banner(r.nome, r.sottotitolo));
        }

        /// <summary>L'inizio di una spedizione: punti di partenza (piu' le opere e la lettera di credito), perk delle cappelle, equipaggiamento piu'
        /// i rifornimenti del mercato, prezzi delle opere. Le armi restano quelle prese nella cattedrale (la rastrelliera della sacrestia).</summary>
        void ResetForExpedition(Bundle supplies)
        {
            ResetRunCommon();
            ResetSeatForExpedition(supplies);
        }

        /// <summary>Quello che vale per tutta la partita all'inizio di una spedizione: le ondate, i premi, i perk e le medaglie di ogni posto.</summary>
        void ResetRunCommon()
        {
            RoundDirector.ResetRun();
            Drops.ResetRun();
            deaths = 0;
            Seats.EachOrOnce(_ => { Perks.ResetRun(); Medals.Reset(); });
        }

        /// <summary>Quello che e' del giocatore che gira adesso (nello schermo condiviso si chiama a nome di ogni posto): punti di partenza, perk delle cappelle, equipaggiamento.</summary>
        void ResetSeatForExpedition(Bundle supplies)
        {
            gm.score = Fabbrica.PuntiIniziali + 500 * supplies[Rifornimenti.Lettera];
            var scoreHud = gm.hud;
            if (scoreHud != null) { scoreHud.SetScoreLabel("PUNTI"); scoreHud.SetScore(gm.score); scoreHud.SetWaveLabel("livello"); }
            if (Fabbrica.Costruita("cappella_pietra")) Perks.Add(PerkId.PelleDiPietra);
            if (Fabbrica.Costruita("cappella_mani")) Perks.Add(PerkId.ManiSvelte);
            if (supplies[Rifornimenti.Cero] > 0) Perks.Add(PerkId.Rinascita);
            var pl = gm.player;
            if (pl != null)
            {
                pl.OnPerksChanged(true);
                pl.shield = 50f;
                pl.weapon.RespawnAmmo();
                Equipment.Ensure(pl).ResetRun(2 + supplies[Rifornimenti.Acquasanta], 1 + supplies[Rifornimenti.Carillon], 1 + supplies[Rifornimenti.Mina]);
            }
            revealMaps = supplies[Rifornimenti.Mappa] > 0;
            ApplyCathedralPrices();
            var bless = Benedizioni.Of(Fabbrica.Benedizione);
            if (bless != null) Debug.Log($"[PryingEyes] cattedrale: benedizione {bless.nome} ({bless.effetto})");
        }

        /// <summary>I prezzi che le opere e la benedizione cambiano (porte, libri). In cooperativa le porte restano al prezzo di tutti.</summary>
        static void ApplyCathedralPrices()
        {
            RoomAccessState.PriceMul = Coop.Active ? 1f : Fabbrica.PorteMul;
            Perks.PriceMul = Fabbrica.PerkMul;
            PlayerPerks.BonusHp = Fabbrica.VitaBonus;
        }

        /// <summary>Tornati su (vivi o morti): niente perk, la pistola di partenza, l'equipaggiamento base. La prossima discesa riparte da li'.</summary>
        void ResetAfterExpedition()
        {
            Seats.EachOrOnce(_ => { Perks.ResetRun(); Medals.Reset(); });
            RoundDirector.ResetRun();
            Drops.ResetRun();
            RoomAccessState.PriceMul = 1f; Perks.PriceMul = 1f; PlayerPerks.BonusHp = 0f;
            var pl = gm.player;
            if (pl == null) return;
            pl.weapon.ResetLoadout();
            pl.OnPerksChanged(true);
            pl.shield = 50f;
            Equipment.Ensure(pl).ResetRun(2, 1, 1);
        }

        /// <summary>Si puo' risalire adesso? Non a meta' di un'ondata (le scale sono infestate) e non mentre si cambia livello.</summary>
        public bool CanReturnNow => Catacombe.Hub && !InHub && !transitioning && floor >= 1 && (RoundDirector.I == null || !RoundDirector.I.Fighting);

        /// <summary>Risalita: lo zaino va nel magazzino e i punti rimasti diventano oboli.</summary>
        public void ReturnToCathedral()
        {
            if (!Catacombe.Hub || InHub || transitioning || floor < 1) return;
            StartCoroutine(ReturnCo());
        }

        IEnumerator ReturnCo()
        {
            transitioning = true;
            SetInvulnerable(true);
            Sfx.UI(SoundBank.Toll, 0.7f, 0.9f);
            for (float k = 0f; k < 1f; k += Time.deltaTime / 1.1f) { FadeAll(k); yield return null; }
            FadeAll(1f);
            var outcome = Fabbrica.Rientra(gm.score);
            ResetAfterExpedition();
            yield return new WaitForSeconds(0.5f);
            yield return GoToHub(outcome, false);
        }

        /// <summary>La campanella del ritorno (il Campanile): una volta per spedizione, dal menu di pausa, anche a meta' di un'ondata.</summary>
        public bool UseReturnBell()
        {
            if (!Fabbrica.CampanellaPronta || InHub || transitioning || floor < 1) return false;
            Fabbrica.UsaCampanella();
            Sfx.UI(SoundBank.Bell, 0.8f);
            StartCoroutine(ReturnCo());
            return true;
        }

        IEnumerator DeathReturnCo()
        {
            transitioning = true;
            SetInvulnerable(true);
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.8f) { FadeAll(k); yield return null; }
            FadeAll(1f);
            var outcome = deathOutcome ?? new Fabbrica.Esito();
            deathOutcome = null;
            ResetAfterExpedition();
            yield return new WaitForSeconds(0.6f);
            yield return GoToHub(outcome, false);
        }

        // ------------------------------------------------------------------ un livello delle catacombe
        /// <summary>Dopo la costruzione di un livello: la scala per risalire e la cassetta delle elemosine accanto all'arrivo, le spoglie nel
        /// primo livello del ramo, l'acqua santa della Fonte, la mappa del cartografo, i prezzi. Solo nelle catacombe.</summary>
        void SetupCatacombLevel(Plan plan)
        {
            if (resumedLevel) resumedLevel = false;       // una spedizione ripresa non conta il livello due volte (FloorDirector.Run.cs)
            else Fabbrica.LivelloSceso();
            ApplyCathedralPrices();
            var eq = gm.player != null ? Equipment.Ensure(gm.player) : null;
            if (eq != null && Fabbrica.AcquasantaMinima > eq.grenades) { eq.grenades = Fabbrica.AcquasantaMinima; gm.hud?.SetEquipment(eq.grenades, eq.lures, eq.mines); }
            // la Mappa del cartografo (revealMaps) la applica GoTo appena nasce la mappa del piano (FloorMap.Create)
            var lift = cur.arrival;
            if (lift == null || !Catacombe.Hub) return;
            // la scala d'ingresso: si risale da qui, tra un'ondata e l'altra
            var up = new GameObject("ScalaRisalita");
            up.transform.SetParent(cur.root.transform, false);
            up.transform.position = lift.spawnPoint;
            var stair = up.AddComponent<ScalaRisalita>();
            stair.point = lift.spawnPoint + Vector3.up * 1.0f - lift.lookOut * 0.6f;
            // la cassetta delle elemosine: un ceppo di legno con la fessura, accanto all'arco d'arrivo
            Vector3 side = Vector3.Cross(Vector3.up, lift.lookOut).normalized;
            Vector3 boxAt = lift.frontPoint + side * 1.5f + lift.lookOut * 0.2f;
            if (NavMesh.SamplePosition(boxAt, out var hit, 1.2f, NavMesh.AllAreas)) boxAt = hit.position;
            var box = new GameObject("CassettaElemosine");
            box.transform.SetParent(cur.root.transform, false);
            box.transform.position = boxAt;
            box.transform.rotation = Quaternion.LookRotation(lift.lookOut);
            ShrineKit.Mats();
            ShrineKit.Prim(box.transform, PrimitiveType.Cube, "Ceppo", ShrineKit.Wood, new Vector3(0f, 0.45f, 0f), new Vector3(0.36f, 0.9f, 0.36f));
            ShrineKit.Prim(box.transform, PrimitiveType.Cube, "Cassetta", ShrineKit.Iron, new Vector3(0f, 0.98f, 0f), new Vector3(0.42f, 0.18f, 0.32f));
            ShrineKit.Prim(box.transform, PrimitiveType.Cube, "Fessura", ShrineKit.Dark, new Vector3(0f, 1.075f, 0f), new Vector3(0.2f, 0.01f, 0.03f));
            var alms = box.AddComponent<CassettaElemosine>();
            alms.point = boxAt + Vector3.up * 1.05f;
            // le spoglie: nel primo livello del ramo dove sei morto, in una delle aree vicine all'arrivo (da comprare)
            if (Rami.LivelloDi(floor) == 1)
            {
                var lost = Fabbrica.Spoglie(Rami.RamoDi(floor));
                if (!lost.IsEmpty)
                {
                    var hops = plan.Hops(plan.start);
                    var rooms = new List<Room>();
                    foreach (var r in plan.rooms) if (!r.IsLift && hops[r.id] >= 1 && hops[r.id] <= 3) rooms.Add(r);
                    var rng = new Rng(plan.seed + 4242);
                    for (int attempt = 0; attempt < 40 && rooms.Count > 0; attempt++)
                    {
                        var r = rooms[rng.Range(0, rooms.Count)];
                        var q = new Vector3(rng.Range(r.x0 * 0.1f + 1.2f, r.x1 * 0.1f - 1.2f), 0f, rng.Range(r.z0 * 0.1f + 1.2f, r.z1 * 0.1f - 1.2f));
                        if (!NavMesh.SamplePosition(q, out var nh, 1.5f, NavMesh.AllAreas)) continue;
                        RisorsaPickup.Spawn(lost, nh.position, true);
                        Seats.ForHuds(h => h.Toast("Le tue spoglie sono in questo livello: riprendile prima di morire di nuovo."));
                        break;
                    }
                }
            }
        }

        string CatacombObjective(string status)
        {
            bool boss = Rami.LivelloDelBoss(floor);
            int waves = RoundDirector.I != null ? RoundDirector.I.FloorWaves : 0;
            string goal;
            if (exitOpen) goal = ReturnsUp ? "La scala risale alla cattedrale" : "Il cancello è aperto: resta o scendi";
            else if (boss) goal = waves >= RoundDirector.WavesForExit ? $"{Rami.BossDi(floor).bossTitolo} è sceso" : $"{RoundDirector.WavesForExit} ondate, poi {Rami.BossDi(floor).bossTitolo}";
            else goal = $"{RoundDirector.WavesForExit} ondate per il cancello";
            string back = Catacombe.Hub ? (CanReturnNow ? "  ·  Puoi risalire dalla scala d'ingresso" : "") : "";
            string power = Power.Panel != null && !Power.On ? "  ·  Trova il quadro elettrico" : "";
            return status + "  ·  " + goal + back + power;
        }
    }

    /// <summary>La scala da cui si e' scesi: tra un'ondata e l'altra si risale alla cattedrale con lo zaino.</summary>
    public sealed class ScalaRisalita : MonoBehaviour, IInteract
    {
        public Vector3 point;
        public Vector3 Point => point;
        public float Radius => 3.2f;
        void OnEnable() { Interact.Add(this); }
        void OnDisable() { Interact.Remove(this); }

        public string Prompt
        {
            get
            {
                var fd = FloorDirector.I;
                if (fd == null || fd.InHub || !Catacombe.Hub) return null;
                if (!fd.CanReturnNow) return "LA SCALA DELLA RISALITA — non durante un'ondata";
                int oboli = Listino.OboliDaPunti(GameManager.I != null ? GameManager.I.score : 0, Fabbrica.OboliOgni);
                return $"RISALI ALLA CATTEDRALE — porti su lo zaino; i tuoi punti valgono {oboli} oboli";
            }
        }

        public void Use()
        {
            var fd = FloorDirector.I;
            if (fd == null) return;
            if (!fd.CanReturnNow) { GameManager.I?.hud?.Toast("Durante l'ondata la scala è infestata. Finisci l'ondata."); Sfx.UI(SoundBank.BookNo, 0.6f); return; }
            fd.ReturnToCathedral();
        }
    }

    /// <summary>La cassetta delle elemosine accanto all'arrivo: versare punti li rende subito oboli, al sicuro anche se muori (uno ogni 15).</summary>
    public sealed class CassettaElemosine : MonoBehaviour, IInteract
    {
        public Vector3 point;
        public Vector3 Point => point;
        public float Radius => 2.0f;
        void OnEnable() { Interact.Add(this); }
        void OnDisable() { Interact.Remove(this); }

        public string Prompt
        {
            get
            {
                if (!Catacombe.Hub) return null;
                int p = Listino.Elemosina, o = Listino.OboliDaPunti(p, Listino.ElemosinaOgni);
                int have = GameManager.I != null ? GameManager.I.score : 0;
                return $"CASSETTA DELLE ELEMOSINE — versa {p} punti: {o} oboli subito al sicuro" + (have < p ? $" (ti mancano {p - have})" : "");
            }
        }

        public void Use()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            int p = Listino.Elemosina;
            if (gm.score < p) { gm.hud?.Toast($"Servono {p} punti."); Sfx.UI(SoundBank.BookNo, 0.6f); return; }
            gm.AddPoints(-p);
            int o = Fabbrica.Elemosina(p);
            Sfx.UI(SoundBank.PickCoins, 0.6f, 0.9f);
            gm.hud?.Toast($"{o} oboli nella cassetta: sono già nella cattedrale.");
        }
    }
}
