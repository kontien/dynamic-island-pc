// Требует TargetFramework: net8.0-windows10.0.19041.0 (WinRT SMTC API)
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using DynamicIslandPC.Wpf.Infrastructure;

namespace DynamicIslandPC.Wpf.Services
{
    public class MediaSessionService : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private bool _disposed;
        private string? _lastTitle;
        private string? _lastArtist;

        public string? CurrentAppId { get; private set; }

        public event Action<string, string, BitmapImage?>? TrackChanged;
        public event Action<bool>? PlaybackStateChanged;
        public event Action<TimeSpan, TimeSpan>? TimelineChanged;

        public async Task StartAsync(CancellationToken ct)
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_disposed || ct.IsCancellationRequested) return;
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                await AttachSessionAsync(_manager.GetCurrentSession());
                Log.Info("[MediaSession] запущен");
            }
            catch (Exception ex) { Log.Error("[MediaSession] start", ex); }
        }

        private async void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args)
        {
            try { await AttachSessionAsync(sender.GetCurrentSession()); }
            catch (Exception ex) { Log.Warn("[MediaSession] switch: " + ex.Message); }
        }

        private async Task AttachSessionAsync(GlobalSystemMediaTransportControlsSession? session)
        {
            if (_disposed) return;

            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }

            _session = session;
            _lastTitle = null;
            _lastArtist = null;

            if (_session == null)
            {
                CurrentAppId = null;
                TrackChanged?.Invoke("", "", null);
                PlaybackStateChanged?.Invoke(false);
                TimelineChanged?.Invoke(TimeSpan.Zero, TimeSpan.Zero);
                return;
            }

            try { CurrentAppId = _session.SourceAppUserModelId; } catch { CurrentAppId = null; }

            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

            await RaiseTrackAsync();
            RaisePlayback();
            PollTimeline();
        }

        private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        { await RaiseTrackAsync(); }

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        { RaisePlayback(); }

        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        { PollTimeline(); }

        private void RaisePlayback()
        {
            if (_session == null) return;
            try
            {
                var info = _session.GetPlaybackInfo();
                PlaybackStateChanged?.Invoke(info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            }
            catch (Exception ex) { Log.Warn("[MediaSession] playback: " + ex.Message); }
        }

        public void PollTimeline()
        {
            if (_disposed || _session == null) return;
            try
            {
                var tl = _session.GetTimelineProperties();
                TimeSpan start = tl.StartTime;
                TimeSpan end = tl.EndTime;
                TimeSpan duration = end > start ? end - start : TimeSpan.Zero;
                TimeSpan position = tl.Position - start;
                if (position < TimeSpan.Zero) position = TimeSpan.Zero;
                if (duration > TimeSpan.Zero && position > duration) position = duration;
                TimelineChanged?.Invoke(position, duration);
            }
            catch { }
        }

        private async Task RaiseTrackAsync()
        {
            if (_disposed || _session == null) return;
            try
            {
                var props = await _session.TryGetMediaPropertiesAsync();
                if (_disposed) return;

                string title = props.Title ?? "";
                string artist = props.Artist ?? "";
                BitmapImage? cover = props.Thumbnail != null ? await LoadThumbnailAsync(props.Thumbnail) : null;

                if (title == _lastTitle && artist == _lastArtist && cover == null) return;
                _lastTitle = title;
                _lastArtist = artist;
                TrackChanged?.Invoke(title, artist, cover);
            }
            catch (Exception ex) { Log.Warn("[MediaSession] track: " + ex.Message); }
        }

        private static async Task<BitmapImage?> LoadThumbnailAsync(IRandomAccessStreamReference reference)
        {
            try
            {
                using var stream = await reference.OpenReadAsync();
                using var managed = stream.AsStreamForRead();
                var ms = new MemoryStream();
                await managed.CopyToAsync(ms);
                ms.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
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
                var c = _session.GetPlaybackInfo().Controls;
                if (c.IsPlayPauseToggleEnabled) await _session.TryTogglePlayPauseAsync();
                else if (c.IsPauseEnabled) await _session.TryPauseAsync();
                else if (c.IsPlayEnabled) await _session.TryPlayAsync();
            }
            catch (Exception ex) { Log.Warn("[MediaSession] toggle: " + ex.Message); }
        }

        public async Task PrevAsync()
        {
            if (_session == null) return;
            try
            {
                if (_session.GetPlaybackInfo().Controls.IsPreviousEnabled) await _session.TrySkipPreviousAsync();
            }
            catch (Exception ex) { Log.Warn("[MediaSession] prev: " + ex.Message); }
        }

        public async Task NextAsync()
        {
            if (_session == null) return;
            try
            {
                if (_session.GetPlaybackInfo().Controls.IsNextEnabled) await _session.TrySkipNextAsync();
            }
            catch (Exception ex) { Log.Warn("[MediaSession] next: " + ex.Message); }
        }

        public async Task SeekAsync(TimeSpan position)
        {
            if (_session == null) return;
            try
            {
                var info = _session.GetPlaybackInfo();
                if (!info.Controls.IsPlaybackPositionEnabled) return;
                var tl = _session.GetTimelineProperties();
                await _session.TryChangePlaybackPositionAsync((tl.StartTime + position).Ticks);
                PollTimeline();
            }
            catch (Exception ex) { Log.Warn("[MediaSession] seek: " + ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                }
                if (_manager != null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            }
            catch { }
            _session = null;
            _manager = null;
        }
    }
}