using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataConcentrator
{
    internal class ActivatedAlarm
    {
        
        public int AlarmId { get; set; }
        public string TagName { get; set; }
        public string Message { get; set; }
        public DateTime Time { get; set; }

        public ActivatedAlarm(int alarmId, string tagName, string message)
        {
            AlarmId = alarmId;
            TagName = tagName;
            Message = message;
            Time = DateTime.Now; 
        }
    }
}
