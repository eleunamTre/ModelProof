# ModelProof

Plugin per Autodesk Revit che verifica i modelli BIM: controlli generali del modello e regole informative
(codifiche, parametri obbligatori, formati) descritte in un file di configurazione del progetto.

Il file di configurazione resta sul computer di chi lavora: nella repository ci sono solo il programma e la
descrizione del formato, nessun dato di progetto.

## Versioni di Revit

| Configurazione | Revit | Framework |
|---|---|---|
| R24 | 2024 | .NET Framework 4.8 |
| R25 | 2025 | .NET 8 |
| R26 | 2026 | .NET 8 |
| R27 | 2027 | .NET 10 (serve l'SDK di .NET 10) |

Si sceglie la configurazione in Visual Studio e si compila: DLL e manifest vengono copiati in
`%APPDATA%\Autodesk\Revit\Addins\<versione>` (chiudere quella versione di Revit prima di compilare, poi riavviarla).
Le API di Revit arrivano da NuGet, quindi non serve avere installata la versione per cui si compila.

## Registro

`%LOCALAPPDATA%\ModelProof\ModelProof.log` (pulsante «Registro» nella scheda ModelProof).
