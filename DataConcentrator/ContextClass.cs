using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml;
using System.Threading;
using PLCSimulator;
using System.Xml.Linq;
using System.IO;
using System.Xml.XPath;
using System.Security.Cryptography;

namespace DataConcentrator
{
    public static class ContextClass
    {
        private static string defaultPath = "config.txt";

        // --- Simulator i sinhronizacija ---
        private static PLCSimulatorManager plcSim = null; // čuva instancu PLCSimulator-a
        private static readonly object plcSimLock = new object(); // služi za zaključavanje pristupa simulatoru iz više thread-ova

        // --- In-memory store (memorijske liste, bez baze) ---
        public static Dictionary<DateTime,Dictionary<string, double>> InputTagsValueHistory = new Dictionary<DateTime, Dictionary<string, double>>();
        public static readonly List<Tag> Tags = new List<Tag>();
        public static List<Tag> InputTags = new List<Tag>();
        public static List<Tag> OutputTags = new List<Tag>();
        public static readonly List<Alarm> Alarms = new List<Alarm>();            // svi alarmi
        public static readonly List<ActivatedAlarm> ActivatedAlarms = new List<ActivatedAlarm>();
        

        // --- Threading / skeneri ---
        private static readonly Dictionary<int, Thread> activeScannerThreads = new Dictionary<int, Thread>(); // za svaki tag id čuvamo thread koji ga skenira
        private static readonly Dictionary<int, bool> stopFlags = new Dictionary<int, bool>(); // zastavice za gašenje skenera
        //public static readonly Dictionary<int, double> lastValue = new Dictionary<int, double>(); // poslednja poznata vrednost za svaki tag
        private static readonly object stateLock = new object(); // lock da sinhronizujemo pristup gornjim strukturama

        // --- Eventi ---
        public static event EventHandler ValueChanged;   // GUI može da se "pretplati" na ovaj event – javi se kada se neka vrednost promeni
        public static event EventHandler AlarmRaised;    // GUI može da se "pretplati" na ovaj event – javi se kada se aktivira alarm

        // --- JAVNE METODE ---

        /// <summary>
        /// Povezuje i startuje PLCSimulator (samo jednom).
        /// Ako simulator već radi, neće ga ponovo startovati.
        /// </summary>
        public static void ConnectAndStartSimulator()
        {
            lock (plcSimLock) 
            {
                if (plcSim == null) // ako simulator nije pokrenut
                {
                    plcSim = new PLCSimulatorManager(); // kreiraj instancu simulatora
                    plcSim.StartPLCSimulator();        // pokreni simulaciju (počinje da generiše podatke)
                }
            }
        }

        /// <summary>
        /// Dodaje tag u listu (ali ne startuje njegovo skeniranje).
        /// </summary>
        public static Boolean AddTag(Tag tag)
        {
            Boolean isTagValid = tag.IsTagValid();
            if (!tag.IsTagValid()) return false;

            lock (stateLock)
            {
                if (!Tags.Any(t => t.name == tag.name)) // dodaj samo ako ne postoji već tag sa istim id
                {

                    Tags.Add(tag);
                    UpdateInputOutputTags();
                    if (tag.isInput) 
                        StartScanner(tag);


                }
            }
            return true;
        }

        /// <summary>
        /// Briše tag iz liste i gasi njegov skener ako je radio.
        /// </summary>
        public static void RemoveTag(int tagId)
        {
            lock (stateLock)
            {
                if (activeScannerThreads.ContainsKey(tagId))
                    TerminateScanner(tagId); // prvo ugasi skener za taj tag

                Tags.RemoveAll(t => t.id == tagId); // ukloni tag iz liste
                ShiftTagIds();
                UpdateInputOutputTags();
                Alarms.RemoveAll(a => a.TagId ==  tagId);
                stopFlags.Remove(tagId);
            }
            
        }

        /// <summary>
        /// Dodaje alarm u listu.
        /// </summary>
        public static void AddAlarm(Alarm alarm)
        {
            lock (stateLock)
            {
                if (!Alarms.Any(a => a.Id == alarm.Id))
                    Alarms.Add(alarm);
                var tag = Tags[alarm.TagId];
                if (!tag.TagSpecific.ContainsKey("Alarms"))
                    Console.WriteLine($"Tag doesn't contain any alarms yet!");
                    tag.TagSpecific["Alarms"] = new List<Alarm>();
                Console.WriteLine("Added alarm");
                ((List<Alarm>)tag.TagSpecific["Alarms"]).Add(alarm);
                UpdateInputOutputTags();
            }
        }

