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

Restano richiesti e aperti: nuove funzioni delle stanze, corridoi progettati come collegamenti, geometrie curve e
meno regolari; riformulazione della Veglia; nuova gerarchia dell'HUD; animazione e feedback degli attacchi e degli
impatti. La prima passata non chiude la revisione complessiva.
