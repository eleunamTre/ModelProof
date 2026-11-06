using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ModelProof
{
    /// <summary>Livello del registro: si scrivono i messaggi fino a questo livello compreso.</summary>
    internal enum LogLevel { Spento = 0, Errori = 1, Avvisi = 2, Info = 3, Debug = 4 }

    /// <summary>
    /// Registro di diagnostica: %LOCALAPPDATA%\ModelProof\ModelProof.log
    /// Ogni messaggio uguale si scrive al massimo una volta al minuto; il file riparte da zero oltre 2 MB.
    /// </summary>
    internal static class Log
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, DateTime> _last = new Dictionary<string, DateTime>();

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModelProof"); }
        }

        public static string FilePath { get { return Path.Combine(Folder, "ModelProof.log"); } }

        public static LogLevel Level { get { return Settings.Current.LogLevel; } }

        /// <summary>Informazioni (livello Info).</summary>
        public static void Write(string where, string message) { Put(LogLevel.Info, where, message); }

        /// <summary>Situazioni anomale gestite (livello Avvisi).</summary>
        public static void Warn(string where, string message) { Put(LogLevel.Avvisi, where, message); }

        /// <summary>Dettagli per le indagini (livello Debug).</summary>
        public static void Debug(string where, string message) { Put(LogLevel.Debug, where, message); }

        public static void Error(string where, Exception ex)
        {
            if (ex == null) return;
            Put(LogLevel.Errori, where, ex.GetType().Name + ": " + ex.Message + "\r\n    " + (ex.StackTrace ?? "").Replace("\n", "\n    "));
        }

        private static void Put(LogLevel level, string where, string message)
        {
            try
            {
                if (level > Level) return;
                lock (_lock)
                {
                    string key = where + "|" + message;
                    DateTime now = DateTime.Now, t;
                    if (_last.TryGetValue(key, out t) && (now - t).TotalSeconds < 60) return;
                    _last[key] = now;
                    string path = FilePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete();
                    string tag = level == LogLevel.Errori ? "ERRORE " : level == LogLevel.Avvisi ? "AVVISO " : level == LogLevel.Debug ? "debug " : "";
                    File.AppendAllText(path, string.Format("{0:yyyy-MM-dd HH:mm:ss}  {1}[{2}]  {3}\r\n", now, tag, where, message), Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Impostazioni del plugin su questo computer (non del progetto): %LOCALAPPDATA%\ModelProof\settings.json
    /// </summary>
    [DataContract]
    internal sealed class Settings
    {
        [DataMember] public LogLevel LogLevel = LogLevel.Info;
        /// <summary>Ultimo file di configurazione usato.</summary>
        [DataMember] public string ConfigPath;

        private static Settings _current;

        private static string FilePath { get { return Path.Combine(Log.Folder, "settings.json"); } }

        public static Settings Current
        {
            get
            {
                if (_current != null) return _current;
                Settings s = null;
                try
                {
                    if (File.Exists(FilePath))
                        using (FileStream fs = File.OpenRead(FilePath))
                            s = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(fs);
                }
                catch { }
                _current = (s ?? new Settings()).Normalized();
                return _current;
            }
        }

        private Settings Normalized()
        {
            if (LogLevel < LogLevel.Spento || LogLevel > LogLevel.Debug) LogLevel = LogLevel.Info;
            return this;
        }

        public void Save()
        {
            Normalized();
            try
            {
                Directory.CreateDirectory(Log.Folder);
                using (FileStream fs = File.Create(FilePath))
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(fs, this);
            }
            catch (Exception ex) { Log.Error("Impostazioni", ex); }
        }
    }
}
