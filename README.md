# ModelProof

Plugin per Autodesk Revit per automatizzazione dei controlli sui modelli BIM: controlli generali del modello e regole informative
(codifiche, parametri obbligatori, formati) secondo quanto descritto in un file di configurazione per il progetto in locale.

## Versioni di Revit

| Configurazione | Revit | Framework |
|---|---|---|
| R24 | 2024 | .NET Framework 4.8 |
| R25 | 2025 | .NET 8 |
| R26 | 2026 | .NET 8 |
| R27 | 2027 | .NET 10 (serve l'SDK di .NET 10) |

Scegliere la configurazione in Visual Studio e compilare: DLL e manifest vengono copiati in
`%APPDATA%\Autodesk\Revit\Addins\<versione>` (chiudere quella versione di Revit prima di compilare, poi riavviarla).
Le API di Revit arrivano da NuGet, non serve avere installata la versione per cui si compila.

## Registro

`%LOCALAPPDATA%\ModelProof\ModelProof.log` (pulsante «Registro» nella scheda ModelProof).
