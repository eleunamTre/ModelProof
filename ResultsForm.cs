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
    /// Risultati della verifica: in alto l'esito di ogni regola, in basso le anomalie delle regole selezionate.
    /// Doppio clic su un'anomalia: seleziona e inquadra l'elemento in Revit. L'ultima verifica di ogni progetto resta
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
        private readonly Label _count;
        private CheckRun _run;
        private double _threshold = 20;
        private bool _shown;
        private readonly SplitContainer _split;
        private bool _splitSet;

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
            _instance._threshold = run.PartialThreshold;
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
            Size = new Size(1100, 760);
            MinimumSize = new Size(760, 480);
            ShowInTaskbar = false;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            _summary = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(0, 0, 0, 4) };
            root.Controls.Add(_summary, 0, 0);
            _notes = new Label { AutoSize = true, ForeColor = Color.FromArgb(160, 90, 0), MaximumSize = new Size(1060, 0), Margin = new Padding(0, 0, 0, 6) };
            root.Controls.Add(_notes, 0, 1);

            _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            SplitContainer split = _split;
            _rules = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true };
            _rules.Columns.Add(L.T("Regola"), 280);
            _rules.Columns.Add(L.T("Esito"), 110);
            _rules.Columns.Add(L.T("Controllati"), 85, HorizontalAlignment.Right);
            _rules.Columns.Add(L.T("Errori"), 70, HorizontalAlignment.Right);
            _rules.Columns.Add(L.T("Avvisi"), 70, HorizontalAlignment.Right);
            _rules.Columns.Add(L.T("Note"), 420);
            _rules.SelectedIndexChanged += (s, e) => FillIssues();
            split.Panel1.Controls.Add(_rules);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 2) };
            _onlyErrors = new CheckBox { Text = L.T("Solo errori"), AutoSize = true, Margin = new Padding(0, 4, 16, 0) };
            _onlyErrors.CheckedChanged += (s, e) => FillIssues();
            _count = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0), ForeColor = UiTheme.Muted };
            bar.Controls.Add(_onlyErrors);
            bar.Controls.Add(_count);
            bottom.Controls.Add(bar, 0, 0);

            _issues = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            _issues.Columns.Add(L.T("Gravità"), 70);
            _issues.Columns.Add("ID", 80, HorizontalAlignment.Right);
            _issues.Columns.Add(L.T("Categoria"), 140);
            _issues.Columns.Add(L.T("Famiglia / tipo"), 230);
            _issues.Columns.Add(L.T("Campo"), 120);
            _issues.Columns.Add(L.T("Valore"), 150);
            _issues.Columns.Add(L.T("Problema"), 360);
            _issues.Columns.Add(L.T("Regola"), 180);
            _issues.DoubleClick += (s, e) => ShowSelected();
            _issues.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) ShowSelected(); };
            bottom.Controls.Add(_issues, 0, 1);
            split.Panel2.Controls.Add(bottom);
            root.Controls.Add(split, 0, 2);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0) };
            var close = new Button { Text = L.T("Chiudi"), AutoSize = true };
            close.Click += (s, e) => Hide();
            var export = new Button { Text = L.T("Esporta CSV..."), AutoSize = true };
            export.Click += (s, e) => ExportCsv();
            var show = new Button { Text = L.T("Mostra nel modello"), AutoSize = true };
            show.Click += (s, e) => ShowSelected();
            buttons.Controls.Add(close);
            buttons.Controls.Add(export);
            buttons.Controls.Add(show);
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
            try { _split.SplitterDistance = Math.Max(120, Math.Min(280, _split.Height / 3)); } catch { }
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

            _rules.BeginUpdate();
            _rules.Items.Clear();
            foreach (RuleResult r in run.Results)
            {
                var it = new ListViewItem(r.Rule.Name) { Tag = r };
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
            _rules.EndUpdate();
            FillIssues();
        }

        /// <summary>Esito della regola come nella checklist: SI, PARZIALE (pochi oggetti con errori), NO.</summary>
        private string Outcome(RuleResult r)
        {
            if (r.Skipped) return L.T("non si applica");
            if (r.Note != null && r.Checked == 0 && r.Issues.Count == 0) return L.T("non eseguita");
            if (r.Errors == 0) return r.Warnings > 0 ? L.T("SI (avvisi)") : "SI";
            if (r.Checked == 0) return "NO";
            double share = 100.0 * r.FailedObjects / r.Checked;
            return share < _threshold ? "PARZIALE" : "NO";
        }

        private IEnumerable<Issue> VisibleIssues()
        {
            if (_run == null) return Enumerable.Empty<Issue>();
            var selected = _rules.SelectedItems.Cast<ListViewItem>().Select(i => (RuleResult)i.Tag).ToList();
            IEnumerable<RuleResult> source = selected.Count > 0 ? selected : _run.Results;
            IEnumerable<Issue> all = source.SelectMany(r => r.Issues);
            if (_onlyErrors.Checked) all = all.Where(i => i.Error);
            return all;
        }

        private void FillIssues()
        {
            List<Issue> list = VisibleIssues().ToList();
            _issues.BeginUpdate();
            _issues.Items.Clear();
            foreach (Issue i in list.Take(MaxRows))
            {
                var it = new ListViewItem(i.Error ? L.T("errore") : L.T("avviso")) { Tag = i };
                it.SubItems.Add(i.ElementId >= 0 ? i.ElementId.ToString() : "");
                it.SubItems.Add(i.Category);
                it.SubItems.Add(i.FamilyType);
                it.SubItems.Add(i.Field);
                it.SubItems.Add(i.Value);
                it.SubItems.Add(i.Message);
                it.SubItems.Add(i.Rule);
                it.ForeColor = i.Error ? Color.FromArgb(190, 40, 40) : Color.FromArgb(160, 90, 0);
                _issues.Items.Add(it);
            }
            _issues.EndUpdate();
            _count.Text = list.Count > MaxRows
                ? L.F("{0} anomalie, mostrate le prime {1} (tutte nel CSV). Doppio clic: mostra l'elemento nel modello.", list.Count, MaxRows)
                : L.F("{0} anomalie. Doppio clic: mostra l'elemento nel modello.", list.Count);
        }

        private void ShowSelected()
        {
            if (_run == null || _issues.SelectedItems.Count == 0) return;
            var i = (Issue)_issues.SelectedItems[0].Tag;
            if (i.ShowId < 0) return;
            ShowElementHandler.Request(_run.DocumentKey, i.ShowId);
        }

        // ------------------------------------------------------------------ esportazione

        private void ExportCsv()
        {
            if (_run == null) return;
            using (var dlg = new SaveFileDialog
            {
                Title = L.T("Esporta le anomalie"),
                Filter = "CSV (*.csv)|*.csv",
                FileName = _run.Model.FileName + "_ModelProof_" + _run.When.ToString("yyyyMMdd_HHmm") + ".csv"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(string.Join(";", new[] { L.T("Regola"), L.T("Gravità"), L.T("Tipo"), "ID", L.T("Categoria"), L.T("Famiglia / tipo"), L.T("Campo"), L.T("Valore"), L.T("Problema") }.Select(Csv)));
                    foreach (RuleResult r in _run.Results)
                        foreach (Issue i in r.Issues)
                            sb.AppendLine(string.Join(";", new[]
                            {
                                i.Rule, i.Error ? L.T("errore") : L.T("avviso"), i.Kind.ToString(), i.ElementId >= 0 ? i.ElementId.ToString() : "",
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
