using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ModelProof
{
    /// <summary>
    /// Controlli già pronti sugli elenchi del file di configurazione: parametri condivisi, codice di categoria,
    /// funzione ammessa, categoria ammessa per il modello, funzione dei muri. Gli elenchi, le colonne e le parti del
    /// nome si leggono dalla regola (chiavi «elenco», «colonna_…», «parte_…»): il codice non conosce i nomi del progetto.
    /// </summary>
    internal sealed partial class Checker
    {
        // ------------------------------------------------------------------ utilità comuni

        private CodeList RuleList(Rule rule, RuleResult rr)
        {
            CodeList list;
            if (rule.ListName.Length > 0 && _cfg.Lists.TryGetValue(rule.ListName, out list)) return list;
            rr.Note = L.T("manca l'elenco della regola («elenco = NOME»)");
            return null;
        }

        private static int Column(Rule rule, string key, int fallback)
        {
            int n;
            return int.TryParse(rule.Get(key).Trim(), out n) && n >= 1 ? n : fallback;
        }

        private static string Part(Rule rule, string key, string fallback)
        {
            string p = rule.Get(key).Trim();
            return p.Length > 0 ? p : fallback;
        }

        /// <summary>
        /// Nome da cui si leggono le parti di un tipo: per i caricabili il nome della famiglia, per i tipi di sistema
        /// il nome del tipo, letto con il formato della regola corrispondente. Match vuoto se il nome non rispetta il
        /// formato (l'errore lo segnala già quella regola).
        /// </summary>
        private Match TypeNameMatch(ElementType type, out string text)
        {
            bool loadable = RevitUtil.IsLoadable(type);
            Rule parser = loadable ? _cfg.FindParser("famiglie caricabili", "Nome famiglia") : _cfg.FindParser("tipi di sistema", "Nome tipo");
            text = loadable ? FamilyNameOf(type) : type.Name;
            if (parser == null || parser.Regex == null || string.IsNullOrEmpty(text)) return Match.Empty;
            return Parse(parser, text);
        }

        private Category CategoryOf(ElementType type, long typeId)
        {
            try
            {
                if (type.Category != null) return type.Category;
                long first;
                if (_typeFirstInstance.TryGetValue(typeId, out first))
                {
                    Element e = _doc.GetElement(new ElementId(first));
                    if (e != null) return e.Category;
                }
            }
            catch { }
            return null;
        }

        /// <summary>La categoria Revit corrisponde al nome inglese dell'elenco (per id, o per nome con Revit in inglese).</summary>
        private static bool SameCategory(Category c, string englishName)
        {
            if (c == null || string.IsNullOrWhiteSpace(englishName)) return false;
            List<long> ids = RevitUtil.CategoryIds(englishName);
            long id = c.Id.Value;
            if (ids.Contains(id)) return true;
            return string.Equals(c.Name, englishName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private Issue TypeIssue(Rule rule, bool error, Target t, string field, string value, string message)
        {
            return MakeIssue(rule, t, error, IssueKind.Controllo, field, value, message);
        }

        // ------------------------------------------------------------------ parametri condivisi

        /// <summary>
        /// Parametri dell'elenco: presenti nel modello come parametri condivisi (OK), presenti solo come parametri di
        /// progetto non condivisi (errore), assenti (secondo «se_assente»). Parametri condivisi assegnati alle
        /// categorie del progetto ma non in elenco: avviso.
        /// </summary>
        private void CheckSharedParameters(Rule rule, RuleResult rr)
        {
            bool error = rule.Severity == "errore";
            CodeList list = RuleList(rule, rr);
            if (list == null) return;

            var shared = new Dictionary<string, List<SharedParameterElement>>(StringComparer.Ordinal);
            foreach (SharedParameterElement sp in new FilteredElementCollector(_doc).OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>())
            {
                string n = (sp.Name ?? "").Trim();
                List<SharedParameterElement> l;
                if (!shared.TryGetValue(n, out l)) shared[n] = l = new List<SharedParameterElement>();
                l.Add(sp);
            }

            // parametri di progetto (assegnati alle categorie): condivisi o no
            var boundShared = new HashSet<string>(StringComparer.Ordinal);
            var boundProject = new HashSet<string>(StringComparer.Ordinal);
            DefinitionBindingMapIterator it = _doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext())
            {
                var def = it.Key as InternalDefinition;
                if (def == null) continue;
                string n = (def.Name ?? "").Trim();
                bool isShared = false;
                try { isShared = _doc.GetElement(def.Id) is SharedParameterElement; } catch { }
                if (isShared) boundShared.Add(n); else boundProject.Add(n);
            }

            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in list.Codes(1))
            {
                string n = name.Trim();
                if (n.Length == 0 || !wanted.Add(n)) continue;
                rr.Checked++;
                List<SharedParameterElement> found;
                if (shared.TryGetValue(n, out found))
                {
                    if (found.Count > 1)
                        rr.Issues.Add(ControlIssue(rule, false, L.T("Parametro"), n,
                            L.F("{0} parametri condivisi con lo stesso nome (GUID diversi: {1})", found.Count, string.Join(", ", found.Select(f => f.GuidValue.ToString())))));
                    else
                        rr.Issues.Add(OkRow(rule, _info.FileName, _info.RoleText, L.T("Parametro"), n,
                            boundShared.Contains(n) ? L.T("condiviso, assegnato alle categorie") : L.T("condiviso (nelle famiglie)")));
                }
                else if (boundProject.Contains(n))
                    rr.Issues.Add(ControlIssue(rule, error, L.T("Parametro"), n, L.T("parametro di progetto NON condiviso")));
                else if (rule.Absent != "ignora")
                    rr.Issues.Add(ControlIssue(rule, error && rule.Absent == "errore", L.T("Parametro"), n, L.T("parametro dell'elenco assente nel modello")));
            }
            foreach (string n in boundShared.Where(x => !wanted.Contains(x)).OrderBy(x => x, StringComparer.Ordinal))
                rr.Issues.Add(ControlIssue(rule, false, L.T("Parametro"), n, L.F("parametro condiviso del progetto non presente nell'elenco [{0}]", list.Name)));
        }

        // ------------------------------------------------------------------ codice di categoria e funzione

        /// <summary>Elenco delle categorie: codice → nomi delle categorie Revit, e nome della categoria → righe.</summary>
        private sealed class CategoryTable
        {
            public readonly Dictionary<string, List<string>> ByCode = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            public readonly List<string[]> Rows = new List<string[]>();   // codice, categoria, discipline
        }

        private CategoryTable ReadCategories(Rule rule, CodeList list)
        {
            int cCode = Column(rule, "colonna_codice", 1), cCat = Column(rule, "colonna_categoria", 2), cDisc = Column(rule, "colonna_discipline", 4);
            var t = new CategoryTable();
            foreach (ConfigRow r in list.Rows)
            {
                string code = r.Cell(cCode - 1).Trim(), cat = r.Cell(cCat - 1).Trim();
                if (code.Length == 0 || cat.Length == 0) continue;
                List<string> l;
                if (!t.ByCode.TryGetValue(code, out l)) t.ByCode[code] = l = new List<string>();
                if (!l.Contains(cat)) l.Add(cat);
                t.Rows.Add(new[] { code, cat, r.Cell(cDisc - 1).Trim() });
            }
            return t;
        }

        /// <summary>Codice di categoria del nome (CAT) coerente con la categoria Revit del tipo.</summary>
        private void CheckCategoryCode(Rule rule, RuleResult rr)
        {
            bool error = rule.Severity == "errore";
            CodeList list = RuleList(rule, rr);
            if (list == null) return;
            CategoryTable table = ReadCategories(rule, list);
            string part = Part(rule, "parte_categoria", "CAT");
            foreach (Target t in Targets("tipi"))
            {
                string text;
                Match m = TypeNameMatch(t.Type, out text);
                if (!m.Success || !m.Groups[part].Success) continue;   // nome fuori formato: lo segnala la regola del nome
                rr.Checked++;
                string code = m.Groups[part].Value;
                List<string> cats;
                Category c = CategoryOf(t.Type, t.Id);
                if (!table.ByCode.TryGetValue(code, out cats))
                    rr.Issues.Add(TypeIssue(rule, error, t, part, code, L.F("codice «{0}» non nell'elenco [{1}]", code, list.Name)));
                else if (c != null && !cats.Any(n => SameCategory(c, n)))
                    rr.Issues.Add(TypeIssue(rule, error, t, part, code,
                        L.F("«{0}» indica {1}, ma la categoria Revit è {2}", code, string.Join(" / ", cats), c.Name)));
                else
                    rr.Issues.Add(MakeOkFor(rule, t, part, code, string.Join(" / ", cats)));
            }
        }

        /// <summary>Funzione (fu) del nome tra quelle ammesse per il codice di categoria; categoria senza funzioni = libera.</summary>
        private void CheckAllowedFunction(Rule rule, RuleResult rr)
        {
            bool error = rule.Severity == "errore";
            CodeList list = RuleList(rule, rr);
            if (list == null) return;
            int cCode = Column(rule, "colonna_codice", 1), cFun = Column(rule, "colonna_funzione", 2);
            var allowed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (ConfigRow r in list.Rows)
            {
                string code = r.Cell(cCode - 1).Trim(), fu = r.Cell(cFun - 1).Trim();
                if (code.Length == 0 || fu.Length == 0) continue;
                HashSet<string> s;
                if (!allowed.TryGetValue(code, out s)) allowed[code] = s = new HashSet<string>(StringComparer.Ordinal);
                s.Add(fu);
            }
            string pCat = Part(rule, "parte_categoria", "CAT"), pFun = Part(rule, "parte_funzione", "fu");
            foreach (Target t in Targets("tipi"))
            {
                string text;
                Match m = TypeNameMatch(t.Type, out text);
                if (!m.Success || !m.Groups[pCat].Success || !m.Groups[pFun].Success) continue;
                rr.Checked++;
                string code = m.Groups[pCat].Value, fu = m.Groups[pFun].Value;
                HashSet<string> ok;
                if (!allowed.TryGetValue(code, out ok))
                    rr.Issues.Add(MakeOkFor(rule, t, pFun, code + "." + fu, L.F("nessuna funzione in elenco per {0}: libera", code)));
                else if (!ok.Contains(fu))
                    rr.Issues.Add(TypeIssue(rule, error, t, pFun, code + "." + fu,
                        L.F("funzione «{0}» non ammessa per {1} (ammesse: {2})", fu, code, string.Join(", ", ok.OrderBy(x => x, StringComparer.Ordinal)))));
                else
                    rr.Issues.Add(MakeOkFor(rule, t, pFun, code + "." + fu, ""));
            }
        }

        /// <summary>Categoria di ogni tipo usato ammessa per le discipline del modello (chiave «RUOLO.discipline»).</summary>
        private void CheckAllowedCategory(Rule rule, RuleResult rr)
        {
            bool error = rule.Severity == "errore";
            CodeList list = RuleList(rule, rr);
            if (list == null) return;
            ConfigSection models = _cfg.File.Find("MODELLI");
            List<string> mine = ProjectConfig.SplitList(models != null && _info.Role != null ? models.Get(_info.Role + ".discipline") : "", ',');
            if (mine.Count == 0)
            {
                rr.Note = L.F("discipline del modello {0} non indicate in [MODELLI]", _info.RoleText);
                return;
            }
            CategoryTable table = ReadCategories(rule, list);
            foreach (Target t in Targets("tipi"))
            {
                Category c = CategoryOf(t.Type, t.Id);
                if (c == null) continue;
                rr.Checked++;
                List<string[]> rows = table.Rows.Where(r => SameCategory(c, r[1])).ToList();
                if (rows.Count == 0)
                {
                    rr.Issues.Add(TypeIssue(rule, false, t, L.T("Categoria"), c.Name, L.F("categoria non presente nell'elenco [{0}]", list.Name)));
                    continue;
                }
                var disc = new HashSet<string>(rows.SelectMany(r => ProjectConfig.SplitList(r[2], ',')), StringComparer.OrdinalIgnoreCase);
                if (disc.Overlaps(mine))
                    rr.Issues.Add(MakeOkFor(rule, t, L.T("Categoria"), c.Name, string.Join(", ", disc.OrderBy(x => x))));
                else
                    rr.Issues.Add(TypeIssue(rule, error, t, L.T("Categoria"), c.Name,
                        L.F("categoria non ammessa per il modello {0} ({1}): ammessa per {2}", _info.RoleText, string.Join(", ", mine), string.Join(", ", disc.OrderBy(x => x)))));
            }
        }

        private Issue MakeOkFor(Rule rule, Target t, string field, string value, string message)
        {
            Issue i = MakeIssue(rule, t, false, IssueKind.Controllo, field, value, message.Length > 0 ? message : L.T("conforme"));
            i.Ok = true;
            return i;
        }

        // ------------------------------------------------------------------ funzione dei muri

        /// <summary>
        /// Muri con funzione esterna o interna nel nome (chiavi «esterne» e «interne», es. «ex ; re»): la Function di
        /// Revit del tipo deve concordare (errore, una volta per tipo); i locali ai due lati del muro, se si trovano,
        /// devono concordare (avviso per muro: un muro esterno con locali da entrambi i lati, o interno con un locale
        /// solo da un lato).
        /// </summary>
        private void CheckWallFunction(Rule rule, RuleResult rr)
        {
            bool error = rule.Severity == "errore";
            var outside = new HashSet<string>(ProjectConfig.SplitList(rule.Get("esterne"), ';'), StringComparer.Ordinal);
            var inside = new HashSet<string>(ProjectConfig.SplitList(rule.Get("interne"), ';'), StringComparer.Ordinal);
            if (outside.Count == 0 && inside.Count == 0)
            {
                rr.Note = L.T("mancano «esterne» e «interne» (funzioni del nome da controllare)");
                return;
            }
            string pFun = Part(rule, "parte_funzione", "fu");
            Phase phase = LastPhase();
            var typeDone = new HashSet<long>();
            foreach (Wall w in _instances.OfType<Wall>())
            {
                var type = _doc.GetElement(w.GetTypeId()) as WallType;
                if (type == null) continue;
                string text;
                Match m = TypeNameMatch(type, out text);
                if (!m.Success || !m.Groups[pFun].Success) continue;
                string fu = m.Groups[pFun].Value;
                bool ext = outside.Contains(fu), inn = inside.Contains(fu);
                if (!ext && !inn) continue;
                rr.Checked++;
                var t = new Target { El = w, Type = type, Id = w.Id.Value, ShowId = w.Id.Value, Category = CatName(w), FamilyType = FamType(type) };

                // Function del tipo (una volta per tipo)
                if (typeDone.Add(type.Id.Value))
                {
                    WallFunction f = WallFunction.Interior;
                    bool known = false;
                    try { f = type.Function; known = true; } catch { }
                    if (known)
                    {
                        bool good = ext ? f == WallFunction.Exterior : f == WallFunction.Interior;
                        var tt = new Target { El = type, Type = type, Id = type.Id.Value, ShowId = w.Id.Value, Category = CatName(w), FamilyType = FamType(type) };
                        if (good) rr.Issues.Add(MakeOkFor(rule, tt, "Function", f.ToString(), L.F("«{0}» nel nome", fu)));
                        else rr.Issues.Add(TypeIssue(rule, error, tt, "Function", f.ToString(),
                            L.F("«{0}» nel nome indica un muro {1}, ma la Function del tipo è {2}", fu, ext ? L.T("esterno") : L.T("interno"), f)));
                    }
                }

                // locali ai due lati
                int sides = RoomSides(w, phase);
                if (sides < 0) continue;   // geometria non leggibile
                string found = L.F("locali trovati: {0} lati su 2", sides);
                if (ext && sides == 2)
                    rr.Issues.Add(TypeIssue(rule, false, t, L.T("Locali ai lati"), "2", L.F("muro esterno («{0}») con locali da entrambi i lati", fu)));
                else if (inn && sides == 1)
                    rr.Issues.Add(TypeIssue(rule, false, t, L.T("Locali ai lati"), "1", L.F("muro interno («{0}») con un locale da un solo lato", fu)));
                else
                    rr.Issues.Add(MakeOkFor(rule, t, L.T("Locali ai lati"), sides.ToString(), found));
            }
        }

        private readonly Dictionary<long, List<Room>> _roomCache = new Dictionary<long, List<Room>>();

        /// <summary>
        /// Locali che contengono l'elemento: per porte e finestre i locali dai due lati, per le altre famiglie il loro
        /// locale, per gli altri elementi il locale al centro dell'ingombro (o appena sopra/sotto). Vuoto se nessuno.
        /// </summary>
        private List<Room> RoomsOf(Element e)
        {
            List<Room> list;
            if (_roomCache.TryGetValue(e.Id.Value, out list)) return list;
            list = new List<Room>();
            try
            {
                var fi = e as FamilyInstance;
                if (fi != null)
                {
                    foreach (Room r in new[] { fi.Room, fi.ToRoom, fi.FromRoom })
                        if (r != null && !list.Any(x => x.Id == r.Id)) list.Add(r);
                }
                if (list.Count == 0)
                {
                    BoundingBoxXYZ bb = e.get_BoundingBox(null);
                    if (bb != null)
                    {
                        XYZ c = (bb.Min + bb.Max) / 2.0;
                        foreach (double z in new[] { c.Z, bb.Max.Z + 0.3, bb.Min.Z + 0.3 })
                        {
                            Room r = _doc.GetRoomAtPoint(new XYZ(c.X, c.Y, z));
                            if (r != null) { list.Add(r); break; }
                        }
                    }
                }
            }
            catch { }
            _roomCache[e.Id.Value] = list;
            return list;
        }

        private Phase LastPhase()
        {
            try
            {
                PhaseArray phases = _doc.Phases;
                return phases.Size > 0 ? phases.get_Item(phases.Size - 1) : null;
            }
            catch { return null; }
        }

        /// <summary>Lati del muro (0, 1 o 2) che hanno un locale, a metà lunghezza e a 1 m dalla base; -1 se non leggibile.</summary>
        private int RoomSides(Wall w, Phase phase)
        {
            try
            {
                var lc = w.Location as LocationCurve;
                BoundingBoxXYZ bb = w.get_BoundingBox(null);
                if (lc == null || lc.Curve == null || bb == null) return -1;
                XYZ mid = lc.Curve.Evaluate(0.5, true);
                XYZ n = w.Orientation;
                double off = w.Width / 2.0 + 1.0;                                 // oltre lo spessore, 1 piede
                double z = bb.Min.Z + Math.Min(1.0 / 0.3048, (bb.Max.Z - bb.Min.Z) / 2.0);
                var p = new XYZ(mid.X, mid.Y, z);
                int sides = 0;
                foreach (double s in new[] { 1.0, -1.0 })
                {
                    XYZ q = p + n * (off * s);
                    Room r = phase != null ? _doc.GetRoomAtPoint(q, phase) : _doc.GetRoomAtPoint(q);
                    if (r != null) sides++;
                }
                return sides;
            }
            catch { return -1; }
        }
    }
}
