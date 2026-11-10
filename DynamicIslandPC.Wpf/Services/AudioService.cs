using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace DynamicIslandPC.Wpf.Services
{
    // COM: смена устройства по умолчанию без прав администратора
    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class PolicyConfigClient { }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        void GetMixFormat();
        void GetDeviceFormat();
        void ResetDeviceFormat();
        void SetDeviceFormat();
        void GetProcessingPeriod();
        void SetProcessingPeriod();
        void GetShareMode();
        void SetShareMode();
        void GetPropertyValue();
        void SetPropertyValue();
        void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int eRole);
        void SetEndpointVisibility();
    }

    public class AudioService : IDisposable
    {
        private MMDevice? _device;
        private MMDeviceEnumerator? _enumerator;
        private readonly string _svvPath;

        public event EventHandler<int>? VolumeChanged;
        public event EventHandler? DevicesChanged;
        public List<MMDevice> Devices { get; private set; } = new();

        public int CurrentVolume { get; private set; }
        public string? ActiveDeviceId => _device?.ID;

        public AudioService(string svvPath)
        {
            _svvPath = svvPath;
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                _enumerator = new MMDeviceEnumerator();
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                CurrentVolume = (int)(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;

                RefreshDevices();
                Console.WriteLine($"[Audio] Initialized. Volume: {CurrentVolume}%. Devices: {Devices.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Audio] Init failed: {ex.Message}");
            }
        }

        private void RefreshDevices()
        {
            if (_enumerator == null) return;
            Devices.Clear();
            try
            {
                var endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var ep in endpoints)
                    Devices.Add(ep);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Audio] RefreshDevices failed: {ex.Message}");
            }
        }

        private void OnVolumeNotification(AudioVolumeNotificationData data)
        {
            int newVolume = (int)(data.MasterVolume * 100);
            if (newVolume != CurrentVolume)
            {
                CurrentVolume = newVolume;
                VolumeChanged?.Invoke(this, newVolume);
            }
        }

        public void SetVolume(int volume)
        {
            if (_device == null) return;
            volume = Math.Clamp(volume, 0, 100);
            try { _device.AudioEndpointVolume.MasterVolumeLevelScalar = volume / 100f; }
            catch (Exception ex) { Console.WriteLine($"[Audio] SetVolume failed: {ex.Message}"); }
        }

        public void SwitchDevice(string deviceId)
        {
            try
            {
                Console.WriteLine($"[Audio] Switching to: {deviceId}");

                bool success = SwitchViaCom(deviceId);
                if (!success)
                {
                    Console.WriteLine("[Audio] COM failed, fallback to SoundVolumeView...");
                    SwitchViaSoundVolumeView(deviceId);
                }

                Reconnect();
                RefreshDevices();
                DevicesChanged?.Invoke(this, EventArgs.Empty);

                Console.WriteLine($"[Audio] Done. Device: {_device?.FriendlyName}, Volume: {CurrentVolume}%");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Audio] Switch failed: {ex.Message}");
            }
        }

        private bool SwitchViaCom(string deviceId)
        {
            try
            {
                var policy = (IPolicyConfig)new PolicyConfigClient();
                policy.SetDefaultEndpoint(deviceId, 0); // 0 = eConsole
                Console.WriteLine("[Audio] Switched via COM");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Audio] COM error: {ex.Message}");
                return false;
            }
        }

        private void SwitchViaSoundVolumeView(string deviceId)
        {
            try
            {
                if (!File.Exists(_svvPath))
                {
                    Console.WriteLine("[Audio] SoundVolumeView not found");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = _svvPath,
                    Arguments = $"/SetDefault \"{deviceId}\" 0",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Audio] SVV error: {ex.Message}");
            }
        }

        private void Reconnect()
        {
            if (_device != null)
                _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;

            _device = _enumerator?.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            if (_device != null)
            {
                _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
                CurrentVolume = (int)(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                VolumeChanged?.Invoke(this, CurrentVolume);
            }
        }

        public void Dispose()
        {
            if (_device != null)
                _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
        }
    }
}