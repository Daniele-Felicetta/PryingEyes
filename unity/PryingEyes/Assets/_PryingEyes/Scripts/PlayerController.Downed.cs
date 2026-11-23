using UnityEngine;

namespace PryingEyes
{
    /// <summary>A terra e rianimazione (0.7.2, "quando si muore si deve andare a terra e ci si possa rianimare"). Quando la vita arriva a
    /// zero (e la Rinascita del libro non ti rialza) non si muore subito: si cade a terra, si striscia (visuale bassa, 0,85 m/s, niente
    /// corsa, salto, coltello, magie, oggetti e acquisti; l'arma in mano spara ancora) e i nemici ti ignorano (ITarget.Alive e' falso).
    /// Dopo BleedOutTime secondi senza aiuto si muore come prima (punti e perk persi, si rinasce all'ingresso). Ti rialza:
    /// - un compagno, tenendo premuto Usa (F, controller X) per ReviveTime secondi a meno di ReviveRange metri: nello schermo condiviso
    ///   direttamente, in cooperativa con ReviveClaim all'ospite che lo gira a chi e' a terra (Revived);
    /// - tu stesso, una volta per piano, tenendo premuto Usa per SelfReviveTime secondi.
    /// Ci si rialza con meta' vita e 2,5 s di tregua.</summary>
    public partial class PlayerController
    {
        public const float BleedOutTime = 30f, ReviveTime = 3f, SelfReviveTime = 5f, ReviveRange = 1.8f, CrawlSpeed = 0.85f, EyeDowned = 0.55f;
        /// <summary>Per le prove: il tempo del dissanguamento (BleedOutTime di serie).</summary>
        public static float bleedOutOverride = -1f;
        /// <summary>Per le prove: come se questo giocatore tenesse premuto Usa (in batchmode non ci sono tasti).</summary>
        [System.NonSerialized] public bool testHoldUse;

        /// <summary>A terra: vivo, ma fuori combattimento finche' qualcuno non lo rialza o il tempo finisce.</summary>
        public bool Downed { get; private set; }
        /// <summary>Secondi prima di morire dissanguato (solo a terra).</summary>
        public float BleedLeft { get; private set; }
        /// <summary>Quanto manca a rialzarsi (0..1), da solo o per mano di un compagno di questo schermo.</summary>
        public float RisingProgress { get; private set; }
        /// <summary>Lato di chi aiuta: chi sto rianimando (nome) e a che punto (0..1).</summary>
        public string RevivingName { get; private set; }
        public float RevivingProgress { get; private set; }

        int selfReviveFloor = -1, downFloor;
        float selfHold, helpedT, reviveHold, aloneT;
        object reviveTarget;

        /// <summary>Una volta per piano ci si puo' rialzare da soli.</summary>
        public bool SelfReviveAvailable => selfReviveFloor != CurrentFloor;
        static int CurrentFloor => Proc.FloorDirector.I != null ? Proc.FloorDirector.I.floor : 0;
        static bool Transitioning => Proc.FloorDirector.I != null && Proc.FloorDirector.I.transitioning;

        static string UseKeyName => PadControls.Preferred ? "X" : "F";

        /// <summary>Qualcuno mi puo' ancora rialzare: io stesso (una volta per piano) o un compagno in piedi, in questo schermo o in rete.
        /// Se nessuno puo', aspettare il dissanguamento non serve a niente: si muore subito.</summary>
        bool AnyoneCanHelp()
        {
            if (SelfReviveAvailable) return true;
            foreach (var s in Seats.All)
            {
                var p = s.player;
                if (p != null && p != this && p.alive && !p.Downed) return true;
            }
            var g = Coop.Game;
            if (g != null)
                foreach (var kv in g.Players)
                {
                    var rp = kv.Value;
                    if (rp != null && rp.Shown && rp.last.alive && !rp.last.downed) return true;
                }
            return false;
        }

