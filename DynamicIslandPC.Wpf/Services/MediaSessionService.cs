using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace DynamicIslandPC.Wpf.Services
{
    public class MediaSessionService : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _current;
        private DispatcherTimer? _fallbackTimer;
        private SynchronizationContext? _sync;
        private bool _disposed;
        private bool _updating;
        private string _lastKey = "";
        private bool _lastPlaying;

        public event Action<string, string, BitmapImage?>? TrackChanged;
        public event Action<bool>? PlaybackStateChanged;

        public bool HasActiveSession => _current != null;

        public async void Start()
        {
            _sync = SynchronizationContext.Current;
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;

                _fallbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _fallbackTimer.Tick += (s, e) => Refresh();
                _fallbackTimer.Start();

                Rehook();
                Refresh();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaSession] Start failed: {ex.Message}");
            }
        }

        private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
            => Post(Refresh);

        private void OnSessionMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
            => Post(Refresh);

        private void OnSessionPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
            => Post(Refresh);

        private void Post(Action a)
        {
            if (_sync != null) _sync.Post(_ => a(), null);
            else a();
        }

        // Выбираем сессию: сначала "focused", иначе любую играющую, иначе первую
        private GlobalSystemMediaTransportControlsSession? PickSession()
        {
            if (_manager == null) return null;
            try
            {
                var cur = _manager.GetCurrentSession();
                if (cur != null) return cur;
                var all = _manager.GetSessions();
                return all.FirstOrDefault(s => SafePlaying(s)) ?? all.FirstOrDefault();
            }
            catch { return null; }
        }

        private static bool SafePlaying(GlobalSystemMediaTransportControlsSession s)
        {
            try { return s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        }

        private void Rehook()
        {
            if (_current != null)
            {
                try
                {
                    _current.MediaPropertiesChanged -= OnSessionMediaPropertiesChanged;
                    _current.PlaybackInfoChanged -= OnSessionPlaybackInfoChanged;
                }
                catch { }
            }
            _current = PickSession();
            if (_current != null)
            {
                try
                {
                    _current.MediaPropertiesChanged += OnSessionMediaPropertiesChanged;
                    _current.PlaybackInfoChanged += OnSessionPlaybackInfoChanged;
                }
                catch { }
            }
        }

        private void Refresh()
        {
            if (_disposed) return;
            if (_manager == null) { _ = TryReinit(); return; }

            var picked = PickSession();
            if (!ReferenceEquals(picked, _current)) Rehook();

            if (_current == null)
            {
                if (_lastKey != "")
                {
                    _lastKey = "";
                    TrackChanged?.Invoke("", "", null);
                }
                if (_lastPlaying) { _lastPlaying = false; PlaybackStateChanged?.Invoke(false); }
                return;
            }
            _ = UpdateAsync(_current);
        }

        private async Task TryReinit()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                Rehook();
            }
            catch { }
        }

        private async Task UpdateAsync(GlobalSystemMediaTransportControlsSession session)
        {
            if (_updating || _disposed) return;
            _updating = true;
            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (_disposed || !ReferenceEquals(session, _current)) return;

                string title = props?.Title ?? "";
                string artist = props?.Artist ?? "";

                BitmapImage? cover = null;
                if (props?.Thumbnail != null)
                {
                    try
                    {
                        using var rs = await props.Thumbnail.OpenReadAsync();
                        cover = new BitmapImage();
                        cover.BeginInit();
                        cover.StreamSource = rs.AsStreamForRead();
                        cover.CacheOption = BitmapCacheOption.OnLoad;
                        cover.EndInit();
                        cover.Freeze();
                    }
                    catch { cover = null; }
                }

                bool playing = SafePlaying(session);
                string key = $"{session.SourceAppUserModelId}|{title}|{artist}";

                if (key != _lastKey)
                {
                    _lastKey = key;
                    TrackChanged?.Invoke(title, artist, cover);
                }
                if (playing != _lastPlaying)
                {
                    _lastPlaying = playing;
                    PlaybackStateChanged?.Invoke(playing);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaSession] Update failed: {ex.Message}");
            }
            finally { _updating = false; }
        }

        public async Task TogglePlayPauseAsync()
        {
            var s = _current ?? PickSession();
            if (s == null) return;
            try
            {
                if (SafePlaying(s)) await s.TryPauseAsync();
                else await s.TryPlayAsync();
            }
            catch { }
        }

        public async Task NextAsync()
        {
            var s = _current ?? PickSession();
            if (s == null) return;
            try { await s.TrySkipNextAsync(); } catch { }
        }

        public async Task PrevAsync()
        {
            var s = _current ?? PickSession();
            if (s == null) return;
            try { await s.TrySkipPreviousAsync(); } catch { }
        }

        public void Dispose()
        {
            _disposed = true;
            _fallbackTimer?.Stop();
            if (_current != null)
            {
                try
                {
                    _current.MediaPropertiesChanged -= OnSessionMediaPropertiesChanged;
                    _current.PlaybackInfoChanged -= OnSessionPlaybackInfoChanged;
                }
                catch { }
            }
            if (_manager != null)
            {
                try { _manager.CurrentSessionChanged -= OnCurrentSessionChanged; } catch { }
            }
        }
    }
}