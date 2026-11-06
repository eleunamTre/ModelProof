using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ModelProof
{
    internal enum IssueLevel { Info = 0, Avviso = 1, Errore = 2 }

    /// <summary>Problema trovato leggendo o controllando il file di configurazione.</summary>
    internal sealed class ConfigIssue
    {
        public IssueLevel Level;
        public int Line;          // 0 = non legato a una riga
        public string Where;      // sezione o regola
        public string Message;

        public override string ToString()
        {
            string pos = Line > 0 ? L.F("riga {0}", Line) : "";
            string head = string.IsNullOrEmpty(Where) ? pos : (pos.Length > 0 ? Where + ", " + pos : Where);
            return head.Length > 0 ? head + ": " + Message : Message;
        }
    }

    /// <summary>Riga «chiave = valore». Le righe seguenti che iniziano con spazi continuano il valore.</summary>
    internal sealed class ConfigEntry
    {
        public string Key;
        public string Value;
        public int Line;
    }

    /// <summary>Riga di un elenco: celle separate da TAB (incollate da Excel) oppure da «;».</summary>
    internal sealed class ConfigRow
    {
        public string[] Cells;
        public int Line;

        public string Cell(int i) { return i >= 0 && i < Cells.Length ? Cells[i] : ""; }
    }

    /// <summary>Sezione [NOME] del file: valori «chiave = valore» e/o righe di elenco.</summary>
    internal sealed class ConfigSection
    {
        public string Name;
        public int Line;
        public readonly List<ConfigEntry> Entries = new List<ConfigEntry>();
        public readonly List<ConfigRow> Rows = new List<ConfigRow>();

        public bool IsRule { get { return Name.StartsWith("REGOLA ", StringComparison.OrdinalIgnoreCase); } }
        public string RuleName { get { return IsRule ? Name.Substring(7).Trim() : null; } }

        public ConfigEntry Find(string key)
        {
            foreach (ConfigEntry e in Entries)
                if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        public string Get(string key, string fallback = "")
        {
            ConfigEntry e = Find(key);
            return e == null ? fallback : e.Value;
        }
    }

    /// <summary>
    /// Lettura del file di configurazione (testo UTF-8). Formato:
    ///   # commento (anche a fine riga, se preceduto da uno spazio o da un TAB)
    ///   [SEZIONE]
    ///   chiave = valore
    ///              continuazione del valore (riga che inizia con spazi)
    ///   cella1 TAB cella2 TAB …      (riga di elenco; senza TAB le celle si separano con «;»)
    /// Qui si legge solo la struttura: il significato delle sezioni lo dà ProjectConfig.
    /// </summary>
    internal sealed class ConfigFile
    {
        public string Path;
        public readonly List<ConfigSection> Sections = new List<ConfigSection>();
        public readonly List<ConfigIssue> Issues = new List<ConfigIssue>();

        private static readonly Regex KeyValue = new Regex(@"^([A-Za-z_][A-Za-z0-9_.]*)[ \t]*=(.*)$", RegexOptions.CultureInvariant);

        public ConfigSection Find(string name)
        {
            foreach (ConfigSection s in Sections)
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        public static ConfigFile Load(string path)
        {
            // ReadAllLines riconosce il BOM; senza BOM legge come UTF-8
            string[] lines = File.ReadAllLines(path, new UTF8Encoding(false));
            return Parse(lines, path);
        }

        public static ConfigFile Parse(string[] lines, string path)
        {
            var f = new ConfigFile { Path = path };
            ConfigSection cur = null;
            ConfigEntry last = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i] ?? "";
                int n = i + 1;
                string text = StripComment(raw);
                string t = text.Trim();
                if (t.Length == 0)
                {
                    // una riga di solo commento non interrompe un valore su più righe; una riga vuota sì
                    if (raw.Trim().Length == 0) last = null;
                    continue;
                }

                bool indented = raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t');

                if (!indented && t.StartsWith("[") && t.EndsWith("]"))
                {
                    cur = new ConfigSection { Name = t.Substring(1, t.Length - 2).Trim(), Line = n };
                    f.Sections.Add(cur);
                    last = null;
                    continue;
                }

                if (cur == null)
                {
                    f.Issues.Add(new ConfigIssue { Level = IssueLevel.Avviso, Line = n, Message = L.T("testo fuori da una sezione: ignorato") });
                    continue;
                }

                // continuazione del valore precedente (righe che iniziano con spazi, non con TAB)
                if (last != null && raw[0] == ' ')
                {
                    last.Value = last.Value + "\n" + t;
                    continue;
                }

                if (!indented)
                {
                    Match m = KeyValue.Match(text);
                    if (m.Success)
                    {
                        last = new ConfigEntry { Key = m.Groups[1].Value, Value = m.Groups[2].Value.Trim(), Line = n };
                        cur.Entries.Add(last);
                        continue;
                    }
                }

                last = null;
                string[] cells = text.IndexOf('\t') >= 0 ? text.Split('\t') : text.Split(';');
                int count = cells.Length;
                for (int c = 0; c < cells.Length; c++) cells[c] = cells[c].Trim();
                while (count > 1 && cells[count - 1].Length == 0) count--;
                if (count != cells.Length) Array.Resize(ref cells, count);
                cur.Rows.Add(new ConfigRow { Cells = cells, Line = n });
            }
            return f;
        }

        /// <summary>Toglie il commento: «#» a inizio riga o preceduto da spazio o TAB, fuori dalle virgolette.</summary>
        public static string StripComment(string raw)
        {
            bool quoted = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '"') quoted = !quoted;
                else if (c == '#' && !quoted && (i == 0 || raw[i - 1] == ' ' || raw[i - 1] == '\t'))
                    return raw.Substring(0, i).TrimEnd();
            }
            return raw.TrimEnd();
        }
    }
}
