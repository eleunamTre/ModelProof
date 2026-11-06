using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;

namespace ModelProof
{
    /// <summary>Lettura di nomi, parametri e livelli dagli elementi di Revit, uguale per tutte le versioni.</summary>
    internal static class RevitUtil
    {
        // ------------------------------------------------------------------ nome del file

        /// <summary>
        /// Nome del file senza estensione. Per i modelli condivisi si usa il nome del file centrale: la copia locale
        /// ha in più il nome dell'utente, che falserebbe la lettura dei campi.
        /// </summary>
        public static string FileName(Document doc)
        {
            try
            {
                if (doc.IsWorkshared)
                {
                    ModelPath central = doc.GetWorksharingCentralModelPath();
                    if (central != null)
                    {
                        string p = ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                        string n = LastSegment(p);
                        if (n.Length > 0) return n;
                    }
                }
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(doc.PathName)) return LastSegment(doc.PathName);
            }
            catch { }
            return LastSegment(doc.Title ?? "");
        }

        private static string LastSegment(string path)
        {
            string s = path.Replace('\\', '/');
            int i = s.LastIndexOf('/');
            if (i >= 0) s = s.Substring(i + 1);
            if (s.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return s.Trim();
        }

        // ------------------------------------------------------------------ parametri

        /// <summary>Nomi di campo che corrispondono a parametri di Revit (in inglese e in italiano).</summary>
        private static readonly Dictionary<string, string[]> KnownFields = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Type Mark", new[] { "ALL_MODEL_TYPE_MARK" } },
            { "Contrassegno tipo", new[] { "ALL_MODEL_TYPE_MARK" } },
            { "Mark", new[] { "ALL_MODEL_MARK" } },
            { "Contrassegno", new[] { "ALL_MODEL_MARK" } },
            { "Export Type to IFC As", new[] { "IFC_EXPORT_ELEMENT_TYPE_AS" } },
            { "Type IFC Predefined Type", new[] { "IFC_EXPORT_PREDEFINEDTYPE_TYPE", "IFC_TYPE_EXPORT_PREDEFINEDTYPE" } },
            { "Export to IFC As", new[] { "IFC_EXPORT_ELEMENT_AS" } },
            { "IFC Predefined Type", new[] { "IFC_EXPORT_PREDEFINEDTYPE" } },
            { "Numero", new[] { "ROOM_NUMBER" } },
            { "Number", new[] { "ROOM_NUMBER" } },
        };

        private static readonly Dictionary<string, List<BuiltInParameter>> _bipCache = new Dictionary<string, List<BuiltInParameter>>(StringComparer.OrdinalIgnoreCase);

        private static List<BuiltInParameter> KnownParameters(string field)
        {
            List<BuiltInParameter> list;
            if (_bipCache.TryGetValue(field, out list)) return list;
            list = new List<BuiltInParameter>();
            string[] names;
            if (KnownFields.TryGetValue(field, out names))
                foreach (string n in names)
                {
                    BuiltInParameter bip;
                    if (Enum.TryParse(n, out bip)) list.Add(bip);
                }
            _bipCache[field] = list;
            return list;
        }

        /// <summary>Parametro con quel nome (prima i parametri di Revit noti, poi per nome).</summary>
        public static Parameter FindParameter(Element e, string field)
        {
            if (e == null) return null;
            foreach (BuiltInParameter bip in KnownParameters(field))
            {
                Parameter p = e.get_Parameter(bip);
                if (p != null) return p;
            }
            return e.LookupParameter(field);
        }