        /// <summary>
        /// Uklanja alarm iz liste po njegovom Id.
        /// </summary>
        public static void RemoveAlarm(int alarmId)
        {
            lock (stateLock)
            {
                //Treba dodati da se brise alarm i iz tag liste alarma
                Alarms.RemoveAll(a => a.Id == alarmId);
                ShiftAlarmIds();
            }
        }

        /// <summary>
        /// Pokreće skeniranje (novi thread) za dati tag.
        /// Thread će u petlji čitati vrednost iz simulatora i javljaće promene.
        /// </summary>
        public static void StartScanner(Tag tag)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            // obavezno startuj simulator pre nego što kreneš sa skeniranjem
            ConnectAndStartSimulator();

            lock (stateLock)
            {
                if (activeScannerThreads.ContainsKey(tag.id))
                    return; // već postoji skener za ovaj tag – ništa ne radi

                stopFlags[tag.id] = false; // resetuj zastavicu zaustavljanja

                // kreiraj novi thread koji će stalno čitati vrednosti za ovaj tag
                Thread t = new Thread(() => ScannerLoop(tag))
                {
                    IsBackground = true,          // da se thread ugasi automatski kad se zatvori app
                    Name = $"Scanner_Tag_{tag.id}"
                };
                activeScannerThreads[tag.id] = t;
                t.Start(); // pokreni skener
            }
        }
        /*public static void LoadConfiguration(string pathToConfig)
        {
            lock(stateLock){ 
                for (int i = 0; i < Tags.Count(); i++)
                    RemoveTag(Tags[i].id);
                for (int i = 0; i < Alarms.Count(); i++)
                    RemoveAlarm(Alarms[i].Id);
                UpdateInputOutputTags();
            
                XElement config = XElement.Load(pathToConfig);
                List<Alarm> newAlarms = new List<Alarm>();
                foreach (XElement alarm in config.Descendants("AlarmItem"))
                {
                    Alarm newAlarm = new Alarm(
                        int.Parse(alarm.Value),
                        int.Parse(alarm.Attribute("TagId").Value),
                        double.Parse(alarm.Attribute("LimitValue").Value),
                        (AlarmDirection)AlarmDirection.Parse(typeof(AlarmDirection), alarm.Attribute("Direction").Value),
                        alarm.Attribute("Message").Value,
                        bool.Parse(alarm.Attribute("IsActivated").Value)
                        );
                    newAlarms.Add(newAlarm);
                    Alarms.Add(newAlarm);
                    //AddAlarm(newAlarm);

                }
                //config.Descendants("Tag").ToList().ForEach(a => Console.WriteLine(a));
                foreach (XElement tag in config.Descendants("TagItem"))
                {
                    Console.WriteLine($"{tag.ToString()}");
                    if((TagType)TagType.Parse(typeof(TagType), tag.Attribute("Type").Value) == TagType.DI)
                    AddTag(new Tag (
                        int.Parse(tag.Value),
                        tag.Attribute("Name").Value,
                        (TagType)TagType.Parse(typeof(TagType), tag.Attribute("Type").Value),
                        tag.Attribute("Description").Value,
                        tag.Attribute("IOAddress").Value,
                        int.Parse(tag.Attribute("ScanTime").Value),
                        bool.Parse(tag.Attribute("Scan").Value)
                    ));
                    else if ((TagType)TagType.Parse(typeof(TagType), tag.Attribute("Type").Value) == TagType.DO)
                        AddTag(new Tag(
                            int.Parse(tag.Value),
                            tag.Attribute("Name").Value,
                            (TagType)TagType.Parse(typeof(TagType), tag.Attribute("Type").Value),
                            tag.Attribute("Description").Value,
                            tag.Attribute("IOAddress").Value,
                            double.Parse(tag.Attribute("Value").Value)
                        ));
                    else if ((TagType)TagType.Parse(typeof(TagType), tag.Attribute("Type").Value) == TagType.AO)
                        AddTag(new Tag(
                            int.Parse(tag.Value),
                            tag.Attribute("Name").Value,
                            (TagType)TagType.Parse(typeof(TagType), tag.Attribute("Type").Value),
                            tag.Attribute("Description").Value,
                            tag.Attribute("IOAddress").Value,
                            double.Parse(tag.Attribute("LowLimit").Value),
                            double.Parse(tag.Attribute("HighLimit").Value),
                            tag.Attribute("Units").Value,
                            double.Parse(tag.Attribute("Value").Value)
                        ));
                    else if ((TagType)TagType.Parse(typeof(TagType), tag.Attribute("Type").Value) == TagType.AO)
                        AddTag(new Tag(
                            int.Parse(tag.Value),
                            tag.Attribute("Name").Value,
                            (TagType)TagType.Parse(typeof(TagType), tag.Attribute("Type").Value),
                            tag.Attribute("Description").Value,
                            tag.Attribute("IOAddress").Value,
                            double.Parse(tag.Attribute("LowLimit").Value),
                            double.Parse(tag.Attribute("HighLimit").Value),
                            tag.Attribute("Units").Value,
                            newAlarms,
                            int.Parse(tag.Attribute("ScanTime").Value),
                            bool.Parse(tag.Attribute("Scan").Value)
                        ));
                }
                foreach( XElement activatedAlarm in config.Descendants("ActivatedAlarmItem"))
                {
                    ActivatedAlarms.Add(new ActivatedAlarm
                    (
                        int.Parse(activatedAlarm.Value),
                        activatedAlarm.Attribute("TagName").Value,
                        activatedAlarm.Attribute("Message").Value,
                        DateTime.Parse(activatedAlarm.Attribute("Time").Value)
                    ));
                }
            }

        }*/

        

