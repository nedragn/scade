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
                if (!Tags.Any(t => t.name == tag.name && t.id == tag.id)) //Dodaj ako ne postoji sa istim imenom i id-em
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
        /// Removes alarm from the internal list of alarms.
        /// </summary>
        /// <param name="alarm">Alarm object to be removed</param>
        public static void RemoveAlarm(Alarm alarm)
        {
            lock (stateLock)
            {
                //Remove alarm from tags list of alarms
                if(Tags.Where(t => t.id == alarm.TagId).FirstOrDefault() != null)
                    ((List<Alarm>)Tags.Where(t => t.id == alarm.TagId).First().TagSpecific["Alarms"]).RemoveAll(a => a.Id == alarm.Id);
                //Remove from the acutal list of alarms
                Alarms.RemoveAll(a => a.Id == alarm.Id);
                ShiftAlarmIds();
            }
        }
        public static void StartScanner(Tag tag)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            ConnectAndStartSimulator();

            lock (stateLock)
            {
                if (activeScannerThreads.ContainsKey(tag.id)) return;

                stopFlags[tag.id] = false; 
                Thread t = new Thread(() => ScannerLoop(tag))
                {
                    IsBackground = true,          
                    Name = $"Scanner_Tag_{tag.id}"
                };
                activeScannerThreads[tag.id] = t;
                t.Start(); 

            }
        }
        public static void LoadConfiguration(string pathToConfig)
        {
            lock(stateLock){
                TerminateAllScanners();
                Tags.Clear();
                Alarms.Clear();
                ActivatedAlarms.Clear();
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
                    else if ((TagType)TagType.Parse(typeof(TagType), tag.Attribute("Type").Value) == TagType.AI)
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
                UpdateInputOutputTags();
            }

        }

        public static void SaveConfiguration(string path)
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
                if (Alarms.Where(a => a.TagId == tag.id).DefaultIfEmpty() != null)
                {

                    foreach (Alarm alarm in Alarms.Where(a => a.TagId == tag.id))
                    {
                        alarm.TagId = idx;
                        Console.WriteLine($"Found alarm with tag id: {alarm.TagId}");
                    }
                }
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