        void GoDown()
        {
            if (!AnyoneCanHelp()) { Die(); return; }        // da solo e gia' rialzato su questo piano: nessuno verrebbe
            Downed = true;
            ResetParkour();
            downFloor = CurrentFloor; aloneT = 0f;
            hp = 0f;
            BleedLeft = bleedOutOverride > 0f ? bleedOutOverride : BleedOutTime;
            RisingProgress = 0f; selfHold = 0f; helpedT = 0f;
            vel = new Vector3(0f, Mathf.Min(vel.y, 0f), 0f);
            lean = 0f;
            Enemy.OnTargetGone(Seats.TargetFor(this));       // chi ti inseguiva cerca un altro (a terra non sei un bersaglio)
            hud?.Banner("A terra", SelfReviveAvailable ? $"Strisci. Tieni {UseKeyName} per rialzarti da solo, o aspetta un compagno."
                                                         : "Strisci. Solo un compagno ti puo' rialzare.");
            hud?.Flash(new Color(0.7f, 0.05f, 0.05f), 0.8f, 0.6f);
            hud?.SetBars(hp, shield);
            Synth.Hurt();
            Debug.Log($"[PryingEyes] a terra: {name} ({BleedLeft:F0} s)");
        }

        /// <summary>Ogni fotogramma a terra: il dissanguamento, il rialzarsi da soli tenendo Usa, la riga che dice come stai.</summary>
        void DownedTick(float dt, bool inputEnabled)
        {
            // il gruppo e' sceso al piano dopo (o ci sei arrivato mentre eri a terra): l'ascensore ti rimette in piedi
            if (CurrentFloor != downFloor && !Transitioning) { StandUp("Il nuovo piano ti rimette in piedi."); return; }
            if (Transitioning) { hud?.SetPrompt(""); return; }      // nella dissolvenza del cambio piano non si sanguina
            BleedLeft -= dt;
            helpedT -= dt;
            // nessuno puo' piu' rialzarti (tutti a terra o morti, e il tuo rialzarti da solo e' gia' usato): un attimo di margine per la rete
            aloneT = AnyoneCanHelp() ? 0f : aloneT + dt;
            if (aloneT > 1.5f) { Die(); return; }
            bool held = testHoldUse || (inputEnabled && (SeatInput.GetKey(KeyCode.F) || TouchControls.Held(TouchControls.Action.Use)));
            if (SelfReviveAvailable && held && helpedT <= 0f)
            {
                selfHold += dt;
                RisingProgress = Mathf.Clamp01(selfHold / SelfReviveTime);
                if (selfHold >= SelfReviveTime) { selfReviveFloor = CurrentFloor; ReviveBy(null); return; }
            }
            else if (helpedT <= 0f) { selfHold = Mathf.Max(0f, selfHold - dt * 2f); RisingProgress = Mathf.Clamp01(selfHold / SelfReviveTime); }
            if (BleedLeft <= 0f) { Die(); return; }
            string state = helpedT > 0f ? "TI STANNO RIALZANDO " + Bar(RisingProgress)
                : SelfReviveAvailable ? (selfHold > 0.01f ? "TI RIALZI " + Bar(RisingProgress) : $"TIENI {UseKeyName} PER RIALZARTI DA SOLO")
                : "ASPETTA UN COMPAGNO";
            hud?.SetPrompt($"A TERRA  {Mathf.CeilToInt(BleedLeft)} s  ·  {state}");
        }

        /// <summary>Un compagno di questo schermo mi sta rialzando (lo chiama lui ogni fotogramma).</summary>
        public void HelpedBy(float progress)
        {
            if (!Downed) return;
            helpedT = 0.2f;
            RisingProgress = Mathf.Clamp01(progress);
        }

        /// <summary>In piedi: con meta' vita e un attimo di tregua. 'by' = chi ti ha rialzato (null = da solo).</summary>
        public void ReviveBy(string by)
        {
            StandUp(by == null ? "Ti sei rialzato da solo. Non succedera' di nuovo su questo piano." : by + " ti ha rialzato.");
        }

        void StandUp(string line)
        {
            if (!Downed || !alive) return;
            Downed = false;
            hp = MaxHp * 0.5f; shield = 0f; reviveInv = 2.5f;
            RisingProgress = 0f; selfHold = 0f; helpedT = 0f; aloneT = 0f;
            hud?.SetBars(hp, shield);
            hud?.SetPrompt("");
            hud?.Banner("In piedi", line);
            hud?.Flash(new Color(0.55f, 1f, 0.62f), 0.7f, 0.5f);
            Sfx.UI(SoundBank.PerkRevive, 0.8f);
            Debug.Log($"[PryingEyes] rialzato: {name} ({line})");
        }

