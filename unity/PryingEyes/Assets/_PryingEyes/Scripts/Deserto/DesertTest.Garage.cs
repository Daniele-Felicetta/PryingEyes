#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PryingEyes.Cattedrale;
using PryingEyes.Characters;
using PryingEyes.Pista;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Le fasi del garage (0.10.3): <c>garage</c> (il catalogo delle dieci moto, il luogo, comprare e vedere nascere la moto nella sua piazzola, la proprieta' che resta nel personaggio, guidare una moto
    /// comprata), <c>menu</c> (il menu di Select: navigazione, mappa, libri, magie e telecamera), <c>cheats</c> (materiali, punti e volo) e
    /// <c>negozio</c> (Bozzo Moto Store: la vetrina, il "sei sicuro?", l'ordine, la ricevuta, i rifiuti). Si danno i comandi alle pagine con <see cref="HUD.HubInput"/> costruiti a mano: e' lo stesso percorso
    /// che fanno tastiera, mouse e controller. Con -desertshots le foto: le dieci moto nel garage, il garage dall'alto della scalinata, di sera, e le pagine del menu.</summary>
    public sealed partial class DesertTest
    {
        /// <summary>Il garage di prova: nessuna moto comprata, il deserto da rifare, i punti a zero. Cosi' ogni fase parte dallo stesso punto.</summary>
        static void ResetGarage()
        {
            DesertGarage.Clear(0);
            DesertBikes.Rebuild();
        }

        static void SetPoints(int points)
        {
            var gm = Gm;
            if (gm == null) return;
            gm.AddPoints(points - gm.score);
        }

        static DesertBike BikeOf(DesertBikeDef d)
        {
            foreach (var b in DesertBike.All) if (b != null && !b.puppet && b.Def == d) return b;
            return null;
        }

        static List<DesertBikeDef> ForSale()
        {
            var l = new List<DesertBikeDef>();
            foreach (var d in DesertBikeDefs.All) if (!d.Free) l.Add(d);
            return l;
        }

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        /// <summary>Dove mettersi per fotografare una moto del garage a tre quarti davanti: a sud della fila, a destra o a sinistra, scegliendo il lato in cui la linea dall'obiettivo alla moto non passa
        /// per un palo del tetto (i pali stanno tra le piazzole, ma da tre quarti la linea puo' passarci lo stesso, e davanti alla macchina ne fanno una foto di pali).</summary>
        static Vector3 PhotoSpot(Vector3 bike)
        {
            var site = DesertGarageSite.I;
            float zPost = site != null ? site.Footprint.y + 0.12f : DesertGarage.RowZ0 - 2.7f + 0.12f;
            const float back = 3.9f;
            float dz = bike.z - back, k = (bike.z - zPost) / back;           // a quale frazione della linea (dalla moto all'obiettivo) sta la riga dei pali
            Vector3 best = new Vector3(bike.x + 1.4f, bike.y, dz); float bestClear = -1f;
            foreach (float off in new[] { 1.4f, -1.4f, 1.9f, -1.9f, 0.9f, -0.9f, 2.4f, -2.4f })
            {
                float crossing = bike.x + off * k;
                float clear = 9f;
                if (site != null) foreach (float px in site.PostXs) clear = Mathf.Min(clear, Mathf.Abs(crossing - px));
                if (clear > bestClear) { bestClear = clear; best = new Vector3(bike.x + off, bike.y, dz); }
                if (clear > 0.6f) break;
            }
            return best;
        }

        // ================================================================== garage
        IEnumerator GarageTest()
        {
            var pl = Pl; var gm = Gm;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset();
            ResetGarage();
            SetPoints(0);
            yield return Frames(3);          // le moto di prima vengono distrutte a fine fotogramma: si aspetta che spariscano
            yield return WaitFor(() => FirstBike() != null, 10f);

            // ---- il catalogo
            var all = DesertBikeDefs.All;
            var sale = ForSale();
            Check("garage: il catalogo ha la moto di casa, gratis e per prima, e le nove della pista in vendita", all.Count == 1 + Bikes.Count && all[0].Starter && all[0].Free && sale.Count == Bikes.Count, $"{all.Count} moto, {sale.Count} in vendita");
            bool ascending = true, unique = true, netOk = true, capsOk = true;
            var ids = new HashSet<string>();
            for (int i = 0; i < sale.Count; i++)
            {
                var d = sale[i];
                if (i > 0 && d.price < sale[i - 1].price) ascending = false;
                if (!ids.Add(d.id)) unique = false;
                if (DesertBikeDefs.FromNet(DesertBikeDefs.NetCode(d)) != d) netOk = false;
                if (d.top < 24.4f || d.top > 33.1f || d.boost > 38.1f || d.boost < d.top || d.accel < 0.99f || d.accel > 1.46f || d.turn < 0.87f || d.turn > 1.19f || d.brake < 0.94f || d.brake > 1.26f) capsOk = false;
                Debug.Log($"[PryingEyes] desertotest garage: {d.name} ({d.klass}) {d.price} punti, {d.top * 3.6f:F0} km/h, turbo {d.boost * 3.6f:F0}, ripresa x{d.accel:F2}, freno x{d.brake:F2}, sterzo x{d.turn:F2}, {d.hp:F0} CV, {d.kg:F0} kg, {DesertBikeDefs.EngineWords(d.engine)}");
            }
            Check("garage: in vendita dalla piu' economica alla piu' cara, con prezzi veri", ascending && sale[0].price > 0, sale.Count > 0 ? $"da {sale[0].price} a {sale[sale.Count - 1].price}" : "nessuna");
            Check("garage: ogni moto ha il suo id, e il numero di rete la ritrova", unique && netOk && DesertBikeDefs.FromNet(0) == all[0]);
            Check("garage: i numeri di guida stanno nei limiti (velocita' 88-119 km/h, turbo al piu' 137, ripresa, freno e sterzo tra x0,9 e x1,5)", capsOk);
            Check("garage: la moto di casa e' la guida di sempre (velocita' massima e turbo di DesertBike)", Mathf.Approximately(all[0].top, DesertBike.TopSpeed) && Mathf.Approximately(all[0].boost, DesertBike.BoostSpeed));

            // ---- il luogo
            var site = DesertGarageSite.I;
            Check("garage: c'e' il tetto del garage, con una fila", site != null && site.Rows == 1, site == null ? "nessun garage" : $"{site.Rows} file");
            bool baysOk = true, flatOk = true;
            for (int b = 0; b < DesertGarage.Bays; b++)
            {
                var p = DesertGarage.BayPos(0, b);
                if (site != null && !site.Footprint.Contains(new Vector2(p.x, p.z))) baysOk = false;
                if (b > 0 && Flat(p, DesertGarage.BayPos(0, b - 1)) < 1.9f) baysOk = false;
                if (Mathf.Abs(p.y - DesertWorld.Height(p.x, p.z)) > 0.05f) flatOk = false;
                if (p.z > -2f) baysOk = false;          // sul sagrato davanti alla cattedrale, non dentro
            }
            Check("garage: le dieci piazzole stanno sotto il tetto, a due metri l'una dall'altra, fuori dalla cattedrale", baysOk);
            bool postsOk = site != null && site.PostXs.Count >= 4;
            if (site != null)
                foreach (float px in site.PostXs)
                    for (int b = 0; b < DesertGarage.Bays; b++)
                        if (Mathf.Abs(px - DesertGarage.BayPos(0, b).x) < 0.8f) postsOk = false;          // un palo a meno di 80 cm dall'asse di una moto sta nella sua vista e a ridosso della ruota
            Check("garage: i pali del tetto stanno tra una piazzola e l'altra, mai davanti al muso di una moto", postsOk, site == null ? "nessun garage" : $"{site.PostXs.Count} pali");
            Check("garage: poggiano sul terreno del sagrato", flatOk);
            var starter = FirstBike();
            Check("garage: all'inizio c'e' solo la moto di casa, nella prima piazzola, col muso al deserto", DesertBike.All.FindAll(x => x != null && !x.puppet).Count == 1 && starter.Def.Starter && Flat(starter.transform.position, DesertGarage.BayPos(0, 0)) < 0.3f
                && Mathf.Abs(Mathf.DeltaAngle(starter.transform.eulerAngles.y, DesertGarage.BayHeading * Mathf.Rad2Deg)) < 3f,
                $"{DesertBike.All.Count} moto, in {starter.transform.position:F1}");

            // ---- comprare
            var cheap = sale[0]; var mid = sale[2]; var dear = sale[sale.Count - 1];
            var poor = DesertGarage.TryBuy(0, cheap, out int missing);
            Check("garage: senza punti non si compra, e dice quanti ne mancano", poor == DesertGarage.Result.NotEnoughPoints && missing == cheap.price && !DesertGarage.Owns(0, cheap), $"{poor}, mancano {missing}");
            gm.AddPoints(cheap.price + mid.price + dear.price + 11);
            int before = gm.score;
            var rMid = DesertGarage.TryBuy(0, mid, out _);
            Check("garage: coi punti si compra, e si pagano il prezzo giusto", rMid == DesertGarage.Result.Bought && DesertGarage.Owns(0, mid) && before - gm.score == mid.price, $"{rMid}, pagati {before - gm.score} su {mid.price}");
            Check("garage: la stessa moto due volte non si compra e non costa niente", DesertGarage.TryBuy(0, mid, out _) == DesertGarage.Result.AlreadyOwned && before - gm.score == mid.price);
            yield return WaitFor(() => BikeOf(mid) != null, 3f);
            yield return new WaitForSeconds(1.0f);
            var bm = BikeOf(mid);
            Check("garage: la moto comprata nasce subito, nella prima piazzola dopo quella di casa, col suo modello", bm != null && Flat(bm.transform.position, DesertGarage.BayPos(0, 1)) < 0.3f && bm.Def == mid && bm.rider == null,
                bm == null ? "non e' nata" : $"in {bm.transform.position:F1} invece di {DesertGarage.BayPos(0, 1):F1}");
            Check("garage: poggia a terra e ha il collider (e' parcheggiata)", bm != null && bm.Settled && Mathf.Abs(Gap(bm)) < 0.2f && bm.ColliderOn, bm == null ? "" : $"scarto {Gap(bm):F2} m");

            // la piu' economica passa davanti: l'ordine delle piazzole e' quello del negozio, e quella di prima scala di una
            var rCheap = DesertGarage.TryBuy(0, cheap, out _);
            yield return new WaitForSeconds(1.2f);
            var bc = BikeOf(cheap); bm = BikeOf(mid);
            Check("garage: la piu' economica va nella piazzola 1 e quella di prima scala nella 2 (l'ordine e' quello del negozio)", rCheap == DesertGarage.Result.Bought && bc != null && bm != null
                && Flat(bc.transform.position, DesertGarage.BayPos(0, 1)) < 0.3f && Flat(bm.transform.position, DesertGarage.BayPos(0, 2)) < 0.3f && Flat(bm.home, DesertGarage.BayPos(0, 2)) < 0.05f,
                bc == null || bm == null ? "manca una moto" : $"la prima in {bc.transform.position:F1}, la seconda in {bm.transform.position:F1}");

            // ---- resta nel personaggio
            var rec = Roster.Local;
            if (rec != null && rec.extras != null)
            {
                var list = rec.extras.Get(DesertGarage.Key)?.Get("owned");
                bool both = false;
                if (list != null && list.IsArray)
                {
                    bool a = false, b = false;
                    foreach (var it in list.Items) { string s = it.AsString(); if (s == cheap.id) a = true; if (s == mid.id) b = true; }
                    both = a && b;
                }
                Check("garage: le moto comprate sono scritte nel personaggio (extras), con il loro id", both);
                DesertGarage.Reload();
                Check("garage: dimenticata la memoria, il personaggio le ridice (sono sue per sempre)", DesertGarage.Owns(0, cheap) && DesertGarage.Owns(0, mid) && !DesertGarage.Owns(0, dear));
            }
            else Check("garage: (nessun personaggio di prova: la proprieta' non si salva, la si prova in memoria)", DesertGarage.Owns(0, cheap));

            // ---- il garage pieno: tutte e dieci, ognuna nella sua piazzola
            foreach (var d in sale) if (!DesertGarage.Owns(0, d)) { gm.AddPoints(d.price + 1); DesertGarage.TryBuy(0, d, out _); }
            yield return new WaitForSeconds(1.5f);
            int placed = 0, settled = 0, tris = 0;
            var bays = new HashSet<int>();
            foreach (var d in all)
            {
                var b = BikeOf(d);
                if (b == null) continue;
                placed++;
                int bay = DesertGarage.BayOf(0, d);
                if (bay >= 0 && Flat(b.transform.position, DesertGarage.BayPos(0, bay)) < 0.3f) bays.Add(bay);
                if (b.Settled) settled++;
                int bt = 0;
                foreach (var mf in b.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) bt += mf.sharedMesh.triangles.Length / 3;
                tris += bt;
                Debug.Log($"[PryingEyes] desertotest garage: {d.name} si disegna con {bt} triangoli");
            }
            Check("garage: con tutte le moto sono dieci, ognuna nella sua piazzola e a terra", placed == all.Count && bays.Count == all.Count && settled == all.Count, $"{placed} moto, {bays.Count} piazzole, {settled} a terra");
            Debug.Log($"[PryingEyes] desertotest garage: i dieci disegni insieme hanno {tris} triangoli");
            Check("garage: i dieci modelli insieme restano sotto i 520 mila triangoli (il 22 novembre ne misurano 468 mila)", tris > 0 && tris < 520000, $"{tris}");
            bool kitOk = true;
            foreach (var d in sale) if (d.grip.y < 0.85f || d.grip.y > 1.1f || d.seatHips.y < 0.9f || d.seatHips.y > 1.1f || d.eye.y < 1.25f || d.eye.y > 1.55f || d.seatLean < 30f || d.seatLean > 70f) kitOk = false;
            Check("garage: la posa in sella di ogni moto (bacino, piedi, occhio, manopole) e' in misure da persona", kitOk);

            // ---- quanto costa vederle tutte: il sagrato dalla cima della scalinata col garage pieno e senza le nove moto comprate (serve la scheda grafica)
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                var sky0 = DesertSky.I;
                if (sky0 != null) sky0.SetHour(10f, true);
                pl.Teleport(new Vector3(21.5f, 0.2f, 2.4f));
                yield return new WaitForSeconds(0.8f);
                Face(DesertGarage.BayPos(0, 5) + Vector3.up * 0.5f);
                yield return new WaitForSeconds(1.6f);
                float full = RenderMs();
                string fullSamples = renderSamples;
                var hidden = new List<GameObject>();
                foreach (var d in all) { var b = BikeOf(d); if (b != null && !d.Starter) { b.gameObject.SetActive(false); hidden.Add(b.gameObject); } }
                yield return new WaitForSeconds(0.6f);
                float empty = RenderMs();
                foreach (var g in hidden) if (g != null) g.SetActive(true);
                if (sky0 != null) sky0.Frozen = false;
                Debug.Log($"[PryingEyes] desertotest garage: dal sagrato a 1920x1080, col garage pieno {full:F1} ms ({fullSamples}), con la sola moto di casa {empty:F1} ms ({renderSamples}): le nove moto costano {full - empty:F1} ms");
                Check("garage: il garage pieno si vede in meno di 33 ms a 1920x1080", full > 0f && full < 33f, $"{full:F1} ms");
                Check("garage: le nove moto comprate costano meno di 4 ms in piu' (sagrato, di giorno, con le ombre)", full - empty < 4f, $"{full - empty:F1} ms");
                yield return new WaitForSeconds(0.4f);
            }

            // ---- guidare una moto comprata: la piu' veloce
            var bd = BikeOf(dear);
            var home = bd.transform.position;
            yield return GoTo(home.x, home.z - 2.6f, 2f);          // davanti alla moto (le piazzole sono in fila: di lato ci sarebbe la vicina)
            yield return new WaitForSeconds(0.6f);
            Face(bd.Point);
            yield return null; yield return null;
            Interact.Tick(pl.cam);
            var near = Interact.Current as DesertBike;
            Check("garage: guardandola da vicino Usa dice il suo nome", near == bd && Interact.Current.Prompt == "Sali sulla " + dear.name, near == null ? "niente" : Interact.Current.Prompt);
            Interact.TryUse();
            yield return null; yield return null;
            Check("garage: Usa mette in sella sulla moto comprata", pl.Riding && pl.Bike == bd && bd.rider == pl);
            Check("garage: il corpo sta sulla sua moto, e l'occhio e' quello della sua posa", Flat(pl.transform.position, bd.transform.position) < 0.3f && Mathf.Abs(bd.EyeWorld.y - bd.transform.position.y - dear.eye.y) < 0.4f);
            pl.yaw = pl.RideHeading; pl.pitch = 0.1f;
            yield return new WaitForSeconds(0.6f);
            Shot("garage_sella");
            DesertBike.botThrottle = 1f;
            float t0 = Time.time;
            yield return RideUntil(() => pl.RideSpeed > dear.top * 0.97f, 30f, new Vector3(0f, 0f, -1f));
            float tTop = Time.time - t0;
            Check("garage: la moto piu' veloce arriva alla sua velocita' massima, senza superarla", pl.RideSpeed > dear.top * 0.97f && pl.RideSpeed < dear.top * 1.03f, $"{pl.RideSpeed * 3.6f:F0} km/h in {tTop:F1} s, la scheda dice {dear.top * 3.6f:F0}");
            Check("garage: e va piu' forte della moto di casa", pl.RideSpeed > DesertBike.TopSpeed * 1.04f, $"{pl.RideSpeed * 3.6f:F0} contro {DesertBike.TopSpeed * 3.6f:F0} km/h");
            Check("garage: il motore ha la voce della sua moto e sale con i giri", bd.EngineVolume > 0.05f, $"volume {bd.EngineVolume:F2}");
            DesertBike.botThrottle = 0f;
            pl.RideRequestDismount();
            yield return RideUntil(() => !pl.Riding, 8f, new Vector3(0f, 0f, -1f));
            DesertBike.BotReset();
            yield return new WaitForSeconds(1.5f);
            Check("garage: si scende, e la moto resta li' dove e' (non torna da sola)", !pl.Riding && bd.rider == null && Flat(bd.transform.position, home) > 20f, $"a {Flat(bd.transform.position, home):F0} m dalla piazzola");
            // la rinascita la riporta a casa
            DesertBikes.ReturnHome(pl);
            yield return new WaitForSeconds(0.6f);
            Check("garage: alla rinascita le moto lasciate in giro tornano nella loro piazzola", Flat(bd.transform.position, DesertGarage.BayPos(0, DesertGarage.BayOf(0, dear))) < 0.5f, $"a {Flat(bd.transform.position, DesertGarage.BayPos(0, DesertGarage.BayOf(0, dear))):F1} m");

            if (shots)
            {
                SetPoints(0);
                foreach (var d in all)
                {
                    var b = BikeOf(d);
                    if (b == null) continue;
                    var p = b.transform.position;
                    var spot = PhotoSpot(p);
                    yield return GoTo(spot.x, spot.z, 2f);
                    yield return Snap("garage_" + d.id, 15.8f, b.Point + Vector3.up * 0.1f, 0.8f);
                }
                // il totem dell'insegna visto da ovest (da dove arriva chi scende la scalinata) e un cartello da vicino
                var gy0 = DesertGarage.BayPos(0, 0).y;
                float tx = DesertGarage.X0 - 1.6f - 1.2f, tz = DesertGarage.RowZ0 + 1.4f;
                yield return GoTo(tx - 4.6f, tz, 2f);
                yield return Snap("garage_totem", 15.8f, new Vector3(tx, gy0 + 3.2f, tz), 0.8f);
                var b1 = BikeOf(all[1]);
                if (b1 != null)
                {
                    yield return GoTo(b1.transform.position.x - 0.5f, b1.transform.position.z - 3.6f, 2f);
                    yield return Snap("garage_cartello", 15.8f, new Vector3(b1.transform.position.x, gy0 + 1.6f, DesertGarage.RowZ0 + 1.85f), 0.8f);
                }
                pl.Teleport(new Vector3(21.5f, 0.2f, 2.4f));
                yield return new WaitForSeconds(0.6f);
                var mid5 = DesertGarage.BayPos(0, 5);
                yield return Snap("garage_dalla_scalinata", 17.3f, mid5, 1.0f);
                yield return GoTo(26f, -17f, 2f);
                yield return Snap("garage_sera", 20.0f, mid5, 1.0f);
                yield return Snap("garage_notte", 22.5f, mid5, 1.0f);
            }
            ResetGarage();
        }

        // ================================================================== menu
        IEnumerator HubTest()
        {
            var pl = Pl; var gm = Gm; var hud = gm.hud;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset();
            ResetGarage();
            yield return Frames(3);
            yield return GoTo(26f, -12f, 2f);
            Check("menu: l'HUD c'e'", hud != null);
            if (hud == null) yield break;
            var pages = hud.HubPages;
            string[] want = { "Mappa", "Libri", "Magie", "Bozzo Moto Store", "Telecamera", "Cheat" };
            bool order = pages.Count == want.Length;
            for (int i = 0; order && i < want.Length; i++) if (pages[i].label != want[i]) order = false;
            Check("menu: six pages in navigation order", order, pages.Count + " pages");
            Check("menu: da chiuso il gioco gira (nessuna interfaccia sopra, i comandi liberi)", !hud.HubIsOpen && !GameMenu.Overlay && !GameMenu.Blocked);

            // il controller: i tre fronti nuovi del mapper (LB, RB, Back), che SuppressGameplay non cancella
            var mapper = new PadControls.Mapper();
            var raw = new PadControls.Raw { connected = true };
            mapper.Step(raw, 1f / 60f);
            raw.buttons = PadControls.LeftShoulder;
            mapper.Step(raw, 1f / 60f);
            bool prevOk = mapper.Prev && !mapper.Next && !mapper.Back && mapper.Down[(int)TouchControls.Action.Spell];
            mapper.SuppressGameplay();
            Check("menu: LB da' 'scheda prima' e SuppressGameplay toglie la magia ma non la scheda", prevOk && mapper.Prev && !mapper.Down[(int)TouchControls.Action.Spell]);
            raw.buttons = 0; mapper.Step(raw, 1f / 60f);
            Check("menu: un fronte dura un giro solo", !mapper.Prev);
            raw.buttons = PadControls.RightShoulder; mapper.Step(raw, 1f / 60f);
            bool nextOk = mapper.Next && !mapper.Prev && !mapper.Back;
            raw.buttons = PadControls.BackBtn; mapper.Step(raw, 1f / 60f);
            Check("menu: RB da' 'scheda dopo' e Back il suo fronte", nextOk && mapper.Back && !mapper.Next);
            raw.buttons = 0; mapper.Step(raw, 1f / 60f);
            Check("menu: un tocco corto di Back resta la 'mappa' del gioco (al rilascio)", mapper.Down[(int)TouchControls.Action.Map]);

            // ---- aprirlo
            SetPoints(0);
            Check("menu: si apre", hud.OpenHub(0));
            yield return new WaitForSeconds(0.7f);
            Check("menu: aperto blocca i comandi di chi lo apre e libera il mouse, ma non ferma il tempo", hud.HubIsOpen && GameMenu.Overlay && GameMenu.Blocked && !GameMenu.Paused && Time.timeScale > 0.5f);
            Check("menu: ha una cornice grande quanto serve e sta nella finestra", hud.HubPanelRect != null && hud.HubPanelRect.lossyScale.x > 0.5f);
            if (shots) Shot("menu_a_mappa");
            for (int i = 0; i < pages.Count; i++)
            {
                hud.SelectHubPage(i);
                yield return Frames(6);
                bool only = true;
                for (int k = 0; k < pages.Count; k++) if (pages[k].root.gameObject.activeSelf != (k == i)) only = false;
                Check("menu: la pagina " + pages[i].label + " e' la sola accesa, e gira senza errori", only && hud.HubPageIndex == i && hud.HubCurrentPage == pages[i]);
            }
            hud.SelectHubPage(0);

            // ---- la mappa
            yield return new WaitForSeconds(1.5f);
            var map = (HUD.HubMapPage)pages[0];
            string all = "";
            foreach (var t in map.root.GetComponentsInChildren<UnityEngine.UI.Text>(true)) all += t.text + "\n";
            Check("menu: la mappa dice dove sei, quanto manca alla cattedrale e dov'e' il garage", all.Contains("Cattedrale:") && all.Contains("Garage:") && all.Contains("x "), all.Length > 120 ? all.Substring(0, 120) : all);
            Check("menu: la mappa ha la carta del deserto (le tessere si dipingono)", hud.DesertMapPaper != null && hud.DesertMapPaper.Ready > 0, hud.DesertMapPaper == null ? "nessuna carta" : $"{hud.DesertMapPaper.Ready} su {hud.DesertMapPaper.Wanted}");
            Check("menu: le moto parcheggiate sono tondi turchesi e c'e' la moto di casa", all.Contains("Ruggine"), "");
            if (shots) { yield return new WaitForSeconds(0.3f); Shot("menu_a_mappa"); }

            // ---- i libri: si compra un nodo, coi punti
            hud.SelectHubPage(1);
            var books = (HUD.HubBookPage)pages[1];
            var s = Magic.State;
            uint w0 = s.Witch;
            books.Focus(Grimoire.Book.Stregoneria, 0);
            SetPoints(10);
            int boughtBefore = GrimoireUI.statBought;
            books.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("libri: senza punti non si compra, e lo dice", s.Witch == w0 && gm.score == 10 && hud.HubToastText.Contains("Servono"), hud.HubToastText);
            SetPoints(5000);
            books.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("libri: coi punti si compra il nodo, al prezzo del libro (1.250)", Grimoire.Has(s.Witch, 0) && gm.score == 5000 - 1250 && GrimoireUI.statBought == boughtBefore + 1, $"punti {gm.score}, nodi {s.Witch}");
            books.Focus(Grimoire.Book.Stregoneria, 14);
            books.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("libri: un nodo interno chiuso non si compra, e dice cosa serve", !Grimoire.Has(s.Witch, 14) && hud.HubToastText.Contains("serve prima"), hud.HubToastText);
            yield return Frames(4);
            if (shots) { hud.SelectHubPage(1); yield return new WaitForSeconds(0.3f); Shot("menu_b_libri"); }
            books.Focus(Grimoire.Book.Stregoneria, 12);
            SetPoints(5000);
            books.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("libri: con un esterno vicino l'interno si apre (3.000) e diventa la magia pronta", Grimoire.Has(s.Witch, 12) && s.SelectedSpell == 12 && gm.score == 5000 - 3000, $"punti {gm.score}, magia {s.SelectedSpell}");

            // ---- le magie
            hud.SelectHubPage(2);
            yield return Frames(4);
            var spells = (HUD.HubSpellPage)pages[2];
            s.SelectedSpell = -1;
            spells.OnShow(hud);
            spells.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("magie: un invio sulla magia che hai la prepara", s.SelectedSpell == 12, $"magia {s.SelectedSpell}");
            spells.Tick(hud, new HUD.HubInput { nx = 1 }, 0.016f);
            spells.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("magie: su un sigillo che non hai porta alla pagina Libri (dove si compra)", hud.HubPageIndex == 1 && books.Book == (int)Grimoire.Book.Arsenale, $"pagina {hud.HubPageIndex}, libro {books.Book}");
            hud.SelectHubPage(2);
            if (shots) { yield return new WaitForSeconds(0.3f); Shot("menu_c_magie"); }

            // ---- la camera: le impostazioni cambiano e alla fine tornano come erano
            hud.SelectHubPage(4);
            yield return Frames(4);
            var camPage = (HUD.HubCameraPage)pages[4];
            float fov0 = GameSettings.Fov, motion0 = GameSettings.Motion, sens0 = GameSettings.Sensitivity, pad0 = GameSettings.PadSensitivity;
            bool invM0 = GameSettings.InvertMouseY, invP0 = GameSettings.InvertPadY, tp0 = GameSettings.ThirdPerson;
            Vector3 framing0 = GameSettings.ThirdPersonFraming;
            string[] camKeys = { "video_fov", "video_motion", "input_sensitivity", "input_pad_sensitivity", "input_invert_y", "input_invert_pad_y",
                "video_thirdperson", "video_third_distance", "video_third_height", "video_third_shoulder" };
            var camKeyWasThere = new bool[camKeys.Length];
            for (int ck = 0; ck < camKeys.Length; ck++) camKeyWasThere[ck] = PlayerPrefs.HasKey(camKeys[ck]);
            Debug.Log($"[PryingEyes] desertotest menu: le impostazioni di chi gioca, da rimettere alla fine: campo visivo {fov0}, movimenti {motion0}, mouse {sens0}, controller {pad0}, asse Y mouse {invM0}, asse Y controller {invP0}, terza persona {tp0}");
            try
            {
                int d = fov0 < 100f ? 1 : -1;
                camPage.Tick(hud, new HUD.HubInput { nx = d }, 0.016f);
                Check("camera: sinistra e destra cambiano il campo visivo di un grado", Mathf.Abs(GameSettings.Fov - (fov0 + d)) < 0.01f, $"{fov0} -> {GameSettings.Fov}");
                camPage.Tick(hud, new HUD.HubInput { ny = -1 }, 0.016f);      // giu': la prospettiva
                camPage.Tick(hud, new HUD.HubInput { ny = -1, nx = framing0.z > 3f ? -1 : 1 }, 0.016f);
                camPage.Tick(hud, new HUD.HubInput { ny = -1, nx = framing0.y > 0.3f ? -1 : 1 }, 0.016f);
                camPage.Tick(hud, new HUD.HubInput { ny = -1, nx = framing0.x > 0f ? -1 : 1 }, 0.016f);
                Vector3 adjusted = GameSettings.ThirdPersonFraming;
                Check("camera: distanza, altezza e spalla si regolano dalla pagina Telecamera",
                    Mathf.Abs(adjusted.x - framing0.x) > 0.02f && Mathf.Abs(adjusted.y - framing0.y) > 0.02f && Mathf.Abs(adjusted.z - framing0.z) > 0.02f);
                camPage.Tick(hud, new HUD.HubInput { ny = -1 }, 0.016f);      // giu': l'intensita' dei movimenti
                camPage.Tick(hud, new HUD.HubInput { nx = motion0 > 0.5f ? -1 : 1 }, 0.016f);
                Check("camera: l'intensita' dei movimenti si regola a passi del cinque per cento", Mathf.Abs(Mathf.Abs(GameSettings.Motion - motion0) - 0.05f) < 0.001f, $"{motion0} -> {GameSettings.Motion}");
                camPage.Tick(hud, new HUD.HubInput { ny = -1 }, 0.016f);      // la sensibilita' del mouse
                camPage.Tick(hud, new HUD.HubInput { nx = sens0 > 1.5f ? -1 : 1 }, 0.016f);
                Check("camera: la sensibilita' del mouse si regola a passi di 0,05", Mathf.Abs(Mathf.Abs(GameSettings.Sensitivity - sens0) - 0.05f) < 0.001f, $"{sens0} -> {GameSettings.Sensitivity}");
                camPage.Tick(hud, new HUD.HubInput { ny = -1 }, 0.016f);      // la sensibilita' del controller
                camPage.Tick(hud, new HUD.HubInput { nx = pad0 > 1.5f ? -1 : 1 }, 0.016f);
                Check("camera: la sensibilita' del controller si regola a passi di 0,05", Mathf.Abs(Mathf.Abs(GameSettings.PadSensitivity - pad0) - 0.05f) < 0.001f, $"{pad0} -> {GameSettings.PadSensitivity}");
                camPage.Tick(hud, new HUD.HubInput { ny = -1 }, 0.016f);      // asse Y del mouse
                camPage.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
                Check("camera: un invio gira l'interruttore dell'asse Y del mouse", GameSettings.InvertMouseY == !invM0);
                if (shots) { yield return new WaitForSeconds(0.3f); Shot("menu_e_telecamera"); }
                // i valori di partenza
                for (int k = 0; k < 7; k++) camPage.Tick(hud, new HUD.HubInput { ny = -1 }, 0.016f);          // fino in fondo
                camPage.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
                Check("camera: 'Valori di partenza' riporta tutto a 75 gradi, movimenti al 100%, sensibilita' a 1 e assi normali", Mathf.Abs(GameSettings.Fov - 75f) < 0.01f && Mathf.Abs(GameSettings.Motion - 1f) < 0.001f && Mathf.Abs(GameSettings.Sensitivity - 1f) < 0.001f
                    && Mathf.Abs(GameSettings.PadSensitivity - 1f) < 0.001f && !GameSettings.InvertMouseY && !GameSettings.InvertPadY && !GameSettings.ThirdPerson
                    && GameSettings.ThirdPersonFraming == GameSettings.DefaultThirdPersonFraming,
                    $"fov {GameSettings.Fov}, movimenti {GameSettings.Motion}, mouse {GameSettings.Sensitivity}, pad {GameSettings.PadSensitivity}");
            }
            finally
            {
                GameSettings.SetFov(fov0); GameSettings.SetMotion(motion0); GameSettings.SetSensitivity(sens0); GameSettings.SetPadSensitivity(pad0);
                GameSettings.SetInvertMouseY(invM0); GameSettings.SetInvertPadY(invP0); GameSettings.SetThirdPerson(tp0);
                GameSettings.SetThirdShoulder(framing0.x); GameSettings.SetThirdHeight(framing0.y); GameSettings.SetThirdDistance(framing0.z);
                for (int ck = 0; ck < camKeys.Length; ck++) if (!camKeyWasThere[ck]) PlayerPrefs.DeleteKey(camKeys[ck]);
                PlayerPrefs.Save();
            }
            Check("camera: alla fine le impostazioni di chi gioca sono tornate quelle di prima", Mathf.Abs(GameSettings.Fov - fov0) < 0.01f && Mathf.Abs(GameSettings.Motion - motion0) < 0.001f && Mathf.Abs(GameSettings.Sensitivity - sens0) < 0.001f
                && Mathf.Abs(GameSettings.PadSensitivity - pad0) < 0.001f && GameSettings.InvertMouseY == invM0 && GameSettings.InvertPadY == invP0 && GameSettings.ThirdPerson == tp0);

            // ---- chiuderlo
            hud.SelectHubPage(3);
            yield return new WaitForSeconds(0.4f);
            if (shots) Shot("menu_d_negozio");
            hud.CloseHub();
            yield return null;
            Check("menu: si chiude, e libera i comandi", !hud.HubIsOpen && !GameMenu.Overlay && !GameMenu.Blocked);
            yield return null;
            Check("menu: l'Esc che l'ha chiuso non apre anche la pausa", !GameMenu.Paused && !GameMenu.InMenu);
            Check("menu: ha tenuto la pagina dove l'avevi lasciata", hud.HubPageIndex == 3);
            Check("menu: non e' inciampato (nessuna eccezione nel registro, il menu e' vivo)", !hud.HubIsDead);
            SetPoints(0);
            ResetGarage();
        }

        IEnumerator CheatsTest()
        {
            var pl = Pl;
            var gm = Gm;
            var hud = gm.hud;
            PlayerController.holdLook = true;
            PlayerController.botDir = Vector3.zero;
            yield return GoTo(26f, -12f, 2f);
            SetPoints(0);
            Check("cheats: opens on page six", hud.OpenHub(5) && hud.HubCurrentPage.label == "Cheat");
            if (!hud.HubIsOpen || hud.HubCurrentPage.label != "Cheat") yield break;

            var page = hud.HubCurrentPage;
            var confirm = new HUD.HubInput { confirm = true };
            var downAndConfirm = new HUD.HubInput { ny = -1, confirm = true };
            page.Tick(hud, confirm, 0f);
            Check("cheats: grants 10,000 points", gm.score == 10000);
            var before = Fabbrica.Banca.Snapshot();
            page.Tick(hud, downAndConfirm, 0f);
            bool materials = true;
            foreach (var resource in Risorse.Tutte)
                materials &= Fabbrica.Banca.Get(resource.id) == before[resource.id] + (Risorse.IsMaterial(resource.id) ? 100 : 0);
            Check("cheats: deposits each material in the cathedral bank", materials);
            page.Tick(hud, downAndConfirm, 0f);
            Check("cheats: enables flight from the page", pl.CheatFlight);
            page.Tick(hud, downAndConfirm, 0f);
            Check("cheats: enables invulnerability from the page", pl.CheatInvulnerability);
            pl.hp = pl.MaxHp * 0.5f;
            pl.shield = 0f;
            page.Tick(hud, downAndConfirm, 0f);
            Check("cheats: restores health and shield", pl.hp == pl.MaxHp && pl.shield >= 50f);
            pl.weapon.mag = pl.weapon.reserve = 0;
            page.Tick(hud, downAndConfirm, 0f);
            Check("cheats: fills magazine and ammo reserve", pl.weapon.mag > 0 && pl.weapon.reserve > 0);
            if (shots) { yield return new WaitForSeconds(0.3f); Shot("menu_f_cheat"); }
            hud.CloseHub();

            float hp = pl.hp;
            float shield = pl.shield;
            pl.invulnerable = false;
            pl.Hurt(30f);
            pl.Sedate(3f);
            Check("cheats: blocks damage and sedation in gameplay", pl.hp == hp && pl.shield == shield && pl.sedT == 0f);

            var raw = new PadControls.Raw { connected = true, buttons = PadControls.A };
            float ground = pl.transform.position.y;
            try
            {
                PadControls.Inject(raw);
                yield return new WaitForSeconds(0.4f);
                float air = pl.transform.position.y;
                Check("cheats: holding A ascends after closing Select", air > ground + 2f, $"{ground:F2} to {air:F2}");
                raw.buttons = 0;
                PadControls.Inject(raw);
                yield return new WaitForSeconds(0.3f);
                Check("cheats: releasing A holds altitude", Mathf.Abs(pl.transform.position.y - air) < 0.5f);
                raw.buttons = PadControls.B;
                PadControls.Inject(raw);
                yield return new WaitForSeconds(0.2f);
                Check("cheats: holding B descends", pl.transform.position.y < air - 1f);
            }
            finally { PadControls.Inject(null); }

            hud.OpenHub(5);
            page.Tick(hud, new HUD.HubInput { ny = -2, confirm = true }, 0f);
            Check("cheats: disables flight from the page", !pl.CheatFlight);
            hud.CloseHub();
            float release = pl.transform.position.y;
            yield return new WaitForSeconds(0.35f);
            Check("cheats: disabling flight restores gravity", pl.transform.position.y < release - 0.4f);
            pl.SetCheatFlight(true);
            pl.SetSpawn(pl.transform.position);
            Check("cheats: resetting the spawn clears flight and invulnerability", !pl.CheatFlight && !pl.CheatInvulnerability);

            SetPoints(0);
            Fabbrica.Banca.Restore(before);
            Fabbrica.Salva(true);
        }

        // ================================================================== negozio
        IEnumerator StoreTest()
        {
            var pl = Pl; var gm = Gm; var hud = gm.hud;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset();
            ResetGarage();
            SetPoints(0);
            yield return Frames(3);
            yield return GoTo(26f, -12f, 2f);
            Check("negozio: l'HUD c'e'", hud != null);
            if (hud == null) yield break;
            hud.OpenHub(3);
            yield return new WaitForSeconds(0.8f);
            var store = hud.HubCurrentPage as HUD.HubStorePage;
            Check("negozio: la quarta pagina e' Bozzo Moto Store, col catalogo delle nove moto in vendita", store != null && store.Count == Bikes.Count && store.Current != null && !store.Current.Free, store == null ? "non e' la pagina" : $"{store.Count} moto");
            if (store == null) { hud.CloseHub(); yield break; }
            var sale = ForSale();
            Check("negozio: parte dalla piu' economica", store.Current == sale[0], store.Current == null ? "" : store.Current.name);
            // il giro del deserto (ogni secondo e mezzo) mette un contorno nero a ogni scritta del HUD, e su carta chiara impasta le scritte piccole: quelle del menu ne restano fuori
            yield return new WaitForSeconds(2f);
            int outlined = 0, texts = 0;
            foreach (var tx in hud.HubPanelRect.GetComponentsInChildren<UnityEngine.UI.Text>(true)) { texts++; var ol = tx.GetComponent<UnityEngine.UI.Outline>(); if (ol != null && ol.enabled) outlined++; }
            Check("negozio: le scritte del menu non hanno il contorno nero del deserto (impasta quelle piccole su carta chiara)", texts > 20 && outlined == 0, $"{outlined} scritte con contorno su {texts}");
            bool gfx =SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            // senza scheda grafica la vetrina non si costruisce (la pagina lo sa): si controlla che non ci sia, e che il catalogo funzioni lo stesso
            if (!gfx) Check("negozio: senza scheda grafica non c'e' la vetrina (e nessun errore di RenderTexture)", store.Stage == null);
            else Check("negozio: la vetrina e' accesa (una camera, tre luci, il piatto)", store.Stage != null && store.Stage.Active && store.Stage.Rig != null, store.Stage == null ? "nessuna vetrina" : "");
            if (gfx && store.Stage != null) Check("negozio: la moto in vetrina e' sola (nessun pilota) e ha il modello della moto in vendita", !store.Stage.Rider && store.Stage.Bike == store.Current.pista);
            if (gfx && store.Stage != null)
            {
                yield return new WaitForSeconds(0.4f);
                var tex = store.Stage.Snapshot(40f, 5.4f, 1.3f, 0.74f, 26f, 360, 240);
                var px = tex.GetPixels32();
                int min = 255, max = 0; long sum = 0;
                foreach (var c in px) { int l = (c.r + c.g + c.b) / 3; sum += l; if (l < min) min = l; if (l > max) max = l; }
                Check("negozio: la foto della moto non e' un quadro vuoto (ha luci e ombre)", max - min > 60 && sum / px.Length > 8, $"da {min} a {max}, media {sum / px.Length}");
                if (shots)
                {
                    Directory.CreateDirectory(TestPaths.Out("deserto"));
                    File.WriteAllBytes(TestPaths.Out("deserto/negozio_foto_" + store.Current.id + ".png"), tex.EncodeToPNG());
                }
                Destroy(tex);
            }

            // ---- senza punti
            store.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("negozio: senza punti un invio non apre niente, e dice quanti ne mancano", store.Phase == 0 && hud.HubToastText.Contains("servono ancora"), hud.HubToastText);

            // ---- il "sei sicuro?" e Annulla
            var first = sale[0];
            SetPoints(first.price + 500);
            int score0 = gm.score;
            store.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("negozio: coi punti un invio apre la domanda (e il menu non si chiude con Indietro)", store.Phase == 1 && hud.HubCurrentPage.CapturesBack, "fase " + store.Phase);
            store.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("negozio: un secondo invio subito dopo non compra (serve piu' di un terzo di secondo)", store.Phase == 1 && !DesertGarage.Owns(0, first) && gm.score == score0);
            store.Tick(hud, new HUD.HubInput { back = true }, 0.016f);
            Check("negozio: Indietro annulla l'ordine: niente moto, i punti sono intatti", store.Phase == 0 && !DesertGarage.Owns(0, first) && gm.score == score0);
            yield return new WaitForSeconds(0.1f);
            Check("negozio: dopo l'annullamento il menu e' ancora aperto", hud.HubIsOpen);

            // ---- l'ordine
            store.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            yield return new WaitForSeconds(0.5f);
            if (shots) { yield return new WaitForSeconds(0.3f); Shot("negozio_domanda"); }
            store.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("negozio: la conferma compra: la ricevuta, i punti pagati, la moto e' tua", store.Phase == 2 && DesertGarage.Owns(0, first) && score0 - gm.score == first.price, $"fase {store.Phase}, pagati {score0 - gm.score} su {first.price}");
            yield return WaitFor(() => BikeOf(first) != null, 3f);
            yield return new WaitForSeconds(1.0f);
            var bike = BikeOf(first);
            Check("negozio: nel garage la moto compare nella sua piazzola, subito", bike != null && Flat(bike.transform.position, DesertGarage.BayPos(0, 1)) < 0.3f && bike.rider == null,
                bike == null ? "non e' nata" : $"in {bike.transform.position:F1}");
            if (shots) { yield return new WaitForSeconds(0.2f); Shot("negozio_ricevuta"); }
            store.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("negozio: la ricevuta si chiude con un invio, e si torna al catalogo", store.Phase == 0 && hud.HubIsOpen);
            store.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("negozio: la stessa moto non si ricompra: dice che e' gia' nel garage", store.Phase == 0 && hud.HubToastText.Contains("già nel tuo garage"), hud.HubToastText);

            // ---- scorrere il catalogo e vedere le altre
            store.Tick(hud, new HUD.HubInput { ny = -1 }, 0.016f);
            Check("negozio: giu' sceglie la moto dopo, e la vetrina la mostra", store.Current == sale[1], store.Current == null ? "" : store.Current.name);
            yield return Frames(3);
            if (gfx) Check("negozio: la vetrina ha cambiato moto", store.Stage != null && store.Stage.Bike == sale[1].pista, store.Stage == null ? "" : $"{store.Stage.Bike} invece di {sale[1].pista}");
            for (int i = 0; i < 20; i++) store.Tick(hud, new HUD.HubInput { ny = -1 }, 0.016f);
            Check("negozio: in fondo al catalogo resta sull'ultima", store.Current == sale[sale.Count - 1]);
            SetPoints(0);
            store.Tick(hud, new HUD.HubInput { confirm = true }, 0.016f);
            Check("negozio: la piu' cara, senza punti, dice quanti ne mancano", store.Phase == 0 && hud.HubToastText.Contains(HudNum(sale[sale.Count - 1].price)), hud.HubToastText);
            if (shots)
            {
                // tutte e nove, con punti da vendere, come le vede chi sceglie
                SetPoints(1000000);
                for (int i = 0; i < sale.Count; i++)
                {
                    store.Choose(i);
                    yield return new WaitForSeconds(0.5f);
                    Shot("negozio_" + sale[i].id);
                }
                // di notte: la vetrina ha il suo fondale e le sue luci, il cielo del deserto non c'entra
                var skyN = DesertSky.I;
                if (skyN != null)
                {
                    skyN.SetHour(23.5f, true);
                    store.Choose(5);
                    yield return new WaitForSeconds(0.9f);
                    Shot("negozio_notte");
                    skyN.Frozen = false;
                }
                SetPoints(0);
            }
            hud.CloseHub();
            yield return Frames(2);
            Check("negozio: il menu si chiude" + (gfx ? " e la vetrina si spegne" : ""), !hud.HubIsOpen && (!gfx || (store.Stage != null && !store.Stage.Active)));
            Check("negozio: nessun errore, il menu e' vivo", !hud.HubIsDead);
            ResetGarage();
        }

        // ================================================================== cabina
        /// <summary>Solo con -desertonly cabina: la vista in sella di ognuna delle dieci moto. Per ogni moto: ci sale, e dice quali pezzi del suo disegno coprono l'inquadratura (quanto del campo visivo, a che
        /// distanza, in alto e in basso) e fa la foto della cabina. Serve a trovare i pezzi che stanno davanti all'occhio (specchi, montanti, cupolini) e a tarare l'occhio di ogni moto.</summary>
        IEnumerator CockpitTest()
        {
            var pl = Pl;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset();
            ResetGarage();
            SetPoints(0);
            yield return Frames(3);
            foreach (var d in ForSale()) DesertGarage.Grant(0, d);
            yield return new WaitForSeconds(1.5f);
            var cam = pl.cam.GetComponent<Camera>();
            foreach (var d in DesertBikeDefs.All)
            {
                var b = BikeOf(d);
                if (b == null) { Check("cabina: " + d.name + " e' nel garage", false); continue; }
                var home = b.transform.position;
                yield return GoTo(home.x, home.z - 2.6f, 2f);
                yield return new WaitForSeconds(0.5f);
                Face(b.Point);
                yield return null; yield return null;
                Interact.Tick(pl.cam);
                Interact.TryUse();
                yield return new WaitForSeconds(0.7f);
                if (!pl.Riding || pl.Bike != b) { Check("cabina: " + d.name + ": in sella", false, "non sale"); continue; }
                pl.yaw = pl.RideHeading; pl.pitch = 0.1f;
                yield return new WaitForSeconds(0.7f);
                // quali pezzi coprono l'inquadratura: il box di ogni pezzo acceso, proiettato sul campo visivo (una stima per eccesso: un pezzo storto ha un box piu' grande di lui)
                var big = new List<string>();
                float worstUpper = 0f; string worstName = "";
                foreach (var r in b.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                    var bnd = r.bounds;
                    float x0 = 9f, x1 = -9f, y0 = 9f, y1 = -9f, nearest = 99f; bool any = false;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = bnd.center + Vector3.Scale(bnd.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                        var vp = cam.WorldToViewportPoint(c);
                        if (vp.z <= 0.05f) continue;
                        any = true; nearest = Mathf.Min(nearest, vp.z);
                        x0 = Mathf.Min(x0, vp.x); x1 = Mathf.Max(x1, vp.x); y0 = Mathf.Min(y0, vp.y); y1 = Mathf.Max(y1, vp.y);
                    }
                    if (!any || nearest > 3f) continue;
                    float w = Mathf.Max(0f, Mathf.Min(x1, 1f) - Mathf.Max(x0, 0f));
                    float cover = w * Mathf.Max(0f, Mathf.Min(y1, 1f) - Mathf.Max(y0, 0f));
                    float upper = w * Mathf.Max(0f, Mathf.Min(y1, 1f) - Mathf.Max(y0, 0.4f));          // sopra il 40 % dal basso: dove si guarda
                    if (upper > worstUpper) { worstUpper = upper; worstName = r.name + " (" + (r.transform.parent != null ? r.transform.parent.name : "") + ")"; }
                    if (cover > 0.02f) big.Add($"{r.name} ({(r.transform.parent != null ? r.transform.parent.name : "")}) a {nearest:F2} m copre {cover * 100f:F0} % [x {x0:F2}-{x1:F2}, y {y0:F2}-{y1:F2}]");
                }
                Debug.Log($"[PryingEyes] desertotest cabina {d.name}: occhio a {Flat(cam.transform.position, b.transform.position):F2} m dal centro, {cam.transform.position.y - b.transform.position.y:F2} m su; pezzi grandi: " + (big.Count == 0 ? "nessuno" : string.Join("; ", big)));
                // i pezzi della moto che passano dietro l'occhio o dentro il piano vicino: il loro box proiettato (sopra) li vede piu' piccoli di quello che sono, ed e' il caso delle colonne scure ai lati
                var behindEye = new List<string>();
                foreach (var r in b.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                    var bnd = r.bounds;
                    int behind = 0; float nearest = 99f;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = bnd.center + Vector3.Scale(bnd.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                        var vp = cam.WorldToViewportPoint(c);
                        if (vp.z <= 0.05f) behind++; else nearest = Mathf.Min(nearest, vp.z);
                    }
                    if (behind == 0 || behind == 8) continue;
                    var loc = cam.transform.InverseTransformPoint(bnd.center);
                    behindEye.Add($"{r.name} ({(r.transform.parent != null ? r.transform.parent.name : "")}) [{bnd.size.x:F2}x{bnd.size.y:F2}x{bnd.size.z:F2} m] {behind}/8 angoli dietro l'occhio, centro nel sistema dell'occhio ({loc.x:F2}, {loc.y:F2}, {loc.z:F2})");
                }
                Debug.Log($"[PryingEyes] desertotest cabina {d.name}: pezzi della moto a cavallo del piano vicino: " + (behindEye.Count == 0 ? "nessuno" : string.Join(" | ", behindEye)));
                // e i pezzi che non sono della moto ma stanno davanti all'occhio (il corpo del giocatore, le maniche): chi non e' della moto non compare sopra
                var others = new List<string>();
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r.transform.IsChildOf(b.transform)) continue;
                    var bnd = r.bounds;
                    if ((bnd.center - cam.transform.position).sqrMagnitude > 9f) continue;
                    float x0 = 9f, x1 = -9f, y0 = 9f, y1 = -9f, nearest = 99f;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = bnd.center + Vector3.Scale(bnd.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                        var vp = cam.WorldToViewportPoint(c);
                        if (vp.z <= 0.05f) continue;
                        nearest = Mathf.Min(nearest, vp.z);
                        x0 = Mathf.Min(x0, vp.x); x1 = Mathf.Max(x1, vp.x); y0 = Mathf.Min(y0, vp.y); y1 = Mathf.Max(y1, vp.y);
                    }
                    string path = r.name; var t = r.transform.parent;
                    for (int k = 0; k < 4 && t != null; k++, t = t.parent) path = t.name + "/" + path;
                    others.Add($"{path} [{bnd.size.x:F2}x{bnd.size.y:F2}x{bnd.size.z:F2} m] a {nearest:F2} m, x {x0:F2}-{x1:F2}, y {y0:F2}-{y1:F2}");
                }
                Debug.Log($"[PryingEyes] desertotest cabina {d.name}: fuori dalla moto, entro 3 m dall'occhio: " + (others.Count == 0 ? "nessuno" : string.Join(" | ", others)));
                // la moto di casa e' il disegno di sempre (il suo manubrio col faro, col box proiettato per eccesso, copre il 7-10 %): per lei il limite e' piu' largo, e basta a vedere se il disegno cresce
                float limit = d.Starter ? 0.12f : 0.03f;
                Check("cabina: " + d.name + $": nessun pezzo copre piu' del {limit * 100f:F0} % dell'inquadratura sopra il bordo basso (la parte che si guarda)", worstUpper < limit, $"il peggiore {worstName} {worstUpper * 100f:F1} %");
                Shot("cabina_" + d.id);
                pl.RideRequestDismount();
                yield return RideUntil(() => !pl.Riding, 5f, new Vector3(0f, 0f, -1f));
                yield return new WaitForSeconds(0.3f);
            }
            ResetGarage();
        }

        static string HudNum(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.');
    }
}
#endif
