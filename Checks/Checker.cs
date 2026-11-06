using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RxGroup = System.Text.RegularExpressions.Group;

namespace ModelProof
{
    /// <summary>Oggetto controllato da una regola di campi (livello, tipo, famiglia, istanza, locale, materiale…).</summary>
    internal sealed class Target
    {
        public Element El;             // elemento da cui si leggono i parametri
        public ElementType Type;       // tipo collegato (per le istanze il loro tipo, per i tipi se stessi)
        public Level Level;            // livello di appartenenza
        public List<Level> Levels;     // più livelli (tipi di sistema impiantistico: livelli degli elementi che li usano)
        public long Id;
        public long ShowId;
        public string Category = "";
        public string FamilyType = "";
    }

    /// <summary>Modello collegato riconosciuto dal nome del file.</summary>
    internal sealed class LinkedModel
    {
        public RevitLinkInstance Instance;
        public Document Doc;
        public ModelInfo Info;
    }

    /// <summary>
    /// Verifica del modello aperto con le regole del file di configurazione. Solo lettura: non modifica il modello.
    /// Le regole di campi sono generiche (oggetto + campo + formato + coerenza + numerazione); i «controlli» sono
    /// scritti qui (coordinate, livelli, …).
    /// </summary>
    internal sealed class Checker
    {
        private readonly Document _doc;
        private readonly ProjectConfig _cfg;
        private ModelInfo _info;
        private Exclusions _excl;

        private List<Element> _instances;
        private readonly Dictionary<long, ElementType> _types = new Dictionary<long, ElementType>();
        private readonly Dictionary<long, long> _typeFirstInstance = new Dictionary<long, long>();
        private List<Level> _levels;
        private List<LinkedModel> _links;
        private readonly List<string> _linkNotes = new List<string>();
        private readonly Dictionary<string, Match> _parseCache = new Dictionary<string, Match>(StringComparer.Ordinal);

        public Checker(Document doc, ProjectConfig cfg)
        {
            _doc = doc;
            _cfg = cfg;
        }

        // ================================================================== riconoscimento del modello

        /// <summary>Ruolo (URS, ARC, IMP), codice edificio e descrizione letti dai campi del nome del file.</summary>
        public static ModelInfo Identify(string fileName, ProjectConfig cfg)
        {
            var info = new ModelInfo { FileName = fileName };
            ConfigSection s = cfg.File.Find("MODELLI");
            if (s == null) return info;
            string[] fields = fileName.Split('_');
            int fEdif, fRole;
            int.TryParse(s.Get("campo_edificio", "1"), out fEdif);
            int.TryParse(s.Get("campo_ruolo", "4"), out fRole);
            if (fEdif >= 1 && fEdif <= fields.Length) info.Building = fields[fEdif - 1].Trim();
            info.Description = fields.Length > 0 ? fields[fields.Length - 1].Trim() : "";
            if (fRole >= 1 && fRole <= fields.Length)
            {
                string code = fields[fRole - 1].Trim();
                foreach (string role in ProjectConfig.Roles)
                    if (string.Equals(s.Get(role + ".codice"), code, StringComparison.OrdinalIgnoreCase)) { info.Role = role; break; }
            }
            if (info.Role != null)
            {
                string expected = s.Get(info.Role + ".descrizione");
                info.DescriptionOk = expected.Length == 0 || string.Equals(expected, info.Description, StringComparison.OrdinalIgnoreCase);
            }
            return info;
        }

        // ================================================================== verifica