        public static void SaveConfiguration(string path)
        {
            var config = new XElement("Config",
                new XElement("Tag",
                    from t in Tags
                    select new XElement("TagItem",
                        new XAttribute("Id", t.id),
                        new XAttribute("Name", t.name),
                        new XAttribute("Type", t.type),
                        new XAttribute("Description", t.Description ?? ""),
                        new XAttribute("IOAddress", t.IOAddress ?? ""),
                        new XAttribute("Value", t.currValue),
                        from kv in t.TagSpecific
                        select new XAttribute(kv.Key, kv.Value ?? "")
                    )
                ),
                new XElement("Alarm",
                    from a in Alarms
                    select new XElement("AlarmItem",
                        new XAttribute("Id", a.Id),
                        new XAttribute("TagId", a.TagId),
                        new XAttribute("LimitValue", a.LimitValue),
                        new XAttribute("Direction", a.Direction),
                        new XAttribute("Message", a.Message ?? "")
                    )
                ),
                new XElement("ActivatedAlarm",
                    from aa in ActivatedAlarms
                    select new XElement("ActivatedAlarmItem",
                        new XAttribute("AlarmId", aa.AlarmId),
                        new XAttribute("TagName", aa.TagName ?? ""),
                        new XAttribute("Time", aa.Time)
                    )
                )
            );

            config.Save(path);
        }

