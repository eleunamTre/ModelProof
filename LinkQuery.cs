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
    /// Interroga elementi dei modelli collegati senza aprirli: si cliccano uno o più elementi dei collegamenti
    /// (Esc per finire) e una finestra mostra file, ID, categoria, famiglia/tipo, livello e tutti i parametri,
    /// con la copia degli ID. Sola lettura.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class LinkQueryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            Document doc = uidoc.Document;
            var picks = new List<LinkedPick>();
            try
            {
                while (true)
                {
                    Reference r;
                    try
                    {
                        r = uidoc.Selection.PickObject(ObjectType.LinkedElement,
                            L.F("Clicca un elemento di un modello collegato ({0} scelti) – Esc per finire", picks.Count));
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; }
                    LinkedPick p = Describe(doc, r);
                    if (p != null && !picks.Any(x => x.LinkInstanceId == p.LinkInstanceId && x.Id == p.Id)) picks.Add(p);
                }
                if (picks.Count == 0) return Result.Cancelled;
                Log.Write("Collegamenti", string.Format("interrogati {0} elementi: {1}", picks.Count,
                    string.Join(", ", picks.Select(p => p.LinkFile + " #" + p.Id))));
                IntPtr owner = IntPtr.Zero;
                try { owner = commandData.Application.MainWindowHandle; } catch { }
                LinkQueryForm.ShowPicks(picks, owner);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log.Error("Interroga collegamento", ex);
                message = ex.Message;
                return Result.Failed;
            }
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
