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

    /// <summary>Elenco degli elementi collegati interrogati e parametri di quello selezionato.</summary>
    internal sealed class LinkQueryForm : MpForm
    {
        private readonly ListView _picks;
        private readonly ListView _params;
        private readonly List<LinkedPick> _items;

        /// <summary>Mostra la finestra (modale) davanti a Revit.</summary>
        public static void ShowPicks(List<LinkedPick> items, IntPtr owner)
        {
            using (var f = new LinkQueryForm(items))
            {
                if (owner != IntPtr.Zero) f.ShowDialog(new RevitWindow(owner));
                else f.ShowDialog();
            }
        }

        private sealed class RevitWindow : IWin32Window
        {
            public RevitWindow(IntPtr h) { Handle = h; }
            public IntPtr Handle { get; private set; }
        }

        public LinkQueryForm(List<LinkedPick> items)
        {
            _items = items;
            Text = L.F("Elementi dei collegamenti ({0})", items.Count);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1000, 600);
            MinimumSize = new Size(600, 360);
            ShowInTaskbar = false;
            Padding = new Padding(10);

            _picks = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true };
            _picks.Columns.Add("ID", 90, HorizontalAlignment.Right);
            _picks.Columns.Add(L.T("File"), 250);
            _picks.Columns.Add(L.T("Categoria"), 140);
            _picks.Columns.Add(L.T("Famiglia / tipo"), 280);
            _picks.Columns.Add(L.T("Livello"), 150);
            foreach (LinkedPick p in items)
            {
                var it = new ListViewItem(p.Id.ToString()) { Tag = p, ToolTipText = "UniqueId: " + p.UniqueId };
                it.SubItems.Add(p.LinkFile);
                it.SubItems.Add(p.Category);
                it.SubItems.Add(p.FamilyType);
                it.SubItems.Add(p.Level);
                _picks.Items.Add(it);
            }
            _picks.ShowItemToolTips = true;
            _picks.SelectedIndexChanged += (s, e) => FillParameters();

            _params = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, ShowGroups = true };
            _params.Columns.Add(L.T("Parametro"), 260);
            _params.Columns.Add(L.T("Valore"), 420);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            split.Panel1.Controls.Add(_picks);
            split.Panel2.Controls.Add(_params);
            Shown += (s, e) =>
            {
                try { split.SplitterDistance = Math.Max(120, split.Height / 3); } catch { }
                if (_picks.Items.Count > 0) _picks.Items[0].Selected = true;
            };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            var close = new Button { Text = L.T("Chiudi"), AutoSize = true };
            close.Click += (s, e) => Close();
            var copyAll = new Button { Text = L.T("Copia tutto"), AutoSize = true };
            copyAll.Click += (s, e) => CopyAll();
            var copyIds = new Button { Text = L.T("Copia ID"), AutoSize = true };
            copyIds.Click += (s, e) => CopyIds();
            buttons.Controls.Add(close);
            buttons.Controls.Add(copyAll);
            buttons.Controls.Add(copyIds);

            Controls.Add(split);
            Controls.Add(buttons);
            CancelButton = close;
            UiTheme.Apply(this);
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
                    var it = new ListViewItem(row[1], g);
                    it.SubItems.Add(row[2]);
                    _params.Items.Add(it);
                }
            }
            _params.EndUpdate();
        }

        /// <summary>ID degli elementi selezionati (o di tutti), separati da virgola: si incollano in «Select by ID».</summary>
        private void CopyIds()
        {
            SetClipboard(string.Join(", ", Selected().Select(p => p.Id.ToString())));
        }

        private void CopyAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join("\t", "ID", "UniqueId", L.T("File"), L.T("Categoria"), L.T("Famiglia / tipo"), L.T("Livello")));
            foreach (LinkedPick p in Selected())
                sb.AppendLine(string.Join("\t", p.Id.ToString(), p.UniqueId, p.LinkFile, p.Category, p.FamilyType, p.Level));
            SetClipboard(sb.ToString());
        }

        private void SetClipboard(string text)
        {
            try { Clipboard.SetText(text); }
            catch (Exception ex) { Log.Error("Copia", ex); }
        }
    }
}
