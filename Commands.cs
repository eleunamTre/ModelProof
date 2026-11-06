using System;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ModelProof
{
    /// <summary>Apre la finestra della configurazione del progetto (file di regole).</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ConfigCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                IntPtr owner = IntPtr.Zero;
                try { owner = commandData.Application.MainWindowHandle; } catch { }
                ConfigForm.ShowWindow(owner);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log.Error("Configurazione", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Apre la cartella del registro di diagnostica.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class OpenLogCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Directory.CreateDirectory(Log.Folder);
                Log.Write("Registro", "aperta la cartella del registro");
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Log.Folder + "\"") { UseShellExecute = false });
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Verifica il modello aperto con le regole del file di configurazione e mostra i risultati.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class CheckModelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            IntPtr owner = IntPtr.Zero;
            try { owner = commandData.Application.MainWindowHandle; } catch { }
            try
            {
                ShowElementHandler.Init();
                Document doc = commandData.Application.ActiveUIDocument.Document;

                // il file si rilegge ogni volta: le modifiche fatte nel Blocco note valgono subito
                string path = Settings.Current.ConfigPath;
                ProjectConfig cfg = null;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try { cfg = ProjectConfig.Load(path); }
                    catch (Exception ex) { Log.Error("Configurazione", ex); }
                }
                if (cfg == null || cfg.ErrorCount > 0)
                {
                    TaskDialog.Show("ModelProof", cfg == null
                        ? L.T("Prima scegli il file di configurazione con «Regole del progetto».")
                        : L.F("Il file di configurazione ha {0} errori: correggili prima della verifica.", cfg.ErrorCount));
                    ConfigForm.ShowWindow(owner);
                    return Result.Succeeded;
                }

                CheckRun run;
                System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
                try { run = new Checker(doc, cfg).Run(); }
                finally { System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default; }
                ResultsForm.ShowRun(run, owner);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log.Error("Verifica", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Riapre i risultati dell'ultima verifica del progetto attivo, senza rifarla.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class LastResultsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                ShowElementHandler.Init();
                IntPtr owner = IntPtr.Zero;
                try { owner = commandData.Application.MainWindowHandle; } catch { }
                CheckRun run = ResultsForm.LastRun(Checker.DocumentKey(commandData.Application.ActiveUIDocument.Document));
                if (run == null)
                {
                    TaskDialog.Show("ModelProof", L.T("Questo progetto non è ancora stato verificato: usa «Verifica modello»."));
                    return Result.Succeeded;
                }
                ResultsForm.ShowRun(run, owner);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log.Error("Risultati", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
