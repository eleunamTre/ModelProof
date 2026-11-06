using System;
using System.Collections.Generic;

namespace ModelProof
{
    /// <summary>
    /// Lingua dell'interfaccia. I testi sono scritti in italiano nel codice e passano da L.T():
    /// se Revit non è in italiano vengono sostituiti con la traduzione inglese (Strings.en.cs).
    /// Un testo senza traduzione resta in italiano, così una dimenticanza non rompe nulla.
    /// </summary>
    internal static partial class L
    {
        /// <summary>true = inglese. Impostato all'avvio dalla lingua di Revit.</summary>
        public static bool English;

        public static void Init(Autodesk.Revit.ApplicationServices.LanguageType lang)
        {
            English = lang != Autodesk.Revit.ApplicationServices.LanguageType.Italian;
            var warm = En; // costruito subito: poi viene solo letto
        }

        public static string T(string it)
        {
            if (!English || it == null) return it;
            string en;
            return En.TryGetValue(it, out en) ? en : it;
        }

        /// <summary>Traduce il modello e poi applica string.Format.</summary>
        public static string F(string it, params object[] args)
        {
            return string.Format(T(it), args);
        }

        private static Dictionary<string, string> _en;

        private static Dictionary<string, string> En
        {
            get
            {
                if (_en == null)
                {
                    var d = new Dictionary<string, string>(StringComparer.Ordinal);
                    FillEnglish(d);
                    _en = d;
                }
                return _en;
            }
        }

        static partial void FillEnglish(Dictionary<string, string> d);
    }
}
