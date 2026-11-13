# Revisione richiesta: ritmo da CoD Zombies

Richiesta utente del 13 novembre 2026. Questa revisione estende l'audit: non è completata dai precedenti controlli.

- Riformulare la Veglia: figura votiva più credibile, evitando il volto cilindrico e la fiamma a cono.
- Ridisegnare l'HUD: gerarchia chiara per munizioni, vita, punti, round e obiettivi durante il combattimento.
- Ripensare la gestione degli spazi: molte più sale grandi, nuovi ambienti, alternanza con corridoi e passaggi;
  anelli di circolazione, vie di fuga, forme meno quadrate e sale con pareti curve.
- Verticalità effettiva nello stesso piano: doppie altezze, balconate percorribili, scale e più collegamenti fra quote;
  percorsi utilizzabili dal controller e dai nemici, senza balconate decorative senza uscita.
- Rifare la leggibilità degli attacchi: preparazione e recupero del corpo a corpo, animazione di contatto coerente,
  effetti dei proiettili e impatti meno approssimativi, feedback di danno chiaro e direzionale.

Verifica richiesta: mappe e misure dei piani, scene renderizzate dalle quote inferiore e superiore, camminata fisica
su scale e balconate, percorsi dei nemici, round e transizioni in cooperativa, immagini e prove degli attacchi.
Il riferimento riguarda ritmo, manovrabilità e feedback; l'identità resta quella dell'hotel e della chiesa.

## Prima passata: scala e percorsi verticali

Implementati moduli da 12,8–17,6 m, più sale a moduli multipli, più collegamenti ad anello e porte principali
da 2,4–3 m. Nelle sale ampie: soffitto almeno 8,2 m, balconata a quota 3,6 m, camminamento largo 2,4 m e due
scale da 18 alzate di 20 cm. Le scale sono staccate dalle pareti per mantenere libere le porte al piano terra.
Il percorso superiore è a U: si può salire da una scala e scendere dall'altra; non è un fondale decorativo.
Gli arredi casuali rispettano i percorsi e i nemici possono usare le quote superiori.

Verifiche: 1.000 piante pure senza fallimenti, 400 piani costruiti senza problemi residui, 9.456 punti superiori
raggiungibili dall'arrivo; camminata del controller su sei balconate nei tre temi (21/21 per ciascuna prova),
più dodici piani completi percorsi senza incastri. Vedere `unity_logs/architecture*` e `docs/quality-audit.md`.

## Seconda passata: nuove forme e funzioni

Il generatore ora sceglie una sala adatta come Rotonda, con quattro pareti curve fisiche, fontana bassa centrale,
isole di seduta e due fasce di circolazione. La curvatura rispetta le porte esistenti; le finestre restano sui tratti
rettilinei. Le sale allungate diventano Gallerie; nell’albergo compare anche un Giardino d’inverno con piante e sedute.
Le stanze curve usano la stessa impronta per appartenenza, arredi, punti dei nemici e mappa. Gli espositori restano
sui tratti rettilinei accessibili. Il lampadario della Rotonda illumina pavimento e ostacoli senza cambiare la luminosità
scelta dal giocatore. Le balconate della prima passata restano nelle altre sale ampie.

Verifiche: 400 piani costruiti, nessun problema residuo, 8.164 punti superiori raggiungibili; 399 sale curve senza
angoli esclusi attraversabili. Camminata fisica e collisioni nei tre temi: 42/42 per prova. Dodici piani completi
percorsi senza avvisi; interfaccia 92/92. Log in `unity_logs/curves-*`.

## Terza passata: gerarchia dell’HUD

Raggruppati vita, scudo, batterie e punti a sinistra; arma e munizioni a destra; piano, obiettivo e perk in alto.
I compagni occupano l’angolo superiore destro. I dati di combattimento usano un sans di sistema più netto,
mentre i titoli e il grande numero rosso del round mantengono il serif dell’hotel. Sfumature scure dietro le risorse
ne sostengono il contrasto anche sui pavimenti chiari. Vita e scudo hanno valori numerici; la vita critica rispetta
anche il massimo aumentato dal perk. La soglia del caricatore critico è il 20% della capacità dell’arma, incluse le
armi consacrate. Avvisi distinti per ricarica, caricatore vuoto e munizioni esaurite.

La verifica di presentazione include tutte le armi consacrate con due slot pieni, quattro perk, round animato,
punteggio a sette cifre, vita critica durante la rigenerazione e ricarica reale. Vedere `docs/quality-audit.md` per gli
esiti sulle risoluzioni verificate. Restano da valutare leggibilità durante combattimenti affollati e dispositivi fisici.

## Quarta passata: dardi, contatti e provenienza del danno

Sostituite le sfere con un nucleo sottile, alone morbido e scia rastremata. La carica sul bastone usa un sigillo
sfaccettato. I contatti di bastone e dardo producono scintille e pulviscolo brevi al posto del disco pieno che si
espandeva. I dardi e gli effetti del mondo rimangono visibili anche nella camera delle ottiche.

Fino a quattro archi attorno al mirino indicano da dove arriva il danno, seguono la rotazione dello sguardo e
svaniscono in 0,85 s; l’immunità non produce segnali falsi. La provenienza passa anche dal server al client.
Il dardo verifica il tratto intero percorso a ogni aggiornamento contro pareti e capsule dei giocatori, sceglie il
primo contatto e infligge danno una volta sola; la copia grafica non causa danno. Accovacciarsi riduce il bersaglio.

## Quinta passata: corpo a corpo degli umanoidi

Nuova animazione di 38 fotogrammi a 30 fps: caricamento laterale, affondo a quota torace al fotogramma 18,
recupero. Monaco, Abate, Maschera, Direttore, Infermiere e Primario usano lo stesso scheletro; l’Abate mantiene
la velocità ridotta a 0,85. L’incantesimo ha ora uno stato distinto, senza usare l’affondo della mischia.

Durante il gesto il nemico mantiene la direzione scelta. Il danno al contatto richiede ancora bersaglio vivo,
settore frontale, distanza, quota e linea libera. Arretrare o spostarsi alle spalle evita il colpo; una parete
sopraggiunta lo blocca. Il bersaglio non cambia durante il gesto, e la sua uscita annulla il contatto. Il recupero
non infligge altri danni. Fruscio dell’arma durante l’affondo, suono e particelle di contatto solo se il colpo arriva.

Verifica nel gioco: **62/62**, comprese le sei clip, altezza delle mesh animate, fase dell’affondo, movimento reale
del controller e contatto annullato. Pose frontali e laterali in `unity_logs/melee`. Cooperativa: **16/16 ospite,
28/28 client**, inclusa la visione della preparazione della nuova clip sul client.

Restano aperti: riformulazione della Veglia e valutazione dei gesti dei nemici non umanoidi, ancora procedurali.
Le immagini ravvicinate evidenziano anche zone bruciate dalla torcia su alcuni costumi: correggere luce/materiali
senza cambiare le preferenze del giocatore. Continuare la valutazione degli spazi durante combattimenti affollati.
La revisione complessiva rimane in corso.
