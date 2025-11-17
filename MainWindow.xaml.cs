using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Configer.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Configer.Views;
using Configer.Services;
using Windows.UI; // keep for Color
using Microsoft.UI.Xaml.Media; // use WinUI 3 media namespace

namespace Configer
{
    public sealed partial class MainWindow : Window
    {
        private readonly ConfigManager _configManager;
        private string _currentSidebarTag = "DataConfig"; // track selected

        public ObservableCollection<EnvEntry> EnvEntries { get; } = new();
        public ObservableCollection<LexiconEntry> LexiconEntries { get; } = new();
        public ObservableCollection<SrtFileItem> SrtFiles { get; } = new();

        private DependencyCheckView? _depsView;
        private HelpView? _helpView;
        private RunView? _runView;

        private readonly SolidColorBrush _accentBrush = new(Color.FromArgb(255, 7, 208, 240));
        private readonly SolidColorBrush _selectedBgBrush = new(Color.FromArgb(255, 243, 244, 246));
        private readonly SolidColorBrush _defaultFgBrush = new(Color.FromArgb(255, 31, 41, 51));
        private readonly SolidColorBrush _settingsFgBrush = new(Color.FromArgb(255, 58, 214, 255));
        private readonly SolidColorBrush _transparentBrush = new(Color.FromArgb(0, 0, 0, 0));
        private readonly SolidColorBrush _disabledBgBrush = new(Color.FromArgb(255, 229, 231, 235));
        private readonly SolidColorBrush _disabledFgBrush = new(Color.FromArgb(255, 156, 163, 175));
        private readonly SolidColorBrush _disabledIndicatorBrush = new(Color.FromArgb(255, 209, 213, 219));

        public MainWindow()
        {
            this.InitializeComponent();
            _configManager = new ConfigManager(AppContext.BaseDirectory);

            // ���ݰ�
            EnvListView.ItemsSource = EnvEntries;
            LexiconListView.ItemsSource = LexiconEntries;
            SrtListView.ItemsSource = SrtFiles;
            SrtFiles.CollectionChanged += (_, __) => UpdateRunNavAvailability();

            UpdateRunNavAvailability();

            // Ĭ����ʾ��������ҳ��������Ѽ�Ϊ��ť��
            NavHeaderText.Text = "配置管理器";
            ContentRoot.Children.Clear();
            ContentRoot.Children.Add(DataConfigPage);

            // ��ʼ�����ѡ����ʽ
            UpdateSidebarSelection("DataConfig");

            // ��ʼ����
            _ = LoadConfigurationAsync();
        }

