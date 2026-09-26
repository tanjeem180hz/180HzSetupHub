using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinSetupHub.App.Models;
using WinSetupHub.App.Services;
using WinSetupHub.App.Services.Profiles;
using WinSetupHub.App.Services.Progress;
using WinSetupHub.App.ViewModels;

namespace WinSetupHub.App;

public partial class MainWindow : Window
{
    private const string AllCategories = "All";
    private const int MaxActivityLines = 80;

    private readonly StoragePaths _paths;
    private readonly PackageCatalog _catalog;
    private readonly ProcessRunner _runner;
    private readonly WingetService _winget;
    private readonly WingetBootstrapperService _wingetBootstrapper;
    private readonly WindowsUpdateService _windowsUpdate;
    private readonly AppUpdateService _appUpdate;
    private readonly UninstallService _uninstallService;
    private readonly CleanupService _cleanupService;
    private readonly NetworkSpeedSampler _networkSpeedSampler = new();
    private readonly DispatcherTimer _homeFeatureTimer = new();
    private readonly ObservableCollection<PackageViewModel> _packages = new();
    private readonly ObservableCollection<PackageViewModel> _updatePackages = new();
    private readonly ObservableCollection<InstalledAppViewModel> _installedApps = new();
    private readonly ObservableCollection<CleanupTargetViewModel> _cleanupTargets = new();
    private readonly Queue<string> _activityLines = new();
    private readonly IReadOnlyList<HomeFeature> _homeFeatures =
    [
        new("01 / Update", "Update Center", "Refresh winget sources, select known updates, and upgrade only what you choose.", "Open Update Center", HomeFeatureTarget.Updates),
        new("02 / Setup", "Setup Apps", "Pick a profile, check apps, and install the essentials for a fresh Windows machine.", "Open Setup Apps", HomeFeatureTarget.Setup),
        new("03 / Uninstall", "Uninstaller", "Find desktop and Store apps, uninstall selected items, then remove safe leftovers when needed.", "Open Uninstaller", HomeFeatureTarget.Uninstaller),
        new("04 / Cleanup", "Junk Cleanup", "Scan temporary files, shader caches, crash dumps, and safe Windows cache targets before cleaning.", "Open Cleanup", HomeFeatureTarget.Cleanup),
        new("05 / Activity", "Activity", "Watch live logs, pause or stop long queues, and trigger Windows Update from one place.", "Open Activity", HomeFeatureTarget.Activity),
        new("06 / Storage", "Storage", "Review the local data folder where package config, logs, and downloads are kept.", "Open Storage", HomeFeatureTarget.Storage)
    ];

    private ICollectionView? _packagesView;
    private ICollectionView? _installedAppsView;
    private CancellationTokenSource? _operationCancellation;
    private ApplicationSettings _settings = new();
    private FrameworkElement? _activePage;
    private Storyboard? _welcomeLoop;
    private int _homeFeatureIndex;
    private bool _isDarkTheme = true;
    private bool _isSidebarCollapsed;
    private bool _sidebarPinnedOpen = true;
    private bool _isBusy;
    private bool _pauseRequested;
    private bool _hasScannedUpdatesThisSession;
    private bool _isAutoUpdateScanQueued;

    public MainWindow()
    {
        InitializeComponent();

        _paths = StoragePaths.CreateDefault();
        _settings = ApplicationSettings.Load(_paths);
        _catalog = new PackageCatalog(_paths);
        _runner = new ProcessRunner(_paths);
        _winget = new WingetService(_runner, _paths);
        _wingetBootstrapper = new WingetBootstrapperService(_paths, _runner);
        _windowsUpdate = new WindowsUpdateService(_runner);
        _appUpdate = new AppUpdateService(_paths);
        _uninstallService = new UninstallService(_runner);
        _cleanupService = new CleanupService();
        _homeFeatureTimer.Interval = TimeSpan.FromSeconds(3);
        _homeFeatureTimer.Tick += HomeFeatureTimer_Tick;

        DataRootText.Text = _paths.Root;
        ApplyTheme(IsDarkTheme(_settings.Ui?.Theme));
        SetSidebarCollapsed(collapsed: false, animate: false);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _activePage = HomePage;
        SetActiveNavigation(HomeNavButton);
        SetHomeFeature(0, animate: false);
        _welcomeLoop = (Storyboard)WelcomeOverlay.Resources["WelcomeLoop"];
        _welcomeLoop.Begin(WelcomeOverlay, true);
        WelcomeEnterButton.IsEnabled = false;
        WelcomeEnterButton.Content = "Preparing...";

        try
        {
            await InitializeAsync();
        }
        finally
        {
            WelcomeEnterButton.Content = "Sign up free";
            WelcomeEnterButton.IsEnabled = true;
        }
    }

    private async Task InitializeAsync()
    {
        SetBusy(true, "Loading package catalog.");
        Log("180Hz Setup Hub started.");
        Log($"Data root: {_paths.Root}");

        await LoadCatalogAsync();
        await LoadInstalledAppsAsync();
        LoadCleanupTargets();
        _settings = await ApplicationSettings.LoadAsync(_paths);
        if (!await EnsureWingetAvailableAsync())
        {
            return;
        }

        SummaryText.Text = "Essentials are selected. Check one app, selected apps, or all apps when you are ready.";
        SetBusy(false, "Ready");
    }

    private async Task LoadCatalogAsync()
    {
        var packages = await _catalog.LoadAsync();
        _packages.Clear();

        foreach (var package in packages)
        {
            var packageViewModel = new PackageViewModel(package);
            packageViewModel.PropertyChanged += Package_PropertyChanged;
            _packages.Add(packageViewModel);
        }

        _packagesView = CollectionViewSource.GetDefaultView(_packages);
        _packagesView.Filter = FilterPackage;
        PackagesList.ItemsSource = _packagesView;
        UpdatesList.ItemsSource = _updatePackages;

        CategoryFilter.ItemsSource = BuildCategoryList();
        CategoryFilter.SelectedIndex = 0;
        ProfileSelector.ItemsSource = SetupProfileCatalog.Profiles.Select(profile => profile.Name).ToList();
        ProfileSelector.SelectedIndex = 0;

        SummaryText.Text = $"{_packages.Count} apps loaded. Essentials are preselected.";
        UpdateSummary();
    }

    private async Task LoadInstalledAppsAsync()
    {
        var apps = await _uninstallService.GetInstalledApplicationsAsync();
        _installedApps.Clear();

        foreach (var app in apps)
        {
            var viewModel = new InstalledAppViewModel(app);
            viewModel.PropertyChanged += InstalledApp_PropertyChanged;
            _installedApps.Add(viewModel);
        }

        _installedAppsView = CollectionViewSource.GetDefaultView(_installedApps);
        _installedAppsView.Filter = FilterInstalledApp;
        InstalledAppsList.ItemsSource = _installedAppsView;
        UpdateUninstallerSummary();
        Log($"{_installedApps.Count} installed apps loaded for Uninstaller.");
    }

    private void LoadCleanupTargets()
    {
        _cleanupTargets.Clear();

        foreach (var target in _cleanupService.BuildTargets())
        {
            var viewModel = new CleanupTargetViewModel(target);
            viewModel.PropertyChanged += CleanupTarget_PropertyChanged;
            _cleanupTargets.Add(viewModel);
        }

        CleanupTargetsList.ItemsSource = _cleanupTargets;
        UpdateCleanupSummary();
    }

    private async Task<bool> EnsureWingetAvailableAsync()
    {
        var wingetAvailable = await _winget.IsAvailableAsync(Log);
        if (wingetAvailable)
        {
            return true;
        }

        SetBusy(true, "Preparing Windows Package Manager.");
        SummaryText.Text = "Preparing Windows Package Manager for app installs and updates.";

        try
        {
            if (await _wingetBootstrapper.EnsureWingetAsync(Log))
            {
                SetBusy(false, "Windows Package Manager is ready.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Log($"Windows Package Manager preparation failed: {ex.Message}");
        }

        SetBusy(false, "Windows Package Manager could not be prepared automatically.");
        MessageBox.Show(
            "180Hz Setup Hub is ready, but Windows Package Manager could not be prepared automatically on this PC. You can still use Cleanup and Uninstaller. App install/update needs App Installer or winget.",
            "Windows Package Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private IReadOnlyList<string> BuildCategoryList()
    {
        return new[] { AllCategories }
            .Concat(_packages.Select(package => package.Category).Distinct().OrderBy(category => category))
            .ToList();
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(HomePage, HomeNavButton, "Home", "Pick a setup area and move step by step.");
    }

    private void SetupNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(SetupPage, SetupNavButton, "Setup Apps", "Choose a profile, check apps, then install exactly what you want.");
    }

    private async void UpdatesNav_Click(object sender, RoutedEventArgs e)
    {
        ShowUpdatesPage();
        await AutoScanUpdatesIfNeededAsync();
    }

    private void ShowUpdatesPage()
    {
        ShowPage(UpdatesPage, UpdatesNavButton, "Update Center", "Automatically scan installed apps and update only what you choose.");
    }

    private void UninstallerNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(UninstallerPage, UninstallerNavButton, "Uninstaller", "Remove desktop and Store apps, then clean safe leftovers when needed.");
    }

    private void CleanupNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(CleanupPage, CleanupNavButton, "Junk Cleanup", "Scan temp files, shader caches, crash dumps, and safe Windows cache targets.");
    }

    private void ActivityNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(ActivityPage, ActivityNavButton, "Activity", "Watch the current queue, control long runs, and review local logs.");
    }

