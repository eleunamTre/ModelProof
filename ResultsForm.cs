using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ModelProof
{
    /// <summary>
    /// Risultati della verifica: in alto l'esito di ogni regola raggruppato per sezione, in basso le anomalie delle
    /// regole selezionate raggruppate per regola. Selezionando anomalie, gli elementi vengono selezionati in Revit;
    /// doppio clic li inquadra; «Isola nella vista» li isola temporaneamente. L'ultima verifica di ogni progetto resta
    /// in memoria finché Revit è aperto; chiudendo la finestra si nasconde soltanto.
    /// </summary>
    internal sealed class ResultsForm : MpForm
    {
        private const int MaxRows = 5000;

        private static ResultsForm _instance;
        private static readonly Dictionary<string, CheckRun> _runs = new Dictionary<string, CheckRun>(StringComparer.OrdinalIgnoreCase);

        private readonly Label _summary;
        private readonly Label _notes;
        private readonly ListView _rules;
        private readonly ListView _issues;
        private readonly CheckBox _onlyErrors;
        private readonly CheckBox _follow;
        private readonly Label _count;
        private readonly SplitContainer _split;
        private readonly Timer _selectTimer;
        private CheckRun _run;
        private bool _shown;
        private bool _splitSet;
        private bool _filling;

        public static CheckRun LastRun(string documentKey)
        {
            CheckRun r;
            return documentKey != null && _runs.TryGetValue(documentKey, out r) ? r : null;
        }

        public static void ShowRun(CheckRun run, IntPtr owner)
        {
            if (run == null) return;
            _runs[run.DocumentKey] = run;
            if (_instance == null || _instance.IsDisposed) _instance = new ResultsForm();
            _instance.Fill(run);
            if (!_instance.Visible)
            {
                if (owner != IntPtr.Zero && !_instance._shown) _instance.Show(new RevitWindow(owner));
                else _instance.Show();
                _instance._shown = true;
            }
            _instance.Activate();
        }

        private ResultsForm()
        {
            Text = L.T("ModelProof – Risultati della verifica");
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1150, 780);
            MinimumSize = new Size(780, 500);
            ShowInTaskbar = false;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            _summary = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(0, 0, 0, 4) };
            root.Controls.Add(_summary, 0, 0);
            _notes = new Label { AutoSize = true, ForeColor = Color.FromArgb(160, 90, 0), MaximumSize = new Size(1100, 0), Margin = new Padding(0, 0, 0, 6) };
            root.Controls.Add(_notes, 0, 1);

            _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            _rules = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, ShowGroups = true };
            _rules.Columns.Add(L.T("Regola"), 280);
            _rules.Columns.Add(L.T("Esito"), 110);
            _rules.Columns.Add(L.T("Controllati"), 85, HorizontalAlignment.Right);
            _rules.Columns.Add(L.T("Errori"), 70, HorizontalAlignment.Right);
            _rules.Columns.Add(L.T("Avvisi"), 70, HorizontalAlignment.Right);
            _rules.Columns.Add(L.T("Note"), 450);
            _rules.SelectedIndexChanged += (s, e) => { if (!_filling) FillIssues(); };
            _split.Panel1.Controls.Add(_rules);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 2) };
            _onlyErrors = new CheckBox { Text = L.T("Solo errori"), AutoSize = true, Margin = new Padding(0, 4, 16, 0) };
            _onlyErrors.CheckedChanged += (s, e) => FillIssues();
            _follow = new CheckBox { Text = L.T("Seleziona nel modello"), AutoSize = true, Checked = true, Margin = new Padding(0, 4, 16, 0) };
            _count = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0), ForeColor = UiTheme.Muted };
            bar.Controls.Add(_onlyErrors);
            bar.Controls.Add(_follow);
            bar.Controls.Add(_count);
            bottom.Controls.Add(bar, 0, 0);

            _issues = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, ShowGroups = true };
            _issues.Columns.Add(L.T("Gravità"), 70);
            _issues.Columns.Add("ID", 80, HorizontalAlignment.Right);
            _issues.Columns.Add(L.T("Categoria"), 140);
            _issues.Columns.Add(L.T("Famiglia / tipo"), 240);
            _issues.Columns.Add(L.T("Campo"), 120);
            _issues.Columns.Add(L.T("Valore"), 160);
            _issues.Columns.Add(L.T("Problema"), 380);
            _issues.DoubleClick += (s, e) => Send(SelectedIssues(), ShowMode.Zoom);
            _issues.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) Send(SelectedIssues(), ShowMode.Zoom);
                else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem it in _issues.Items) it.Selected = true; }
            };
            // selezionando righe gli elementi si selezionano anche in Revit (con un attimo di ritardo, per non
            // mandare una richiesta a ogni riga mentre si scorre con le frecce)
            _selectTimer = new Timer { Interval = 300 };
            _selectTimer.Tick += (s, e) =>
            {
                _selectTimer.Stop();
                if (_follow.Checked && _issues.SelectedItems.Count > 0) Send(SelectedIssues(), ShowMode.Select);
            };
            _issues.SelectedIndexChanged += (s, e) => { if (_filling) return; _selectTimer.Stop(); _selectTimer.Start(); };
            bottom.Controls.Add(_issues, 0, 1);
            _split.Panel2.Controls.Add(bottom);
            root.Controls.Add(_split, 0, 2);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0) };
            var close = new Button { Text = L.T("Chiudi"), AutoSize = true };
            close.Click += (s, e) => Hide();
            var excel = new Button { Text = L.T("Esporta Excel..."), AutoSize = true };
            excel.Click += (s, e) => ExportExcel();
            var csv = new Button { Text = L.T("CSV..."), AutoSize = true };
            csv.Click += (s, e) => ExportCsv();
            var reset = new Button { Text = L.T("Ripristina vista"), AutoSize = true };
            reset.Click += (s, e) => { if (_run != null) ShowElementHandler.Request(_run.DocumentKey, new long[0], ShowMode.Reset); };
            var isolate = new Button { Text = L.T("Isola nella vista"), AutoSize = true };
            isolate.Click += (s, e) => Send(SelectedOrVisible(), ShowMode.Isolate);
            var zoom = new Button { Text = L.T("Inquadra"), AutoSize = true };
            zoom.Click += (s, e) => Send(SelectedOrVisible(), ShowMode.Zoom);
            var select = new Button { Text = L.T("Seleziona tutte"), AutoSize = true };
            select.Click += (s, e) => Send(VisibleIssues(), ShowMode.Select);
            buttons.Controls.Add(close);
            buttons.Controls.Add(excel);
            buttons.Controls.Add(csv);
            buttons.Controls.Add(reset);
            buttons.Controls.Add(isolate);
            buttons.Controls.Add(zoom);
            buttons.Controls.Add(select);
            root.Controls.Add(buttons, 0, 3);
            CancelButton = close;

            UiTheme.Apply(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // la divisione si imposta quando la finestra ha la sua altezza (prima darebbe errore)
            if (_splitSet) return;
            _splitSet = true;
            try { _split.SplitterDistance = Math.Max(140, Math.Min(300, _split.Height * 2 / 5)); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------ riempimento

        private void Fill(CheckRun run)
        {
            _run = run;
            ModelInfo m = run.Model;
            _summary.Text = L.F("{0}  –  modello {1}, edificio {2}  –  {3} errori, {4} avvisi  –  {5:dd/MM/yyyy HH:mm}, {6:F1} s",
                m.FileName, m.RoleText, m.Building ?? "?", run.Errors, run.Warnings, run.When, run.Seconds);
            _summary.ForeColor = run.Errors == 0 ? Color.FromArgb(30, 120, 60) : Color.FromArgb(190, 40, 40);
            _notes.Text = string.Join("\n", run.Notes);
            _notes.Visible = run.Notes.Count > 0;

            _filling = true;
            _rules.BeginUpdate();
            _rules.Items.Clear();
            _rules.Groups.Clear();
            var groups = new Dictionary<string, ListViewGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (RuleResult r in run.Results)
            {
                string chapter = r.Rule.Chapter ?? r.Rule.Name;
                ListViewGroup g;
                if (!groups.TryGetValue(chapter, out g))
                {
                    g = new ListViewGroup(chapter, chapter);
                    groups[chapter] = g;
                    _rules.Groups.Add(g);
                }
                var it = new ListViewItem(r.Rule.Name, g) { Tag = r };
                it.SubItems.Add(Outcome(r));
                it.SubItems.Add(r.Skipped ? "" : r.Checked.ToString());
                it.SubItems.Add(r.Skipped ? "" : r.Errors.ToString());
                it.SubItems.Add(r.Skipped ? "" : r.Warnings.ToString());
                it.SubItems.Add(r.Note ?? "");
                if (r.Skipped || (r.Note != null && r.Checked == 0)) it.ForeColor = UiTheme.Muted;
                else if (r.Errors > 0) it.ForeColor = Color.FromArgb(190, 40, 40);
                else if (r.Warnings > 0) it.ForeColor = Color.FromArgb(160, 90, 0);
                _rules.Items.Add(it);
            }
            // intestazioni delle sezioni con i totali
            foreach (var kv in groups)
            {
                var results = run.Results.Where(r => string.Equals(r.Rule.Chapter ?? r.Rule.Name, kv.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                kv.Value.Header = L.F("{0}  –  {1} errori, {2} avvisi", kv.Key, results.Sum(r => r.Errors), results.Sum(r => r.Warnings));
            }
            _rules.EndUpdate();
            _filling = false;
            FillIssues();
        }

        /// <summary>Esito della regola come nella checklist: SI, PARZIALE (pochi oggetti con errori), NO.</summary>
        private string Outcome(RuleResult r)
        {
            return OutcomeText(r, _run != null ? _run.PartialThreshold : 20);
        }

        public static string OutcomeText(RuleResult r, double threshold)
        {
            if (r.Skipped) return L.T("non si applica");
            if (r.Note != null && r.Checked == 0 && r.Issues.Count == 0) return L.T("non eseguita");
            if (r.Errors == 0) return r.Warnings > 0 ? L.T("SI (avvisi)") : "SI";
            if (r.Checked == 0) return "NO";
            double share = 100.0 * r.FailedObjects / r.Checked;
            return share < threshold ? "PARZIALE" : "NO";
        }

        private List<Issue> VisibleIssues()
        {
            if (_run == null) return new List<Issue>();
            var selected = _rules.SelectedItems.Cast<ListViewItem>().Select(i => (RuleResult)i.Tag).ToList();
            IEnumerable<RuleResult> source = selected.Count > 0 ? selected : _run.Results;
            IEnumerable<Issue> all = source.SelectMany(r => r.Issues);
            if (_onlyErrors.Checked) all = all.Where(i => i.Error);
            return all.OrderBy(i => i.Priority).ToList();   // OrderBy è stabile: dentro la priorità resta l'ordine della verifica
        }

        private List<Issue> SelectedIssues()
        {
            return _issues.SelectedItems.Cast<ListViewItem>().Select(i => (Issue)i.Tag).ToList();
        }

        private List<Issue> SelectedOrVisible()
        {
            List<Issue> sel = SelectedIssues();
            return sel.Count > 0 ? sel : VisibleIssues();
        }

        private void Send(List<Issue> issues, ShowMode mode)
        {
            if (_run == null || issues.Count == 0) return;
            ShowElementHandler.Request(_run.DocumentKey, issues.Select(i => i.ShowId), mode);
        }

        private void FillIssues()
        {
            List<Issue> list = VisibleIssues();
            _filling = true;
            _issues.BeginUpdate();
            _issues.Items.Clear();
            _issues.Groups.Clear();
            var groups = new Dictionary<string, ListViewGroup>(StringComparer.Ordinal);
            foreach (Issue i in list.Take(MaxRows))
            {
                ListViewGroup g;
                if (!groups.TryGetValue(i.Rule, out g))
                {
                    int n = list.Count(x => x.Rule == i.Rule);
                    g = new ListViewGroup(i.Rule, L.F("{0}  –  {1} anomalie", i.Rule, n));
                    groups[i.Rule] = g;
                    _issues.Groups.Add(g);
                }
                var it = new ListViewItem(i.LevelText, g) { Tag = i };
                it.SubItems.Add(i.ElementId >= 0 ? i.ElementId.ToString() : "");
                it.SubItems.Add(i.Category);
                it.SubItems.Add(i.FamilyType);
                it.SubItems.Add(i.Field);
                it.SubItems.Add(i.Value);
                it.SubItems.Add(i.Message);
                it.ForeColor = i.Priority == 0 ? Color.FromArgb(190, 40, 40) : i.Priority == 1 ? Color.FromArgb(120, 60, 170) : Color.FromArgb(160, 90, 0);
                _issues.Items.Add(it);
            }
            _issues.EndUpdate();
            _filling = false;
            _count.Text = list.Count > MaxRows
                ? L.F("{0} anomalie, mostrate le prime {1} (tutte nell'Excel). Doppio clic: inquadra.", list.Count, MaxRows)
                : L.F("{0} anomalie. Doppio clic: inquadra; Ctrl+A: tutte.", list.Count);
        }

        // ------------------------------------------------------------------ esportazione

        private string DefaultName(string ext)
        {
            return _run.Model.FileName + "_ModelProof_" + _run.When.ToString("yyyyMMdd_HHmm") + ext;
        }

        /// <summary>Excel con un foglio di sintesi e un foglio per ogni sezione con anomalie.</summary>
        private void ExportExcel()
        {
            if (_run == null) return;
            using (var dlg = new SaveFileDialog { Title = L.T("Esporta i risultati"), Filter = "Excel (*.xlsx)|*.xlsx", FileName = DefaultName(".xlsx") })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ReportWriter.WriteExcel(_run, dlg.FileName);
                    Log.Write("Verifica", "esportato " + Path.GetFileName(dlg.FileName));
                }
                catch (Exception ex)
                {
                    Log.Error("Esporta Excel", ex);
                    MessageBox.Show(this, ex.Message, "ModelProof", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void ExportCsv()
        {
            if (_run == null) return;
            using (var dlg = new SaveFileDialog { Title = L.T("Esporta le anomalie"), Filter = "CSV (*.csv)|*.csv", FileName = DefaultName(".csv") })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(string.Join(";", new[] { L.T("Sezione"), L.T("Regola"), L.T("Gravità"), L.T("Tipo"), "ID", L.T("Categoria"), L.T("Famiglia / tipo"), L.T("Campo"), L.T("Valore"), L.T("Problema") }.Select(Csv)));
                    foreach (RuleResult r in _run.Results)
                        foreach (Issue i in r.Issues)
                            sb.AppendLine(string.Join(";", new[]
                            {
                                r.Rule.Chapter, i.Rule, i.LevelText, i.Kind.ToString(), i.ElementId >= 0 ? i.ElementId.ToString() : "",
                                i.Category, i.FamilyType, i.Field, i.Value, i.Message
                            }.Select(Csv)));
                    File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                    Log.Write("Verifica", "esportate " + _run.Results.Sum(r => r.Issues.Count) + " anomalie in " + Path.GetFileName(dlg.FileName));
                }
                catch (Exception ex)
                {
                    Log.Error("Esporta CSV", ex);
                    MessageBox.Show(this, ex.Message, "ModelProof", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static string Csv(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return s.IndexOfAny(new[] { ';', '"' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        private sealed class RevitWindow : IWin32Window
        {
            public RevitWindow(IntPtr h) { Handle = h; }
            public IntPtr Handle { get; private set; }
        }
    }
}
