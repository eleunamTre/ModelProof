using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ModelProof
{
    /// <summary>Pulsanti sempre disponibili, anche senza progetti aperti (configurazione, registro).</summary>
    public class AlwaysAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet selected)
        {
            return true;
        }
    }

    /// <summary>Serve un progetto aperto (non una famiglia).</summary>
    public class ProjectAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet selected)
        {
            try { return app.ActiveUIDocument != null && !app.ActiveUIDocument.Document.IsFamilyDocument; }
            catch { return false; }
        }
    }

    /// <summary>
    /// «Crea abachi»: serve un progetto aperto e un file di regole con almeno una sezione [ABACO …]. Il file si
    /// guarda solo quando cambia (percorso o data di modifica), così il controllo resta leggero.
    /// </summary>
    public class ScheduleAvailability : IExternalCommandAvailability
    {
        private static string _path;
        private static DateTime _stamp;
        private static bool _has;

        public bool IsCommandAvailable(UIApplication app, CategorySet selected)
        {
            try
            {
                if (app.ActiveUIDocument == null || app.ActiveUIDocument.Document.IsFamilyDocument) return false;
                string path = Settings.Current.ConfigPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (path != _path || stamp != _stamp)
                {
                    _path = path;
                    _stamp = stamp;
                    _has = false;
                    foreach (string line in File.ReadLines(path))
                        if (line.TrimStart().StartsWith("[ABACO ", StringComparison.OrdinalIgnoreCase)) { _has = true; break; }
                }
                return _has;
            }
            catch { return false; }
        }
    }
}
