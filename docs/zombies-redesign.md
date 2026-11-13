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

Restano aperti: riformulazione della Veglia e revisione delle animazioni di corpo a corpo, preparazione, contatto e
recupero. La quarta passata migliora gli effetti e il feedback ma mantiene per ora le clip esistenti dei nemici.
Continuare anche la valutazione degli spazi durante il combattimento: le prove di navigazione non bastano a
dimostrare il ritmo finale con gruppi di nemici. La revisione complessiva rimane in corso.