        public CheckRun Run()
        {
            var total = Stopwatch.StartNew();
            var run = new CheckRun { ConfigPath = _cfg.File.Path, DocumentKey = DocumentKey(_doc), PartialThreshold = _cfg.PartialThreshold };
            _info = Identify(RevitUtil.FileName(_doc), _cfg);
            run.Model = _info;
            if (_info.Role == null)
                run.Notes.Add(L.F("Ruolo del modello non riconosciuto dal nome del file «{0}»: le regole per URS, ARC e IMP non si applicano.", _info.FileName));
            else if (!_info.DescriptionOk)
                run.Notes.Add(L.F("Il nome del file indica il modello {0} ma la descrizione finale è «{1}».", _info.Role, _info.Description));

            var sw = Stopwatch.StartNew();
            Collect();
            run.Notes.AddRange(_linkNotes);
            Log.Write("Verifica", string.Format("{0} ({1}, edificio {2}): {3} istanze, {4} tipi usati, {5} livelli, {6} collegamenti riconosciuti, raccolta {7} ms",
                _info.FileName, _info.RoleText, _info.Building ?? "?", _instances.Count, _types.Count, _levels.Count, _links.Count, sw.ElapsedMilliseconds));

            foreach (Rule rule in _cfg.Rules)
            {
                var rr = new RuleResult { Rule = rule };
                run.Results.Add(rr);
                if (!rule.AppliesTo(_info.Role))
                {
                    rr.Skipped = true;
                    rr.Note = L.F("non si applica al modello {0}", _info.RoleText);
                    continue;
                }
                sw.Restart();
                try
                {
                    if (rule.IsControl) RunControl(rule, rr);
                    else RunFieldRule(rule, rr);
                }
                catch (Exception ex)
                {
                    Log.Error("Verifica " + rule.Name, ex);
                    rr.Note = L.F("interrotta da un errore: {0}", ex.Message);
                }
                Log.Write("Verifica", string.Format("{0}: {1} controllati, {2} errori, {3} avvisi, {4} ms{5}",
                    rule.Name, rr.Checked, rr.Errors, rr.Warnings, sw.ElapsedMilliseconds, rr.Note != null ? " (" + rr.Note + ")" : ""));
            }
            run.Seconds = total.Elapsed.TotalSeconds;
            Log.Write("Verifica", string.Format("{0}: {1} errori, {2} avvisi in {3:F1} s", _info.FileName, run.Errors, run.Warnings, run.Seconds));
            return run;
        }

        public static string DocumentKey(Document doc)
        {
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return doc.Title; }
        }

        // ================================================================== raccolta

