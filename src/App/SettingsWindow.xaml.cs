using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Core;

namespace PopupGuard;

public partial class SettingsWindow : Window
{
    private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string AppRunName = "PopupGuard";
    private const string FilterSettingsPath = "filter_settings.json";

    public SettingsWindow()
    {
        #pragma warning disable CS0103
        InitializeComponent();
        #pragma warning restore CS0103
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        LoadSettings();
        InitPreset();
    }

    private void LoadSettings()
    {
        // 加载自启动设置
        if (FindName("AutoRunCheck") is System.Windows.Controls.CheckBox autoRunCheck)
        {
            autoRunCheck.IsChecked = IsAutoRunEnabled();
        }

        // 加载过滤设置
        if (FindName("FilterOwnProcessCheck") is System.Windows.Controls.CheckBox filterOwnCheck)
        {
            filterOwnCheck.IsChecked = LoadFilterSetting("FilterOwnProcess", true);
        }

        if (FindName("EnableSmartFilteringCheck") is System.Windows.Controls.CheckBox smartFilterCheck)
        {
            smartFilterCheck.IsChecked = LoadFilterSetting("EnableSmartFiltering", false);
        }

        // 加载性能设置
        if (FindName("EnableDetailedInfoCheck") is System.Windows.Controls.CheckBox enableDetailCheck)
        {
            enableDetailCheck.IsChecked = LoadPerfSettingBool("EnableDetailedInfo", false);
        }
        if (FindName("MinEventIntervalBox") is System.Windows.Controls.TextBox minIntervalBox)
        {
            minIntervalBox.Text = LoadPerfSettingInt("MinEventIntervalMs", 100).ToString();
        }
        if (FindName("UiUpdateIntervalBox") is System.Windows.Controls.TextBox uiIntervalBox)
        {
            uiIntervalBox.Text = LoadPerfSettingInt("UiUpdateIntervalMs", 200).ToString();
        }
        if (FindName("EnableIconCacheCheck") is System.Windows.Controls.CheckBox iconCacheCheck)
        {
            iconCacheCheck.IsChecked = LoadPerfSettingBool("EnableIconCache", true);
        }
        if (FindName("MaxRowsBox") is System.Windows.Controls.TextBox maxRowsBox)
        {
            maxRowsBox.Text = LoadPerfSettingInt("MaxRows", 500).ToString();
        }
    }

    private static string GetExecutablePath()
    {
        return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
    }

