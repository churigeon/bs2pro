#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace FlydigiBS2Lite;

internal static class Program
{
    internal const string StopEventName = @"Local\FlydigiBS2Lite_Stop";
    internal const string PauseEventName = @"Local\FlydigiBS2Lite_Pause";
    internal const string ResumeEventName = @"Local\FlydigiBS2Lite_Resume";
    internal const string ReconnectEventName = @"Local\FlydigiBS2Lite_Reconnect";
    internal const string ShowEventName = @"Local\FlydigiBS2Lite_Show";
    internal static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlydigiBS2Lite");
    internal static readonly string StatusPath = Path.Combine(DataDirectory, "status.json");

    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(DataDirectory);

        bool showOnStart = args.Length == 0;

        if (args.Length > 0)
        {
            string command = args[0].ToLowerInvariant();
            if (command == "--status")
            {
                Console.WriteLine(File.Exists(StatusPath) ? File.ReadAllText(StatusPath, Encoding.UTF8) : "尚未生成状态");
                return 0;
            }
            if (command == "--stop") return Signal(StopEventName);
            if (command == "--pause") return Signal(PauseEventName);
            if (command == "--resume") return Signal(ResumeEventName);
            if (command == "--reconnect") return Signal(ReconnectEventName);
            if (command == "--show")
            {
                StartupRegistration.EnsureCurrentExecutable();
                int result = Signal(ShowEventName);
                if (result == 0) return 0;
                showOnStart = true;
            }
            else if (command == "--background") showOnStart = false;
        }

        StartupRegistration.EnsureCurrentExecutable();

        using var mutex = new Mutex(true, @"Local\FlydigiBS2Lite_Instance", out bool firstInstance);
        if (!firstInstance)
        {
            Signal(showOnStart ? ShowEventName : ResumeEventName);
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var context = new TrayContext(showOnStart);
        Application.Run(context);
        return 0;
    }

    private static int Signal(string eventName)
    {
        try
        {
            using EventWaitHandle ev = EventWaitHandle.OpenExisting(eventName);
            ev.Set();
            return 0;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            Console.WriteLine("BS2 Lite 当前没有运行");
            return 2;
        }
    }
}

