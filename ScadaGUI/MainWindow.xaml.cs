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

            ContextClass.AddTag(testTag1);
            ContextClass.AddTag(testTag2);
            ContextClass.AddTag(testTag3);
            ContextClass.AddTag(testTag4);
            //ContextClass.OutputTags = ContextClass.Tags.Where(t => t.type == TagType.DO || t.type == TagType.AO).ToList();
            // Pretplata na eventove DataConcentrator-a
            ContextClass.ValueChanged += ContextClass_ValueChanged;
            ContextClass.AlarmRaised += ContextClass_AlarmRaised;
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
                ContextClass.ActivatedAlarms.RemoveAll(a => a.AlarmId == editedAlarm.Id);
            }
        }
        private void TagLoadingRowsEventHandler(object sender, DataGridRowEventArgs e)
        {
            ContextMenu ctxMenu = new ContextMenu();
            //Make a event handler for clicking the remove button inside the context menu.
            MenuItem removeItem = new MenuItem { Header = "Remove" };

            removeItem.Click += (senderClick, eventArgs) => {
                ContextClass.RemoveTag((e.Row.Item as Tag).id);
                RefreshGrid();
            };
            ctxMenu.Items.Add(removeItem);
            e.Row.ContextMenu = ctxMenu;
        }
        private void ActivatedAlarmLoadingRowsEventHandler(object sender, DataGridRowEventArgs e)
        {
            e.Row.Background = Brushes.Red;
        }
        private void ContextClass_ValueChanged(object sender, EventArgs e)
        {
        }
        private void ContextClass_AlarmRaised(object sender, EventArgs e)
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
            foreach(Tag selectedTag in InputTagsGrid.SelectedItems)
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
        private void ReportBtn_Click(object sender, RoutedEventArgs e)
        {
            
            string fileName = @"..\..\log.txt";
            
            using (var writer = new StreamWriter(fileName, append: true))
            {
                foreach(DateTime time in ContextClass.InputTagsValueHistory.Keys)
                {
                    
                    //writer.WriteLine($"Time : {time}");
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
        /*private void SaveConfigurationEventHandler(object sender, RoutedEventArgs e)
        {
            ContextClass.SaveConfiguration("config.xml");
            MessageBox.Show("Uspesno sacuvano!");

        }
        private void LoadConfigurationEventHandler(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "XML File (*.xml)|*.xml|All files (*.*)|*.*";
            /*
            if (dlg.ShowDialog() == true)
            {
                string[] textFileContent = File.ReadAllLines(dlg.FileName);
                //Console.WriteLine(textFileContent[0]);
                List<Tag> tagsToLoad = new List<Tag>();
                List<Alarm> alarmsToLoad = new List<Alarm>();
                List<ActivatedAlarm> activatedAlarmsToLoad = new List<ActivatedAlarm>();
                foreach(string fieldContent in textFileContent)
                {
                    if (fieldContent.Contains("Alarm: ") && !fieldContent.Contains("ActivatedAlarm: "))
                    {
                        string field = fieldContent.Replace("Alarm: ", "");
                        string[] tagFields = field.Split(',');
                        Console.WriteLine($"--> { field}");
                        int id = int.Parse(tagFields[0]);
                        int tagId = int.Parse(tagFields[1]);
                        double limitValue = double.Parse(tagFields[2]);
                        AlarmDirection direction = (AlarmDirection)AlarmDirection.Parse(typeof(AlarmDirection), tagFields[3]);
                        string Message = tagFields[4];
                        bool isActivated = bool.Parse(tagFields[5]);
                        alarmsToLoad.Add(new Alarm(id, tagId, limitValue, direction, Message, isActivated));

                    }
                    if (fieldContent.Contains("Tag: "))
                    {
                        string field = fieldContent.Replace("Tag: ", "");
                        field = field.Replace("/,", "");
                        Console.WriteLine(field);
                        string[] tagFields = field.Split(',');
                        //tagFields.ToList().ForEach(l => Console.WriteLine(l));

                        int id = int.Parse(tagFields[0]);
                        string name = tagFields[1];
                        TagType type = (TagType)TagType.Parse(typeof(TagType), tagFields[2]);
                        string Description = tagFields[3];
                        string IOAddress = tagFields[4];
                        double currValue = double.Parse(tagFields[5]);


                        if (type == TagType.DI) 
                        {
                            int ScanTime = int.Parse(tagFields[5]);
                            bool Scan = bool.Parse(tagFields[6]);
                            tagsToLoad.Add(new DataConcentrator.Tag(id, name, type, Description, IOAddress, ScanTime, Scan));
                        }
                        if (type == TagType.DO)
                        {
                            tagsToLoad.Add(new DataConcentrator.Tag(id, name, type, Description, IOAddress, currValue));
                        }
                        if (type == TagType.AI)
                        {
                            double LowLimit = double.Parse(tagFields[6]);
                            double HighLimit = double.Parse(tagFields[7]);
                            string Units = tagFields[8];
                            List<Alarm> Alarms = new List<Alarm>();
                            if (alarmsToLoad.Where(a => a.TagId == id).DefaultIfEmpty() != null)
                                Alarms = alarmsToLoad.Where(a => a.TagId == id).ToList();
                            int ScanTime = int.Parse(tagFields[9]);
                            bool Scan = bool.Parse(tagFields[10]);
                            tagsToLoad.Add(new DataConcentrator.Tag(id, name, type, Description, IOAddress, LowLimit, HighLimit, Units, Alarms, ScanTime, Scan));
                        }
                        if (type == TagType.AO)
                        {
                            double LowLimit = double.Parse(tagFields[6]);
                            double HighLimit = double.Parse(tagFields[7]);
                            string Units = tagFields[8];
                            tagsToLoad.Add(new DataConcentrator.Tag(id, name, type, Description, IOAddress,  LowLimit, HighLimit, Units, currValue));
                        }
                    }
                    if(fieldContent.Contains("ActivatedAlarm: "))
                    {
                        string field = fieldContent.Replace("ActivatedAlarm: ", "");
                        string[] tagFields = field.Split(',');
                        Console.WriteLine(field);
                        int AlarmId = int.Parse(tagFields[0]);
                        string TagName = tagFields[1];
                        string Message = tagFields[2];
                        DateTime Time = DateTime.Parse(tagFields[3]);
                        activatedAlarmsToLoad.Add(new ActivatedAlarm(AlarmId, TagName, Message, Time));
                    }
                }
                ContextClass.LoadConfiguration(tagsToLoad, alarmsToLoad, activatedAlarmsToLoad);
                ContextClass.UpdateInputOutputTags();
                RefreshGrid();
            }
            */
        /*    if (dlg.ShowDialog() == true)
            ContextClass.LoadConfiguration(dlg.FileName);

            RefreshGrid();
        }*/

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

        // Osvježavanje DataGrid-a
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

                int id = ContextClass.Alarms.Count;

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



