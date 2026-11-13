using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>I passi di CoopTest per il ciclo a ondate (SurvivalMode, quello predefinito). Al posto di candela e round: V5 un ospitato compra
    /// un'area (la decide l'ospite; da lontano la richiesta e' rifiutata e i punti tornano), V6 un'ondata (nemici solo nelle aree aperte, premio
    /// per tutti), V7 altre due ondate e l'ascensore si apre per tutti; L0s-L2s il compagno entra a ondata in corso con le aree gia' comprate;
    /// SoloWave l'ondata che l'ospite fa da solo dopo l'uscita del compagno. Le ondate partono ferme (-cooptest le tiene in pausa, SurvivalMode.Hold):
    /// i passi che contano punti e nemici uno per uno non devono trovarsele in mezzo, e le fa partire il copione quando serve.</summary>
    public sealed partial class CoopTest
    {
        // dati passati da un passo all'altro
        int boughtRoom = -1;                                    // l'area comprata (la stessa su tutti i processi)
        int peerOpenRooms, peerBoughtRoom = -1, roomsSeq;       // cio' che l'ospite dice sulle aree (messaggio di prova 5)
        int waveSeen, waveOutside, waveMaxAlive;                // risultati dell'ultimo FightWave

        /// <summary>L'ospite racconta quante aree ha aperte e quale ha comprato (gli ospitati lo ricevono in Receive, sub 5).</summary>
        void SendRooms()
        {
            var access = RoomAccessDoors.I;
            if (!Coop.IsHost || access == null) return;
            G.SendTest(w.Reset().U8(5).U8(access.Access.OpenRooms.Count).U8(boughtRoom + 1).ToArray());
            Log($"aree: {access.Access.OpenRooms.Count} aperte, comprata {boughtRoom}");
        }

        /// <summary>La prima porta da comprare, nell'ordine del piano: lo stesso piano da' la stessa porta su tutti i processi.</summary>
        static PaidRoomDoor FirstDoor(RoomAccessDoors access)
        {
            foreach (var d in access.Doors) if (d != null && d.TargetRoom >= 0) return d;
            return null;
        }

        /// <summary>In piedi davanti alla porta, dalla parte gia' aperta, a guardarla (come chi sta per premere F).</summary>
        static void PlaceAt(PaidRoomDoor door, int fromRoom)
        {
            Vector3 pos = NavPoint(door.Front(fromRoom));
            Vector3 look = door.Point - (pos + Vector3.up * 1.6f);
            var pl = GameManager.I.player;
            pl.Teleport(pos);
            pl.yaw = Mathf.Atan2(look.x, look.z);
            pl.pitch = -Mathf.Atan2(look.y, Mathf.Sqrt(look.x * look.x + look.z * look.z));      // pitch positivo = guarda in basso
        }

        /// <summary>Il centro navigabile dell'area aperta piu' lontana da 'from'; vero solo se e' lontana almeno 'minDist' metri.</summary>
        static bool FarOpenPoint(RoomAccessDoors access, Vector3 from, float minDist, out Vector3 p)
        {
            p = from;
            float best = -1f;
            foreach (var room in FloorDirector.I.cur.plan.rooms)
            {
                if (room.IsLift || !access.Access.IsOpen(room.id)) continue;
                if (!NavMesh.SamplePosition(new Vector3(room.CenterX, from.y, room.CenterZ), out var h, 3f, NavMesh.AllAreas)) continue;
                float d = Flat(h.position - from);
                if (d > best) { best = d; p = h.position; }
            }
            return best >= minDist;
        }

        /// <summary>Come Fight, ma per le ondate: lascia in waveSeen quanti nemici ha visto nascere, in waveOutside quanti in un'area ancora chiusa
        /// (alla prima occhiata) e in waveMaxAlive il massimo di vivi contemporaneamente.</summary>
        IEnumerator FightWave(RoundDirector rd, float minAge, float timeout)
        {
            var fd = FloorDirector.I; var access = RoomAccessDoors.I;
            var seen = new Dictionary<Enemy, float>();
            var claimed = new Dictionary<Enemy, float>();
            fightKills = waveSeen = waveOutside = waveMaxAlive = 0;
            float t0 = Time.time;
            while (rd.Fighting && Time.time - t0 < timeout)
            {
                int alive = 0;
                foreach (var e in new List<Enemy>(Enemy.All))
                {
                    if (e == null || e.IsDead) continue;
                    alive++;
                    if (!seen.TryGetValue(e, out float t1))
                    {
                        seen[e] = Time.time; waveSeen++;
                        var room = fd.cur.RoomAt(e.transform.position);
                        if (room != null && !access.Access.IsOpen(room.id)) waveOutside++;
                        continue;
                    }
                    if (!e.relentless || Time.time - t1 < minAge) continue;
                    if (claimed.TryGetValue(e, out float tc) && Time.time - tc < 1.5f) continue;
                    claimed[e] = Time.time;
                    e.Damage(100000f, e.AimPoint, false, true, DamageSource.Gun);
                    fightKills++;
                }
                waveMaxAlive = Mathf.Max(waveMaxAlive, alive);
                yield return new WaitForSeconds(0.3f);
            }
        }

        // V5 ------------------------------------------------------------------ un'area comprata dall'ospitato (la decide l'ospite)
        IEnumerator V5()
        {
            Safe();
            var gm = GameManager.I;
            var access = RoomAccessDoors.I;
            Check("le porte a pagamento ci sono su tutti i processi", access != null && access.Doors.Count > 0,
                access == null ? "nessuna" : $"{access.Doors.Count} porte, {access.Access.OpenRooms.Count} aree aperte");
            var door = access != null ? FirstDoor(access) : null;
            Check("c'e' un'area da comprare accanto alla partenza", door != null);
            if (door == null) { aborted = true; yield break; }
            int room = door.TargetRoom, from = access.OpenSide(door), price = access.Access.Price(room);
            boughtRoom = room;
            Check("il prezzo della prima area e' 500", price == 500, price + " punti");
            int s0 = gm.score;
            if (Coop.IsHost || !Primary)
            {
                yield return Until(() => access.Access.IsOpen(room), 25f);
                Check("l'area comprata dal compagno e' aperta anche qui (lo dice l'ospite)", access.Access.IsOpen(room), $"area {room}");
                yield return Until(() => door.Shutter.IsUp, 6f);
                Check("la porta pagata dal compagno e' salita", door.Shutter.IsUp);
                yield return new WaitForSeconds(1f);
                Check("l'acquisto del compagno non costa niente a me", gm.score == s0, $"{gm.score - s0:+0;-0;0} punti");
            }
            else
            {
                gm.AddPoints(3000);                         // i punti per comprare (i colpi dei passi prima ne hanno dati pochi)
                s0 = gm.score;
                PlaceAt(door, from);
                yield return new WaitForSeconds(1.5f);      // l'ospite deve vedermi accanto alla porta
                door.Use();                                 // la chiedo all'ospite, che controlla e decide
                yield return Until(() => access.Access.IsOpen(room), 10f);
                Check("l'ospite accetta: l'area comprata si apre anche per me", access.Access.IsOpen(room), $"area {room}");
                yield return Until(() => door.Shutter.IsUp, 6f);
                Check("la porta pagata e' salita", door.Shutter.IsUp);
                yield return new WaitForSeconds(1.5f);
                Check("il prezzo e' pagato una volta sola", gm.score == s0 - price, $"{gm.score - s0:+0;-0;0} punti, prezzo {price}");
            }

            // una seconda richiesta da lontano: l'ospite controlla la vicinanza, rifiuta, i punti messi da parte tornano e l'area resta chiusa
            var door2 = FirstDoor(access);
            Vector3 far = default;
            if (door2 == null || !FarOpenPoint(access, door2.Point, door2.Radius + 3f, out far))
            {
                Log("info: nessuna seconda porta con un'area aperta lontana: prova del rifiuto saltata");
                yield break;
            }
            int room2 = door2.TargetRoom, price2 = access.Access.Price(room2);
            if (Coop.IsHost || !Primary)
            {
                yield return new WaitForSeconds(7f);        // il tempo che serve al compagno per andare lontano, chiedere e ricevere il rifiuto
                Check("la richiesta rifiutata non apre nessuna area", !access.Access.IsOpen(room2) && door2.Shutter.IsDown, $"area {room2}");
            }
            else
            {
                gm.player.Teleport(far);
                yield return new WaitForSeconds(1.5f);
                int sb = gm.score;
                G.RequestRoom(room2);
                Check("i punti della richiesta sono messi da parte subito", gm.score == sb - price2, $"{gm.score - sb:+0;-0;0} punti, prezzo {price2}");
                yield return Until(() => gm.score == sb, 8f);
                Check("da lontano l'ospite rifiuta: i punti tornano e l'area resta chiusa", gm.score == sb && !access.Access.IsOpen(room2), $"{gm.score - sb:+0;-0;0} punti");
            }
        }

        // V6 ------------------------------------------------------------------ un'ondata: nemici solo nelle aree aperte, premio per tutti
        IEnumerator V6()
        {
            Safe();
            var fd = FloorDirector.I;
            var rd = RoundDirector.I;
            var gm = GameManager.I;
            var access = RoomAccessDoors.I;
            Check("direttore delle ondate e porte presenti", rd != null && access != null);
            if (rd == null || access == null) { aborted = true; yield break; }

            // via tutti i nemici dall'ospite (anche quelli dei passi di prima): spariscono anche sugli ospitati
            if (Coop.IsHost)
            {
                foreach (var e in new List<Enemy>(Enemy.All)) if (e != null) Enemy.Remove(e);
                yield return new WaitForSeconds(1.5f);
                SendEnemies();
            }
            else yield return ExpectEnemies("tolti tutti i nemici dall'ospite, spariscono anche i pupazzi", 10f);

            int round0 = RoundDirector.Round, waves0 = rd.FloorWaves, s0 = gm.score;     // prima della sincronia: dopo, l'ondata puo' gia' essere partita
            yield return Barrier();
            if (aborted) yield break;

            SurvivalMode.Hold(false);                       // da qui le ondate possono partire; la prima la fa partire l'ospite adesso
            if (Coop.IsHost) rd.SurvivalNextWaveIn(0f);
            yield return Until(() => rd.Fighting, 12f);
            Check("l'ondata parte su tutti (la decide l'ospite)", rd.Fighting, $"ondata {RoundDirector.Round}");
            if (!rd.Fighting) yield break;
            roundNo = RoundDirector.Round;
            Check("stesso numero di ondata su tutti", RoundDirector.Round == round0 + 1, $"ondata {RoundDirector.Round} (prima {round0})");
            if (Coop.IsHost)
            {
                Check("i bersagli dell'ospite sono quanti i giocatori", Targets.Count == Expected, $"{Targets.Count} su {Expected}");
                int want = RoundDirector.SurvivalTarget(roundNo, Expected);
                Check("l'ondata e' dimensionata sul numero di giocatori", rd.Target == want, $"{rd.Target} nemici, attesi {want}");
            }

            // si combatte: gli ospitati uccidono i pupazzi (colpi a distanza), l'ospite solo quelli che restano troppo
            float t0 = Time.time;
            yield return FightWave(rd, Coop.IsHost ? 14f : 0.8f, 150f);
            Check("l'ondata finisce da sola su tutti", !rd.Fighting, $"{Time.time - t0:F0} s, {waveSeen} nemici visti, colpi mortali dati qui {fightKills}");
            Check("i nemici nascono solo nelle aree aperte", waveOutside == 0, $"{waveOutside} in un'area chiusa");
            if (Coop.IsHost)
            {
                int cap = RoundDirector.SurvivalCap(roundNo, Expected);
                Check("il tetto dei nemici vivi insieme e' rispettato", waveMaxAlive <= cap, $"massimo {waveMaxAlive}, tetto {cap}");
            }
            yield return new WaitForSeconds(2f);
            int reward = RoundRules.Reward(roundNo);
            // il premio dell'ondata e' di tutti; i punti delle uccisioni solo di chi le ha fatte (gli ospitati)
            Check("il premio dell'ondata e' di tutti", gm.score - s0 >= reward, $"+{gm.score - s0} punti (premio {reward})");
            Check("stessa conta di ondate superate su tutti, ascensore ancora chiuso", rd.FloorWaves == waves0 + 1 && !fd.ExitOpen, $"{rd.FloorWaves} superate");
            SurvivalMode.Hold(true);                        // la prossima la decide il copione
        }

        // V7 ------------------------------------------------------------------ altre due ondate: l'ascensore si apre per tutti
        IEnumerator V7()
        {
            Safe();
            var fd = FloorDirector.I;
            var rd = RoundDirector.I;
            for (int i = 0; i < 2; i++)
            {
                int waves0 = rd.FloorWaves, round0 = RoundDirector.Round;
                yield return Barrier();
                if (aborted) yield break;
                SurvivalMode.Hold(false);
                if (Coop.IsHost) rd.SurvivalNextWaveIn(0f);
                yield return Until(() => rd.Fighting, 12f);
                Check($"ondata {round0 + 1}: parte su tutti", rd.Fighting && RoundDirector.Round == round0 + 1, $"ondata {RoundDirector.Round}");
                if (!rd.Fighting) yield break;
                yield return FightWave(rd, Coop.IsHost ? 14f : 0.8f, 150f);
                yield return new WaitForSeconds(1.5f);
                SurvivalMode.Hold(true);
                Check($"ondata {round0 + 1}: finisce su tutti, {waves0 + 1} superate", !rd.Fighting && rd.FloorWaves == waves0 + 1, $"{rd.FloorWaves} superate");
                Check($"ondata {round0 + 1}: nemici solo nelle aree aperte", waveOutside == 0, $"{waveOutside} in un'area chiusa, {waveSeen} visti");
                if (i == 0) Check("con due ondate l'ascensore e' ancora chiuso", !fd.ExitOpen);
            }
            yield return Until(() => fd.ExitOpen, 5f);
            Check("tre ondate superate: l'ascensore si apre su tutti", fd.ExitOpen && fd.cur.exit != null && fd.cur.exit.unlocked, $"ondate superate {rd.FloorWaves}");
        }

        /// <summary>Il piano nuovo (dopo S8) riparte da capo: solo le aree iniziali aperte, nessuna ondata, ascensore chiuso.</summary>
        void CheckFreshFloor()
        {
            var fd = FloorDirector.I;
            var rd = RoundDirector.I;
            var access = RoomAccessDoors.I;
            int start = new RoomAccessState(fd.cur.plan).OpenRooms.Count;
            Check("il piano nuovo riparte con le sole aree iniziali aperte, senza ondate e con l'ascensore chiuso",
                access != null && access.Access.OpenRooms.Count == start && rd != null && rd.FloorWaves == 0 && !rd.Fighting && !fd.ExitOpen,
                $"{(access != null ? access.Access.OpenRooms.Count : -1)} aree aperte su {start}, ondate {(rd != null ? rd.FloorWaves : -1)}");
        }

        // L ------------------------------------------------------------------ -cooplate: il compagno entra quando l'ondata e' gia' in corso
        IEnumerator L0s()
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
                var access = RoomAccessDoors.I;
                var door = access != null ? FirstDoor(access) : null;
                if (door == null || rd == null) { Log("nessuna area da comprare su questo piano: prova non applicabile"); aborted = true; yield break; }
                var gm = GameManager.I;
                int room = door.TargetRoom, price = access.Access.Price(room);
                gm.AddPoints(3000);
                int s0 = gm.score;
                PlaceAt(door, access.OpenSide(door));
                yield return new WaitForSeconds(1f);
                door.Use();
                yield return Until(() => access.Access.IsOpen(room), 3f);
                Check("l'ospite da solo compra un'area", access.Access.IsOpen(room) && gm.score == s0 - price, $"area {room}, {gm.score - s0:+0;-0;0} punti");
                boughtRoom = room;
                SurvivalMode.Hold(false);
                rd.SurvivalNextWaveIn(0f);
                yield return Until(() => rd.Fighting, 6f);
                Check("l'ondata parte con l'ospite da solo", rd.Fighting, $"ondata {RoundDirector.Round}");
                yield return Until(PeerShown, 90f);
                Check("il compagno entra a ondata in corso", PeerShown() && rd.Fighting, $"ondata in corso {rd.Fighting}");
            }
            else
            {
                yield return Until(() => g.FloorBuilt && g.Started && PeerShown() && !fd.transitioning, 90f);
                Check("entrato: piano costruito e ospite in vista", g.FloorBuilt && PeerShown());
                Safe();
            }
        }

        IEnumerator L1s()
        {
            var rd = RoundDirector.I;
            var access = RoomAccessDoors.I;
            Safe();
            if (Coop.IsHost)
            {
                yield return new WaitForSeconds(3f);
                SendRooms();
                yield return Barrier();                 // il nuovo arrivato ha finito di controllare le aree
                if (aborted) yield break;
                // il rapporto dei nemici parte solo adesso: sono all'attacco e corrono verso chi e' appena arrivato, uno vecchio non coinciderebbe piu'
                SendEnemies();
                yield break;
            }
            SurvivalMode.Hold(false);
            yield return Until(() => rd.Fighting, 10f);
            Check("arrivato a ondata in corso: l'ondata e' in corso anche qui", rd.Fighting, $"ondata {RoundDirector.Round}");
            Check("il numero dell'ondata e' quello dell'ospite (la prima)", RoundDirector.Round == 1, "ondata " + RoundDirector.Round);
            yield return Until(() => roomsSeq > 0, 10f);
            Check("le aree comprate dall'ospite prima del mio arrivo sono aperte anche qui",
                roomsSeq > 0 && access.Access.OpenRooms.Count == peerOpenRooms && access.Access.IsOpen(peerBoughtRoom),
                $"{access.Access.OpenRooms.Count} aperte qui, {peerOpenRooms} sull'ospite, comprata {peerBoughtRoom}");
            yield return new WaitForSeconds(2.5f);
            int open = 0, closed = 0;
            bool openUp = true, closedDown = true;
            foreach (var d in access.Doors)
            {
                if (access.Access.IsOpen(d.Opening.a) && access.Access.IsOpen(d.Opening.b)) { open++; if (!d.Shutter.IsUp) openUp = false; }
                else { closed++; if (!d.Shutter.IsDown) closedDown = false; }
            }
            Check("le porte delle aree aperte sono gia' su", open > 0 && openUp, $"{open} porte aperte");
            Check("le porte delle aree ancora chiuse sono giu'", closedDown, $"{closed} porte chiuse");
            yield return Barrier();
            if (aborted) yield break;
            yield return ExpectEnemies("i nemici dell'ondata in corso sono qui come pupazzi uguali", 10f);
        }

        IEnumerator L2s()
        {
            var rd = RoundDirector.I;
            Safe();
            float t0 = Time.time;
            yield return FightWave(rd, Coop.IsHost ? 14f : 0.8f, 150f);
            Check("l'ondata finisce da sola, anche per chi e' entrato dopo", !rd.Fighting && rd.FloorWaves == 1, $"{Time.time - t0:F0} s, colpi mortali {fightKills}, superate {rd.FloorWaves}");
            Check("i nemici sono nati solo nelle aree aperte", waveOutside == 0, $"{waveOutside} in un'area chiusa");
            SurvivalMode.Hold(true);
        }

        // E ------------------------------------------------------------------ l'ospite rimasto solo fa un'ondata intera
        IEnumerator SoloWave()
        {
            var rd = RoundDirector.I;
            if (rd == null) { Log("info: nessun direttore delle ondate"); yield break; }
            SurvivalMode.Hold(false);
            rd.SurvivalNextWaveIn(0f);
            yield return Until(() => rd.Fighting, 8f);
            Check("l'ondata parte da sola dopo l'uscita del compagno", rd.Fighting, $"ondata {RoundDirector.Round}");
            if (!rd.Fighting) yield break;
            int want = RoundDirector.SurvivalTarget(RoundDirector.Round, 1);
            Check("l'ondata si dimensiona su un giocatore solo", rd.Target == want, $"{rd.Target} nemici, attesi {want}");
            yield return FightWave(rd, 1f, 120f);
            Check("e finisce", !rd.Fighting, $"colpi mortali {fightKills}");
            SurvivalMode.Hold(true);
        }
    }
}
