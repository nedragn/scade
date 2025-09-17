using System;
using System.Collections.Generic;
using System.Linq;
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
        public string TagId { get; set; }  // ID taga nad kojim je alarm
        public double LimitValue { get; set; }  // Granica za aktivaciju alarma
        public AlarmDirection Direction { get; set; }  // Iznad / Ispod
        public string Message { get; set; }  // Poruka alarma

        
        public Alarm(int id, string tagId, double limitValue, AlarmDirection direction, string message)
        {
            Id = id;
            TagId = tagId;
            LimitValue = limitValue;
            Direction = direction;
            Message = message;
        }

        
        public bool checkAlarm(double currentValue)
        {
            if (Direction == AlarmDirection.HIGH && currentValue > LimitValue)
                return true;
            if (Direction == AlarmDirection.LOW && currentValue < LimitValue)
                return true;
            return false;
        }
    }
}
