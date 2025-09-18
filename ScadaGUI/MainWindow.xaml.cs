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

            //PLC Simulator
            _plc = new PLCSimulatorManager();
            _plc.StartPLCSimulator();

            RefreshGrid();
        }

        
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

        //dodavanje taga
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
        }

        //brisanje taga
        private void RemoveTagBtn_Click(object sender, RoutedEventArgs e)
        {
            if (TagsGrid.SelectedItem is Tag selectedTag)
            {
                _tags.Remove(selectedTag);
                RefreshGrid();
            }
        }

        
        private void RefreshGrid()
        {
            TagsGrid.ItemsSource = null;
            TagsGrid.ItemsSource = _tags;
        }

        private void TagsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}


