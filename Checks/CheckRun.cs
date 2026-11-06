using System;
using System.Collections.Generic;
using System.Linq;

namespace ModelProof
{
    /// <summary>Tipo di anomalia: decide in quale riga della checklist finisce (colonna «parte»).</summary>
    internal enum IssueKind
    {
        Mancante,     // parametro assente o non compilato
        Formato,      // compilato ma non rispetta il formato
        Coerenza,     // parti che non coincidono con la fonte
        Numerazione,  // doppioni o salti
        Controllo     // controlli già pronti (coordinate, livelli, …)
    }

    /// <summary>Una anomalia trovata da una regola.</summary>
    internal sealed class Issue
    {
        public string Rule;
        public bool Error;             // false = avviso
        public IssueKind Kind;
        public long ElementId = -1;    // oggetto controllato (-1 = nessuno)
        public long ShowId = -1;       // elemento da inquadrare (per tipi e famiglie: una loro istanza)
        public string Category = "";
        public string FamilyType = "";
        public string Field = "";
        public string Value = "";
        public string Message = "";
    }

    /// <summary>Esito di una regola su un modello.</summary>
    internal sealed class RuleResult
    {
        public Rule Rule;
        public int Checked;            // oggetti controllati
        public bool Skipped;           // la regola non si applica a questo modello
        public string Note;            // spiegazione (non applicabile, non ancora disponibile, riferimento mancante…)
        public readonly List<Issue> Issues = new List<Issue>();

        public int Errors { get { return Issues.Count(i => i.Error); } }
        public int Warnings { get { return Issues.Count(i => !i.Error); } }
        /// <summary>Oggetti diversi con almeno un errore.</summary>
        public int FailedObjects { get { return Issues.Where(i => i.Error).Select(i => i.ElementId).Distinct().Count(); } }
    }

    /// <summary>Riconoscimento di un modello dal nome del file.</summary>
    internal sealed class ModelInfo
    {
        public string FileName = "";
        public string Role;            // URS, ARC, IMP o null se non riconosciuto
        public string Building;        // codice edificio dal nome file (null se manca)
        public string Description = "";
        public bool DescriptionOk = true;

        public string RoleText { get { return Role ?? "?"; } }
    }

    /// <summary>Risultato di una verifica completa di un modello.</summary>
    internal sealed class CheckRun
    {
        public ModelInfo Model;
        public string DocumentKey;     // per riconoscere il progetto (percorso o titolo)
        public DateTime When = DateTime.Now;
        public double Seconds;
        public string ConfigPath;
        public double PartialThreshold = 20;   // % di oggetti con errori sotto cui l'esito è PARZIALE
        public readonly List<RuleResult> Results = new List<RuleResult>();
        public readonly List<string> Notes = new List<string>();

        public int Errors { get { return Results.Sum(r => r.Errors); } }
        public int Warnings { get { return Results.Sum(r => r.Warnings); } }
    }
}