        /// <summary>Per le prove che vogliono la morte vera (penalita', ondata annullata): chi e' a terra muore subito, come a fine
        /// dissanguamento. Da solo si va a terra perche' ci si puo' rialzare una volta per piano.</summary>
        public void BleedOutNow() { if (Downed && alive) Die(); }

        /// <summary>Morte vera: dissanguato o colpito senza scampo. Come prima: punti e perk persi, si rinasce all'ingresso.</summary>
        void Die()
        {
            ResetParkour();
            Downed = false;
            hp = 0f; alive = false; respawnT = 2.5f;
            RisingProgress = 0f;
            GameManager.deaths++;
            Arcana.OnDeath();
            // si perde meta' dei punti e l'ultimo perk comprato (FloorDirector.DeathPenalty): la morte pesa, ma non e' la fine
            string pen = GameManager.proc ? Proc.FloorDirector.I?.DeathPenalty() : null;
            hud?.Banner("Sei morto", string.IsNullOrEmpty(pen) ? "L'hotel ti riporta all'ingresso." : pen);
            hud?.SetPrompt("");
            if (!Seats.Split) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }     // nello split screen il mouse e' di tutti: non si sblocca per uno che muore
        }

        /// <summary>In piedi: c'e' un compagno a terra qui vicino? Tenendo Usa lo si rialza in ReviveTime secondi. Nello schermo condiviso
        /// lo rialza direttamente, in cooperativa lo dice all'ospite (CoopGame.ReviveRemote). 'held' viene dai tasti (o dalla prova).
        /// Vero se c'e' qualcuno da rialzare qui vicino (la riga dei comandi e' sua).</summary>
        public bool ReviveOthersTick(float dt, bool held)
        {
            object target = FindDownedMate(out string tname, out _);
            var g = Coop.Game;
            if (target == null) { reviveTarget = null; reviveHold = 0f; RevivingName = null; RevivingProgress = 0f; return false; }
            if (target != reviveTarget) { reviveTarget = target; reviveHold = 0f; }
            if (held) reviveHold += dt; else reviveHold = Mathf.Max(0f, reviveHold - dt * 2f);
            RevivingName = tname;
            RevivingProgress = Mathf.Clamp01(reviveHold / ReviveTime);
            if (target is PlayerController pc && reviveHold > 0f) pc.HelpedBy(RevivingProgress);     // solo se lo sto davvero rialzando
            if (reviveHold >= ReviveTime)
            {
                string me2 = Seats.Split ? Seats.OfPlayer(this)?.Name ?? "Un compagno" : Coop.PlayerName;
                if (target is PlayerController pc2) pc2.ReviveBy(me2);
                else if (target is RemotePlayer rp2) g?.ReviveRemote(rp2.slot);
                hud?.Toast($"Hai rialzato {tname}.");
                reviveTarget = null; reviveHold = 0f; RevivingProgress = 0f;
                return true;
            }
            hud?.SetPrompt(reviveHold > 0.01f ? $"RIALZI {tname.ToUpperInvariant()} {Bar(RevivingProgress)}" : $"{tname.ToUpperInvariant()} E' A TERRA  ·  TIENI {UseKeyName} PER RIALZARLO");
            return true;
        }

        /// <summary>Il compagno a terra piu' vicino entro ReviveRange (un altro posto di questo schermo o un compagno in rete), o null.</summary>
        object FindDownedMate(out string tname, out Vector3 at)
        {
            object target = null; tname = null; at = default;
            float best = ReviveRange * ReviveRange;
            Vector3 me = transform.position;
            foreach (var s in Seats.All)
            {
                var p = s.player;
                if (p == null || p == this || !p.alive || !p.Downed) continue;
                float d = (p.transform.position - me).sqrMagnitude;
                if (d < best) { best = d; target = p; tname = s.Name; at = p.transform.position; }
            }
            var g = Coop.Game;
            if (g != null)
                foreach (var kv in g.Players)
                {
                    var rp = kv.Value;
                    if (rp == null || !rp.Shown || !rp.last.alive || !rp.last.downed) continue;
                    float d = (rp.transform.position - me).sqrMagnitude;
                    if (d < best) { best = d; target = rp; tname = rp.playerName; at = rp.transform.position; }
                }
            return target;
        }

        static string Bar(float k)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(k * 10f), 0, 10);
            return new string('▮', n) + new string('▯', 10 - n);
        }
    }
}
