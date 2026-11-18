using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DynamicIslandPC.Wpf.Infrastructure;

namespace DynamicIslandPC.Wpf.Services
{
    /// <summary>
    /// VPN-тумблер в старой схеме: нажали кнопку на острове → запустилась
    /// программа подключения (rasdial или внешний .bat/.exe) → подождали ~секунду →
    /// опросили статус → перекрасили кнопку. Плюс фоновый опрос каждые 4 сек.
    /// </summary>
    public class VpnService : IDisposable
    {
        // ================= КОНФИГ =================
        /// Имя встроенного VPN-соединения Windows (ncpa.cpl → раздел VPN).
        public string ConnectionName { get; set; } = "MyVPN";

        /// Своя команда ПОДКЛЮЧЕНИЯ. Если null — используется rasdial.
        /// Пример: @"C:\Tools\vpn-connect.bat"  или  "wg-quick.exe up wg0"
        public string? ExternalConnectCommand { get; set; } = null;

        /// Своя команда ОТКЛЮЧЕНИЯ. Если null — rasdial "имя" /disconnect.
        public string? ExternalDisconnectCommand { get; set; } = null;

        /// Имя процесса, чьё присутствие означает «подключено» (опционально).
        /// Пример: "mullvad", "wireguard". Если null — статус через список rasdial.
        public string? StatusProcessName { get; set; } = null;

        /// Минимальное время тумбла, чтобы UI не мигал («программа шла секунду»).
        public int MinToggleDelayMs { get; set; } = 800;

        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(4);

        // ================= СОСТОЯНИЕ =================
        public bool IsConnected { get; private set; }
        public bool IsBusy { get; private set; }

        public event Action<bool>? StatusChanged;
        public event Action<string>? Error;

        private readonly DispatcherTimer _pollTimer = new();
        private readonly CancellationTokenSource _lifetimeCts = new();
        private readonly EventHandler _pollTickHandler;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private bool _disposed;
        private bool _started;

        public VpnService()
        {
            _pollTickHandler = async (s, e) => await SafeRefreshAsync(_lifetimeCts.Token);
        }

        public void Start()
        {
            if (_disposed || _started) return;
            _started = true;
            _pollTimer.Interval = PollInterval;
            _pollTimer.Tick += _pollTickHandler;
            _pollTimer.Start();
            _ = SafeRefreshAsync(_lifetimeCts.Token);
        }

        // ================= ТУМБЛЕР =================
        public async Task ToggleAsync()
        {
            if (IsBusy || _disposed) return;
            IsBusy = true;
            var sw = Stopwatch.StartNew();
            try
            {
                bool target = !IsConnected;
                Log.Info($"[VPN] toggle -> {(target ? "connect" : "disconnect")} ({DescribeMode()})");

                bool ok = target ? await ConnectCoreAsync(_lifetimeCts.Token) : await DisconnectCoreAsync(_lifetimeCts.Token);

                // первый опрос сразу, потом дожираем минимальную задержку
                await RefreshAsync(_lifetimeCts.Token);
                int left = MinToggleDelayMs - (int)sw.ElapsedMilliseconds;
                if (left > 0) await Task.Delay(left, _lifetimeCts.Token);
                await RefreshAsync(_lifetimeCts.Token);

                if (!ok)
                    Error?.Invoke(target
                        ? "Команда подключения вернула ошибку"
                        : "Команда отключения вернула ошибку");
            }
            catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Log.Error("[VPN] toggle", ex);
                Error?.Invoke("VPN: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
                if (!_disposed) StatusChanged?.Invoke(IsConnected);
            }
        }

        public Task ConnectAsync() => RunExplicitAsync(true);
        public Task DisconnectAsync() => RunExplicitAsync(false);

        private async Task RunExplicitAsync(bool connect)
        {
            if (IsBusy || _disposed) return;
            IsBusy = true;
            try
            {
                await (connect ? ConnectCoreAsync(_lifetimeCts.Token) : DisconnectCoreAsync(_lifetimeCts.Token));
                await Task.Delay(MinToggleDelayMs, _lifetimeCts.Token);
                await RefreshAsync(_lifetimeCts.Token);
            }
            catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { }
            catch (Exception ex) { Log.Error("[VPN] explicit", ex); }
            finally { IsBusy = false; if (!_disposed) StatusChanged?.Invoke(IsConnected); }
        }

        // ================= ЗАПУСК ПРОГРАММ =================
        private Task<bool> ConnectCoreAsync(CancellationToken cancellationToken) =>
            ExternalConnectCommand != null
                ? RunCommandLineAsync(ExternalConnectCommand, cancellationToken)
                : RunRasdialAsync(connect: true, cancellationToken);

        private Task<bool> DisconnectCoreAsync(CancellationToken cancellationToken) =>
            ExternalDisconnectCommand != null
                ? RunCommandLineAsync(ExternalDisconnectCommand, cancellationToken)
                : RunRasdialAsync(connect: false, cancellationToken);

        private async Task<bool> RunRasdialAsync(bool connect, CancellationToken cancellationToken)
        {
            var startInfo = CreateProcessStartInfo("rasdial.exe");
            startInfo.ArgumentList.Add(ConnectionName);
            if (!connect) startInfo.ArgumentList.Add("/disconnect");
            return await RunProcessAsync(startInfo, cancellationToken, "rasdial");
        }

