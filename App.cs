using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace ModelProof
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try { L.Init(application.ControlledApplication.Language); } catch { }
            try
            {
                BuildRibbon(application);
                Log.Write("Avvio", string.Format("ModelProof {0} caricato in Revit {1}",
                    Assembly.GetExecutingAssembly().GetName().Version, application.ControlledApplication.VersionNumber));
            }
            catch (Exception ex) { Log.Error("Avvio", ex); }
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        // ------------------------------------------------------------------ barra multifunzione

        private static void BuildRibbon(UIControlledApplication app)
        {
            const string tabName = "ModelProof";
            try { app.CreateRibbonTab(tabName); } catch { /* scheda già esistente */ }
            string asm = Assembly.GetExecutingAssembly().Location;

            RibbonPanel pCheck = app.CreateRibbonPanel(tabName, L.T("Verifica"));
            pCheck.AddItem(Push("Check", "Verifica\nmodello", asm, typeof(CheckModelCommand), "check",
                "Controlla il modello aperto con le regole del file di configurazione: riconosce il modello dal nome del file, confronta livelli e coordinate con l'URS collegato, controlla codifiche e parametri. Il modello non viene modificato.",
                typeof(ProjectAvailability)));
            pCheck.AddItem(Push("Results", "Ultimi\nrisultati", asm, typeof(LastResultsCommand), "results",
                "Riapre i risultati dell'ultima verifica del progetto attivo, senza rifarla.",
                typeof(ProjectAvailability)));

            RibbonPanel pTools = app.CreateRibbonPanel(tabName, L.T("Strumenti"));
            pTools.AddItem(Push("LinkQuery", "Interroga\ncollegamento", asm, typeof(LinkQueryCommand), "pick",
                "Apre una finestra da cui scegliere in Revit uno o più elementi dei modelli collegati: mostra ID, file, categoria, famiglia e tipo, livello e tutti i parametri, senza aprire il modello originale. Gli ID si copiano (doppio clic, Ctrl+C o clic destro) per «Select by ID» nel file originale.",
                typeof(ProjectAvailability)));
            pTools.AddItem(Push("Schedules", "Crea\nabachi", asm, typeof(CreateSchedulesCommand), "schedule",
                "Crea nel modello gli abachi di controllo descritti nel file delle regole (sezioni [ABACO nome]), per verificare a mano parametri, IFC, materiali e locali. Disponibile solo se il file delle regole contiene abachi. Si sceglie se aggiornare quelli esistenti o creare solo i mancanti; si annulla con Ctrl+Z.",
                typeof(ScheduleAvailability)));

            RibbonPanel pConfig = app.CreateRibbonPanel(tabName, L.T("Configurazione"));
            pConfig.AddItem(Push("Config", "Regole del\nprogetto", asm, typeof(ConfigCommand), "config",
                "Sceglie il file di configurazione del progetto (regole, codifiche, elenchi, collegamento alla checklist), lo controlla e permette di provare un valore con una regola.",
                typeof(AlwaysAvailable)));

            RibbonPanel pSettings = app.CreateRibbonPanel(tabName, L.T("Impostazioni"));
            pSettings.AddItem(Push("Log", "Registro", asm, typeof(OpenLogCommand), "log",
                "Apre la cartella del registro di diagnostica (ModelProof.log), da allegare quando si segnala un problema.",
                typeof(AlwaysAvailable)));
        }

        private static PushButtonData Push(string name, string text, string assembly, Type command, string icon, string tooltip, Type availability)
        {
            var d = new PushButtonData(name, L.T(text), assembly, command.FullName);
            d.AvailabilityClassName = availability.FullName;
            d.ToolTip = L.T(tooltip);
            d.LargeImage = Icon(icon, 32);
            d.Image = Icon(icon, 16);
            return d;
        }

        // ------------------------------------------------------------------ icone incorporate nella DLL

        private static BitmapSource Icon(string name, int size)
        {
            try
            {
                string resource = "ModelProof.Resources." + name + size + ".png";
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (s == null) return null;
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = s;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
