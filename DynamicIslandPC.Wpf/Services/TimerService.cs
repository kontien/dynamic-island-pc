using System;
using System.Media;
using System.Windows.Threading;

namespace DynamicIslandPC.Wpf.Services
{
    public enum TimerPhase { Idle, Running, Paused, Finished }

    public class TimerService : IDisposable
    {
        private readonly DispatcherTimer _tick = new();
        private DateTime _deadline;
        private TimeSpan _pausedLeft;

        public TimerPhase Phase { get; private set; } = TimerPhase.Idle;
        public TimeSpan Total { get; private set; }
        public TimeSpan Remaining { get; private set; }
        public string Label { get; private set; } = "";

        public event Action? Changed;
        public event Action? Finished;

        public double Progress => Total.TotalSeconds > 0 ? Math.Clamp(1 - Remaining.TotalSeconds / Total.TotalSeconds, 0, 1) : 0;

        public TimerService()
        {
            _tick.Interval = TimeSpan.FromMilliseconds(200);
            _tick.Tick += (s, e) => Tick();
        }

        public void Start(TimeSpan duration, string label = "Таймер")
        {
            if (duration <= TimeSpan.Zero) return;
            Total = duration; Remaining = duration; Label = label;
            _deadline = DateTime.Now + duration;
            Phase = TimerPhase.Running;
            _tick.Start();
            Changed?.Invoke();
        }

        public void Toggle()
        {
            if (Phase == TimerPhase.Running) Pause();
            else if (Phase == TimerPhase.Paused) Resume();
        }

        public void Pause()
        {
            if (Phase != TimerPhase.Running) return;
            _pausedLeft = Remaining; Phase = TimerPhase.Paused; _tick.Stop(); Changed?.Invoke();
        }

        public void Resume()
        {
            if (Phase != TimerPhase.Paused) return;
            _deadline = DateTime.Now + _pausedLeft; Phase = TimerPhase.Running; _tick.Start(); Changed?.Invoke();
        }

        public void Add(TimeSpan delta)
        {
            if (Phase != TimerPhase.Running && Phase != TimerPhase.Paused) return;
            var left = Remaining + delta;
            if (left < TimeSpan.FromSeconds(1)) left = TimeSpan.FromSeconds(1);
            Total += delta; if (Total < left) Total = left;
            Remaining = left;
            if (Phase == TimerPhase.Running) _deadline = DateTime.Now + left;
            else _pausedLeft = left;
            Changed?.Invoke();
        }

        public void Reset()
        {
            _tick.Stop(); Phase = TimerPhase.Idle; Total = TimeSpan.Zero; Remaining = TimeSpan.Zero; Label = "";
            Changed?.Invoke();
        }

        private void Tick()
        {
            if (Phase != TimerPhase.Running) return;
            var left = _deadline - DateTime.Now;
            if (left <= TimeSpan.Zero)
            {
                Remaining = TimeSpan.Zero; Phase = TimerPhase.Finished; _tick.Stop();
                Changed?.Invoke();
                try { SystemSounds.Asterisk.Play(); } catch { }
                Finished?.Invoke();
                return;
            }
            Remaining = left; Changed?.Invoke();
        }

        public void Dispose() => _tick.Stop();
    }
}