        private void Collect()
        {
            _excl = RevitUtil.BuildExclusions(_cfg.Exclusions);
            _instances = new List<Element>();
            foreach (Element e in new FilteredElementCollector(_doc).WhereElementIsNotElementType().WhereElementIsViewIndependent())
            {
                try
                {
                    Category c = e.Category;
                    if (c == null || c.CategoryType != CategoryType.Model || _excl.Contains(c)) continue;
                    ElementId tid = e.GetTypeId();
                    if (tid == null || tid == ElementId.InvalidElementId) continue;
                    var fi = e as FamilyInstance;
                    if (fi != null && fi.SuperComponent != null) continue;          // componenti annidati
                    if (e.DesignOption != null && !e.DesignOption.IsPrimary) continue; // opzioni di progetto secondarie
                    if (e.get_BoundingBox(null) == null) continue;                 // senza geometria
                    var type = _doc.GetElement(tid) as ElementType;
                    if (type == null) continue;
                    _instances.Add(e);
                    long t = tid.Value;
                    if (!_types.ContainsKey(t))
                    {
                        _types[t] = type;
                        _typeFirstInstance[t] = e.Id.Value;
                    }
                }
                catch { }
            }

            _levels = new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();

            _links = new List<LinkedModel>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var described = new List<string>();
            foreach (RevitLinkInstance li in new FilteredElementCollector(_doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                try
                {
                    Document ld = li.GetLinkDocument();
                    if (ld == null)
                    {
                        // collegamento non caricato: lo si dice, perché i controlli che ne hanno bisogno non partono
                        var lt = _doc.GetElement(li.GetTypeId()) as RevitLinkType;
                        string ln = lt != null ? lt.Name : li.Name;
                        if (seen.Add(ln)) described.Add(L.F("{0} (non caricato)", ln));
                        continue;
                    }
                    string name = RevitUtil.FileName(ld);
                    if (!seen.Add(name)) continue;
                    var lm = new LinkedModel { Instance = li, Doc = ld, Info = Identify(name, _cfg) };
                    _links.Add(lm);
                    described.Add(lm.Info.Role != null ? name + " (" + lm.Info.Role + ")" : L.F("{0} (ruolo non riconosciuto)", name));
                }
                catch { }
            }
            _linkNotes.Add(described.Count > 0 ? L.F("Collegamenti: {0}.", string.Join("; ", described)) : L.T("Nessun modello Revit collegato."));
        }

        private LinkedModel Reference(Rule rule)
        {
            string role = rule.Reference.Length > 0 ? rule.Reference : "URS";
            return _links.FirstOrDefault(l => l.Info.Role == role);
        }

        // ================================================================== regole di campi

        private void RunFieldRule(Rule rule, RuleResult rr)
        {
            List<Target> targets = Targets(rule.Object);
            var numbers = new List<Tuple<string, int, Target>>();
            bool error = rule.Severity == "errore";

            foreach (Target t in targets)
            {
                rr.Checked++;
                for (int fi = 0; fi < rule.Fields.Count; fi++)
                {
                    string field = rule.Fields[fi];
                    bool exists;
                    string value = ReadField(rule.Object, t, field, out exists);
                    if (!exists)
                    {
                        if (rule.Absent != "ignora")
                            rr.Issues.Add(MakeIssue(rule, t, error && rule.Absent == "errore", IssueKind.Mancante, field, "", L.F("parametro «{0}» assente", field)));
                        continue;
                    }
                    if (IsEmpty(value, rule))
                    {
                        rr.Issues.Add(MakeIssue(rule, t, error, IssueKind.Mancante, field, value, L.F("«{0}» non compilato", field)));
                        continue;
                    }
                    if (rule.ValidValues.Any(v => string.Equals(v, value.Trim(), StringComparison.OrdinalIgnoreCase))) continue;

                    foreach (WarnFormat w in rule.WarnFormats)
                        if (string.Equals(w.Field, field, StringComparison.OrdinalIgnoreCase) && !SafeMatch(w.Regex, value).Success)
                            rr.Issues.Add(MakeIssue(rule, t, false, IssueKind.Formato, field, value, L.F("valore diverso dal formato atteso {0}", w.Format)));

                    if (rule.Regex == null) continue;
                    Match m = SafeMatch(rule.Regex, value);
                    if (!m.Success)
                    {
                        List<string> unlisted = ProjectConfig.Unlisted(rule, value);
                        if (unlisted != null)
                            rr.Issues.Add(MakeIssue(rule, t, error, IssueKind.Elenco, field, value, string.Join("; ", unlisted)));
                        else
                            rr.Issues.Add(MakeIssue(rule, t, error, IssueKind.Formato, field, value, L.F("non rispetta il formato {0}", rule.Format)));
                        continue;
                    }
                    if (fi > 0) continue;   // coerenza e numerazione sul primo campo

                    CheckCoherence(rule, rr, t, field, value, m, error);

                    if (rule.EqualTo != null)
                    {
                        Parameter op = RevitUtil.FindParameter(t.El, rule.EqualTo);
                        string other = RevitUtil.ValueOf(op, _doc);
                        if (other == null)
                            rr.Issues.Add(MakeIssue(rule, t, error, IssueKind.Coerenza, field, value, L.F("parametro «{0}» assente: confronto impossibile", rule.EqualTo)));
                        else if (!string.Equals(other.Trim(), value.Trim(), StringComparison.Ordinal))
                            rr.Issues.Add(MakeIssue(rule, t, error, IssueKind.Coerenza, field, value, L.F("«{0}» è «{1}»: dovrebbe essere uguale", rule.EqualTo, other)));
                    }

                    if (rule.NumberGroup != null)
                    {
                        int n;
                        RxGroup g = m.Groups[rule.NumberGroup];
                        if (g.Success && int.TryParse(g.Value, NumberStyles.None, CultureInfo.InvariantCulture, out n))
                        {
                            string key = string.Join(".", rule.NumberBy.Select(b => m.Groups[b].Success ? m.Groups[b].Value : ""));
                            numbers.Add(Tuple.Create(key, n, t));
                        }
                    }
                }
            }

            if (rule.NumberGroup != null) CheckNumbering(rule, rr, numbers);
        }

        private void CheckCoherence(Rule rule, RuleResult rr, Target t, string field, string value, Match m, bool error)
        {
            foreach (Coherence c in rule.Coherence)
            {
                RxGroup g = m.Groups[c.Group];
                if (!g.Success) continue;
                string actual = g.Value;
                bool isError = error && !c.Warning;
                if (isError && c.WarnIfGroup != null)
                {
                    RxGroup w = m.Groups[c.WarnIfGroup];
                    if (w.Success && w.Value == c.WarnIfValue) isError = false;
                }
                if (c.Source == null)
                {
                    if (actual != c.Literal)
                        rr.Issues.Add(MakeIssue(rule, t, isError, IssueKind.Coerenza, field, value, L.F("{0} è «{1}», atteso «{2}»", c.Group, actual, c.Literal)));
                    continue;
                }
                List<string> expected = SourceValues(c, rule, t);
                if (expected.Count == 0) continue;   // fonte non leggibile: niente confronto
                var different = expected.Where(e => e != actual).Distinct().ToList();
                if (different.Count == 0) continue;
                string src = c.Source == "livello" && t.Levels != null ? L.T("livelli degli elementi") : c.Source;
                rr.Issues.Add(MakeIssue(rule, t, isError, IssueKind.Coerenza, field, value,
                    L.F("{0} è «{1}», ma in {2} è «{3}»", c.Group, actual, src, string.Join(", ", different))));
            }
        }

        private void CheckNumbering(Rule rule, RuleResult rr, List<Tuple<string, int, Target>> numbers)
        {
            foreach (var group in numbers.GroupBy(x => x.Item1))
            {
                string label = group.Key.Length > 0 ? group.Key : L.T("(tutti)");
                if (rule.Duplicates != "no")
                    foreach (var same in group.GroupBy(x => x.Item2).Where(x => x.Select(y => y.Item3.Id).Distinct().Count() > 1))
                        foreach (var item in same)
                            rr.Issues.Add(MakeIssue(rule, item.Item3, rule.Duplicates == "errore", IssueKind.Numerazione, rule.Fields[0], "",
                                L.F("numero {0} usato più volte nel gruppo {1}", same.Key, label)));
                if (rule.Gaps != "no")
                {
                    var present = new HashSet<int>(group.Select(x => x.Item2));
                    int max = present.Max();
                    var missing = Enumerable.Range(1, max).Where(i => !present.Contains(i)).ToList();
                    if (missing.Count > 0)
                        rr.Issues.Add(new Issue
                        {
                            Rule = rule.Name, Error = rule.Gaps == "errore", Kind = IssueKind.Numerazione, Field = rule.Fields[0],
                            Message = L.F("gruppo {0}: mancano i numeri {1} (ultimo {2})", label, Shorten(missing), max)
                        });
                }
            }
        }

        private static string Shorten(List<int> list)
        {
            if (list.Count <= 12) return string.Join(", ", list);
            return string.Join(", ", list.Take(12)) + L.F(" e altri {0}", list.Count - 12);
        }

        // ------------------------------------------------------------------ oggetti di una regola

        private readonly Dictionary<string, List<Target>> _targets = new Dictionary<string, List<Target>>();

        private List<Target> Targets(string obj)
        {
            List<Target> list;
            if (_targets.TryGetValue(obj, out list)) return list;
            list = new List<Target>();
            switch (obj)
            {
                case "livelli":
                    foreach (Level l in _levels)
                        list.Add(new Target { El = l, Level = l, Id = l.Id.Value, ShowId = l.Id.Value, Category = CatName(l), FamilyType = l.Name });
                    break;

                case "istanze":
                    foreach (Element e in _instances)
                    {
                        var type = _types[e.GetTypeId().Value];
                        list.Add(new Target { El = e, Type = type, Level = RevitUtil.LevelOf(e), Id = e.Id.Value, ShowId = e.Id.Value, Category = CatName(e), FamilyType = FamType(type) });
                    }
                    break;

                case "tipi":
                case "tipi di sistema":
                case "tipi caricabili":
                    foreach (var kv in _types)
                    {
                        ElementType type = kv.Value;
                        bool loadable = RevitUtil.IsLoadable(type);
                        if (obj == "tipi di sistema" && loadable) continue;
                        if (obj == "tipi caricabili" && !loadable) continue;
                        list.Add(new Target { El = type, Type = type, Id = kv.Key, ShowId = _typeFirstInstance[kv.Key], Category = CatName(type), FamilyType = FamType(type) });
                    }
                    break;

                case "famiglie caricabili":
                    {
                        var done = new HashSet<long>();
                        foreach (var kv in _types)
                        {
                            var fs = kv.Value as FamilySymbol;
                            if (fs == null || !RevitUtil.IsLoadable(fs) || fs.Family == null || !done.Add(fs.Family.Id.Value)) continue;
                            list.Add(new Target { El = fs.Family, Type = fs, Id = fs.Family.Id.Value, ShowId = _typeFirstInstance[kv.Key], Category = CatName(fs), FamilyType = fs.Family.Name });
                        }
                    }
                    break;

                case "locali":
                    foreach (Element e in new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType())
                    {
                        var room = e as Room;
                        if (room == null || room.Location == null || room.Area <= 0) continue;   // non posizionati o non chiusi
                        if (room.DesignOption != null && !room.DesignOption.IsPrimary) continue;
                        list.Add(new Target { El = room, Level = room.Level, Id = room.Id.Value, ShowId = room.Id.Value, Category = CatName(room), FamilyType = room.Number + " " + RoomName(room) });
                    }
                    break;

                case "materiali":
                    {
                        var first = new Dictionary<long, long>();
                        foreach (Element e in _instances)
                        {
                            ICollection<ElementId> ids;
                            try { ids = e.GetMaterialIds(false); } catch { continue; }
                            foreach (ElementId id in ids)
                                if (!first.ContainsKey(id.Value)) first[id.Value] = e.Id.Value;
                        }
                        foreach (var kv in first)
                        {
                            var mat = _doc.GetElement(new ElementId(kv.Key)) as Material;
                            if (mat == null) continue;
                            list.Add(new Target { El = mat, Id = kv.Key, ShowId = kv.Value, Category = L.T("Materiali"), FamilyType = mat.Name });
                        }
                    }
                    break;

                case "tipi di sistema impiantistico":
                    {
                        var levels = new Dictionary<long, Dictionary<long, Level>>();
                        var first = new Dictionary<long, long>();
                        foreach (Element e in _instances)
                            foreach (BuiltInParameter bip in SystemTypeParameters)
                            {
                                Parameter p = e.get_Parameter(bip);
                                if (p == null || p.StorageType != StorageType.ElementId) continue;
                                ElementId sid = p.AsElementId();
                                if (sid == null || sid == ElementId.InvalidElementId) continue;
                                long k = sid.Value;
                                if (!levels.ContainsKey(k)) { levels[k] = new Dictionary<long, Level>(); first[k] = e.Id.Value; }
                                Level l = RevitUtil.LevelOf(e);
                                if (l != null) levels[k][l.Id.Value] = l;
                            }
                        foreach (var kv in levels)
                        {
                            var st = _doc.GetElement(new ElementId(kv.Key)) as ElementType;
                            if (st == null) continue;
                            list.Add(new Target { El = st, Type = st, Levels = kv.Value.Values.ToList(), Id = kv.Key, ShowId = first[kv.Key], Category = CatName(st), FamilyType = st.Name });
                        }
                    }
                    break;
            }
            _targets[obj] = list;
            return list;
        }

        private static readonly BuiltInParameter[] SystemTypeParameters =
        {
            BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM,
            BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM
        };

        // ------------------------------------------------------------------ lettura dei campi

        private string ReadField(string obj, Target t, string field, out bool exists)
        {
            exists = true;
            string f = field.Trim().ToLowerInvariant();
            if (f == "nome" || f == "name")
                return obj == "locali" ? RoomName((Room)t.El) : (t.El.Name ?? "");
            if (f == "nome tipo")
                return t.Type != null ? t.Type.Name ?? "" : t.El.Name ?? "";
            if (f == "nome famiglia")
            {
                var fs = t.Type as FamilySymbol;
                if (fs != null && fs.Family != null) return fs.Family.Name ?? "";
                var fam = t.El as Family;
                if (fam != null) return fam.Name ?? "";
                exists = false;
                return null;
            }
            Parameter p = RevitUtil.FindParameter(t.El, field);
            if (p == null && obj == "istanze" && t.Type != null) p = RevitUtil.FindParameter(t.Type, field);   // parametro di tipo
            if (p == null)
            {
                exists = false;
                return null;
            }
            return RevitUtil.ValueOf(p, _doc);
        }

        private static bool IsEmpty(string value, Rule rule)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            foreach (string e in rule.EmptyValues)
                if (string.Equals(e, value, StringComparison.Ordinal) || (e.Trim().Length > 0 && string.Equals(e.Trim(), value.Trim(), StringComparison.Ordinal)))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ fonti della coerenza

        private List<string> SourceValues(Coherence c, Rule rule, Target t)
        {
            var values = new List<string>();
            switch (c.Source)
            {
                case "file":
                    if (!string.IsNullOrEmpty(_info.Building)) values.Add(_info.Building);
                    break;

                case "livello":
                    {
                        Rule parser = _cfg.FindParser("livelli", "Nome");
                        IEnumerable<Level> levels = t.Levels ?? (t.Level != null ? new List<Level> { t.Level } : new List<Level>());
                        foreach (Level l in levels) AddGroup(values, parser, l.Name, c.SourceGroup);
                    }
                    break;

                case "nome":
                    if (rule.Object == "materiali") AddGroup(values, _cfg.FindParser("materiali", "Nome"), t.El.Name, c.SourceGroup);
                    else if (RevitUtil.IsLoadable(t.Type)) AddGroup(values, _cfg.FindParser("famiglie caricabili", "Nome famiglia"), FamilyNameOf(t.Type), c.SourceGroup);
                    else if (t.Type != null) AddGroup(values, _cfg.FindParser("tipi di sistema", "Nome tipo"), t.Type.Name, c.SourceGroup);
                    break;

                case "nome tipo":
                    if (RevitUtil.IsLoadable(t.Type)) AddGroup(values, _cfg.FindParser("tipi caricabili", "Nome tipo"), t.Type.Name, c.SourceGroup);
                    else if (t.Type != null) AddGroup(values, _cfg.FindParser("tipi di sistema", "Nome tipo"), t.Type.Name, c.SourceGroup);
                    break;

                case "nome famiglia":
                    if (RevitUtil.IsLoadable(t.Type)) AddGroup(values, _cfg.FindParser("famiglie caricabili", "Nome famiglia"), FamilyNameOf(t.Type), c.SourceGroup);
                    break;

                case "type mark":
                    if (t.Type != null)
                    {
                        Rule parser = _cfg.FindParser("tipi", "Type Mark");
                        string tm = RevitUtil.ValueOf(RevitUtil.FindParameter(t.Type, "Type Mark"), _doc);
                        if (!string.IsNullOrEmpty(tm)) AddGroup(values, parser, tm, c.SourceGroup);
                    }
                    break;
            }
            return values;
        }

        private static string FamilyNameOf(ElementType t)
        {
            var fs = t as FamilySymbol;
            return fs != null && fs.Family != null ? fs.Family.Name : t != null ? t.FamilyName : null;
        }

        private void AddGroup(List<string> values, Rule parser, string text, string group)
        {
            if (parser == null || string.IsNullOrEmpty(text)) return;
            Match m = Parse(parser, text);
            if (m.Success && m.Groups[group].Success) values.Add(m.Groups[group].Value);
        }

        private Match Parse(Rule parser, string text)
        {
            string key = parser.Name + "\n" + text;
            Match m;
            if (!_parseCache.TryGetValue(key, out m))
            {
                m = SafeMatch(parser.Regex, text);
                _parseCache[key] = m;
            }
            return m;
        }

        private static Match SafeMatch(Regex rx, string text)
        {
            try { return rx.Match(text ?? ""); }
            catch (RegexMatchTimeoutException) { return Match.Empty; }
        }

        // ================================================================== controlli già pronti

        private void RunControl(Rule rule, RuleResult rr)
        {
            switch (rule.Control)
            {
                case "stesso edificio": CheckSameBuilding(rule, rr); break;
                case "coordinate": CheckCoordinates(rule, rr); break;
                case "livelli uguali al riferimento": CheckLevelsAsReference(rule, rr); break;
                case "copy monitor livelli": CheckCopyMonitor(rule, rr); break;
                default: rr.Note = L.T("controllo non ancora disponibile in questa versione del plugin"); break;
            }
        }

        private Issue ControlIssue(Rule rule, bool error, string field, string value, string message, Element e = null)
        {
            var i = new Issue { Rule = rule.Name, Error = error, Kind = IssueKind.Controllo, Field = field ?? "", Value = value ?? "", Message = message };
            if (e != null)
            {
                i.ElementId = e.Id.Value;
                i.ShowId = e.Id.Value;
                i.Category = CatName(e);
                i.FamilyType = e.Name ?? "";
            }
            return i;
        }

        private void CheckSameBuilding(Rule rule, RuleResult rr)
        {
            bool error = rule.Severity == "errore";
            rr.Checked = 1;
            if (string.IsNullOrEmpty(_info.Building))
            {
                rr.Issues.Add(ControlIssue(rule, error, L.T("nome file"), _info.FileName, L.T("codice edificio non letto dal nome del file")));
                return;
            }
            foreach (LinkedModel l in _links.Where(x => x.Info.Role != null))
            {
                rr.Checked++;
                if (!string.Equals(l.Info.Building, _info.Building, StringComparison.Ordinal))
                    rr.Issues.Add(ControlIssue(rule, error, L.T("nome file"), l.Info.FileName,
                        L.F("il collegamento {0} ha codice edificio «{1}», questo modello «{2}»", l.Info.Role, l.Info.Building, _info.Building)));
            }
        }

        private const double FeetToMm = 304.8;

        /// <summary>Coordinate di un punto base (E/W, N/S, quota) come le mostra Revit, in piedi.</summary>
        private static XYZ BasePointCoordinates(Document doc, bool survey)
        {
            BasePoint bp = new FilteredElementCollector(doc).OfClass(typeof(BasePoint)).Cast<BasePoint>().FirstOrDefault(b => b.IsShared == survey);
            if (bp == null) return null;
            Parameter ew = bp.get_Parameter(BuiltInParameter.BASEPOINT_EASTWEST_PARAM);
            Parameter ns = bp.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM);
            Parameter el = bp.get_Parameter(BuiltInParameter.BASEPOINT_ELEVATION_PARAM);
            if (ew != null && ns != null && el != null) return new XYZ(ew.AsDouble(), ns.AsDouble(), el.AsDouble());
            return bp.SharedPosition;
        }

