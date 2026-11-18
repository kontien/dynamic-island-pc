using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIslandPC.Wpf.Services;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private string _mixerSearchQuery = "";

        private void BuildAppVolumeList()
        {
            AppVolumeStack.Children.Clear();
            var masterGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            masterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            masterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            masterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var masterIcon = new TextBlock { Text = "🔊", FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(masterIcon, 0); masterGrid.Children.Add(masterIcon);
            var masterText = new TextBlock { Text = "ОБЩАЯ ГРОМКОСТЬ", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = (SolidColorBrush)FindResource("TextFaint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(masterText, 1); masterGrid.Children.Add(masterText);
            var masterSlider = new Slider { Minimum = 0, Maximum = 100, Value = _audio.CurrentVolume, Width = 150, Style = (Style)FindResource("CustomSlider") };
            masterSlider.ValueChanged += (s, ev) => { if (masterSlider.IsFocused) { _audio.SetVolume((int)ev.NewValue); VolBigText.Text = $"{(int)ev.NewValue}%"; } };
            Grid.SetColumn(masterSlider, 2); masterGrid.Children.Add(masterSlider);
            AppVolumeStack.Children.Add(masterGrid);

            var buttonsGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            buttonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var muteAllBtn = new Button { Content = "🔇 Mute All", FontFamily = new FontFamily("Segoe UI Emoji"), Style = (Style)FindResource("ChipButton"), Margin = new Thickness(0, 0, 4, 0) };
            muteAllBtn.Click += (s, e) => { foreach (var app in _appVolume.AppVolumes) _appVolume.SetAppMute(app.ProcessId, true); _appVolume.RefreshAppVolumes(); };
            Grid.SetColumn(muteAllBtn, 0); buttonsGrid.Children.Add(muteAllBtn);
            var unmuteAllBtn = new Button { Content = "🔊 Unmute All", FontFamily = new FontFamily("Segoe UI Emoji"), Style = (Style)FindResource("ChipButton"), Margin = new Thickness(4, 0, 0, 0) };
            unmuteAllBtn.Click += (s, e) => { foreach (var app in _appVolume.AppVolumes) _appVolume.SetAppMute(app.ProcessId, false); _appVolume.RefreshAppVolumes(); };
            Grid.SetColumn(unmuteAllBtn, 1); buttonsGrid.Children.Add(unmuteAllBtn);
            AppVolumeStack.Children.Add(buttonsGrid);

            var apps = _appVolume.AppVolumes;
            if (!string.IsNullOrEmpty(_mixerSearchQuery))
                apps = apps.Where(a => a.DisplayName.Contains(_mixerSearchQuery, StringComparison.OrdinalIgnoreCase)).ToList();
            if (apps.Count == 0) { AppVolumeStack.Children.Add(EmptyHint(string.IsNullOrEmpty(_mixerSearchQuery) ? "Нет активных приложений" : "Ничего не найдено")); return; }

            var groups = new Dictionary<string, List<AppVolumeInfo>> { ["Браузеры"] = new(), ["Игры"] = new(), ["Музыка"] = new(), ["Системные"] = new(), ["Другие"] = new() };
            foreach (var app in apps)
            {
                string name = app.DisplayName.ToLower();
                if (name.Contains("chrome") || name.Contains("firefox") || name.Contains("edge") || name.Contains("browser")) groups["Браузеры"].Add(app);
                else if (name.Contains("game") || name.Contains("steam") || name.Contains("epic")) groups["Игры"].Add(app);
                else if (name.Contains("spotify") || name.Contains("music") || name.Contains("vlc")) groups["Музыка"].Add(app);
                else if (app.ProcessId == 0 || name.Contains("system")) groups["Системные"].Add(app);
                else groups["Другие"].Add(app);
            }

            foreach (var group in groups.Where(g => g.Value.Count > 0))
            {
                AppVolumeStack.Children.Add(new TextBlock { Text = group.Key, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = (SolidColorBrush)FindResource("AccentColor"), Margin = new Thickness(0, 8, 0, 4) });
                foreach (var app in group.Value)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    var icon = new TextBlock { Text = GetAppIcon(app.DisplayName), FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    Grid.SetColumn(icon, 0); row.Children.Add(icon);
                    var name = new TextBlock { Text = app.DisplayName, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (SolidColorBrush)FindResource("TextPrimary"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                    Grid.SetColumn(name, 1); row.Children.Add(name);
                    var muteBtn = new Button { Content = app.IsMuted ? "\uE74F" : "\uE767", Width = 24, Height = 24, Style = (Style)FindResource("IconButton") };
                    muteBtn.Click += (s, ev) => { _appVolume.ToggleAppMute(app.ProcessId); _appVolume.RefreshAppVolumes(); };
                    Grid.SetColumn(muteBtn, 2); row.Children.Add(muteBtn);
                    var slider = new Slider { Minimum = 0, Maximum = 100, Value = app.Volume, Width = 90, Style = (Style)FindResource("CustomSlider") };
                    slider.ValueChanged += (s, ev) => { if (slider.IsFocused) _appVolume.SetAppVolume(app.ProcessId, (int)ev.NewValue); };
                    Grid.SetColumn(slider, 3); row.Children.Add(slider);
                    AppVolumeStack.Children.Add(row);
                }
            }
        }

        private void PresetGames_Click(object sender, RoutedEventArgs e)
        {
            _audio.SetVolume(80);
            foreach (var app in _appVolume.AppVolumes)
            {
                string n = app.DisplayName.ToLower();
                if (n.Contains("chrome") || n.Contains("firefox") || n.Contains("edge")) _appVolume.SetAppMute(app.ProcessId, true);
                else if (n.Contains("discord") || n.Contains("steam") || n.Contains("game")) { _appVolume.SetAppMute(app.ProcessId, false); _appVolume.SetAppVolume(app.ProcessId, 100); }
            }
            _appVolume.RefreshAppVolumes();
        }
        private void PresetWork_Click(object sender, RoutedEventArgs e)
        {
            _audio.SetVolume(40);
            foreach (var app in _appVolume.AppVolumes)
            {
                string n = app.DisplayName.ToLower();
                if (n.Contains("game") || n.Contains("steam") || n.Contains("epic")) _appVolume.SetAppMute(app.ProcessId, true);
                else if (n.Contains("discord") || n.Contains("telegram")) { _appVolume.SetAppMute(app.ProcessId, false); _appVolume.SetAppVolume(app.ProcessId, 60); }
                else if (n.Contains("chrome") || n.Contains("firefox") || n.Contains("edge")) { _appVolume.SetAppMute(app.ProcessId, false); _appVolume.SetAppVolume(app.ProcessId, 20); }
            }
            _appVolume.RefreshAppVolumes();
        }
        private void PresetMovie_Click(object sender, RoutedEventArgs e)
        {
            _audio.SetVolume(60);
            foreach (var app in _appVolume.AppVolumes)
            {
                string n = app.DisplayName.ToLower();
                if (n.Contains("chrome") || n.Contains("firefox") || n.Contains("edge") || n.Contains("vlc")) { _appVolume.SetAppMute(app.ProcessId, false); _appVolume.SetAppVolume(app.ProcessId, 100); }
                else _appVolume.SetAppMute(app.ProcessId, true);
            }
            _appVolume.RefreshAppVolumes();
        }

        private string GetAppIcon(string appName)
        {
            string n = appName.ToLower();
            if (n.Contains("chrome") || n.Contains("firefox") || n.Contains("edge")) return "🌐";
            if (n.Contains("spotify") || n.Contains("music")) return "🎵";
            if (n.Contains("discord")) return "💬";
            if (n.Contains("steam") || n.Contains("game")) return "🎮";
            if (n.Contains("vlc")) return "🎬";
            if (n.Contains("system")) return "⚙️";
            return "📱";
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingVolume) return;
            _isUpdatingVolume = true;
            try { _audio.SetVolume((int)e.NewValue); VolBigText.Text = $"{(int)e.NewValue}%"; UpdateOsdUi("Система", (int)e.NewValue); ShowOSD(); }
            finally { _isUpdatingVolume = false; }
        }

        private void MuteBtn_Click(object sender, RoutedEventArgs e) { _isMuted = !_isMuted; MuteBtn.Content = _isMuted ? "\uE74F" : "\uE767"; _audio.SetVolume(_isMuted ? 0 : 50); }

        private void BuildDeviceStack()
        {
            DeviceStack.Children.Clear();
            var devices = _audio.Devices;
            var activeId = _audio.ActiveDeviceId;
            for (int i = 0; i < Math.Min(devices.Count, 3); i++)
            {
                var dev = devices[i]; bool isActive = dev.ID == activeId;
                string icon = dev.FriendlyName.Contains("Head") || dev.FriendlyName.Contains("Науш") ? "🎧" : "🔈";
                var btn = new Button { Style = (Style)FindResource("DeviceButton"), Tag = dev.ID, Margin = new Thickness(0, 0, 0, 4) };
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock { Text = icon, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                sp.Children.Add(new TextBlock { Text = Truncate(dev.FriendlyName, 22), FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                btn.Content = sp;
                if (isActive) { btn.Background = new SolidColorBrush(Color.FromArgb(40, 40, 225, 246)); btn.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 40, 225, 246)); btn.Foreground = (SolidColorBrush)FindResource("AccentColor"); }
                string devId = dev.ID; btn.Click += async (s, e) => await _audio.SwitchDeviceAsync(devId, _lifetimeCts.Token);
                DeviceStack.Children.Add(btn);
            }
            if (devices.Count == 0) DeviceStack.Children.Add(new TextBlock { Text = "Нет устройств", FontSize = 10, Foreground = (SolidColorBrush)FindResource("TextSecondary"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }

        private void BuildDeviceLargeStack()
        {
            DeviceLargeStack.Children.Clear();
            var devices = _audio.Devices;
            var activeId = _audio.ActiveDeviceId;
            for (int i = 0; i < devices.Count; i++)
            {
                var dev = devices[i]; bool isActive = dev.ID == activeId;
                string icon = dev.FriendlyName.Contains("Head") || dev.FriendlyName.Contains("Науш") ? "🎧" : "🔈";
                var btn = new Button { Style = (Style)FindResource("DeviceButton"), Tag = dev.ID, Margin = new Thickness(0, 0, 0, 6) };
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock { Text = icon, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                sp.Children.Add(new TextBlock { Text = dev.FriendlyName, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                btn.Content = sp;
                if (isActive) { btn.Background = new SolidColorBrush(Color.FromArgb(40, 40, 225, 246)); btn.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 40, 225, 246)); btn.Foreground = (SolidColorBrush)FindResource("AccentColor"); }
                string devId = dev.ID; btn.Click += async (s, e) => await _audio.SwitchDeviceAsync(devId, _lifetimeCts.Token);
                DeviceLargeStack.Children.Add(btn);
            }
            if (devices.Count == 0) DeviceLargeStack.Children.Add(new TextBlock { Text = "Нет устройств", FontSize = 11, Foreground = (SolidColorBrush)FindResource("TextSecondary"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }

        private void MixerSearchBox_TextChanged(object sender, TextChangedEventArgs e) { _mixerSearchQuery = MixerSearchBox.Text; BuildAppVolumeList(); }
    }
}