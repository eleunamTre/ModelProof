using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace ModelProof
{
    /// <summary>
    /// Apre la finestra «Interroga collegamento» (resta aperta accanto a Revit). Dalla finestra si scelgono gli elementi
    /// dei modelli collegati (a clic, uno alla volta, o con un riquadro); per ciascuno si vedono ID, file, categoria, famiglia/tipo, livello
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
    /// Richieste della finestra verso Revit (eventi esterni, perché la finestra resta aperta accanto a Revit):
    /// scelta a clic (un elemento per clic, si aggiunge subito; Esc conclude), scelta a riquadro (poi «Fine»),
    /// evidenziazione nel modello delle righe selezionate e inquadratura nella vista attiva.
    /// </summary>
    internal sealed class LinkPickHandler : IExternalEventHandler
    {
        private enum Mode { Click, Box, Highlight, Zoom }

        private static ExternalEvent _pickEvent, _showEvent;
        private static LinkPickHandler _pick, _show;
        private Mode _mode;
        private List<LinkedPick> _targets = new List<LinkedPick>();

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>Da chiamare dentro un comando (contesto dell'API).</summary>
        public static void Init()
        {
            if (_pickEvent != null) return;
            _pick = new LinkPickHandler();
            _pickEvent = ExternalEvent.Create(_pick);
            _show = new LinkPickHandler();
            _showEvent = ExternalEvent.Create(_show);
            LinkQueryForm.PickRequested = box => { _pick._mode = box ? Mode.Box : Mode.Click; _pickEvent.Raise(); };
            LinkQueryForm.ShowRequested = (items, zoom) =>
            {
                _show._mode = zoom ? Mode.Zoom : Mode.Highlight;
                _show._targets = items.ToList();
                _showEvent.Raise();
            };
        }

        public string GetName() { return "ModelProof – interroga collegamento"; }

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null) return;
                switch (_mode)
                {
                    case Mode.Click: PickByClick(app, uidoc); break;
                    case Mode.Box: PickByBox(app, uidoc); break;
                    default: Show(uidoc, _targets, _mode == Mode.Zoom); break;
                }
            }
            catch (Exception ex) { Log.Error("Interroga collegamento", ex); }
        }

        /// <summary>Porta Revit in primo piano: senza, il primo clic serve solo ad attivare la finestra di Revit.</summary>
        private static void FocusRevit(UIApplication app)
        {
            try { SetForegroundWindow(app.MainWindowHandle); } catch { }
        }

        /// <summary>Un elemento per clic, aggiunto subito all'elenco; Esc (o clic destro → Annulla) conclude.</summary>
        private static void PickByClick(UIApplication app, UIDocument uidoc)
        {
            FocusRevit(app);
            int count = 0;
            while (true)
            {
                Reference r;
                try
                {
                    r = uidoc.Selection.PickObject(ObjectType.LinkedElement,
                        L.T("Clicca un elemento di un modello collegato (si aggiunge all'elenco) – Esc per finire"));
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; }
                LinkedPick p = Describe(uidoc.Document, r);
                if (p == null) continue;
                count++;
                LinkQueryForm.AddPicks(new List<LinkedPick> { p }, false);
            }
            Log.Write("Collegamenti", string.Format("interrogati {0} elementi a clic", count));
            LinkQueryForm.PickFinished();
        }

        /// <summary>Riquadro (e clic) come la selezione multipla di Revit, poi «Fine» o Invio; Esc annulla.</summary>
        private static void PickByBox(UIApplication app, UIDocument uidoc)
        {
            FocusRevit(app);
            IList<Reference> refs;
            try
            {
                refs = uidoc.Selection.PickObjects(ObjectType.LinkedElement,
                    L.T("Racchiudi in un riquadro gli elementi dei modelli collegati, poi «Fine» – Esc per annullare"));
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { LinkQueryForm.PickFinished(); return; }
            var picks = new List<LinkedPick>();
            foreach (Reference r in refs)
            {
                LinkedPick p = Describe(uidoc.Document, r);
                if (p != null) picks.Add(p);
            }
            Log.Write("Collegamenti", string.Format("interrogati {0} elementi: {1}", picks.Count,
                string.Join(", ", picks.Take(50).Select(p => p.LinkFile + " #" + p.Id))));
            LinkQueryForm.AddPicks(picks, true);
            LinkQueryForm.PickFinished();
        }

        /// <summary>
        /// Seleziona in Revit gli elementi dei collegamenti delle righe scelte (si vedono evidenziati nelle viste);
        /// con zoom inquadra anche il loro ingombro nella vista attiva.
        /// </summary>
        private static void Show(UIDocument uidoc, List<LinkedPick> items, bool zoom)
        {
            Document doc = uidoc.Document;
            var refs = new List<Reference>();
            XYZ min = null, max = null;
            foreach (LinkedPick p in items)
            {
                try
                {
                    var li = doc.GetElement(new ElementId(p.LinkInstanceId)) as RevitLinkInstance;
                    Document ld = li != null ? li.GetLinkDocument() : null;
                    Element e = ld != null ? ld.GetElement(new ElementId(p.Id)) : null;
                    if (e == null) continue;
                    Reference r = new Reference(e).CreateLinkReference(li);
                    if (r != null) refs.Add(r);
                    if (!zoom) continue;
                    BoundingBoxXYZ bb = e.get_BoundingBox(null);
                    if (bb == null) continue;
                    Transform t = li.GetTotalTransform();
                    foreach (XYZ c in Corners(bb))
                    {
                        XYZ w = t.OfPoint(c);
                        min = min == null ? w : new XYZ(Math.Min(min.X, w.X), Math.Min(min.Y, w.Y), Math.Min(min.Z, w.Z));
                        max = max == null ? w : new XYZ(Math.Max(max.X, w.X), Math.Max(max.Y, w.Y), Math.Max(max.Z, w.Z));
                    }
                }
                catch (Exception ex) { Log.Error("Evidenzia collegamento", ex); }
            }
            try { uidoc.Selection.SetReferences(refs); }
            catch (Exception ex) { Log.Error("Evidenzia collegamento", ex); }
            if (!zoom || min == null) return;
            try
            {
                UIView view = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == uidoc.ActiveView.Id);
                if (view == null) return;
                XYZ pad = (max - min) * 0.15 + new XYZ(1, 1, 1);
                view.ZoomAndCenterRectangle(min - pad, max + pad);
            }
            catch (Exception ex) { Log.Error("Inquadra collegamento", ex); }
        }

        private static IEnumerable<XYZ> Corners(BoundingBoxXYZ bb)
        {
            Transform t = bb.Transform ?? Transform.Identity;
            foreach (double x in new[] { bb.Min.X, bb.Max.X })
                foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                    foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                        yield return t.OfPoint(new XYZ(x, y, z));
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
