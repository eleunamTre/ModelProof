# ModelProof – roadmap

Plugin Revit di model checking: controlli generali del modello, regole informative configurabili, report Excel con
sintesi e foto delle anomalie, verifica di più modelli insieme (ARC, MEP, URS).

## Fatto
- Struttura del progetto per Revit 2024, 2025, 2026 e 2027 (stesso codice, una configurazione per versione).
- Lettura e controllo del file di configurazione del progetto, prova delle regole (MODIFICHE 2–4).

## Prossimi passi
1. Riconoscimento dei modelli dal nome del file e primi controlli sul modello aperto: livelli e coordinate rispetto
   all'URS, nomi di famiglie e tipi, Type Mark, codici delle istanze; finestra dei risultati.
2. Finestra di configurazione completa (una scheda per controllo) che scrive lo stesso file di testo.
3. Controlli generali dagli script Dynamo: dimensione file, georeferenziazione, locali, modelli generici e famiglie
   locali, oggetti sovrapposti, funzione dei muri, collocazione rispetto ai livelli, inventario, parametri IFC,
   parametri condivisi.
4. Regole informative configurabili.
5. Report: foglio di sintesi, foto automatiche delle anomalie, finestra delle anomalie in Revit.
6. Verifica di più modelli: dai collegamenti e da un set di file aperti in background.
7. Uscite: checklist compilata (solo le colonne dell'esito) e report di dettaglio con foto.