internal static class AppIcon
{
    public static Icon Load()
    {
        try
        {
            using Icon? extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (extracted != null) return (Icon)extracted.Clone();
        }
        catch { }
        return (Icon)SystemIcons.Application.Clone();
    }
}

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FlydigiBS2Lite";

    public static void EnsureCurrentExecutable()
    {
        try
        {
            string command = $"\"{Application.ExecutablePath}\" --background";
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        catch { }
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _resumeItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly EventWaitHandle _stopEvent = new(false, EventResetMode.AutoReset, Program.StopEventName);
    private readonly EventWaitHandle _pauseEvent = new(false, EventResetMode.AutoReset, Program.PauseEventName);
    private readonly EventWaitHandle _resumeEvent = new(false, EventResetMode.AutoReset, Program.ResumeEventName);
    private readonly EventWaitHandle _reconnectEvent = new(false, EventResetMode.AutoReset, Program.ReconnectEventName);
    private readonly EventWaitHandle _showEvent = new(false, EventResetMode.AutoReset, Program.ShowEventName);
    private readonly CoolerController _controller = new();
    private ControlPanelForm? _panel;
    private bool _closing;

    public TrayContext(bool showOnStart)
    {
        _statusItem = new ToolStripMenuItem("正在启动……") { Enabled = false };
        var openPanel = new ToolStripMenuItem("打开控制面板", null, (_, _) => ShowPanel());
        _resumeItem = new ToolStripMenuItem("启用智能性能模式", null, (_, _) => _controller.Resume());
        _pauseItem = new ToolStripMenuItem("暂停散热", null, (_, _) => _controller.Pause());
        var reconnect = new ToolStripMenuItem("立即重连", null, (_, _) => _controller.Reconnect());
        var details = new ToolStripMenuItem("查看详细状态", null, (_, _) => ShowDetails());
        var exit = new ToolStripMenuItem("退出 BS2 Lite", null, (_, _) => CloseApp());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _statusItem, openPanel, new ToolStripSeparator(), _resumeItem, _pauseItem, reconnect,
            details, new ToolStripSeparator(), exit
        });

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            Text = "BS2 Lite - 正在启动",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowPanel();

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        _controller.Start();
        if (showOnStart) ShowPanel();
    }

    private void Tick()
    {
        if (_stopEvent.WaitOne(0)) { CloseApp(); return; }
        if (_pauseEvent.WaitOne(0)) _controller.Pause();
        if (_resumeEvent.WaitOne(0)) _controller.Resume();
        if (_reconnectEvent.WaitOne(0)) _controller.Reconnect();
        if (_showEvent.WaitOne(0)) ShowPanel();

        ControllerStatus s = _controller.Status;
        _statusItem.Text = s.Summary;
        _tray.Text = Truncate("BS2 Lite - " + s.Summary, 63);
        _pauseItem.Enabled = !s.Paused;
        _resumeItem.Enabled = s.Paused;
        _panel?.RefreshStatus(s);
    }

    private void ShowPanel()
    {
        if (_panel == null || _panel.IsDisposed)
        {
            _panel = new ControlPanelForm(_controller);
            _panel.FormClosed += (_, _) => _panel = null;
        }
        _panel.Show();
        if (_panel.WindowState == FormWindowState.Minimized) _panel.WindowState = FormWindowState.Normal;
        _panel.Activate();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value.Substring(0, max);

    private void ShowDetails()
    {
        ControllerStatus s = _controller.Status;
        string text =
            $"连接：{(s.Connected ? "已连接" : "未连接")}\n" +
            $"模式：{(s.Paused ? "已暂停" : s.ModeText)}\n" +
            $"CPU：{FormatTemp(s.CpuTemperature)}\n" +
            $"GPU：{FormatTemp(s.GpuTemperature)}\n" +
            $"当前指令：{s.TargetRpm} rpm\n" +
            $"供电：{s.PowerMode}\n\n{s.Message}";
        MessageBox.Show(text, "BS2 Lite 状态", MessageBoxButtons.OK, s.Connected ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private static string FormatTemp(double? value) => value.HasValue ? $"{value.Value:0} °C" : "未读取到";

    private void CloseApp()
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        _controller.Stop();
        _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _panel?.Dispose();
            _controller.Dispose();
            _tray.Dispose();
            _timer.Dispose();
            _stopEvent.Dispose();
            _pauseEvent.Dispose();
            _resumeEvent.Dispose();
            _reconnectEvent.Dispose();
            _showEvent.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class CoolerController : IDisposable
{
    private const ushort ServiceUuid = 0xFFF0;
    private const ushort NotifyUuid = 0xFFF1;
    private const ushort WriteUuid = 0xFFF2;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _logPath = Path.Combine(Program.DataDirectory, "BS2Lite.log");
    private WinRtBle? _ble;
    private Computer? _computer;
    private Task? _worker;
    private volatile bool _paused;
    private volatile bool _forceReconnect;
    private ControllerStatus _status = ControllerStatus.Starting();
    private int _lastRpm;
    private int _powerMode = 3;
    private DateTime _lastPowerQuery = DateTime.MinValue;
    private LiteSettings _settings = SettingsStore.Load();
    private volatile bool _applyRequested;
    private int _stopped;
    private bool _configured;
    private int _fanCommandFailures;

    internal static readonly int[] FixedSpeeds = { 1300, 1700, 1900, 2100, 2400, 2700, 2800, 3000, 3300, 3500, 3700, 4000 };
    internal static readonly string[] FixedNames = { "静音1档", "静音2档", "静音3档", "标准1档", "标准2档", "标准3档", "强劲1档", "强劲2档", "强劲3档", "超频1档", "超频2档", "超频3档" };

    public ControllerStatus Status
    {
        get { lock (_gate) return _status.Copy(); }
    }

    public LiteSettings Settings
    {
        get { lock (_gate) return _settings.Clone(); }
    }

    public void UpdateSettings(LiteSettings settings)
    {
        settings.Normalize();
        lock (_gate) _settings = settings.Clone();
        SettingsStore.Save(settings);
        _applyRequested = true;
        SetStatus(s => s.Message = "设置已保存，正在应用");
    }

    public void Start()
    {
        SettingsStore.Save(Settings);
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }
    public void Pause() { _paused = true; SetStatus(s => { s.Paused = true; s.Message = "已暂停；右键托盘图标可恢复"; }); }
    public void Resume() { _paused = false; _forceReconnect = true; SetStatus(s => { s.Paused = false; s.Message = "正在恢复智能性能模式"; }); }
    public void Reconnect() { _forceReconnect = true; SetStatus(s => s.Message = "正在重新连接底座"); }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _cts.Cancel();
        try { _worker?.Wait(2500); } catch { }
        Disconnect(sendStop: true);
    }

    private async Task RunAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Program.DataDirectory);
        InitializeTemperatureReader();
        Log("BS2 Lite started");

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_forceReconnect)
                {
                    _forceReconnect = false;
                    Disconnect(sendStop: false);
                }

                if (!IsConnected() || !_configured)
                {
                    if (_paused)
                    {
                        SetStatus(s => { s.Connected = false; s.Paused = true; s.Message = "已暂停"; });
                        await Task.Delay(1000, token);
                        continue;
                    }

                    if (!ConnectAndConfigure())
                    {
                        SetStatus(s => { s.Connected = false; s.Message = "未找到 BS2 Pro，5 秒后自动重试"; });
                        await Task.Delay(5000, token);
                        continue;
                    }
                }

                if (_paused)
                {
                    TryCommand(36, null, 2);
                    SetStatus(s => { s.Paused = true; s.TargetRpm = 0; s.Message = "已暂停散热"; });
                    await Task.Delay(1000, token);
                    continue;
                }

                LiteSettings settings = Settings;
                if (_applyRequested)
                {
                    if (ApplyDeviceSettings(settings)) _applyRequested = false;
                    else Log("Applying saved settings will be retried");
                }

                if ((DateTime.UtcNow - _lastPowerQuery).TotalSeconds >= 15)
                    QueryPowerMode();

                (double? cpu, double? gpu) = ReadTemperatures();
                double controlTemp = Math.Max(cpu ?? 0, gpu ?? cpu ?? 0);
                int target;
                if (settings.ControlMode == 0)
                {
                    target = controlTemp > 0 ? GetSmartRpm(controlTemp, _powerMode, settings) : 2700;
                    if (target != _lastRpm)
                    {
                        byte[] speed = { (byte)(target & 0xFF), (byte)(target >> 8) };
                        if (TryCommand(33, speed, 10))
                        {
                            _lastRpm = target;
                            _fanCommandFailures = 0;
                        }
                        else
                        {
                            _fanCommandFailures++;
                            Log("Fan speed command will be retried without reconnecting");
                            if (_fanCommandFailures >= 3)
                            {
                                SetStatus(s => { s.Connected = false; s.Message = "转速回执中断，正在重新握手"; });
                                Disconnect(sendStop: false);
                                await Task.Delay(1500, token);
                                continue;
                            }
                        }
                    }
                }
                else
                {
                    target = FixedSpeeds[settings.FixedLevel];
                    if (_lastRpm != target)
                    {
                        if (ApplyFixedSpeed(settings.FixedLevel)) _lastRpm = target;
                        else _applyRequested = true;
                    }
                }

                SetStatus(s =>
                {
                    s.Connected = true;
                    s.Paused = false;
                    s.CpuTemperature = cpu;
                    s.GpuTemperature = gpu;
                    s.TargetRpm = _lastRpm > 0 ? _lastRpm : target;
                    s.PowerMode = PowerModeName(_powerMode);
                    s.ModeText = settings.ControlMode == 0 ? "智能变频 · " + settings.ProfileName : "固定转速 · " + FixedNames[settings.FixedLevel];
                    s.Message = controlTemp > 0 ? "运行正常；设置会自动保存并在重连后恢复" : "未读取到温度，已使用安全转速 2700 rpm";
                });
                await Task.Delay(1000, token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log("Loop error: " + ex.Message);
                SetStatus(s => { s.Connected = false; s.Message = "连接异常，正在自动恢复：" + ex.Message; });
                Disconnect(sendStop: false);
                try { await Task.Delay(3000, token); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private bool ConnectAndConfigure()
    {
        SetStatus(s => { s.Connected = false; s.Message = "正在搜索 FlyDigi BS2PRO"; });
        try
        {
            try { _ble?.Close(); } catch { }
            _ble = new WinRtBle();
            if (!_ble.Connect(ServiceUuid, NotifyUuid, WriteUuid)) return false;

            if (!TryCommand(5, null, 5)) throw new IOException("唤醒底座失败");
            QueryPowerMode();
            TryCommand(70, new byte[] { 0 }, 3);
            if (!ApplyDeviceSettings(Settings)) throw new IOException("应用已保存设置失败");
            _applyRequested = false;
            _configured = true;
            _fanCommandFailures = 0;
            Log("Connected and configured");
            SetStatus(s => { s.Connected = true; s.Paused = false; s.Message = "已连接，正在应用性能曲线"; });
            return true;
        }
        catch (Exception ex)
        {
            Log("Connect failed: " + ex.Message);
            Disconnect(sendStop: false);
            return false;
        }
    }

    private void QueryPowerMode()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            byte[]? payload = SendCommand(7, null);
            if (payload != null && payload.Length > 0 && payload[0] >= 1 && payload[0] <= 3)
            {
                _powerMode = payload[0];
                Log($"Power mode={_powerMode}");
                break;
            }
            Thread.Sleep(50);
        }
        _lastPowerQuery = DateTime.UtcNow;
    }

    private bool ApplyDeviceSettings(LiteSettings settings)
    {
        bool ok = true;
        ok &= TryCommand(72, new byte[] { (byte)(settings.GearLed ? 1 : 0) }, 5);
        ok &= TryCommand(13, new byte[] { (byte)settings.SmartStartStop }, 5);
        ok &= TryCommand(12, new byte[] { (byte)(settings.PowerOnAutoStart ? 1 : 2) }, 5);
        if (settings.ControlMode == 0)
        {
            ok &= TryCommand(35, null, 5);
            _lastRpm = 0;
        }
        else
        {
            TryCommand(36, null, 3);
            ok &= ApplyFixedSpeed(settings.FixedLevel);
            if (ok) _lastRpm = FixedSpeeds[settings.FixedLevel];
        }
        return ok;
    }

    private bool ApplyFixedSpeed(int level)
    {
        level = Math.Max(0, Math.Min(level, FixedSpeeds.Length - 1));
        int mode = level / 3;
        int speed = FixedSpeeds[level];
        byte[] payload = { (byte)mode, (byte)(speed & 0xFF), (byte)(speed >> 8) };
        return TryCommand(38, payload, 8);
    }

    private bool TryCommand(byte command, byte[]? payload, int attempts)
    {
        for (int i = 0; i < attempts; i++)
        {
            byte[]? response = SendCommand(command, payload);
            if (response != null)
            {
                Log($"ACK cmd={command:X2} payload={BitConverter.ToString(response)}");
                if (response.Length == 0 || response[0] == 1 || response[0] == 3) return true;
            }
            Thread.Sleep(30);
        }
        Log($"Command failed cmd={command:X2}");
        return false;
    }

    private byte[]? SendCommand(byte command, byte[]? payload)
    {
        if (_ble == null || !_ble.IsConnected) return null;
        payload ??= Array.Empty<byte>();
        byte length = (byte)(2 + payload.Length);
        byte[] frame = new byte[2 + length + 1];
        frame[0] = 0x5A;
        frame[1] = 0xA5;
        frame[2] = command;
        frame[3] = length;
        if (payload.Length > 0) System.Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
        byte checksum = 0;
        for (int i = 2; i < frame.Length - 1; i++) unchecked { checksum += frame[i]; }
        frame[frame.Length - 1] = checksum;

        Thread.Sleep(10);
        if (!_ble.Write(frame)) return null;
        try
        {
            for (int i = 0; i < 2; i++)
            {
                byte[] response = _ble.Read(600);
                if (response.Length >= 6 && response[0] == 0x5A && response[1] == 0xA5 && response[2] == command)
                    return response.Skip(4).Take(response[3] - 2).ToArray();
                Log($"Unexpected frame for cmd={command:X2}: {BitConverter.ToString(response)}");
            }
        }
        catch (TimeoutException) { }
        return null;
    }

    private bool IsConnected() => _ble != null && _ble.IsConnected;

    private void Disconnect(bool sendStop)
    {
        try { if (sendStop && IsConnected()) TryCommand(36, null, 2); } catch { }
        try { _ble?.Close(); } catch { }
        _ble = null;
        _lastRpm = 0;
        _configured = false;
        _fanCommandFailures = 0;
    }

    private void InitializeTemperatureReader()
    {
        try
        {
            _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            _computer.Open();
        }
        catch (Exception ex)
        {
            Log("Temperature reader init failed: " + ex.Message);
            _computer = null;
        }
    }

    private (double? Cpu, double? Gpu) ReadTemperatures()
    {
        if (_computer == null) return (null, null);
        try
        {
            var visitor = new UpdateVisitor();
            _computer.Accept(visitor);
            double? cpu = null;
            double? gpu = null;
            foreach (IHardware hardware in _computer.Hardware.SelectMany(Flatten))
            {
                foreach (ISensor sensor in hardware.Sensors)
                {
                    if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue) continue;
                    double value = sensor.Value.Value;
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        if (sensor.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 || !cpu.HasValue)
                            cpu = Math.Max(cpu ?? value, value);
                    }
                    else if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        if (sensor.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 || !gpu.HasValue)
                            gpu = Math.Max(gpu ?? value, value);
                    }
                }
            }
            if (!gpu.HasValue) gpu = cpu;
            return (cpu, gpu);
        }
        catch (Exception ex)
        {
            Log("Temperature read failed: " + ex.Message);
            return (null, null);
        }
    }

    private static IEnumerable<IHardware> Flatten(IHardware root)
    {
        yield return root;
        foreach (IHardware sub in root.SubHardware)
            foreach (IHardware nested in Flatten(sub)) yield return nested;
    }

    private static int GetSmartRpm(double temperature, int powerMode, LiteSettings settings)
    {
        int[] temperatures = { 30, 40, 50, 60, 70, 80, 90, 100 };
        int[] rpms;
        if (settings.SmartProfile == 2)
        {
            rpms = settings.CustomCurve.ToArray();
        }
        else if (settings.SmartProfile == 0)
        {
            rpms = powerMode switch
            {
                3 => new[] { 1300, 1300, 1700, 2100, 2700, 3000, 3300, 3500 },
                2 => new[] { 1300, 1300, 1700, 2100, 2700, 3000, 3300, 3300 },
                _ => new[] { 1300, 1300, 1700, 2100, 2400, 2700, 2700, 2700 }
            };
        }
        else
        {
            rpms = powerMode switch
            {
                3 => new[] { 1700, 1700, 2100, 2700, 3300, 3500, 4000, 4000 },
                2 => new[] { 1700, 1700, 2100, 2700, 3300, 3300, 3300, 3300 },
                _ => new[] { 1300, 1700, 2100, 2400, 2700, 2700, 2700, 2700 }
            };
        }
        int powerLimit = powerMode == 3 ? 4000 : powerMode == 2 ? 3300 : 2700;
        int result = Math.Min(rpms[0], powerLimit);
        for (int i = 0; i < temperatures.Length; i++)
        {
            if (temperature < temperatures[i]) break;
            result = Math.Min(rpms[i], powerLimit);
        }
        return result;
    }

    private static string PowerModeName(int mode) => mode switch { 3 => "PD 20V（最高 4000 rpm）", 2 => "QC 9V（最高 3300 rpm）", _ => "5V（最高 2700 rpm）" };

    private void SetStatus(Action<ControllerStatus> update)
    {
        ControllerStatus snapshot;
        lock (_gate)
        {
            update(_status);
            _status.LastUpdated = DateTime.Now;
            snapshot = _status.Copy();
        }
        try { File.WriteAllText(Program.StatusPath, snapshot.ToJson(), new UTF8Encoding(false)); } catch { }
    }

    private void Log(string message)
    {
        try
        {
            if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 1024 * 1024) File.WriteAllText(_logPath, string.Empty);
            File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}", new UTF8Encoding(false));
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        try { _computer?.Close(); } catch { }
        _computer = null;
        _cts.Dispose();
    }
}

internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware) { hardware.Update(); foreach (IHardware sub in hardware.SubHardware) sub.Accept(this); }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}

internal sealed class WinRtBle : IDisposable
{
    // The paired BS2 Pro address from Windows: BTHLE\DEV_E466E514052A.
    private const ulong DeviceAddress = 0xE466E514052AUL;
    private readonly ConcurrentQueue<byte[]> _received = new();
    private readonly AutoResetEvent _receivedSignal = new(false);
    private BluetoothLEDevice? _device;
    private GattCharacteristic? _writeCharacteristic;
    private GattCharacteristic? _notifyCharacteristic;

    public bool IsConnected => _device != null && _device.ConnectionStatus != BluetoothConnectionStatus.Disconnected;

    public bool Connect(ushort serviceUuid, ushort notifyUuid, ushort writeUuid)
    {
        try
        {
            _device = Wait(BluetoothLEDevice.FromBluetoothAddressAsync(DeviceAddress), 5000);
            if (_device == null) return false;
            Guid service = ShortUuid(serviceUuid);
            GattDeviceServicesResult services = Wait(_device.GetGattServicesForUuidAsync(service), 5000);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0) return false;

            GattDeviceService selectedService = services.Services[0];
            GattCharacteristicsResult characteristics = Wait(selectedService.GetCharacteristicsAsync(), 5000);
            if (characteristics.Status != GattCommunicationStatus.Success) return false;
            _writeCharacteristic = characteristics.Characteristics.FirstOrDefault(c => c.Uuid == ShortUuid(writeUuid));
            _notifyCharacteristic = characteristics.Characteristics.FirstOrDefault(c => c.Uuid == ShortUuid(notifyUuid));
            if (_writeCharacteristic == null || _notifyCharacteristic == null) return false;

            _notifyCharacteristic.ValueChanged += OnValueChanged;
            GattCommunicationStatus notifyStatus = Wait(
                _notifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify), 5000);
            if (notifyStatus != GattCommunicationStatus.Success)
            {
                Close();
                return false;
            }
            return true;
        }
        catch
        {
            Close();
            return false;
        }
    }

    public bool Write(byte[] frame)
    {
        if (_writeCharacteristic == null || !IsConnected) return false;
        try
        {
            using var writer = new DataWriter();
            writer.WriteBytes(frame);
            GattCommunicationStatus status = Wait(
                _writeCharacteristic.WriteValueWithResultAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse), 2500).Status;
            return status == GattCommunicationStatus.Success;
        }
        catch { return false; }
    }

    public byte[] Read(int timeoutMilliseconds)
    {
        if (!_receivedSignal.WaitOne(timeoutMilliseconds)) throw new TimeoutException();
        return _received.TryDequeue(out byte[]? data) ? data : Array.Empty<byte>();
    }

    public void Close()
    {
        if (_notifyCharacteristic != null)
        {
            try { _notifyCharacteristic.ValueChanged -= OnValueChanged; } catch { }
        }
        _notifyCharacteristic = null;
        _writeCharacteristic = null;
        try { _device?.Dispose(); } catch { }
        _device = null;
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        using DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
        var bytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(bytes);
        // 0xEF is the cooler's periodic telemetry frame, not an acknowledgement.
        // The vendor client also discards it from the command-response queue.
        if (bytes.Length >= 3 && bytes[2] == 0xEF) return;
        _received.Enqueue(bytes);
        _receivedSignal.Set();
    }

    private static Guid ShortUuid(ushort shortUuid) => new Guid($"0000{shortUuid:x4}-0000-1000-8000-00805f9b34fb");

    private static T Wait<T>(IAsyncOperation<T> operation, int timeoutMilliseconds)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (operation.Status == AsyncStatus.Started && DateTime.UtcNow < deadline) Thread.Sleep(10);
        if (operation.Status != AsyncStatus.Completed) throw new TimeoutException(operation.Status.ToString());
        return operation.GetResults();
    }

    public void Dispose()
    {
        Close();
        _receivedSignal.Dispose();
    }
}

