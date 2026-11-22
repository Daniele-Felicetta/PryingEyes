using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Controller (XInput: Xbox 360/One/Series e compatibili, anche via Steam Input o DS4Windows): giocatore singolo, cooperativa in
    /// rete e schermo condiviso. Si legge direttamente da XInput e non dall'Input Manager di Unity: cosi' ogni giocatore legge il SUO
    /// controller (slot 0..3), anche quando i controller sono quattro nello stesso gioco (uno per posto: Channel), mentre tastiera e mouse
    /// arrivano solo al giocatore 1. I comandi escono nella forma di quelli del touch (TouchControls.Held/Down/Move): il resto del gioco non
    /// sa da dove arrivano.
    ///
    /// Come si attiva: nello schermo condiviso gli slot li assegna Seats.Begin; altrove il gioco lo cerca da solo (EnableAuto): guarda i
    /// controller collegati e adotta il primo su cui si preme un tasto o si muove uno stick, cosi' un controller dimenticato nella porta USB
    /// (o con lo stick che non torna al centro) non muove nessuno. Quando un menu e' aperto il controller non da' comandi di gioco ma
    /// naviga (PadMenuInput): croce o stick sinistro muovono la selezione, A conferma, B torna indietro, Start apre e chiude la pausa.
    ///
    /// Mappa (rivista il 2026-11-16 per l'uso col controller, sullo schema dei giochi a ondate: niente combinazioni di due tasti; dove un
    /// tasto fa due cose il tocco parte al rilascio e il tenuto dopo Mapper.HoldTime):
    ///   stick sinistro muove, L3 corsa (a scatti: si annulla fermandosi, mirando o sparando); stick destro guarda, R3 coltello (G)
    ///   RT fuoco, LT mira; con la mira L3 e R3 sporgono a sinistra e a destra (Q, E: un tocco sporge, il secondo o lasciare la mira raddrizza)
    ///   A salto; B abbassati (a interruttore, come C tenuto: il pollice destro resta sullo stick; saltare o correre rialzano)
    ///   X ricarica (R), tenuto usa e compra (F); Y cambia arma, tenuto modo di fuoco (B)
    ///   LB mano sinistra: tocco la magia, tenuto il sigillo sull'arma (3); RB acquasanta (Z)
    ///   croce: su torcia (T), tenuto cambio pila (Y); giu' carillon (4), tenuto mina (5); sinistra sigillo seguente (7), tenuto velo (H);
    ///   destra magia seguente (6), tenuto indica le porte (V) (fino al 2026-11-16 era al contrario: sigillo tenuto a destra, porte a sinistra)
    ///   Back mappa (M), tenuto tabellone (Tab); Start pausa.</summary>
    [DefaultExecutionOrder(-10001)]
    public sealed class PadControls : MonoBehaviour
    {
        // bit di XINPUT_GAMEPAD.wButtons
        public const ushort DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008, StartBtn = 0x0010, BackBtn = 0x0020,
            LeftStick = 0x0040, RightStick = 0x0080, LeftShoulder = 0x0100, RightShoulder = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

        /// <summary>Lo stato di un controller come lo da' XInput (stick da -32768 a 32767, grilletti da 0 a 255).</summary>
        public struct Raw
        {
            public bool connected;
            public ushort buttons;
            public byte leftTrigger, rightTrigger;
            public short lx, ly, rx, ry;
        }

        /// <summary>Traduce lo stato grezzo in comandi: zone morte, grilletti con isteresi, secondo livello con LB, corsa a scatti, e per i menu
        /// direzione di navigazione, conferma e indietro. Non conosce ne' Unity (a parte i vettori) ne' XInput: la prova lo alimenta con stati inventati.</summary>
        public sealed class Mapper
        {
            public const float LeftDead = 7849f / 32767f, RightDead = 8689f / 32767f;      // i valori consigliati da XInput
            public const float TriggerOn = 0.5f, TriggerOff = 0.3f;
            /// <summary>Velocita' di rotazione a stick pieno (rad/s, a sensibilita' 1): circa 183 gradi al secondo in orizzontale.</summary>
            public const float YawRate = 3.2f, PitchRate = 2.2f;
            /// <summary>Lo stick sinistro naviga i menu oltre il 60% della corsa e smette sotto il 40% (isteresi: niente tremolio sulla soglia).</summary>
            public const float NavOn = 0.6f, NavOff = 0.4f;

            /// <summary>Un tasto che fa due cose: sotto questo tempo (s) e' un tocco (parte al rilascio), oltre e' tenuto (parte una volta).</summary>
            public const float HoldTime = 0.30f;
            public readonly bool[] Held = new bool[TouchControls.Slots];
            public readonly bool[] Down = new bool[TouchControls.Slots];
            public Vector2 Move, LookRate;
            /// <summary>Menu: direzione tenuta, croce o stick sinistro, un asse alla volta (+1 destra o su, -1 sinistra o giu').</summary>
            public int NavX, NavY;
            /// <summary>Menu: fronte di salita di A (conferma) e di B (indietro).</summary>
            public bool Confirm, Cancel;
            /// <summary>Menu a schede (il menu di Select nel deserto): fronte di salita di LB (scheda prima), di RB (scheda dopo) e di Select (chiude). Come A e B non li cancella SuppressGameplay.</summary>
            public bool Prev, Next, Back;
            public bool Sprinting => sprint;
            public bool Crouching => crouch;
            ushort prev, holdDone;
            bool rtOn, ltOn, sprint, crouch, leanL, leanR;
            bool sprint0, crouch0, leanL0, leanR0;          // gli interruttori all'inizio del giro (un menu aperto non li cambia)
            int stickX, stickY;
            float clock;
            readonly float[] pressAt = new float[16];

            public void Reset()
            {
                Array.Clear(Held, 0, Held.Length); Array.Clear(Down, 0, Down.Length);
                Move = LookRate = Vector2.zero;
                NavX = NavY = 0; Confirm = Cancel = Prev = Next = Back = false;
                prev = holdDone = 0; rtOn = ltOn = sprint = crouch = leanL = leanR = false; stickX = stickY = 0;
                clock = 0f; Array.Clear(pressAt, 0, pressAt.Length);
            }

            /// <summary>Un menu e' aperto: i comandi di gioco non devono uscire (A su "Gioca" non fa saltare, X non ricarica, lo stick non
            /// muove). Start resta, perche' il menu lo usa per tornare indietro e per riprendere; la navigazione non si tocca.</summary>
            public void SuppressGameplay()
            {
                bool pause = Down[(int)TouchControls.Action.Pause];
                Array.Clear(Held, 0, Held.Length); Array.Clear(Down, 0, Down.Length);
                Move = LookRate = Vector2.zero;
                Down[(int)TouchControls.Action.Pause] = pause;
                // B che torna indietro nel menu non abbassa, L3 non accende la corsa
                sprint = sprint0; crouch = crouch0; leanL = leanL0; leanR = leanR0;
            }

            /// <summary>Il gioco non ha il fuoco: nessun comando, ma lo stato dei tasti resta aggiornato (tornando non nasce un fronte finto).</summary>
            public void Silence()
            {
                Array.Clear(Held, 0, Held.Length); Array.Clear(Down, 0, Down.Length);
                Move = LookRate = Vector2.zero; NavX = NavY = 0; Confirm = Cancel = Prev = Next = Back = false;
            }

            static Vector2 Stick(short x, short y, float dead)
            {
                var v = new Vector2(Mathf.Max(-1f, x / 32767f), Mathf.Max(-1f, y / 32767f));
                float m = v.magnitude;
                if (m < dead) return Vector2.zero;
                float k = Mathf.Min(1f, (m - dead) / (1f - dead));
                return v / m * k;
            }

            /// <summary>Quanto della rotazione piena si da' a quella inclinazione dello stick: piano vicino al centro (mira fine), pieno in fondo.</summary>
            public static float Curve(float k) => k * (0.45f + 0.55f * k);

            /// <summary>Direzione di navigazione di un asse dello stick con isteresi: cur e' quella del giro prima (0, +1, -1).</summary>
            static int Hold(float v, int cur)
            {
                float a = Mathf.Abs(v);
                int sign = v > 0f ? 1 : -1;
                if (a > NavOn) return sign;
                if (cur != 0 && sign == cur && a > NavOff) return cur;
                return 0;
            }

            void Set(TouchControls.Action a, bool held, bool pressed)
            {
                if (held) Held[(int)a] = true;
                if (pressed) Down[(int)a] = true;
            }

            static int Bit(ushort mask)
            {
                int i = 0;
                while (i < 15 && (mask & (1 << i)) == 0) i++;
                return i;
            }

            /// <summary>Un tasto, due azioni: toccato (rilasciato prima di HoldTime) fa 'tap' al rilascio, tenuto fa 'hold' una volta appena
            /// passa HoldTime (e con holdHeld resta tenuto finche' lo si tiene: il tabellone).</summary>
            void TapHold(ushort b, ushort mask, TouchControls.Action tap, TouchControls.Action hold, bool holdHeld = false)
            {
                int i = Bit(mask);
                bool now = (b & mask) != 0, was = (prev & mask) != 0;
                if (now && !was) { pressAt[i] = clock; holdDone = (ushort)(holdDone & ~mask); }
                if (now && (holdDone & mask) == 0 && clock - pressAt[i] >= HoldTime) { holdDone = (ushort)(holdDone | mask); Down[(int)hold] = true; }
                if (now && holdHeld && (holdDone & mask) != 0) Held[(int)hold] = true;
                if (!now && was && (holdDone & mask) == 0) Down[(int)tap] = true;
            }

            /// <summary>dt: il tempo dal giro prima (s), per distinguere i tocchi dai tasti tenuti.</summary>
            public void Step(Raw s, float dt = 1f / 60f)
            {
                clock += Mathf.Max(0f, dt);
                sprint0 = sprint; crouch0 = crouch; leanL0 = leanL; leanR0 = leanR;
                Array.Clear(Down, 0, Down.Length);
                Array.Clear(Held, 0, Held.Length);
                NavX = NavY = 0; Confirm = Cancel = Prev = Next = Back = false;
                if (!s.connected) { Move = LookRate = Vector2.zero; prev = holdDone = 0; rtOn = ltOn = sprint = crouch = leanL = leanR = false; stickX = stickY = 0; return; }

                ushort b = s.buttons;
                bool Is(ushort m) => (b & m) != 0;
                bool Pressed(ushort m) => (b & m) != 0 && (prev & m) == 0;

                float rt = s.rightTrigger / 255f, lt = s.leftTrigger / 255f;
                bool fireWas = rtOn, aimWas = ltOn;
                rtOn = rtOn ? rt > TriggerOff : rt > TriggerOn;
                ltOn = ltOn ? lt > TriggerOff : lt > TriggerOn;
                Set(TouchControls.Action.Fire, rtOn, rtOn && !fireWas);
                Set(TouchControls.Action.Aim, ltOn, false);

                Move = Stick(s.lx, s.ly, LeftDead);
                var look = Stick(s.rx, s.ry, RightDead);
                float lm = look.magnitude;
                LookRate = lm > 0f ? new Vector2(look.x / lm * Curve(lm) * YawRate, look.y / lm * Curve(lm) * PitchRate) : Vector2.zero;

                // menu: croce e stick sinistro muovono la selezione (un asse alla volta), A conferma, B torna indietro
                float fx = Mathf.Max(-1f, s.lx / 32767f), fy = Mathf.Max(-1f, s.ly / 32767f);
                stickX = Hold(fx, stickX); stickY = Hold(fy, stickY);
                int dx = (Is(DpadRight) ? 1 : 0) - (Is(DpadLeft) ? 1 : 0), dy = (Is(DpadUp) ? 1 : 0) - (Is(DpadDown) ? 1 : 0);
                int nx = dx != 0 ? dx : stickX, ny = dy != 0 ? dy : stickY;
                if (nx != 0 && ny != 0)
                {
                    if (dx != 0 && dy != 0) nx = 0;                          // due tasti della croce insieme: vince l'asse verticale
                    else if (dx != 0) ny = 0;                                // la croce vince sullo stick
                    else if (dy != 0) nx = 0;
                    else if (Mathf.Abs(fx) > Mathf.Abs(fy)) ny = 0; else nx = 0;
                }
                NavX = nx; NavY = ny;
                Confirm = Pressed(A); Cancel = Pressed(B);
                Prev = Pressed(LeftShoulder); Next = Pressed(RightShoulder); Back = Pressed(BackBtn);

                // un tasto, una cosa (e dove serve, tenuto, la seconda): niente combinazioni di due tasti in combattimento
                Set(TouchControls.Action.Pause, false, Pressed(StartBtn));
                Set(TouchControls.Action.Jump, Is(A), Pressed(A));
                Set(TouchControls.Action.Spell, Is(LeftShoulder), Pressed(LeftShoulder));          // la mano sinistra: MagicCaster distingue tocco e tenuto
                Set(TouchControls.Action.Grenade, false, Pressed(RightShoulder));
                Set(TouchControls.Action.Knife, false, !ltOn && Pressed(RightStick));
                TapHold(b, X, TouchControls.Action.Reload, TouchControls.Action.Use, true);       // Usa resta tenuto: rialzare un compagno vuole 3 s
                TapHold(b, Y, TouchControls.Action.Swap, TouchControls.Action.Mode);
                TapHold(b, DpadUp, TouchControls.Action.Torch, TouchControls.Action.Battery);
                TapHold(b, DpadDown, TouchControls.Action.Lure, TouchControls.Action.Mine);
                TapHold(b, DpadLeft, TouchControls.Action.InfusionNext, TouchControls.Action.Velo);       // il sigillo e' a sinistra come la sua icona nell'HUD
                TapHold(b, DpadRight, TouchControls.Action.SpellNext, TouchControls.Action.Ping);
                TapHold(b, BackBtn, TouchControls.Action.Map, TouchControls.Action.Scoreboard, true);

                // abbassati a interruttore: tenere B col pollice destro vorrebbe dire non guardare; saltare rialza
                if (Pressed(B)) crouch = !crouch;
                if (Pressed(A)) crouch = false;
                // con la mira L3 e R3 sporgono (un tocco sporge, il secondo raddrizza; lasciando la mira si torna dritti)
                if (ltOn)
                {
                    if (Pressed(LeftStick)) { leanL = !leanL; leanR = false; }
                    if (Pressed(RightStick)) { leanR = !leanR; leanL = false; }
                }
                else leanL = leanR = false;
                Set(TouchControls.Action.Left, leanL, false);
                Set(TouchControls.Action.Right, leanR, false);

                // corsa: L3 (senza mira) la accende e la spegne; si spegne da sola se ci si ferma o si prende la mira o il grilletto;
                // correre rialza
                if (!ltOn && Pressed(LeftStick)) { sprint = !sprint; if (sprint) crouch = false; }
                if (Move.sqrMagnitude < 0.25f * 0.25f || (ltOn && !aimWas) || (rtOn && !fireWas)) sprint = false;
                Set(TouchControls.Action.Sprint, sprint, false);
                Set(TouchControls.Action.Crouch, crouch, Pressed(B));

                prev = b;
            }
        }

        /// <summary>Un controller e tutto quello che il gioco sa di lui: lo slot XInput, il traduttore dei comandi, se e' stato adottato.
        /// Il giocatore singolo ne ha uno (Primary); nello split screen ogni posto (Seat) ha il suo e Seats.Enter mette al suo posto
        /// quello di chi gira (Use): il resto del gioco legge sempre PadControls.Held/Down/Move e compagni.</summary>
        public sealed class Channel
        {
            public readonly Mapper mapper = new Mapper();
            /// <summary>Lo slot XInput (0..3), -1 se non usa nessun controller (in modo automatico: finche' non ne ha adottato uno).</summary>
            public int index = -1;
            public bool auto, preferred, connected;
            public Raw? injected;
            internal float retryAt, scanAt, lostAt = -1f, lastActivity = -100f;
            internal int scanMask;
            internal bool wasConnected;
        }

        static readonly Channel primary = new Channel();
        static readonly System.Collections.Generic.List<Channel> extra = new System.Collections.Generic.List<Channel>();
        static Channel ch = primary;
        static PadControls driver;

        /// <summary>Il controller del giocatore singolo (e del primo posto dello split screen).</summary>
        public static Channel Primary => primary;
        /// <summary>Quello di chi gira adesso.</summary>
        public static Channel Mine => ch;
        public static void Use(Channel c) { ch = c ?? primary; }
        /// <summary>Un controller in piu' per un posto dello split screen (si ascolta da solo: lo aggiorna il driver, un fotogramma prima del gioco).</summary>
        public static Channel NewChannel() { var c = new Channel(); extra.Add(c); EnsureDriver(); return c; }
        public static void ClearChannels() { extra.Clear(); ch = primary; }
        /// <summary>Il posto ascolta quello slot (0..3), o nessuno (-1: resta in cerca da solo, adotta il primo libero su cui si preme un tasto).</summary>
        public static void Assign(Channel c, int slot)
        {
            if (c == null) return;
            c.mapper.Reset();
            if (slot < 0) { c.index = -1; c.connected = false; c.auto = true; c.preferred = false; }
            else { c.index = Mathf.Clamp(slot, 0, 3); c.auto = false; c.preferred = true; c.wasConnected = true; c.lostAt = -1f; c.retryAt = 0f; }
            EnsureDriver();
        }

        /// <summary>Lo slot XInput di chi gira (0..3), -1 se non usa nessun controller.</summary>
        public static int Index => ch.index;
        public static bool Enabled => ch.index >= 0;
        /// <summary>L'ultimo giro ha trovato il controller collegato (o una prova ne ha messo uno finto).</summary>
        public static bool Connected => ch.connected;
        /// <summary>Si sta giocando col controller: e' stato adottato e l'ultimo comando e' venuto da lui (non dalla tastiera). Decide quali
        /// tasti nominano i suggerimenti a schermo.</summary>
        public static bool Preferred => ch.connected && ch.preferred;
        /// <summary>Il tasto per usare e comprare, nel nome che gli da' chi gioca (F con la tastiera, X tenuto col controller).</summary>
        public static string UseKey => Preferred ? "tieni X" : "F";

        public static Vector2 Move => ch.mapper.Move;
        /// <summary>Rotazione richiesta dallo stick destro, in radianti al secondo a sensibilita' 1 (x a destra, y verso l'alto).</summary>
        public static Vector2 LookRate => ch.mapper.LookRate;
        public static bool Held(TouchControls.Action a) => ch.mapper.Held[(int)a];
        public static bool Down(TouchControls.Action a) => ch.mapper.Down[(int)a];
        /// <summary>Navigazione dei menu (croce o stick sinistro): direzione tenuta, +1 destra o su, -1 sinistra o giu'. Nei menu vale il
        /// controller di chiunque (nello split screen il primo che si muove).</summary>
        public static int NavX { get { int v = primary.mapper.NavX; for (int i = 0; v == 0 && i < extra.Count; i++) v = extra[i].mapper.NavX; return v; } }
        public static int NavY { get { int v = primary.mapper.NavY; for (int i = 0; v == 0 && i < extra.Count; i++) v = extra[i].mapper.NavY; return v; } }
        /// <summary>Menu: A e B appena premuti (di chiunque).</summary>
        public static bool Confirm { get { bool v = primary.mapper.Confirm; for (int i = 0; !v && i < extra.Count; i++) v = extra[i].mapper.Confirm; return v; } }
        public static bool Cancel { get { bool v = primary.mapper.Cancel; for (int i = 0; !v && i < extra.Count; i++) v = extra[i].mapper.Cancel; return v; } }
        /// <summary>Menu: Start premuto da chiunque (apre e chiude la pausa).</summary>
        public static bool PausePressed { get { bool v = primary.mapper.Down[(int)TouchControls.Action.Pause]; for (int i = 0; !v && i < extra.Count; i++) v = extra[i].mapper.Down[(int)TouchControls.Action.Pause]; return v; } }

        static void EnsureDriver()
        {
            if (driver != null) return;
            var go = new GameObject("PadControls");
            DontDestroyOnLoad(go);
            driver = go.AddComponent<PadControls>();
        }

        /// <summary>Da ora il giocatore singolo (o il primo posto) ascolta il controller dello slot dato (0..3); -1 lo spegne.</summary>
        public static void Enable(int slot)
        {
            if (slot < 0) { Disable(); return; }
            primary.index = Mathf.Clamp(slot, 0, 3);
            primary.auto = false; primary.preferred = true;
            EnsureDriver();
        }

        /// <summary>Giocatore singolo e cooperativa in rete: il controller lo cerca il gioco da solo e adotta il primo su cui si preme un
        /// tasto. Non cambia niente se uno slot e' gia' stato scelto (Enable); si ferma con Enable e con Disable.</summary>
        public static void EnableAuto()
        {
            if (primary.index >= 0) return;
            primary.auto = true;
            EnsureDriver();
        }

        public static void Disable()
        {
            primary.index = -1; primary.connected = false; primary.auto = primary.preferred = false;
            primary.mapper.Reset();
        }

        /// <summary>Per le prove: al posto del controller vero il gioco legge questo stato (null = di nuovo quello vero).</summary>
        public static void Inject(Raw? raw) { Inject(primary, raw); }
        public static void Inject(Channel c, Raw? raw)
        {
            if (c == null) return;
            c.injected = raw;
            if (raw.HasValue) EnsureDriver();
        }

        static bool Activity(Raw s) => s.buttons != 0 || s.leftTrigger > 100 || s.rightTrigger > 100 ||
            Mathf.Abs(s.lx) > 18000 || Mathf.Abs(s.ly) > 18000 || Mathf.Abs(s.rx) > 18000 || Mathf.Abs(s.ry) > 18000;

        /// <summary>Il gioco puo' ascoltare il controller: ha il fuoco (un controller non da' ordini a un gioco in secondo piano).</summary>
        static bool Listening =>
#if !PE_DISTRIBUTION_BUILD
            TestPoll != null ||
#endif
            Application.isFocused;

        /// <summary>Lo slot e' gia' di qualcun altro (un altro posto dello split screen): la ricerca automatica non lo adotta.</summary>
        static bool Taken(int slot, Channel except)
        {
            if (primary != except && primary.index == slot) return true;
            for (int i = 0; i < extra.Count; i++) if (extra[i] != except && extra[i].index == slot) return true;
            return false;
        }

        void Update()
        {
            Step(primary);
            for (int i = 0; i < extra.Count; i++) Step(extra[i]);
        }

        void Step(Channel c)
        {
            if (c.injected.HasValue) { Apply(c, c.injected.Value, true); return; }
            if (c.index < 0)
            {
                // nessun controller scelto: in modo automatico si guardano quelli collegati, e solo a gioco in primo piano
                if (!c.auto || !Listening) { c.mapper.Reset(); c.connected = false; return; }
                Scan(c);
                if (c.index < 0) { c.mapper.Reset(); c.connected = false; return; }
            }
            // un controller scollegato si richiede una volta ogni mezzo secondo: XInput lo paga caro a ogni chiamata
            if (!c.wasConnected && Time.unscaledTime < c.retryAt) { c.mapper.Reset(); c.connected = false; return; }
            Raw s = Poll(c.index);
            if (s.connected) c.lostAt = -1f;
            else
            {
                c.retryAt = Time.unscaledTime + 0.5f;
                if (c.auto)
                {
                    // scollegato per piu' di due secondi: lo slot si lascia e si ricomincia a cercare (potrebbe tornare un altro)
                    if (c.lostAt < 0f) c.lostAt = Time.unscaledTime;
                    else if (Time.unscaledTime - c.lostAt > 2f) { Release(c); return; }
                }
            }
            Apply(c, s, false);
        }

        static void Release(Channel c)
        {
            c.index = -1; c.connected = false; c.wasConnected = false; c.preferred = false;
            c.lostAt = -1f; c.scanAt = 0f; c.scanMask = 0;
            c.mapper.Reset();
        }

        /// <summary>Modo automatico, ancora nessuno slot: si rinfresca ogni mezzo secondo quali sono collegati e ogni fotogramma si guardano
        /// solo quelli; il primo su cui c'e' un tasto premuto o uno stick mosso (e che nessun altro posto sta usando) viene adottato.</summary>
        static void Scan(Channel c)
        {
            float t = Time.unscaledTime;
            if (t >= c.scanAt) { c.scanAt = t + 0.5f; c.scanMask = ConnectedMask(); }
            for (int i = 0; i < 4; i++)
            {
                if ((c.scanMask & (1 << i)) == 0 || Taken(i, c)) continue;
                Raw s = Poll(i);
                if (!s.connected) { c.scanMask &= ~(1 << i); continue; }
                if (!Activity(s)) continue;
                c.index = i; c.wasConnected = true; c.lostAt = -1f; c.retryAt = 0f;
                return;
            }
        }

        static void Apply(Channel c, Raw s, bool fromTest)
        {
            c.wasConnected = s.connected; c.connected = s.connected;
            c.mapper.Step(s, Time.unscaledDeltaTime);
            if (s.connected && Activity(s)) { c.lastActivity = Time.unscaledTime; c.preferred = true; }
            else if (c.auto && c.preferred && c == primary && Input.anyKeyDown && Time.unscaledTime - c.lastActivity > 0.3f) c.preferred = false;     // ha preso tastiera o mouse
            if (!fromTest && !Listening) c.mapper.Silence();                                     // un controller non da' ordini a un gioco in secondo piano
            else if (BlockedFor(c)) c.mapper.SuppressGameplay();                                 // menu aperto: il controller naviga, non gioca
        }

        /// <summary>Un menu o un libro usa i comandi di questo controller? Il menu e la pausa sono di tutti; nello schermo condiviso il grimorio
        /// aperto e' di un posto solo, e gli altri controller continuano a giocare.</summary>
        static bool BlockedFor(Channel c)
        {
            if (GameMenu.InMenu || GameMenu.Paused) return true;
            if (!Seats.Split) return GameMenu.Blocked;
            var s = Seats.OfPad(c);
            return s != null && Seats.OverlayOf(s);
        }

        void OnDestroy()
        {
            if (driver == this) driver = null;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        struct XState { public uint packet; public ushort buttons; public byte lt, rt; public short lx, ly, rx, ry; }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern uint GetState14(uint user, out XState state);
        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")] static extern uint GetState13(uint user, out XState state);
        static int api;      // 0 da provare, 1 xinput1_4, 2 xinput1_3, -1 nessuna

        static uint GetState(int user, out XState st)
        {
            st = default;
            if (api == 0 || api == 1)
            {
                try { uint r = GetState14((uint)user, out st); api = 1; return r; }
                catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { api = 2; }
            }
            if (api == 2)
            {
                try { return GetState13((uint)user, out st); }
                catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { api = -1; }
            }
            return 1167;     // ERROR_DEVICE_NOT_CONNECTED
        }

        static Raw PollReal(int slot)
        {
            // 0.9.1: un processo senza finestra (-batchmode: le prove) non e' giocato da nessuno e non legge i controller veri: uno collegato e usato da chi
            // gioca in quel momento finiva nei posti simulati e faceva fallire le prove dello schermo condiviso (i movimenti dei posti fermi, il rialzare)
            if (Application.isBatchMode) return default;
            if (slot < 0 || slot > 3 || GetState(slot, out XState s) != 0) return default;
            return new Raw { connected = true, buttons = s.buttons, leftTrigger = s.lt, rightTrigger = s.rt, lx = s.lx, ly = s.ly, rx = s.rx, ry = s.ry };
        }
#else
        static Raw PollReal(int slot) => default;
#endif

#if !PE_DISTRIBUTION_BUILD
        /// <summary>Solo per le prove: al posto di XInput lo stato dei quattro slot lo da' questa funzione (la ricerca automatica del
        /// controller si prova cosi', senza un controller vero).</summary>
        public static Func<int, Raw> TestPoll;
#endif

        /// <summary>Legge il controller dello slot dato.</summary>
        public static Raw Poll(int slot)
        {
#if !PE_DISTRIBUTION_BUILD
            if (TestPoll != null) return TestPoll(slot);
#endif
            return PollReal(slot);
        }

        /// <summary>Quali slot hanno un controller collegato (bit 0 = slot 0). Si usa nel menu e alla partenza dello split screen.</summary>
        public static int ConnectedMask()
        {
            int m = 0;
            for (int i = 0; i < 4; i++) if (Poll(i).connected) m |= 1 << i;
            return m;
        }
    }
}