        public static void LoadConfiguration(string path)
        {
            if (!File.Exists(path)) return;

            lock (stateLock)
            {
                // Bezbedno obriši postojeće (zaustavi skenere itd.)
                TerminateAllScanners();
                Tags.Clear();
                Alarms.Clear();
                ActivatedAlarms.Clear();
                UpdateInputOutputTags();

                var config = XElement.Load(path);

                // 1) Parsiraj alarme u privremen listu (ne dodaj još u ContextClass)
                var alarmsTemp = new List<Alarm>();
                foreach (var aElem in config.Element("Alarm")?.Elements("AlarmItem") ?? Enumerable.Empty<XElement>())
                {
                    int aid = int.Parse(aElem.Attribute("Id")?.Value ?? "0");
                    int tagId = int.Parse(aElem.Attribute("TagId")?.Value ?? "0");
                    double limit = double.TryParse(aElem.Attribute("LimitValue")?.Value, out var lv) ? lv : 0;
                    AlarmDirection dir = Enum.TryParse<AlarmDirection>(aElem.Attribute("Direction")?.Value, out var dd) ? dd : AlarmDirection.HIGH;
                    string message = aElem.Attribute("Message")?.Value ?? "";
                    bool isActivated = bool.TryParse(aElem.Attribute("IsActivated")?.Value, out var ia) ? ia : false;

                    // Koristimo konstruktor koji ukljucuje isActivated (ako postoji)
                    alarmsTemp.Add(new Alarm(aid, tagId, limit, dir, message, isActivated));
                }

                // 2) Parsiraj tagove i dodaj ih (bez alarm-a) koristeci postojece konstruktore
                foreach (var elem in config.Element("Tag")?.Elements("TagItem") ?? Enumerable.Empty<XElement>())
                {
                    int id = int.Parse(elem.Attribute("Id")?.Value ?? "0");
                    string name = elem.Attribute("Name")?.Value ?? "";
                    TagType type = Enum.TryParse<TagType>(elem.Attribute("Type")?.Value, out var tt) ? tt : TagType.DI;
                    string desc = elem.Attribute("Description")?.Value ?? "";
                    string io = elem.Attribute("IOAddress")?.Value ?? "";
                    double currValue = double.TryParse(elem.Attribute("Value")?.Value, out var cv) ? cv : 0;

                    if (type == TagType.DI)
                    {
                        int scanTime = int.TryParse(elem.Attribute("ScanTime")?.Value, out var st) ? st : 1000;
                        bool scan = bool.TryParse(elem.Attribute("Scan")?.Value, out var s) ? s : true;
                        AddTag(new Tag(id, name, type, desc, io, scanTime, scan));
                    }
                    else if (type == TagType.DO)
                    {
                        float init = (float)currValue;
                        AddTag(new Tag(id, name, type, desc, io, init));
                    }
                    else if (type == TagType.AI)
                    {
                        float low = float.TryParse(elem.Attribute("LowLimit")?.Value, out var lowv) ? (float)lowv : 0f;
                        float high = float.TryParse(elem.Attribute("HighLimit")?.Value, out var highv) ? (float)highv : 0f;
                        string units = elem.Attribute("Units")?.Value ?? "";
                        int scanTime = int.TryParse(elem.Attribute("ScanTime")?.Value, out var st) ? st : 1000;
                        bool scan = bool.TryParse(elem.Attribute("Scan")?.Value, out var s) ? s : true;

                        // Prosledi prazan spisak alarm-a — kasnije ih dodajemo kroz AddAlarm
                        AddTag(new Tag(id, name, type, desc, io, low, high, units, new List<Alarm>(), scanTime, scan));
                    }
                    else if (type == TagType.AO)
                    {
                        float low = float.TryParse(elem.Attribute("LowLimit")?.Value, out var lowv) ? (float)lowv : 0f;
                        float high = float.TryParse(elem.Attribute("HighLimit")?.Value, out var highv) ? (float)highv : 0f;
                        string units = elem.Attribute("Units")?.Value ?? "";
                        float init = (float)currValue;
                        AddTag(new Tag(id, name, type, desc, io, low, high, units, init));
                    }
                }

                // 3) Sada dodaj alarme u ContextClass (tako da se povezu sa tag-ovima)
                foreach (var alarm in alarmsTemp)
                {
                    AddAlarm(alarm); // AddAlarm ce dodati alarm u Alarms listu i u tag.TagSpecific["Alarms"]
                }

                // 4) Aktivirani alarmi
                foreach (var act in config.Element("ActivatedAlarm")?.Elements("ActivatedAlarmItem") ?? Enumerable.Empty<XElement>())
                {
                    int aid = int.Parse(act.Attribute("AlarmId")?.Value ?? "0");
                    string tagName = act.Attribute("TagName")?.Value ?? "";
                    string message = act.Attribute("Message")?.Value ?? "";
                    DateTime time = DateTime.TryParse(act.Attribute("Time")?.Value, out var t) ? t : DateTime.Now;

                    ActivatedAlarms.Add(new ActivatedAlarm(aid, tagName, message, time));
                }

                UpdateInputOutputTags();
            }
        }