internal sealed class NativeBle : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ConnectionChanged(IntPtr handle, byte connected);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ReadNotification(IntPtr context, ref ReadBuffer buffer, ulong length);
    [StructLayout(LayoutKind.Sequential)]
    private struct ReadBuffer
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)]
        public byte[] Data;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)]
        public byte[] Name;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)]
        public byte[] DeviceId;
        public int Rssi;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfos
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public DeviceInfo[] Infos;
    }

    [DllImport("lib/x64/WCHBLEDLL", CallingConvention = CallingConvention.Winapi)]
    private static extern void WCHBLEInit();
    [DllImport("lib/x64/WCHBLEDLL", CallingConvention = CallingConvention.Winapi)]
    private static extern bool WCHBLEIsBluetoothOpened();
    [DllImport("lib/x64/WCHBLEDLL", CallingConvention = CallingConvention.Winapi)]
    private static extern void WCHBLEEnumDevice(uint scanMilliseconds, string deviceIdFilter, ref DeviceInfos devices, ref uint count);
    [DllImport("lib/x64/WCHBLEDLL", CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr WCHBLEOpenDevice(string deviceId, ConnectionChanged callback);
    [DllImport("lib/x64/WCHBLEDLL", CallingConvention = CallingConvention.Winapi)]
    private static extern void WCHBLECloseDevice(IntPtr handle);
    [DllImport("lib/x64/WCHBLEDLL", CallingConvention = CallingConvention.Winapi)]
    private static extern byte WCHBLERegisterReadNotify(IntPtr handle, ushort serviceUuid, ushort characteristicUuid, ReadNotification? callback, IntPtr context);
    [DllImport("lib/x64/WCHBLEDLL", CallingConvention = CallingConvention.Winapi)]
    private static extern byte WCHBLEWriteCharacteristic(IntPtr handle, ushort serviceUuid, ushort characteristicUuid, bool withResponse, byte[] buffer, uint length);

    private readonly ConcurrentQueue<byte[]> _received = new();
    private readonly AutoResetEvent _receivedSignal = new(false);
    private readonly ConnectionChanged _connectionChanged;
    private readonly ReadNotification _readNotification;
    private IntPtr _handle;
    private ushort _serviceUuid;
    private ushort _notifyUuid;
    private ushort _writeUuid;

    public NativeBle()
    {
        _connectionChanged = OnConnectionChanged;
        _readNotification = OnReadNotification;
    }

    public bool IsConnected => _handle != IntPtr.Zero;

    public static string ScanDevices()
    {
        try
        {
            WCHBLEInit();
            uint count = 20;
            DeviceInfos devices = default;
            WCHBLEEnumDevice(2500, string.Empty, ref devices, ref count);
            if (devices.Infos == null || count == 0) return "未扫描到 BLE 广播";
            var items = new List<string>();
            for (int i = 0; i < Math.Min((int)count, devices.Infos.Length); i++)
            {
                DeviceInfo info = devices.Infos[i];
                string name = Encoding.UTF8.GetString(info.Name ?? Array.Empty<byte>()).TrimEnd('\0');
                string id = Encoding.UTF8.GetString(info.DeviceId ?? Array.Empty<byte>()).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(id)) items.Add($"{name} [{id}] {info.Rssi}dBm");
            }
            return items.Count == 0 ? "未扫描到 BLE 广播" : string.Join("；", items);
        }
        catch (Exception ex)
        {
            return "扫描异常：" + ex.GetType().Name;
        }
    }

    public bool Connect(string deviceId, ushort serviceUuid, ushort notifyUuid, ushort writeUuid)
    {
        WCHBLEInit();
        if (!WCHBLEIsBluetoothOpened()) return false;
        _serviceUuid = serviceUuid;
        _notifyUuid = notifyUuid;
        _writeUuid = writeUuid;
        _handle = WCHBLEOpenDevice(deviceId, _connectionChanged);
        if (_handle == IntPtr.Zero) return false;
        if (WCHBLERegisterReadNotify(_handle, _serviceUuid, _notifyUuid, _readNotification, _handle) != 0)
        {
            Close();
            return false;
        }
        return true;
    }

    public bool Write(byte[] frame) => IsConnected && WCHBLEWriteCharacteristic(_handle, _serviceUuid, _writeUuid, false, frame, (uint)frame.Length) == 0;

    public byte[] Read(int timeoutMilliseconds)
    {
        if (!_receivedSignal.WaitOne(timeoutMilliseconds)) throw new TimeoutException();
        return _received.TryDequeue(out byte[]? data) ? data : Array.Empty<byte>();
    }

    public void Close()
    {
        IntPtr handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle == IntPtr.Zero) return;
        try { WCHBLERegisterReadNotify(handle, _serviceUuid, _notifyUuid, null, IntPtr.Zero); } catch { }
        try { WCHBLECloseDevice(handle); } catch { }
    }

    private void OnConnectionChanged(IntPtr handle, byte connected)
    {
        if (connected == 0 && handle == _handle) _handle = IntPtr.Zero;
    }

    private void OnReadNotification(IntPtr context, ref ReadBuffer buffer, ulong length)
    {
        byte[]? source = buffer.Data;
        int count = (int)Math.Min((ulong)(source?.Length ?? 0), length);
        if (count <= 0) return;
        var data = new byte[count];
        System.Buffer.BlockCopy(source!, 0, data, 0, count);
        _received.Enqueue(data);
        _receivedSignal.Set();
    }

    public void Dispose()
    {
        Close();
        _receivedSignal.Dispose();
    }
}

