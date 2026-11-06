using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ModelProof
{
    /// <summary>
    /// Seleziona e inquadra un elemento dalla finestra dei risultati. La finestra non è modale, quindi la richiesta
    /// passa da un ExternalEvent e Revit la esegue quando può.
    /// </summary>
    internal sealed class ShowElementHandler : IExternalEventHandler
    {
        private static ShowElementHandler _instance;
        private static ExternalEvent _event;

        private long _id = -1;
        private string _documentKey;

        /// <summary>Da chiamare dentro un comando (contesto dell'API).</summary>
        public static void Init()
        {
            if (_event != null) return;
            _instance = new ShowElementHandler();
            _event = ExternalEvent.Create(_instance);
        }

        public static void Request(string documentKey, long id)
        {
            if (_event == null || id < 0) return;
            _instance._id = id;
            _instance._documentKey = documentKey;
            _event.Raise();
        }

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null) return;
                if (!string.Equals(Checker.DocumentKey(uidoc.Document), _documentKey, StringComparison.OrdinalIgnoreCase))
                {
                    TaskDialog.Show("ModelProof", L.T("Il progetto attivo non è quello verificato: attiva la sua finestra e riprova."));
                    return;
                }
                var id = new ElementId(_id);
                if (uidoc.Document.GetElement(id) == null) return;
                uidoc.Selection.SetElementIds(new List<ElementId> { id });
                try { uidoc.ShowElements(id); }
                catch (Exception ex) { Log.Debug("Mostra elemento", ex.Message); }
            }
            catch (Exception ex) { Log.Error("Mostra elemento", ex); }
        }

        public string GetName()
        {
            return "ModelProof - mostra elemento";
        }
    }
}
