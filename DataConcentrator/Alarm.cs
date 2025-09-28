using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DataConcentrator
{

    public enum AlarmDirection
    {
        HIGH, // Aktivira se kada je iznad granice
        LOW // Aktivira se kada je ispod granice
    }

    public class Alarm
    {
        public int Id { get; set; }  // ID alarma
        public int TagId { get; set; }  // ID taga nad kojim je alarm
        public double LimitValue { get; set; }  // Granica za aktivaciju alarma
        public AlarmDirection Direction { get; set; }  // Iznad / Ispod
        public string DirectionDisplay { get; set; }
        public string Message { get; set; }  // Poruka alarma
        public bool isActivated { get; set; } = false;

        
        public Alarm(int id, int tagId, double limitValue, AlarmDirection direction, string message)
        {
            Id = id;
            TagId = tagId;
            LimitValue = limitValue;
            Direction = direction;
            DirectionDisplay = direction == AlarmDirection.HIGH ? "Greater or Equal" : "Lower or Equal"; 
            Message = message;
        }
        public Alarm(int id, int tagId, double limitValue, AlarmDirection direction, string message, bool isActivated)
        {
            Id = id;
            TagId = tagId;
            LimitValue = limitValue;
            Direction = direction;
            DirectionDisplay = direction == AlarmDirection.HIGH ? "Greater or Equal" : "Lower or Equal";
            Message = message;
            this.isActivated = isActivated;
        }

        public Boolean checkAlarm(double value)
        {
            return this.Direction == AlarmDirection.HIGH ? value >= this.LimitValue : value <= this.LimitValue;  
        }
        public override string ToString()
        {
            string printString = $"{Id},{TagId},{LimitValue},{Direction},{Message},{isActivated},";
            return printString;
        }
    }
}
