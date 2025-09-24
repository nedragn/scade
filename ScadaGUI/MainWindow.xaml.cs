/*using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;


namespace ScadaGUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    }
}*/

using DataConcentrator;
using PLCSimulator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Xml.Linq;
using System.Threading;

namespace ScadaGUI
{
    public partial class MainWindow : Window
    {
       
        private PLCSimulatorManager _plc;

        public MainWindow()
        {
            InitializeComponent();
            

            // Inicijalizacija PLC Simulator
            _plc = new PLCSimulatorManager();
            _plc.StartPLCSimulator();

            AddressBox.ItemsSource = PLCSimulatorManager.addressValues.Keys;



            TagsGrid.AutoGenerateColumns = false;
            TagsGrid.Columns.Clear();   
            List<string> columnNames = new List<string> {
                "Id", "Name", "Desc.", "Address", "Type",
                "Scan Time [AI, DI]", "Low Limit [AI, AO]", "High Limit [AI, AO]", "Units [AI, AO]", "Value"
            };  
            foreach(string columnName in columnNames)
            {
                TagsGrid.Columns.Add(new DataGridTextColumn { 
                    Header= columnName, 
                    Binding = new Binding($"[{columnName}]")});
            }

            columnNames = new List<string> {
                "Id", "Tag Id", "Limit Value", "Direction", "Message"
            };
            foreach (string columnName in columnNames)
            {
                AlarmsGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = columnName,
                    Binding = new Binding($"[{columnName}]")
                });
            }

