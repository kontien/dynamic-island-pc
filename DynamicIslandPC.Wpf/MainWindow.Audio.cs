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

        private static string AppName(AppVolumeInfo app) => app.DisplayName ?? "";

        private void BuildAppVolumeList()
        {
            if (AppVolumeStack == null) return;
            AppVolumeStack.Children.Clear();

            var masterGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            masterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            masterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            masterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var masterIcon = new TextBlock
            {
                Text = "🔊",
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(masterIcon, 0);
            masterGrid.Children.Add(masterIcon);

            var masterText = new TextBlock
            {
                Text = "ОБЩАЯ ГРОМКОСТЬ",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (SolidColorBrush)FindResource("TextFaint"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(masterText, 1);
            masterGrid.Children.Add(masterText);

            var masterSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = _audio.CurrentVolume,
                Width = 150,
                Style = (Style)FindResource("CustomSlider")
            };
            masterSlider.ValueChanged += (s, ev) =>
            {
                if (!masterSlider.IsFocused && !masterSlider.IsMouseCaptureWithin) return;
                _audio.SetVolume((int)ev.NewValue);
                VolBigText.Text = $"{(int)ev.NewValue}%";
            };
            Grid.SetColumn(masterSlider, 2);
            masterGrid.Children.Add(masterSlider);
            AppVolumeStack.Children.Add(masterGrid);

            var buttonsGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            buttonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var muteAllBtn = new Button
            {
                Content = "🔇 Заглушить всё",
                Style = (Style)FindResource("ChipButton"),
                Margin = new Thickness(0, 0, 4, 0)
            };
            muteAllBtn.Click += (s, e) =>
            {
                foreach (var app in _appVolume.AppVolumes) _appVolume.SetAppMute(app.ProcessId, true);
                _appVolume.RefreshAppVolumes();
                BuildAppVolumeList();
            };
            Grid.SetColumn(muteAllBtn, 0);
            buttonsGrid.Children.Add(muteAllBtn);

            var unmuteAllBtn = new Button
            {
                Content = "🔊 Вернуть всё",
                Style = (Style)FindResource("ChipButton"),
                Margin = new Thickness(4, 0, 0, 0)
            };
            unmuteAllBtn.Click += (s, e) =>
            {
                foreach (var app in _appVolume.AppVolumes) _appVolume.SetAppMute(app.ProcessId, false);
                _appVolume.RefreshAppVolumes();
                BuildAppVolumeList();
            };
            Grid.SetColumn(unmuteAllBtn, 1);
            buttonsGrid.Children.Add(unmuteAllBtn);
            AppVolumeStack.Children.Add(buttonsGrid);

            var apps = _appVolume.AppVolumes.ToList();
            if (!string.IsNullOrWhiteSpace(_mixerSearchQuery))
                apps = apps.Where(a => AppName(a).Contains(_mixerSearchQuery, StringComparison.OrdinalIgnoreCase)).ToList();

            if (apps.Count == 0)
            {
                AppVolumeStack.Children.Add(EmptyHint(string.IsNullOrWhiteSpace(_mixerSearchQuery)
                    ? "Нет активных приложений" : "Ничего не найдено"));
                return;
            }

            var groups = new Dictionary<string, List<AppVolumeInfo>>
            {
                ["Браузеры"] = new(),
                ["Игры"] = new(),
                ["Музыка"] = new(),
                ["Общение"] = new(),
                ["Системные"] = new(),
                ["Другие"] = new()
            };

            foreach (var app in apps)
            {
                string name = AppName(app).ToLowerInvariant();
                if (name.Contains("chrome") || name.Contains("firefox") || name.Contains("edge") || name.Contains("browser") || name.Contains("браузер")) groups["Браузеры"].Add(app);
                else if (name.Contains("game") || name.Contains("steam") || name.Contains("epic") || name.Contains("игра")) groups["Игры"].Add(app);
                else if (name.Contains("spotify") || name.Contains("music") || name.Contains("vlc") || name.Contains("музык")) groups["Музыка"].Add(app);
                else if (name.Contains("discord") || name.Contains("telegram") || name.Contains("teams") || name.Contains("zoom")) groups["Общение"].Add(app);
                else if (app.ProcessId == 0 || name.Contains("system") || name.Contains("систем")) groups["Системные"].Add(app);
                else groups["Другие"].Add(app);
            }

            foreach (var group in groups.Where(g => g.Value.Count > 0))
            {
                AppVolumeStack.Children.Add(new TextBlock
                {
                    Text = group.Key.ToUpperInvariant(),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = (SolidColorBrush)FindResource("AccentColor"),
                    Margin = new Thickness(0, 8, 0, 4)
                });

                foreach (var app in group.Value)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

                    var icon = new TextBlock
                    {
                        Text = GetAppIcon(AppName(app)),
                        FontFamily = new FontFamily("Segoe UI Emoji"),
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    Grid.SetColumn(icon, 0);
                    row.Children.Add(icon);

                    var name = new TextBlock
                    {
                        Text = AppName(app),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (SolidColorBrush)FindResource("TextPrimary"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Opacity = app.IsMuted ? 0.5 : 1
                    };
                    Grid.SetColumn(name, 1);
                    row.Children.Add(name);

                    int pid = app.ProcessId;
                    var muteBtn = new Button
                    {
                        Content = app.IsMuted ? "\uE74F" : "\uE767",
                        Width = 24,
                        Height = 24,
                        Style = (Style)FindResource("IconButton"),
                        ToolTip = app.IsMuted ? "Включить звук" : "Заглушить"
                    };
                    muteBtn.Click += (s, ev) =>
                    {
                        _appVolume.ToggleAppMute(pid);
                        _appVolume.RefreshAppVolumes();
                        BuildAppVolumeList();
                    };
                    Grid.SetColumn(muteBtn, 2);
                    row.Children.Add(muteBtn);

                    var slider = new Slider
                    {
                        Minimum = 0,
                        Maximum = 100,
                        Value = app.Volume,
                        Width = 90,
                        Style = (Style)FindResource("CustomSlider"),
                        IsEnabled = !app.IsMuted
                    };
                    slider.ValueChanged += (s, ev) =>
                    {
                        if (!slider.IsFocused && !slider.IsMouseCaptureWithin) return;
                        _appVolume.SetAppVolume(pid, (int)ev.NewValue);
                    };
                    Grid.SetColumn(slider, 3);
                    row.Children.Add(slider);

                    AppVolumeStack.Children.Add(row);
                }
            }
        }

        private void PresetGames_Click(object sender, RoutedEventArgs e)
        {
            _audio.SetVolume(80);
            foreach (var app in _appVolume.AppVolumes)
            {
                string n = AppName(app).ToLowerInvariant();
                if (n.Contains("chrome") || n.Contains("firefox") || n.Contains("edge") || n.Contains("браузер"))
                    _appVolume.SetAppMute(app.ProcessId, true);
                else if (n.Contains("discord") || n.Contains("steam") || n.Contains("game") || n.Contains("игра"))
                { _appVolume.SetAppMute(app.ProcessId, false); _appVolume.SetAppVolume(app.ProcessId, 100); }
            }
            _appVolume.RefreshAppVolumes();
            BuildAppVolumeList();
        }

        private void PresetWork_Click(object sender, RoutedEventArgs e)
        {
            _audio.SetVolume(40);
            foreach (var app in _appVolume.AppVolumes)
            {
                string n = AppName(app).ToLowerInvariant();
                if (n.Contains("game") || n.Contains("steam") || n.Contains("epic")) _appVolume.SetAppMute(app.ProcessId, true);
                else if (n.Contains("discord") || n.Contains("telegram") || n.Contains("teams"))
                { _appVolume.SetAppMute(app.ProcessId, false); _appVolume.SetAppVolume(app.ProcessId, 60); }
                else if (n.Contains("chrome") || n.Contains("firefox") || n.Contains("edge") || n.Contains("браузер"))
                { _appVolume.SetAppMute(app.ProcessId, false); _appVolume.SetAppVolume(app.ProcessId, 20); }
            }
            _appVolume.RefreshAppVolumes();
            BuildAppVolumeList();
        }

        private void PresetMovie_Click(object sender, RoutedEventArgs e)
        {
            _audio.SetVolume(60);
            foreach (var app in _appVolume.AppVolumes)
            {
                string n = AppName(app).ToLowerInvariant();
                bool keep = n.Contains("chrome") || n.Contains("firefox") || n.Contains("edge") ||
                            n.Contains("vlc") || n.Contains("браузер") || n.Contains("spotify");
                _appVolume.SetAppMute(app.ProcessId, !keep);
                if (keep) _appVolume.SetAppVolume(app.ProcessId, 100);
            }
            _appVolume.RefreshAppVolumes();
            BuildAppVolumeList();
        }

        private string GetAppIcon(string? appName)
        {
            string n = (appName ?? "").ToLowerInvariant();
            if (n.Contains("chrome") || n.Contains("firefox") || n.Contains("edge") || n.Contains("браузер")) return "🌐";
            if (n.Contains("spotify") || n.Contains("music") || n.Contains("музык")) return "🎵";
            if (n.Contains("discord") || n.Contains("teams") || n.Contains("zoom")) return "💬";
            if (n.Contains("telegram")) return "✈️";
            if (n.Contains("steam") || n.Contains("game") || n.Contains("epic") || n.Contains("игра")) return "🎮";
            if (n.Contains("vlc")) return "🎬";
            if (n.Contains("system") || n.Contains("систем")) return "⚙️";
            return "📱";
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingVolume) return;
            _isUpdatingVolume = true;
            try
            {
                _audio.SetVolume((int)e.NewValue);
                VolBigText.Text = $"{(int)e.NewValue}%";
                UpdateOsdUi("Система", (int)e.NewValue);
                ShowOSD();
            }
            finally { _isUpdatingVolume = false; }
        }

        private void MuteBtn_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;
            MuteBtn.Content = _isMuted ? "\uE74F" : "\uE767";
            _audio.SetVolume(_isMuted ? 0 : 50);
        }

        // ==================== УСТРОЙСТВА ====================
        private static string DeviceGlyph(string full)
        {
            string n = full.ToLowerInvariant();
            if (n.Contains("head") || n.Contains("науш") || n.Contains("sony") || n.Contains("wh-") ||
                n.Contains("airpod") || n.Contains("buds")) return "🎧";
            if (n.Contains("hdmi") || n.Contains("display") || n.Contains("nvidia") || n.Contains("amd") || n.Contains("монитор")) return "🖥";
            if (n.Contains("2nd") || n.Contains("втор")) return "🔉";
            return "🔈";
        }

        private static string DeviceShort(string full)
        {
            if (full.Length == 0) return "";
            int i = full.IndexOf(" (", StringComparison.Ordinal);
            string b = i > 0 ? full.Substring(0, i) : full;
            var parts = b.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                         .Where(w =>
                         {
                             string t = w.ToLowerInvariant().Trim('.', ',');
                             return t != "audio" && t != "output" && t != "device" && t != "high" && t != "definition";
                         });
            string r = string.Join(" ", parts).Trim();
            return r.Length == 0 ? full.Trim() : r;
        }

        private void BuildDeviceStack()
        {
            if (DeviceStack == null) return;
            DeviceStack.Children.Clear();

            var devices = _audio.Devices;
            string activeId = _audio.ActiveDeviceId ?? "";
            var ordered = devices.OrderByDescending(d => (d.ID ?? "") == activeId).ToList();
            int shown = Math.Min(2, ordered.Count);

            if (shown == 0)
            {
                DeviceStack.Children.Add(new TextBlock
                {
                    Text = "Нет устройств",
                    FontSize = 10,
                    Foreground = (SolidColorBrush)FindResource("TextSecondary"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                return;
            }

            for (int i = 0; i < shown; i++)
            {
                var dev = ordered[i];
                string full = dev.FriendlyName ?? "";
                string id = dev.ID ?? "";
                bool active = id == activeId;

                var row = new Border
                {
                    Height = ordered.Count == 1 ? 34 : 24,
                    Margin = new Thickness(0, 0, 0, i == shown - 1 ? 0 : 3),
                    CornerRadius = new CornerRadius(6),
                    Background = active
                        ? new SolidColorBrush(Color.FromArgb(0x22, 40, 225, 246))
                        : new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)),
                    BorderBrush = active
                        ? (SolidColorBrush)FindResource("AccentColor")
                        : new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ClipToBounds = true,
                    ToolTip = full + (active ? "  ·  активно" : "  ·  нажмите, чтобы переключить")
                };

                var g = new Grid { Margin = new Thickness(7, 0, 7, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var glyph = new TextBlock
                {
                    Text = DeviceGlyph(full),
                    FontFamily = new FontFamily("Segoe UI Emoji"),
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(glyph, 0);
                g.Children.Add(glyph);

                var name = new TextBlock
                {
                    Text = Truncate(DeviceShort(full), 24),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)FindResource("TextPrimary"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(name, 1);
                g.Children.Add(name);

                if (active)
                {
                    var dot = new Border
                    {
                        Width = 6,
                        Height = 6,
                        CornerRadius = new CornerRadius(3),
                        Background = (SolidColorBrush)FindResource("AccentColor"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 0, 0)
                    };
                    Grid.SetColumn(dot, 2);
                    g.Children.Add(dot);
                }

                row.Child = g;
                string devId = id;
                row.MouseLeftButtonUp += async (s, e) =>
                {
                    e.Handled = true;
                    if (devId == (_audio.ActiveDeviceId ?? "")) return;
                    await _audio.SwitchDeviceAsync(devId, _lifetimeCts.Token);
                };

                DeviceStack.Children.Add(row);
            }

            if (LogicalTreeHelper.GetParent(DeviceStack) is ScrollViewer sv)
            {
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
        }

        private void BuildDeviceLargeStack()
        {
            if (DeviceLargeStack == null) return;
            DeviceLargeStack.Children.Clear();

            var devices = _audio.Devices;
            string activeId = _audio.ActiveDeviceId ?? "";

            DeviceLargeStack.Children.Add(new TextBlock
            {
                Text = devices.Count == 0 ? "УСТРОЙСТВА ВЫВОДА" : $"УСТРОЙСТВА ВЫВОДА · {devices.Count}",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("TextFaint"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            if (devices.Count == 0)
            {
                DeviceLargeStack.Children.Add(EmptyHint("Устройства вывода не найдены"));
                return;
            }

            foreach (var dev in devices.OrderByDescending(d => (d.ID ?? "") == activeId))
            {
                string full = dev.FriendlyName ?? "";
                string id = dev.ID ?? "";
                bool active = id == activeId;

                var row = new Border
                {
                    Height = 50,
                    Margin = new Thickness(0, 0, 0, 6),
                    CornerRadius = new CornerRadius(8),
                    Background = active
                        ? new SolidColorBrush(Color.FromArgb(0x22, 40, 225, 246))
                        : new SolidColorBrush(Color.FromArgb(0x12, 255, 255, 255)),
                    BorderBrush = active
                        ? (SolidColorBrush)FindResource("AccentColor")
                        : new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Cursor = active ? Cursors.Arrow : Cursors.Hand,
                    ClipToBounds = true
                };

                var g = new Grid { Margin = new Thickness(10, 0, 10, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var glyph = new TextBlock
                {
                    Text = DeviceGlyph(full),
                    FontFamily = new FontFamily("Segoe UI Emoji"),
                    FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(glyph, 0);
                g.Children.Add(glyph);

                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };
                text.Children.Add(new TextBlock
                {
                    Text = Truncate(DeviceShort(full), 44),
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)FindResource("TextPrimary"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                text.Children.Add(new TextBlock
                {
                    Text = Truncate(full, 56),
                    FontSize = 9.5,
                    Foreground = (SolidColorBrush)FindResource("TextFaint"),
                    Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                Grid.SetColumn(text, 1);
                g.Children.Add(text);

                var chip = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(8, 2, 8, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = active
                        ? (SolidColorBrush)FindResource("AccentColor")
                        : new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255)),
                    BorderBrush = active
                        ? (SolidColorBrush)FindResource("AccentColor")
                        : new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = active ? "АКТИВНО" : "Переключить",
                        FontSize = active ? 8.5 : 9.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = active
                            ? new SolidColorBrush(Color.FromRgb(6, 20, 28))
                            : (SolidColorBrush)FindResource("TextSecondary")
                    }
                };
                Grid.SetColumn(chip, 2);
                g.Children.Add(chip);

                row.Child = g;
                string devId = id;
                row.MouseLeftButtonUp += async (s, e) =>
                {
                    e.Handled = true;
                    if (devId == (_audio.ActiveDeviceId ?? "")) return;
                    await _audio.SwitchDeviceAsync(devId, _lifetimeCts.Token);
                };

                DeviceLargeStack.Children.Add(row);
            }

            DeviceLargeStack.Children.Add(new TextBlock
            {
                Text = "Клик по устройству делает его активным. Список обновляется автоматически.",
                FontSize = 9.5,
                Foreground = (SolidColorBrush)FindResource("TextFaint"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }

        private void MixerSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _mixerSearchQuery = MixerSearchBox.Text;
            BuildAppVolumeList();
        }
    }
}