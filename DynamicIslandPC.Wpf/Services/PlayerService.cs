using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;

namespace DynamicIslandPC.Wpf.Services
{
    public enum RepeatMode { Off, One, All }

    public class PlayerService : IDisposable
    {
        private WaveOutEvent? _waveOut;
        private AudioFileReader? _reader;
        private readonly List<string> _playlist = new();
        private int _index = -1;
        private float _volume = 0.8f;
        private bool _manualStop;

        public RepeatMode Repeat { get; set; } = RepeatMode.All;
        public bool Shuffle { get; set; } = false;
        private readonly Random _rng = new();

        public event EventHandler? TrackChanged;
        public event EventHandler? StateChanged;

        public IReadOnlyList<string> Playlist => _playlist;
        public int CurrentIndex => _index;
        public string? CurrentTrack => _index >= 0 && _index < _playlist.Count ? _playlist[_index] : null;
        public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;
        public float Volume { get => _volume; set { _volume = Math.Clamp(value, 0f, 1f); if (_reader != null) _reader.Volume = _volume; StateChanged?.Invoke(this, EventArgs.Empty); } }

        public TimeSpan Position => _waveOut != null ? TimeSpan.FromMilliseconds(_waveOut.GetPosition()) : TimeSpan.Zero;
        public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

        public void AddTracks(string[] files)
        {
            bool wasEmpty = _playlist.Count == 0;
            int firstNew = -1;
            foreach (var f in files)
            {
                if (!File.Exists(f)) continue;
                if (!IsAudio(f)) continue;
                if (_playlist.Contains(f)) continue;
                if (firstNew < 0) firstNew = _playlist.Count;
                _playlist.Add(f);
            }
            if (firstNew < 0) return; // ничего не добавилось

            if (wasEmpty)
            {
                LoadAndPlay(0);
            }
            else
            {
                TrackChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void RemoveAt(int i)
        {
            if (i < 0 || i >= _playlist.Count) return;
            bool wasCurrent = i == _index;
            _playlist.RemoveAt(i);
            if (i < _index) _index--;
            if (wasCurrent)
            {
                StopInternal(disposeReader: true);
                if (_playlist.Count > 0) LoadAndPlay(Math.Min(_index, _playlist.Count - 1));
                else { _index = -1; TrackChanged?.Invoke(this, EventArgs.Empty); StateChanged?.Invoke(this, EventArgs.Empty); }
            }
            else
            {
                TrackChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void ClearPlaylist()
        {
            StopInternal(disposeReader: true);
            _playlist.Clear();
            _index = -1;
            TrackChanged?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void LoadAndPlay(int index)
        {
            if (index < 0 || index >= _playlist.Count) return;
            StopInternal(disposeReader: true);
            _index = index;
            _manualStop = false;
            try
            {
                _reader = new AudioFileReader(_playlist[index]);
                _reader.Volume = _volume;
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_reader);
                _waveOut.PlaybackStopped += OnPlaybackStopped;
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Player] Load failed: {ex.Message}");
                DisposeCurrent();
                _index = -1;
            }
            TrackChanged?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void TogglePlayPause()
        {
            if (_waveOut == null || _reader == null)
            {
                if (_playlist.Count > 0) LoadAndPlay(0);
                return;
            }
            if (IsPlaying) { _manualStop = true; _waveOut.Pause(); }
            else _waveOut.Play();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Play() { if (_waveOut != null && !IsPlaying) { _manualStop = false; _waveOut.Play(); StateChanged?.Invoke(this, EventArgs.Empty); } }
        public void Pause() { if (_waveOut != null && IsPlaying) { _manualStop = true; _waveOut.Pause(); StateChanged?.Invoke(this, EventArgs.Empty); } }

        public void Next()
        {
            if (_playlist.Count == 0) return;
            if (Shuffle && _playlist.Count > 1)
            {
                int n; do { n = _rng.Next(_playlist.Count); } while (n == _index);
                LoadAndPlay(n);
                return;
            }
            int next = _index + 1;
            if (next >= _playlist.Count)
            {
                if (Repeat == RepeatMode.All) next = 0;
                else { StopInternal(disposeReader: false); _waveOut?.Pause(); StateChanged?.Invoke(this, EventArgs.Empty); return; }
            }
            LoadAndPlay(next);
        }

        public void Prev()
        {
            if (_playlist.Count == 0) return;
            // если играем больше 3 сек — перемотать в начало текущего, иначе предыдущий
            if (Position > TimeSpan.FromSeconds(3)) { Seek(TimeSpan.Zero); return; }
            int prev = _index - 1;
            if (prev < 0) prev = Repeat == RepeatMode.All ? _playlist.Count - 1 : 0;
            LoadAndPlay(prev);
        }

        public void Seek(TimeSpan target)
        {
            if (_reader == null || _waveOut == null) return;
            var total = _reader.TotalTime;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (target > total) target = total;
            bool wasPlaying = IsPlaying;
            _manualStop = true;
            _waveOut.Stop();
            _reader.CurrentTime = target;
            _waveOut.Init(_reader);
            _manualStop = false;
            if (wasPlaying) _waveOut.Play();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (_manualStop) return;
            if (e.Exception != null) { Console.WriteLine($"[Player] Playback error: {e.Exception.Message}"); }
            if (Repeat == RepeatMode.One) { LoadAndPlay(_index); return; }
            Next();
        }

        private void StopInternal(bool disposeReader)
        {
            _manualStop = true;
            if (disposeReader) DisposeCurrent();
        }

        private void DisposeCurrent()
        {
            if (_waveOut != null) { _waveOut.PlaybackStopped -= OnPlaybackStopped; _waveOut.Dispose(); _waveOut = null; }
            if (_reader != null) { _reader.Dispose(); _reader = null; }
        }

        private static bool IsAudio(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp3" or ".wav" or ".wma" or ".aac" or ".m4a" or ".ogg" or ".flac";
        }

        public void Dispose() => DisposeCurrent();
    }
}