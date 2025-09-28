using PLCSimulator;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;


namespace DataConcentrator
{

    public enum TagType { DI, DO, AI, AO} 
    public class Tag : INotifyPropertyChanged
    {
        public int id { get; set; }
        public string name { get; set; }
        public TagType type { get; set; }
        public string Description { get; set; }
        public string IOAddress { get; set; }
        
        private double _value { get; set; }
        public double currValue 
        {
            get => _value;
            set
            {
                if (TagSpecific != null && TagSpecific.TryGetValue("Scan", out var isScan) && isScan is bool scan && !scan) return;
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged();
                }
            }
        }
        public double prevValue { get; set; }
        public bool isInput { get; set; } = true;
        // Tag specific.
        public Dictionary<string, object> TagSpecific { get; set; }
        private List<string> TagSpecificKeysDI { get; set; } = new List<string> { "ScanTime", "Scan" };
        private List<string> TagSpecificKeysDO { get; set; } = new List<string> {};
        private List<string> TagSpecificKeysAI { get; set; } = new List<string> { "LowLimit", "HighLimit", "Units", "Alarms", "ScanTime", "Scan" };
        private List<string> TagSpecificKeysAO { get; set; } = new List<string> { "LowLimit", "HighLimit", "Units"};
        //Constructs foe each tag type.
        //DI
        public Tag(int id, string name, TagType type, string description, string iOAddress, int ScanTime, Boolean Scan)
        {
            this.id = id;
            this.name = name;
            this.type = type;
            this.Description = description;
            this.IOAddress = iOAddress;
            this.currValue = PLCSimulatorManager.addressValues[this.IOAddress];

            this.TagSpecific = new Dictionary<string, object>();
            this.TagSpecific["ScanTime"] = ScanTime;
            this.TagSpecific["Scan"] = Scan;
        }
        //DO
        public Tag(int id, string name, TagType type, string description, string iOAddress, double InitValue)
        {
            this.id = id;
            this.name = name;
            this.type = type;
            this.Description = description;
            this.IOAddress = iOAddress;
            this.currValue = InitValue;
            this.isInput = false;

            this.TagSpecific = new Dictionary<string, object>();
            //this.TagSpecific["InitValue"] = InitValue;
        }
        public Tag(int id, string name, TagType type, string description, string iOAddress, double LowLimit, double HighLimit, string Units, double InitValue)
        {
            this.id = id;
            this.name = name;
            this.type = type;
            this.Description = description;
            this.IOAddress = iOAddress;
            this.currValue = InitValue;
            this.isInput = false;

            this.TagSpecific = new Dictionary<string, object>();
            this.TagSpecific["LowLimit"] = LowLimit;
            this.TagSpecific["HighLimit"] = HighLimit;
            this.TagSpecific["Units"] = Units;
            //this.TagSpecific["InitValue"] = InitValue;
        }
        public Tag(int id, string name, TagType type, string description, string iOAddress, double LowLimit, double HighLimit, string Units,
    List<Alarm> Alarms, int ScanTime, Boolean Scan)
        {
            this.id = id;
            this.name = name;
            this.type = type;
            this.Description = description;
            this.IOAddress = iOAddress;
            this.currValue = PLCSimulatorManager.addressValues[this.IOAddress];

            this.TagSpecific = new Dictionary<string, object>();
            this.TagSpecific["LowLimit"] = LowLimit;
            this.TagSpecific["HighLimit"] = HighLimit;
            this.TagSpecific["Units"] = Units;
            this.TagSpecific["Alarms"] = Alarms;
            this.TagSpecific["ScanTime"] = ScanTime;
            this.TagSpecific["Scan"] = Scan;
        }
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public Boolean IsTagValid() // Should ideally move this to ctxClass
        {
            Boolean isValid = true;
            switch (this.type)
            {
                case TagType.DI:
                    printTagSpecificError(this.TagSpecificKeysDI, this.TagSpecific,ref isValid);
                    break;
                case TagType.DO:
                    printTagSpecificError(this.TagSpecificKeysDO, this.TagSpecific, ref isValid);
                    break;
                case TagType.AI:
                    printTagSpecificError(this.TagSpecificKeysAI, this.TagSpecific, ref isValid);
                    break;
                case TagType.AO:
                    printTagSpecificError(this.TagSpecificKeysAO, this.TagSpecific, ref isValid);
                    break;
            }
            return isValid;
        }
        private static void printTagSpecificError(List<string> TagSpecificKeys, Dictionary<string, object> TagSpecific, ref Boolean isValid)
        {
            foreach (string key in TagSpecificKeys)
            {
                Console.WriteLine(key);
                if (!TagSpecific.ContainsKey(key))
                {
                    Console.WriteLine($"Tag must contain {key}");
                    isValid = false;
                }
                //Check scan time
                if(key == "ScanTime")
                {
                    int ScanTime = Convert.ToInt32(TagSpecific["ScanTime"]); //Null = 0
                    Debug.WriteLine($"ScanTime: {ScanTime}");
                    if (ScanTime <= 0)
                    {
                        Console.WriteLine($"Scan time must be bigger then 0ms");
                        isValid = false;
                    }

                }

            }


        }
        public override string ToString()
        {
            string printString = $"{id},{name},{type},{Description},{IOAddress},{currValue},";
            List<string> unionTagSpecificKeys = TagSpecificKeysAI.Union(TagSpecificKeysAO).ToList();
            unionTagSpecificKeys = unionTagSpecificKeys.Union(TagSpecificKeysDI).ToList();
            unionTagSpecificKeys = unionTagSpecificKeys.Union(TagSpecificKeysDO).ToList();
            foreach (string key in unionTagSpecificKeys) 
            {
                if (TagSpecific.ContainsKey(key) && key != "Alarms")
                    printString += $"{TagSpecific[key]},";
                else printString += "/,";
            }
            //Console.WriteLine(printString);
            return printString;
        }


    }

    
}
