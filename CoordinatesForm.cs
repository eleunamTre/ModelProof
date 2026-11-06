using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ModelProof
{
    /// <summary>
    /// Dettaglio delle coordinate dell'ultima verifica: per il file verificato e per ogni collegamento caricato
    /// Survey Point, Project Base Point, nord reale, presenza delle coordinate condivise e posizionamento del
    /// collegamento. Le righe con differenze sono in rosso.
    /// </summary>
    internal sealed class CoordinatesForm : MpForm
    {
        public CoordinatesForm(CheckRun run)
        {
            Text = L.F("Coordinate – {0}", run.Model.FileName);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1250, 360);
            MinimumSize = new Size(700, 220);
            ShowInTaskbar = false;

            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, ShowItemToolTips = true };
            list.Columns.Add(L.T("Modello"), 60);
            list.Columns.Add(L.T("File"), 230);
            list.Columns.Add("Survey Point", 250);
            list.Columns.Add("Project Base Point", 250);
            list.Columns.Add(L.T("Nord reale (°)"), 95, HorizontalAlignment.Right);
            list.Columns.Add(L.T("Coordinate condivise nel file"), 95);
            list.Columns.Add(L.T("Collegato in coordinate condivise"), 95);
            list.Columns.Add(L.T("Problemi"), 420);
            foreach (CoordinateRow r in run.Coordinates)
            {
                var it = new ListViewItem(r.Role) { ToolTipText = r.File };
                it.SubItems.Add(r.Host ? L.F("{0} (file verificato)", r.File) : r.File);
                it.SubItems.Add(Checker.Coord(r.Survey));
                it.SubItems.Add(Checker.Coord(r.BasePoint));
                it.SubItems.Add(double.IsNaN(r.North) ? "" : r.North.ToString("F4", CultureInfo.InvariantCulture));
                it.SubItems.Add(r.SharedDefined ? L.T("sì") : L.T("no"));
                it.SubItems.Add(r.LinkShared == null ? "" : r.LinkShared.Value ? L.T("sì") : L.T("no"));
                it.SubItems.Add(r.Problems.Count > 0 ? string.Join("; ", r.Problems) : L.T("Conforme"));
                if (r.Problems.Count > 0) it.ForeColor = Color.FromArgb(190, 40, 40);
                list.Items.Add(it);
            }
            if (run.Coordinates.Count == 0) list.Items.Add(new ListViewItem(L.T("Coordinate non lette: vedere il registro.")));

            var close = new Button { Text = L.T("Chiudi"), AutoSize = true, Dock = DockStyle.Right };
            close.Click += (s, e) => Close();
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(0, 6, 0, 0) };
            bar.Controls.Add(close);
            Padding = new Padding(10);
            Controls.Add(list);
            Controls.Add(bar);
            CancelButton = close;
            UiTheme.Apply(this);
        }
    }
}
