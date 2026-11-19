using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace ModelProof
{
    /// <summary>
    /// Apre la finestra «Interroga collegamento» (resta aperta accanto a Revit). Dalla finestra si scelgono gli elementi
    /// dei modelli collegati, anche molti insieme; per ciascuno si vedono ID, file, categoria, famiglia/tipo, livello
    /// e parametri, e gli ID si copiano. Sola lettura.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class LinkQueryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                LinkPickHandler.Init();
                IntPtr owner = IntPtr.Zero;
                try { owner = commandData.Application.MainWindowHandle; } catch { }
                LinkQueryForm.Open(owner);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log.Error("Interroga collegamento", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Scelta degli elementi dei collegamenti chiesta dalla finestra: in Revit si cliccano o si racchiudono in un
    /// riquadro quanti elementi si vuole e si conclude con «Fine» (o Invio); Esc annulla.
    /// </summary>
    internal sealed class LinkPickHandler : IExternalEventHandler
    {
        private static LinkPickHandler _instance;
        private static ExternalEvent _event;

        /// <summary>Da chiamare dentro un comando (contesto dell'API).</summary>
        public static void Init()
        {
            if (_event != null) return;
            _instance = new LinkPickHandler();
            _event = ExternalEvent.Create(_instance);
            LinkQueryForm.PickRequested = () => _event.Raise();
        }

        public string GetName() { return "ModelProof – interroga collegamento"; }

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null) return;
                IList<Reference> refs;
                try
                {
                    refs = uidoc.Selection.PickObjects(ObjectType.LinkedElement,
                        L.T("Scegli gli elementi dei modelli collegati (clic o riquadro), poi «Fine» – Esc per annullare"));
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return; }
                var picks = new List<LinkedPick>();
                foreach (Reference r in refs)
                {
                    LinkedPick p = Describe(uidoc.Document, r);
                    if (p != null) picks.Add(p);
                }
                Log.Write("Collegamenti", string.Format("interrogati {0} elementi: {1}", picks.Count,
                    string.Join(", ", picks.Take(50).Select(p => p.LinkFile + " #" + p.Id))));
                LinkQueryForm.AddPicks(picks);
            }
            catch (Exception ex) { Log.Error("Interroga collegamento", ex); }
        }

        private static LinkedPick Describe(Document doc, Reference r)
        {
            var li = doc.GetElement(r.ElementId) as RevitLinkInstance;
            Document ld = li != null ? li.GetLinkDocument() : null;
            Element e = ld != null ? ld.GetElement(r.LinkedElementId) : null;
            if (e == null) return null;
            var p = new LinkedPick { LinkFile = RevitUtil.FileName(ld), LinkInstanceId = li.Id.Value, Id = e.Id.Value };
            try { p.UniqueId = e.UniqueId ?? ""; } catch { }
            try { p.Category = e.Category != null ? e.Category.Name : ""; } catch { }
            var type = ld.GetElement(e.GetTypeId()) as ElementType;
            try
            {
                var fs = type as FamilySymbol;
                string fam = fs != null && fs.Family != null ? fs.Family.Name : type != null ? type.FamilyName : "";
                p.FamilyType = type == null ? (e.Name ?? "") : string.IsNullOrEmpty(fam) ? type.Name : fam + " : " + type.Name;
            }
            catch { }
            try
            {
                Level lv = e.LevelId != null && e.LevelId != ElementId.InvalidElementId ? ld.GetElement(e.LevelId) as Level : null;
                p.Level = lv != null ? lv.Name : "";
            }
            catch { }
            AddParameters(p, L.T("istanza"), e, ld);
            if (type != null) AddParameters(p, L.T("tipo"), type, ld);
            return p;
        }

        private static void AddParameters(LinkedPick p, string group, Element e, Document ld)
        {
            var rows = new List<string[]>();
            foreach (Parameter par in e.Parameters)
            {
                try
                {
                    string name = par.Definition != null ? par.Definition.Name : "";
                    if (name.Length == 0) continue;
                    rows.Add(new[] { group, name, RevitUtil.ValueOf(par, ld) ?? "" });
                }
                catch { }
            }
            p.Parameters.AddRange(rows.OrderBy(x => x[1], StringComparer.CurrentCultureIgnoreCase));
        }
    }
}
