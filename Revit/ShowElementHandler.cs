using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ModelProof
{
    /// <summary>Cosa fare con gli elementi chiesti dalla finestra dei risultati.</summary>
    internal enum ShowMode
    {
        Select,     // seleziona (evidenziati in blu da Revit)
        Zoom,       // seleziona e inquadra
        Isolate,    // seleziona e isola temporaneamente nella vista attiva
        Reset       // toglie l'isolamento temporaneo della vista attiva
    }

    /// <summary>
    /// Seleziona, inquadra o isola elementi dalla finestra dei risultati. La finestra non è modale, quindi la
    /// richiesta passa da un ExternalEvent e Revit la esegue quando può. Il modello non viene modificato:
    /// l'isolamento è quello temporaneo della vista (occhiali in basso), che non si salva.
    /// </summary>
    internal sealed class ShowElementHandler : IExternalEventHandler
    {
        private static ShowElementHandler _instance;
        private static ExternalEvent _event;

        private List<long> _ids = new List<long>();
        private string _documentKey;
        private ShowMode _mode;

        /// <summary>Da chiamare dentro un comando (contesto dell'API).</summary>
        public static void Init()
        {
            if (_event != null) return;
            _instance = new ShowElementHandler();
            _event = ExternalEvent.Create(_instance);
        }

        public static void Request(string documentKey, IEnumerable<long> ids, ShowMode mode)
        {
            if (_event == null) return;
            _instance._ids = ids.Where(i => i >= 0).Distinct().ToList();
            _instance._documentKey = documentKey;
            _instance._mode = mode;
            if (_instance._ids.Count == 0 && mode != ShowMode.Reset) return;
            _event.Raise();
        }

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null) return;
                Document doc = uidoc.Document;
                if (!string.Equals(Checker.DocumentKey(doc), _documentKey, StringComparison.OrdinalIgnoreCase))
                {
                    TaskDialog.Show("ModelProof", L.T("Il progetto attivo non è quello verificato: attiva la sua finestra e riprova."));
                    return;
                }
                View view = uidoc.ActiveView;

                if (_mode == ShowMode.Reset)
                {
                    if (view != null && view.IsTemporaryHideIsolateActive())
                        using (var t = new Transaction(doc, "ModelProof: " + L.T("ripristina vista")))
                        {
                            t.Start();
                            view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                            t.Commit();
                        }
                    return;
                }

                var ids = new List<ElementId>();
                foreach (long v in _ids)
                {
                    var id = new ElementId(v);
                    if (doc.GetElement(id) != null) ids.Add(id);
                }
                if (ids.Count == 0) return;
                uidoc.Selection.SetElementIds(ids);

                if (_mode == ShowMode.Zoom)
                {
                    try { uidoc.ShowElements(ids); }
                    catch (Exception ex) { Log.Debug("Mostra elementi", ex.Message); }
                }
                else if (_mode == ShowMode.Isolate && view != null)
                {
                    try
                    {
                        using (var t = new Transaction(doc, "ModelProof: " + L.T("isola elementi")))
                        {
                            t.Start();
                            if (view.IsTemporaryHideIsolateActive()) view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                            view.IsolateElementsTemporary(ids);
                            t.Commit();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Isola elementi", ex.Message);
                        TaskDialog.Show("ModelProof", L.T("Nella vista attiva non si possono isolare questi elementi (es. vista abaco o tavola)."));
                    }
                }
                Log.Debug("Mostra elementi", _mode + ": " + ids.Count + " elementi");
            }
            catch (Exception ex) { Log.Error("Mostra elementi", ex); }
        }

        public string GetName()
        {
            return "ModelProof - mostra elementi";
        }
    }
}
