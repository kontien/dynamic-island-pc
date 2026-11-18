using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DynamicIslandPC.Wpf.Infrastructure;
using NAudio.CoreAudioApi;

namespace DynamicIslandPC.Wpf.Services
{
    [ComImport][Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] internal class PolicyConfigClient { }
    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
        void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode();
        void GetPropertyValue(); void SetPropertyValue();
        void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int eRole);
        void SetEndpointVisibility();
    }

    public class AudioService : IDisposable
    {
        private MMDevice? _device;
        private MMDeviceEnumerator? _enumerator;
        private readonly string _svvPath;
        private int _currentVolume;
        private bool _disposed;

        public event EventHandler<int>? VolumeChanged;
        public event EventHandler? DevicesChanged;
        public List<MMDevice> Devices { get; private set; } = new();
        public int CurrentVolume => Volatile.Read(ref _currentVolume);
        public string? ActiveDeviceId => _device?.ID;

        public AudioService(string svvPath) { _svvPath = svvPath; Initialize(); }

        private void Initialize()
        {
            try
            {
                _enumerator = new MMDeviceEnumerator();
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                SetCurrentVolume((int)(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100), notify: false);
                _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
                RefreshDevices();
                Log.Info($"[Audio] init: {CurrentVolume}%, устройств {Devices.Count}");
            }
            catch (Exception ex) { Log.Error("[Audio] init", ex); }
        }

        private void RefreshDevices()
        {
            if (_enumerator == null) return;
            foreach (var d in Devices)
            {
                try { d.Dispose(); }
                catch (Exception ex) { Log.Warn($"[Audio] dispose device: {ex.Message}"); }
            }
            Devices.Clear();
            try { foreach (var ep in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) Devices.Add(ep); }
            catch (Exception ex) { Log.Error("[Audio] RefreshDevices", ex); }
        }

        private void OnVolumeNotification(AudioVolumeNotificationData data)
        {
            // NAudio may invoke this callback on its COM notification thread; keep it UI-free and thread-safe.
            SetCurrentVolume((int)(data.MasterVolume * 100), notify: true);
        }

        private void SetCurrentVolume(int volume, bool notify)
        {
            volume = Math.Clamp(volume, 0, 100);
            int previous = Interlocked.Exchange(ref _currentVolume, volume);
            if (!notify || previous == volume) return;

            try { VolumeChanged?.Invoke(this, volume); }
            catch (Exception ex) { Log.Error("[Audio] volume callback", ex); }
        }

        public void SetVolume(int volume)
        {
            if (_disposed || _device == null) return;
            volume = Math.Clamp(volume, 0, 100);
            try { _device.AudioEndpointVolume.MasterVolumeLevelScalar = volume / 100f; }
            catch (Exception ex) { Log.Error("[Audio] SetVolume", ex); }
        }

        public async Task SwitchDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (_disposed || string.IsNullOrWhiteSpace(deviceId)) return;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Log.Info($"[Audio] переключение на {deviceId}");
                bool switched = SwitchViaCom(deviceId);
                if (!switched)
                    switched = await SwitchViaSoundVolumeViewAsync(deviceId, cancellationToken);
                if (!switched) return;

                cancellationToken.ThrowIfCancellationRequested();
                Reconnect(); RefreshDevices();
                try { DevicesChanged?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex) { Log.Error("[Audio] device callback", ex); }
                Log.Info($"[Audio] готово: {_device?.FriendlyName}, {CurrentVolume}%");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex) { Log.Error("[Audio] SwitchDevice", ex); }
        }

        private bool SwitchViaCom(string deviceId)
        {
            IPolicyConfig? policy = null;
            try
            {
                policy = (IPolicyConfig)new PolicyConfigClient();
                for (int role = 0; role <= 2; role++) policy.SetDefaultEndpoint(deviceId, role);
                return true;
            }
            catch (Exception ex) { Log.Warn($"[Audio] COM: {ex.Message}"); return false; }
            finally
            {
                // Release the policy COM object on the same thread that created it.
                if (policy != null && Marshal.IsComObject(policy)) Marshal.FinalReleaseComObject(policy);
            }
        }

        private async Task<bool> SwitchViaSoundVolumeViewAsync(string deviceId, CancellationToken cancellationToken)
        {
            if (!File.Exists(_svvPath)) { Log.Warn("[Audio] SoundVolumeView не найден"); return false; }
            try
            {
                for (int role = 0; role <= 2; role++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var psi = new ProcessStartInfo { FileName = _svvPath, UseShellExecute = false, CreateNoWindow = true };
                    psi.ArgumentList.Add("/SetDefault");
                    psi.ArgumentList.Add(deviceId);
                    psi.ArgumentList.Add(role.ToString(System.Globalization.CultureInfo.InvariantCulture));

                    using var process = Process.Start(psi);
                    if (process == null) return false;

                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException)
                    {
                        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                        catch (Exception ex) { Log.Warn($"[Audio] остановка SoundVolumeView: {ex.Message}"); }

                        if (cancellationToken.IsCancellationRequested) throw;
                        Log.Warn("[Audio] SoundVolumeView завершился по таймауту");
                        return false;
                    }

                    if (process.ExitCode != 0)
                    {
                        Log.Warn($"[Audio] SoundVolumeView завершился с кодом {process.ExitCode}");
                        return false;
                    }
                }
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Log.Error("[Audio] SVV", ex); }
            return false;
        }

        private void Reconnect()
        {
            if (_device != null) { _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification; _device.Dispose(); }
            _device = _enumerator?.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            if (_device == null) return;
            _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
            SetCurrentVolume((int)(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100), notify: true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_device != null) { _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification; _device.Dispose(); _device = null; }
            foreach (var d in Devices)
            {
                try { d.Dispose(); }
                catch (Exception ex) { Log.Warn($"[Audio] dispose device: {ex.Message}"); }
            }
            Devices.Clear();
            _enumerator?.Dispose(); _enumerator = null;
            VolumeChanged = null;
            DevicesChanged = null;
        }
    }
}