internal sealed class LiteSettings
{
    public int ControlMode { get; set; } = 0; // 0=smart, 1=fixed
    public int SmartProfile { get; set; } = 1; // 0=silent, 1=performance, 2=custom
    public int FixedLevel { get; set; } = 5;
    public bool GearLed { get; set; } = true;
    public int SmartStartStop { get; set; } = 1; // off, instant, delayed
    public bool PowerOnAutoStart { get; set; } = true;
    public int[] CustomCurve { get; set; } = { 1700, 1700, 2100, 2700, 3300, 3500, 4000, 4000 };

    [System.Text.Json.Serialization.JsonIgnore]
    public string ProfileName => SmartProfile switch { 0 => "静音模式", 2 => "自定义模式", _ => "性能模式" };

    public void Normalize()
    {
        ControlMode = Math.Max(0, Math.Min(1, ControlMode));
        SmartProfile = Math.Max(0, Math.Min(2, SmartProfile));
        FixedLevel = Math.Max(0, Math.Min(CoolerController.FixedSpeeds.Length - 1, FixedLevel));
        SmartStartStop = Math.Max(0, Math.Min(2, SmartStartStop));
        if (CustomCurve == null || CustomCurve.Length != 8)
            CustomCurve = new[] { 1700, 1700, 2100, 2700, 3300, 3500, 4000, 4000 };
        for (int i = 0; i < CustomCurve.Length; i++)
        {
            CustomCurve[i] = Math.Max(1300, Math.Min(4000, CustomCurve[i]));
            if (i > 0 && CustomCurve[i] < CustomCurve[i - 1]) CustomCurve[i] = CustomCurve[i - 1];
        }
    }

