using System;
using System.Collections.Generic;
using System.Linq;

namespace ModelProof
{
    /// <summary>Tipo di anomalia: decide in quale riga della checklist finisce (colonna «parte»).</summary>
    internal enum IssueKind
    {
        Mancante,     // parametro assente o non compilato
        Formato,      // compilato ma non rispetta il formato (struttura sbagliata)
        Elenco,       // struttura giusta, ma un codice non è negli elenchi del file (codice sbagliato o elenco da completare)
        Coerenza,     // parti che non coincidono con la fonte
        Numerazione,  // doppioni o salti
        Controllo     // controlli già pronti (coordinate, livelli, …)
    }

    /// <summary>Una anomalia trovata da una regola.</summary>
    internal sealed class Issue
    {
        public string Rule;
        public string File = "";       // file in cui sta il problema (il modello verificato o un collegamento)
        public string Role = "";       // ruolo di quel file (URS, ARC, IMP o «?»)
        public bool Error;             // false = avviso
        public bool Ok;                // riga conforme (verde): cosa è stato controllato e il valore letto; non si conta
        public IssueKind Kind;
        public long ElementId = -1;    // oggetto controllato (-1 = nessuno)
        public long ShowId = -1;       // elemento da inquadrare (per tipi e famiglie: una loro istanza)
        public string Category = "";
        public string FamilyType = "";
        public string Field = "";
        public string Value = "";
        public string Message = "";

        /// <summary>Priorità: 0 errore di struttura, 1 codice non in elenco, 2 avviso.</summary>
        public int Priority { get { return Ok ? 3 : !Error ? 2 : Kind == IssueKind.Elenco ? 1 : 0; } }
        public string LevelText { get { return Ok ? "OK" : !Error ? L.T("avviso") : Kind == IssueKind.Elenco ? L.T("non in elenco") : L.T("errore"); } }
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
        public int Warnings { get { return Issues.Count(i => !i.Error && !i.Ok); } }
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
        public string DescriptionRole; // ruolo indicato dalla descrizione in fondo al nome (con «_» o spazi), null se nessuno
        public string RoleCode = "";   // campo del ruolo così com'è nel nome

        public string RoleText { get { return Role ?? "?"; } }
    }

    /// <summary>Coordinate di un file (quello verificato o un collegamento) per il dettaglio delle coordinate.</summary>
    internal sealed class CoordinateRow
    {
        public string File = "";
        public string Role = "?";
        public bool Host;              // il file verificato
        public double[] Survey;        // Survey Point E/W, N/S, quota in coordinate condivise (m); null = non trovato
        public double[] BasePoint;     // Project Base Point, idem
        public double North = double.NaN;   // angolo tra nord di progetto e nord reale dalla posizione condivisa (gradi)
        public bool SharedDefined;     // il file ha un sistema di coordinate condivise diverso da quello interno
        public bool? LinkShared;       // solo collegamenti: posizionato in coordinate condivise
        public double LinkOffsetMm;    // scarto della posizione del collegamento rispetto a quella condivisa
        public double LinkRotDeg;
        public readonly List<CoordinateAspect> Aspects = new List<CoordinateAspect>();   // cosa si è controllato
        public List<string> Problems { get { return Aspects.Where(a => !a.Ok).Select(a => a.Field + ": " + a.Message).ToList(); } }
    }

    /// <summary>Un aspetto controllato delle coordinate di un file (es. Survey Point uguale al file verificato).</summary>
    internal sealed class CoordinateAspect
    {
        public string Field;
        public string Value;
        public bool Ok;
        public string Message;         // spiegazione del problema (vuota se Ok)
        public bool LinkPosition;      // riguarda la posizione dell'istanza del collegamento (si può inquadrare)
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
        public readonly List<CoordinateRow> Coordinates = new List<CoordinateRow>();

        public int Errors { get { return Results.Sum(r => r.Errors); } }
        public int Warnings { get { return Results.Sum(r => r.Warnings); } }
    }
}
