using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicIslandPC.Wpf.Services
{
    public class AppVolumeService : IDisposable
    {
        private MMDeviceEnumerator? _enumerator;
        private MMDevice? _device;
        private readonly object _lock = new();
        private readonly Dictionary<string, TrackedSession> _tracked = new();
        private DispatcherTimer? _refreshTimer;
        private bool _disposed;

        // Теперь передаем PID для умного OSD
        public event Action<string, int, uint>? AppVolumeChanged;
        public event Action? SessionsChanged;

        private sealed class TrackedSession
        {
            public AudioSessionControl Session = null!;
            public VolumeHandler Handler = null!;
            public uint ProcessId;
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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppVolume] Init failed: {ex.Message}");
            }
        }

        private void RefreshSessions()
        {
            if (_enumerator == null || _disposed) return;
            try
            {
                var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                bool changed = false;
                lock (_lock)
                {
                    if (_device == null || _device.ID != defaultDevice.ID)
                    {
                        ClearTrackedLocked();
                        _device?.Dispose();
                        _device = defaultDevice;
                        changed = true;
                    }
                    else defaultDevice.Dispose();

                    var sessions = _device.AudioSessionManager.Sessions;
                    var alive = new HashSet<string>();

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (session.IsSystemSoundsSession) { session.Dispose(); continue; }

                        string key = session.GetSessionInstanceIdentifier;
                        if (string.IsNullOrEmpty(key)) key = "pid:" + session.GetProcessID;
                        alive.Add(key);

                        if (!_tracked.ContainsKey(key))
                        {
                            var handler = new VolumeHandler(
                                session,
                                (name, vol, pid) => AppVolumeChanged?.Invoke(name, vol, pid),
                                () => RemoveTracked(key));
                            _tracked[key] = new TrackedSession { Session = session, Handler = handler, ProcessId = session.GetProcessID };
                            changed = true;
                        }
                        else session.Dispose();
                    }

                    var toRemove = _tracked.Keys.Where(k => !alive.Contains(k)).ToList();
                    foreach (var key in toRemove)
                    {
                        RemoveTrackedLocked(key);
                        changed = true;
                    }
                }

                if (changed) SessionsChanged?.Invoke();
            }
            catch { }
        }

        public void SetVolume(string key, int volume)
        {
            lock (_lock)
            {
                if (_tracked.TryGetValue(key, out var session))
                {
                    try { session.Session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0, 100) / 100f; }
                    catch { }
                }
            }
        }

        public IEnumerable<(string Key, string Name, int Volume, uint Pid)> GetActiveSessions()
        {
            lock (_lock)
            {
                foreach (var kv in _tracked)
                {
                    yield return (kv.Key, kv.Value.Handler.AppName, (int)(kv.Value.Session.SimpleAudioVolume.Volume * 100), kv.Value.ProcessId);
                }
            }
        }

        private void RemoveTracked(string key)
        {
            lock (_lock)
            {
                RemoveTrackedLocked(key);
                SessionsChanged?.Invoke();
            }
        }

        private void RemoveTrackedLocked(string key)
        {
            if (!_tracked.TryGetValue(key, out var t)) return;
            _tracked.Remove(key);
            t.Handler.Detach();
            t.Session.Dispose();
        }

        private void ClearTrackedLocked()
        {
            foreach (var t in _tracked.Values) { t.Handler.Detach(); t.Session.Dispose(); }
            _tracked.Clear();
        }

        public void Dispose()
        {
            _disposed = true;
            _refreshTimer?.Stop();
            lock (_lock)
            {
                ClearTrackedLocked();
                _device?.Dispose();
                _device = null;
            }
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }

    internal class VolumeHandler : IAudioSessionEventsHandler
    {
        private readonly AudioSessionControl _session;
        private readonly Action<string, int, uint> _callback;
        private readonly Action _onDisconnected;
        private readonly uint _processId;
        public string AppName { get; }

        public VolumeHandler(AudioSessionControl session, Action<string, int, uint> callback, Action onDisconnected)
        {
            _session = session;
            _callback = callback;
            _onDisconnected = onDisconnected;
            _processId = session.GetProcessID;
            AppName = GetFriendlyName((int)_processId);
            try { _session.RegisterEventClient(this); } catch { }
        }

        public void Detach()
        {
            try { _session.UnRegisterEventClient(this); } catch { }
        }

        private string GetFriendlyName(int pid)
        {
            try
            {
                if (pid == 0) return "Система";
                using var p = Process.GetProcessById(pid);
                string name = p.ProcessName;
                if (name.Contains("chrome") || name.Contains("msedge") || name.Contains("firefox") || name.Contains("opera") || name.Contains("brave") || name.Contains("yandex"))
                    return "Браузер";
                if (name.Contains("spotify")) return "Spotify";
                if (name.Contains("discord")) return "Discord";
                if (name.Contains("telegram")) return "Telegram";
                return char.ToUpper(name[0]) + name.Substring(1);
            }
            catch { return "Приложение"; }
        }

        public void OnVolumeChanged(float volume, bool isMuted) => _callback(AppName, (int)(volume * 100), _processId);
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnDisplayNameChanged(string displayName) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => _onDisconnected();
        public void OnStateChanged(AudioSessionState state) { if (state == AudioSessionState.AudioSessionStateExpired) _onDisconnected(); }
    }
}