        private void UpdateSidebarSelection(string tag)
        {
            _currentSidebarTag = tag;

            Indicator_DataConfig.Visibility = Visibility.Collapsed;
            Indicator_Run.Visibility = Visibility.Collapsed;
            Indicator_Deps.Visibility = Visibility.Collapsed;
            Indicator_Help.Visibility = Visibility.Collapsed;
            Indicator_Settings.Visibility = Visibility.Collapsed;

            NavItemDataConfig.Background = _transparentBrush;
            NavItemRun.Background = NavItemRun.IsEnabled ? _transparentBrush : _disabledBgBrush;
            NavItemDeps.Background = _transparentBrush;
            NavItemHelp.Background = _transparentBrush;
            NavItemSettings.Background = _transparentBrush;

            Icon_Home.Foreground = _defaultFgBrush;
            Text_Home.Foreground = _defaultFgBrush;

            Icon_Run.Foreground = NavItemRun.IsEnabled ? _defaultFgBrush : _disabledFgBrush;
            Text_Run.Foreground = NavItemRun.IsEnabled ? _defaultFgBrush : _disabledFgBrush;
            NavItemRun.Opacity = NavItemRun.IsEnabled ? 1.0 : 0.55;
            Indicator_Run.Background = NavItemRun.IsEnabled ? _accentBrush : _disabledIndicatorBrush;
            if (!NavItemRun.IsEnabled)
            {
                Indicator_Run.Visibility = Visibility.Visible;
            }

            Icon_Deps.Foreground = _defaultFgBrush;
            Text_Deps.Foreground = _defaultFgBrush;

            Icon_Help.Foreground = _defaultFgBrush;
            Text_Help.Foreground = _defaultFgBrush;

            Icon_Settings.Foreground = _settingsFgBrush;
            Text_Settings.Foreground = _settingsFgBrush;

            if (tag == "DataConfig")
            {
                Indicator_DataConfig.Visibility = Visibility.Visible;
                NavItemDataConfig.Background = _selectedBgBrush;
                Icon_Home.Foreground = _accentBrush;
                Text_Home.Foreground = _accentBrush;
            }
            else if (tag == "Run" && NavItemRun.IsEnabled)
            {
                Indicator_Run.Visibility = Visibility.Visible;
                NavItemRun.Background = _selectedBgBrush;
                Icon_Run.Foreground = _accentBrush;
                Text_Run.Foreground = _accentBrush;
            }
            else if (tag == "Deps")
            {
                Indicator_Deps.Visibility = Visibility.Visible;
                NavItemDeps.Background = _selectedBgBrush;
                Icon_Deps.Foreground = _accentBrush;
                Text_Deps.Foreground = _accentBrush;
            }
            else if (tag == "Help")
            {
                Indicator_Help.Visibility = Visibility.Visible;
                NavItemHelp.Background = _selectedBgBrush;
                Icon_Help.Foreground = _accentBrush;
                Text_Help.Foreground = _accentBrush;
            }
            else if (tag == "Settings")
            {
                Indicator_Settings.Visibility = Visibility.Visible;
                NavItemSettings.Background = _selectedBgBrush;
                Icon_Settings.Foreground = _accentBrush;
                Text_Settings.Foreground = _accentBrush;
            }
        }

        // Keep hover state while pointer is over button �� simplified: only background changes, no indicator or icon color change
        private void NavItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                if (!btn.IsEnabled || tag == _currentSidebarTag) return; // already selected or disabled

                // Capture pointer to prevent child elements from interrupting hover
                try
                {
                    btn.CapturePointer(e.Pointer);
                }
                catch { }

                var hoverBg = new SolidColorBrush(Color.FromArgb(255, 245, 247, 248));