        /*public static void SaveConfiguration(string path)
         {
             XElement tags = new XElement("Tag");
             foreach(Tag tag in Tags)
             {
                 object lowLimit = null;
                 tag.TagSpecific.TryGetValue("LowLimit", out lowLimit);
                 object highLimit = null;
                 tag.TagSpecific.TryGetValue("HighLimit", out highLimit);
                 object ScanTime = null;
                 tag.TagSpecific.TryGetValue("ScanTime", out ScanTime);
                 object Scan = null;
                 tag.TagSpecific.TryGetValue("Scan", out Scan);
                 object Units = null;
                 tag.TagSpecific.TryGetValue("Units", out Units);

                 if(tag.type == TagType.DI)
                 {
                     tags.Add(new XElement(
                     "TagItem", tag.id,
                     new XAttribute("Name", tag.name),
                     new XAttribute("Type", tag.type),
                     new XAttribute("IOAddress", tag.IOAddress),
                     new XAttribute("Value", tag.currValue),
                     new XAttribute("Description", tag.Description),
                     new XAttribute("ScanTime", ScanTime),
                     new XAttribute("Scan", Scan)
                     ));
                 }
                 else if(tag.type == TagType.DO)
                 {
                     tags.Add(new XElement(
                     "TagItem", tag.id,
                     new XAttribute("Name", tag.name),
                     new XAttribute("Type", tag.type),
                     new XAttribute("IOAddress", tag.IOAddress),
                     new XAttribute("Value", tag.currValue),
                     new XAttribute("Description", tag.Description)
                     ));
                 }
                 else if(tag.type == TagType.AI)
                 {
                     tags.Add(new XElement(
                     "TagItem", tag.id,
                     new XAttribute("Name", tag.name),
                     new XAttribute("Type", tag.type),
                     new XAttribute("IOAddress", tag.IOAddress),
                     new XAttribute("Value", tag.currValue),
                     new XAttribute("Description", tag.Description),
                     new XAttribute("LowLimit", lowLimit),
                     new XAttribute("HighLimit", highLimit),
                     new XAttribute("ScanTime", ScanTime),
                     new XAttribute("Scan", Scan),
                     new XAttribute("Units", Units)
                     ));
                 }
                 else if (tag.type == TagType.AO)
                 {
                     tags.Add(new XElement(
                     "TagItem", tag.id,
                     new XAttribute("Name", tag.name),
                     new XAttribute("Type", tag.type),
                     new XAttribute("IOAddress", tag.IOAddress),
                     new XAttribute("Value", tag.currValue),
                     new XAttribute("Description", tag.Description),
                     new XAttribute("LowLimit", lowLimit),
                     new XAttribute("HighLimit", highLimit),
                     new XAttribute("Units", Units)
                     ));
                 }

             }
             XElement alarms = new XElement("Alarm");
             foreach(Alarm alarm in Alarms)
             {
                 alarms.Add(new XElement(
                 "AlarmItem", alarm.Id,
                 new XAttribute("TagId", alarm.TagId),
                 new XAttribute("LimitValue", alarm.LimitValue),
                 new XAttribute("Message", alarm.Message),
                 new XAttribute("IsActivated", alarm.isActivated),
                 new XAttribute("Direction", alarm.Direction)
                 ));
             }
             XElement activatedAlarms = new XElement("ActivatedAlarm");
             foreach(ActivatedAlarm activatedAlarm in ActivatedAlarms)
             {
                 activatedAlarms.Add(new XElement(
                     "ActivatedAlarmItem", activatedAlarm.AlarmId,
                     new XAttribute("Time", activatedAlarm.Time),
                     new XAttribute("Message", activatedAlarm.Message),
                     new XAttribute("TagName", activatedAlarm.TagName)
                     ));
             }

             XElement saveFileElement = new XElement("Config");
             saveFileElement.Add(tags);
             saveFileElement.Add(alarms);
             saveFileElement.Add(activatedAlarms);
             saveFileElement.Save(path);
         }
         */


        public static void TerminateScanner(int tagId)
        {
            lock (stateLock)
            {
                if (stopFlags.ContainsKey(tagId))
                    stopFlags[tagId] = true; // reci thread-u da se ugasi

                if (activeScannerThreads.TryGetValue(tagId, out var th))
                {
                    try
                    {
                        if (!th.Join(50)) // sačekaj do 1s da se thread završi
                        {
                            try { th.Abort(); } catch { } // ako neće lepo da se ugasi, nasilno ga prekini
                        }
                    }
                    catch { }
                    activeScannerThreads.Remove(tagId); // izbaci iz rečnika
                }
                
            }
        }

        /// <summary>
        /// Zaustavlja sve skenere i gasi simulator.
        /// Koristi se npr. pri zatvaranju aplikacije.
        /// </summary>
        public static void TerminateAllScanners()
        {
            List<int> running;
            lock (stateLock)
            {
                running = activeScannerThreads.Keys.ToList();
            }
            foreach (var id in running)
                TerminateScanner(id);

            lock (plcSimLock)
            {
                if (plcSim != null)
                {
                    try { plcSim.Abort(); } catch { }
                    plcSim = null;
                }
            }
        }