        private static async Task<bool> RunCommandLineAsync(string command, CancellationToken cancellationToken)
        {
            var startInfo = CreateProcessStartInfo("cmd.exe");
            startInfo.Arguments = "/c " + command;
            return await RunProcessAsync(startInfo, cancellationToken, "cmd");
        }

        private static ProcessStartInfo CreateProcessStartInfo(string fileName) => new()
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        private static async Task<bool> RunProcessAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken, string logName)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var process = Process.Start(startInfo);
                if (process == null) return false;

                Task<string>? stdoutTask = startInfo.RedirectStandardOutput ? process.StandardOutput.ReadToEndAsync() : null;
                Task<string>? stderrTask = startInfo.RedirectStandardError ? process.StandardError.ReadToEndAsync() : null;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));

                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch (Exception ex) { Log.Warn($"[VPN] остановка процесса {logName}: {ex.Message}"); }

                    try { await process.WaitForExitAsync(); }
                    catch (Exception ex) { Log.Warn($"[VPN] ожидание завершения {logName}: {ex.Message}"); }

                    try
                    {
                        if (stdoutTask != null && stderrTask != null) await Task.WhenAll(stdoutTask, stderrTask);
                        else if (stdoutTask != null) await stdoutTask;
                        else if (stderrTask != null) await stderrTask;
                    }
                    catch (Exception ex) { Log.Warn($"[VPN] чтение вывода {logName}: {ex.Message}"); }

                    if (cancellationToken.IsCancellationRequested) throw;
                    throw new TimeoutException($"Процесс {logName} не завершился в течение 30 секунд.");
                }

                if (stdoutTask != null && stderrTask != null) await Task.WhenAll(stdoutTask, stderrTask);
                else if (stdoutTask != null) await stdoutTask;
                else if (stderrTask != null) await stderrTask;
                string stdout = stdoutTask == null ? string.Empty : await stdoutTask;
                string stderr = stderrTask == null ? string.Empty : await stderrTask;
                Log.Info($"[VPN] {logName} exit={process.ExitCode} out=\"{stdout.Trim()}\" err=\"{stderr.Trim()}\"");
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (TimeoutException ex) { Log.Error($"[VPN] {logName}", ex); return false; }
            catch (Exception ex) { Log.Error($"[VPN] {logName}", ex); return false; }
        }

        // ================= СТАТУС =================
        private async Task SafeRefreshAsync(CancellationToken cancellationToken)
        {
            try { await RefreshAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex) { Log.Warn("[VPN] refresh: " + ex.Message); }
        }

        private async Task RefreshAsync(CancellationToken cancellationToken)
        {
            if (_disposed || cancellationToken.IsCancellationRequested || !await _refreshGate.WaitAsync(0, cancellationToken)) return;
            try
            {
                bool now = StatusProcessName != null
                    ? IsProcessRunning(StatusProcessName)
                    : await QueryRasdialListAsync(cancellationToken);

                if (_disposed || cancellationToken.IsCancellationRequested) return;
                if (now != IsConnected)
                {
                    IsConnected = now;
                    Log.Info($"[VPN] status -> {(now ? "ON" : "OFF")}");
                    StatusChanged?.Invoke(IsConnected);
                }
            }
            finally { _refreshGate.Release(); }
        }

        private static bool IsProcessRunning(string processName)
        {
            Process[] processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcessesByName(processName);
                return processes.Length > 0;
            }
            catch (Exception ex)
            {
                Log.Warn($"[VPN] проверка процесса {processName}: {ex.Message}");
                return false;
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }

        /// rasdial без аргументов печатает активные соединения поимённо.
        private async Task<bool> QueryRasdialListAsync(CancellationToken cancellationToken)
        {
            try
            {
                var startInfo = CreateProcessStartInfo("rasdial.exe");
                var result = await RunProcessOutputAsync(startInfo, cancellationToken);
                return result.ExitCode == 0 && result.StandardOutput.IndexOf(ConnectionName, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (OperationCanceledException) { throw; }
            catch { return false; }
        }

        private static async Task<(int ExitCode, string StandardOutput)> RunProcessOutputAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var process = Process.Start(startInfo);
            if (process == null) return (-1, string.Empty);

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception ex) { Log.Warn($"[VPN] остановка проверки rasdial: {ex.Message}"); }
                try { await process.WaitForExitAsync(); } catch { }
                try { await Task.WhenAll(stdoutTask, stderrTask); } catch { }
                if (cancellationToken.IsCancellationRequested) throw;
                throw new TimeoutException("Проверка VPN через rasdial превысила 30 секунд.");
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            return (process.ExitCode, await stdoutTask);
        }

        private string DescribeMode()
        {
            if (StatusProcessName != null) return "process-watch: " + StatusProcessName;
            if (ExternalConnectCommand != null) return "external program";
            return "rasdial: " + ConnectionName;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetimeCts.Cancel();
            _pollTimer.Stop();
            _pollTimer.Tick -= _pollTickHandler;
            StatusChanged = null;
            Error = null;
            _lifetimeCts.Dispose();
            _refreshGate.Dispose();
        }
    }
}