using System.Collections.Generic;

namespace ModelProof
{
    /// <summary>Traduzioni inglesi dei testi dell'interfaccia (chiave = testo italiano come scritto nel codice).</summary>
    internal static partial class L
    {
        static partial void FillEnglish(Dictionary<string, string> d)
        {
            d["   esempi: {0} giusti, {1} sbagliati"] = "   examples: {0} valid, {1} invalid";
            d["(nessun formato)"] = "(no format)";
            d["AVVISI"] = "WARNINGS";
            d["Apre la cartella del registro di diagnostica (ModelProof.log), da allegare quando si segnala un problema."] = "Opens the folder of the diagnostic log (ModelProof.log), to attach when reporting a problem.";
            d["Apri nel Blocco note"] = "Open in Notepad";
            d["CHECKLIST"] = "CHECKLIST";
            d["Chiudi"] = "Close";
            d["Configurazione"] = "Configuration";
            d["Configurazione valida: {0} regole, {1} elenchi, {2} righe della checklist. Avvisi: {3}."] = "Valid configuration: {0} rules, {1} lists, {2} checklist rows. Warnings: {3}.";
            d["Da correggere: {0} errori, {1} avvisi. I controlli non partono finché ci sono errori."] = "To be fixed: {0} errors, {1} warnings. Checks will not run while there are errors.";
            d["ELENCHI"] = "LISTS";
            d["ERRORI"] = "ERRORS";
            d["File di configurazione del progetto"] = "Project configuration file";
            d["File di testo"] = "Text files";
            d["File:"] = "File:";
            d["Formato: {0}"] = "Format: {0}";
            d["Il file non si può leggere."] = "The file cannot be read.";
            d["Impostazioni"] = "Settings";
            d["La regola non ha un formato da provare."] = "The rule has no format to test.";
            d["ModelProof – Configurazione"] = "ModelProof – Configuration";
            d["NON rispetta il formato {0}"] = "Does NOT match the format {0}";
            d["Nessun file di configurazione scelto."] = "No configuration file selected.";
            d["Prova interrotta: il formato è troppo complesso per questo valore."] = "Test stopped: the format is too complex for this value.";
            d["Prova una regola:"] = "Test a rule:";
            d["REGOLE"] = "RULES";
            d["Registro"] = "Log";
            d["Regole del\nprogetto"] = "Project\nrules";
            d["Rileggi"] = "Reload";
            d["Rispetta il formato."] = "Matches the format.";
            d["Scegli il file di testo con le regole del progetto (resta sul tuo computer)."] = "Choose the text file with the project rules (it stays on your computer).";
            d["Scegli..."] = "Choose...";
            d["Sceglie il file di configurazione del progetto (regole, codifiche, elenchi, collegamento alla checklist), lo controlla e permette di provare un valore con una regola."] = "Chooses the project configuration file (rules, codes, lists, link to the checklist), checks it and lets you test a value against a rule.";
            d["Segnaposto: {0}. Categorie escluse: {1}."] = "Placeholders: {0}. Excluded categories: {1}.";
            d["Tutti i file"] = "All files";
            d["Vedasi punto {0}"] = "See item {0}";
            d["carattere ammesso non riconosciuto: «{0}»"] = "allowed character not recognized: «{0}»";
            d["checklist {0}"] = "checklist {0}";
            d["ci sono esempi di prova ma la regola non ha un formato"] = "there are test examples but the rule has no format";
            d["codici ripetuti: {0}"] = "repeated codes: {0}";
            d["descrizione non riconosciuta: «{0}»"] = "description not recognized: «{0}»";
            d["descrizione vuota"] = "empty description";
            d["dopo «senza» vanno i caratteri tra virgolette, es. senza \".\""] = "after «senza» put the characters in quotes, e.g. senza \".\"";
            d["elenco [{0}] non trovato"] = "list [{0}] not found";
            d["elenco ripetuto: vale solo il primo"] = "repeated list: only the first one is used";
            d["formato non valido: {0}"] = "invalid format: {0}";
            d["formato troppo complesso: la prova degli esempi non finisce"] = "format too complex: testing the examples does not finish";
            d["il numero di caratteri deve essere almeno 1"] = "the number of characters must be at least 1";
            d["il segnaposto {{{0}}} rimanda a se stesso"] = "placeholder {{{0}}} refers to itself";
            d["l'elenco [{0}] non ha codici nella colonna {1}"] = "list [{0}] has no codes in column {1}";
            d["l'esempio «{0}» dovrebbe essere giusto ma non rispetta il formato"] = "example «{0}» should be valid but does not match the format";
            d["l'esempio «{0}» dovrebbe essere sbagliato ma rispetta il formato"] = "example «{0}» should be invalid but matches the format";
            d["la colonna parte da 1"] = "columns start at 1";
            d["la colonna «parte» deve essere -, mancanti o formato"] = "the «parte» column must be -, mancanti or formato";
            d["manca «modelli»"] = "«modelli» is missing";
            d["manca «oggetto»"] = "«oggetto» is missing";
            d["manca «{0}.codice»"] = "«{0}.codice» is missing";
            d["manca «{0}»"] = "«{0}» is missing";
            d["modello non valido: {0}"] = "invalid pattern: {0}";
            d["modello «{0}» sconosciuto (ammessi: URS, ARC, IMP)"] = "unknown model «{0}» (allowed: URS, ARC, IMP)";
            d["nessuna regola [REGOLA …] nel file"] = "no [REGOLA …] rules in the file";
            d["né regole né rimandi: la riga resterà vuota"] = "neither rules nor references: the row will stay empty";
            d["parentesi [ senza ]"] = "[ without ]";
            d["parentesi ] senza ["] = "] without [";
            d["parentesi { senza }"] = "{ without }";
            d["parentesi } senza {"] = "} without {";
            d["regola con lo stesso nome di un'altra"] = "rule with the same name as another one";
            d["regola «{0}» non trovata"] = "rule «{0}» not found";
            d["riga della checklist ripetuta"] = "repeated checklist row";
            d["riga {0}"] = "line {0}";
            d["rimando a «{0}», che non è nella sezione"] = "reference to «{0}», which is not in the section";
            d["segnaposto {0} definito due volte"] = "placeholder {0} defined twice";
            d["segnaposto {{{0}}} non definito in [SEGNAPOSTO]"] = "placeholder {{{0}}} not defined in [SEGNAPOSTO]";
            d["segnaposto {{{0}}} non valido"] = "invalid placeholder {{{0}}}";
            d["sezione mancante"] = "missing section";
            d["sezione mancante: la checklist non verrà compilata"] = "missing section: the checklist will not be filled in";
            d["sezione sconosciuta o elenco vuoto"] = "unknown section or empty list";
            d["testo fisso vuoto"] = "empty fixed text";
            d["testo fuori da una sezione: ignorato"] = "text outside a section: ignored";
            d["valore:"] = "value:";
            d["{0} righe"] = "{0} rows";
            d["«gravita» deve essere «errore» o «avviso»"] = "«gravita» must be «errore» or «avviso»";
            d["«soglia_parziale» deve essere un numero tra 0 e 100"] = "«soglia_parziale» must be a number between 0 and 100";
            d["«{0}» deve essere un numero di campo (1 = primo)"] = "«{0}» must be a field number (1 = first)";
        }
    }
}