                // apply hover background to the button being hovered
                btn.Background = hoverBg;
            }
        }

        private void NavItem_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                if (!btn.IsEnabled)
                {
                    return;
                }

                // Release pointer capture when leaving
                try
                {
                    btn.ReleasePointerCapture(e.Pointer);
                }
                catch { }

                if (tag == _currentSidebarTag) return; // keep selected look

                btn.Background = _transparentBrush;
            }
        }

        // If capture is lost unexpectedly, ensure visuals are restored according to selection
        private void NavItem_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                if (!btn.IsEnabled)
                {
                    return;
                }

                if (tag == _currentSidebarTag)
                {
                    // ensure selected look remains
                    UpdateSidebarSelection(_currentSidebarTag);
                }
                else
                {
                    // clear hover visuals
                    btn.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                }
            }
        }

        private async Task LoadConfigurationAsync()
        {
            try
            {
                EnvEntries.Clear();
                var env = await _configManager.LoadEnvAsync();
                foreach (var kv in env)
                {
                    EnvEntries.Add(new EnvEntry { Key = kv.Key, Value = kv.Value });
                }

                LexiconEntries.Clear();
                var lex = await _configManager.LoadLexiconAsync();
                foreach (var item in lex)
                {
                    LexiconEntries.Add(item);
                }

                // Populate SRT files with status
                SrtFiles.Clear();
                var srtItems = _configManager.ListSrtFileItems();
                foreach (var item in srtItems)
                {
                    SrtFiles.Add(item);
                }

                UpdateRunNavAvailability();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = GetDialogXamlRoot(),
                    Title = "加载失败",
                    Content = ex.Message,
                    CloseButtonText = "确定"
                };

                await dialog.ShowAsync();
            }
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadConfigurationAsync();
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // ���� .env
                var envDict = EnvEntries.ToDictionary(x => x.Key ?? string.Empty, x => x.Value ?? string.Empty);
                await _configManager.SaveEnvAsync(envDict);

                // ���� lexicon
                await _configManager.SaveLexiconAsync(LexiconEntries);

                // ��ʾ
                var successDialog = new ContentDialog
                {
                    XamlRoot = GetDialogXamlRoot(),
                    Title = "保存成功",
                    Content = "配置已保存",
                    CloseButtonText = "确定"
                };

                await successDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    XamlRoot = GetDialogXamlRoot(),
                    Title = "保存失败",
                    Content = ex.Message,
                    CloseButtonText = "确定"
                };

                await errorDialog.ShowAsync();
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "data");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            // ����Դ������
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        private void AddEnvButton_Click(object sender, RoutedEventArgs e)
        {
            EnvEntries.Add(new EnvEntry { Key = "NEW_KEY", Value = "" });
        }

        private void RemoveEnvButton_Click(object sender, RoutedEventArgs e)
        {
            if (EnvListView.SelectedItem is EnvEntry selected)
            {
                EnvEntries.Remove(selected);
            }
        }

        private void AddLexButton_Click(object sender, RoutedEventArgs e)
        {
            LexiconEntries.Add(new LexiconEntry { Word = "new_word", Tag = "" });
        }

        private void RemoveLexButton_Click(object sender, RoutedEventArgs e)
        {
            if (LexiconListView.SelectedItem is LexiconEntry sel)
            {
                LexiconEntries.Remove(sel);
            }
        }

        private async void ImportSrtButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                // Initialize with the current window handle
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                picker.FileTypeFilter.Add(".srt");
                picker.SuggestedStartLocation = PickerLocationId.Desktop;

                var files = await picker.PickMultipleFilesAsync();
                if (files == null || files.Count == 0) return;

                var destDir = Path.Combine(AppContext.BaseDirectory, "data", "subtitle");
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                foreach (var file in files)
                {
                    // Copy via stream to ensure compatibility
                    var name = file.Name;
                    var dest = Path.Combine(destDir, name);
                    var uniqueDest = dest;
                    int i = 1;
                    while (File.Exists(uniqueDest))
                    {
                        uniqueDest = Path.Combine(destDir, Path.GetFileNameWithoutExtension(dest) + $" ({i})" + Path.GetExtension(dest));
                        i++;
                    }

                    using (var read = await file.OpenStreamForReadAsync())
                    using (var fs = File.Create(uniqueDest))
                    {
                        await read.CopyToAsync(fs);
                    }
                }

                // Refresh list with status
                SrtFiles.Clear();
                var srtItems = _configManager.ListSrtFileItems();
                foreach (var item in srtItems)
                {
                    SrtFiles.Add(item);
                }

                UpdateRunNavAvailability();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = GetDialogXamlRoot(),
                    Title = "导入失败",
                    Content = ex.Message,
                    CloseButtonText = "确定"
                };

                await dialog.ShowAsync();
            }
        }

        private XamlRoot GetDialogXamlRoot()
        {
            if (Content?.XamlRoot is XamlRoot windowRoot)
            {
                return windowRoot;
            }

            if (ContentRoot?.XamlRoot is XamlRoot contentRoot)
            {
                return contentRoot;
            }

            throw new InvalidOperationException("Unable to locate a XamlRoot for dialog presentation.");
        }

        private void RefreshSrtStatusButton_Click(object sender, RoutedEventArgs e)
        {
            // Re-scan roast directory and update Status for items
            var srtItems = _configManager.ListSrtFileItems();
            SrtFiles.Clear();
            foreach (var item in srtItems)
            {
                SrtFiles.Add(item);
            }
            UpdateRunNavAvailability();
        }

        private void UpdateRunNavAvailability()
        {
            bool hasSubtitles;
            try
            {
                hasSubtitles = _configManager.HasSubtitleFiles();
            }
            catch
            {
                hasSubtitles = false;
            }

            NavItemRun.IsEnabled = hasSubtitles;
            ToolTipService.SetToolTip(NavItemRun, hasSubtitles ? null : "请先在 data/subtitle 中导入 .srt 字幕文件");

            if (!hasSubtitles && _currentSidebarTag == "Run")
            {
                NavHeaderText.Text = "配置管理器";
                UpdateSidebarSelection("DataConfig");
                ContentRoot.Children.Clear();
                ContentRoot.Children.Add(DataConfigPage);
            }
            else
            {
                UpdateSidebarSelection(_currentSidebarTag);
            }
        }

        private async Task<bool> EnsureSubtitleAvailabilityAsync()
        {
            if (_configManager.HasSubtitleFiles())
            {
                return true;
            }

            UpdateRunNavAvailability();

            var dialog = new ContentDialog
            {
                XamlRoot = GetDialogXamlRoot(),
                Title = "暂无字幕文件",
                Content = "请先在 data/subtitle 目录放入 .srt 字幕文件后再运行。",
                CloseButtonText = "确定"
            };

            await dialog.ShowAsync();
            return false;
        }

        // New handler to support Button-based navigation in XAML sidebar
        private async void NavItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                if (tag == "Run" && !await EnsureSubtitleAvailabilityAsync())
                {
                    return;
                }

                // Update header based on tag
                if (tag == "DataConfig") NavHeaderText.Text = "配置管理器";
                else if (tag == "Run") NavHeaderText.Text = "运行";
                else if (tag == "Deps") NavHeaderText.Text = "依赖检查";
                else if (tag == "Help") NavHeaderText.Text = "帮助";
                else NavHeaderText.Text = tag;

                // Update sidebar visuals
                UpdateSidebarSelection(tag);

                if (tag == "DataConfig")
                {
                    ContentRoot.Children.Clear();
                    ContentRoot.Children.Add(DataConfigPage);
                }
                else if (tag == "Run")
                {
                    _runView ??= new RunView();
                    ContentRoot.Children.Clear();
                    ContentRoot.Children.Add(_runView);
                }
                else if (tag == "Deps")
                {
                    if (_depsView == null) _depsView = new DependencyCheckView();
                    ContentRoot.Children.Clear();
                    ContentRoot.Children.Add(_depsView);
                    await _depsView.RunChecksAsync();
                }
                else if (tag == "Help")
                {
                    if (_helpView == null) _helpView = new HelpView();
                    ContentRoot.Children.Clear();
                    ContentRoot.Children.Add(_helpView);
                    await _helpView.EnsureReadmeLoadedAsync();
                }
                else
                {
                    ContentRoot.Children.Clear();
                    ContentRoot.Children.Add(EmptyPage);
                }
            }
        }

        // Keep NavigationView handler for compatibility if NavigationView is ever used
        private async void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer is NavigationViewItem nvi)
            {
                NavHeaderText.Text = nvi.Content?.ToString() ?? "";
            }

            if (args.SelectedItemContainer == NavItemDataConfig)
            {
                ContentRoot.Children.Clear();
                ContentRoot.Children.Add(DataConfigPage);
            }
            else if (args.SelectedItemContainer == NavItemDeps)
            {
                if (_depsView == null) _depsView = new DependencyCheckView();
                ContentRoot.Children.Clear();
                ContentRoot.Children.Add(_depsView);
                await _depsView.RunChecksAsync();
            }
            else
            {
                ContentRoot.Children.Clear();
                ContentRoot.Children.Add(EmptyPage);
            }
        }
    }
}