        private static double TrueNorthDegrees(Document doc)
        {
            double rad = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle;
            double deg = rad * 180.0 / Math.PI % 360.0;
            return deg < 0 ? deg + 360.0 : deg;
        }

        private static string Coord(XYZ p)
        {
            return string.Format(CultureInfo.InvariantCulture, "E/W {0:F3}  N/S {1:F3}  Q {2:F3} m", p.X * 0.3048, p.Y * 0.3048, p.Z * 0.3048);
        }

        private void CheckCoordinates(Rule rule, RuleResult rr)
        {
            bool error = rule.Severity == "errore";
            LinkedModel reference = Reference(rule);
            if (reference == null)
            {
                rr.Issues.Add(ControlIssue(rule, error, "", "", L.F("modello {0} non collegato o non caricato: coordinate non confrontabili", RefName(rule))));
                return;
            }
            double tol = rule.ToleranceMm;

            foreach (bool survey in new[] { true, false })
            {
                rr.Checked++;
                string name = survey ? "Survey Point" : "Project Base Point";
                XYZ mine = BasePointCoordinates(_doc, survey);
                XYZ theirs = BasePointCoordinates(reference.Doc, survey);
                if (mine == null || theirs == null) { rr.Issues.Add(ControlIssue(rule, error, name, "", L.T("punto non trovato"))); continue; }
                double d = mine.DistanceTo(theirs) * FeetToMm;
                if (d > tol)
                    rr.Issues.Add(ControlIssue(rule, error, name, Coord(mine),
                        L.F("diverso da {0} ({1}): scarto {2:F1} mm", RefName(rule), Coord(theirs), d)));
            }

            rr.Checked++;
            double a1 = TrueNorthDegrees(_doc), a2 = TrueNorthDegrees(reference.Doc);
            double da = Math.Abs((a1 - a2 + 540.0) % 360.0 - 180.0);
            if (da > rule.ToleranceDeg)
                rr.Issues.Add(ControlIssue(rule, error, L.T("Nord reale"), a1.ToString("F4", CultureInfo.InvariantCulture) + "°",
                    L.F("{0} ha {1}°: scarto {2}°", RefName(rule), a2.ToString("F4", CultureInfo.InvariantCulture), da.ToString("F4", CultureInfo.InvariantCulture))));

            // posizione del collegamento: in coordinate condivise se L = T(questo) · T(collegato)^-1
            rr.Checked++;
            Transform tHost = _doc.ActiveProjectLocation.GetTotalTransform();
            Transform tLink = reference.Doc.ActiveProjectLocation.GetTotalTransform();
            Transform expected = tHost.Multiply(tLink.Inverse);
            Transform actual = reference.Instance.GetTotalTransform();
            double dist = actual.Origin.DistanceTo(expected.Origin) * FeetToMm;
            double rot = actual.BasisX.AngleTo(expected.BasisX) * 180.0 / Math.PI;
            if (dist > tol || rot > rule.ToleranceDeg)
                rr.Issues.Add(ControlIssue(rule, error, L.T("Collegamento"), reference.Info.FileName,
                    L.F("collegamento {0} non posizionato in coordinate condivise: spostato di {1:F1} mm, ruotato di {2}°", RefName(rule), dist, rot.ToString("F4", CultureInfo.InvariantCulture)),
                    reference.Instance));
        }

