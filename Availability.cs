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
}
