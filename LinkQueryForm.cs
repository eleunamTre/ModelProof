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
    /// Finestra «Interroga collegamento», aperta accanto a Revit: «Scegli elementi…» passa a Revit per scegliere uno o
    /// più elementi dei collegamenti (clic o riquadro, poi «Fine»); gli elementi si aggiungono all'elenco. Selezionando
    /// una riga si vedono i suoi parametri. ID e valori si copiano con Ctrl+C, clic destro o i pulsanti; doppio clic su
    /// una riga copia il suo ID. Chiudendo la finestra si nasconde soltanto e l'elenco resta.
    /// </summary>
    internal sealed class LinkQueryForm : MpForm
    {
        /// <summary>Impostato dal lato Revit: chiede la scelta degli elementi (evento esterno).</summary>
        public static Action PickRequested;

        private static LinkQueryForm _instance;
        private bool _shown;
        private readonly ListView _picks;
        private readonly ListView _params;
        private readonly Label _status;
        private readonly List<LinkedPick> _items = new List<LinkedPick>();

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

        /// <summary>Elementi scelti in Revit: si aggiungono all'elenco (senza doppioni) e si selezionano.</summary>
        public static void AddPicks(List<LinkedPick> picks)
        {
            if (_instance == null || _instance.IsDisposed) return;
            _instance.Add(picks);
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
            var pick = new Button { Text = L.T("Scegli elementi…"), AutoSize = true };
            pick.Click += (s, e) =>
            {
                if (PickRequested == null) return;
                _status.Text = L.T("In Revit: clicca gli elementi dei collegamenti (anche con un riquadro), poi «Fine» nella barra delle opzioni. Esc annulla.");
                PickRequested();
            };
            var clear = new Button { Text = L.T("Svuota"), AutoSize = true };
            clear.Click += (s, e) => { _items.Clear(); _picks.Items.Clear(); _params.Items.Clear(); UpdateStatus(); };
            _status = new Label { AutoSize = true, Margin = new Padding(12, 8, 0, 0), ForeColor = UiTheme.Muted };
            top.Controls.Add(pick);
            top.Controls.Add(clear);
            top.Controls.Add(_status);

            _picks = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, ShowItemToolTips = true };
            _picks.Columns.Add("ID", 90, HorizontalAlignment.Right);
            _picks.Columns.Add(L.T("File"), 250);
            _picks.Columns.Add(L.T("Categoria"), 140);
            _picks.Columns.Add(L.T("Famiglia / tipo"), 280);
            _picks.Columns.Add(L.T("Livello"), 150);
            _picks.SelectedIndexChanged += (s, e) => FillParameters();
            _picks.DoubleClick += (s, e) => CopyIds();
            _picks.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C) { CopyIds(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem it in _picks.Items) it.Selected = true; e.Handled = true; }
            };
            var pickMenu = new ContextMenuStrip();
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

        private void Add(List<LinkedPick> picks)
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
                ? L.T("Premi «Scegli elementi…» e clicca in Revit gli elementi dei modelli collegati.")
                : L.F("{0} elementi. Doppio clic o Ctrl+C: copia gli ID delle righe selezionate.", _items.Count);
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
