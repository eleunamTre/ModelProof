using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ModelProof
{
    /// <summary>Errore nella descrizione di un formato o di un segnaposto.</summary>
    internal sealed class ConfigException : Exception
    {
        public ConfigException(string message) : base(message) { }
    }

    /// <summary>Segnaposto {NOME}: descrizione scritta nel file e modello (regex) ricavato.</summary>
    internal sealed class Placeholder
    {
        public string Name;
        public string Description;
        public int Line;
        public string Pattern;   // null finché non è compilato
        public string Error;
    }

    /// <summary>Elenco di codici (sezione con sole righe, es. MATERIALI).</summary>
    internal sealed class CodeList
    {
        public string Name;
        public int Line;
        public List<ConfigRow> Rows;
        /// <summary>Colonne usate come codice dai segnaposto (1 = prima); per il controllo dei doppioni.</summary>
        public int KeyColumns = 1;

        public IEnumerable<string> Codes(int column)
        {
            foreach (ConfigRow r in Rows)
            {
                string c = r.Cell(column - 1);
                if (c.Length > 0) yield return c;
            }
        }
    }

    /// <summary>Regola [REGOLA nome]. Per ora si leggono i campi comuni; il resto lo useranno i controlli.</summary>
    internal sealed class Rule
    {
        public string Name;
        public int Line;
        public ConfigSection Section;
        public List<string> Models = new List<string>();
        public string Object;
        public List<string> Fields = new List<string>();
        public string Format;          // testo del formato, vuoto = basta che sia compilato
        public Regex Regex;            // formato compilato (null se non c'è o non è valido)
        public string Severity;        // errore | avviso
        public List<string> TestYes = new List<string>();
        public List<string> TestNo = new List<string>();

        public string Get(string key) { return Section.Get(key); }
    }

    /// <summary>Riga della sezione [CHECKLIST]: dove va l'esito delle regole.</summary>
    internal sealed class ChecklistLink
    {
        public string Row;             // es. 6.4
        public string Title;
        public List<string> Rules = new List<string>();
        public string Part;            // -, mancanti, formato
        public List<string> Models = new List<string>();
        public List<string> Refs = new List<string>();   // rimandi «Vedasi punto …»
        public int Line;
    }

    /// <summary>
    /// Configurazione del progetto letta dal file di testo: segnaposto, elenchi, regole, collegamento alla checklist.
    /// Load controlla anche il file e raccoglie errori e avvisi in Issues.
    /// </summary>
    internal sealed class ProjectConfig
    {
        public static readonly string[] Roles = { "URS", "ARC", "IMP" };
        private static readonly string[] NonListSections = { "PROGETTO", "MODELLI", "SEGNAPOSTO", "CHECKLIST", "ESCLUSIONI" };

        public ConfigFile File;
        public readonly Dictionary<string, Placeholder> Placeholders = new Dictionary<string, Placeholder>(StringComparer.Ordinal);
        public readonly Dictionary<string, CodeList> Lists = new Dictionary<string, CodeList>(StringComparer.OrdinalIgnoreCase);
        public readonly List<Rule> Rules = new List<Rule>();
        public readonly List<ChecklistLink> Checklist = new List<ChecklistLink>();
        public readonly List<string> Exclusions = new List<string>();
        public double PartialThreshold = 20;

        public List<ConfigIssue> Issues { get { return File.Issues; } }
        public int ErrorCount { get { return Issues.Count(i => i.Level == IssueLevel.Errore); } }
        public int WarningCount { get { return Issues.Count(i => i.Level == IssueLevel.Avviso); } }

        public Rule FindRule(string name)
        {
            foreach (Rule r in Rules)
                if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        private void Add(IssueLevel level, int line, string where, string message)
        {
            File.Issues.Add(new ConfigIssue { Level = level, Line = line, Where = where, Message = message });
        }

        // ================================================================== lettura

        public static ProjectConfig Load(string path)
        {
            return Build(ConfigFile.Load(path));
        }

        public static ProjectConfig Build(ConfigFile file)
        {
            var c = new ProjectConfig { File = file };
            c.ReadLists();
            c.ReadPlaceholders();
            c.ReadRules();
            c.ReadChecklist();
            c.ReadExclusions();
            c.CheckModels();
            c.CheckDuplicates();
            return c;
        }

        private void ReadLists()
        {
            foreach (ConfigSection s in File.Sections)
            {
                if (s.IsRule || NonListSections.Any(n => string.Equals(n, s.Name, StringComparison.OrdinalIgnoreCase))) continue;
                if (s.Rows.Count == 0)
                {
                    Add(IssueLevel.Avviso, s.Line, "[" + s.Name + "]", L.T("sezione sconosciuta o elenco vuoto"));
                    continue;
                }
                if (Lists.ContainsKey(s.Name))
                {
                    Add(IssueLevel.Errore, s.Line, "[" + s.Name + "]", L.T("elenco ripetuto: vale solo il primo"));
                    continue;
                }
                Lists[s.Name] = new CodeList { Name = s.Name, Line = s.Line, Rows = s.Rows };
            }
        }

        private void ReadPlaceholders()
        {
            ConfigSection s = File.Find("SEGNAPOSTO");
            if (s == null)
            {
                Add(IssueLevel.Errore, 0, "[SEGNAPOSTO]", L.T("sezione mancante"));
                return;
            }
            foreach (ConfigEntry e in s.Entries)
            {
                if (Placeholders.ContainsKey(e.Key))
                {
                    Add(IssueLevel.Errore, e.Line, "[SEGNAPOSTO]", L.F("segnaposto {0} definito due volte", e.Key));
                    continue;
                }
                Placeholders[e.Key] = new Placeholder { Name = e.Key, Description = e.Value, Line = e.Line };
            }
            foreach (Placeholder p in Placeholders.Values)
            {
                try { CompilePlaceholder(p.Name, new HashSet<string>(StringComparer.Ordinal)); }
                catch (ConfigException ex)
                {
                    p.Error = ex.Message;
                    Add(IssueLevel.Errore, p.Line, "{" + p.Name + "}", ex.Message);
                }
            }
            foreach (Placeholder p in Placeholders.Values)
                if (p.Pattern != null) Log.Debug("Configurazione", "{" + p.Name + "} = " + p.Pattern);
        }

        private void ReadRules()
        {
            foreach (ConfigSection s in File.Sections)
            {
                if (!s.IsRule) continue;
                string where = s.RuleName;
                if (FindRule(where) != null)
                {
                    Add(IssueLevel.Errore, s.Line, where, L.T("regola con lo stesso nome di un'altra"));
                    continue;
                }
                var r = new Rule { Name = where, Line = s.Line, Section = s };
                r.Models = SplitList(s.Get("modelli"), ',');
                r.Object = s.Get("oggetto");
                r.Fields = SplitList(s.Get("campo"), ';');
                r.Format = s.Get("formato");
                r.Severity = s.Get("gravita").ToLowerInvariant();
                r.TestYes = SplitList(s.Get("prova_si"), ';');
                r.TestNo = SplitList(s.Get("prova_no"), ';');
                Rules.Add(r);

                if (r.Models.Count == 0) Add(IssueLevel.Errore, s.Line, where, L.T("manca «modelli»"));
                foreach (string m in r.Models)
                    if (!Roles.Contains(m)) Add(IssueLevel.Errore, s.Line, where, L.F("modello «{0}» sconosciuto (ammessi: URS, ARC, IMP)", m));
                if (r.Severity != "errore" && r.Severity != "avviso")
                    Add(IssueLevel.Errore, s.Line, where, L.T("«gravita» deve essere «errore» o «avviso»"));
                if (string.IsNullOrEmpty(r.Object)) Add(IssueLevel.Avviso, s.Line, where, L.T("manca «oggetto»"));

                if (r.Format.Length > 0)
                {
                    try
                    {
                        string pattern = CompileFormat(r.Format, new HashSet<string>(StringComparer.Ordinal));
                        r.Regex = new Regex("^" + pattern + "$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                        Log.Debug("Configurazione", where + ": ^" + pattern + "$");
                    }
                    catch (ConfigException ex) { Add(IssueLevel.Errore, s.Find("formato").Line, where, ex.Message); }
                    catch (ArgumentException ex) { Add(IssueLevel.Errore, s.Find("formato").Line, where, L.F("formato non valido: {0}", ex.Message)); }
                }
                else if (r.TestYes.Count > 0 || r.TestNo.Count > 0)
                    Add(IssueLevel.Avviso, s.Line, where, L.T("ci sono esempi di prova ma la regola non ha un formato"));

                if (r.Regex != null)
                {
                    try
                    {
                        foreach (string v in r.TestYes)
                            if (!r.Regex.IsMatch(v)) Add(IssueLevel.Errore, s.Find("prova_si").Line, where, L.F("l'esempio «{0}» dovrebbe essere giusto ma non rispetta il formato", v));
                        foreach (string v in r.TestNo)
                            if (r.Regex.IsMatch(v)) Add(IssueLevel.Errore, s.Find("prova_no").Line, where, L.F("l'esempio «{0}» dovrebbe essere sbagliato ma rispetta il formato", v));
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        Add(IssueLevel.Errore, s.Line, where, L.T("formato troppo complesso: la prova degli esempi non finisce"));
                    }
                }
            }
            if (Rules.Count == 0) Add(IssueLevel.Avviso, 0, null, L.T("nessuna regola [REGOLA …] nel file"));
        }

        private void ReadChecklist()
        {
            ConfigSection s = File.Find("CHECKLIST");
            if (s == null)
            {
                Add(IssueLevel.Avviso, 0, "[CHECKLIST]", L.T("sezione mancante: la checklist non verrà compilata"));
                return;
            }
            string t = s.Get("soglia_parziale", "20");
            double v;
            if (double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0 && v <= 100) PartialThreshold = v;
            else Add(IssueLevel.Errore, s.Line, "[CHECKLIST]", L.T("«soglia_parziale» deve essere un numero tra 0 e 100"));

            foreach (ConfigRow row in s.Rows)
            {
                var link = new ChecklistLink
                {
                    Row = row.Cell(0),
                    Title = row.Cell(1),
                    Part = row.Cell(3).Length == 0 ? "-" : row.Cell(3).ToLowerInvariant(),
                    Line = row.Line
                };
                string where = L.F("checklist {0}", link.Row);
                if (Checklist.Any(c => c.Row == link.Row))
                    Add(IssueLevel.Errore, row.Line, where, L.T("riga della checklist ripetuta"));
                foreach (string name in SplitList(row.Cell(2), ';'))
                {
                    if (name == "-") continue;
                    string clean = StripParenthesis(name);
                    link.Rules.Add(clean);
                    if (FindRule(clean) == null) Add(IssueLevel.Errore, row.Line, where, L.F("regola «{0}» non trovata", clean));
                }
                if (link.Part != "-" && link.Part != "mancanti" && link.Part != "formato")
                    Add(IssueLevel.Errore, row.Line, where, L.T("la colonna «parte» deve essere -, mancanti o formato"));
                string models = row.Cell(4);
                if (models.Length > 0 && models != "-" && !string.Equals(models, "tutti", StringComparison.OrdinalIgnoreCase))
                    foreach (string m in SplitList(models, ','))
                    {
                        link.Models.Add(m);
                        if (!Roles.Contains(m)) Add(IssueLevel.Errore, row.Line, where, L.F("modello «{0}» sconosciuto (ammessi: URS, ARC, IMP)", m));
                    }
                string refs = row.Cell(5);
                if (refs.Length > 0 && refs != "-") link.Refs = SplitList(refs, ',');
                if (link.Rules.Count == 0 && link.Refs.Count == 0)
                    Add(IssueLevel.Avviso, row.Line, where, L.T("né regole né rimandi: la riga resterà vuota"));
                Checklist.Add(link);
            }
            foreach (ChecklistLink link in Checklist)
                foreach (string r in link.Refs)
                    if (!Checklist.Any(c => c.Row == r))
                        Add(IssueLevel.Errore, link.Line, L.F("checklist {0}", link.Row), L.F("rimando a «{0}», che non è nella sezione", r));
        }

        private void ReadExclusions()
        {
            ConfigSection s = File.Find("ESCLUSIONI");
            if (s == null) return;
            foreach (ConfigRow r in s.Rows) if (r.Cell(0).Length > 0) Exclusions.Add(r.Cell(0));
        }

        private void CheckModels()
        {
            ConfigSection s = File.Find("MODELLI");
            if (s == null)
            {
                Add(IssueLevel.Errore, 0, "[MODELLI]", L.T("sezione mancante"));
                return;
            }
            foreach (string key in new[] { "campo_edificio", "campo_ruolo" })
            {
                int n;
                ConfigEntry e = s.Find(key);
                if (e == null) Add(IssueLevel.Errore, s.Line, "[MODELLI]", L.F("manca «{0}»", key));
                else if (!int.TryParse(e.Value, out n) || n < 1) Add(IssueLevel.Errore, e.Line, "[MODELLI]", L.F("«{0}» deve essere un numero di campo (1 = primo)", key));
            }
            foreach (string role in Roles)
                if (s.Get(role + ".codice").Length == 0) Add(IssueLevel.Errore, s.Line, "[MODELLI]", L.F("manca «{0}.codice»", role));
        }

        private void CheckDuplicates()
        {
            foreach (CodeList list in Lists.Values)
            {
                var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                var dup = new List<string>();
                foreach (ConfigRow r in list.Rows)
                {
                    var key = new StringBuilder();
                    for (int c = 0; c < list.KeyColumns; c++) key.Append(c > 0 ? "." : "").Append(r.Cell(c));
                    string k = key.ToString();
                    if (k.Replace(".", "").Length == 0) continue;
                    if (seen.ContainsKey(k)) { if (!dup.Contains(k)) dup.Add(k); }
                    else seen[k] = r.Line;
                }
                if (dup.Count > 0)
                    Add(IssueLevel.Avviso, list.Line, "[" + list.Name + "]", L.F("codici ripetuti: {0}", string.Join(", ", dup)));
            }
        }

        // ================================================================== segnaposto e formati

        /// <summary>Modello (regex senza ancore) del segnaposto, compilato una volta sola.</summary>
        public string CompilePlaceholder(string name, HashSet<string> stack)
        {
            Placeholder p;
            if (!Placeholders.TryGetValue(name, out p)) throw new ConfigException(L.F("segnaposto {{{0}}} non definito in [SEGNAPOSTO]", name));
            if (p.Pattern != null) return p.Pattern;
            if (p.Error != null) throw new ConfigException(L.F("segnaposto {{{0}}} non valido", name));
            if (!stack.Add(name)) throw new ConfigException(L.F("il segnaposto {{{0}}} rimanda a se stesso", name));
            try
            {
                string pattern = CompileDescription(p.Description, stack);
                new Regex(pattern, RegexOptions.CultureInvariant);   // verifica
                p.Pattern = pattern;
                return pattern;
            }
            catch (ArgumentException ex)
            {
                throw new ConfigException(L.F("modello non valido: {0}", ex.Message));
            }
            finally { stack.Remove(name); }
        }

        /// <summary>
        /// Formato con segnaposto: {NOME} = segnaposto (diventa un gruppo con quel nome), [ … ] = parte facoltativa,
        /// tutto il resto è testo fisso.
        /// </summary>
        public string CompileFormat(string format, HashSet<string> stack)
        {
            var sb = new StringBuilder();
            int depth = 0;
            for (int i = 0; i < format.Length; i++)
            {
                char c = format[i];
                if (c == '{')
                {
                    int j = format.IndexOf('}', i + 1);
                    if (j < 0) throw new ConfigException(L.T("parentesi { senza }"));
                    string name = format.Substring(i + 1, j - i - 1).Trim();
                    string inner = CompilePlaceholder(name, stack);
                    sb.Append(IsGroupName(name) ? "(?<" + name + ">" + inner + ")" : "(?:" + inner + ")");
                    i = j;
                }
                else if (c == '}') throw new ConfigException(L.T("parentesi } senza {"));
                else if (c == '[') { sb.Append("(?:"); depth++; }
                else if (c == ']')
                {
                    if (depth == 0) throw new ConfigException(L.T("parentesi ] senza ["));
                    sb.Append(")?");
                    depth--;
                }
                else sb.Append(Regex.Escape(c.ToString()));
            }
            if (depth != 0) throw new ConfigException(L.T("parentesi [ senza ]"));
            return sb.ToString();
        }

        private static readonly Regex RxList = new Regex(@"^elenco\s+(\S+)(?:\s+colonna\s+(\d+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RxCount = new Regex(@"^(\d+)\s+(cifre|cifra|lettere maiuscole|lettera maiuscola|lettere minuscole|lettera minuscola|lettere|lettera|caratteri|carattere)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RxChars = new Regex(@"^(\d+)\s+caratter[ei]\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RxFields = new Regex("^campi separati da \"(.)\"$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RxText = new Regex("^testo libero(?:\\s+senza\\s+(.+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RxQuoted = new Regex("^\"(.*)\"$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Descrizione di un segnaposto (vedi l'intestazione del file di configurazione):
        ///   regex: …                            modello scritto direttamente
        ///   {A}_{B}                             composizione di altri segnaposto
        ///   elenco NOME [colonna N]             uno dei codici dell'elenco (colonna 1 se non indicata)
        ///   3 cifre / 2 lettere maiuscole / 2 lettere minuscole / 2 lettere / 2 caratteri
        ///   1 carattere: cifra o X              caratteri ammessi: cifra, lettera maiuscola, lettera minuscola, lettera, o un carattere
        ///   numero progressivo                  1, 2, 3 … (senza zeri iniziali)
        ///   campi separati da "_"               uno o più campi non vuoti separati da quel carattere
        ///   testo libero [senza "." ","]        qualsiasi testo non vuoto, eventualmente senza quei caratteri
        ///   "_"                                 testo fisso
        /// Le alternative si uniscono con « oppure ».
        /// </summary>
        private string CompileDescription(string description, HashSet<string> stack)
        {
            string d = description.Trim();
            if (d.Length == 0) throw new ConfigException(L.T("descrizione vuota"));
            if (d.StartsWith("regex:", StringComparison.OrdinalIgnoreCase)) return d.Substring(6).Trim();
            List<string> alts = SplitOutsideQuotes(d, " oppure ");
            if (alts.Count > 1)
            {
                var parts = new List<string>();
                foreach (string a in alts) parts.Add(CompileAtom(a.Trim(), stack));
                return "(?:" + string.Join("|", parts) + ")";
            }
            return CompileAtom(d, stack);
        }

        private string CompileAtom(string a, HashSet<string> stack)
        {
            Match m = RxQuoted.Match(a);
            if (m.Success)
            {
                if (m.Groups[1].Value.Length == 0) throw new ConfigException(L.T("testo fisso vuoto"));
                return Regex.Escape(m.Groups[1].Value);
            }

            if (a.IndexOf('{') >= 0) return CompileFormat(a, stack);

            m = RxList.Match(a);
            if (m.Success)
            {
                string name = m.Groups[1].Value;
                int column = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 1;
                CodeList list;
                if (!Lists.TryGetValue(name, out list)) throw new ConfigException(L.F("elenco [{0}] non trovato", name));
                if (column < 1) throw new ConfigException(L.T("la colonna parte da 1"));
                list.KeyColumns = Math.Max(list.KeyColumns, column);
                List<string> codes = list.Codes(column).Distinct(StringComparer.Ordinal).OrderByDescending(c => c.Length).ToList();
                if (codes.Count == 0) throw new ConfigException(L.F("l'elenco [{0}] non ha codici nella colonna {1}", name, column));
                return "(?:" + string.Join("|", codes.Select(c => Regex.Escape(c))) + ")";
            }

            m = RxCount.Match(a);
            if (m.Success)
            {
                string cls;
                string what = m.Groups[2].Value.ToLowerInvariant();
                if (what.StartsWith("cifr")) cls = "[0-9]";
                else if (what.EndsWith("maiuscole") || what.EndsWith("maiuscola")) cls = "[A-Z]";
                else if (what.EndsWith("minuscole") || what.EndsWith("minuscola")) cls = "[a-z]";
                else if (what.StartsWith("letter")) cls = "[A-Za-z]";
                else cls = "[^\\r\\n]";
                return cls + Count(m.Groups[1].Value);
            }

            m = RxChars.Match(a);
            if (m.Success)
            {
                var cls = new StringBuilder("[");
                foreach (string item in Regex.Split(m.Groups[2].Value, @"\s+o\s+|\s*,\s*"))
                {
                    string it = item.Trim();
                    string low = it.ToLowerInvariant();
                    Match q = RxQuoted.Match(it);
                    if (q.Success) it = q.Groups[1].Value;
                    if (low == "cifra" || low == "cifre") cls.Append("0-9");
                    else if (low == "lettera maiuscola" || low == "lettere maiuscole") cls.Append("A-Z");
                    else if (low == "lettera minuscola" || low == "lettere minuscole") cls.Append("a-z");
                    else if (low == "lettera" || low == "lettere") cls.Append("A-Za-z");
                    else if (it.Length == 1) cls.Append(EscapeClass(it[0]));
                    else throw new ConfigException(L.F("carattere ammesso non riconosciuto: «{0}»", item.Trim()));
                }
                return cls.Append(']').ToString() + Count(m.Groups[1].Value);
            }

            if (string.Equals(a, "numero progressivo", StringComparison.OrdinalIgnoreCase)) return "[1-9][0-9]*";

            m = RxFields.Match(a);
            if (m.Success)
            {
                string sep = Regex.Escape(m.Groups[1].Value);
                string not = "[^" + EscapeClass(m.Groups[1].Value[0]) + "]+";
                return not + "(?:" + sep + not + ")*";
            }

            m = RxText.Match(a);
            if (m.Success)
            {
                if (!m.Groups[1].Success) return ".+";
                var cls = new StringBuilder("[^");
                foreach (Match q in Regex.Matches(m.Groups[1].Value, "\"(.)\""))
                    cls.Append(EscapeClass(q.Groups[1].Value[0]));
                if (cls.Length == 2) throw new ConfigException(L.T("dopo «senza» vanno i caratteri tra virgolette, es. senza \".\""));
                return cls.Append("]+").ToString();
            }

            throw new ConfigException(L.F("descrizione non riconosciuta: «{0}»", a));
        }

        private static string Count(string n)
        {
            int k = int.Parse(n, CultureInfo.InvariantCulture);
            if (k < 1) throw new ConfigException(L.T("il numero di caratteri deve essere almeno 1"));
            return k == 1 ? "" : "{" + k + "}";
        }

        private static string EscapeClass(char c)
        {
            return c == '\\' || c == ']' || c == '[' || c == '^' || c == '-' ? "\\" + c : c.ToString();
        }

        private static bool IsGroupName(string name)
        {
            if (name.Length == 0 || char.IsDigit(name[0])) return false;
            foreach (char c in name) if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }

        // ================================================================== utilità

        /// <summary>Divide per il separatore, togliendo spazi e voci vuote.</summary>
        public static List<string> SplitList(string text, char sep)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (string s in text.Replace("\n", " ").Split(sep))
            {
                string t = s.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        private static List<string> SplitOutsideQuotes(string text, string sep)
        {
            var parts = new List<string>();
            bool quoted = false;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '"') quoted = !quoted;
                else if (!quoted && string.CompareOrdinal(text, i, sep, 0, sep.Length) == 0)
                {
                    parts.Add(text.Substring(start, i - start));
                    start = i + sep.Length;
                    i = start - 1;
                }
            }
            parts.Add(text.Substring(start));
            return parts;
        }

        private static string StripParenthesis(string name)
        {
            int p = name.IndexOf('(');
            return (p > 0 ? name.Substring(0, p) : name).Trim();
        }

        /// <summary>Prova un valore con una regola: esito e parti riconosciute (per la finestra di prova).</summary>
        public static string Explain(Rule rule, string value)
        {
            if (rule == null || rule.Regex == null) return L.T("La regola non ha un formato da provare.");
            Match m;
            try { m = rule.Regex.Match(value ?? ""); }
            catch (RegexMatchTimeoutException) { return L.T("Prova interrotta: il formato è troppo complesso per questo valore."); }
            if (!m.Success) return L.F("NON rispetta il formato {0}", rule.Format);
            var parts = new List<string>();
            foreach (string g in rule.Regex.GetGroupNames())
            {
                if (char.IsDigit(g[0])) continue;
                Group grp = m.Groups[g];
                if (grp.Success) parts.Add(g + " = " + grp.Value);
            }
            return L.T("Rispetta il formato.") + (parts.Count > 0 ? "  " + string.Join("   ", parts) : "");
        }
    }
}
