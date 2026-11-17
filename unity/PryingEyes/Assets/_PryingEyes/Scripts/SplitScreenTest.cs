#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -splittest [-seed S -floor F]: le parti dello schermo condiviso che non hanno bisogno di una partita
    /// a piu' giocatori. Prima la parte pura: disposizione delle porzioni (2, 3 e 4 giocatori: nello schermo, senza sovrapposizioni, tutto coperto),
    /// assegnazione dei controller, campo visivo nelle porzioni larghe, traduzione XInput -> comandi (zone morte, grilletti, secondo livello con
    /// LB, corsa a scatti). Poi il giocatore vero: con uno stato XInput finto (PadControls.Inject) cammina, gira, spara, salta, si abbassa e corre.
    /// La lettura vera di XInput si prova solo come "risponde senza errori": un controller fisico non c'e'.
    /// La partita a piu' giocatori la prova -seatstest (SeatsTest.cs, lanciato da tools/coop/run_split.ps1).</summary>
    public sealed class SplitScreenTest : MonoBehaviour
    {
        int checks, failed, exceptions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-splittest") >= 0) new GameObject("SplitScreenTest").AddComponent<SplitScreenTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] splittest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            PadControls.Inject(null); PadControls.Disable();
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] splittest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        static PadControls.Raw Pad(ushort buttons = 0, int lt = 0, int rt = 0, int lx = 0, int ly = 0, int rx = 0, int ry = 0) =>
            new PadControls.Raw { connected = true, buttons = buttons, leftTrigger = (byte)lt, rightTrigger = (byte)rt, lx = (short)lx, ly = (short)ly, rx = (short)rx, ry = (short)ry };

        static bool IsDown(PadControls.Mapper m, TouchControls.Action a) => m.Down[(int)a];
        static bool IsHeld(PadControls.Mapper m, TouchControls.Action a) => m.Held[(int)a];

        // ------------------------------------------------------------------ parte pura

        void Layouts()
        {
            // Camera.rect: frazioni dello schermo, origine in basso a sinistra
            for (int n = 2; n <= 4; n++)
            {
                bool inside = true, disjoint = true; float sum = 0f;
                for (int i = 0; i < n; i++)
                {
                    var t = SplitScreen.ViewOf(i, n);
                    sum += t.width * t.height;
                    inside &= t.width > 0f && t.height > 0f && t.xMin >= 0f && t.yMin >= 0f && t.xMax <= 1f && t.yMax <= 1f;
                    for (int j = 0; j < i; j++)
                    {
                        var u = SplitScreen.ViewOf(j, n);
                        disjoint &= t.xMax <= u.xMin || u.xMax <= t.xMin || t.yMax <= u.yMin || u.yMax <= t.yMin;
                    }
                }
                Check($"{n} giocatori: le porzioni stanno nello schermo, non si sovrappongono e lo coprono tutto", inside && disjoint && Mathf.Abs(sum - 1f) < 1e-4f);
            }
            var l = SplitScreen.ViewOf(0, 2); var r = SplitScreen.ViewOf(1, 2);
            Check("due giocatori: uno a sinistra e uno a destra, alti come lo schermo", l.xMin == 0f && r.xMin == l.xMax && l.height == 1f && r.height == 1f && r.xMax == 1f);
            var t3 = SplitScreen.ViewOf(2, 3);
            Check("tre giocatori: due sopra e il terzo sotto, a tutta larghezza", t3.xMin == 0f && t3.width == 1f && t3.yMin == 0f && SplitScreen.ViewOf(0, 3).yMin > 0f && SplitScreen.ViewOf(1, 3).yMin > 0f);
            var q = new[] { SplitScreen.ViewOf(0, 4), SplitScreen.ViewOf(1, 4), SplitScreen.ViewOf(2, 4), SplitScreen.ViewOf(3, 4) };
            Check("quattro giocatori: quattro quadranti (alto sinistra, alto destra, basso sinistra, basso destra)",
                q[0].xMin == 0f && q[0].yMin > 0f && q[1].xMin > 0f && q[1].yMin == q[0].yMin && q[2].xMin == 0f && q[2].yMin == 0f && q[3].xMin == q[1].xMin && q[3].yMin == 0f && q[0].width == q[2].width && q[0].height == q[2].height);
        }

        /// <summary>Piu' monitor (0.8.0): chi sta su quale monitor e in quale porzione. Con un monitor solo tutto come prima.</summary>
        void Monitors()
        {
            bool Eq(Rect a, Rect b) => Mathf.Abs(a.x - b.x) < 1e-5f && Mathf.Abs(a.y - b.y) < 1e-5f && Mathf.Abs(a.width - b.width) < 1e-5f && Mathf.Abs(a.height - b.height) < 1e-5f;
            bool same = true;
            for (int n = 2; n <= 4; n++) for (int i = 0; i < n; i++) same &= Eq(SplitScreen.ViewOn(i, n, 1), SplitScreen.ViewOf(i, n)) && SplitScreen.DisplayOf(i, n, 1) == 0;
            Check("un monitor solo: ogni giocatore sta dove stava (ViewOn = ViewOf, tutti sul monitor 0)", same);

            // per ogni numero di giocatori e di monitor: ognuno sta su un monitor usato, le porzioni di ogni monitor lo coprono tutto senza sovrapporsi
            bool all = true, ordered = true, even = true, tile = true; string detail = "";
            for (int n = 2; n <= 4; n++)
                for (int m = 1; m <= 4; m++)
                {
                    int used = SplitScreen.MonitorsFor(n, m), total = 0;
                    var area = new float[used]; var onDisplay = new int[used];
                    for (int i = 0; i < n; i++)
                    {
                        int d = SplitScreen.DisplayOf(i, n, m);
                        var v = SplitScreen.ViewOn(i, n, m);
                        all &= d >= 0 && d < used && v.width > 0f && v.height > 0f && v.xMin >= 0f && v.yMin >= 0f && v.xMax <= 1f && v.yMax <= 1f;
                        if (i > 0) ordered &= d >= SplitScreen.DisplayOf(i - 1, n, m);
                        if (d < used) { area[d] += v.width * v.height; onDisplay[d]++; }
                        for (int j = 0; j < i; j++)
                            if (SplitScreen.DisplayOf(j, n, m) == d)
                            {
                                var u = SplitScreen.ViewOn(j, n, m);
                                tile &= v.xMax <= u.xMin + 1e-5f || u.xMax <= v.xMin + 1e-5f || v.yMax <= u.yMin + 1e-5f || u.yMax <= v.yMin + 1e-5f;
                            }
                    }
                    for (int d = 0; d < used; d++)
                    {
                        total += onDisplay[d];
                        even &= onDisplay[d] == SplitScreen.PlayersOn(d, n, m) && onDisplay[d] >= 1 && Mathf.Abs(area[d] - 1f) < 1e-4f && (d == 0 || onDisplay[d] <= onDisplay[d - 1]) && onDisplay[d] >= onDisplay[0] - 1;
                    }
                    if (total != n || SplitScreen.PlayersOn(used, n, m) != 0) { even = false; detail += $" [{n} giocatori su {m}: {total}]"; }
                }
            Check("2-4 giocatori su 1-4 monitor: ognuno su un monitor usato, in ordine, e le porzioni di ogni monitor lo coprono tutto" + detail, all && ordered && tile && even);

            Check("due giocatori, due monitor: uno per monitor, a schermo intero", SplitScreen.DisplayOf(0, 2, 2) == 0 && SplitScreen.DisplayOf(1, 2, 2) == 1
                && Eq(SplitScreen.ViewOn(0, 2, 2), new Rect(0, 0, 1, 1)) && Eq(SplitScreen.ViewOn(1, 2, 2), new Rect(0, 0, 1, 1)));
            Check("tre giocatori, due monitor: due sul primo (meta' e meta') e il terzo da solo sul secondo", SplitScreen.PlayersOn(0, 3, 2) == 2 && SplitScreen.PlayersOn(1, 3, 2) == 1
                && SplitScreen.DisplayOf(2, 3, 2) == 1 && Eq(SplitScreen.ViewOn(0, 3, 2), new Rect(0, 0, 0.5f, 1)) && Eq(SplitScreen.ViewOn(1, 3, 2), new Rect(0.5f, 0, 0.5f, 1)) && Eq(SplitScreen.ViewOn(2, 3, 2), new Rect(0, 0, 1, 1)));
            Check("quattro giocatori, due monitor: due e due, meta' e meta' su ciascuno", SplitScreen.PlayersOn(0, 4, 2) == 2 && SplitScreen.PlayersOn(1, 4, 2) == 2
                && SplitScreen.DisplayOf(1, 4, 2) == 0 && SplitScreen.DisplayOf(2, 4, 2) == 1 && Eq(SplitScreen.ViewOn(2, 4, 2), new Rect(0, 0, 0.5f, 1)) && Eq(SplitScreen.ViewOn(3, 4, 2), new Rect(0.5f, 0, 0.5f, 1)));
            Check("il giocatore 1 sta sempre sul monitor 0 (quello con tastiera e mouse)", SplitScreen.DisplayOf(0, 2, 2) == 0 && SplitScreen.DisplayOf(0, 3, 2) == 0 && SplitScreen.DisplayOf(0, 4, 4) == 0);
            Check("i monitor in piu' dei giocatori non servono: 2 giocatori su 4 monitor ne usano 2, 4 su 9 ne usano 4, da 0 se ne usa 1",
                SplitScreen.MonitorsFor(2, 4) == 2 && SplitScreen.MonitorsFor(4, 9) == 4 && SplitScreen.MonitorsFor(3, 0) == 1 && SplitScreen.MonitorsFor(4, 3) == 3);
            Check("il testo del menu dice come si dividono: 2 giocatori 1+1, 3 giocatori 2+1, 4 giocatori 2+2 su 2 monitor",
                SplitScreen.Arrangement(2).Contains("2 giocatori 1+1") && SplitScreen.Arrangement(2).Contains("3 giocatori 2+1") && SplitScreen.Arrangement(2).Contains("4 giocatori 2+2"));
        }

        void Pads()
        {
            bool Same(int[] a, params int[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
            Check("nessun controller, tre giocatori: il 1 tastiera, il 2 e il 3 aspettano gli slot 0 e 1", Same(SplitScreen.AssignPads(0, 3, false), -1, 0, 1));
            Check("un controller nello slot 1, due giocatori: il giocatore 2 prende lo slot 1", Same(SplitScreen.AssignPads(0b0010, 2, false), -1, 1));
            Check("controller negli slot 0 e 2, tre giocatori: 2 e 3 prendono 0 e 2", Same(SplitScreen.AssignPads(0b0101, 3, false), -1, 0, 2));
            Check("due controller, due giocatori, tutti col controller: 0 e 1", Same(SplitScreen.AssignPads(0b0011, 2, true), 0, 1));
            Check("senza controller e tutti col controller: slot 0 e 1", Same(SplitScreen.AssignPads(0, 2, true), 0, 1));
            Check("quattro giocatori, un controller nello slot 3: 2 prende il 3, gli altri i primi liberi", Same(SplitScreen.AssignPads(0b1000, 4, false), -1, 3, 0, 1));
        }

        void Fov()
        {
            Check("campo visivo: nelle finestre strette e nei quadranti resta quello scelto", Mathf.Approximately(SplitScreen.FovFor(75f, 0.9f), 75f) && Mathf.Approximately(SplitScreen.FovFor(75f, 2.39f), 75f));
            float v = SplitScreen.FovFor(75f, 4.8f);
            float h = 2f * Mathf.Atan(Mathf.Tan(v * 0.5f * Mathf.Deg2Rad) * 4.8f) * Mathf.Rad2Deg;
            Check($"campo visivo: in una finestra larga 4,8 volte si accorcia ({v:F1} gradi verticali, {h:F1} orizzontali, tetto {SplitScreen.MaxHorizontalFov})",
                v < 60f && Mathf.Abs(h - SplitScreen.MaxHorizontalFov) < 0.5f);
        }

        void Mapping()
        {
            var m = new PadControls.Mapper();
            const float eps = 0.01f;
            m.Step(Pad(ly: 5000));
            Check("stick sinistro sotto la zona morta: fermo", m.Move == Vector2.zero);
            m.Step(Pad(ly: 32767));
            Check("stick sinistro tutto avanti: (0, 1)", Mathf.Abs(m.Move.y - 1f) < eps && Mathf.Abs(m.Move.x) < eps);
            m.Step(Pad(lx: 32767, ly: 32767));
            Check("in diagonale il movimento non supera 1", m.Move.magnitude <= 1.001f && m.Move.x > 0.6f && m.Move.y > 0.6f);
            m.Step(Pad(rx: 32767));
            Check($"stick destro tutto a destra: {PadControls.Mapper.YawRate} rad/s, niente rotazione verticale", Mathf.Abs(m.LookRate.x - PadControls.Mapper.YawRate) < 0.05f && Mathf.Abs(m.LookRate.y) < eps);
            m.Step(Pad(rx: 16000));
            Check("a meta' stick la rotazione e' meno della meta' (mira fine)", m.LookRate.x > 0f && m.LookRate.x < PadControls.Mapper.YawRate * 0.5f);
            m.Step(Pad(ry: 32767));
            Check("stick destro in alto: guarda in alto", m.LookRate.y > 1f);

            m.Reset();
            m.Step(Pad(PadControls.A));
            Check("A: salto al primo giro", IsDown(m, TouchControls.Action.Jump) && IsHeld(m, TouchControls.Action.Jump));
            m.Step(Pad(PadControls.A));
            Check("A tenuto: il salto non si ripete ma resta premuto", !IsDown(m, TouchControls.Action.Jump) && IsHeld(m, TouchControls.Action.Jump));
            m.Step(Pad());
            Check("A rilasciato", !IsHeld(m, TouchControls.Action.Jump));

            // un tasto, una cosa (mappa del 2026-11-16): al primo giro
            var press = new (ushort button, TouchControls.Action action, string name)[]
            {
                (PadControls.RightShoulder, TouchControls.Action.Grenade, "RB acquasanta"), (PadControls.RightStick, TouchControls.Action.Knife, "R3 coltello"),
                (PadControls.LeftShoulder, TouchControls.Action.Spell, "LB mano sinistra (magia)"), (PadControls.StartBtn, TouchControls.Action.Pause, "Start pausa"),
            };
            foreach (var c in press)
            {
                m.Reset(); m.Step(Pad(c.button));
                Check(c.name, IsDown(m, c.action) || IsHeld(m, c.action));
            }
            m.Reset(); m.Step(Pad(PadControls.LeftShoulder)); m.Step(Pad(PadControls.LeftShoulder));
            Check("LB tenuto resta tenuto (MagicCaster ne fa il sigillo)", IsHeld(m, TouchControls.Action.Spell) && !IsDown(m, TouchControls.Action.Spell));

            // tocco (al rilascio) e tenuto (dopo HoldTime, una volta sola, e niente tocco al rilascio)
            var twoWay = new (ushort button, TouchControls.Action tap, TouchControls.Action hold, string name)[]
            {
                (PadControls.X, TouchControls.Action.Reload, TouchControls.Action.Use, "X: ricarica, tenuto usa e compra"),
                (PadControls.Y, TouchControls.Action.Swap, TouchControls.Action.Mode, "Y: cambia arma, tenuto modo di fuoco"),
                (PadControls.DpadUp, TouchControls.Action.Torch, TouchControls.Action.Battery, "croce su: torcia, tenuta pila"),
                (PadControls.DpadDown, TouchControls.Action.Lure, TouchControls.Action.Mine, "croce giu': carillon, tenuta mina"),
                (PadControls.DpadLeft, TouchControls.Action.Ping, TouchControls.Action.Velo, "croce sinistra: porte, tenuta velo"),
                (PadControls.DpadRight, TouchControls.Action.SpellNext, TouchControls.Action.InfusionNext, "croce destra: magia seguente, tenuta sigillo seguente"),
                (PadControls.BackBtn, TouchControls.Action.Map, TouchControls.Action.Scoreboard, "Back: mappa, tenuto tabellone"),
            };
            int holdSteps = Mathf.CeilToInt(PadControls.Mapper.HoldTime * 60f) + 1;
            foreach (var c in twoWay)
            {
                m.Reset(); m.Step(Pad(c.button));
                bool none = !IsDown(m, c.tap) && !IsDown(m, c.hold);
                m.Step(Pad());
                Check(c.name + ": toccato fa la prima al rilascio", none && IsDown(m, c.tap) && !IsDown(m, c.hold));
                m.Reset();
                bool holdOnce = false, tapAfter = false; int holdCount = 0;
                for (int k = 0; k < holdSteps + 3; k++) { m.Step(Pad(c.button)); if (IsDown(m, c.hold)) holdCount++; }
                holdOnce = holdCount == 1;
                m.Step(Pad());
                tapAfter = IsDown(m, c.tap);
                Check(c.name + ": tenuto fa la seconda una volta, e lasciandolo non fa la prima", holdOnce && !tapAfter);
            }
            m.Reset();
            for (int k = 0; k < holdSteps + 1; k++) m.Step(Pad(PadControls.BackBtn));
            Check("Back tenuto: il tabellone resta finche' lo si tiene", IsHeld(m, TouchControls.Action.Scoreboard));

            // B a interruttore, A e la corsa rialzano; con la mira L3 e R3 sporgono, senza mira R3 e' il coltello
            m.Reset(); m.Step(Pad(PadControls.B)); m.Step(Pad());
            Check("B toccato: abbassati e resta abbassato", IsHeld(m, TouchControls.Action.Crouch));
            m.Step(Pad(PadControls.B)); m.Step(Pad());
            Check("B di nuovo: in piedi", !IsHeld(m, TouchControls.Action.Crouch));
            m.Step(Pad(PadControls.B)); m.Step(Pad(PadControls.A));
            Check("A da abbassato: salta e si rialza", IsDown(m, TouchControls.Action.Jump) && !IsHeld(m, TouchControls.Action.Crouch));
            m.Reset(); m.Step(Pad(lt: 255)); m.Step(Pad(PadControls.LeftStick, lt: 255));
            Check("con la mira L3: sporgersi a sinistra (e non correre)", IsHeld(m, TouchControls.Action.Left) && !IsHeld(m, TouchControls.Action.Sprint));
            m.Step(Pad(lt: 255)); m.Step(Pad(PadControls.RightStick, lt: 255));
            Check("con la mira R3: sporgersi a destra (e non il coltello)", IsHeld(m, TouchControls.Action.Right) && !IsHeld(m, TouchControls.Action.Left) && !IsDown(m, TouchControls.Action.Knife));
            m.Step(Pad());
            Check("lasciando la mira si torna dritti", !IsHeld(m, TouchControls.Action.Right) && !IsHeld(m, TouchControls.Action.Left));

            m.Reset();
            m.Step(Pad(rt: 100));
            Check("grilletto destro a 39%: non spara", !IsHeld(m, TouchControls.Action.Fire));
            m.Step(Pad(rt: 200));
            Check("grilletto destro a 78%: spara (un solo colpo in piu')", IsHeld(m, TouchControls.Action.Fire) && IsDown(m, TouchControls.Action.Fire));
            m.Step(Pad(rt: 100));
            Check("scendendo a 39% resta premuto (isteresi), senza nuovo colpo", IsHeld(m, TouchControls.Action.Fire) && !IsDown(m, TouchControls.Action.Fire));
            m.Step(Pad(rt: 60));
            Check("sotto il 30% e' rilasciato", !IsHeld(m, TouchControls.Action.Fire));
            m.Step(Pad(lt: 200));
            Check("grilletto sinistro: mira", IsHeld(m, TouchControls.Action.Aim));

            m.Reset();
            m.Step(Pad(PadControls.LeftStick, ly: 32767));
            Check("L3 con lo stick avanti: corsa", IsHeld(m, TouchControls.Action.Sprint));
            m.Step(Pad(PadControls.LeftStick, ly: 32767)); m.Step(Pad(ly: 32767));
            Check("la corsa resta anche tenendo e rilasciando L3", IsHeld(m, TouchControls.Action.Sprint));
            m.Step(Pad(PadControls.LeftStick, ly: 32767)); m.Step(Pad(ly: 32767));
            Check("un altro L3 la spegne", !IsHeld(m, TouchControls.Action.Sprint));
            m.Step(Pad(PadControls.LeftStick, ly: 32767)); m.Step(Pad());
            Check("fermandosi la corsa si annulla", !IsHeld(m, TouchControls.Action.Sprint));
            m.Step(Pad(PadControls.LeftStick, ly: 32767)); m.Step(Pad(ly: 32767, rt: 255));
            Check("sparando la corsa si annulla", !IsHeld(m, TouchControls.Action.Sprint));

            m.Step(Pad(PadControls.A | PadControls.RightShoulder, ly: 32767, rt: 255));
            m.Step(default);
            Check("controller scollegato: tutto a zero", m.Move == Vector2.zero && m.LookRate == Vector2.zero && !IsHeld(m, TouchControls.Action.Fire) && !IsDown(m, TouchControls.Action.Jump));
        }

        void Api()
        {
            int mask = -1;
            try { mask = PadControls.ConnectedMask(); } catch (Exception e) { Debug.LogException(e); }
            Check($"XInput risponde senza errori (slot collegati: maschera {mask})", mask >= 0 && mask <= 15);
        }

        // ------------------------------------------------------------------ il giocatore vero

        IEnumerator Run()
        {
            Layouts(); Monitors(); Pads(); Fov(); Mapping(); Api();

            float deadline = Time.realtimeSinceStartup + 90;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("c'e' spazio per la prova", MeleeTest.FindArena(out Vector3 arena, out Vector3 forward));
            if (failed > 0) yield break;
            player.invulnerable = true;
            float yaw = Mathf.Atan2(forward.x, forward.z);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            PadControls.Enable(0);                 // l'istanza ascolta lo slot 0; la prova gli da' lo stato

            void Back() { player.Teleport(arena - forward * 2.8f + Vector3.up * 0.05f); player.yaw = yaw; player.pitch = 0f; }

            // stick sinistro: cammina avanti
            Back();
            yield return null; yield return null;
            Vector3 p0 = player.transform.position;
            PadControls.Inject(Pad(ly: 32767));
            yield return new WaitForSeconds(1.0f);
            Vector3 d = player.transform.position - p0;
            float along = Vector3.Dot(d, forward), side = Mathf.Abs(Vector3.Dot(d, right));
            Check($"stick sinistro in avanti: il giocatore avanza ({along:F1} m in 1 s, {side:F1} m di lato)", along > 2f && side < 0.6f);
            PadControls.Inject(Pad());
            yield return new WaitForSeconds(0.5f);

            // stick destro: la visuale gira
            float y0 = player.yaw, expected;
            PadControls.Inject(Pad(rx: 32767));
            yield return new WaitForSeconds(0.5f);
            PadControls.Inject(Pad());
            float dyaw = player.yaw - y0;
            expected = PadControls.Mapper.YawRate * GameSettings.Sensitivity * player.weapon.SensMult * 0.5f;
            Check($"stick destro a destra: la visuale gira a destra (+{dyaw:F2} rad, attesi circa {expected:F2})", dyaw > expected * 0.7f && dyaw < expected * 1.3f);
            float pitch0 = player.pitch;
            PadControls.Inject(Pad(ry: 32767));
            yield return new WaitForSeconds(0.3f);
            PadControls.Inject(Pad());
            Check($"stick destro in alto: la visuale sale (inclinazione {pitch0:F2} -> {player.pitch:F2})", player.pitch < pitch0 - 0.2f);
            player.yaw = yaw; player.pitch = 0f;

            // grilletto destro: spara
            int shots = GameManager.shotsFired;
            Check("l'arma ha colpi", player.weapon.mag > 0);
            PadControls.Inject(Pad(rt: 255));
            yield return new WaitForSeconds(0.6f);
            PadControls.Inject(Pad());
            Check($"grilletto destro: il giocatore spara ({GameManager.shotsFired - shots} colpi)", GameManager.shotsFired > shots);

            // A: salta
            Back();
            yield return new WaitForSeconds(0.4f);
            float yBase = player.transform.position.y, yMax = yBase;
            PadControls.Inject(Pad(PadControls.A));
            yield return null; yield return null;
            PadControls.Inject(Pad());
            for (float t = 0f; t < 0.8f; t += Time.deltaTime) { yMax = Mathf.Max(yMax, player.transform.position.y); yield return null; }
            Check($"A: il giocatore salta (+{yMax - yBase:F2} m)", yMax - yBase > 0.25f);

            // B: abbassati a interruttore (un tocco abbassa e si resta giu', il secondo rialza)
            yield return new WaitForSeconds(0.6f);
            PadControls.Inject(Pad(PadControls.B));
            yield return null; yield return null;
            PadControls.Inject(Pad());
            yield return new WaitForSeconds(0.8f);
            bool crouched = player.IsCrouching;
            PadControls.Inject(Pad(PadControls.B));
            yield return null; yield return null;
            PadControls.Inject(Pad());
            yield return new WaitForSeconds(0.8f);
            Check("B toccato: abbassato e resta giu'; toccato di nuovo: in piedi", crouched && !player.IsCrouching);

            // L3: corsa
            Back();
            yield return new WaitForSeconds(0.3f);
            PadControls.Inject(Pad(PadControls.LeftStick, ly: 32767));
            yield return null; yield return null;
            PadControls.Inject(Pad(ly: 32767));
            yield return new WaitForSeconds(0.7f);
            bool sprinting = player.IsSprinting;
            PadControls.Inject(Pad());
            yield return new WaitForSeconds(0.6f);
            Check($"L3 con lo stick avanti: corre (velocita' {player.SpeedH:F1} m/s dopo)", sprinting && !player.IsSprinting);

            PadControls.Inject(null);
            player.invulnerable = false;
        }
    }
}
#endif
