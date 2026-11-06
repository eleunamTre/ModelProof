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
}