    private static bool IsAutoRunEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        var value = key?.GetValue(AppRunName) as string;
        return !string.IsNullOrEmpty(value);
    }

    private static void SetAutoRun(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true) ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enable)
        {
            key.SetValue(AppRunName, '"' + GetExecutablePath() + '"');
        }
        else
        {
            key.DeleteValue(AppRunName, false);
        }
    }

    private static bool LoadFilterSetting(string settingName, bool defaultValue)
    {
        try
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PopupGuard", FilterSettingsPath);
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<FilterSettings>(json);
                return settingName switch
                {
                    "FilterOwnProcess" => settings?.FilterOwnProcess ?? defaultValue,
                    "EnableSmartFiltering" => settings?.EnableSmartFiltering ?? defaultValue,
                    _ => defaultValue
                };
            }
        }
        catch
        {
            // 忽略错误，使用默认值
        }
        return defaultValue;
    }

    private static void SaveFilterSettings(bool filterOwnProcess, bool enableSmartFiltering)
    {
        try
        {
            var settings = new FilterSettings
            {
                FilterOwnProcess = filterOwnProcess,
                EnableSmartFiltering = enableSmartFiltering
            };

            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PopupGuard");
            Directory.CreateDirectory(appDataPath);
            
            var settingsPath = Path.Combine(appDataPath, FilterSettingsPath);
            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsPath, json);
        }
        catch
        {
            // 忽略保存错误
        }
    }

    private static bool LoadPerfSettingBool(string name, bool defaultValue)
    {
        try
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PopupGuard", FilterSettingsPath);
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<PerfSettings>(json);
                return name switch
                {
                    "EnableDetailedInfo" => settings?.EnableDetailedInfo ?? defaultValue,
                    "EnableIconCache" => settings?.EnableIconCache ?? defaultValue,
                    _ => defaultValue
                };
            }
        }
        catch { }
        return defaultValue;
    }

    private static int LoadPerfSettingInt(string name, int defaultValue)
    {
        try
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PopupGuard", FilterSettingsPath);
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<PerfSettings>(json);
                return name switch
                {
                    "MinEventIntervalMs" => settings?.MinEventIntervalMs ?? defaultValue,
                    "UiUpdateIntervalMs" => settings?.UiUpdateIntervalMs ?? defaultValue,
                    "MaxRows" => settings?.MaxRows ?? defaultValue,
                    _ => defaultValue
                };
            }
        }
        catch { }
        return defaultValue;
    }

    private static void SavePerfSettings(bool detailed, int minEventIntervalMs, int uiUpdateIntervalMs, bool enableIconCache, int maxRows)
    {
        try
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PopupGuard");
            Directory.CreateDirectory(appDataPath);
            var settingsPath = Path.Combine(appDataPath, FilterSettingsPath);

            PerfSettings existing = new PerfSettings();
            if (File.Exists(settingsPath))
            {
                try
                {
                    var json0 = File.ReadAllText(settingsPath);
                    existing = System.Text.Json.JsonSerializer.Deserialize<PerfSettings>(json0) ?? new PerfSettings();
                }
                catch { }
            }

            existing.EnableDetailedInfo = detailed;
            existing.MinEventIntervalMs = minEventIntervalMs;
            existing.UiUpdateIntervalMs = uiUpdateIntervalMs;
            existing.EnableIconCache = enableIconCache;
            existing.MaxRows = maxRows;

            var json = System.Text.Json.JsonSerializer.Serialize(existing, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsPath, json);
        }
        catch { }
    }

    private void InitPreset()
    {
        try
        {
            var detailed = LoadPerfSettingBool("EnableDetailedInfo", false);
            var minEvt = LoadPerfSettingInt("MinEventIntervalMs", 100);
            var uiInt = LoadPerfSettingInt("UiUpdateIntervalMs", 200);

            if (FindName("PerfPresetCombo") is System.Windows.Controls.ComboBox cb)
            {
                string preset = GetPresetTag(detailed, minEvt, uiInt);
                foreach (var item in cb.Items)
                {
                    if (item is System.Windows.Controls.ComboBoxItem cbi && (string)(cbi.Tag ?? "") == preset)
                    {
                        cb.SelectedItem = cbi;
                        break;
                    }
                }
            }
        }
        catch { }
    }

    private static string GetPresetTag(bool detailed, int minEvt, int uiInt)
    {
        // 预设映射
        // High: detailed=false, minEvt>=150, uiInt>=300
        // Balanced: detailed=false, ~100/200
        // Detail: detailed=true, minEvt<=80, uiInt<=150
        if (!detailed && minEvt >= 150 && uiInt >= 300) return "High";
        if (detailed && minEvt <= 80 && uiInt <= 150) return "Detail";
        if (!detailed && Math.Abs(minEvt - 100) <= 20 && Math.Abs(uiInt - 200) <= 40) return "Balanced";
        return "Custom";
    }

    private void OnPresetChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FindName("PerfPresetCombo") is not System.Windows.Controls.ComboBox cb || cb.SelectedItem is not System.Windows.Controls.ComboBoxItem cbi) return;
        var tag = (string)(cbi.Tag ?? "Custom");
        bool detailed = false; int minEvt = 100; int uiInt = 200; bool iconCache = true; int maxRows = 500;
        switch (tag)
        {
            case "High": detailed = false; minEvt = 180; uiInt = 350; iconCache = true; maxRows = 300; break;
            case "Balanced": detailed = false; minEvt = 100; uiInt = 200; iconCache = true; maxRows = 500; break;
            case "Detail": detailed = true; minEvt = 60; uiInt = 120; iconCache = true; maxRows = 800; break;
            case "Custom": return; // 不改动
        }

        if (FindName("EnableDetailedInfoCheck") is System.Windows.Controls.CheckBox detailedCheck)
        {
            detailedCheck.IsChecked = detailed;
        }
        if (FindName("MinEventIntervalBox") is System.Windows.Controls.TextBox minBox)
        {
            minBox.Text = minEvt.ToString();
        }
        if (FindName("UiUpdateIntervalBox") is System.Windows.Controls.TextBox uiBox)
        {
            uiBox.Text = uiInt.ToString();
        }
        if (FindName("EnableIconCacheCheck") is System.Windows.Controls.CheckBox iconCacheCheck)
        {
            iconCacheCheck.IsChecked = iconCache;
        }
        if (FindName("MaxRowsBox") is System.Windows.Controls.TextBox maxRowsBox)
        {
            maxRowsBox.Text = maxRows.ToString();
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // 保存自启动设置
            if (FindName("AutoRunCheck") is System.Windows.Controls.CheckBox autoRunCheck)
            {
                SetAutoRun(autoRunCheck.IsChecked == true);
            }

            // 保存过滤设置
            bool filterOwnProcess = false;
            bool enableSmartFiltering = false;

            if (FindName("FilterOwnProcessCheck") is System.Windows.Controls.CheckBox filterOwnCheck)
            {
                filterOwnProcess = filterOwnCheck.IsChecked == true;
            }

            if (FindName("EnableSmartFilteringCheck") is System.Windows.Controls.CheckBox smartFilterCheck)
            {
                enableSmartFiltering = smartFilterCheck.IsChecked == true;
            }

            SaveFilterSettings(filterOwnProcess, enableSmartFiltering);

            // 保存性能设置
            bool enableDetailed = false;
            int minEventIntervalMs = 100;
            int uiUpdateIntervalMs = 200;
            bool enableIconCache = true;
            int maxRows = 500;

            if (FindName("EnableDetailedInfoCheck") is System.Windows.Controls.CheckBox enableDetailCheck)
            {
                enableDetailed = enableDetailCheck.IsChecked == true;
            }
            if (FindName("MinEventIntervalBox") is System.Windows.Controls.TextBox minIntervalBox)
            {
                if (!int.TryParse(minIntervalBox.Text, out minEventIntervalMs) || minEventIntervalMs < 0) minEventIntervalMs = 100;
            }
            if (FindName("UiUpdateIntervalBox") is System.Windows.Controls.TextBox uiIntervalBox)
            {
                if (!int.TryParse(uiIntervalBox.Text, out uiUpdateIntervalMs) || uiUpdateIntervalMs < 50) uiUpdateIntervalMs = 200;
            }
            if (FindName("EnableIconCacheCheck") is System.Windows.Controls.CheckBox iconCacheCheck)
            {
                enableIconCache = iconCacheCheck.IsChecked == true;
            }
            if (FindName("MaxRowsBox") is System.Windows.Controls.TextBox maxRowsBox)
            {
                if (!int.TryParse(maxRowsBox.Text, out maxRows) || maxRows < 100) maxRows = 500;
            }

            SavePerfSettings(enableDetailed, minEventIntervalMs, uiUpdateIntervalMs, enableIconCache, maxRows);

            // 通知主窗口更新过滤/性能设置
            if (Owner is MainWindow mainWindow)
            {
                mainWindow.UpdateFilterSettings(filterOwnProcess, enableSmartFiltering);
                mainWindow.UpdatePerformanceSettings(enableDetailed, minEventIntervalMs, uiUpdateIntervalMs, enableIconCache, maxRows);
            }

            System.Windows.MessageBox.Show(this, "设置已保存。", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public class FilterSettings
{
    public bool FilterOwnProcess { get; set; } = true;
    public bool EnableSmartFiltering { get; set; } = false;
}

public class PerfSettings : FilterSettings
{
    public bool EnableDetailedInfo { get; set; } = false;
    public int MinEventIntervalMs { get; set; } = 100;
    public int UiUpdateIntervalMs { get; set; } = 200;
    public bool EnableIconCache { get; set; } = true;
    public int MaxRows { get; set; } = 500;
}