        /// <summary>Valore del parametro come testo ("" se vuoto, null se il parametro non c'è).</summary>
        public static string ValueOf(Parameter p, Document doc)
        {
            if (p == null) return null;
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        return p.AsString() ?? "";
                    case StorageType.ElementId:
                        {
                            ElementId id = p.AsElementId();
                            if (id == null || id == ElementId.InvalidElementId) return "";
                            Element el = doc.GetElement(id);
                            return el != null ? el.Name : "";
                        }
                    default:
                        if (!p.HasValue) return "";
                        return p.AsValueString() ?? "";
                }
            }
            catch { return ""; }
        }

        // ------------------------------------------------------------------ livello di un elemento

        private static readonly string[] LevelParameterNames =
        {
            "FAMILY_LEVEL_PARAM", "INSTANCE_REFERENCE_LEVEL_PARAM", "SCHEDULE_LEVEL_PARAM", "INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM",
            "WALL_BASE_CONSTRAINT", "STAIRS_BASE_LEVEL_PARAM", "RBS_START_LEVEL_PARAM"
        };

        private static List<BuiltInParameter> _levelParameters;

        private static List<BuiltInParameter> LevelParameters
        {
            get
            {
                if (_levelParameters != null) return _levelParameters;
                var list = new List<BuiltInParameter>();
                foreach (string n in LevelParameterNames)
                {
                    BuiltInParameter bip;
                    if (Enum.TryParse(n, out bip)) list.Add(bip);
                }
                _levelParameters = list;
                return list;
            }
        }

        /// <summary>Livello di appartenenza (null se l'elemento non ne ha uno).</summary>
        public static Level LevelOf(Element e)
        {
            Document doc = e.Document;
            try
            {
                if (e.LevelId != null && e.LevelId != ElementId.InvalidElementId)
                {
                    Level l = doc.GetElement(e.LevelId) as Level;
                    if (l != null) return l;
                }
            }
            catch { }
            var mep = e as MEPCurve;
            if (mep != null)
            {
                try { if (mep.ReferenceLevel != null) return mep.ReferenceLevel; } catch { }
            }
            foreach (BuiltInParameter bip in LevelParameters)
            {
                try
                {
                    Parameter p = e.get_Parameter(bip);
                    if (p == null || p.StorageType != StorageType.ElementId) continue;
                    Level l = doc.GetElement(p.AsElementId()) as Level;
                    if (l != null) return l;
                }
                catch { }
            }
            return null;
        }

        // ------------------------------------------------------------------ categorie

        /// <summary>Categorie mai controllate dalle regole su istanze e tipi (non sono oggetti costruiti).</summary>
        private static readonly string[] AlwaysExcluded =
        {
            "OST_RvtLinks", "OST_Cameras", "OST_Lines", "OST_SketchLines", "OST_ProjectBasePoint", "OST_SharedBasePoint",
            "OST_IOSModelGroups", "OST_Rooms", "OST_Areas", "OST_MEPSpaces", "OST_Levels", "OST_Grids", "OST_HVAC_Zones",
            "OST_Viewers", "OST_Materials", "OST_ProjectInformation", "OST_PipeSegments", "OST_DetailComponents",
            "OST_RasterImages", "OST_Mass", "OST_MassForm", "OST_CLines", "OST_ReferencePoints", "OST_PointClouds",
            "OST_InternalOriginPoint"
        };

        /// <summary>
        /// Nomi inglesi delle categorie (come nell'elenco CATEGORIE del file di configurazione), per riconoscerle anche
        /// con Revit in un'altra lingua. Le categorie che non esistono in una versione vengono ignorate.
        /// </summary>
        private static readonly Dictionary<string, string> EnglishCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Air Terminals", "OST_DuctTerminal" }, { "Areas", "OST_Areas" }, { "Audio Visual Devices", "OST_AudioVisualDevices" },
            { "Cable Tray Fittings", "OST_CableTrayFitting" }, { "Cable Trays", "OST_CableTray" }, { "Casework", "OST_Casework" },
            { "Ceilings", "OST_Ceilings" }, { "Columns", "OST_Columns" }, { "Communication Devices", "OST_CommunicationDevices" },
            { "Conduit Fittings", "OST_ConduitFitting" }, { "Conduits", "OST_Conduit" }, { "Curtain Panels", "OST_CurtainWallPanels" },
            { "Curtain Systems", "OST_CurtaSystem" }, { "Curtain Wall Grids", "OST_CurtainGrids" }, { "Curtain Wall Mullions", "OST_CurtainWallMullions" },
            { "Data Devices", "OST_DataDevices" }, { "Detail Items", "OST_DetailComponents" }, { "Doors", "OST_Doors" },
            { "Duct Accessories", "OST_DuctAccessory" }, { "Duct Fittings", "OST_DuctFitting" }, { "Duct Insulations", "OST_DuctInsulations" },
            { "Duct Linings", "OST_DuctLinings" }, { "Duct Placeholders", "OST_PlaceHolderDucts" }, { "Ducts", "OST_DuctCurves" },
            { "Electrical Equipment", "OST_ElectricalEquipment" }, { "Electrical Fixtures", "OST_ElectricalFixtures" }, { "Entourage", "OST_Entourage" },
            { "Fire Alarm Devices", "OST_FireAlarmDevices" }, { "Fire Protection", "OST_FireProtection" }, { "Flex Ducts", "OST_FlexDuctCurves" },
            { "Flex Pipes", "OST_FlexPipeCurves" }, { "Floor Edge", "OST_EdgeSlab" }, { "Slab Edges", "OST_EdgeSlab" }, { "Floors", "OST_Floors" },
            { "Food Service Equipment", "OST_FoodServiceEquipment" }, { "Furniture", "OST_Furniture" }, { "Furniture Systems", "OST_FurnitureSystems" },
            { "Generic Models", "OST_GenericModel" }, { "Grids", "OST_Grids" }, { "Hardscape", "OST_Hardscape" }, { "HVAC Zones", "OST_HVAC_Zones" },
            { "Levels", "OST_Levels" }, { "Lighting Devices", "OST_LightingDevices" }, { "Lighting Fixtures", "OST_LightingFixtures" },
            { "Lines", "OST_Lines" }, { "Mass", "OST_Mass" }, { "Mechanical Control Devices", "OST_MechanicalControlDevices" },
            { "Mechanical Equipment", "OST_MechanicalEquipment" }, { "Medical Equipment", "OST_MedicalEquipment" },
            { "MEP Fabrication Containment", "OST_FabricationContainment" }, { "MEP Fabrication Ductwork", "OST_FabricationDuctwork" },
            { "MEP Fabrication Hangers", "OST_FabricationHangers" }, { "MEP Fabrication Pipework", "OST_FabricationPipework" },
            { "Nurse Call Devices", "OST_NurseCallDevices" }, { "Parking", "OST_Parking" }, { "Parts", "OST_Parts" },
            { "Pipe Accessories", "OST_PipeAccessory" }, { "Pipe Fittings", "OST_PipeFitting" }, { "Pipe Insulations", "OST_PipeInsulations" },
            { "Pipe Placeholders", "OST_PlaceHolderPipes" }, { "Pipes", "OST_PipeCurves" }, { "Planting", "OST_Planting" },
            { "Plumbing Equipment", "OST_PlumbingEquipment" }, { "Plumbing Fixtures", "OST_PlumbingFixtures" }, { "Railings", "OST_StairsRailing" },
            { "Railings - Handrails", "OST_RailingHandRail" }, { "Railings - Toprail", "OST_RailingTopRail" }, { "Ramps", "OST_Ramps" },
            { "Raster Images", "OST_RasterImages" }, { "Roads", "OST_Roads" }, { "Roofs", "OST_Roofs" }, { "Roofs - Fascia", "OST_Fascia" },
            { "Roofs - Gutter", "OST_Gutter" }, { "Rooms", "OST_Rooms" }, { "Security Devices", "OST_SecurityDevices" },
            { "Shaft Openings", "OST_ShaftOpening" }, { "Signage", "OST_Signage" }, { "Site", "OST_Site" }, { "Site-Pad", "OST_BuildingPad" },
            { "Spaces", "OST_MEPSpaces" }, { "Specialty Equipment", "OST_SpecialityEquipment" }, { "Sprinklers", "OST_Sprinklers" },
            { "Stairs", "OST_Stairs" }, { "Structural Area Reinforcement", "OST_AreaRein" }, { "Structural Beam Systems", "OST_StructuralFramingSystem" },
            { "Structural Columns", "OST_StructuralColumns" }, { "Structural Connections", "OST_StructConnections" },
            { "Structural Fabric Areas", "OST_FabricAreas" }, { "Structural Fabric Reinforcement", "OST_FabricReinforcement" },
            { "Structural Foundations", "OST_StructuralFoundation" }, { "Structural Framing", "OST_StructuralFraming" },
            { "Structural Path Reinforcement", "OST_PathRein" }, { "Structural Rebar", "OST_Rebar" }, { "Structural Rebar Couplers", "OST_Coupler" },
            { "Structural Stiffeners", "OST_StructuralStiffener" }, { "Structural Tendons", "OST_StructuralTendons" },
            { "Structural Trusses", "OST_StructuralTruss" }, { "Telephone Devices", "OST_TelephoneDevices" },
            { "Temporary Structures", "OST_TemporaryStructure" }, { "Topography", "OST_Topography" }, { "Toposolid", "OST_Toposolid" },
            { "Vertical Circulation", "OST_VerticalCirculation" }, { "Walls", "OST_Walls" }, { "Walls - Reveals", "OST_Reveals" },
            { "Walls - Sweep", "OST_Cornices" }, { "Windows", "OST_Windows" }, { "Wires", "OST_Wire" },
            // parti di ringhiere e scale (come le mostra Revit in inglese)
            { "Top Rails", "OST_RailingTopRail" }, { "Handrails", "OST_RailingHandRail" }, { "Balusters", "OST_StairsRailingBaluster" },
            { "Supports", "OST_RailingSupport|OST_StairsStringerCarriage" }, { "Runs", "OST_StairsRuns" }, { "Landings", "OST_StairsLandings" },
            { "Railing Terminations", "OST_RailingTermination" }, { "Stringers", "OST_StairsStringerCarriage" },
        };

        /// <summary>Id numerici delle categorie Revit con quel nome inglese (vuoto se sconosciuta in questa versione).</summary>
        public static List<long> CategoryIds(string englishName)
        {
            var ids = new List<long>();
            string osts;
            if (!EnglishCategories.TryGetValue(englishName.Trim(), out osts)) return ids;
            foreach (string ost in osts.Split('|'))
            {
                BuiltInCategory bic;
                if (Enum.TryParse(ost, out bic)) ids.Add((long)bic);
            }
            return ids;
        }

        /// <summary>Insieme delle categorie escluse: quelle fisse più quelle dell'elenco [ESCLUSIONI] (nome inglese o di Revit).</summary>
        public static Exclusions BuildExclusions(IEnumerable<string> configured)
        {
            var ex = new Exclusions();
            foreach (string ost in AlwaysExcluded)
            {
                BuiltInCategory bic;
                if (Enum.TryParse(ost, out bic)) ex.Ids.Add((long)bic);
            }
            foreach (string name in configured)
            {
                List<long> ids = CategoryIds(name);
                foreach (long id in ids) ex.Ids.Add(id);
                ex.Names.Add(name.Trim());   // anche per nome: vale con Revit in un'altra lingua
            }
            return ex;
        }

        /// <summary>
        /// Tipo di una famiglia caricabile. Pannelli di sistema delle facciate continue e montanti sono FamilySymbol
        /// nell'API ma in Revit sono famiglie di sistema: si nominano come i tipi di sistema.
        /// </summary>
        public static bool IsLoadable(ElementType t)
        {
            if (!(t is FamilySymbol)) return false;
            if (t is MullionType || t is PanelType) return false;
            return true;
        }

        public static long IdOf(ElementId id)
        {
            return id == null ? -1 : id.Value;
        }
    }

    /// <summary>Categorie escluse: per id (BuiltInCategory) o per nome mostrato da Revit.</summary>
    internal sealed class Exclusions
    {
        public readonly HashSet<long> Ids = new HashSet<long>();
        public readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool Contains(Category c)
        {
            if (c == null) return true;
            return Ids.Contains(c.Id.Value) || Names.Contains(c.Name);
        }
    }
}
