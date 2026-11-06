using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ModelProof
{
    /// <summary>
    /// Finestra «Configurazione»: sceglie il file di configurazione del progetto, lo legge e lo controlla (errori,
    /// avvisi, esempi di prova delle regole) e permette di provare un valore con una regola.
    /// Resta in memoria finché Revit è aperto: chiudendola si nasconde e riaprendola ritrova lo stato.
    /// </summary>
    internal sealed class ConfigForm : MpForm
    {
        private static ConfigForm _instance;

        /// <summary>Ultima configurazione letta senza errori (la useranno i controlli).</summary>
        public static ProjectConfig Current { get; private set; }

        private readonly TextBox _path;
        private readonly Label _summary;
        private readonly TextBox _report;
        private readonly ComboBox _rule;
        private readonly TextBox _value;
        private readonly Label _result;
        private ProjectConfig _config;
        private bool _shown;

        public static void ShowWindow(IntPtr owner)
        {
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new ConfigForm();
                if (!string.IsNullOrEmpty(Settings.Current.ConfigPath) && File.Exists(Settings.Current.ConfigPath))
                    _instance.LoadConfig(Settings.Current.ConfigPath);
            }
            if (!_instance.Visible)
            {
                // la finestra madre si assegna solo la prima volta: dopo averla nascosta basta rimostrarla
                if (owner != IntPtr.Zero && !_instance._shown) _instance.Show(new RevitWindow(owner));
                else _instance.Show();
                _instance._shown = true;
            }
            _instance.Activate();
        }

        /// <summary>Configurazione da usare per i controlli: quella già letta, altrimenti l'ultimo file scelto.</summary>
        public static ProjectConfig GetOrLoad()
        {
            if (Current != null) return Current;
            string path = Settings.Current.ConfigPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                ProjectConfig c = ProjectConfig.Load(path);
                if (c.ErrorCount == 0) Current = c;
                return Current;
            }
            catch (Exception ex)
            {
                Log.Error("Configurazione", ex);
                return null;
            }
        }

        private ConfigForm()
        {
            Text = L.T("ModelProof – Configurazione");
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(900, 680);
            MinimumSize = new Size(640, 460);
            ShowInTaskbar = false;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // ---- file
            var fileRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Margin = new Padding(0) };
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fileRow.Controls.Add(new Label { Text = L.T("File:"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 0, 0);
            _path = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 6, 3) };
            fileRow.Controls.Add(_path, 1, 0);
            var choose = new Button { Text = L.T("Scegli..."), AutoSize = true };
            choose.Click += (s, e) => ChooseFile();
            var reload = new Button { Text = L.T("Rileggi"), AutoSize = true };
            reload.Click += (s, e) => { if (_path.Text.Length > 0) LoadConfig(_path.Text); };
            var edit = new Button { Text = L.T("Apri nel Blocco note"), AutoSize = true };
            edit.Click += (s, e) => OpenInNotepad();
            fileRow.Controls.Add(choose, 2, 0);
            fileRow.Controls.Add(reload, 3, 0);
            fileRow.Controls.Add(edit, 4, 0);
            root.Controls.Add(fileRow, 0, 0);

            _summary = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 6), Font = new Font("Segoe UI Semibold", 9.5f) };
            root.Controls.Add(_summary, 0, 1);

            _report = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f), BackColor = Color.White
            };
            root.Controls.Add(_report, 0, 2);

            // ---- prova di una regola
            var test = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 10, 0, 0) };
            test.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            test.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            test.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            test.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            test.Controls.Add(new Label { Text = L.T("Prova una regola:"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 0, 0);
            _rule = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 6, 3) };
            test.Controls.Add(_rule, 1, 0);
            test.Controls.Add(new Label { Text = L.T("valore:"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 2, 0);
            _value = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            test.Controls.Add(_value, 3, 0);
            _result = new Label { AutoSize = true, Margin = new Padding(0, 4, 0, 0), MaximumSize = new Size(860, 0) };
            test.Controls.Add(_result, 0, 1);
            test.SetColumnSpan(_result, 4);
            _rule.SelectedIndexChanged += (s, e) => UpdateTest();
            _value.TextChanged += (s, e) => UpdateTest();
            root.Controls.Add(test, 0, 3);

            var close = new Button { Text = L.T("Chiudi"), AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 10, 0, 0) };
            close.Click += (s, e) => Hide();
            root.Controls.Add(close, 0, 4);
            CancelButton = close;

            UiTheme.Apply(this);
            ShowEmpty();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // si nasconde soltanto: riaprendola si ritrovano file, controllo e prova
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        private void ShowEmpty()
        {
            _summary.Text = L.T("Nessun file di configurazione scelto.");
            _report.Text = L.T("Scegli il file di testo con le regole del progetto (resta sul tuo computer).") + "\r\n";
        }

        private void ChooseFile()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = L.T("File di configurazione del progetto"),
                Filter = L.T("File di testo") + " (*.txt)|*.txt|" + L.T("Tutti i file") + " (*.*)|*.*",
                CheckFileExists = true
            })
            {
                if (_path.Text.Length > 0)
                {
                    try { dlg.InitialDirectory = Path.GetDirectoryName(_path.Text); } catch { }
                }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                LoadConfig(dlg.FileName);
            }
        }

        private void OpenInNotepad()
        {
            if (_path.Text.Length == 0 || !File.Exists(_path.Text)) return;
            try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + _path.Text + "\"") { UseShellExecute = false }); }
            catch (Exception ex) { Log.Error("Configurazione", ex); }
        }

        private void LoadConfig(string path)
        {
            _path.Text = path;
            Settings.Current.ConfigPath = path;
            Settings.Current.Save();
            var sw = Stopwatch.StartNew();
            try
            {
                _config = ProjectConfig.Load(path);
            }
            catch (Exception ex)
            {
                Log.Error("Configurazione", ex);
                _config = null;
                Current = null;
                _summary.Text = L.T("Il file non si può leggere.");
                _report.Text = ex.Message;
                FillRules();
                return;
            }
            Current = _config.ErrorCount == 0 ? _config : null;
            Log.Write("Configurazione", string.Format("letto {0}: {1} regole, {2} elenchi, {3} righe checklist, {4} errori, {5} avvisi, {6} ms",
                Path.GetFileName(path), _config.Rules.Count, _config.Lists.Count, _config.Checklist.Count, _config.ErrorCount, _config.WarningCount, sw.ElapsedMilliseconds));
            foreach (ConfigIssue i in _config.Issues)
                if (i.Level == IssueLevel.Errore) Log.Warn("Configurazione", i.ToString());
            ShowReport();
            FillRules();
        }

        private void ShowReport()
        {
            ProjectConfig c = _config;
            _summary.Text = c.ErrorCount == 0
                ? L.F("Configurazione valida: {0} regole, {1} elenchi, {2} righe della checklist. Avvisi: {3}.", c.Rules.Count, c.Lists.Count, c.Checklist.Count, c.WarningCount)
                : L.F("Da correggere: {0} errori, {1} avvisi. I controlli non partono finché ci sono errori.", c.ErrorCount, c.WarningCount);
            _summary.ForeColor = c.ErrorCount == 0 ? Color.FromArgb(30, 120, 60) : Color.FromArgb(190, 40, 40);

            var sb = new StringBuilder();
            AppendIssues(sb, L.T("ERRORI"), IssueLevel.Errore);
            AppendIssues(sb, L.T("AVVISI"), IssueLevel.Avviso);

            sb.AppendLine(L.T("REGOLE"));
            foreach (Rule r in c.Rules)
            {
                string models = string.Join(", ", r.Models);
                string fmt = r.Format.Length > 0 ? r.Format : L.T("(nessun formato)");
                string tests = r.TestYes.Count + r.TestNo.Count > 0 ? L.F("   esempi: {0} giusti, {1} sbagliati", r.TestYes.Count, r.TestNo.Count) : "";
                sb.AppendLine(string.Format("  {0,-36} {1,-14} {2,-7} {3}{4}", r.Name, models, r.Severity, fmt, tests));
            }
            sb.AppendLine();

            sb.AppendLine(L.T("CHECKLIST"));
            foreach (ChecklistLink k in c.Checklist)
            {
                string what = k.Rules.Count > 0 ? string.Join(" ; ", k.Rules) : L.F("Vedasi punto {0}", string.Join(", ", k.Refs));
                string part = k.Part == "-" ? "" : " [" + k.Part + "]";
                string models = k.Models.Count > 0 ? " (" + string.Join(", ", k.Models) + ")" : "";
                sb.AppendLine(string.Format("  {0,-6} {1}{2}{3}", k.Row, what, part, models));
            }
            sb.AppendLine();

            sb.AppendLine(L.T("ELENCHI"));
            foreach (CodeList l in c.Lists.Values)
                sb.AppendLine(string.Format("  {0,-24} {1}", l.Name, L.F("{0} righe", l.Rows.Count)));
            sb.AppendLine();
            sb.AppendLine(L.F("Segnaposto: {0}. Categorie escluse: {1}.", string.Join(", ", c.Placeholders.Keys), c.Exclusions.Count));
            _report.Text = sb.ToString();
        }

        private void AppendIssues(StringBuilder sb, string title, IssueLevel level)
        {
            var list = _config.Issues.Where(i => i.Level == level).OrderBy(i => i.Line).ToList();
            if (list.Count == 0) return;
            sb.AppendLine(title + " (" + list.Count + ")");
            foreach (ConfigIssue i in list) sb.AppendLine("  " + i);
            sb.AppendLine();
        }

        private void FillRules()
        {
            string previous = _rule.SelectedItem as string;
            _rule.Items.Clear();
            if (_config != null)
                foreach (Rule r in _config.Rules)
                    if (r.Regex != null) _rule.Items.Add(r.Name);
            if (previous != null && _rule.Items.Contains(previous)) _rule.SelectedItem = previous;
            else if (_rule.Items.Count > 0) _rule.SelectedIndex = 0;
            UpdateTest();
        }

        private void UpdateTest()
        {
            if (_config == null || _rule.SelectedItem == null)
            {
                _result.Text = "";
                return;
            }
            Rule r = _config.FindRule((string)_rule.SelectedItem);
            if (_value.Text.Length == 0)
            {
                _result.ForeColor = UiTheme.Muted;
                _result.Text = L.F("Formato: {0}", r.Format);
                return;
            }
            string text = ProjectConfig.Explain(r, _value.Text);
            bool ok = r.Regex != null && SafeMatch(r, _value.Text);
            _result.ForeColor = ok ? Color.FromArgb(30, 120, 60) : Color.FromArgb(190, 40, 40);
            _result.Text = text;
        }

        private static bool SafeMatch(Rule r, string v)
        {
            try { return r.Regex.IsMatch(v); }
            catch { return false; }
        }

        /// <summary>Finestra madre = finestra principale di Revit.</summary>
        private sealed class RevitWindow : IWin32Window
        {
            public RevitWindow(IntPtr h) { Handle = h; }
            public IntPtr Handle { get; private set; }
        }
    }
}
