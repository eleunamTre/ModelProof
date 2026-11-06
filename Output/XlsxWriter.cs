using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ModelProof
{
    /// <summary>
    /// Scrittura di un file Excel (.xlsx) senza librerie esterne: più fogli, riga di intestazione in grassetto e fissa,
    /// filtro automatico, larghezza delle colonne, testo rosso/arancio per righe di errore/avviso.
    /// </summary>
    internal sealed class XlsxWriter
    {
        /// <summary>Stile di una riga.</summary>
        public enum RowStyle { Normal = 0, Header = 1, Error = 2, Warning = 3, Title = 4, Unlisted = 5 }

        private sealed class Sheet
        {
            public string Name;
            public readonly List<object[]> Rows = new List<object[]>();
            public readonly List<RowStyle> Styles = new List<RowStyle>();
            public double[] Widths;
            public bool HeaderRow;     // prima riga = intestazione (fissa e con filtro)
        }

        private readonly List<Sheet> _sheets = new List<Sheet>();
        private readonly HashSet<string> _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Nuovo foglio; il nome viene reso valido per Excel (max 31 caratteri, senza []:*?/\ e unico).</summary>
        public int AddSheet(string name, double[] widths, bool headerRow)
        {
            var s = new Sheet { Name = UniqueName(name), Widths = widths, HeaderRow = headerRow };
            _sheets.Add(s);
            return _sheets.Count - 1;
        }

        public void AddRow(int sheet, RowStyle style, params object[] cells)
        {
            _sheets[sheet].Rows.Add(cells);
            _sheets[sheet].Styles.Add(style);
        }

        private string UniqueName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "")
                sb.Append("[]:*?/\\".IndexOf(c) >= 0 ? '_' : c);
            string n = sb.ToString().Trim().Trim('\'');
            if (n.Length == 0) n = "Foglio";
            if (n.Length > 31) n = n.Substring(0, 31);
            string candidate = n;
            for (int i = 2; _names.Contains(candidate); i++)
            {
                string suffix = " (" + i + ")";
                candidate = (n.Length + suffix.Length > 31 ? n.Substring(0, 31 - suffix.Length) : n) + suffix;
            }
            _names.Add(candidate);
            return candidate;
        }

        public void Save(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            using (FileStream fs = File.Create(path))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Put(zip, "[Content_Types].xml", ContentTypes());
                Put(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");
                Put(zip, "xl/workbook.xml", Workbook());
                Put(zip, "xl/_rels/workbook.xml.rels", WorkbookRels());
                Put(zip, "xl/styles.xml", Styles());
                for (int i = 0; i < _sheets.Count; i++)
                    Put(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", SheetXml(_sheets[i]));
            }
        }

        private static void Put(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream s = e.Open())
            {
                byte[] b = new UTF8Encoding(false).GetBytes(content);
                s.Write(b, 0, b.Length);
            }
        }

        private string ContentTypes()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 0; i < _sheets.Count; i++)
                sb.Append("<Override PartName=\"/xl/worksheets/sheet" + (i + 1) + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            sb.Append("</Types>");
            return sb.ToString();
        }

        private string Workbook()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            for (int i = 0; i < _sheets.Count; i++)
                sb.Append("<sheet name=\"" + Esc(_sheets[i].Name) + "\" sheetId=\"" + (i + 1) + "\" r:id=\"rId" + (i + 1) + "\"/>");
            sb.Append("</sheets>");
            // nomi definiti per il filtro automatico (Excel li vuole per mostrare i filtri come propri)
            var defs = new StringBuilder();
            for (int i = 0; i < _sheets.Count; i++)
            {
                Sheet s = _sheets[i];
                if (!s.HeaderRow || s.Rows.Count < 1) continue;
                defs.Append("<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"" + i + "\" hidden=\"1\">'" + Esc(s.Name.Replace("'", "''")) + "'!" + AbsRange(s) + "</definedName>");
            }
            if (defs.Length > 0) sb.Append("<definedNames>").Append(defs).Append("</definedNames>");
            sb.Append("</workbook>");
            return sb.ToString();
        }

        private string WorkbookRels()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < _sheets.Count; i++)
                sb.Append("<Relationship Id=\"rId" + (i + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + (i + 1) + ".xml\"/>");
            sb.Append("<Relationship Id=\"rId" + (_sheets.Count + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        /// <summary>Stili: 0 normale, 1 intestazione (grassetto, sfondo grigio), 2 errore (rosso), 3 avviso (arancio), 4 titolo.</summary>
        private static string Styles()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"6\">" +
                "<font><sz val=\"10\"/><name val=\"Calibri\"/></font>" +
                "<font><b/><sz val=\"10\"/><name val=\"Calibri\"/></font>" +
                "<font><sz val=\"10\"/><color rgb=\"FFBE2828\"/><name val=\"Calibri\"/></font>" +
                "<font><sz val=\"10\"/><color rgb=\"FFA05A00\"/><name val=\"Calibri\"/></font>" +
                "<font><b/><sz val=\"13\"/><name val=\"Calibri\"/></font>" +
                "<font><sz val=\"10\"/><color rgb=\"FF783CAA\"/><name val=\"Calibri\"/></font>" +
                "</fonts>" +
                "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9D9D9\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"6\">" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"><alignment vertical=\"top\"/></xf>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"><alignment vertical=\"top\"/></xf>" +
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"><alignment vertical=\"top\"/></xf>" +
                "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"><alignment vertical=\"top\"/></xf>" +
                "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"5\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"><alignment vertical=\"top\"/></xf>" +
                "</cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                "</styleSheet>";
        }

        private static string SheetXml(Sheet s)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            if (s.HeaderRow)
                sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            if (s.Widths != null && s.Widths.Length > 0)
            {
                sb.Append("<cols>");
                for (int c = 0; c < s.Widths.Length; c++)
                    sb.Append("<col min=\"" + (c + 1) + "\" max=\"" + (c + 1) + "\" width=\"" + s.Widths[c].ToString("0.##", CultureInfo.InvariantCulture) + "\" customWidth=\"1\"/>");
                sb.Append("</cols>");
            }
            sb.Append("<sheetData>");
            for (int r = 0; r < s.Rows.Count; r++)
            {
                object[] row = s.Rows[r];
                int style = (int)s.Styles[r];
                sb.Append("<row r=\"" + (r + 1) + "\">");
                for (int c = 0; c < row.Length; c++)
                {
                    object v = row[c];
                    if (v == null) continue;
                    string refc = Col(c) + (r + 1);
                    if (v is int || v is long || v is double || v is float)
                    {
                        string num = Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
                        sb.Append("<c r=\"" + refc + "\" s=\"" + style + "\"><v>" + num + "</v></c>");
                    }
                    else
                    {
                        string text = v.ToString();
                        if (text.Length == 0) continue;
                        if (text.Length > 32000) text = text.Substring(0, 32000);
                        sb.Append("<c r=\"" + refc + "\" s=\"" + style + "\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" + Esc(text) + "</t></is></c>");
                    }
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");
            if (s.HeaderRow && s.Rows.Count > 0) sb.Append("<autoFilter ref=\"" + Range(s) + "\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static int MaxCols(Sheet s)
        {
            int n = 1;
            foreach (object[] r in s.Rows) n = Math.Max(n, r.Length);
            return n;
        }

        private static string Range(Sheet s)
        {
            return "A1:" + Col(MaxCols(s) - 1) + Math.Max(1, s.Rows.Count);
        }

        private static string AbsRange(Sheet s)
        {
            return "$A$1:$" + Col(MaxCols(s) - 1) + "$" + Math.Max(1, s.Rows.Count);
        }

        /// <summary>Lettera della colonna (0 = A).</summary>
        private static string Col(int index)
        {
            string s = "";
            int n = index + 1;
            while (n > 0)
            {
                int m = (n - 1) % 26;
                s = (char)('A' + m) + s;
                n = (n - 1) / 26;
            }
            return s;
        }

        private static string Esc(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default:
                        // caratteri di controllo non ammessi in XML (tranne TAB, a capo)
                        if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') break;
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