    public LiteSettings Clone() => new()
    {
        ControlMode = ControlMode,
        SmartProfile = SmartProfile,
        FixedLevel = FixedLevel,
        GearLed = GearLed,
        SmartStartStop = SmartStartStop,
        PowerOnAutoStart = PowerOnAutoStart,
        CustomCurve = CustomCurve.ToArray()
    };
}

internal static class SettingsStore
{
    private static readonly string PathName = Path.Combine(Program.DataDirectory, "settings.json");

    public static LiteSettings Load()
    {
        try
        {
            LiteSettings? settings = File.Exists(PathName)
                ? JsonSerializer.Deserialize<LiteSettings>(File.ReadAllText(PathName, Encoding.UTF8))
                : null;
            settings ??= new LiteSettings();
            settings.Normalize();
            return settings;
        }
        catch { return new LiteSettings(); }
    }

    public static void Save(LiteSettings settings)
    {
        Directory.CreateDirectory(Program.DataDirectory);
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(PathName, json, new UTF8Encoding(false));
    }
}

internal sealed class ControlPanelForm : Form
{
    private static readonly Color Background = Color.FromArgb(20, 21, 23);
    private static readonly Color Surface = Color.FromArgb(31, 33, 36);
    private static readonly Color Accent = Color.FromArgb(245, 160, 45);
    private static readonly Color TextMain = Color.FromArgb(242, 242, 242);
    private static readonly Color TextMuted = Color.FromArgb(160, 164, 170);
    private readonly CoolerController _controller;
    private readonly Label _status = MakeLabel("正在连接…", 17, TextMain);
    private readonly Label _reading = MakeLabel("--°C  ·  -- rpm", 28, TextMain);
    private readonly Label _power = MakeLabel("供电检测中", 10, TextMuted);
    private readonly Button _smartButton = MakeButton("智能变频");
    private readonly Button _fixedButton = MakeButton("固定转速");
    private readonly ComboBox _profile = MakeCombo();
    private readonly Panel _summaryPanel = new() { BackColor = Surface };
    private readonly Label _smartSummary = MakeLabel(string.Empty, 9.5f, TextMain);
    private readonly Panel _customPanel = new() { BackColor = Surface };
    private readonly NumericUpDown[] _curveInputs = new NumericUpDown[8];
    private readonly Panel _fixedPanel = new() { BackColor = Surface };
    private readonly TrackBar _fixedLevel = new() { Minimum = 0, Maximum = 11, TickFrequency = 1, SmallChange = 1, LargeChange = 1, AutoSize = false, Height = 40 };
    private readonly Label _fixedValue = MakeLabel(string.Empty, 12, TextMain);
    private readonly CheckBox _gearLed = MakeCheck("档位灯显示");
    private readonly ComboBox _smartStart = MakeCombo();
    private readonly CheckBox _powerOn = MakeCheck("通电自启动");
    private readonly Label _saved = MakeLabel(string.Empty, 10, TextMuted);
    private int _controlMode;
    private bool _reallyClosing;

