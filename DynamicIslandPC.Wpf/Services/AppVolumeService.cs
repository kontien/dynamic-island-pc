using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using DynamicIslandPC.Wpf.Infrastructure;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicIslandPC.Wpf.Services
{
    public class AppVolumeInfo
    {
        public int ProcessId { get; set; }
        public string DisplayName { get; set; } = "";
        public int Volume { get; set; }
        public bool IsMuted { get; set; }
        public string? IconPath { get; set; }
    }

    public class AppVolumeService : IDisposable
    {
        private MMDeviceEnumerator? _enumerator;
        private MMDevice? _device;
        private readonly object _gate = new();
        private readonly Dictionary<string, TrackedSession> _tracked = new();
        private DispatcherTimer? _refreshTimer;
        private bool _disposed, _errorLogged;

        private readonly List<AppVolumeInfo> _appVolumes = new();
        public List<AppVolumeInfo> AppVolumes => _appVolumes;
        public event Action? AppVolumesChanged;

        public event Action<string, int>? AppVolumeChanged;

        private sealed class TrackedSession
        {
            public AudioSessionControl Session = null!;
            public VolumeHandler Handler = null!;
        }

        public void Start()
        {
            try
            {
                _enumerator = new MMDeviceEnumerator();
                _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _refreshTimer.Tick += (s, e) => RefreshSessions();
                _refreshTimer.Start();
                RefreshSessions();
                Log.Info("[AppVolume] запущен");
            }
            catch (Exception ex) { Log.Error("[AppVolume] init", ex); }
        }

        public void RefreshAppVolumes()
        {
            _appVolumes.Clear();
            if (_enumerator == null) return;

            try
            {
                var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessionManager = device.AudioSessionManager;

                for (int i = 0; i < sessionManager.Sessions.Count; i++)
                {
                    var session = sessionManager.Sessions[i];
                    if (session.IsSystemSoundsSession) continue;

                    int pid = (int)session.GetProcessID;
                    string name = GetProcessName(pid);
                    int volume = (int)(session.SimpleAudioVolume.Volume * 100);
                    bool muted = session.SimpleAudioVolume.Mute;

                    _appVolumes.Add(new AppVolumeInfo
                    {
                        ProcessId = pid,
                        DisplayName = name,
                        Volume = volume,
                        IsMuted = muted,
                        IconPath = session.IconPath
                    });
                }

                AppVolumesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("[AppVolume] refresh volumes", ex);
            }
        }

        public void SetAppVolume(int processId, int volume)
        {
            if (_enumerator == null) return;
            volume = Math.Clamp(volume, 0, 100);

            try
            {
                var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessionManager = device.AudioSessionManager;

                for (int i = 0; i < sessionManager.Sessions.Count; i++)
                {
                    var session = sessionManager.Sessions[i];
                    if ((int)session.GetProcessID == processId)
                    {
                        session.SimpleAudioVolume.Volume = volume / 100f;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[AppVolume] set volume", ex);
            }
        }

        public void SetAppMute(int processId, bool muted)
        {
            if (_enumerator == null) return;

            try
            {
                var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessionManager = device.AudioSessionManager;

                for (int i = 0; i < sessionManager.Sessions.Count; i++)
                {
                    var session = sessionManager.Sessions[i];
                    if ((int)session.GetProcessID == processId)
                    {
                        session.SimpleAudioVolume.Mute = muted;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[AppVolume] set mute", ex);
            }
        }
        public void ToggleAppMute(int processId)
        {
            if (_enumerator == null) return;

            try
            {
                var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessionManager = device.AudioSessionManager;

                for (int i = 0; i < sessionManager.Sessions.Count; i++)
                {
                    var session = sessionManager.Sessions[i];
                    if ((int)session.GetProcessID == processId)
                    {
                        session.SimpleAudioVolume.Mute = !session.SimpleAudioVolume.Mute;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[AppVolume] toggle mute", ex);
            }
        }

        private void RefreshSessions()
        {
            if (_enumerator == null || _disposed) return;
            try
            {
                var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                lock (_gate)
                {
                    if (_device == null || _device.ID != defaultDevice.ID)
                    {
                        ClearTrackedLocked();
                        _device?.Dispose();
                        _device = defaultDevice;
                    }
                    else defaultDevice.Dispose();

                    var sessions = _device.AudioSessionManager.Sessions;
                    var alive = new HashSet<string>();

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        AudioSessionControl? session = null;
                        try
                        {
                            session = sessions[i];
                            if (session.IsSystemSoundsSession) { session.Dispose(); continue; }
                            string key = session.GetSessionInstanceIdentifier;
                            if (string.IsNullOrEmpty(key)) key = "pid:" + session.GetProcessID;
                            alive.Add(key);
                            if (!_tracked.ContainsKey(key))
                            {
                                var handler = new VolumeHandler(session, (name, vol) => AppVolumeChanged?.Invoke(name, vol), () => RemoveTracked(key));
                                _tracked[key] = new TrackedSession { Session = session, Handler = handler };
                            }
                            else session.Dispose();
                        }
                        catch (Exception ex) { session?.Dispose(); Log.Warn($"[AppVolume] сессия {i}: {ex.Message}"); }
                    }
                    foreach (var key in _tracked.Keys.Where(k => !alive.Contains(k)).ToList()) RemoveTrackedLocked(key);
                }
                _errorLogged = false;
            }
            catch (Exception ex)
            {
                if (_errorLogged) return;
                _errorLogged = true;
                Log.Error("[AppVolume] refresh", ex);
            }
        }

        private void RemoveTracked(string key) { lock (_gate) RemoveTrackedLocked(key); }

        private void RemoveTrackedLocked(string key)
        {
            if (!_tracked.TryGetValue(key, out var t)) return;
            _tracked.Remove(key);
            t.Handler.Detach(); t.Session.Dispose();
        }

        private void ClearTrackedLocked()
        {
            foreach (var t in _tracked.Values) { t.Handler.Detach(); t.Session.Dispose(); }
            _tracked.Clear();
        }

        private static string GetProcessName(int pid)
        {
            try
            {
                if (pid == 0) return "System Sounds";
                using var p = Process.GetProcessById(pid);
                string name = p.ProcessName;
                return name.ToLowerInvariant() switch
                {
                    var n when n.Contains("chrome") || n.Contains("msedge") || n.Contains("firefox") || n.Contains("brave") || n.Contains("yandex") || n.Contains("opera") => "Браузер",
                    var n when n.Contains("spotify") => "Spotify",
                    var n when n.Contains("discord") => "Discord",
                    var n when n.Contains("telegram") => "Telegram",
                    var n when n.Contains("steam") => "Steam",
                    var n when n.Contains("vlc") => "VLC",
                    var n when n.Contains("music") => "Музыка",
                    _ => char.ToUpper(name[0]) + name.Substring(1)
                };
            }
            catch
            {
                return $"Process {pid}";
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _refreshTimer?.Stop();
            lock (_gate) { ClearTrackedLocked(); _device?.Dispose(); _device = null; }
            _enumerator?.Dispose(); _enumerator = null;
        }
    }

    internal class VolumeHandler : IAudioSessionEventsHandler
    {
        private readonly AudioSessionControl _session;
        private readonly Action<string, int> _callback;
        private readonly Action _onDisconnected;
        private readonly string _appName;
        private bool _registered;

        public VolumeHandler(AudioSessionControl session, Action<string, int> callback, Action onDisconnected)
        {
            _session = session; _callback = callback; _onDisconnected = onDisconnected;
            _appName = GetProcessName((int)session.GetProcessID);
            try { _session.RegisterEventClient(this); _registered = true; }
            catch (Exception ex) { Log.Warn($"[AppVolume] подписка: {ex.Message}"); }
        }

        public void Detach()
        {
            if (!_registered) return;
            _registered = false;
            try { _session.UnRegisterEventClient(this); }
            catch (Exception ex) { Log.Warn($"[AppVolume] отписка: {ex.Message}"); }
        }

        private static string GetProcessName(int pid)
        {
            try
            {
                if (pid == 0) return "Система";
                using var p = Process.GetProcessById(pid);
                string name = p.ProcessName;
                if (name.Contains("chrome", StringComparison.OrdinalIgnoreCase) || name.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("firefox", StringComparison.OrdinalIgnoreCase) || name.Contains("opera", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("brave", StringComparison.OrdinalIgnoreCase) || name.Contains("yandex", StringComparison.OrdinalIgnoreCase))
                    return "Браузер";
                if (name.Contains("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
                if (name.Contains("discord", StringComparison.OrdinalIgnoreCase)) return "Discord";
                if (name.Contains("telegram", StringComparison.OrdinalIgnoreCase)) return "Telegram";
                if (name.Contains("steam", StringComparison.OrdinalIgnoreCase)) return "Steam";
                if (name.Contains("music", StringComparison.OrdinalIgnoreCase)) return "Музыка";
                return char.ToUpper(name[0]) + name.Substring(1);
            }
            catch { return "Приложение"; }
        }

        public void OnVolumeChanged(float volume, bool isMuted) => _callback(_appName, (int)(volume * 100));
        public void OnSessionDisconnected(AudioSessionDisconnectReason reason) => _onDisconnected();
        public void OnStateChanged(AudioSessionState state) { if (state == AudioSessionState.AudioSessionStateExpired) _onDisconnected(); }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnDisplayNameChanged(string displayName) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void OnIconPathChanged(string iconPath) { }
    }
}