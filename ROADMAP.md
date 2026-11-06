# ModelProof – roadmap

Plugin Revit di model checking: controlli generali del modello, regole informative configurabili, report Excel con
sintesi e foto delle anomalie, verifica di più modelli insieme (ARC, MEP, URS).

## Fatto
- Struttura del progetto per Revit 2024, 2025, 2026 e 2027 (stesso codice, una configurazione per versione).
- Lettura e controllo del file di configurazione del progetto, prova delle regole (MODIFICHE 2–4).
- Verifica del modello aperto: riconoscimento dal nome del file, regole di campi (formato, coerenza, numerazione),
  coordinate e livelli rispetto al riferimento collegato, finestra dei risultati con CSV (MODIFICHE 5–7).
- Correzioni dopo la prima prova su un modello vero: campi facoltativi, componenti annidati e parti di ringhiere e
  scale esclusi, pannelli e montanti non trattati come famiglie caricabili, «se_assente», note sui collegamenti
  (MODIFICHE 8).
- Report diviso per sezione: finestra con regole raggruppate, Excel con foglio di sintesi e un foglio per sezione;
  anomalie selezionate, inquadrate o isolate nel modello (MODIFICHE 9).
- Priorità delle anomalie: errori di struttura, codici non presenti negli elenchi del file («non in elenco»), avvisi,
  con colori diversi; avviso condizionato nella coerenza «(avviso se PARTE = "testo")» (MODIFICHE 10).
- Controllo «nome file»: codice del modello coerente con la descrizione, un solo modello per ruolo; riferimento
  riconosciuto anche dalla descrizione quando il codice è sbagliato (MODIFICHE 11).
- Modello e file di ogni anomalia nella finestra, nel CSV e nell'Excel (MODIFICHE 12).

## Prossimi passi
1. Controlli già pronti mancanti: parametri condivisi, codice categoria e funzione ammessa (elenchi), categoria
   ammessa per il modello, funzione dei muri; numero del locale nella ZBS (locale che contiene l'elemento).
2. Uscite: checklist compilata (copia del modello, solo le colonne dell'esito, con anteprima modificabile; la parte
   «formato» comprende anche i codici non in elenco) e foto automatiche delle anomalie nel report di dettaglio.
3. Controlli generali dagli script Dynamo: dimensione file, locali non posizionati e zone chiuse senza locale,
   modelli generici e famiglie locali, oggetti sovrapposti, collocazione rispetto ai livelli, inventario,
   creazione degli abachi IFC.
4. Verifica di più modelli: oltre ai collegamenti, un set di file aperti in background (mai salvati); prima il
   riferimento, poi gli altri a cascata. Report unico diviso anche per modello (sintesi per file e filtro per
   modello nella finestra).
5. Finestra di configurazione completa (una scheda per controllo) che scrive lo stesso file di testo.
6. Da decidere: tolleranze di coordinate e angoli, soglia dell'esito PARZIALE, parametri in lista ma assenti.
