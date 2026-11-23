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

        /// <summary>
        /// La categoria Revit corrisponde al nome inglese dell'elenco (per id, o per nome con Revit in inglese). Le
        /// facciate continue non sono una categoria ma un tipo di muro: «Curtain Wall» vale per i muri di tipo facciata
        /// continua, «Walls» per gli altri muri.
        /// </summary>
        private static bool SameCategory(Category c, string englishName, ElementType type)
        {
            if (c == null || string.IsNullOrWhiteSpace(englishName)) return false;
            string name = englishName.Trim();
            List<long> ids = RevitUtil.CategoryIds(name);
            long id = c.Id.Value;
            bool same = ids.Contains(id) || string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase);
            if (!same)
            {
                // categorie che Revit separa ma che fanno parte della stessa cosa: cornici e scanalature dei muri sono
                // muri; dal 2024 il terreno è Toposolid invece di Topography
                List<string> also;
                if (!Compatible.TryGetValue(name, out also)) return false;
                foreach (string ost in also)
                {
                    BuiltInCategory bic;
                    if (Enum.TryParse(ost, out bic) && (long)bic == id) return true;
                }
                return false;
            }
            var wt = type as WallType;
            if (wt == null || id != (long)BuiltInCategory.OST_Walls) return true;
            bool curtainName = name.StartsWith("Curtain Wall", StringComparison.OrdinalIgnoreCase);
            bool curtainType = false;
            try { curtainType = wt.Kind == WallKind.Curtain; } catch { }
            return curtainName == curtainType;
        }

        private static readonly Dictionary<string, List<string>> Compatible = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Walls", new List<string> { "OST_Cornices", "OST_Reveals" } },
            { "Topography", new List<string> { "OST_Toposolid" } },
            { "Toposolid", new List<string> { "OST_Topography" } }
        };

        /// <summary>Nome da mostrare per la categoria di un tipo (le facciate continue come «Walls (Curtain Wall)»).</summary>
        private static string CategoryText(Category c, ElementType type)
        {
            var wt = type as WallType;
            try { if (wt != null && wt.Kind == WallKind.Curtain) return c.Name + " (Curtain Wall)"; } catch { }
            return c.Name;
        }

        private Issue TypeIssue(Rule rule, bool error, Target t, string field, string value, string message)
        {
            return MakeIssue(rule, t, error, IssueKind.Controllo, field, value, message);
        }

        // ------------------------------------------------------------------ parametri condivisi

        /// <summary>
        /// Parametri condivisi IN USO nel modello (assegnati ad almeno una categoria del progetto, oppure compilati su
        /// almeno un elemento o tipo): ognuno deve essere nell'elenco della regola. Parametri nascosti (Visible = false,
        /// creati da altri programmi) esclusi. I parametri condivisi solo
        /// registrati nel documento (arrivati con famiglie, mai usati: «fantasma») non contano, e i parametri
        /// dell'elenco che il modello non usa non sono un errore. Chiave facoltativa «prefisso»: si controllano solo i
        /// parametri il cui nome inizia così (maiuscole e minuscole contano).
        /// </summary>
        private void CheckSharedParameters(Rule rule, RuleResult rr)
        {
            bool error = rule.Severity == "errore";
            CodeList list = RuleList(rule, rr);
            if (list == null) return;
            string prefix = rule.Get("prefisso").Trim().Trim('"');
            var wanted = new HashSet<string>(list.Codes(1).Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.Ordinal);
            // «escludi»: nomi o inizi di nome (con * finale) da non controllare
            var skip = ProjectConfig.SplitList(rule.Get("escludi"), ';');
            Func<string, bool> skipped = n => skip.Any(x => x.EndsWith("*")
                ? n.StartsWith(x.Substring(0, x.Length - 1), StringComparison.Ordinal)
                : string.Equals(n, x, StringComparison.Ordinal));
            // con il prefisso si controllano i parametri che iniziano così, più quelli scritti nell'elenco
            Func<string, bool> passes = n => !skipped(n) && (prefix.Length == 0 || n.StartsWith(prefix, StringComparison.Ordinal) || wanted.Contains(n));
            // «criterio»: quando un parametro è «in uso». compilati (di base) = ha un valore su almeno un elemento o tipo
            // del modello; assegnati = è un parametro di progetto; assegnati o compilati = uno dei due
            string criterion = rule.Get("criterio", "compilati").Trim().ToLowerInvariant();
            bool useBound = criterion.Contains("assegnati");
            bool useFilled = criterion.Contains("compilati");

            // parametri condivisi registrati nel documento
            var byGuid = new Dictionary<Guid, string>();
            var sharedIds = new HashSet<long>();
            foreach (SharedParameterElement sp in new FilteredElementCollector(_doc).OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>())
            {
                sharedIds.Add(sp.Id.Value);
                string n = (sp.Name ?? "").Trim();
                bool visible = true;
                try { InternalDefinition d = sp.GetDefinition(); if (d != null) visible = d.Visible; } catch { }
                if (visible && passes(n)) byGuid[sp.GuidValue] = n;   // parametri nascosti (di altri programmi): non contano
            }

            // assegnati alle categorie del progetto (condivisi o no)
            var bound = new HashSet<string>(StringComparer.Ordinal);
            var boundGuid = new Dictionary<string, Guid>(StringComparer.Ordinal);   // GUID del parametro di progetto
            var boundProject = new HashSet<string>(StringComparer.Ordinal);
            DefinitionBindingMapIterator it = _doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext())
            {
                var def = it.Key as InternalDefinition;
                if (def == null) continue;
                string n = (def.Name ?? "").Trim();
                bool shown = true;
                try { shown = def.Visible; } catch { }
                if (!shown || !passes(n)) continue;
                int cats = 1;
                try { var b = it.Current as ElementBinding; if (b != null) cats = b.Categories.Size; } catch { }
                if (cats == 0) continue;
                if (sharedIds.Contains(def.Id.Value))
                {
                    bound.Add(n);
                    try { var sp = _doc.GetElement(def.Id) as SharedParameterElement; if (sp != null) boundGuid[n] = sp.GuidValue; } catch { }
                }
                else boundProject.Add(n);
            }

            // compilati su elementi o tipi (anche se arrivano solo dalle famiglie); per ogni GUID anche le famiglie che lo
            // compilano e un elemento da mostrare (prima le istanze), per i nomi doppi
            var filled = new Dictionary<Guid, int>();
            var guidFamilies = new Dictionary<Guid, SortedSet<string>>();
            var guidElement = new Dictionary<Guid, Element>();
            if (byGuid.Count > 0)
                foreach (bool types in new[] { false, true })
                {
                    var coll = new FilteredElementCollector(_doc);
                    IEnumerable<Element> els = types ? coll.WhereElementIsElementType() : coll.WhereElementIsNotElementType();
                    foreach (Element el in els)
                    {
                        try
                        {
                            // solo elementi e tipi del modello (non informazioni di progetto, viste, tavole…)
                            if (el.Category == null || el.Category.CategoryType != CategoryType.Model) continue;
                            foreach (Parameter p in el.Parameters)
                            {
                                if (!p.IsShared) continue;
                                Guid g = p.GUID;
                                if (!byGuid.ContainsKey(g) || !HasValue(p)) continue;
                                int c; filled.TryGetValue(g, out c); filled[g] = c + 1;
                                SortedSet<string> fams;
                                if (!guidFamilies.TryGetValue(g, out fams)) guidFamilies[g] = fams = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
                                string fam = FamilyLabel(el);
                                if (fam.Length > 0 && fams.Count < 6) fams.Add(fam);
                                if (!guidElement.ContainsKey(g)) guidElement[g] = el;
                            }
                        }
                        catch { }
                    }
                }
            var filledByName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in filled) { int c; filledByName.TryGetValue(byGuid[kv.Key], out c); filledByName[byGuid[kv.Key]] = c + kv.Value; }

            var inUse = new SortedSet<string>((useBound ? bound : Enumerable.Empty<string>()).Concat(useFilled ? filledByName.Keys : Enumerable.Empty<string>()), StringComparer.Ordinal);
            if (!useFilled) filledByName.Clear();   // con «assegnati» i conteggi non servono nei messaggi
            foreach (string n in inUse)
            {
                rr.Checked++;
                var origin = new List<string>();
                if (bound.Contains(n)) origin.Add(L.T("parametro di progetto"));
                int c;
                if (filledByName.TryGetValue(n, out c)) origin.Add(L.F("compilato su {0} elementi o tipi", c));
                string where = string.Join(", ", origin);
                if (!wanted.Contains(n))
                    rr.Issues.Add(ControlIssue(rule, error, L.T("Parametro"), n, L.F("parametro condiviso in uso ({0}) non presente nell'elenco [{1}]", where, list.Name)));
                else
                    rr.Issues.Add(OkRow(rule, _info.FileName, _info.RoleText, L.T("Parametro"), n, where));

                // stesso nome, GUID diversi: conta solo se più GUID sono davvero usati (assegnato o compilato). Quello
                // «giusto» è il GUID del parametro di progetto (o il più compilato); gli altri arrivano da famiglie fatte
                // con un altro file dei parametri condivisi e i loro valori non compaiono negli abachi né nell'IFC
                var used = byGuid.Where(kv => kv.Value == n && (filled.ContainsKey(kv.Key) || (boundGuid.ContainsKey(n) && boundGuid[n] == kv.Key)))
                                 .Select(kv => kv.Key).ToList();
                if (used.Count < 2) continue;
                Guid main;
                bool hasMain = boundGuid.TryGetValue(n, out main) && used.Contains(main);
                if (!hasMain) main = used.OrderByDescending(g => { int k; filled.TryGetValue(g, out k); return k; }).First();
                foreach (Guid g in used.Where(x => x != main))
                {
                    int k; filled.TryGetValue(g, out k);
                    SortedSet<string> fams; guidFamilies.TryGetValue(g, out fams);
                    Element show; guidElement.TryGetValue(g, out show);
                    string famText = fams != null && fams.Count > 0 ? string.Join(", ", fams) + (fams.Count >= 6 ? ", …" : "") : "?";
                    string msg = hasMain
                        ? L.F("stesso nome ma GUID diverso da quello del parametro di progetto ({0}): compilato su {1} elementi o tipi delle famiglie {2}. Negli abachi e nell'IFC si vede solo il parametro di progetto: va sostituito nelle famiglie", g, k, famText)
                        : L.F("stesso nome ma GUID diverso ({0}): compilato su {1} elementi o tipi delle famiglie {2}", g, k, famText);
                    Issue i = ControlIssue(rule, false, L.T("Parametro"), n, msg, show is ElementType ? null : show);
                    if (show is ElementType) { i.FamilyType = FamilyLabel(show); i.ElementId = show.Id.Value; }
                    rr.Issues.Add(i);
                }
            }
            // parametri di progetto NON condivisi con il nome di un parametro dell'elenco
            foreach (string n in boundProject.Where(x => wanted.Contains(x)))
            {
                rr.Checked++;
                rr.Issues.Add(ControlIssue(rule, error, L.T("Parametro"), n, L.T("parametro di progetto NON condiviso: nell'elenco è un parametro condiviso")));
            }
        }

        /// <summary>Famiglia (o tipo di sistema) di un elemento o di un tipo, per i messaggi.</summary>
        private string FamilyLabel(Element el)
        {
            try
            {
                var type = el as ElementType ?? _doc.GetElement(el.GetTypeId()) as ElementType;
                if (type == null) return "";
                var fs = type as FamilySymbol;
                return fs != null && fs.Family != null ? fs.Family.Name : (type.FamilyName ?? "") + " : " + type.Name;
            }
            catch { return ""; }
        }

        private static bool HasValue(Parameter p)
        {
            try
            {
                if (!p.HasValue) return false;
                if (p.StorageType == StorageType.String) { string v = p.AsString(); return v != null && v.Trim().Length > 0; }
                return true;
            }
            catch { return false; }
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
                else if (c != null && !cats.Any(n => SameCategory(c, n, t.Type)))
                    rr.Issues.Add(TypeIssue(rule, error, t, part, code,
                        L.F("«{0}» indica {1}, ma la categoria Revit è {2}", code, string.Join(" / ", cats), CategoryText(c, t.Type))));
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
                List<string[]> rows = table.Rows.Where(r => SameCategory(c, r[1], t.Type)).ToList();
                if (rows.Count == 0)
                {
                    rr.Issues.Add(TypeIssue(rule, false, t, L.T("Categoria"), CategoryText(c, t.Type), L.F("categoria non presente nell'elenco [{0}]", list.Name)));
                    continue;
                }
                var disc = new HashSet<string>(rows.SelectMany(r => ProjectConfig.SplitList(r[2], ',')), StringComparer.OrdinalIgnoreCase);
                if (disc.Overlaps(mine))
                    rr.Issues.Add(MakeOkFor(rule, t, L.T("Categoria"), CategoryText(c, t.Type), string.Join(", ", disc.OrderBy(x => x))));
                else
                    rr.Issues.Add(TypeIssue(rule, error, t, L.T("Categoria"), CategoryText(c, t.Type),
                        L.F("categoria non ammessa per il modello {0} ({1}): ammessa per {2}", _info.RoleText, string.Join(", ", mine), string.Join(", ", disc.OrderBy(x => x)))));
            }
        }

        /// <summary>
        /// Classe IFC del tipo coerente con il codice di categoria del nome: il valore del campo (di base «Export Type to
        /// IFC As») deve essere una delle classi IFC dell'elenco per quel codice (colonna «colonna_ifc», classi separate
        /// da «;» o «,»). Si confronta senza maiuscole, senza il «Type» finale e senza il tipo predefinito dopo il punto
        /// (IfcWallType e IfcWall.SOLIDWALL valgono IfcWall). Codici senza classi in elenco: non controllati; campo
        /// vuoto: lo segnala la regola sui parametri IFC.
        /// </summary>
        private void CheckIfcClass(Rule rule, RuleResult rr)
        {
            bool error = rule.Severity == "errore";
            CodeList list = RuleList(rule, rr);
            if (list == null) return;
            int cCode = Column(rule, "colonna_codice", 1), cIfc = Column(rule, "colonna_ifc", 5);
            string part = Part(rule, "parte_categoria", "CAT");
            string field = rule.Fields.Count > 0 ? rule.Fields[0] : "Export Type to IFC As";
            var classes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (ConfigRow r in list.Rows)
            {
                string code = r.Cell(cCode - 1).Trim();
                if (code.Length == 0) continue;
                List<string> l;
                if (!classes.TryGetValue(code, out l)) classes[code] = l = new List<string>();
                foreach (string c in r.Cell(cIfc - 1).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    if (c.Trim().Length > 0 && !l.Contains(c.Trim())) l.Add(c.Trim());
            }
            foreach (Target t in Targets("tipi"))
            {
                string text;
                Match m = TypeNameMatch(t.Type, out text);
                if (!m.Success || !m.Groups[part].Success) continue;
                string code = m.Groups[part].Value;
                List<string> allowed;
                if (!classes.TryGetValue(code, out allowed) || allowed.Count == 0) continue;
                string value = RevitUtil.ValueOf(RevitUtil.FindParameter(t.Type, field), _doc);
                if (string.IsNullOrWhiteSpace(value)) continue;
                rr.Checked++;
                string v = IfcBase(value);
                if (allowed.Any(a => string.Equals(IfcBase(a), v, StringComparison.OrdinalIgnoreCase)))
                    rr.Issues.Add(MakeOkFor(rule, t, field, value, string.Format("{0}: {1}", code, string.Join(", ", allowed))));
                else
                    rr.Issues.Add(TypeIssue(rule, error, t, field, value,
                        L.F("classe IFC «{0}» non prevista per «{1}» (previste: {2})", value, code, string.Join(", ", allowed))));
            }
        }

        // ------------------------------------------------------------------ coerenza dei nomi tra tipi analoghi

        private static readonly Regex RxDim = new Regex(@"^\d+(?:[.,]\d+)?(?:[xX×]\d+(?:[.,]\d+)?)+$", RegexOptions.CultureInvariant);
        private static readonly Regex RxNum = new Regex(@"^\d+(?:[.,]\d+)?$", RegexOptions.CultureInvariant);
        private static readonly Regex RxCod = new Regex(@"^[A-Z]{1,3}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Struttura di un nome: le parti tra separatori (_ - . spazio) diventano DIM (misure come 85x214), N (numero),
        /// COD (1-3 lettere maiuscole), TXT (altro testo) o «·» (parte vuota); i separatori restano.
        /// Es. «AL_85x214» → «COD_DIM», «AC_95x210_REI» → «COD_DIM_COD».
        /// </summary>
        public static string NameShape(string name)
        {
            var sb = new System.Text.StringBuilder();
            var tok = new System.Text.StringBuilder();
            Action flush = () =>
            {
                string s = tok.ToString();
                sb.Append(s.Length == 0 ? "·" : RxDim.IsMatch(s) ? "DIM" : RxNum.IsMatch(s) ? "N" : RxCod.IsMatch(s) ? "COD" : "TXT");
                tok.Clear();
            };
            foreach (char ch in name ?? "")
            {
                if (ch == '_' || ch == '-' || ch == '.' || ch == ' ') { flush(); sb.Append(ch); }
                else tok.Append(ch);
            }
            flush();
            return sb.ToString();
        }

        /// <summary>
        /// Avviso «da attenzionare», non una regola: tra i tipi dello stesso gruppo (parti del nome indicate in
        /// «raggruppa», es. CAT fu: stessa categoria e funzione) il campo (di base il nome del tipo) dovrebbe avere la
        /// stessa struttura. Si cerca la struttura più comune del gruppo e si segnalano i tipi che ne hanno un'altra
        /// (es. le dimensioni in un altro punto, o mancanti). Solo gruppi con almeno «minimo» tipi (di base 3) e una
        /// struttura prevalente su almeno «soglia» % (di base 60).
        /// </summary>
        private void CheckNameConsistency(Rule rule, RuleResult rr)
        {
            string field = rule.Fields.Count > 0 ? rule.Fields[0] : "Nome tipo";
            var parts = rule.Get("raggruppa", "CAT fu").Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int min = Column(rule, "minimo", 3);
            int threshold = Column(rule, "soglia", 60);
            var groups = new Dictionary<string, List<Tuple<Target, string, string>>>(StringComparer.Ordinal);
            foreach (Target t in Targets("tipi"))
            {
                string text;
                Match m = TypeNameMatch(t.Type, out text);
                string key;
                if (m.Success && parts.All(p => m.Groups[p].Success)) key = string.Join(".", parts.Select(p => m.Groups[p].Value));
                else continue;   // nome fuori formato: lo segnala la regola del nome
                bool exists;
                string value = ReadField("tipi", t, field, out exists);
                if (!exists || string.IsNullOrWhiteSpace(value)) continue;
                List<Tuple<Target, string, string>> l;
                if (!groups.TryGetValue(key, out l)) groups[key] = l = new List<Tuple<Target, string, string>>();
                l.Add(Tuple.Create(t, value, NameShape(value)));
            }
            foreach (var kv in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var items = kv.Value;
                if (items.Count < min) continue;
                var best = items.GroupBy(x => x.Item3).OrderByDescending(g => g.Count()).First();
                if (best.Count() * 100 < threshold * items.Count) continue;   // nessuna struttura prevalente: niente da dire
                string example = best.First().Item2;
                foreach (var x in items)
                {
                    rr.Checked++;
                    if (x.Item3 == best.Key)
                        rr.Issues.Add(MakeOkFor(rule, x.Item1, field, x.Item2, L.F("{0}: struttura «{1}» come {2} tipi su {3}", kv.Key, best.Key, best.Count(), items.Count)));
                    else
                        rr.Issues.Add(TypeIssue(rule, false, x.Item1, field, x.Item2,
                            L.F("{0}: struttura del nome «{1}» diversa da quella degli altri tipi «{2}» ({3} su {4}, es. «{5}»)",
                                kv.Key, x.Item3, best.Key, best.Count(), items.Count, example)));
                }
            }
        }

        /// <summary>Classe IFC senza tipo predefinito («.SOLIDWALL») e senza «Type» finale.</summary>
        private static string IfcBase(string s)
        {
            string v = (s ?? "").Trim();
            int dot = v.IndexOf('.');
            if (dot > 0) v = v.Substring(0, dot);
            if (v.EndsWith("Type", StringComparison.OrdinalIgnoreCase) && v.Length > 7) v = v.Substring(0, v.Length - 4);
            return v;
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