    public ControlPanelForm(CoolerController controller)
    {
        _controller = controller;
        Text = "BS2 Lite";
        Icon = AppIcon.Load();
        float layoutScale = GetDesktopDpiScale();
        ClientSize = new System.Drawing.Size((int)Math.Round(620 * layoutScale), (int)Math.Round(750 * layoutScale));
        MinimumSize = new System.Drawing.Size(ClientSize.Width, ClientSize.Height);
        MaximizeBox = false;
        BackColor = Background;
        ForeColor = TextMain;
        Font = new Font("Microsoft YaHei UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BuildLayout();
        if (Math.Abs(layoutScale - 1f) > 0.01f) ScaleChildBounds(this, layoutScale);
        LoadSettings(controller.Settings);
        FormClosing += (_, e) => { if (!_reallyClosing) { e.Cancel = true; Hide(); } };
    }

    private static float GetDesktopDpiScale()
    {
        using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
        return Math.Max(1f, graphics.DpiX / 96f);
    }

    private static void ScaleChildBounds(Control parent, float scale)
    {
        foreach (Control control in parent.Controls)
        {
            control.SetBounds(
                (int)Math.Round(control.Left * scale),
                (int)Math.Round(control.Top * scale),
                (int)Math.Round(control.Width * scale),
                (int)Math.Round(control.Height * scale));
            if (control.HasChildren) ScaleChildBounds(control, scale);
        }
    }

    private void BuildLayout()
    {
        _status.SetBounds(28, 18, 564, 38);
        _reading.SetBounds(28, 58, 564, 58);
        _power.SetBounds(30, 118, 562, 24);
        Controls.AddRange(new Control[] { _status, _reading, _power });

        _smartButton.SetBounds(28, 154, 278, 46);
        _fixedButton.SetBounds(314, 154, 278, 46);
        _smartButton.Click += (_, _) => SetMode(0);
        _fixedButton.Click += (_, _) => SetMode(1);
        Controls.AddRange(new Control[] { _smartButton, _fixedButton });

        var profileLabel = MakeLabel("温控方案", 11, TextMuted);
        profileLabel.SetBounds(30, 219, 120, 30);
        _profile.Items.AddRange(new object[] { "静音模式", "性能模式", "自定义模式" });
        _profile.SetBounds(164, 216, 428, 36);
        _profile.SelectedIndexChanged += (_, _) => UpdateModeVisibility();
        Controls.AddRange(new Control[] { profileLabel, _profile });

        _summaryPanel.SetBounds(28, 268, 564, 170);
        var summaryTitle = MakeLabel("当前温控曲线", 10, TextMuted);
        summaryTitle.SetBounds(20, 16, 240, 24);
        _smartSummary.SetBounds(20, 48, 524, 104);
        _smartSummary.TextAlign = ContentAlignment.MiddleLeft;
        _summaryPanel.Controls.AddRange(new Control[] { summaryTitle, _smartSummary });
        Controls.Add(_summaryPanel);

        _customPanel.SetBounds(28, 268, 564, 170);
        int[] temps = { 30, 40, 50, 60, 70, 80, 90, 100 };
        for (int i = 0; i < 8; i++)
        {
            int col = i % 4;
            int row = i / 4;
            var label = MakeLabel($"{temps[i]}°C", 9, TextMuted);
            label.SetBounds(12 + col * 136, 16 + row * 70, 54, 20);
            var input = new NumericUpDown
            {
                Minimum = 1300, Maximum = 4000, Increment = 100, Width = 120,
                BackColor = Color.FromArgb(45, 47, 51), ForeColor = TextMain,
                BorderStyle = BorderStyle.FixedSingle, ThousandsSeparator = false
            };
            input.SetBounds(12 + col * 136, 38 + row * 70, 120, 28);
            _curveInputs[i] = input;
            _customPanel.Controls.Add(label);
            _customPanel.Controls.Add(input);
        }
        Controls.Add(_customPanel);

        _fixedPanel.SetBounds(28, 268, 564, 170);
        var fixedHint = MakeLabel("12 档固定转速", 10, TextMuted);
        fixedHint.SetBounds(20, 16, 220, 24);
        _fixedLevel.SetBounds(14, 52, 536, 42);
        _fixedLevel.BackColor = Surface;
        _fixedValue.SetBounds(20, 108, 524, 32);
        _fixedValue.TextAlign = ContentAlignment.MiddleCenter;
        _fixedLevel.ValueChanged += (_, _) => UpdateFixedLabel();
        _fixedPanel.Controls.AddRange(new Control[] { fixedHint, _fixedLevel, _fixedValue });
        Controls.Add(_fixedPanel);

        var separator = new Panel { BackColor = Color.FromArgb(52, 54, 58) };
        separator.SetBounds(28, 460, 564, 1);
        Controls.Add(separator);

        _gearLed.SetBounds(32, 484, 260, 34);
        var startLabel = MakeLabel("智能启停", 11, TextMain);
        startLabel.SetBounds(32, 538, 120, 30);
        _smartStart.Items.AddRange(new object[] { "关闭", "立即运行", "延时运行" });
        _smartStart.SetBounds(164, 534, 428, 36);
        _powerOn.SetBounds(32, 590, 260, 34);
        Controls.AddRange(new Control[] { _gearLed, startLabel, _smartStart, _powerOn });

        var apply = MakeButton("保存并应用");
        apply.BackColor = Accent;
        apply.ForeColor = Color.Black;
        apply.SetBounds(314, 680, 278, 46);
        apply.Click += (_, _) => SaveAndApply();
        var hide = MakeButton("隐藏到托盘");
        hide.SetBounds(28, 680, 278, 46);
        hide.Click += (_, _) => Hide();
        _saved.SetBounds(32, 640, 560, 28);
        Controls.AddRange(new Control[] { _saved, hide, apply });
    }

    private void LoadSettings(LiteSettings settings)
    {
        _controlMode = settings.ControlMode;
        _profile.SelectedIndex = settings.SmartProfile;
        _fixedLevel.Value = settings.FixedLevel;
        _gearLed.Checked = settings.GearLed;
        _smartStart.SelectedIndex = settings.SmartStartStop;
        _powerOn.Checked = settings.PowerOnAutoStart;
        for (int i = 0; i < 8; i++) _curveInputs[i].Value = settings.CustomCurve[i];
        UpdateFixedLabel();
        UpdateModeVisibility();
    }

    private void SetMode(int mode) { _controlMode = mode; UpdateModeVisibility(); }

    private void UpdateModeVisibility()
    {
        bool smart = _controlMode == 0;
        _smartButton.BackColor = smart ? Accent : Surface;
        _smartButton.ForeColor = smart ? Color.Black : TextMain;
        _fixedButton.BackColor = smart ? Surface : Accent;
        _fixedButton.ForeColor = smart ? TextMain : Color.Black;
        _profile.Visible = smart;
        _customPanel.Visible = smart && _profile.SelectedIndex == 2;
        _summaryPanel.Visible = smart && _profile.SelectedIndex != 2;
        _fixedPanel.Visible = !smart;
        _smartSummary.Text = _profile.SelectedIndex == 0
            ? "30° 1300 · 40° 1300 · 50° 1700 · 60° 2100\n70° 2700 · 80° 3000 · 90° 3300 · 100° 3500"
            : "30° 1700 · 40° 1700 · 50° 2100 · 60° 2700\n70° 3300 · 80° 3500 · 90° 4000 · 100° 4000";
    }

    private void UpdateFixedLabel()
    {
        int i = _fixedLevel.Value;
        _fixedValue.Text = $"{CoolerController.FixedNames[i]}  ·  {CoolerController.FixedSpeeds[i]} rpm";
    }

    private void SaveAndApply()
    {
        LiteSettings settings = _controller.Settings;
        settings.ControlMode = _controlMode;
        settings.SmartProfile = Math.Max(0, _profile.SelectedIndex);
        settings.FixedLevel = _fixedLevel.Value;
        settings.GearLed = _gearLed.Checked;
        settings.SmartStartStop = Math.Max(0, _smartStart.SelectedIndex);
        settings.PowerOnAutoStart = _powerOn.Checked;
        settings.CustomCurve = _curveInputs.Select(x => (int)x.Value).ToArray();
        settings.Normalize();
        _controller.UpdateSettings(settings);
        LoadSettings(settings);
        _saved.ForeColor = Color.FromArgb(90, 210, 140);
        _saved.Text = "已保存；重启和断线重连后会自动恢复";
    }

    public void RefreshStatus(ControllerStatus status)
    {
        _status.Text = status.Connected ? "● 已连接  ·  " + status.ModeText : "○ 正在自动连接";
        _status.ForeColor = status.Connected ? Color.FromArgb(90, 220, 140) : TextMuted;
        double temp = Math.Max(status.CpuTemperature ?? 0, status.GpuTemperature ?? 0);
        _reading.Text = $"{(temp > 0 ? temp.ToString("0") : "--")}°C  ·  {status.TargetRpm} rpm";
        _power.Text = status.PowerMode;
    }

    protected override void Dispose(bool disposing)
    {
        _reallyClosing = true;
        base.Dispose(disposing);
    }

    private static Label MakeLabel(string text, float size, Color color) => new()
    {
        Text = text, AutoSize = false, ForeColor = color, BackColor = Color.Transparent,
        Font = new Font("Microsoft YaHei UI", size, size >= 17 ? FontStyle.Bold : FontStyle.Regular)
    };

    private static Button MakeButton(string text) => new()
    {
        Text = text, FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = TextMain,
        Font = new Font("Microsoft YaHei UI", 11f), Cursor = Cursors.Hand,
        UseVisualStyleBackColor = false
    };

    private static ComboBox MakeCombo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(48, 50, 54),
        ForeColor = TextMain, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 10.5f)
    };

    private static CheckBox MakeCheck(string text) => new()
    {
        Text = text, ForeColor = TextMain, BackColor = Color.Transparent,
        Font = new Font("Microsoft YaHei UI", 11f), AutoSize = false, Cursor = Cursors.Hand
    };
}

