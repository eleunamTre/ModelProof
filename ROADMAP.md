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
- Controlli pronti sugli elenchi (parametri condivisi, codice categoria, funzione ammessa, categoria ammessa),
  funzione dei muri e numero del locale nella ZBS (MODIFICHE 16).
- Comando «Interroga collegamento»: ID e parametri degli elementi dei modelli collegati (MODIFICHE 18).
- Comando «Crea abachi»: abachi di controllo configurati nel file delle regole (MODIFICHE 21).
- Righe OK in verde nella finestra, nascoste da «Solo errori» (MODIFICHE 14–15).
- Dettaglio delle coordinate di tutti i file (valori, nord reale, coordinate condivise, posizionamento dei
  collegamenti) nella finestra e nell'Excel (MODIFICHE 13).

## Prossimi passi
1. Prove sui modelli veri dei controlli di MODIFICHE 16 (parametri condivisi, codice categoria, funzione ammessa,
   categoria ammessa, funzione dei muri, numero del locale nella ZBS) e correzione dei falsi positivi.
2. Uscite: checklist compilata (copia del modello, solo le colonne dell'esito, con anteprima modificabile; la parte
   «formato» comprende anche i codici non in elenco) e foto automatiche delle anomalie nel report di dettaglio.
3. Controlli generali dagli script Dynamo: dimensione file, locali non posizionati e zone chiuse senza locale,
   modelli generici e famiglie locali, oggetti sovrapposti, collocazione rispetto ai livelli, inventario,
   creazione degli abachi IFC.
4. Verifica di più modelli: oltre ai collegamenti, un set di file aperti in background (mai salvati); prima il
   riferimento, poi gli altri a cascata. Report unico diviso anche per modello (sintesi per file e filtro per
   modello nella finestra).
5. Finestra di configurazione guidata che scrive lo stesso file di testo: elenco delle regole, scheda per regola con
   oggetto, campo, formato costruito a pezzi dai segnaposto, coerenza e numerazione scelte da menu, prova immediata
   sui valori del modello aperto (es. i primi nomi dei tipi); stessa cosa per elenchi, abachi e checklist.
   Dopo: editor a blocchi in stile Dynamo (nodi collegati da fili: sorgente → lettura del campo → formato →
   confronto → esito), con blocchi di base (oggetti, campi, segnaposto, elenchi, coerenza, numerazione, condizioni)
   e blocchi personalizzati salvati e riusabili; il grafo si salva nello stesso file di configurazione.
   Poi, facoltativo: regola proposta a partire da una frase («il Type Mark è CAT.fu.N coerente con il nome») o da
   esempi di valori giusti, mostrata nella finestra guidata per conferma. Con un modello linguistico servirebbe una
   connessione e una scelta sulla riservatezza (il testo delle regole uscirebbe dal computer); in alternativa
   proposta locale dagli esempi (si riconoscono campi, separatori e codici degli elenchi).
6. Mappatura IFC per codice di categoria + funzione (oggi la regola «classe ifc» usa solo il codice di categoria
   ed è un avviso): elenco dedicato nel file di configurazione con classe e tipo predefinito attesi.
7. Falsi positivi confermati: rileggere un Excel di risultati con la colonna «Verifica manuale» compilata e
   ricordare le anomalie segnate «Falso positivo» (per file, regola, elemento e valore) per mostrarle a parte, in
   grigio, nelle verifiche successive; elenco consultabile e annullabile.
8. Da decidere: tolleranze di coordinate e angoli, soglia dell'esito PARZIALE, parametri in lista ma assenti;
   righe OK delle regole di campi nella finestra (tenerle, metterle dietro un'opzione o toglierle dopo le prove).
