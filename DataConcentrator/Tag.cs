using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;


namespace DataConcentrator
{

    public enum TagType { DI, DO, AI, AO}
    public class Tag
    {
        public int id { get; set; }
        public string name { get; set; }
        public TagType type { get; set; }
        public string Description { get; set; }
        public string IOAddress { get; set; }
        public double value { get; set; }    
        // Tag specific.
        public Dictionary<string, object> TagSpecific { get; set; }
        private List<string> TagSpecificKeysDI { get; set; } = new List<string> { "ScanTime", "Scan" };
        private List<string> TagSpecificKeysDO { get; set; } = new List<string> { "InitValue"};
        private List<string> TagSpecificKeysAI { get; set; } = new List<string> { "LowLimit", "HighLimit", "Units", "Alarms", "ScanTime", "Scan" };
        private List<string> TagSpecificKeysAO { get; set; } = new List<string> { "LowLimit", "HighLimit", "Units", "InitValue"};
        //Constructs foe each tag type.
        public Tag(int id, string name, TagType type, string description, string iOAddress, int ScanTime, Boolean Scan)
        {
            this.id = id;
            this.name = name;
            this.type = type;
            this.Description = description;
            this.IOAddress = iOAddress;

            this.TagSpecific = new Dictionary<string, object>();
            this.TagSpecific["ScanTime"] = ScanTime;
            this.TagSpecific["Scan"] = Scan;
        }
        public Tag(int id, string name, TagType type, string description, string iOAddress, float InitValue)
        {
            this.id = id;
            this.name = name;
            this.type = type;
            this.Description = description;
            this.IOAddress = iOAddress;

            this.TagSpecific = new Dictionary<string, object>();
            this.TagSpecific["InitValue"] = InitValue;
        }
        public Tag(int id, string name, TagType type, string description, string iOAddress, float LowLimit, float HighLimit, string Units, float InitValue)
        {
            this.id = id;
            this.name = name;
            this.type = type;
            this.Description = description;
            this.IOAddress = iOAddress;

            this.TagSpecific = new Dictionary<string, object>();
            this.TagSpecific["LowLimit"] = LowLimit;
            this.TagSpecific["HighLimit"] = HighLimit;
            this.TagSpecific["Units"] = Units;
            this.TagSpecific["InitValue"] = InitValue;
        }
        public Tag(int id, string name, TagType type, string description, string iOAddress, float LowLimit, float HighLimit, string Units,
    List<Alarm> Alarms, int ScanTime, Boolean Scan)
        {
            this.id = id;
            this.name = name;
            this.type = type;
            this.Description = description;
            this.IOAddress = iOAddress;

            this.TagSpecific = new Dictionary<string, object>();
            this.TagSpecific["LowLimit"] = LowLimit;
            this.TagSpecific["HighLimit"] = HighLimit;
            this.TagSpecific["Units"] = Units;
            this.TagSpecific["Alarms"] = Alarms;
            this.TagSpecific["ScanTime"] = ScanTime;
            this.TagSpecific["Scan"] = Scan;
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



    }

    
}
