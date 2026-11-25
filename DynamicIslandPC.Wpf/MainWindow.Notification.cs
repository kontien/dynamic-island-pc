using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIslandPC.Wpf.Helpers;
using DynamicIslandPC.Wpf.Infrastructure;
using DynamicIslandPC.Wpf.Services;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private readonly List<NotificationItem> _notifItems = new();
        private readonly HashSet<uint> _notifSeen = new();
        private string _notifFilter = "all";
        private string _notifSearch = "";
        private int _notifUnread;

        private readonly DispatcherTimer _notifOsdTimer = new();
        private static readonly CubicEase NotifEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        private static readonly (string Key, string Label)[] NotifFilters =
        {
            ("all", "Все"),
            ("Мессенджеры", "💬 Чаты"),
            ("Игры", "🎮 Игры"),
            ("Система", "🪟 Система"),
            ("Браузеры", "🌐 Браузеры"),
            ("Прочее", "📦 Прочее")
        };

        private void InitNotifications()
        {
            _notifOsdTimer.Interval = TimeSpan.FromSeconds(4.5);
            _notifOsdTimer.Tick += (s, e) => HideNotifPeek();

            NotifBackBtn.Click += (s, e) => HideTileFocus();
            NotifSearchBox.TextChanged += (s, e) => { _notifSearch = NotifSearchBox.Text; BuildNotificationLargeList(); };
            NotifReadAllBtn.Click += (s, e) => MarkAllNotificationsRead();
            NotifClearBtn.Click += (s, e) => ClearNotifications();
            NotifAccessBtn.Click += async (s, e) => await RequestNotificationAccess();

            // Кнопка самотеста конвейера (не требует Windows-прав)
            if (NotifReadAllBtn.Parent is StackPanel sp &&
                !sp.Children.OfType<Button>().Any(b => b.Name == "NotifTestBtn"))
            {
                var test = new Button
                {
                    Name = "NotifTestBtn",
                    Content = "🧪 Тест",
                    Style = (Style)FindResource("ChipButton"),
                    Margin = new Thickness(6, 0, 0, 0),
                    ToolTip = "Впустить тестовое уведомление в остров"
                };
                test.Click += (s, e) => _notifService.InjectTest();
                sp.Children.Add(test);
            }

            BuildNotifChips();
            NotifOsd.Visibility = Visibility.Collapsed;
            NotifOsd.Opacity = 0;

            UpdateNotifAccessUi();
            BuildNotificationTile();
            UpdateNotifCounter();
        }

        private void BuildNotifChips()
        {
            NotifChips.Children.Clear();
            foreach (var (key, label) in NotifFilters)
            {
                var b = new Button
                {
                    Content = label,
                    Tag = key,
                    Style = (Style)FindResource("ChipButton"),
                    Margin = new Thickness(0, 0, 5, 0)
                };
                b.Click += (s, e) =>
                {
                    if (s is Button btn && btn.Tag is string k)
                    {
                        _notifFilter = k;
                        BuildNotificationLargeList();
                    }
                };
                NotifChips.Children.Add(b);
            }
            PaintNotifChips();
        }

        private void PaintNotifChips()
        {
            foreach (var b in NotifChips.Children.OfType<Button>())
            {
                bool on = b.Tag is string k && k == _notifFilter;
                b.Background = on ? (SolidColorBrush)FindResource("AccentColor") : (SolidColorBrush)FindResource("CardBg");
                b.Foreground = on ? new SolidColorBrush(Color.FromRgb(6, 20, 28)) : (SolidColorBrush)FindResource("TextPrimary");
                b.BorderBrush = on ? (SolidColorBrush)FindResource("AccentColor") : (SolidColorBrush)FindResource("Hair");
            }
        }

        // ==================== СОБЫТИЯ СЕРВИСА ====================
        private void OnNotificationAdded(NotificationItem item) => DispatchToUi(() =>
        {
            if (_notifSeen.Contains(item.Id)) return;
            _notifSeen.Add(item.Id);

            _notifItems.Insert(0, item);
            while (_notifItems.Count > 80) _notifItems.RemoveAt(_notifItems.Count - 1);

            _notifUnread = _notifItems.Count(x => !x.IsRead);
            UpdateNotifCounter();
            BuildNotificationTile();
            if (_isTileFocused && NotifPanel.Visibility == Visibility.Visible) BuildNotificationLargeList();

            ShowNotifPeek(item);
        });

        private void OnNotificationsChanged() => DispatchToUi(() =>
        {
            _notifUnread = _notifItems.Count(x => !x.IsRead);
            UpdateNotifCounter();
            BuildNotificationTile();
            if (_isTileFocused && NotifPanel.Visibility == Visibility.Visible) BuildNotificationLargeList();
        });

        private void OnNotificationAccessChanged() => DispatchToUi(() =>
        {
            UpdateNotifAccessUi();
            BuildNotificationTile();
            if (_isTileFocused && NotifPanel.Visibility == Visibility.Visible) BuildNotificationLargeList();
        });

        private void UpdateNotifCounter()
        {
            if (NotifCountText == null) return;
            NotifCountText.Text = $"🔔 {_notifUnread}";
            NotifCountText.Visibility = _notifUnread > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (NotifTileCount != null)
            {
                NotifTileCount.Text = _notifUnread > 0 ? $"{_notifUnread} новых" : "нет новых";
                NotifTileCount.Foreground = _notifUnread > 0
                    ? (SolidColorBrush)FindResource("AccentColor")
                    : (SolidColorBrush)FindResource("TextFaint");
            }
        }

        private void UpdateNotifAccessUi()
        {
            if (NotifAccessText == null) return;
            bool ok = _notifService.AccessGranted;
            NotifAccessText.Text = _notifService.AccessStatus;
            NotifAccessBar.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            NotifAccessBtn.Content = ok ? "Обновить" : "Разрешить доступ";
        }

        private async System.Threading.Tasks.Task RequestNotificationAccess()
        {
            NotifAccessText.Text = "Запрашиваем доступ…";
            bool ok = await _notifService.RequestAccessAsync();
            UpdateNotifAccessUi();

            if (ok)
            {
                ShowScreenshotConfirmation("Уведомления подключены");
                BuildNotificationLargeList();
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true });
                ShowScreenshotConfirmation("Открыты параметры уведомлений");
            }
            catch (Exception ex)
            {
                Log.Warn("[Notify] settings: " + ex.Message);
                ShowScreenshotConfirmation("Разрешите доступ в параметрах Windows", isError: true);
            }
        }

        // ==================== ПЛИТКА ====================
        private void BuildNotificationTile()
        {
            if (NotifTileStack == null) return;
            NotifTileStack.Children.Clear();

            if (!_notifService.AccessGranted && _notifItems.Count == 0)
            {
                NotifTileStack.Children.Add(new TextBlock
                {
                    Text = "Нажмите, чтобы подключить уведомления Windows",
                    FontSize = 10.5,
                    Foreground = (SolidColorBrush)FindResource("TextFaint"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                return;
            }

            if (_notifItems.Count == 0)
            {
                NotifTileStack.Children.Add(new TextBlock
                {
                    Text = "Новых уведомлений нет",
                    FontSize = 10.5,
                    Foreground = (SolidColorBrush)FindResource("TextFaint"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                return;
            }

            foreach (var item in _notifItems.Take(2))
                NotifTileStack.Children.Add(NotifTileRow(item));
        }

        private Grid NotifTileRow(NotificationItem item)
        {
            var g = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var host = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = item.IsRead
                    ? new SolidColorBrush(Color.FromArgb(0x0C, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };
            Grid.SetColumn(host, 0);
            Grid.SetColumnSpan(host, 3);
            g.Children.Add(host);

            var glyph = NotifGlyph(item, 16);
            Grid.SetColumn(glyph, 0);
            g.Children.Add(glyph);

            var text = new TextBlock
            {
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (SolidColorBrush)FindResource("TextPrimary")
            };
            text.Inlines.Add(new System.Windows.Documents.Run(item.AppName + " · ")
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("AccentColor")
            });
            text.Inlines.Add(new System.Windows.Documents.Run(item.Title));
            if (!string.IsNullOrEmpty(item.Body))
                text.Inlines.Add(new System.Windows.Documents.Run("  " + item.Body)
                {
                    Foreground = (SolidColorBrush)FindResource("TextFaint")
                });
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            var time = new TextBlock
            {
                Text = item.Time.ToString("HH:mm"),
                FontSize = 9.5,
                Foreground = (SolidColorBrush)FindResource("TextFaint"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            };
            Grid.SetColumn(time, 2);
            g.Children.Add(time);
            return g;
        }

        private UIElement NotifGlyph(NotificationItem item, double size)
        {
            if (item.Logo != null)
                return new Image
                {
                    Source = item.Logo,
                    Width = size,
                    Height = size,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

            string g = item.Group switch
            {
                "Мессенджеры" => "💬",
                "Игры" => "🎮",
                "Почта" => "✉️",
                "Браузеры" => "🌐",
                "Система" => "🪟",
                _ => "🔔"
            };
            return CenterIcon(g, (int)(size / 0.6));
        }

        // ==================== ПАНЕЛЬ ====================
        private void TileNotif_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(NotifPanel, TileNotif);
            BuildNotificationLargeList();
        }

        private void BuildNotificationLargeList()
        {
            if (NotifListStack == null) return;
            NotifListStack.Children.Clear();
            PaintNotifChips();
            UpdateNotifAccessUi();

            var items = _notifItems.AsEnumerable();
            if (_notifFilter != "all") items = items.Where(i => i.Group == _notifFilter);
            if (!string.IsNullOrWhiteSpace(_notifSearch))
            {
                string q = _notifSearch;
                items = items.Where(i =>
                    (i.AppName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (i.Title ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (i.Body ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            var list = items.OrderByDescending(i => i.Time).ToList();

            if (list.Count == 0)
            {
                NotifListStack.Children.Add(EmptyHint(_notifItems.Count == 0
                    ? "Уведомлений пока нет"
                    : "Ничего не найдено"));
                return;
            }

            foreach (var group in list.GroupBy(i => i.AppName).OrderByDescending(g => g.Max(x => x.Time)))
            {
                var head = new Grid { Margin = new Thickness(0, 8, 0, 6) };
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.Children.Add(new TextBlock
                {
                    Text = group.Key.ToUpperInvariant(),
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)FindResource("AccentColor"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                var cnt = new TextBlock
                {
                    Text = group.Count().ToString(),
                    FontSize = 9.5,
                    Foreground = (SolidColorBrush)FindResource("TextFaint"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(cnt, 1);
                head.Children.Add(cnt);
                NotifListStack.Children.Add(head);

                foreach (var item in group)
                    NotifListStack.Children.Add(NotifLargeRow(item));
            }

            NotifListStack.Children.Add(new TextBlock
            {
                Text = $"{list.Count} из {_notifItems.Count} · непрочитанных: {_notifUnread}",
                FontSize = 9.5,
                Foreground = (SolidColorBrush)FindResource("TextFaint"),
                Margin = new Thickness(0, 10, 0, 0)
            });

            StaggerNotifications(NotifListStack);
        }

        private Border NotifLargeRow(NotificationItem item)
        {
            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 5),
                Padding = new Thickness(9, 7, 9, 7),
                CornerRadius = new CornerRadius(8),
                Background = item.IsRead
                    ? new SolidColorBrush(Color.FromArgb(0x0C, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255)),
                BorderBrush = item.IsRead
                    ? new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255))
                    : (SolidColorBrush)FindResource("AccentColor"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "Клик — отметить прочитанным и открыть приложение"
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var glyph = NotifGlyph(item, 20);
            if (glyph is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Top;
                fe.Margin = new Thickness(0, 2, 0, 0);
            }
            Grid.SetColumn(glyph, 0);
            g.Children.Add(glyph);

            var text = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
            text.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("TextPrimary"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (!string.IsNullOrWhiteSpace(item.Body))
                text.Children.Add(new TextBlock
                {
                    Text = item.Body,
                    FontSize = 10.5,
                    Foreground = (SolidColorBrush)FindResource("TextSecondary"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 32,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            text.Children.Add(new TextBlock
            {
                Text = $"{item.Group} · {item.Time:HH:mm}",
                FontSize = 9,
                Foreground = (SolidColorBrush)FindResource("TextFaint"),
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            var actions = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Top };
            actions.Children.Add(RowActionBtn("\uE8A7", "Открыть приложение", () => ActivateNotificationApp(item)));
            var del = RowActionBtn("\uE74D", "Убрать уведомление", () => RemoveNotification(item), isDanger: true);
            del.Margin = new Thickness(0, 3, 0, 0);
            actions.Children.Add(del);
            Grid.SetColumn(actions, 2);
            g.Children.Add(actions);

            row.Child = g;
            row.MouseLeftButtonUp += (s, e) =>
            {
                if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
                e.Handled = true;
                item.IsRead = true;
                _notifUnread = _notifItems.Count(x => !x.IsRead);
                UpdateNotifCounter();
                BuildNotificationTile();
                BuildNotificationLargeList();
                ActivateNotificationApp(item);
            };
            return row;
        }

        private void ActivateNotificationApp(NotificationItem item)
        {
            try
            {
                string key = ((item.AppName ?? "") + " " + (item.AppId ?? "")).ToLowerInvariant();
                foreach (var p in Process.GetProcesses())
                {
                    bool match = false;
                    try
                    {
                        string n = p.ProcessName.ToLowerInvariant();
                        match = n.Length > 2 && key.Contains(n);
                    }
                    catch { }

                    if (match && p.MainWindowHandle != IntPtr.Zero)
                    {
                        NativeMethods.SetForegroundWindow(p.MainWindowHandle);
                        p.Dispose();
                        return;
                    }
                    p.Dispose();
                }
                ShowScreenshotConfirmation(item.AppName + ": окно не найдено");
            }
            catch (Exception ex) { Log.Warn("[Notify] activate: " + ex.Message); }
        }

        private void RemoveNotification(NotificationItem item)
        {
            _notifItems.RemoveAll(x => x.Id == item.Id);
            _notifSeen.Remove(item.Id);
            _notifService.Remove(item.Id);
            _notifUnread = _notifItems.Count(x => !x.IsRead);
            UpdateNotifCounter();
            BuildNotificationTile();
            BuildNotificationLargeList();
        }

        private void MarkAllNotificationsRead()
        {
            foreach (var i in _notifItems) i.IsRead = true;
            _notifUnread = 0;
            UpdateNotifCounter();
            BuildNotificationTile();
            BuildNotificationLargeList();
            ShowScreenshotConfirmation("Все уведомления прочитаны");
        }

        private void ClearNotifications()
        {
            _notifItems.Clear();
            _notifSeen.Clear();
            _notifUnread = 0;
            _notifService.Clear();
            UpdateNotifCounter();
            BuildNotificationTile();
            BuildNotificationLargeList();
            ShowScreenshotConfirmation("Уведомления очищены");
        }

        private static void StaggerNotifications(Panel panel)
        {
            int i = 0;
            foreach (UIElement child in panel.Children)
            {
                if (i > 14) { child.Opacity = 1; child.RenderTransform = null; continue; }
                child.Opacity = 0;
                var tt = new TranslateTransform(0, 7);
                child.RenderTransform = tt;
                var begin = TimeSpan.FromMilliseconds(i * 26);
                child.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { BeginTime = begin, EasingFunction = NotifEase });
                tt.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { BeginTime = begin, EasingFunction = NotifEase });
                i++;
            }
        }

        // ==================== PEEK В СВЁРНУТОМ ОСТРОВЕ ====================
        private void ShowNotifPeek(NotificationItem item)
        {
            if (_dnd || _isExpanded || _isFullScreen || _isAnimating) return;
            if (NotifOsd == null) return;

            if (item.Logo != null)
            {
                NotifOsdIcon.Text = "";
                NotifOsdLogo.Source = item.Logo;
                NotifOsdLogo.Visibility = Visibility.Visible;
            }
            else
            {
                NotifOsdLogo.Visibility = Visibility.Collapsed;
                NotifOsdIcon.Text = item.Group switch
                {
                    "Мессенджеры" => "💬",
                    "Игры" => "🎮",
                    "Почта" => "✉️",
                    "Браузеры" => "🌐",
                    "Система" => "🪟",
                    _ => "🔔"
                };
            }

            NotifOsdApp.Text = item.AppName;
            NotifOsdTitle.Text = item.Title;
            NotifOsdBody.Text = string.IsNullOrWhiteSpace(item.Body) ? " " : item.Body;
            NotifOsdTime.Text = item.Time.ToString("HH:mm");

            _notifOsdTimer.Stop();
            _notifOsdTimer.Start();

            NotifOsd.Visibility = Visibility.Visible;
            NotifOsd.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = NotifEase });
        }

        private void HideNotifPeek()
        {
            _notifOsdTimer.Stop();
            if (NotifOsd == null || NotifOsd.Visibility != Visibility.Visible) return;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)) { EasingFunction = NotifEase };
            fade.Completed += (s, e) =>
            {
                if (NotifOsd.Opacity < 0.05) NotifOsd.Visibility = Visibility.Collapsed;
            };
            NotifOsd.BeginAnimation(OpacityProperty, fade);
        }
    }
}