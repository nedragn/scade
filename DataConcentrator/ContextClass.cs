using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PLCSimulator;

namespace DataConcentrator
{
    /// <summary>
    /// PLCDataHandler – glavna klasa za rad sa simulatorom i tagovima.
    /// 
    /// Zadužena je za:
    /// - startovanje PLCSimulator-a
    /// - čuvanje liste tagova i alarma u memoriji
    /// - pokretanje thread-ova koji "skenuju" tagove u određenom intervalu
    /// - podizanje eventa (ValueChanged) kada se vrednost nekog taga promeni
    /// - podizanje eventa (AlarmRaised) kada se aktivira alarm
    /// - gašenje skenera i simulatora kada zatvaramo aplikaciju
    /// 
    /// GUI (tvoj WPF) može da se poveže na ove evente i prikazuje vrednosti/alarme.
    /// </summary>
    public static class ContextClass
    {
        // --- Simulator i sinhronizacija ---
        private static PLCSimulatorManager plcSim = null; // čuva instancu PLCSimulator-a
        private static readonly object plcSimLock = new object(); // služi za zaključavanje pristupa simulatoru iz više thread-ova

        // --- In-memory store (memorijske liste, bez baze) ---
        public static readonly List<Tag> Tags = new List<Tag>();                  // svi tagovi koje sistem zna
        public static readonly List<Alarm> Alarms = new List<Alarm>();            // svi alarmi
        public static readonly List<ActivatedAlarm> ActivatedAlarms = new List<ActivatedAlarm>(); // aktivirani alarmi (koji su se desili)

        // --- Threading / skeneri ---
        private static readonly Dictionary<int, Thread> activeScannerThreads = new Dictionary<int, Thread>(); // za svaki tag id čuvamo thread koji ga skenira
        private static readonly Dictionary<int, bool> stopFlags = new Dictionary<int, bool>(); // zastavice za gašenje skenera
        public static readonly Dictionary<int, double> lastValue = new Dictionary<int, double>(); // poslednja poznata vrednost za svaki tag
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
            lock (plcSimLock) // zaključavamo jer možda više thread-ova zove ovu metodu
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
                if (!Tags.Any(t => t.id == tag.id)) // dodaj samo ako ne postoji već tag sa istim id
                {

                    Tags.Add(tag);
                    if( tag.type == TagType.DI || tag.type == TagType.AI ) StartScanner(tag);
                }
                // inicijalno postavi lastValue na NaN (znači da još nemamo očitanu vrednost)
                if (!lastValue.ContainsKey(tag.id))
                    lastValue[tag.id] = double.NaN;
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
                lastValue.Remove(tagId);
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
                    tag.TagSpecific["Alarms"] = new List<Alarm>();

                ((List<Alarm>)tag.TagSpecific["Alarms"]).Add(alarm);
            }
        }

        /// <summary>
        /// Uklanja alarm iz liste po njegovom Id.
        /// </summary>
        public static void RemoveAlarm(int alarmId)
        {
            lock (stateLock)
            {
                Alarms.RemoveAll(a => a.Id == alarmId);
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

        /// <summary>
        /// Zaustavlja jedan skener (po tag id).
        /// </summary>
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
        public static void ForceOutput(string address, double value)
        {
            if (string.IsNullOrEmpty(address)) return;
            lock (plcSimLock)
            {
                if (plcSim == null) ConnectAndStartSimulator();
                try
                {
                    plcSim.SetAnalogValue(address, value); // direktno upiši vrednost
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[PLCDataHandler] Error ForceOutput: " + ex.Message);
                }
            }
        }

        // --- PRIVATNE METODE ---

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

                bool changed = false;
                lock (stateLock)
                {
                    // detektuj promenu vrednosti (ako je prva vrednost ili je različita)
                    if (!lastValue.ContainsKey(tag.id) || double.IsNaN(lastValue[tag.id]) || Math.Abs(lastValue[tag.id] - value) > 1e-6)
                    {
                        changed = true;
                        lastValue[tag.id] = value; // upamti novu vrednost
                        tag.value = value; // snimi i u sam tag
                    }
                }

                if (changed)
                {
                    // obavesti sve pretplaćene da se vrednost promenila
                    try { ValueChanged?.Invoke(tag, EventArgs.Empty); } catch { }

                    // proveri sve alarme vezane za ovaj tag
                    List<Alarm> alarmsForTag;
                    lock (stateLock)
                    {
                        alarmsForTag = Alarms.Where(a => a.TagId == tag.id).ToList();
                    }
                    foreach (var alarm in alarmsForTag)
                    {
                        try
                        {
                            if (alarm.checkAlarm(value))
                            {
                                var act = new ActivatedAlarm(alarm.Id, tag.Description ?? tag.IOAddress, alarm.Message);
                                lock (stateLock)
                                {
                                    ActivatedAlarms.Add(act); // zapamti da se desio alarm
                                }
                                TryPersistActivatedAlarm(act); // pokušaj upisa u bazu (ako implementiraš)

                                try { AlarmRaised?.Invoke(null, EventArgs.Empty); } catch { }
                            }
                        }
                        catch { }
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
                if (lastValue.ContainsKey(tag.id))
                    lastValue.Remove(tag.id);
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

        /// <summary>
        /// Trenutno ne radi ništa osim što beleži grešku.
        /// Kasnije ovde možeš dodati logiku da upisuješ aktivirane alarme u bazu.
        /// </summary>
        private static void TryPersistActivatedAlarm(ActivatedAlarm act)
        {
            try
            {
                // Primer:
                // using (var ctx = new ContextClass())
                // {
                //     ctx.ActivatedAlarms.Add(act);
                //     ctx.SaveChanges();
                // }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PLCDataHandler] PersistActivatedAlarm failed: " + ex.Message);
            }
        }
    }
}