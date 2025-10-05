using DataConcentrator;
using Microsoft.Win32;
using PLCSimulator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

            Tag testTag1 = new Tag(0,"Test tag 1", TagType.AI, " ", "ADDR001", -50, 50, "A", new List<Alarm>(), 1000, true);
            Tag testTag2 = new Tag(1, "Test tag 2", TagType.AI, " ", "ADDR002", -10, 10, "V", new List<Alarm>(), 1000, true);
            Tag testTag3 = new Tag(2, "Test tag 3", TagType.DO, " ", "ADDR005", 1);
            Tag testTag4 = new Tag(3, "Test tag 4", TagType.AO, " ", "ADDR006", -10, 10, "V", (float)2.7);
            Tag testTag5 = new Tag(4, "Test tag 5", TagType.AI, " ", "ADDR003", -50, 50, "A", new List<Alarm>(), 10, true);

            ContextClass.AddTag(testTag1);
            ContextClass.AddTag(testTag2);
            ContextClass.AddTag(testTag3);
            ContextClass.AddTag(testTag4);
            ContextClass.AddTag(testTag5);

            ContextClass.ValueChanged += ContextClassValueChangedEventHandler;
            ContextClass.AlarmRaised += ContextClassAlarmRaisedEventHandler;
            RefreshGrid();
            

        }
        private void BeginningEditEventHandler(object sender, DataGridBeginningEditEventArgs e)
        {
            Tag tag = e.Row.Item as Tag;
            Console.WriteLine($"{tag} {tag.id}");
            int tagIndexInOutputTags = ContextClass.OutputTags.IndexOf(tag);
            ContextClass.OutputTags[tagIndexInOutputTags].prevValue = tag.currValue;
            ContextClass.Tags[tag.id].prevValue = tag.currValue;
            ContextClass.UpdateInputOutputTags();
        }
        private void CellEditEndingEventHandler(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column.Header.ToString() == "Value")
            {
                Tag editedTag = e.Row.Item as Tag;
                int tagIndexInOutputTags = ContextClass.OutputTags.IndexOf(editedTag);
                editedTag = ContextClass.OutputTags[tagIndexInOutputTags];
                Console.WriteLine($"{editedTag.currValue}");    
                if (e.EditAction == DataGridEditAction.Commit)
                {
                    Console.WriteLine($"Tag type {editedTag.type.GetType()}");
                    if (editedTag.type == TagType.DO && !((int)editedTag.currValue == 0 || (int)editedTag.currValue == 1))
                    {
                        MessageBox.Show("Digital outputs can only be ON(1) or OFF(0).");
                        ContextClass.Tags[editedTag.id].currValue = editedTag.prevValue;
                        ContextClass.UpdateInputOutputTags();
                        return;
                    }
                    else if (editedTag.type == TagType.AO &&
                        ((double)editedTag.currValue <= (double)editedTag.TagSpecific["LowLimit"] ||
                         (double)editedTag.currValue >= (double)editedTag.TagSpecific["HighLimit"]))
                    {
                        MessageBox.Show("Value out of bounds. What did you set the limits for? ");
                        ContextClass.Tags[editedTag.id].currValue = editedTag.prevValue;
                        ContextClass.UpdateInputOutputTags();
                        return;
                    }
                    else
                    {
                        ContextClass.ForceOutput(editedTag);
                        
                        ContextClass.Tags[editedTag.id].currValue = editedTag.currValue;
                        Console.WriteLine($"Edited tag value: {editedTag.currValue}");
                        Console.WriteLine($"Edited tag value: {ContextClass.OutputTags[tagIndexInOutputTags].currValue}");
                        Console.WriteLine($"Edited tag value: {tagIndexInOutputTags}");
                        Console.WriteLine($"Edited tag value: {ContextClass.Tags[editedTag.id].currValue}");
                        Console.WriteLine($"Edited tag value: {PLCSimulatorManager.addressValues[editedTag.IOAddress]}");
                        ContextClass.UpdateInputOutputTags();
                    }
                    
                }
                RefreshOutputsGrid();
                
            }
            if (e.Column.Header.ToString() == "Scan")
            {
                //Update
                    Tag editedTag = e.Row.Item as Tag;
                    int tagIndexInInputTags = ContextClass.InputTags.IndexOf(editedTag);
                    editedTag = ContextClass.InputTags[tagIndexInInputTags];
                    ContextClass.Tags[editedTag.id].TagSpecific["Scan"] = editedTag.TagSpecific["Scan"];
                //Turn off scnars 
                    if (!(bool)editedTag.TagSpecific["Scan"])ContextClass.TerminateScanner(editedTag.id);
                    else ContextClass.StartScanner(editedTag);
                    ContextClass.UpdateInputOutputTags();
                RefreshInputsGrid();
            }
        }
        private void AlarmCellEditEndingEventHandler(object sender, DataGridCellEditEndingEventArgs e)
        {
            if(e.Column.Header.ToString() == "Activated")
            {
                Alarm editedAlarm = e.Row.Item as Alarm;
                editedAlarm = ContextClass.Alarms[editedAlarm.Id];
                //ContextClass.ActivatedAlarms.RemoveAll(a => a.AlarmId == editedAlarm.Id);
            }
        }
        private void TagLoadingRowsEventHandler(object sender, DataGridRowEventArgs e)
        {
            ContextMenu ctxMenu = new ContextMenu();
            //Make a event handler for clicking the remove button inside the context menu.
            MenuItem removeItem = new MenuItem { Header = "Remove" };

            removeItem.Click += (senderClick, eventArgs) => {
                ContextClass.RemoveTag(e.Row.Item as Tag);
                RefreshGrid();
            };
            ctxMenu.Items.Add(removeItem);
            e.Row.ContextMenu = ctxMenu;
        }
        private void AlarmLoadingRowsEventHandler(object sender, DataGridRowEventArgs e)
        {
            ContextMenu ctxMenu = new ContextMenu();
            //Make a event handler for clicking the remove button inside the context menu.
            MenuItem removeItem = new MenuItem { Header = "Remove" };

            removeItem.Click += (senderClick, eventArgs) => {
                ContextClass.RemoveAlarm(e.Row.Item as Alarm);
                RefreshGrid();
            };
            ctxMenu.Items.Add(removeItem);
            e.Row.ContextMenu = ctxMenu;
        }
        private void ActivatedAlarmLoadingRowsEventHandler(object sender, DataGridRowEventArgs e)
        {
            e.Row.Background = Brushes.Red;
        }
        private void ContextClassValueChangedEventHandler(object sender, EventArgs e)
        {
        }
        private void ContextClassAlarmRaisedEventHandler(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var lastAlarm = ContextClass.ActivatedAlarms.LastOrDefault();
                if (lastAlarm != null)
                {
                    MessageBox.Show($"ALARM!\nTag: {lastAlarm.TagName}\nMessage: {lastAlarm.Message}\nTime: {lastAlarm.Time}");
                }
                RefreshAlarmsGrid();
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
        private void AddTagBtnClickEventHandler(object sender, RoutedEventArgs e)
        {
            if (!(TagTypeCombo.SelectedItem is ComboBoxItem typeItem))
            {
                MessageBox.Show("Izaberi tip taga!");
                return;
            }

            TagType type = (TagType)Enum.Parse(typeof(TagType), typeItem.Content.ToString());
            string name = TagNameBox.Text;
            if (ContextClass.Tags.Where(t => t.name == name).FirstOrDefault() != null)
            {
                MessageBox.Show("Tag with that name already exists pleas choose a different name. ");
                return;
            }
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
        private void RemoveTagBtnClickEventHandler(object sender, RoutedEventArgs e)
        {
            // Dobijamo tag[ove] koji je selektovan u DataGrid-u
            List<Tag> tagsToRemove = new List<Tag>();
            foreach(Tag selectedTag in InputTagsGrid.SelectedItems)
            {
                Console.WriteLine(selectedTag);
                tagsToRemove.Add(selectedTag);
            }
            foreach(Tag tagToRemove in tagsToRemove)
            {
                if (tagToRemove != null)
                {
                    // Ukloni iz DataConcentrator-a
                    ContextClass.RemoveTag(tagToRemove);
                    PLCSimulatorManager.writtenAdresses.Remove(tagToRemove.IOAddress);
                    RefreshGrid();
                }
            }
        }
        private void ReportBtnClickEventHandler(object sender, RoutedEventArgs e)
        {
            string fileName = "log.txt";
            
            using (var writer = new StreamWriter(fileName, append: true))
            {
                foreach(DateTime time in ContextClass.InputTagsValueHistory.Keys)
                {
                    
                    foreach(string addr in ContextClass.InputTagsValueHistory[time].Keys)
                    {
                        ContextClass.InputTagsValueHistory[time].OrderBy(a => a.Key);

                        Tag tagAtAddress = ContextClass.Tags.Where(t => t.IOAddress == addr).First();
                        double lowLimit = (double)tagAtAddress.TagSpecific["LowLimit"];
                        double highLimit = (double)tagAtAddress.TagSpecific["HighLimit"];
                        double appendCriterion = (Math.Abs(lowLimit) + Math.Abs(highLimit)) / 2;
                        double value = ContextClass.InputTagsValueHistory[time][addr];
                        if (value <= appendCriterion + 5 && value >= appendCriterion - 5)
                            writer.WriteLine($"{time} : {addr} <= {value}");
                    }

                }
            }
        }
        private void SaveConfigurationEventHandler(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog();
            dlg.Filter = "XML File (*.xml)|*.xml|All files (*.*)|*.*";

            if (dlg.ShowDialog() == true)
            {
                ContextClass.SaveConfiguration(dlg.FileName);
                MessageBox.Show("Uspešno sačuvano!");
            }
        }

        private void LoadConfigurationEventHandler(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "XML File (*.xml)|*.xml|All files (*.*)|*.*";

            if (dlg.ShowDialog() == true)
            {
                ContextClass.LoadConfiguration(dlg.FileName);
                ContextClass.UpdateInputOutputTags();
                RefreshGrid();
                MessageBox.Show("Uspesno učitano!");
            }
        }
        private void RefreshInputsGrid()
        {
            InputTagsGrid.ItemsSource = null;
            InputTagsGrid.ItemsSource = ContextClass.InputTags;
        }
        private void RefreshOutputsGrid()
        {
            OutputTagsGrid.ItemsSource = null;
            OutputTagsGrid.ItemsSource = ContextClass.OutputTags;
        }
        private void RefreshTagsGrid()
        {
            RefreshInputsGrid();
            RefreshOutputsGrid();

            List<Tag> AITags = ContextClass.Tags.Where(t => t.type == TagType.AI).ToList();
            AITagCombo.ItemsSource = (from tag in AITags select tag.name).ToList();
        }
        private void RefreshAlarmsGrid()
        {
            AlarmsGrid.ItemsSource = null;
            AlarmsGrid.ItemsSource = ContextClass.Alarms;

            ActivatedAlarmsGrid.ItemsSource = null;
            ActivatedAlarmsGrid.ItemsSource = ContextClass.ActivatedAlarms;
        }
        private void RefreshGrid()
        {

            RefreshTagsGrid();
            RefreshAlarmsGrid();
        }

        /// <summary>
        /// Event handler za klik na dodavanje alarma
        /// </summary>
        private void AddAlarmBtnClickEventHandler(object sender, RoutedEventArgs e)
        {
            string selectedTagName = AITagCombo.SelectedItem as string;
            if (selectedTagName == null) return;
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

                int id = ContextClass.Alarms.Count;

                Alarm alarm = new Alarm(id, selectedTag.id, limit, dir, message);
                ContextClass.AddAlarm(alarm);

                RefreshAlarmsGrid();
            }
        
        
        /// <summary>
        /// Event handler za klik na remove alarm button
        /// </summary>
        private void RemoveAlarmBtnClickEventHandler(object sender, RoutedEventArgs e)
        {
            if (AlarmsGrid.SelectedItem is Alarm selectedAlarm)
            {
                ContextClass.RemoveAlarm(selectedAlarm);
                RefreshAlarmsGrid();
            }
        }
    }
}



