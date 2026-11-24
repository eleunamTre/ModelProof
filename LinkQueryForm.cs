using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ModelProof
{
    /// <summary>Elemento di un modello collegato scelto con «Interroga collegamento».</summary>
    internal sealed class LinkedPick
    {
        public string LinkFile = "";
        public long LinkInstanceId;
        public long Id;
        public string UniqueId = "";
        public string Category = "";
        public string FamilyType = "";
        public string Level = "";
        public readonly List<string[]> Parameters = new List<string[]>();   // gruppo (istanza/tipo), nome, valore
    }

    /// <summary>
    /// Finestra «Interroga collegamento», aperta accanto a Revit: «Scegli a clic…» passa a Revit e ogni clic su un
    /// elemento di un collegamento lo aggiunge all'elenco (Esc finisce); «Scegli con riquadro…» usa la selezione
    /// multipla di Revit (poi «Fine»). Selezionando righe si vedono i parametri (di una) e gli elementi si evidenziano
    /// nel modello; dal clic destro si inquadrano nella vista attiva. ID e valori si copiano con Ctrl+C, clic destro o i pulsanti; doppio clic su
    /// una riga copia il suo ID. Chiudendo la finestra si nasconde soltanto e l'elenco resta.
    /// </summary>
    internal sealed class LinkQueryForm : MpForm
    {
        /// <summary>Impostato dal lato Revit: chiede la scelta degli elementi (true = riquadro, false = a clic).</summary>
        public static Action<bool> PickRequested;

        /// <summary>Impostato dal lato Revit: evidenzia nel modello gli elementi (true = anche inquadra nella vista).</summary>
        public static Action<IEnumerable<LinkedPick>, bool> ShowRequested;

        private static LinkQueryForm _instance;
        private bool _shown;
        private readonly ListView _picks;
        private readonly ListView _params;
        private readonly Label _status;
        private readonly List<LinkedPick> _items = new List<LinkedPick>();
        private readonly CheckBox _highlight;
        private readonly Timer _highlightTimer = new Timer { Interval = 300 };
        private bool _picking;

        public static void Open(IntPtr owner)
        {
            if (_instance == null || _instance.IsDisposed) _instance = new LinkQueryForm();
            if (!_instance.Visible)
            {
                if (owner != IntPtr.Zero && !_instance._shown) _instance.Show(new RevitWindow(owner));
                else _instance.Show();
                _instance._shown = true;
            }
            _instance.Activate();
        }

        /// <summary>
        /// Elementi scelti in Revit: si aggiungono all'elenco (senza doppioni) e si selezionano. Durante la scelta a
        /// clic la finestra non si attiva, perché toglierebbe a Revit il clic successivo.
        /// </summary>
        public static void AddPicks(List<LinkedPick> picks, bool activate)
        {
            if (_instance == null || _instance.IsDisposed) return;
            _instance.Add(picks, activate);
        }

        /// <summary>Fine della scelta in Revit: la finestra torna in primo piano.</summary>
        public static void PickFinished()
        {
            if (_instance == null || _instance.IsDisposed) return;
            _instance._picking = false;
            _instance.UpdateStatus();
            if (!_instance.Visible) _instance.Show();
            _instance.Activate();
            _instance._picks.Focus();
        }

        private sealed class RevitWindow : IWin32Window
        {
            public RevitWindow(IntPtr h) { Handle = h; }
            public IntPtr Handle { get; private set; }
        }

        private LinkQueryForm()
        {
            Text = L.T("ModelProof – Interroga collegamento");
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1000, 620);
            MinimumSize = new Size(640, 380);
            ShowInTaskbar = false;
            Padding = new Padding(10);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
            var pick = new Button { Text = L.T("Scegli a clic…"), AutoSize = true };
            pick.Click += (s, e) =>
            {
                if (PickRequested == null || _picking) return;
                _picking = true;
                _status.Text = L.T("In Revit: clicca gli elementi dei collegamenti, uno alla volta; ognuno si aggiunge subito. Esc per finire.");
                PickRequested(false);
            };
            var box = new Button { Text = L.T("Scegli con riquadro…"), AutoSize = true };
            box.Click += (s, e) =>
            {
                if (PickRequested == null || _picking) return;
                _picking = true;
                _status.Text = L.T("In Revit: racchiudi gli elementi in un riquadro, poi «Fine» nella barra delle opzioni. Esc annulla.");
                PickRequested(true);
            };
            var clear = new Button { Text = L.T("Svuota"), AutoSize = true };
            clear.Click += (s, e) => { _items.Clear(); _picks.Items.Clear(); _params.Items.Clear(); UpdateStatus(); };
            _status = new Label { AutoSize = true, Margin = new Padding(12, 8, 0, 0), ForeColor = UiTheme.Muted };
            _highlight = new CheckBox { Text = L.T("Evidenzia nel modello"), AutoSize = true, Checked = true, Margin = new Padding(12, 6, 0, 0) };
            _highlight.CheckedChanged += (s, e) => { if (_highlight.Checked) RequestHighlight(); };
            _highlightTimer.Tick += (s, e) => { _highlightTimer.Stop(); Highlight(false); };
            top.Controls.Add(pick);
            top.Controls.Add(box);
            top.Controls.Add(clear);
            top.Controls.Add(_highlight);
            top.Controls.Add(_status);

            _picks = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, ShowItemToolTips = true };
            _picks.Columns.Add("ID", 90, HorizontalAlignment.Right);
            _picks.Columns.Add(L.T("File"), 250);
            _picks.Columns.Add(L.T("Categoria"), 140);
            _picks.Columns.Add(L.T("Famiglia / tipo"), 280);
            _picks.Columns.Add(L.T("Livello"), 150);
            _picks.SelectedIndexChanged += (s, e) => { FillParameters(); RequestHighlight(); };
            _picks.DoubleClick += (s, e) => CopyIds();
            _picks.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C) { CopyIds(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem it in _picks.Items) it.Selected = true; e.Handled = true; }
            };
            var pickMenu = new ContextMenuStrip();
            pickMenu.Items.Add(L.T("Evidenzia nel modello"), null, (s, e) => Highlight(false));
            pickMenu.Items.Add(L.T("Inquadra nella vista"), null, (s, e) => Highlight(true));
            pickMenu.Items.Add(new ToolStripSeparator());
            pickMenu.Items.Add(L.T("Copia ID"), null, (s, e) => CopyIds());
            pickMenu.Items.Add(L.T("Copia righe"), null, (s, e) => CopyRows());
            pickMenu.Items.Add(L.T("Copia UniqueId"), null, (s, e) => SetClipboard(string.Join(Environment.NewLine, Selected().Select(p => p.UniqueId))));
            pickMenu.Items.Add(new ToolStripSeparator());
            pickMenu.Items.Add(L.T("Togli dall'elenco"), null, (s, e) => RemoveSelected());
            _picks.ContextMenuStrip = pickMenu;

            _params = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, ShowGroups = true };
            _params.Columns.Add(L.T("Parametro"), 260);
            _params.Columns.Add(L.T("Valore"), 420);
            _params.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C) { CopyParameters(false); e.Handled = true; } };
            var paramMenu = new ContextMenuStrip();
            paramMenu.Items.Add(L.T("Copia valore"), null, (s, e) => CopyParameters(true));
            paramMenu.Items.Add(L.T("Copia parametro e valore"), null, (s, e) => CopyParameters(false));
            _params.ContextMenuStrip = paramMenu;

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            split.Panel1.Controls.Add(_picks);
            split.Panel2.Controls.Add(_params);
            Shown += (s, e) => { try { split.SplitterDistance = Math.Max(120, split.Height * 2 / 5); } catch { } };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            var close = new Button { Text = L.T("Chiudi"), AutoSize = true };
            close.Click += (s, e) => Hide();
            var copyAll = new Button { Text = L.T("Copia righe"), AutoSize = true };
            copyAll.Click += (s, e) => CopyRows();
            var copyIds = new Button { Text = L.T("Copia ID"), AutoSize = true };
            copyIds.Click += (s, e) => CopyIds();
            buttons.Controls.Add(close);
            buttons.Controls.Add(copyAll);
            buttons.Controls.Add(copyIds);

            Controls.Add(split);
            Controls.Add(buttons);
            Controls.Add(top);
            CancelButton = close;
            UiTheme.Apply(this);
            UpdateStatus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }

        /// <summary>Evidenziazione rimandata di poco: con Maiusc/Ctrl la selezione cambia più volte di seguito.</summary>
        private void RequestHighlight()
        {
            if (!_highlight.Checked || _picking) return;
            _highlightTimer.Stop();
            _highlightTimer.Start();
        }

        /// <summary>Seleziona in Revit gli elementi delle righe selezionate; con zoom li inquadra nella vista attiva.</summary>
        private void Highlight(bool zoom)
        {
            if (ShowRequested == null || _picking) return;
            List<LinkedPick> sel = _picks.SelectedItems.Cast<ListViewItem>().Select(i => (LinkedPick)i.Tag).ToList();
            if (sel.Count == 0 && !zoom) return;
            ShowRequested(sel.Count > 0 ? sel : _items, zoom);
        }

        private void Add(List<LinkedPick> picks, bool activate)
        {
            _picks.BeginUpdate();
            foreach (ListViewItem it in _picks.Items) it.Selected = false;
            foreach (LinkedPick p in picks)
            {
                ListViewItem existing = _picks.Items.Cast<ListViewItem>()
                    .FirstOrDefault(i => ((LinkedPick)i.Tag).LinkInstanceId == p.LinkInstanceId && ((LinkedPick)i.Tag).Id == p.Id);
                if (existing != null) { existing.Selected = true; continue; }
                _items.Add(p);
                var it = new ListViewItem(p.Id.ToString()) { Tag = p, ToolTipText = "UniqueId: " + p.UniqueId };
                it.SubItems.Add(p.LinkFile);
                it.SubItems.Add(p.Category);
                it.SubItems.Add(p.FamilyType);
                it.SubItems.Add(p.Level);
                _picks.Items.Add(it);
                it.Selected = true;
            }
            _picks.EndUpdate();
            if (_picks.SelectedItems.Count > 0) _picks.SelectedItems[_picks.SelectedItems.Count - 1].EnsureVisible();
            if (!activate) { _status.Text = L.F("{0} elementi. Continua a cliccare in Revit, Esc per finire.", _items.Count); return; }
            UpdateStatus();
            if (!Visible) Show();
            Activate();
            _picks.Focus();
        }

        private void RemoveSelected()
        {
            foreach (ListViewItem it in _picks.SelectedItems.Cast<ListViewItem>().ToList())
            {
                _items.Remove((LinkedPick)it.Tag);
                _picks.Items.Remove(it);
            }
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            _status.Text = _items.Count == 0
                ? L.T("Premi «Scegli a clic…» o «Scegli con riquadro…» e scegli in Revit gli elementi dei modelli collegati.")
                : L.F("{0} elementi. Le righe selezionate si evidenziano nel modello. Doppio clic o Ctrl+C: copia gli ID.", _items.Count);
        }

        private List<LinkedPick> Selected()
        {
            var sel = _picks.SelectedItems.Cast<ListViewItem>().Select(i => (LinkedPick)i.Tag).ToList();
            return sel.Count > 0 ? sel : _items;
        }

        private void FillParameters()
        {
            _params.BeginUpdate();
            _params.Items.Clear();
            _params.Groups.Clear();
            List<LinkedPick> sel = _picks.SelectedItems.Cast<ListViewItem>().Select(i => (LinkedPick)i.Tag).ToList();
            if (sel.Count == 1)
            {
                var groups = new Dictionary<string, ListViewGroup>();
                foreach (string[] row in sel[0].Parameters)
                {
                    ListViewGroup g;
                    if (!groups.TryGetValue(row[0], out g))
                    {
                        g = new ListViewGroup(row[0], L.F("Parametri di {0}", row[0]));
                        groups[row[0]] = g;
                        _params.Groups.Add(g);
                    }
                    var it = new ListViewItem(row[1], g) { Tag = row };
                    it.SubItems.Add(row[2]);
                    _params.Items.Add(it);
                }
            }
            _params.EndUpdate();
        }

        /// <summary>ID delle righe selezionate (o di tutte), separati da virgola: si incollano in «Select by ID».</summary>
        private void CopyIds()
        {
            List<LinkedPick> sel = Selected();
            if (sel.Count == 0) return;
            SetClipboard(string.Join(", ", sel.Select(p => p.Id.ToString())));
            _status.Text = sel.Count == 1 ? L.F("Copiato l'ID {0}.", sel[0].Id) : L.F("Copiati {0} ID.", sel.Count);
        }

        private void CopyRows()
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join("\t", "ID", "UniqueId", L.T("File"), L.T("Categoria"), L.T("Famiglia / tipo"), L.T("Livello")));
            foreach (LinkedPick p in Selected())
                sb.AppendLine(string.Join("\t", p.Id.ToString(), p.UniqueId, p.LinkFile, p.Category, p.FamilyType, p.Level));
            SetClipboard(sb.ToString());
        }

        private void CopyParameters(bool valueOnly)
        {
            var rows = _params.SelectedItems.Cast<ListViewItem>().Select(i => (string[])i.Tag).ToList();
            if (rows.Count == 0) return;
            SetClipboard(string.Join(Environment.NewLine, rows.Select(r => valueOnly ? r[2] : r[1] + "\t" + r[2])));
        }

        private static void SetClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); }
            catch (Exception ex) { Log.Error("Copia", ex); }
        }
    }
}
