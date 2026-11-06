using System;
using System.Collections.Generic;
using System.Linq;

namespace ModelProof
{
    /// <summary>Report di dettaglio in Excel: foglio di sintesi e un foglio per ogni sezione con anomalie.</summary>
    internal static class ReportWriter
    {
        public static string KindText(IssueKind k)
        {
            switch (k)
            {
                case IssueKind.Mancante: return L.T("mancante");
                case IssueKind.Formato: return L.T("formato");
                case IssueKind.Elenco: return L.T("non in elenco");
                case IssueKind.Coerenza: return L.T("coerenza");
                case IssueKind.Numerazione: return L.T("numerazione");
                default: return L.T("controllo");
            }
        }

        private static object Val(double[] p, int k)
        {
            return p == null ? null : (object)Math.Round(p[k], 3);
        }

        public static void WriteExcel(CheckRun run, string path)
        {
            var x = new XlsxWriter();
            ModelInfo m = run.Model;

            // ---- sintesi
            int s = x.AddSheet(L.T("Sintesi"), new double[] { 30, 36, 16, 12, 10, 10, 70 }, false);
            x.AddRow(s, XlsxWriter.RowStyle.Title, L.T("ModelProof – verifica del modello"));
            x.AddRow(s, XlsxWriter.RowStyle.Normal, L.T("File"), m.FileName);
            x.AddRow(s, XlsxWriter.RowStyle.Normal, L.T("Modello"), m.RoleText);
            x.AddRow(s, XlsxWriter.RowStyle.Normal, L.T("Edificio"), m.Building ?? "?");
            x.AddRow(s, XlsxWriter.RowStyle.Normal, L.T("Data"), run.When.ToString("dd/MM/yyyy HH:mm"));
            x.AddRow(s, XlsxWriter.RowStyle.Normal, L.T("Durata (s)"), Math.Round(run.Seconds, 1));
            x.AddRow(s, XlsxWriter.RowStyle.Normal, L.T("Regole"), run.ConfigPath ?? "");
            x.AddRow(s, run.Errors > 0 ? XlsxWriter.RowStyle.Error : XlsxWriter.RowStyle.Normal, L.T("Errori"), run.Errors);
            x.AddRow(s, run.Warnings > 0 ? XlsxWriter.RowStyle.Warning : XlsxWriter.RowStyle.Normal, L.T("Avvisi"), run.Warnings);
            foreach (string note in run.Notes) x.AddRow(s, XlsxWriter.RowStyle.Warning, L.T("Nota"), note);
            x.AddRow(s, XlsxWriter.RowStyle.Normal);
            x.AddRow(s, XlsxWriter.RowStyle.Header, L.T("Sezione"), L.T("Regola"), L.T("Esito"), L.T("Controllati"), L.T("Errori"), L.T("Avvisi"), L.T("Note"));
            foreach (RuleResult r in run.Results)
            {
                XlsxWriter.RowStyle st = r.Errors > 0 ? XlsxWriter.RowStyle.Error : r.Warnings > 0 ? XlsxWriter.RowStyle.Warning : XlsxWriter.RowStyle.Normal;
                x.AddRow(s, st, r.Rule.Chapter, r.Rule.Name, ResultsForm.OutcomeText(r, run.PartialThreshold),
                    r.Skipped ? null : (object)r.Checked, r.Skipped ? null : (object)r.Errors, r.Skipped ? null : (object)r.Warnings, r.Note ?? "");
            }

            // ---- dettaglio delle coordinate: il file verificato e i collegamenti
            if (run.Coordinates.Count > 0)
            {
                int c = x.AddSheet(L.T("Coordinate"), new double[] { 8, 40, 14, 13, 13, 10, 13, 13, 10, 14, 12, 14, 14, 11, 11, 70 }, true);
                x.AddRow(c, XlsxWriter.RowStyle.Header, L.T("Modello"), L.T("File"), L.T("Ruolo nella verifica"),
                    "SP E/W (m)", "SP N/S (m)", "SP Q (m)", "PBP E/W (m)", "PBP N/S (m)", "PBP Q (m)", L.T("Nord di progetto → nord reale (PBP)"), L.T("Nord reale (°)"),
                    L.T("Coordinate condivise nel file"), L.T("Collegato in coordinate condivise"), L.T("Scarto (mm)"), L.T("Rotazione (°)"), L.T("Problemi"));
                foreach (CoordinateRow r in run.Coordinates)
                    x.AddRow(c, r.Problems.Count > 0 ? XlsxWriter.RowStyle.Error : XlsxWriter.RowStyle.Normal,
                        r.Role, r.File, r.Host ? L.T("file verificato") : L.T("collegamento"),
                        Val(r.Survey, 0), Val(r.Survey, 1), Val(r.Survey, 2), Val(r.BasePoint, 0), Val(r.BasePoint, 1), Val(r.BasePoint, 2),
                        double.IsNaN(r.ProjectNorth) ? null : (object)Math.Round(r.ProjectNorth, 4),
                        double.IsNaN(r.North) ? null : (object)Math.Round(r.North, 4),
                        r.SharedDefined ? L.T("sì") : L.T("no"),
                        r.LinkShared == null ? "" : r.LinkShared.Value ? L.T("sì") : L.T("no"),
                        r.Host ? null : (object)Math.Round(r.LinkOffsetMm, 1), r.Host ? null : (object)Math.Round(r.LinkRotDeg, 4),
                        r.Problems.Count > 0 ? string.Join("; ", r.Problems) : L.T("Conforme"));
            }

            // ---- un foglio per sezione, nell'ordine delle regole
            var chapters = new List<string>();
            foreach (RuleResult r in run.Results)
                if (r.Issues.Count > 0 && !chapters.Contains(r.Rule.Chapter)) chapters.Add(r.Rule.Chapter);
            foreach (string chapter in chapters)
            {
                int sh = x.AddSheet(chapter, new double[] { 28, 9, 12, 8, 34, 11, 22, 40, 22, 26, 70 }, true);
                x.AddRow(sh, XlsxWriter.RowStyle.Header, L.T("Regola"), L.T("Gravità"), L.T("Tipo"), L.T("Modello"), L.T("File"), "ID", L.T("Categoria"),
                    L.T("Famiglia / tipo"), L.T("Campo"), L.T("Valore"), L.T("Problema"));
                foreach (RuleResult r in run.Results.Where(r => r.Rule.Chapter == chapter))
                    foreach (Issue i in r.Issues.OrderBy(i => i.Priority))
                        x.AddRow(sh, i.Priority == 0 ? XlsxWriter.RowStyle.Error : i.Priority == 1 ? XlsxWriter.RowStyle.Unlisted : i.Priority == 2 ? XlsxWriter.RowStyle.Warning : XlsxWriter.RowStyle.Normal,
                            i.Rule, i.LevelText, KindText(i.Kind), i.Role, i.File, i.ElementId >= 0 ? (object)i.ElementId : null,
                            i.Category, i.FamilyType, i.Field, i.Value, i.Message);
            }
            x.Save(path);
        }
    }
}
