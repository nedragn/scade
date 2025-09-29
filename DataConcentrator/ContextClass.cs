using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PLCSimulator;

namespace DataConcentrator
{
    public static class ContextClass
    {
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
                if (!Tags.Any(t => t.id == tag.id) && !Tags.Any(t => t.name == tag.name)) // dodaj samo ako ne postoji već tag sa istim id
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
        public static void LoadConfiguration(List<Tag> tagsToLoad, List<Alarm> alarmsToLoad, List<ActivatedAlarm> activatedAlarmsToLoad)
        {
            lock(stateLock){ 
            for (int i = 0; i < Tags.Count(); i++)
                RemoveTag(Tags[i].id);
            for (int i = 0; i < Alarms.Count(); i++)
                RemoveAlarm(Alarms[i].Id);
            UpdateInputOutputTags();
        }
                
                foreach (Tag tagToLoad in tagsToLoad)
                {
                //Console.WriteLine(tagToLoad.ToString());
                    AddTag(tagToLoad);
                }

                foreach (Alarm alarmToLoad in alarmsToLoad)
                {
                    AddAlarm(alarmToLoad);
                }


                foreach (ActivatedAlarm activatedAlarmToLoad in activatedAlarmsToLoad)
                {
                    ActivatedAlarms.Add(activatedAlarmToLoad);
                }
                UpdateInputOutputTags();
        }
        public static void SaveConfiguration(string path)
        {

                using (var writer = new StreamWriter(path, append:false))
                {
                Alarms.ForEach(t => writer.WriteLine("Alarm: " + t.ToString()));
                Tags.ForEach(t => writer.WriteLine("Tag: " + t.ToString()));
                    ActivatedAlarms.ForEach(t => writer.WriteLine("ActivatedAlarm: " + t.ToString()));

                }     
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