        /// <summary>Quota dei livelli in coordinate condivise (m), per nome.</summary>
        private static Dictionary<string, double> SharedLevelElevations(Document doc)
        {
            Transform toShared = doc.ActiveProjectLocation.GetTotalTransform().Inverse;
            var map = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (Level l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
                map[l.Name] = toShared.OfPoint(new XYZ(0, 0, l.ProjectElevation)).Z;
            return map;
        }

        private void CheckLevelsAsReference(Rule rule, RuleResult rr)
        {
            bool error = rule.Severity == "errore";
            LinkedModel reference = Reference(rule);
            if (reference == null)
            {
                rr.Issues.Add(ControlIssue(rule, error, "", "", L.F("modello {0} non collegato o non caricato: livelli non confrontabili", RefName(rule))));
                return;
            }
            Dictionary<string, double> theirs = SharedLevelElevations(reference.Doc);
            Transform toShared = _doc.ActiveProjectLocation.GetTotalTransform().Inverse;
            foreach (Level l in _levels)
            {
                rr.Checked++;
                double z = toShared.OfPoint(new XYZ(0, 0, l.ProjectElevation)).Z;
                double other;
                if (!theirs.TryGetValue(l.Name, out other))
                {
                    rr.Issues.Add(ControlIssue(rule, error, L.T("Nome"), l.Name, L.F("livello non presente in {0}", RefName(rule)), l));
                    continue;
                }
                double d = Math.Abs(z - other) * FeetToMm;
                if (d > rule.ToleranceMm)
                    rr.Issues.Add(ControlIssue(rule, error, L.T("Quota"), (z * 0.3048).ToString("F3", CultureInfo.InvariantCulture) + " m",
                        L.F("in {0} la quota è {1} m: scarto {2:F1} mm", RefName(rule), (other * 0.3048).ToString("F3", CultureInfo.InvariantCulture), d), l));
            }
            var mine = new HashSet<string>(_levels.Select(l => l.Name), StringComparer.Ordinal);
            foreach (var kv in theirs.Where(x => !mine.Contains(x.Key)))
                rr.Issues.Add(ControlIssue(rule, error, L.T("Nome"), kv.Key, L.F("livello di {0} mancante in questo modello", RefName(rule))));
        }

        private void CheckCopyMonitor(Rule rule, RuleResult rr)
        {
            bool error = rule.Severity == "errore";
            foreach (Level l in _levels)
            {
                rr.Checked++;
                bool monitored = false;
                try { monitored = l.IsMonitoringLinkElement(); } catch { }
                if (!monitored)
                    rr.Issues.Add(ControlIssue(rule, error, L.T("Nome"), l.Name, L.F("livello non controllato (copy/monitor) da un collegamento {0}", RefName(rule)), l));
            }
        }

        private static string RefName(Rule rule)
        {
            return rule.Reference.Length > 0 ? rule.Reference : "URS";
        }

        // ================================================================== descrizioni

        private Issue MakeIssue(Rule rule, Target t, bool error, IssueKind kind, string field, string value, string message)
        {
            return new Issue
            {
                Rule = rule.Name, Error = error, Kind = kind, ElementId = t.Id, ShowId = t.ShowId, Category = t.Category,
                FamilyType = t.FamilyType, Field = field, Value = value ?? "", Message = message
            };
        }

        private static string CatName(Element e)
        {
            try { return e.Category != null ? e.Category.Name : ""; } catch { return ""; }
        }

        private static string FamType(ElementType t)
        {
            if (t == null) return "";
            try
            {
                var fs = t as FamilySymbol;
                string fam = fs != null && fs.Family != null ? fs.Family.Name : t.FamilyName;
                return string.IsNullOrEmpty(fam) ? t.Name : fam + " : " + t.Name;
            }
            catch { return t.Name ?? ""; }
        }

        private static string RoomName(Room r)
        {
            try
            {
                Parameter p = r.get_Parameter(BuiltInParameter.ROOM_NAME);
                return p != null ? p.AsString() ?? "" : r.Name ?? "";
            }
            catch { return ""; }
        }
    }
}
