using DynamicIslandPC.Wpf.Infrastructure;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIslandPC.Wpf.Services
{
    public class MediaSessionService : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private bool _disposed;

        public event Action<string, string, BitmapImage?>? TrackChanged;
        public event Action<bool>? PlaybackStateChanged;
        public event Action<TimeSpan, TimeSpan>? TimelineChanged;

        public async Task StartAsync(CancellationToken ct)
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                await AttachSessionAsync(_manager.GetCurrentSession());
                Log.Info("[MediaSession] запущен");
            }
            catch (Exception ex)
            {
                Log.Error("[MediaSession] start", ex);
            }
        }

        private async void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args)
        {
            await AttachSessionAsync(sender.GetCurrentSession());
        }

        private async Task AttachSessionAsync(GlobalSystemMediaTransportControlsSession? session)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }

            _session = session;
            if (_session == null) return;

            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            await RaiseAllAsync();
        }

        private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        {
            await RaiseTrackAsync();
        }

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            try
            {
                var info = sender.GetPlaybackInfo();
                bool isPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                PlaybackStateChanged?.Invoke(isPlaying);
            }
            catch (Exception ex)
            {
                Log.Warn("[MediaSession] playback: " + ex.Message);
            }
        }

        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            try
            {
                var tl = sender.GetTimelineProperties();
                TimelineChanged?.Invoke(tl.Position, tl.EndTime);
            }
            catch (Exception ex)
            {
                Log.Warn("[MediaSession] timeline: " + ex.Message);
            }
        }

        // ===== НОВОЕ: ручной опрос позиции (для YouTube/браузеров, где событие приходит редко) =====
        public void PollTimeline()
        {
            if (_session == null) return;
            try
            {
                var tl = _session.GetTimelineProperties();
                TimelineChanged?.Invoke(tl.Position, tl.EndTime);
            }
            catch { }
        }

        private async Task RaiseAllAsync()
        {
            await RaiseTrackAsync();
            try
            {
                if (_session != null)
                {
                    var info = _session.GetPlaybackInfo();
                    PlaybackStateChanged?.Invoke(info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
                    var tl = _session.GetTimelineProperties();
                    TimelineChanged?.Invoke(tl.Position, tl.EndTime);
                }
            }
            catch { }
        }

        private async Task RaiseTrackAsync()
        {
            if (_session == null) return;
            try
            {
                var props = await _session.TryGetMediaPropertiesAsync();
                BitmapImage? cover = null;
                if (props.Thumbnail != null)
                {
                    cover = await LoadThumbnailAsync(props.Thumbnail);
                }
                TrackChanged?.Invoke(props.Title, props.Artist, cover);
            }
            catch (Exception ex)
            {
                Log.Warn("[MediaSession] track: " + ex.Message);
            }
        }

        private static async Task<BitmapImage?> LoadThumbnailAsync(IRandomAccessStreamReference reference)
        {
            try
            {
                using var stream = await reference.OpenReadAsync();
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream.AsStreamForRead();
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        public async Task TogglePlayPauseAsync()
        {
            if (_session == null) return;
            try
            {
                var info = _session.GetPlaybackInfo();
                if (info.Controls.IsPlayPauseToggleEnabled)
                    await _session.TryTogglePlayPauseAsync();
                else if (info.Controls.IsPlayEnabled)
                    await _session.TryPlayAsync();
                else if (info.Controls.IsPauseEnabled)
                    await _session.TryPauseAsync();
            }
            catch (Exception ex) { Log.Warn("[MediaSession] toggle: " + ex.Message); }
        }

        public async Task PrevAsync()
        {
            if (_session == null) return;
            try { await _session.TrySkipPreviousAsync(); }
            catch (Exception ex) { Log.Warn("[MediaSession] prev: " + ex.Message); }
        }

        public async Task NextAsync()
        {
            if (_session == null) return;
            try { await _session.TrySkipNextAsync(); }
            catch (Exception ex) { Log.Warn("[MediaSession] next: " + ex.Message); }
        }

        public async Task SeekAsync(TimeSpan position)
        {
            if (_session == null) return;
            try
            {
                var info = _session.GetPlaybackInfo();
                if (info.Controls.IsPlaybackPositionEnabled)
                    await _session.TryChangePlaybackPositionAsync(position.Ticks);
            }
            catch (Exception ex) { Log.Warn("[MediaSession] seek: " + ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }
            if (_manager != null)
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
    }
}