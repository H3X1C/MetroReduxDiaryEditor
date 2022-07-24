using MetroUserData.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace MetroUserData
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<DiaryMetaData>? MappedDiaryPagesList { get; set; }
        public GameSelectedEnum GameSelectedEnum { get; set; }
        public int TotalDiaryEntries { get; set; }
        public int CollectedDiaryEntries { get; set; }
        private IConfigurationRoot _configuration { get; set; }
        private List<DiaryMetaData> _metro2033DiaryMetaData { get; set; }
        private List<DiaryMetaData> _metroLastLightDiaryMetaData { get; set; }
        private string _xboxNetDataString { get; set; }
        private string[] _userConfig { get; set; }
        private string _userConfigFilePath { get; set; }

        public MainWindow(IConfigurationRoot configuration)
        {
            _configuration = configuration;
            _metro2033DiaryMetaData = _configuration.GetSection("DiaryData:Metro2033").Get<List<DiaryMetaData>>();
            _metroLastLightDiaryMetaData = _configuration.GetSection("DiaryData:MetroLastLight").Get<List<DiaryMetaData>>();
            InitializeComponent();
            UpdateDiaryData();
        }

        public async Task initAsync()
        {
            // load data from file
            var xboxNetDataList = await loadXboxNetDataFromFile();
            if(xboxNetDataList is not null)
            {
                // Take extracted data and map
                MapDiaryMetaData(xboxNetDataList);
            }
        }

        public async Task<List<XboxNetData>?> loadXboxNetDataFromFile()
        {
            // Read in the user data file.
            if (GameSelectedEnum == GameSelectedEnum.Metro2033 && !Directory.Exists($"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\4A Games\\Metro 2033") || GameSelectedEnum == GameSelectedEnum.MetroLastLight && !Directory.Exists($"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\4A Games\\Metro Last Light"))
            {
                MessageBox.Show("Cannot locate %localappdata%/4A Games/<MetroGame>", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            var metroRootPath = GameSelectedEnum == GameSelectedEnum.Metro2033 ? Directory.GetDirectories($"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\4A Games\\Metro 2033").FirstOrDefault() : Directory.GetDirectories($"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\4A Games\\Metro Last Light").FirstOrDefault();
            if (metroRootPath is null)
            {
                MessageBox.Show("Cannot locate profile folder in %localappdata%/4A Games/<MetroGame>. Ensure you have launched the game at least once", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            // Record config path, save routine will use it
            _userConfigFilePath = $"{metroRootPath}\\user.cfg";

            // 1 time backup code, to make a backup of the users config prior to this tools modifications
            if (!Directory.Exists($"{metroRootPath}\\MetroDiaryEditorConfigBackup"))
            {
                MessageBox.Show($"A one time backup of your original user.cfg has been created at: {metroRootPath}\\MetroDiaryEditorConfigBackup", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                Directory.CreateDirectory($"{metroRootPath}\\MetroDiaryEditorConfigBackup");
                File.Copy(_userConfigFilePath, $"{metroRootPath}\\MetroDiaryEditorConfigBackup\\user.cfg");
            }

            _userConfig = await File.ReadAllLinesAsync(_userConfigFilePath);
            _xboxNetDataString = _userConfig.FirstOrDefault(x => x.StartsWith("xbox_net_data"));
            if (_xboxNetDataString is null)
            {
                MessageBox.Show("Could not find 'xbox_net_data' within the user.cfg. Your cfg file may be correct or from an unsupported version of the game. Aborting as unsafe to proceed", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            // Processs line data to extract out values
            var xboxNetDataValueString = Regex.Match(_xboxNetDataString, @"\(([^)]*)\)").Groups[1].Value;
            var xboxNetDataValueArray = xboxNetDataValueString.Split(",");

            // Further processing to assign to class representation
            var xboxNetDataList = new List<XboxNetData>();
            foreach (var NetData in xboxNetDataValueArray)
            {
                var pair = NetData.Split("=");
                xboxNetDataList.Add(new XboxNetData { XboxId = Convert.ToInt32(pair[0], 16), XboxValue = Convert.ToInt32(pair[1], 16) });
            }

            return xboxNetDataList;
        }

        public void MapDiaryMetaData(List<XboxNetData> xboxNetData)
        {
            // Validate
            if (xboxNetData.Count != 326)
            {
                MessageBox.Show("Encountered an unexpected data count. The game may have since updated and or your config is damaged / corrupted. Aborting as unsafe to proceed", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Game specific indexes
            var diaryStartIndex = (GameSelectedEnum == GameSelectedEnum.Metro2033) ? 305 : 247;
            var diaryEndIndex = (GameSelectedEnum == GameSelectedEnum.Metro2033) ? 355 : 289;
            var diaryCountIndex = (GameSelectedEnum == GameSelectedEnum.Metro2033) ? 293 : 404;
            var specificMetaData = (GameSelectedEnum == GameSelectedEnum.Metro2033) ? _metro2033DiaryMetaData : _metroLastLightDiaryMetaData;

            // Fetch diary pages offsets and label.
            var diaryPages = xboxNetData.Where(x => x.XboxId >= diaryStartIndex && x.XboxId <= diaryEndIndex).ToList();
            foreach (var page in diaryPages)
            {
                var metaData = specificMetaData.FirstOrDefault(x => x.Index == page.XboxId);
                if (metaData is null)
                {
                    specificMetaData.Add( new DiaryMetaData{ Index = 0, Collected = false, LevelName = "UNKNOWN", NoteNumber = 0 } );
                }
                else
                {
                    metaData.Collected = Convert.ToBoolean(page.XboxValue);
                }
            }

            // Query diary count - don't need to do this as we use our own calculation in case a users config is broken and counts don't match.
            var diaryCountEntity = xboxNetData.FirstOrDefault(x => x.XboxId == diaryCountIndex);
            if (diaryCountEntity is not null)
            {
                CollectedDiaryEntries = diaryCountEntity.XboxValue;
            }

            // Populate total count
            TotalDiaryEntries = specificMetaData.Count();

            // Push updates to UI
            UpdateDiaryData(specificMetaData);
        }

        public void RegenerateXboxNetData()
        {
            if (MappedDiaryPagesList is null)
            {
                return;
            }

            // Re-gen string representation of diary page values
            var outputList = new List<string>();
            foreach (var netDat in MappedDiaryPagesList)
            {
                outputList.Add($"{netDat.Index.ToString("x")}={Convert.ToInt32(netDat.Collected)}");    // Convertion not required for 0/1
            }

            // Formatted diary page data
            var outputString = string.Join(",", outputList);

            var startingHexIdentifier = outputList[0].Split("=")[0] + '=';

            var diaryCountIdentifier = (GameSelectedEnum == GameSelectedEnum.Metro2033) ? "125=" : "194=";

            // formatted diary total count data
            var diaryCountString = diaryCountIdentifier + CollectedDiaryEntries.ToString("x");

            // Get index locations in net data string for diary unlocks and total count
            var netDataDiaryStartIndex = _xboxNetDataString.IndexOf(startingHexIdentifier);
            var netDataDiaryCountIndex = _xboxNetDataString.IndexOf(diaryCountIdentifier);

            // Assemble new net data string
            var aStringBuilder = new StringBuilder(_xboxNetDataString);
            aStringBuilder.Remove(netDataDiaryStartIndex, outputString.Length);
            aStringBuilder.Insert(netDataDiaryStartIndex, outputString);
            aStringBuilder.Remove(netDataDiaryCountIndex, diaryCountString.Length);
            aStringBuilder.Insert(netDataDiaryCountIndex, diaryCountString);
            var outputXboxNetData = aStringBuilder.ToString();

            // Locate xbox_net_data and replace
            var netDataLineIndex = Array.FindIndex(_userConfig, row => row.StartsWith("xbox_net_data"));
            _userConfig[netDataLineIndex] = outputXboxNetData;

            // Write new config to disk
            File.WriteAllLines(_userConfigFilePath, _userConfig);

            // Inform user
            MessageBox.Show("Sucessfully written user config. The unlocks will be visible on next game launch.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void UpdateDiaryData(List<DiaryMetaData>? xboxNetData = null)
        {
            MappedDiaryPagesList = xboxNetData is null ? new ObservableCollection<DiaryMetaData>() : new ObservableCollection<DiaryMetaData>(xboxNetData);
            DiaryPagesGrid.ItemsSource = this.MappedDiaryPagesList;
            UpdateDiaryCount();
        }

        public void UpdateDiaryCount()
        {
            if (CollectedCount is not null && MappedDiaryPagesList is not null)
            {
                TotalDiaryEntries = MappedDiaryPagesList.Count;
                CollectedDiaryEntries = MappedDiaryPagesList.Count(x => x.Collected == true);
                CollectedCount.Content = $"Diary Entries Collected: {CollectedDiaryEntries}/{TotalDiaryEntries}";
            }
        }

        public bool CheckForActiveGameProcess()
        {
            if (Process.GetProcessesByName("metro").Length > 0)
            {
                MessageBox.Show("Please exit Metro before using this editor!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }
            return false;
        }

        // UI Events
        private void Metro2033Select_Checked(object sender, RoutedEventArgs e)
        {
            GameSelectedEnum = GameSelectedEnum.Metro2033;
            UpdateDiaryData();
        }

        private void MetroLastLightSelect_Checked(object sender, RoutedEventArgs e)
        {
            GameSelectedEnum = GameSelectedEnum.MetroLastLight;
            UpdateDiaryData();
        }
        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateDiaryCount(); // Re-sync count
        }

        private void Load_Click(object sender, RoutedEventArgs e)
        {
            if (CheckForActiveGameProcess())
            {
                return;
            }
            _ = initAsync();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (CheckForActiveGameProcess())
            {
                return;
            }

            RegenerateXboxNetData();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Setall_Click(object sender, RoutedEventArgs e)
        {
            if (MappedDiaryPagesList is not null)
            {
                UpdateDiaryData(MappedDiaryPagesList.Select(x => { x.Collected = true; return x; }).ToList());
            }
        }

        private void SetNone_Click(object sender, RoutedEventArgs e)
        {
            if (MappedDiaryPagesList is not null)
            {
                UpdateDiaryData(MappedDiaryPagesList.Select(x => { x.Collected = false; return x; }).ToList());
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"Metro Redux Diary Editor v{Assembly.GetEntryAssembly().GetName().Version}", "Error", MessageBoxButton.OK, MessageBoxImage.Information);

            // ToDo: Embed a text which has basic instructions on ussage and advice regarding getting achievements to pop
        }
    }
}
