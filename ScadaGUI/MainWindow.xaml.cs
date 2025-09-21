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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DataConcentrator;
using PLCSimulator;

namespace ScadaGUI
{
    public partial class MainWindow : Window
    {
        private List<Tag> _tags = new List<Tag>();
        private PLCSimulatorManager _plc;

        public MainWindow()
        {
            InitializeComponent();

            // Inicijalizacija PLC Simulator
            _plc = new PLCSimulatorManager();
            _plc.StartPLCSimulator();

            // Pretplata na eventove DataConcentrator-a
            PLCDataHandler.ValueChanged += PLCDataHandler_ValueChanged;
            PLCDataHandler.AlarmRaised += PLCDataHandler_AlarmRaised;

            RefreshGrid();
        }

        // Event handler za promenu vrednosti tagova
        private void PLCDataHandler_ValueChanged(object sender, EventArgs e)
        {
            // Osveži DataGrid na UI thread-u
            Dispatcher.Invoke(() =>
            {
                TagsGrid.Items.Refresh();
            });
        }

        // Event handler za aktiviranje alarma
        private void PLCDataHandler_AlarmRaised(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var lastAlarm = PLCDataHandler.ActivatedAlarms.LastOrDefault();
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

        // Dodavanje taga
        private void AddTagBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!(TagTypeCombo.SelectedItem is ComboBoxItem typeItem))
            {
                MessageBox.Show("Izaberi tip taga!");
                return;
            }

            TagType type = (TagType)Enum.Parse(typeof(TagType), typeItem.Content.ToString());
            string desc = DescriptionBox.Text;
            string addr = AddressBox.Text;
            int id = _tags.Count > 0 ? _tags.Max(t => t.id) + 1 : 1;

            Tag tag = null;

            try
            {
                switch (type)
                {
                    case TagType.DI:
                        int scanTimeDI = int.Parse(ScanTimeBox.Text);
                        bool scanDI = ScanCheckBox.IsChecked == true;
                        tag = new Tag(id, type, desc, addr, scanTimeDI, scanDI);
                        break;

                    case TagType.DO:
                        float initDO = float.Parse(InitValueBox.Text);
                        tag = new Tag(id, type, desc, addr, initDO);
                        break;

                    case TagType.AI:
                        float lowAI = float.Parse(LowLimitBox.Text);
                        float highAI = float.Parse(HighLimitBox.Text);
                        string unitsAI = UnitsBox.Text;
                        int scanTimeAI = int.Parse(ScanTimeBox.Text);
                        bool scanAI = ScanCheckBox.IsChecked == true;
                        tag = new Tag(id, type, desc, addr, lowAI, highAI, unitsAI, new List<Alarm>(), scanTimeAI, scanAI);
                        break;

                    case TagType.AO:
                        float lowAO = float.Parse(LowLimitBox.Text);
                        float highAO = float.Parse(HighLimitBox.Text);
                        string unitsAO = UnitsBox.Text;
                        float initAO = float.Parse(InitValueBox.Text);
                        tag = new Tag(id, type, desc, addr, lowAO, highAO, unitsAO, initAO);
                        break;
                }

                if (tag != null && tag.IsValid())
                {
                    _tags.Add(tag);
                    RefreshGrid();
                }
                else
                {
                    MessageBox.Show("Nevalidan tag!");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Greška pri unosu: {ex.Message}");
            }

            if (tag != null && tag.IsValid())
            {
                _tags.Add(tag);
                RefreshGrid();

                // START scanner za AI/DI tagove
                if (tag.type == TagType.AI || tag.type == TagType.DI)
                {
                    PLCDataHandler.AddTag(tag);          // dodaj tag u DataConcentrator
                    PLCDataHandler.StartScanner(tag);    // pokreni skener
                }
                else
                {
                    PLCDataHandler.AddTag(tag);          // dodaj AO/DO tag bez skenera
                }
            }
        }

        // Uklanjanje taga
        private void RemoveTagBtn_Click(object sender, RoutedEventArgs e)
        {
            /*if (TagsGrid.SelectedItem is Tag selectedTag)
            {
                _tags.Remove(selectedTag);
                RefreshGrid();
            }*/

            // Dobijamo tag koji je selektovan u DataGrid-u
            Tag tagToRemove = TagsGrid.SelectedItem as Tag;
            if (tagToRemove != null)
            {
                _tags.Remove(tagToRemove); // ukloni iz liste
                RefreshGrid();

                // Ako je AI/DI tag, zaustavi njegov skener
                if (tagToRemove.type == TagType.AI || tagToRemove.type == TagType.DI)
                {
                    PLCDataHandler.TerminateScanner(tagToRemove.id);
                }

                // Ukloni iz DataConcentrator-a
                PLCDataHandler.RemoveTag(tagToRemove.id);
            }

        }

        // Osvježavanje DataGrid-a
        private void RefreshGrid()
        {
            TagsGrid.ItemsSource = null;
            TagsGrid.ItemsSource = _tags;
        }

        private void TagsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        // Osvežavanje AI tagova u comboBox-u
        private void RefreshAITags()
        {
            AITagCombo.ItemsSource = _tags.Where(t => t.type == TagType.AI).ToList();
            AITagCombo.DisplayMemberPath = "Description";
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

                AlarmDirection dir = (AlarmDirection)Enum.Parse(typeof(AlarmDirection), dirItem.Content.ToString());
                string message = AlarmMessageBox.Text;

                int id = PLCDataHandler.Alarms.Count > 0 ? PLCDataHandler.Alarms.Max(a => a.Id) + 1 : 1;

                Alarm alarm = new Alarm(id, selectedTag.id, limit, dir, message);
                PLCDataHandler.AddAlarm(alarm);

                RefreshAlarmsGrid(selectedTag.id);
            }
        }
        
        // Uklanjanje alarma
        private void RemoveAlarmBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AlarmsGrid.SelectedItem is Alarm selectedAlarm)
            {
                PLCDataHandler.RemoveAlarm(selectedAlarm.Id);
                RefreshAlarmsGrid(selectedAlarm.TagId);
            }
        }

        // Osvežavanje DataGrid-a za alarme
        private void RefreshAlarmsGrid(int tagId)
        {
            AlarmsGrid.ItemsSource = null;
            AlarmsGrid.ItemsSource = PLCDataHandler.Alarms.Where(a => a.TagId == tagId).ToList();
        }
    }
}