            // Pretplata na eventove DataConcentrator-a
            ContextClass.ValueChanged += ContextClass_ValueChanged;
            ContextClass.AlarmRaised += ContextClass_AlarmRaised;
            RefreshGrid();
  
        }

        // Event handler za promenu vrednosti tagova
        private void ContextClass_ValueChanged(object sender, EventArgs e)
        {
            Console.WriteLine(sender);
            Dispatcher.Invoke(new Action(() => { RefreshGrid(); }));
        }

        // Event handler za aktiviranje alarma
        private void ContextClass_AlarmRaised(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var lastAlarm = ContextClass.ActivatedAlarms.LastOrDefault();
                if (lastAlarm != null)
                {
                    MessageBox.Show($"ALARM!\nTag: {lastAlarm.TagName}\nMessage: {lastAlarm.Message}\nTime: {lastAlarm.Time}");
                }
            });
        }

        // Dinamičko prikazivanje polja po tipu taga
        private void TagTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ScanFields.Visibility = Visibility.Collapsed;
            AnalogFields.Visibility = Visibility.Collapsed;
            InitValueField.Visibility = Visibility.Collapsed;

            if (TagTypeCombo.SelectedItem is ComboBoxItem item)
            {
                switch (item.Content.ToString())
                {
                    case "DI":
                        ScanFields.Visibility = Visibility.Visible;
                        break;
                    case "DO":
                        InitValueField.Visibility = Visibility.Visible;
                        break;
                    case "AI":
                        ScanFields.Visibility = Visibility.Visible;
                        AnalogFields.Visibility = Visibility.Visible;
                        break;
                    case "AO":
                        AnalogFields.Visibility = Visibility.Visible;
                        InitValueField.Visibility = Visibility.Visible;
                        break;
                }
            }
        }
        //Returns a truth value corresponding to the validity of the tag
        // Dodavanje taga, cela logika proveravanja ispravnosti tag treba biti u ContextClass
        private void AddTagBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!(TagTypeCombo.SelectedItem is ComboBoxItem typeItem))
            {
                MessageBox.Show("Izaberi tip taga!");
                return;
            }

            TagType type = (TagType)Enum.Parse(typeof(TagType), typeItem.Content.ToString());
            string name = TagNameBox.Text;
            string desc = DescriptionBox.Text;
            string addr = AddressBox.Text;
            if (PLCSimulatorManager.writtenAdresses.Contains(addr))
            {
                MessageBox.Show("Memory location is already populted. Remove tag from memory first.");
                return;
            }
            int id = ContextClass.Tags.Count > 0 ? ContextClass.Tags.Max(t => t.id) + 1 : 1;

            Tag tag = null;

            try
            {
                switch (type)
                {
                    case TagType.DI:
                        int scanTimeDI = int.Parse(ScanTimeBox.Text);
                        Boolean scanDI = (Boolean)ScanCheckBox.IsChecked;
                        tag = new Tag(id,name, type, desc, addr, scanTimeDI, scanDI);
                        break;

                    case TagType.DO:
                        float initDO = float.Parse(InitValueBox.Text);
                        tag = new Tag(id,name, type, desc, addr, initDO);
                        break;

                    case TagType.AI:
                        float lowAI = float.Parse(LowLimitBox.Text);
                        float highAI = float.Parse(HighLimitBox.Text);
                        string unitsAI = UnitsBox.Text;
                        int scanTimeAI = int.Parse(ScanTimeBox.Text);
                        bool scanAI = (Boolean)ScanCheckBox.IsChecked;
                        tag = new Tag(id,name, type, desc, addr, lowAI, highAI, unitsAI, new List<Alarm>(), scanTimeAI, scanAI);
                        break;

                    case TagType.AO:
                        float lowAO = float.Parse(LowLimitBox.Text);
                        float highAO = float.Parse(HighLimitBox.Text);
                        string unitsAO = UnitsBox.Text;
                        float initAO = float.Parse(InitValueBox.Text);
                        tag = new Tag(id,name, type, desc, addr, lowAO, highAO, unitsAO, initAO);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Oops...  {ex.Message}");
            }
            if (tag != null && ContextClass.AddTag(tag))
            {
                Console.WriteLine("Uslo!");
                PLCSimulatorManager.writtenAdresses.Add(addr);

                RefreshGrid();
            }
        }

        // Uklanjanje taga
        private void RemoveTagBtn_Click(object sender, RoutedEventArgs e)
        {
            // Dobijamo tag[ove] koji je selektovan u DataGrid-u
            List<Tag> tagsToRemove = new List<Tag>();
            foreach(Tag selectedTag in TagsGrid.SelectedItems)
            {
                Console.WriteLine(selectedTag);
                tagsToRemove.Add(selectedTag);
            }
            foreach(Tag tagToRemove in tagsToRemove)
            {
                if (tagToRemove != null)
                {
                    RefreshGrid();

                    // Ako je AI/DI tag, zaustavi njegov skener
                    if (tagToRemove.type == TagType.AI || tagToRemove.type == TagType.DI)
                    {
                        ContextClass.TerminateScanner(tagToRemove.id);
                    }

                    // Ukloni iz DataConcentrator-a
                    ContextClass.RemoveTag(tagToRemove.id);
                    PLCSimulatorManager.writtenAdresses.Remove(tagToRemove.IOAddress);
                }
            }


        }
        // Osvježavanje DataGrid-a
        private string checkTagSpecificExist(Tag tag, string key)
        {
            return tag.TagSpecific.ContainsKey(key) ? tag.TagSpecific[key].ToString() : " - ";
        }
        private void RefreshGridValueOnly(int id, int idx)
        {
                var rowValue = TagsGrid.Items[idx].GetType().GetProperty("Value");
                if (rowValue != null) rowValue.SetValue(TagsGrid.Items[idx], ContextClass.lastValue[id]);
                TagsGrid.Items.Refresh(); 


        }
        private void RefreshGrid()
        {
           
            //Treba prosirit
            TagsGrid.ItemsSource = null;
            var tagRows = new List<Dictionary<string, object>>();
            foreach (Tag tag in ContextClass.Tags)
            {
                tagRows.Add(new Dictionary<string, object>
                {
                    ["Id"] = tag.id,
                    ["Name"] = tag.name,
                    ["Desc."] = tag.Description,
                    ["Type"] = tag.type,
                    ["Address"] = tag.IOAddress,
                    ["Value"] = ContextClass.lastValue[tag.id].ToString(),
                    ["Scan Time [AI, DI]"]  = checkTagSpecificExist(tag, "ScanTime"),
                    ["Low Limit [AI, AO]"]  = checkTagSpecificExist(tag, "LowLimit"),
                    ["High Limit [AI, AO]"] = checkTagSpecificExist(tag, "HighLimit"),
                    ["Units [AI, AO]"]      = checkTagSpecificExist(tag, "Units"),

                });
            }
            TagsGrid.ItemsSource = tagRows;

            var alarmRows = new List<Dictionary<string, object>>();
            foreach (Alarm alarm in ContextClass.Alarms)
            {
                alarmRows.Add(new Dictionary<string, object>
                {
                    ["Id"] = alarm.Id.ToString(),
                    ["Tag Id"] = alarm.TagId.ToString(),
                    ["Limi Value"] = alarm.LimitValue.ToString(),
                    ["Direction"] = alarm.Direction.ToString(),
                    ["Message"] = alarm.Message,


                });
            }
            AITagCombo.ItemsSource = ContextClass.Tags.Where(t => t.type == TagType.AI);
            AlarmsGrid.ItemsSource = alarmRows;
        }

        // Dodavanje alarma
        private void AddAlarmBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AITagCombo.SelectedItem is Tag selectedTag)
            {
                if (!float.TryParse(LimitValueBox.Text, out float limit))
                {
                    MessageBox.Show("Neispravan limit value!");
                    return;
                }
                if (!(DirectionCombo.SelectedItem is ComboBoxItem dirItem))
                {
                    MessageBox.Show("Izaberi direction!");
                    return;
                }

                AlarmDirection dir = dirItem.Content.ToString().Equals("Greater or Equal") ? AlarmDirection.HIGH : AlarmDirection.LOW;
                string message = AlarmMessageBox.Text;

                int id = ContextClass.Alarms.Count > 0 ? ContextClass.Alarms.Max(a => a.Id) + 1 : 1;

                Alarm alarm = new Alarm(id, selectedTag.id, limit, dir, message);
                ContextClass.AddAlarm(alarm);

                RefreshAlarmsGrid(selectedTag.id);
            }
        }
        
        // Uklanjanje alarma
        private void RemoveAlarmBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AlarmsGrid.SelectedItem is Alarm selectedAlarm)
            {
                ContextClass.RemoveAlarm(selectedAlarm.Id);
                RefreshAlarmsGrid(selectedAlarm.TagId);
            }
        }

        // Osvežavanje DataGrid-a za alarme
        private void RefreshAlarmsGrid(int tagId)
        {
            AlarmsGrid.ItemsSource = null;
            AlarmsGrid.ItemsSource = ContextClass.Alarms.Where(a => a.TagId == tagId).ToList();
        }
    }
}