        /// <summary>
        /// Forsira izlaz (piše vrednost u PLC adresu).
        /// Koristi se za AO/DO tagove da "pošaljemo" vrednost u simulator.
        /// </summary>
        public static void ForceOutput(Tag tag)
        {
            lock (plcSimLock)
            {
                if (plcSim == null) ConnectAndStartSimulator();
                try
                {
                    if(tag.type == TagType.DO) 
                        plcSim.SetDigitalValue(tag.IOAddress, tag.currValue);
                    if (tag.type == TagType.AO) 
                        plcSim.SetAnalogValue(tag.IOAddress, tag.currValue);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[PLCDataHandler] Error ForceOutput: " + ex.Message);
                }
            }
        }

        // --- PRIVATNE METODE ---
        public static void UpdateInputOutputTags()
        {
            InputTags = Tags.Where(t => t.type == TagType.DI || t.type == TagType.AI).ToList();
            OutputTags = Tags.Where(t => t.type == TagType.DO || t.type == TagType.AO).ToList();
        }
        private static void ShiftTagIds()
        {
            int idx = 0;
            foreach(Tag tag in Tags)
            {
                tag.id = idx;
                idx++;
            }
        }
        private static void ShiftAlarmIds()
        {
            int idx = 0;
            foreach(Alarm alarm in Alarms)
            {
                alarm.Id = idx;
                idx++;
            }
        }
        /// <summary>
        /// Petlja koja se izvršava u posebnom thread-u i stalno čita vrednost za tag.
        /// Ako se vrednost promeni -> podiže event.
        /// Takođe proverava da li je neki alarm aktiviran.
        /// </summary>
        private static void ScannerLoop(Tag tag)
        {
            int intervalMs = (int)tag.TagSpecific["ScanTime"]; // default 1s interval

            while (true)
            {
                // proveri da li treba da prekine rad
                lock (stateLock)
                {
                    if (stopFlags.ContainsKey(tag.id) && stopFlags[tag.id])
                        break;
                }

                // pročitaj vrednost iz simulatora
                double value = ReadValueFromSimulator(tag.IOAddress);

                lock (stateLock)
                {
                    tag.prevValue = tag.currValue;
                    tag.currValue = value; // snimi i u sam tag
                    UpdateInputOutputTags();
                    if(tag.type == TagType.AI) { 
                        DateTime captureTime = DateTime.Now;
                        if(!InputTagsValueHistory.ContainsKey(captureTime))
                        InputTagsValueHistory.Add(captureTime,
                            new Dictionary<string, double>());
                        InputTagsValueHistory[captureTime].Add(tag.IOAddress, tag.currValue);
                        //Console.WriteLine($"Value : {InputTagsValueHistory[captureTime][tag.IOAddress]}");

                    }
                }

                if (true)
                {
                    // obavesti sve pretplaćene da se vrednost promenila
                    ValueChanged?.Invoke(tag, EventArgs.Empty);

                    // proveri sve alarme vezane za ovaj tag
                    List<Alarm> alarmsForTag;
                    lock (stateLock)
                    {
                        alarmsForTag = Alarms.Where(a => a.TagId == tag.id).ToList();
                    }
                    foreach (var alarm in alarmsForTag)
                    {
                            if (alarm.checkAlarm(tag.currValue) &&
                                !alarm.isActivated)
                            {

                                var act = new ActivatedAlarm(alarm.Id, tag.name ,alarm.Message);
                                lock (stateLock)
                                {
                                    Alarms[alarm.Id].isActivated = true;
                                    ActivatedAlarms.Add(act); // zapamti da se desio alarm
                                }
                                try { AlarmRaised?.Invoke(null, EventArgs.Empty); } catch { Console.WriteLine($"Alarm exception"); }
                            }
                    }
                }

                try { Thread.Sleep(intervalMs); } catch { break; } // čekaj sledeći ciklus
            }

            // kada petlja završi, očisti strukture
            lock (stateLock)
            {
                if (activeScannerThreads.ContainsKey(tag.id))
                    activeScannerThreads.Remove(tag.id);
                if (stopFlags.ContainsKey(tag.id))
                    stopFlags.Remove(tag.id);
            }
        }

        /// <summary>
        /// Pomoćna metoda za čitanje vrednosti iz simulatora.
        /// </summary>
        private static double ReadValueFromSimulator(string address)
        {
            if (string.IsNullOrEmpty(address)) return double.NaN;
            lock (plcSimLock)
            {
                if (plcSim == null) ConnectAndStartSimulator();
                try { return plcSim.GetAnalogValue(address); }
                catch { return double.NaN; }
            }
        }

    }
}