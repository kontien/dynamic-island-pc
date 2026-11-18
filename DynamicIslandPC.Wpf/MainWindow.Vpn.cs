using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIslandPC.Wpf.Infrastructure;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private void UpdateVpnUi(bool isConnected)
        {
            if (isConnected)
            {
                VpnTileBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                VpnTileBorder.Background = new SolidColorBrush(Color.FromArgb(30, 16, 185, 129));
                VpnIcon.Text = "🛡️"; VpnStatusText.Text = "Вкл";
                VpnStatusText.Foreground = (SolidColorBrush)FindResource("Lime");
                VpnDetailsText.Text = "защищено";
                VpnIndicatorText.Visibility = Visibility.Visible;
            }
            else
            {
                VpnTileBorder.BorderBrush = (SolidColorBrush)FindResource("Hair");
                VpnTileBorder.Background = (SolidColorBrush)FindResource("CardBg");
                VpnIcon.Text = "⏻"; VpnStatusText.Text = "Выкл";
                VpnStatusText.Foreground = (SolidColorBrush)FindResource("TextSecondary");
                VpnDetailsText.Text = "подключить";
                VpnIndicatorText.Visibility = Visibility.Collapsed;
            }
        }

        private async void VpnTile_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isVpnToggling) return;
            _isVpnToggling = true;
            VpnIcon.Text = "⏳"; VpnStatusText.Text = "Связь…";
            try { await _vpnService.ToggleAsync(); }
            catch (Exception ex) { Log.Error("[VPN] toggle", ex); }
            _isVpnToggling = false;
        }
    }
}