    private void StorageNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(StoragePage, StorageNavButton, "Storage", "Open runtime config, package catalog, logs, and downloads.");
    }

    private async void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ApplyTheme(!_isDarkTheme);
        await SaveThemePreferenceAsync();
    }

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        _sidebarPinnedOpen = _isSidebarCollapsed;
        SetSidebarCollapsed(!_isSidebarCollapsed, animate: true);
    }

    private void SidebarPanel_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isSidebarCollapsed)
        {
            SetSidebarCollapsed(collapsed: false, animate: true);
        }
    }

    private void SidebarPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_sidebarPinnedOpen)
        {
            SetSidebarCollapsed(collapsed: true, animate: true);
        }
    }

    private void StartSetup_Click(object sender, RoutedEventArgs e)
    {
        ApplyProfile(SetupProfileCatalog.Default);
        UpdateSummary();
        ShowPage(SetupPage, SetupNavButton, "Setup Apps", "Choose a profile, check apps, then install exactly what you want.");
    }

    private void WelcomeEnter_Click(object sender, RoutedEventArgs e)
    {
        RevealShell();
    }

    private void PreviousHomeFeature_Click(object sender, RoutedEventArgs e)
    {
        SetHomeFeature(_homeFeatureIndex - 1, animate: true);
        RestartHomeFeatureTimer();
    }

    private void NextHomeFeature_Click(object sender, RoutedEventArgs e)
    {
        SetHomeFeature(_homeFeatureIndex + 1, animate: true);
        RestartHomeFeatureTimer();
    }

    private void HomeFeatureTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value } && int.TryParse(value, out var index))
        {
            SetHomeFeature(index, animate: true);
            RestartHomeFeatureTimer();
        }
    }

    private void HomeFeatureTimer_Tick(object? sender, EventArgs e)
    {
        SetHomeFeature(_homeFeatureIndex + 1, animate: true);
    }

    private void OpenHomeFeature_Click(object sender, RoutedEventArgs e)
    {
        var feature = _homeFeatures[_homeFeatureIndex];
        switch (feature.Target)
        {
            case HomeFeatureTarget.Setup:
                ShowPage(SetupPage, SetupNavButton, "Setup Apps", "Choose a profile, check apps, then install exactly what you want.");
                break;
            case HomeFeatureTarget.Updates:
                ShowUpdatesPage();
                _ = AutoScanUpdatesIfNeededAsync();
                break;
            case HomeFeatureTarget.Uninstaller:
                ShowPage(UninstallerPage, UninstallerNavButton, "Uninstaller", "Remove desktop and Store apps, then clean safe leftovers when needed.");
                break;
            case HomeFeatureTarget.Cleanup:
                ShowPage(CleanupPage, CleanupNavButton, "Junk Cleanup", "Scan temp files, shader caches, crash dumps, and safe Windows cache targets.");
                break;
            case HomeFeatureTarget.Activity:
                ShowPage(ActivityPage, ActivityNavButton, "Activity", "Watch the current queue, control long runs, and review local logs.");
                break;
            case HomeFeatureTarget.Storage:
                ShowPage(StoragePage, StorageNavButton, "Storage", "Open runtime config, package catalog, logs, and downloads.");
                break;
        }
    }

    private void SetHomeFeature(int index, bool animate)
    {
        _homeFeatureIndex = (index + _homeFeatures.Count) % _homeFeatures.Count;
        var feature = _homeFeatures[_homeFeatureIndex];

        HomeFeatureBadgeText.Text = feature.Badge;
        HomeFeatureTitleText.Text = feature.Title;
        HomeFeatureSubtitleText.Text = feature.Subtitle;
        HomeFeatureActionButton.Content = feature.ActionText;
        HomeFeatureNumberText.Text = $"{_homeFeatureIndex + 1:00}";
        HomeFeaturePositionText.Text = $"{_homeFeatureIndex + 1:00} of {_homeFeatures.Count:00}";
        UpdateHomeFeatureTiles();

        if (!animate)
        {
            HomeFeatureStage.Opacity = 1;
            HomeFeatureTransform.X = 0;
            HomeFeatureTransform.Y = 0;
            return;
        }

        HomeFeatureStage.Opacity = 0;
        HomeFeatureTransform.X = 24;
        HomeFeatureTransform.Y = 0;
        HomeFeatureStage.BeginAnimation(OpacityProperty, BuildDoubleAnimation(1, 240));
        HomeFeatureTransform.BeginAnimation(TranslateTransform.XProperty, BuildDoubleAnimation(0, 320));
    }

    private void UpdateHomeFeatureTiles()
    {
        var tiles = new[] { HomeFeatureTile0, HomeFeatureTile1, HomeFeatureTile2, HomeFeatureTile3, HomeFeatureTile4, HomeFeatureTile5 };
        var activeBackground = _isDarkTheme ? "#1E1836" : "#EDE9FE";
        var inactiveBackground = _isDarkTheme ? "#0D101A" : "#F8FAFC";
        var activeBorder = _isDarkTheme ? "#00F0FF" : "#7C3AED";
        var inactiveBorder = _isDarkTheme ? "#232A42" : "#CBDDE8";

        for (var index = 0; index < tiles.Length; index++)
        {
            var isActive = index == _homeFeatureIndex;
            tiles[index].Opacity = isActive ? 1 : 0.62;
            tiles[index].Background = BrushFromHex(isActive ? activeBackground : inactiveBackground);
            tiles[index].BorderBrush = BrushFromHex(isActive ? activeBorder : inactiveBorder);
        }
    }

    private void UpdateHomeFeatureTimer()
    {
        if (_activePage == HomePage && WelcomeOverlay.Visibility == Visibility.Collapsed)
        {
            if (!_homeFeatureTimer.IsEnabled)
            {
                _homeFeatureTimer.Start();
            }

            return;
        }

        _homeFeatureTimer.Stop();
    }

    private void RestartHomeFeatureTimer()
    {
        if (!_homeFeatureTimer.IsEnabled)
        {
            return;
        }

        _homeFeatureTimer.Stop();
        _homeFeatureTimer.Start();
    }

    private void ApplyTheme(bool useDarkTheme)
    {
        _isDarkTheme = useDarkTheme;

        if (useDarkTheme)
        {
            SetBrushColor("AppBackground", "#07080D");
            SetBrushColor("PanelBackground", "#0D101A");
            SetBrushColor("SubtleBackground", "#151A2B");
            SetBrushColor("PanelBorder", "#232A42");
            SetBrushColor("PrimaryText", "#F1F5F9");
            SetBrushColor("MutedText", "#8A99B5");
            SetBrushColor("AccentBrush", "#00F0FF");
            SetBrushColor("AccentDarkBrush", "#B026FF");
            SetBrushColor("AccentForeground", "#070913");
            SetBrushColor("ButtonHover", "#1E253D");
            SetBrushColor("ButtonDisabled", "#111420");
            SetBrushColor("SuccessBackground", "#08281D");
            SetBrushColor("SuccessText", "#00FFA3");
            SetBrushColor("WarningBackground", "#2D1E04");
            SetBrushColor("WarningText", "#FFB800");
            SetBrushColor("DangerBackground", "#360A17");
            SetBrushColor("DangerText", "#FF2A6D");
            SetBrushColor("InfoBackground", "#0B2540");
            SetBrushColor("InfoText", "#38BDF8");
            SetBrushColor("NeutralBackground", "#141724");
            SetBrushColor("NeutralText", "#94A3B8");
            BgStopOne.Color = Color.FromRgb(7, 8, 13);
            BgStopTwo.Color = Color.FromRgb(13, 16, 27);
            BgStopThree.Color = Color.FromRgb(19, 14, 36);
        }
        else
        {
            SetBrushColor("AppBackground", "#F4F6FB");
            SetBrushColor("PanelBackground", "#FFFFFF");
            SetBrushColor("SubtleBackground", "#EEF4FF");
            SetBrushColor("PanelBorder", "#D5DCE8");
            SetBrushColor("PrimaryText", "#0F172A");
            SetBrushColor("MutedText", "#526077");
            SetBrushColor("AccentBrush", "#7C3AED");
            SetBrushColor("AccentDarkBrush", "#6D28D9");
            SetBrushColor("AccentForeground", "#FFFFFF");
            SetBrushColor("ButtonHover", "#EDE9FE");
            SetBrushColor("ButtonDisabled", "#E2E8F0");
            SetBrushColor("SuccessBackground", "#E6FBF2");
            SetBrushColor("SuccessText", "#059669");
            SetBrushColor("WarningBackground", "#FEF3C7");
            SetBrushColor("WarningText", "#D97706");
            SetBrushColor("DangerBackground", "#FFE4E6");
            SetBrushColor("DangerText", "#E11D48");
            SetBrushColor("InfoBackground", "#E0F2FE");
            SetBrushColor("InfoText", "#0284C7");
            SetBrushColor("NeutralBackground", "#F1F5F9");
            SetBrushColor("NeutralText", "#64748B");
            BgStopOne.Color = Color.FromRgb(244, 246, 251);
            BgStopTwo.Color = Color.FromRgb(238, 242, 255);
            BgStopThree.Color = Color.FromRgb(245, 243, 255);
        }

        ApplyWelcomeTheme(useDarkTheme);
        ApplyHomeTheme(useDarkTheme);
        UpdateThemeToggleContent();

        var activeButton = _activePage == SetupPage ? SetupNavButton :
            _activePage == UpdatesPage ? UpdatesNavButton :
            _activePage == UninstallerPage ? UninstallerNavButton :
            _activePage == CleanupPage ? CleanupNavButton :
            _activePage == ActivityPage ? ActivityNavButton :
            _activePage == StoragePage ? StorageNavButton :
            HomeNavButton;
        SetActiveNavigation(activeButton);
    }

    private async Task SaveThemePreferenceAsync()
    {
        var uiSettings = _settings.Ui ?? new UiSettings();
        _settings = _settings with
        {
            Ui = uiSettings with { Theme = _isDarkTheme ? "Dark" : "Light" }
        };

        try
        {
            await _settings.SaveAsync(_paths);
            Log($"Theme preference saved: {_settings.Ui.Theme}.");
        }
        catch (Exception ex)
        {
            Log($"Could not save theme preference: {ex.Message}");
        }
    }

    private static bool IsDarkTheme(string? theme)
    {
        return !string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateThemeToggleContent()
    {
        var lightIcon = "\u2600";
        var darkIcon = "\u263E";

        ThemeToggleButton.Content = _isSidebarCollapsed
            ? (_isDarkTheme ? lightIcon : darkIcon)
            : (_isDarkTheme ? $"{lightIcon} Light" : $"{darkIcon} Dark");
        ThemeToggleButton.ToolTip = _isDarkTheme ? "Switch to light theme" : "Switch to dark theme";
    }

    private void ApplyHomeTheme(bool useDarkTheme)
    {
        if (useDarkTheme)
        {
            HomeDeckBorder.BorderBrush = BrushFromHex("#3B2862");
            HomeDeckStopOne.Color = ColorFromHex("#0A0D18");
            HomeDeckStopTwo.Color = ColorFromHex("#121026");
            HomeDeckStopThree.Color = ColorFromHex("#091626");
            HomeBandOne.Background = BrushFromHex("#B026FF");
            HomeBandOne.Opacity = 0.22;
            HomeBandTwo.Background = BrushFromHex("#00F0FF");
            HomeBandTwo.Opacity = 0.18;
            HomeDeckEyebrowText.Foreground = BrushFromHex("#00F0FF");
            HomeDeckTitleText.Foreground = BrushFromHex("#FFFFFF");
            HomeDeckSubtitleText.Foreground = BrushFromHex("#8A99B5");
            HomeFeatureStage.Background = BrushFromHex("#D9101424");
            HomeFeatureStage.BorderBrush = BrushFromHex("#362C58");
            HomeFeatureBadge.Background = BrushFromHex("#26143C");
            HomeFeatureBadge.BorderBrush = BrushFromHex("#7C22BA");
            HomeFeatureBadgeText.Foreground = BrushFromHex("#E879F9");
            HomeFeatureTitleText.Foreground = BrushFromHex("#FFFFFF");
            HomeFeatureSubtitleText.Foreground = BrushFromHex("#8A99B5");
            HomeFeatureNumberPanel.Background = BrushFromHex("#0C1A2E");
            HomeFeatureNumberPanel.BorderBrush = BrushFromHex("#0284C7");
            HomeFeatureBrandText.Foreground = BrushFromHex("#00F0FF");
            HomeFeatureNumberText.Foreground = BrushFromHex("#FFFFFF");
            HomeFeatureNextLabelText.Foreground = BrushFromHex("#8A99B5");
            HomeFeaturePositionText.Foreground = BrushFromHex("#8A99B5");
            UpdateHomeFeatureTiles();
            return;
        }

        HomeDeckBorder.BorderBrush = BrushFromHex("#BFD8E1");
        HomeDeckStopOne.Color = ColorFromHex("#FFFFFF");
        HomeDeckStopTwo.Color = ColorFromHex("#F5FAFC");
        HomeDeckStopThree.Color = ColorFromHex("#EFF8F3");
        HomeBandOne.Background = BrushFromHex("#D8EEE7");
        HomeBandOne.Opacity = 0.5;
        HomeBandTwo.Background = BrushFromHex("#DCEBFF");
        HomeBandTwo.Opacity = 0.48;
        HomeDeckEyebrowText.Foreground = BrushFromHex("#0B7E68");
        HomeDeckTitleText.Foreground = BrushFromHex("#172033");
        HomeDeckSubtitleText.Foreground = BrushFromHex("#607086");
        HomeFeatureStage.Background = BrushFromHex("#F7FBFC");
        HomeFeatureStage.BorderBrush = BrushFromHex("#B9D9E2");
        HomeFeatureBadge.Background = BrushFromHex("#E3F5F1");
        HomeFeatureBadge.BorderBrush = BrushFromHex("#A8D8CF");
        HomeFeatureBadgeText.Foreground = BrushFromHex("#0B6B43");
        HomeFeatureTitleText.Foreground = BrushFromHex("#172033");
        HomeFeatureSubtitleText.Foreground = BrushFromHex("#3F5268");
        HomeFeatureNumberPanel.Background = BrushFromHex("#E9F6F7");
        HomeFeatureNumberPanel.BorderBrush = BrushFromHex("#A8CCD2");
        HomeFeatureBrandText.Foreground = BrushFromHex("#0B6B43");
        HomeFeatureNumberText.Foreground = BrushFromHex("#172033");
        HomeFeatureNextLabelText.Foreground = BrushFromHex("#607086");
        HomeFeaturePositionText.Foreground = BrushFromHex("#607086");
        UpdateHomeFeatureTiles();
    }

    private void ApplyWelcomeTheme(bool useDarkTheme)
    {
        if (useDarkTheme)
        {
            WelcomeBgStopOne.Color = ColorFromHex("#07080D");
            WelcomeBgStopTwo.Color = ColorFromHex("#0D101B");
            WelcomeBgStopThree.Color = ColorFromHex("#130E24");
            WelcomeCard.BorderBrush = BrushFromHex("#B026FF");
            WelcomeCardStopOne.Color = ColorFromHex("#E00E1220");
            WelcomeCardStopTwo.Color = ColorFromHex("#D0140F2A");
            WelcomeCardStopThree.Color = ColorFromHex("#C00A1422");
            WelcomeTitleText.Foreground = BrushFromHex("#FFFFFF");
            WelcomeSubtitleText.Foreground = BrushFromHex("#8A99B5");
            WelcomeProgressTrack.Background = BrushFromHex("#161B2C");
            return;
        }

        WelcomeBgStopOne.Color = ColorFromHex("#EEF7FF");
        WelcomeBgStopTwo.Color = ColorFromHex("#F4FAF2");
        WelcomeBgStopThree.Color = ColorFromHex("#F6F1FF");
        WelcomeCard.BorderBrush = BrushFromHex("#C8D9EC");
        WelcomeCardStopOne.Color = ColorFromHex("#BFFFFFFF");
        WelcomeCardStopTwo.Color = ColorFromHex("#AEF9FCFF");
        WelcomeCardStopThree.Color = ColorFromHex("#A5F5FBF7");
        WelcomeTitleText.Foreground = BrushFromHex("#172033");
        WelcomeSubtitleText.Foreground = BrushFromHex("#607086");
        WelcomeProgressTrack.Background = BrushFromHex("#EDF2F8");
    }

    private static void SetBrushColor(string key, string hexColor)
    {
        var color = ColorFromHex(hexColor);

        if (Application.Current.Resources[key] is SolidColorBrush brush)
        {
            if (brush.IsFrozen)
            {
                Application.Current.Resources[key] = new SolidColorBrush(color);
                return;
            }

            brush.Color = color;
        }
    }

    private static Color ColorFromHex(string hexColor)
    {
        return (Color)ColorConverter.ConvertFromString(hexColor);
    }

    private static SolidColorBrush BrushFromHex(string hexColor)
    {
        return new SolidColorBrush(ColorFromHex(hexColor));
    }

    private void SetSidebarCollapsed(bool collapsed, bool animate)
    {
        _isSidebarCollapsed = collapsed;
        var targetWidth = collapsed ? 88 : 268;
        var currentWidth = SidebarColumn.ActualWidth > 0 ? SidebarColumn.ActualWidth : SidebarColumn.Width.Value;

        if (animate)
        {
            SidebarColumn.BeginAnimation(
                ColumnDefinition.WidthProperty,
                new GridLengthAnimation
                {
                    From = new GridLength(currentWidth),
                    To = new GridLength(targetWidth),
                    Duration = TimeSpan.FromMilliseconds(260)
                });
        }
        else
        {
            SidebarColumn.Width = new GridLength(targetWidth);
        }

        SidebarPanel.Padding = collapsed ? new Thickness(12, 16, 12, 16) : new Thickness(16);
        UpdateThemeToggleContent();
        ThemeToggleButton.Padding = collapsed ? new Thickness(0) : new Thickness(14, 7, 14, 7);
        ThemeToggleButton.Margin = collapsed ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 8, 8);
        ThemeToggleButton.MinHeight = collapsed ? 42 : 36;
        ThemeToggleButton.MinWidth = collapsed ? 64 : 92;
        SidebarToggleButton.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarToggleButton.Content = "Hide";

        foreach (var button in new[] { HomeNavButton, SetupNavButton, UpdatesNavButton, UninstallerNavButton, CleanupNavButton, ActivityNavButton, StorageNavButton })
        {
            button.Padding = collapsed ? new Thickness(0) : new Thickness(14, 7, 14, 7);
            button.Margin = collapsed ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 8, 8);
            button.MinHeight = collapsed ? 42 : 36;
            button.HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        }

        foreach (var element in new FrameworkElement[]
                 {
                     SidebarBrandBlock,
                     NavHomeText,
                     NavSetupText,
                     NavUpdatesText,
                     NavUninstallerText,
                     NavCleanupText,
                     NavActivityText,
                     NavStorageText,
                     SidebarStatusPanel
                 })
        {
            element.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private async void InstallSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _packages
            .Where(package => package.IsSelected && package.State is PackageInstallState.Unknown or PackageInstallState.NotInstalled or PackageInstallState.Failed)
            .ToList();

        if (selected.Count == 0)
        {
            SetBusy(false, "No selected apps are ready to install.");
            Log("No selected apps are ready to install.");
            return;
        }

        await InstallOrUpgradeAsync(selected);
    }

    private async void InstallSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PackageViewModel package })
        {
            await InstallOrUpgradeAsync([package]);
        }
    }

    private async void CheckSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PackageViewModel package })
        {
            await CheckPackagesAsync([package], "Status checked.");
        }
    }

    private async void CheckSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _packages.Where(package => package.IsSelected).ToList();
        if (selected.Count == 0)
        {
            SetBusy(false, "No apps selected to check.");
            Log("No apps selected to check.");
            return;
        }

        await CheckPackagesAsync(selected, "Selected apps checked.");
    }

    private async void CheckAll_Click(object sender, RoutedEventArgs e)
    {
        await CheckPackagesAsync(_packages.ToList(), "All apps checked.");
    }

    private async void UpdateSelected_Click(object sender, RoutedEventArgs e)
    {
        var selectedUpdates = _packages
            .Where(package => package.IsSelected && package.State == PackageInstallState.UpdateAvailable)
            .ToList();

        if (selectedUpdates.Count == 0)
        {
            SetBusy(false, "No selected updates found.");
            Log("No selected apps have a known update. Use Check Selected or Check All first.");
            return;
        }

        await InstallOrUpgradeAsync(selectedUpdates);
    }

    private async Task InstallOrUpgradeAsync(IReadOnlyCollection<PackageViewModel> packages)
    {
        if (_isBusy)
        {
            return;
        }

        if (!await EnsureWingetAvailableAsync())
        {
            SetBusy(false, "Installer engine is not ready.");
            return;
        }

        var cancellationToken = BeginOperation("Installer is running.");
        var finishedStatus = "Install run completed.";

        try
        {
            foreach (var package in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(cancellationToken);
                await InstallOrUpgradePackageAsync(package, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            finishedStatus = "Operation stopped.";
            Log("Operation stopped by user.");
        }
        finally
        {
            UpdateSummary();
            EndOperation(finishedStatus);
        }
    }

    private async Task InstallOrUpgradePackageAsync(PackageViewModel package, CancellationToken cancellationToken)
    {
        var shouldUpgrade = package.State == PackageInstallState.UpdateAvailable;
        var actionLabel = shouldUpgrade ? "Upgrading" : "Installing";

        package.MarkBusy(actionLabel);
        SummaryText.Text = $"{actionLabel} {package.Name}.";
        CurrentOperationProgressText.Text = $"{package.Name}: Starting...";
        Log($"{actionLabel} {package.Name}");

        using var progressCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var startedAt = DateTime.Now;
        _networkSpeedSampler.Reset();
        var progressTask = TrackProgressAsync(package, startedAt, progressCancellation.Token);

        ProcessRunResult result;
        try
        {
            result = shouldUpgrade
                ? await _winget.UpgradeAsync(package.Package, output => HandleOperationOutput(package, startedAt, output), cancellationToken)
                : await _winget.InstallAsync(package.Package, output => HandleOperationOutput(package, startedAt, output), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            package.SetStatus(PackageInstallState.Failed, "Stopped");
            Log($"{package.Name} was stopped. Check it again before retrying.");
            throw;
        }
        finally
        {
            progressCancellation.Cancel();
            await IgnoreCancellationAsync(progressTask);
        }

        if (result.ExitCode == 0)
        {
            await CheckPackageAsync(package, cancellationToken);
            Log($"Completed: {package.Name}");
            return;
        }

        Log($"{package.Name} did not report success. Verifying installed state before marking it failed.");
        await CheckPackageAsync(package, cancellationToken);
        if (package.State is PackageInstallState.Installed or PackageInstallState.UpdateAvailable)
        {
            Log($"{package.Name} was found after verification.");
            return;
        }

        package.SetStatus(PackageInstallState.Failed, BuildInstallFailureStatus(result));
        Log(BuildInstallFailureMessage(package.Name, result));
    }

    private async Task CheckPackagesAsync(IReadOnlyCollection<PackageViewModel> packages, string finishedStatus)
    {
        if (_isBusy)
        {
            return;
        }

        if (!await EnsureWingetAvailableAsync())
        {
            SetBusy(false, "Installer engine is not ready.");
            return;
        }

        var cancellationToken = BeginOperation($"Checking {packages.Count} app{(packages.Count == 1 ? string.Empty : "s")}.");
        var selectedPackages = packages.ToHashSet();
        var finishedStatusText = finishedStatus;

        try
        {
            foreach (var package in _packages)
            {
                if (!selectedPackages.Contains(package))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(cancellationToken);
                await CheckPackageAsync(package, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            finishedStatusText = "Operation stopped.";
            Log("Check operation stopped by user.");
        }
        finally
        {
            UpdateSummary();
            EndOperation(finishedStatusText);
        }
    }

    private async Task CheckPackageAsync(PackageViewModel package, CancellationToken cancellationToken = default)
    {
        package.MarkBusy("Checking");

        try
        {
            var status = await _winget.GetStatusAsync(package.Package, cancellationToken);
            package.SetStatus(status);
        }
        catch (OperationCanceledException)
        {
            package.SetStatus(PackageInstallState.Unknown, "Stopped");
            throw;
        }
        catch (Exception ex)
        {
            package.SetStatus(PackageInstallState.Failed, "Check failed");
            Log($"{package.Name} check failed: {ex.Message}");
        }
    }

    private async void RefreshWingetSources_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (!await EnsureWingetAvailableAsync())
        {
            SetBusy(false, "Installer engine is not ready.");
            return;
        }

        SetBusy(true, "Updating winget sources.");
        var result = await _winget.RefreshSourcesAsync(Log);
        _hasScannedUpdatesThisSession = false;
        UpdateScanStatusText.Text = result.ExitCode == 0
            ? "Sources updated. Scan updates to refresh the ready list."
            : "Source update failed. Existing update results were kept.";
        SetBusy(false, result.ExitCode == 0 ? "Winget sources updated." : $"Winget source update failed ({result.ExitCode}).");
    }

    private async void ScanUpdates_Click(object sender, RoutedEventArgs e)
    {
        await ScanAvailableUpdatesAsync(isAutomatic: false);
    }

    private async Task AutoScanUpdatesIfNeededAsync()
    {
        if (_hasScannedUpdatesThisSession || _isAutoUpdateScanQueued || _isBusy)
        {
            return;
        }

        _isAutoUpdateScanQueued = true;
        try
        {
            await ScanAvailableUpdatesAsync(isAutomatic: true);
        }
        finally
        {
            _isAutoUpdateScanQueued = false;
        }
    }

    private async Task ScanAvailableUpdatesAsync(bool isAutomatic)
    {
        if (_isBusy)
        {
            return;
        }

        if (!await EnsureWingetAvailableAsync())
        {
            SetBusy(false, "Installer engine is not ready.");
            return;
        }

        var cancellationToken = BeginOperation(isAutomatic ? "Auto-scanning app updates." : "Scanning app updates.");
        var finishedStatus = "Update scan completed.";
        UpdateScanStatusText.Text = "Scanning installed apps for available updates...";

        try
        {
            var updates = await _winget.GetAvailableUpdatesAsync(Log, cancellationToken);
            var matched = ApplyAvailableUpdates(updates);
            _hasScannedUpdatesThisSession = true;
            finishedStatus = matched == 0
                ? "No catalog updates found."
                : $"{matched} update-ready app{(matched == 1 ? string.Empty : "s")} found.";
            UpdateScanStatusText.Text = matched == 0
                ? "No catalog app updates were found right now."
                : $"{matched} update-ready app{(matched == 1 ? string.Empty : "s")} found and selected.";
            Log($"Update scan found {updates.Count} winget update(s), matched {matched} catalog app(s).");
        }
        catch (OperationCanceledException)
        {
            finishedStatus = "Update scan stopped.";
            UpdateScanStatusText.Text = "Update scan stopped.";
            Log("Update scan stopped by user.");
        }
        finally
        {
            UpdateSummary();
            EndOperation(finishedStatus);
        }
    }

    private int ApplyAvailableUpdates(IReadOnlyList<AvailablePackageUpdate> updates)
    {
        var matchedPackages = new HashSet<PackageViewModel>();

        foreach (var update in updates)
        {
            var package = _packages.FirstOrDefault(candidate => MatchesAvailableUpdate(candidate.Package, update));
            if (package is null)
            {
                continue;
            }

            package.SetStatus(
                PackageInstallState.UpdateAvailable,
                installedVersion: update.InstalledVersion,
                availableVersion: update.AvailableVersion);
            package.IsSelected = true;
            matchedPackages.Add(package);
        }

        foreach (var package in _packages.Where(package => package.State == PackageInstallState.UpdateAvailable && !matchedPackages.Contains(package)))
        {
            package.SetStatus(PackageInstallState.Installed, "Up to date");
        }

        RefreshUpdatePackages();
        return matchedPackages.Count;
    }

    private static bool MatchesAvailableUpdate(PackageDefinition package, AvailablePackageUpdate update)
    {
        if (package.Id.Equals(update.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var candidates = new[] { package.Name }
            .Concat(package.DetectionNames)
            .Concat(package.Id.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeUpdateMatchText)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var updateName = NormalizeUpdateMatchText(update.Name);
        var updateId = NormalizeUpdateMatchText(update.Id);
        return candidates.Any(candidate => candidate == updateName || candidate == updateId);
    }

    private static string NormalizeUpdateMatchText(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private async void UpgradeAll_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var updates = _packages
            .Where(package => package.State == PackageInstallState.UpdateAvailable)
            .ToList();

        if (updates.Count == 0)
        {
            SetBusy(false, "No checked updates found.");
            Log("No known updates found. Use Check All first if the list is not checked.");
            return;
        }

        await InstallOrUpgradeAsync(updates);
    }

    private async void WindowsUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true, "Checking Windows Update.");
        await _windowsUpdate.TriggerInteractiveScanAsync(Log);
        SetBusy(false, "Windows Update settings opened.");
    }

    private async void CheckAppUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true, "Checking 180Hz Setup Hub update source.");
        var message = await _appUpdate.CheckForUpdatesAsync();
        Log(message);
        SetBusy(false, message);
    }

    private async void RefreshInstalledApps_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var cancellationToken = BeginOperation("Refreshing installed apps.");
        try
        {
            await LoadInstalledAppsAsync();
            EndOperation("Installed apps refreshed.");
        }
        catch (OperationCanceledException)
        {
            EndOperation("Refresh stopped.");
        }
    }

    private async void UninstallSelectedApps_Click(object sender, RoutedEventArgs e)
    {
        var selected = _installedApps.Where(app => app.IsSelected && app.CanUninstall).ToList();
        if (selected.Count == 0)
        {
            SetBusy(false, "No uninstallable apps selected.");
            Log("No uninstallable apps selected.");
            return;
        }

        await UninstallAppsAsync(selected);
    }

    private async void UninstallSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InstalledAppViewModel app })
        {
            await UninstallAppsAsync([app]);
        }
    }

    private async Task UninstallAppsAsync(IReadOnlyCollection<InstalledAppViewModel> apps)
    {
        if (_isBusy)
        {
            return;
        }

        var cancellationToken = BeginOperation($"Uninstalling {apps.Count} app{(apps.Count == 1 ? string.Empty : "s")}.");
        var finishedStatus = "Uninstall run completed.";

        try
        {
            foreach (var app in apps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(cancellationToken);
                await UninstallAppAsync(app, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            finishedStatus = "Uninstall stopped.";
            Log("Uninstall operation stopped by user.");
        }
        finally
        {
            UpdateUninstallerSummary();
            EndOperation(finishedStatus);
        }
    }

    private async Task UninstallAppAsync(InstalledAppViewModel app, CancellationToken cancellationToken)
    {
        app.MarkBusy("Uninstalling");
        app.UpdateProgress(5, "Preparing uninstall...");
        CurrentOperationProgressText.Text = $"{app.Name}: Preparing uninstall...";
        Log($"Uninstalling {app.Name}");

        app.UpdateProgress(20, "Running uninstaller...");
        var result = await _uninstallService.UninstallAsync(
            app.App,
            output => HandleUninstallOutput(app, output),
            cancellationToken);

        app.UpdateProgress(70, "Verifying uninstall result...");
        if (result.ExitCode == 0)
        {
            app.SetStatus("Uninstalled");
            Log($"Uninstalled: {app.Name}");
        }
        else
        {
            app.SetStatus("Uninstall failed");
            Log($"{app.Name} uninstall failed. Exit code: {result.ExitCode}. {BuildShortResultMessage(result)}");
        }

        app.MarkBusy("Scanning leftovers");
        app.UpdateProgress(82, "Scanning safe leftovers...");
        var leftovers = await _uninstallService.ScanLeftoversAsync(app.App, cancellationToken);
        app.SetLeftovers(leftovers);
        app.UpdateProgress(100, "Uninstall flow complete.");
        app.SetStatus(result.ExitCode == 0
            ? $"Uninstalled; {leftovers.Count} leftover(s)"
            : $"Uninstall failed; {leftovers.Count} leftover(s)");
    }

    private void HandleUninstallOutput(InstalledAppViewModel app, string output)
    {
        if (ProgressOutputParser.TryReadPercent(output, out var percent))
        {
            Dispatcher.Invoke(() =>
            {
                var normalized = Math.Clamp(20 + percent * 0.5, 20, 70);
                app.UpdateProgress(normalized, $"{percent:0}% uninstalling...");
                CurrentOperationProgressText.Text = $"{app.Name}: {app.ProgressText}";
            });
            return;
        }

        Log(output);
    }

    private async void ScanSelectedLeftovers_Click(object sender, RoutedEventArgs e)
    {
        var selected = _installedApps.Where(app => app.IsSelected).ToList();
        if (selected.Count == 0)
        {
            SetBusy(false, "No installed apps selected to scan.");
            Log("No installed apps selected to scan.");
            return;
        }

        await ScanLeftoversAsync(selected);
    }

    private async void ScanLeftoversSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InstalledAppViewModel app })
        {
            await ScanLeftoversAsync([app]);
        }
    }

    private async Task ScanLeftoversAsync(IReadOnlyCollection<InstalledAppViewModel> apps)
    {
        if (_isBusy)
        {
            return;
        }

        var cancellationToken = BeginOperation($"Scanning leftovers for {apps.Count} app{(apps.Count == 1 ? string.Empty : "s")}.");
        var finishedStatus = "Leftover scan completed.";

        try
        {
            foreach (var app in apps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(cancellationToken);
                app.MarkBusy("Scanning leftovers");
                var leftovers = await _uninstallService.ScanLeftoversAsync(app.App, cancellationToken);
                app.SetLeftovers(leftovers);
                app.SetStatus($"{leftovers.Count} leftover item(s) found");
                Log($"{app.Name}: {leftovers.Count} leftover item(s) found.");
            }
        }
        catch (OperationCanceledException)
        {
            finishedStatus = "Leftover scan stopped.";
            Log("Leftover scan stopped by user.");
        }
        finally
        {
            UpdateUninstallerSummary();
            EndOperation(finishedStatus);
        }
    }

    private async void DeleteSelectedLeftovers_Click(object sender, RoutedEventArgs e)
    {
        var selected = _installedApps.Where(app => app.IsSelected && app.CanDeleteLeftovers).ToList();
        if (selected.Count == 0)
        {
            SetBusy(false, "No safe leftover cleanup is ready.");
            Log("No selected app has delete-ready leftovers. Uninstall or failed-uninstall an app first.");
            return;
        }

        await DeleteLeftoversAsync(selected);
    }

    private async void DeleteLeftoversSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InstalledAppViewModel app })
        {
            await DeleteLeftoversAsync([app]);
        }
    }

    private async Task DeleteLeftoversAsync(IReadOnlyCollection<InstalledAppViewModel> apps)
    {
        if (_isBusy)
        {
            return;
        }

        var cancellationToken = BeginOperation($"Deleting leftovers for {apps.Count} app{(apps.Count == 1 ? string.Empty : "s")}.");
        var finishedStatus = "Leftover cleanup completed.";

        try
        {
            foreach (var app in apps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(cancellationToken);
                app.MarkBusy("Deleting leftovers");
                var result = await _uninstallService.DeleteLeftoversAsync(app.App, app.Leftovers, Log, cancellationToken);
                app.SetLeftovers([]);
                app.SetStatus($"Deleted {result.DeletedCount} leftover(s), skipped {result.SkippedCount}");
                Log($"{app.Name}: deleted {result.DeletedCount} leftover(s), freed {SizeFormatter.Format(result.DeletedBytes)}, skipped {result.SkippedCount}.");
            }
        }
        catch (OperationCanceledException)
        {
            finishedStatus = "Leftover cleanup stopped.";
            Log("Leftover cleanup stopped by user.");
        }
        finally
        {
            UpdateUninstallerSummary();
            EndOperation(finishedStatus);
        }
    }

    private async void ScanCleanup_Click(object sender, RoutedEventArgs e)
    {
        var selected = _cleanupTargets.Where(target => target.IsSelected).ToList();
        if (selected.Count == 0)
        {
            SetBusy(false, "No cleanup targets selected.");
            Log("No cleanup targets selected.");
            return;
        }

        await ScanCleanupTargetsAsync(selected);
    }

    private async void ScanCleanupSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CleanupTargetViewModel target })
        {
            await ScanCleanupTargetsAsync([target]);
        }
    }

    private async Task ScanCleanupTargetsAsync(IReadOnlyCollection<CleanupTargetViewModel> targets)
    {
        if (_isBusy)
        {
            return;
        }

        var cancellationToken = BeginOperation($"Scanning {targets.Count} cleanup target{(targets.Count == 1 ? string.Empty : "s")}.");
        var finishedStatus = "Cleanup scan completed.";

        try
        {
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(cancellationToken);
                target.MarkBusy("Scanning");
                var result = await _cleanupService.ScanAsync(target.Target, cancellationToken);
                target.SetScanResult(result);
                Log($"{target.Name}: {result.FileCount} file(s), {SizeFormatter.Format(result.SizeBytes)}, skipped {result.SkippedCount}.");
            }
        }
        catch (OperationCanceledException)
        {
            finishedStatus = "Cleanup scan stopped.";
            Log("Cleanup scan stopped by user.");
        }
        finally
        {
            UpdateCleanupSummary();
            EndOperation(finishedStatus);
        }
    }

    private async void CleanSelectedJunk_Click(object sender, RoutedEventArgs e)
    {
        var selected = _cleanupTargets.Where(target => target.IsSelected).ToList();
        if (selected.Count == 0)
        {
            SetBusy(false, "No cleanup targets selected.");
            Log("No cleanup targets selected.");
            return;
        }

        await CleanTargetsAsync(selected);
    }

    private async void CleanJunkSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CleanupTargetViewModel target })
        {
            await CleanTargetsAsync([target]);
        }
    }

    private async Task CleanTargetsAsync(IReadOnlyCollection<CleanupTargetViewModel> targets)
    {
        if (_isBusy)
        {
            return;
        }

        var cancellationToken = BeginOperation($"Cleaning {targets.Count} target{(targets.Count == 1 ? string.Empty : "s")}.");
        var finishedStatus = "Junk cleanup completed.";

        try
        {
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(cancellationToken);
                target.MarkBusy("Cleaning");
                var result = await _cleanupService.CleanAsync(target.Target, Log, cancellationToken);
                target.SetRunResult(result);
            }
        }
        catch (OperationCanceledException)
        {
            finishedStatus = "Junk cleanup stopped.";
            Log("Junk cleanup stopped by user.");
        }
        finally
        {
            UpdateCleanupSummary();
            EndOperation(finishedStatus);
        }
    }

    private void SelectCleanupDefaults_Click(object sender, RoutedEventArgs e)
    {
        foreach (var target in _cleanupTargets)
        {
            target.IsSelected = target.Target.IsSelectedByDefault;
        }

        UpdateCleanupSummary();
        SetBusy(false, "Safe cleanup defaults selected.");
    }

    private void SelectEssentials_Click(object sender, RoutedEventArgs e)
    {
        ApplyProfile(SetupProfileCatalog.Default);
        SetBusy(false, "Essentials selected.");
        UpdateSummary();
    }

    private void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        var profileName = ProfileSelector.SelectedItem as string ?? "Most Useful";
        var profile = SetupProfileCatalog.FindByName(profileName);
        ApplyProfile(profile);
        SetBusy(false, $"{profileName} selected.");
        UpdateSummary();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var package in _packages)
        {
            package.IsSelected = true;
        }

        SetBusy(false, "All apps selected.");
        UpdateSummary();
    }

    private void SelectUpdates_Click(object sender, RoutedEventArgs e)
    {
        foreach (var package in _packages)
        {
            package.IsSelected = package.State == PackageInstallState.UpdateAvailable;
        }

        SetBusy(false, "Known updates selected.");
        UpdateSummary();
    }

    private void ApplyProfile(SetupProfile profile)
    {
        foreach (var package in _packages)
        {
            package.IsSelected = profile.Matches(package.Package);
        }
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var package in _packages)
        {
            package.IsSelected = false;
        }

        SetBusy(false, "Selection cleared.");
        UpdateSummary();
    }

    private void RevealShell()
    {
        ShellRoot.BeginAnimation(OpacityProperty, BuildDoubleAnimation(1, 480));
        ShellTransform.BeginAnimation(TranslateTransform.YProperty, BuildDoubleAnimation(0, 520));

        var fadeOut = BuildDoubleAnimation(0, 420);
        fadeOut.Completed += (_, _) =>
        {
            _welcomeLoop?.Stop(WelcomeOverlay);
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            _sidebarPinnedOpen = false;
            SetSidebarCollapsed(collapsed: true, animate: true);
            UpdateHomeFeatureTimer();
        };
        WelcomeOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ShowPage(FrameworkElement page, Button navigationButton, string title, string subtitle)
    {
        PageTitleText.Text = title;
        PageSubtitleText.Text = subtitle;
        SetActiveNavigation(navigationButton);

        if (_activePage == page && page.Visibility == Visibility.Visible)
        {
            UpdateHomeFeatureTimer();
            return;
        }

        foreach (var candidate in new[] { HomePage, SetupPage, UpdatesPage, UninstallerPage, CleanupPage, ActivityPage, StoragePage })
        {
            candidate.Visibility = Visibility.Collapsed;
        }

        page.Visibility = Visibility.Visible;
        page.Opacity = 0;

        if (page.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            page.RenderTransform = transform;
        }

        transform.Y = 14;
        page.BeginAnimation(OpacityProperty, BuildDoubleAnimation(1, 260));
        transform.BeginAnimation(TranslateTransform.YProperty, BuildDoubleAnimation(0, 320));
        _activePage = page;
        _sidebarPinnedOpen = false;
        SetSidebarCollapsed(collapsed: true, animate: true);
        UpdateHomeFeatureTimer();
    }

    private void SetActiveNavigation(Button activeButton)
    {
        foreach (var button in new[] { HomeNavButton, SetupNavButton, UpdatesNavButton, UninstallerNavButton, CleanupNavButton, ActivityNavButton, StorageNavButton })
        {
            button.Background = BrushFromHex(_isDarkTheme ? "#0D101A" : "#FFFFFF");
            button.BorderBrush = BrushFromHex(_isDarkTheme ? "#232A42" : "#D7E3EC");
            button.Foreground = BrushFromHex(_isDarkTheme ? "#8A99B5" : "#172033");
            button.BorderThickness = new Thickness(1);
            button.FontWeight = FontWeights.Normal;
        }

        activeButton.Background = CreateGradientBrush(
            _isDarkTheme ? "#281245" : "#EAF2FF",
            _isDarkTheme ? "#0B263C" : "#E5F7F1");
        activeButton.BorderBrush = BrushFromHex(_isDarkTheme ? "#00F0FF" : "#7C3AED");
        activeButton.Foreground = BrushFromHex(_isDarkTheme ? "#00F0FF" : "#6D28D9");
        activeButton.BorderThickness = new Thickness(1.4);
        activeButton.FontWeight = FontWeights.SemiBold;
    }

    private static LinearGradientBrush CreateGradientBrush(string startHex, string endHex)
    {
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(ColorFromHex(startHex), 0),
                new GradientStop(ColorFromHex(endHex), 1)
            }
        };
    }

    private static DoubleAnimation BuildDoubleAnimation(double to, int milliseconds)
    {
        return new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(_paths.Root) { UseShellExecute = true });
    }

    private void PauseOperation_Click(object sender, RoutedEventArgs e)
    {
        if (!_isBusy || _operationCancellation is null || _operationCancellation.IsCancellationRequested)
        {
            return;
        }

        _pauseRequested = true;
        StatusText.Text = "Pause requested.";
        SummaryText.Text = "Current app will finish, then the queue will pause.";
        Log("Pause requested. The current command will finish before the queue pauses.");
        UpdateOperationControlState();
    }

    private void ResumeOperation_Click(object sender, RoutedEventArgs e)
    {
        if (!_isBusy || _operationCancellation is null)
        {
            return;
        }

        _pauseRequested = false;
        StatusText.Text = "Resuming operation.";
        Log("Operation resumed.");
        UpdateOperationControlState();
    }

    private void StopOperation_Click(object sender, RoutedEventArgs e)
    {
        if (!_isBusy || _operationCancellation is null || _operationCancellation.IsCancellationRequested)
        {
            return;
        }

        StatusText.Text = "Stopping operation.";
        SummaryText.Text = "Stopping current command and cancelling the remaining queue.";
        Log("Stop requested. Cancelling the current command and remaining queue.");
        _operationCancellation.Cancel();
        UpdateOperationControlState();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _packagesView?.Refresh();
        UpdateSummary();
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _packagesView?.Refresh();
        UpdateSummary();
    }

    private void UninstallerSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _installedAppsView?.Refresh();
        UpdateUninstallerSummary();
    }

    private bool FilterPackage(object item)
    {
        if (item is not PackageViewModel package)
        {
            return false;
        }

        var category = CategoryFilter.SelectedItem as string ?? AllCategories;
        if (category != AllCategories && !string.Equals(package.Category, category, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return package.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || package.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || package.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterInstalledApp(object item)
    {
        if (item is not InstalledAppViewModel app)
        {
            return false;
        }

        var query = UninstallerSearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return app.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || app.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)
            || app.KindText.Contains(query, StringComparison.OrdinalIgnoreCase)
            || app.InstallLocation.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private CancellationToken BeginOperation(string status)
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        _pauseRequested = false;
        SetBusy(true, status);
        return _operationCancellation.Token;
    }

    private void EndOperation(string status)
    {
        var operation = _operationCancellation;
        _operationCancellation = null;
        _pauseRequested = false;
        operation?.Dispose();
        SetBusy(false, status);
    }

    private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        if (!_pauseRequested)
        {
            return;
        }

        Log("Operation paused.");
        while (_pauseRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatusText.Text = "Paused.";
            SummaryText.Text = "Queue paused.";
            UpdateOperationControlState();
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        StatusText.Text = "Resuming operation.";
    }

    private void SetBusy(bool isBusy, string status)
    {
        _isBusy = isBusy;
        StatusText.Text = status;
        BusyProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        if (!isBusy)
        {
            CurrentOperationProgressText.Text = string.Empty;
        }

        InstallSelectedButton.IsEnabled = !isBusy;
        CheckSelectedButton.IsEnabled = !isBusy;
        CheckAllButton.IsEnabled = !isBusy;
        UpdateSelectedButton.IsEnabled = !isBusy;
        RefreshWingetSourcesButton.IsEnabled = !isBusy;
        UpgradeAllButton.IsEnabled = !isBusy;
        ScanUpdatesButton.IsEnabled = !isBusy;
        WindowsUpdateButton.IsEnabled = !isBusy;
        CheckAppUpdateButton.IsEnabled = !isBusy;
        ApplyProfileButton.IsEnabled = !isBusy;
        ProfileSelector.IsEnabled = !isBusy;
        SelectEssentialsButton.IsEnabled = !isBusy;
        SelectAllButton.IsEnabled = !isBusy;
        SelectUpdatesButton.IsEnabled = !isBusy;
        ClearSelectionButton.IsEnabled = !isBusy;
        RefreshInstalledAppsButton.IsEnabled = !isBusy;
        ScanSelectedLeftoversButton.IsEnabled = !isBusy;
        UninstallSelectedAppsButton.IsEnabled = !isBusy;
        DeleteSelectedLeftoversButton.IsEnabled = !isBusy;
        ScanCleanupButton.IsEnabled = !isBusy;
        CleanSelectedJunkButton.IsEnabled = !isBusy;
        SelectCleanupDefaultsButton.IsEnabled = !isBusy;
        UpdateOperationControlState();
    }

    private void UpdateOperationControlState()
    {
        var canControlOperation = _isBusy
            && _operationCancellation is not null
            && !_operationCancellation.IsCancellationRequested;

        PauseOperationButton.IsEnabled = canControlOperation && !_pauseRequested;
        ResumeOperationButton.IsEnabled = canControlOperation && _pauseRequested;
        StopOperationButton.IsEnabled = canControlOperation;
    }

    private void Log(string message)
    {
        if (IsNoiseOutput(message))
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            _activityLines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
            while (_activityLines.Count > MaxActivityLines)
            {
                _activityLines.Dequeue();
            }

            LogBox.Text = string.Join(Environment.NewLine, _activityLines);
            LogBox.ScrollToEnd();
        });
    }

    private void Package_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PackageViewModel.IsSelected) or nameof(PackageViewModel.State))
        {
            Dispatcher.Invoke(UpdateSummary);
        }
    }

    private void InstalledApp_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstalledAppViewModel.IsSelected)
            or nameof(InstalledAppViewModel.StatusText)
            or nameof(InstalledAppViewModel.Leftovers))
        {
            Dispatcher.Invoke(UpdateUninstallerSummary);
        }
    }

    private void CleanupTarget_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CleanupTargetViewModel.IsSelected)
            or nameof(CleanupTargetViewModel.SizeBytes))
        {
            Dispatcher.Invoke(UpdateCleanupSummary);
        }
    }

    private void UpdateSummary()
    {
        if (InstalledCountText is null)
        {
            return;
        }

        var installed = _packages.Count(package => package.State is PackageInstallState.Installed or PackageInstallState.UpdateAvailable);
        var missing = _packages.Count(package => package.State == PackageInstallState.NotInstalled);
        var updates = _packages.Count(package => package.State == PackageInstallState.UpdateAvailable);
        var uncheckedApps = _packages.Count(package => package.State == PackageInstallState.Unknown);
        var selected = _packages.Count(package => package.IsSelected);
        var visible = _packagesView?.Cast<object>().Count() ?? _packages.Count;

        InstalledCountText.Text = installed.ToString();
        MissingCountText.Text = missing.ToString();
        UpdateCountText.Text = updates.ToString();
        SelectedCountText.Text = selected.ToString();
        UncheckedCountText.Text = uncheckedApps.ToString();
        VisibleCountText.Text = $"{visible} shown";
        SummaryText.Text = $"{installed} installed, {missing} not installed, {updates} updates available, {uncheckedApps} not checked, {selected} selected.";
        RefreshUpdatePackages();
    }

    private void RefreshUpdatePackages()
    {
        if (UpdatesList is null)
        {
            return;
        }

        var updatePackages = _packages
            .Where(package => package.State == PackageInstallState.UpdateAvailable)
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _updatePackages.Clear();
        foreach (var package in updatePackages)
        {
            _updatePackages.Add(package);
        }

        VisibleUpdatesText.Text = updatePackages.Count == 0
            ? "No updates ready"
            : $"{updatePackages.Count} ready";
    }

    private void UpdateUninstallerSummary()
    {
        if (InstalledAppsCountText is null)
        {
            return;
        }

        var total = _installedApps.Count;
        var protectedApps = _installedApps.Count(app => app.IsProtected);
        var selected = _installedApps.Count(app => app.IsSelected);
        var visible = _installedAppsView?.Cast<object>().Count() ?? total;

        InstalledAppsCountText.Text = total.ToString();
        ProtectedAppsCountText.Text = protectedApps.ToString();
        SelectedInstalledAppsText.Text = selected.ToString();
        VisibleInstalledAppsText.Text = $"{visible} shown";
    }

    private void UpdateCleanupSummary()
    {
        if (CleanupSummaryText is null)
        {
            return;
        }

        var selected = _cleanupTargets.Count(target => target.IsSelected);
        var totalSize = _cleanupTargets.Sum(target => target.SizeBytes);
        CleanupSelectedText.Text = $"{selected} selected";
        CleanupSummaryText.Text = $"{selected} cleanup target(s) selected. Scanned junk total: {SizeFormatter.Format(totalSize)}.";
    }

    private static bool IsNoiseOutput(string message)
    {
        var text = message.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (text.Contains('\u00C3', StringComparison.Ordinal))
        {
            return true;
        }

        if (text.All(character => character is '-' or '\\' or '/' or '|' or ' ' or '\b' or '\r'))
        {
            return true;
        }

        if (ProgressOutputParser.TryReadTransferPercent(text, out _))
        {
            return true;
        }

        return ContainsProgressBarGlyph(text) && ProgressOutputParser.TryReadPercent(text, out _);
    }

    private static bool ContainsProgressBarGlyph(string text)
    {
        return text.Any(character => character is '\u2588' or '\u2592' or '\u2593' or '\u2591' or '\u25A0' or '\u25A1');
    }

    private void HandleOperationOutput(PackageViewModel package, DateTime startedAt, string output)
    {
        var isProgressOutput = false;

        if (ProgressOutputParser.TryReadTransfer(output, out var transfer))
        {
            Dispatcher.Invoke(() =>
            {
                var transferText = BuildTransferText(transfer);
                package.UpdateProgress(
                    transfer.Percent,
                    BuildProgressText(package, startedAt, includeEta: true, transferText),
                    transferText);
                CurrentOperationProgressText.Text = $"{package.Name}: {package.ProgressText}";
            });
            isProgressOutput = true;
        }
        else if (ProgressOutputParser.TryReadPercent(output, out var percent))
        {
            Dispatcher.Invoke(() =>
            {
                package.UpdateProgress(percent, BuildProgressText(package, startedAt, includeEta: true));
                CurrentOperationProgressText.Text = $"{package.Name}: {package.ProgressText}";
            });
            isProgressOutput = true;
        }

        if (isProgressOutput)
        {
            return;
        }

        Log(output);
    }

    private static string BuildInstallFailureStatus(ProcessRunResult result)
    {
        if (ContainsResultText(result, "Installer hash does not match"))
        {
            return "Hash mismatch";
        }

        if (ContainsResultText(result, "No installed package found matching input criteria"))
        {
            return "Package not found";
        }

        if (ContainsResultText(result, "requires elevation")
            || ContainsResultText(result, "administrator")
            || ContainsResultText(result, "access is denied")
            || ContainsResultText(result, "0x80070005"))
        {
            return "Admin required";
        }

        if (ContainsResultText(result, "network")
            || ContainsResultText(result, "internet")
            || ContainsResultText(result, "timed out")
            || ContainsResultText(result, "could not be resolved"))
        {
            return "Network issue";
        }

        if (ContainsResultText(result, "No package found matching input criteria"))
        {
            return "Package unavailable";
        }

        return "Retry ready";
    }

    private static string BuildInstallFailureMessage(string packageName, ProcessRunResult result)
    {
        if (ContainsResultText(result, "Installer hash does not match"))
        {
            return $"{packageName} did not finish because winget blocked the installer hash. Sources were refreshed and retried once. Exit code: {result.ExitCode}.";
        }

        if (ContainsResultText(result, "No installed package found matching input criteria"))
        {
            return $"{packageName} upgrade could not find the installed package after exact fallback checks. Check the app status and retry. Exit code: {result.ExitCode}.";
        }

        if (ContainsResultText(result, "requires elevation")
            || ContainsResultText(result, "administrator")
            || ContainsResultText(result, "access is denied")
            || ContainsResultText(result, "0x80070005"))
        {
            return $"{packageName} needs Windows administrator permission for this installer. Start 180Hz Setup Hub as administrator and retry. Exit code: {result.ExitCode}.";
        }

        if (ContainsResultText(result, "network")
            || ContainsResultText(result, "internet")
            || ContainsResultText(result, "timed out")
            || ContainsResultText(result, "could not be resolved"))
        {
            return $"{packageName} could not download from the package source. Check internet/VPN/firewall and retry. Exit code: {result.ExitCode}.";
        }

        return $"{packageName} did not finish after automatic repair and fallback attempts. Exit code: {result.ExitCode}. {BuildShortResultMessage(result)}";
    }

    private static bool ContainsResultText(ProcessRunResult result, string text)
    {
        return result.Output.Contains(text, StringComparison.OrdinalIgnoreCase)
            || result.Error.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildShortResultMessage(ProcessRunResult result)
    {
        var message = string.Join(
                " ",
                new[] { result.Error, result.Output }
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Select(text => text.Trim()))
            .Replace(Environment.NewLine, " ", StringComparison.Ordinal);

        return message.Length <= 180 ? message : $"{message[..180]}...";
    }

    private async Task TrackProgressAsync(PackageViewModel package, DateTime startedAt, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                package.UpdateProgress(null, BuildProgressText(package, startedAt, includeEta: !package.IsProgressIndeterminate));
                CurrentOperationProgressText.Text = $"{package.Name}: {package.ProgressText}";
            });

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Progress tracking is expected to stop when the installer exits.
        }
    }

    private string BuildProgressText(
        PackageViewModel package,
        DateTime startedAt,
        bool includeEta,
        string? transferText = null)
    {
        return InstallProgressFormatter.Build(
            package.ProgressPercent,
            package.IsProgressIndeterminate,
            startedAt,
            _networkSpeedSampler.Read(),
            includeEta,
            transferText ?? package.TransferText);
    }

    private static string BuildTransferText(TransferProgress progress)
    {
        return $"{SizeFormatter.Format(progress.CurrentBytes)} of {SizeFormatter.Format(progress.TotalBytes)}";
    }

    private enum HomeFeatureTarget
    {
        Setup,
        Updates,
        Uninstaller,
        Cleanup,
        Activity,
        Storage
    }

    private sealed record HomeFeature(
        string Badge,
        string Title,
        string Subtitle,
        string ActionText,
        HomeFeatureTarget Target);

    private sealed class GridLengthAnimation : AnimationTimeline
    {
        public static readonly DependencyProperty FromProperty = DependencyProperty.Register(
            nameof(From),
            typeof(GridLength),
            typeof(GridLengthAnimation));

        public static readonly DependencyProperty ToProperty = DependencyProperty.Register(
            nameof(To),
            typeof(GridLength),
            typeof(GridLengthAnimation));

        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public override Type TargetPropertyType => typeof(GridLength);

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        {
            var progress = animationClock.CurrentProgress ?? 1;
            var eased = 1 - Math.Pow(1 - progress, 3);
            var width = From.Value + ((To.Value - From.Value) * eased);
            return new GridLength(width, GridUnitType.Pixel);
        }

        protected override Freezable CreateInstanceCore()
        {
            return new GridLengthAnimation();
        }
    }
}
