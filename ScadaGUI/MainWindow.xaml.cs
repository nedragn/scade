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
using System.Diagnostics.Tracing;
using System.Windows.Input;
using System.Runtime.Remoting.Contexts;

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



            Tag testTag1 = new Tag(0,"Test tag 1", TagType.AI, " ", "ADDR001", -1, 1, "A", new List<Alarm>(), 100, true);
            Tag testTag2 = new Tag(1, "Test tag 2", TagType.AI, " ", "ADDR002", -10, 10, "V", new List<Alarm>(), 100, true);
            Tag testTag3 = new Tag(2, "Test tag 3", TagType.DO, " ", "ADDR005", 1);
            Tag testTag4 = new Tag(3, "Test tag 4", TagType.AO, " ", "ADDR006", -10, 10, "V", (float)2.7);

            ContextClass.AddTag(testTag1);
            ContextClass.AddTag(testTag2);
            ContextClass.AddTag(testTag3);
            ContextClass.AddTag(testTag4);
            // Pretplata na eventove DataConcentrator-a
            ContextClass.ValueChanged += ContextClass_ValueChanged;
            ContextClass.AlarmRaised += ContextClass_AlarmRaised;
            //RefreshGrid();
            DataContext = this;

        }
        private void BeginningEditEventHandler(object sender, DataGridBeginningEditEventArgs e)
        {
            Tag tag = e.Row.Item as Tag;
            Console.WriteLine($"{tag} {tag.id}");
            ContextClass.Tags[tag.id].prevValue = tag.value;
        }
        private void CellEditEndingEventHandler(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column.Header.ToString() == "Value")
            {
                Tag editedTag = e.Row.Item as Tag;
                editedTag = ContextClass.Tags[editedTag.id];
                if (e.EditAction == DataGridEditAction.Commit)
                {
                    Console.WriteLine($"Tag type {editedTag.type.GetType()}");
                    if (editedTag.type == TagType.DO && !((int)editedTag.value == 0 || (int)editedTag.value == 1))
                    {
                        MessageBox.Show("Digital outputs can only be ON(1) or OFF(0).");
                        ContextClass.Tags[editedTag.id].value = editedTag.prevValue;
                        return;
                    }
                    else if (editedTag.type == TagType.AO &&
                        ((float)editedTag.value <= (float)editedTag.TagSpecific["LowLimit"] ||
                         (float)editedTag.value >= (float)editedTag.TagSpecific["HighLimit"]))
                    {
                        MessageBox.Show("Value out of bounds. What did you set the limits for? ");
                        ContextClass.Tags[editedTag.id].value = editedTag.prevValue;
                        return;
                    }
                    else ContextClass.ForceOutput(editedTag);
                }
                
            }
        }
        private void TagLoadingRowsEventHandler(object sender, DataGridRowEventArgs e)
        {
            ContextMenu ctxMenu = new ContextMenu();
            //Make a event handler for clicking the remove button inside the context menu.
            MenuItem removeItem = new MenuItem { Header = "Remove" };

            removeItem.Click += (senderClick, eventArgs) => {
                ContextClass.RemoveTag((e.Row.Item as Tag).id);
                //RefreshGrid();
            };
            ctxMenu.Items.Add(removeItem);
            e.Row.ContextMenu = ctxMenu;
        }
        private void ContextClass_ValueChanged(object sender, EventArgs e)
        {

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

                //RefreshGrid();
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
                    //RefreshGrid();

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
        private void ReportBtn_Click(object sender, RoutedEventArgs e)
        {

        }
        // Osvježavanje DataGrid-a
        private void RefreshTagsGrid()
        {
            TagsGrid.ItemsSource = null;
            TagsGrid.ItemsSource =ContextClass.Tags;
            //TagsGrid.Items.Refresh();

            List<Tag> AITags = ContextClass.Tags.Where(t => t.type == TagType.AI).ToList();
            AITagCombo.ItemsSource = (from tag in AITags select tag.name).ToList();
        }
        private void RefreshAlarmsGrid()
        {
            AlarmsGrid.ItemsSource = null;
            AlarmsGrid.ItemsSource = ContextClass.Alarms;
            //AlarmsGrid.Items.Refresh();

            ActivatedAlarmsGrid.ItemsSource = null;
            ActivatedAlarmsGrid.ItemsSource = ContextClass.ActivatedAlarms;
            //ActivatedAlarmsGrid.Items.Refresh();
        }
        private void RefreshGrid()
        {

            //Treba prosirit
            RefreshTagsGrid();
            RefreshAlarmsGrid();
        }

        // Dodavanje alarma
        private void AddAlarmBtn_Click(object sender, RoutedEventArgs e)
        {
            string selectedTagName = AITagCombo.SelectedItem as string;
            Tag selectedTag = ContextClass.Tags.Where(t => t.name == selectedTagName).First();
            Console.WriteLine($"Selcted tag: {selectedTag.name}");
            if (selectedTag == null) Console.WriteLine("Tag is null");
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

                RefreshAlarmsGrid();
            }
        
        
        // Uklanjanje alarma
        private void RemoveAlarmBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AlarmsGrid.SelectedItem is Alarm selectedAlarm)
            {
                ContextClass.RemoveAlarm(selectedAlarm.Id);
                RefreshAlarmsGrid();
            }
        }

        // Osvežavanje DataGrid-a za alarme

    }
}