internal sealed class ControllerStatus
{
    public bool Connected;
    public bool Paused;
    public double? CpuTemperature;
    public double? GpuTemperature;
    public int TargetRpm;
    public string PowerMode = "检测中";
    public string ModeText = "智能变频 · 性能模式";
    public string Message = "正在启动";
    public DateTime LastUpdated = DateTime.Now;

    public string Summary => Paused ? "已暂停" : Connected ? $"{Math.Max(CpuTemperature ?? 0, GpuTemperature ?? 0):0}°C · {TargetRpm} rpm" : "正在连接底座";
    public static ControllerStatus Starting() => new();
    public ControllerStatus Copy() => new()
    {
        Connected = Connected, Paused = Paused, CpuTemperature = CpuTemperature, GpuTemperature = GpuTemperature,
        TargetRpm = TargetRpm, PowerMode = PowerMode, ModeText = ModeText, Message = Message, LastUpdated = LastUpdated
    };

    public string ToJson() =>
        "{\n" +
        $"  \"connected\": {Connected.ToString().ToLowerInvariant()},\n" +
        $"  \"paused\": {Paused.ToString().ToLowerInvariant()},\n" +
        $"  \"cpuTemperature\": {Number(CpuTemperature)},\n" +
        $"  \"gpuTemperature\": {Number(GpuTemperature)},\n" +
        $"  \"targetRpm\": {TargetRpm},\n" +
        $"  \"powerMode\": \"{Escape(PowerMode)}\",\n" +
        $"  \"mode\": \"{Escape(ModeText)}\",\n" +
        $"  \"message\": \"{Escape(Message)}\",\n" +
        $"  \"lastUpdated\": \"{LastUpdated:yyyy-MM-dd HH:mm:ss}\"\n" +
        "}";

    private static string Number(double? value) => value.HasValue ? value.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "null";
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
}
