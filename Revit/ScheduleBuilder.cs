using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ModelProof
{
    /// <summary>
    /// Crea nel modello gli abachi descritti nelle sezioni [ABACO nome] del file di configurazione, per il controllo a
    /// mano (come lo script Dynamo «Abaco check IFC»). Unico comando del plugin che modifica il modello: chiede conferma
    /// e lavora in una sola transazione, annullabile con Ctrl+Z.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CreateSchedulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            IntPtr owner = IntPtr.Zero;
            try { owner = commandData.Application.MainWindowHandle; } catch { }
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;
                string path = Settings.Current.ConfigPath;
                ProjectConfig cfg = null;
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    try { cfg = ProjectConfig.Load(path); }
                    catch (Exception ex) { Log.Error("Configurazione", ex); }
                }
                if (cfg == null || cfg.ErrorCount > 0)
                {
                    TaskDialog.Show("ModelProof", cfg == null
                        ? L.T("Prima scegli il file di configurazione con «Regole del progetto».")
                        : L.F("Il file di configurazione ha {0} errori: correggili prima.", cfg.ErrorCount));
                    ConfigForm.ShowWindow(owner);
                    return Result.Succeeded;
                }
                if (cfg.Schedules.Count == 0)
                {
                    TaskDialog.Show("ModelProof", L.T("Il file di configurazione non ha abachi: aggiungi una sezione [ABACO nome]."));
                    return Result.Succeeded;
                }

                var ask = new TaskDialog("ModelProof")
                {
                    MainInstruction = L.F("Creare {0} abachi nel modello?", cfg.Schedules.Count),
                    MainContent = string.Join("\n", cfg.Schedules.Select(a => "• " + a.Name)) + "\n\n" +
                                  L.T("Gli abachi con lo stesso nome vengono sostituiti (se «sovrascrivi = si»). Si annulla con Ctrl+Z."),
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.Yes
                };
                if (ask.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                var log = new List<string>();
                ViewSchedule first = null;
                using (var t = new Transaction(doc, L.T("ModelProof – abachi di controllo")))
                {
                    t.Start();
                    foreach (ScheduleSpec a in cfg.Schedules)
                    {
                        try
                        {
                            ViewSchedule v = ScheduleBuilder.Create(doc, a, log);
                            if (first == null && v != null) first = v;
                        }
                        catch (Exception ex)
                        {
                            log.Add(L.F("[{0}] errore: {1}", a.Name, ex.Message));
                            Log.Error("Abaco " + a.Name, ex);
                        }
                    }
                    t.Commit();
                }
                foreach (string l in log) Log.Write("Abachi", l);
                if (first != null)
                    try { commandData.Application.ActiveUIDocument.ActiveView = first; } catch { }
                TaskDialog.Show("ModelProof", string.Join("\n", log));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log.Error("Abachi", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    internal static class ScheduleBuilder
    {
        /// <summary>Parametri di Revit di riserva per i nomi di campo più comuni (se il nome non si trova, es. Revit in italiano).</summary>
        private static readonly Dictionary<string, string> KnownFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Category", "ELEM_CATEGORY_PARAM" }, { "Family", "ELEM_FAMILY_PARAM" }, { "Type", "ELEM_TYPE_PARAM" },
            { "Family and Type", "ELEM_FAMILY_AND_TYPE_PARAM" }, { "Type Mark", "ALL_MODEL_TYPE_MARK" }, { "Mark", "ALL_MODEL_MARK" },
            { "Export Type to IFC As", "IFC_EXPORT_ELEMENT_TYPE_AS" }, { "Type IFC Predefined Type", "IFC_TYPE_EXPORT_PREDEFINEDTYPE" },
            { "Export to IFC As", "IFC_EXPORT_ELEMENT_AS" }, { "IFC Predefined Type", "IFC_EXPORT_PREDEFINEDTYPE" },
            { "Material: Mark", "MATERIAL_MARK" }, { "Material: Name", "MATERIAL_NAME" },
            { "Level", "ROOM_LEVEL_ID" }, { "Number", "ROOM_NUMBER" }, { "Name", "ROOM_NAME" }, { "Count", "" }
        };

        public static ViewSchedule Create(Document doc, ScheduleSpec a, List<string> log)
        {
            ViewSchedule existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .FirstOrDefault(s => string.Equals(s.Name, a.Name, StringComparison.Ordinal));
            if (existing != null)
            {
                if (!a.Overwrite) { log.Add(L.F("[{0}] esiste già: non toccato.", a.Name)); return existing; }
                doc.Delete(existing.Id);
            }

            ElementId cat = ElementId.InvalidElementId;
            if (a.Kind == "categoria")
            {
                List<long> ids = RevitUtil.CategoryIds(a.Category);
                if (ids.Count > 0) cat = new ElementId(ids[0]);
                else log.Add(L.F("[{0}] categoria «{1}» sconosciuta: abaco multicategoria.", a.Name, a.Category));
            }
            ViewSchedule v = a.Kind == "computo materiali" ? ViewSchedule.CreateMaterialTakeoff(doc, cat) : ViewSchedule.CreateSchedule(doc, cat);
            v.Name = a.Name;
            ScheduleDefinition def = v.Definition;
            IList<SchedulableField> available = def.GetSchedulableFields();

            var added = new Dictionary<string, ScheduleFieldId>(StringComparer.OrdinalIgnoreCase);
            var usedParams = new HashSet<long>();
            var explicitNames = new HashSet<string>(a.Fields.Where(f => !f.EndsWith("*")), StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();
            foreach (string f in a.Fields)
            {
                if (f.EndsWith("*"))
                {
                    // tutti i campi che iniziano con il prefisso (maiuscole e minuscole contano), in ordine alfabetico
                    string prefix = f.Substring(0, f.Length - 1);
                    var matches = available.Select(sf => new { sf, name = SafeName(sf, doc) })
                        .Where(x => x.name.StartsWith(prefix, StringComparison.Ordinal) && !explicitNames.Contains(x.name))
                        .OrderBy(x => x.name, StringComparer.CurrentCultureIgnoreCase).ToList();
                    if (matches.Count == 0) log.Add(L.F("[{0}] nessun campo che inizia con «{1}».", a.Name, prefix));
                    foreach (var x in matches) AddField(def, x.sf, x.name, added, usedParams);
                    continue;
                }
                SchedulableField found = Find(available, doc, f);
                if (found == null) { missing.Add(f); continue; }
                AddField(def, found, f, added, usedParams);
            }

            def.IsItemized = a.Itemize;
            def.ShowGrandTotal = a.GrandTotal;
            foreach (ScheduleSort o in a.Sorts)
            {
                ScheduleFieldId id;
                if (!added.TryGetValue(o.Field, out id)) { log.Add(L.F("[{0}] ordinamento per «{1}»: campo non aggiunto.", a.Name, o.Field)); continue; }
                var sg = new ScheduleSortGroupField(id, o.Descending ? ScheduleSortOrder.Descending : ScheduleSortOrder.Ascending)
                {
                    ShowHeader = o.Header,
                    ShowFooter = o.Footer,
                    ShowBlankLine = o.Blank
                };
                if (o.Footer) { sg.ShowFooterTitle = true; sg.ShowFooterCount = true; }
                def.AddSortGroupField(sg);
            }
            log.Add(missing.Count == 0
                ? L.F("[{0}] creato: {1} campi.", a.Name, added.Count)
                : L.F("[{0}] creato: {1} campi; non trovati: {2}.", a.Name, added.Count, string.Join(", ", missing)));
            return v;
        }

        private static string SafeName(SchedulableField sf, Document doc)
        {
            try { return sf.GetName(doc) ?? ""; } catch { return ""; }
        }

        private static SchedulableField Find(IList<SchedulableField> available, Document doc, string name)
        {
            SchedulableField byName = available.FirstOrDefault(sf => string.Equals(SafeName(sf, doc), name, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
            string bipName;
            BuiltInParameter bip;
            if (KnownFields.TryGetValue(name, out bipName) && bipName.Length > 0 && Enum.TryParse(bipName, out bip))
            {
                long id = (long)bip;
                return available.FirstOrDefault(sf => sf.ParameterId != null && sf.ParameterId.Value == id);
            }
            return null;
        }

        private static void AddField(ScheduleDefinition def, SchedulableField sf, string name, Dictionary<string, ScheduleFieldId> added, HashSet<long> usedParams)
        {
            long key = sf.ParameterId != null ? sf.ParameterId.Value : long.MinValue;
            if (key != long.MinValue && key != ElementId.InvalidElementId.Value && !usedParams.Add(key)) return;   // campo già aggiunto
            ScheduleField field = def.AddField(sf);
            if (!added.ContainsKey(name)) added[name] = field.FieldId;
        }
    }
}
