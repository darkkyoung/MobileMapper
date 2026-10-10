using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MobileMapper.Core;
using MobileMapper.Device;
using MobileMapper.Media;
using MobileMapper.Session;
using Windows.System;
using WinRT;

namespace MobileMapper.App;

public sealed class MainWindow : Window
{
    private readonly TextBox adbPath = new() { Header = "Official Platform-Tools: adb.exe", PlaceholderText = "Select the downloaded adb.exe" };
    private readonly TextBox pairingEndpoint = new() { Header = "Pairing endpoint (IP:port)" };
    private readonly PasswordBox pairingCode = new() { Header = "6-digit pairing code", MaxLength = 6 };
    private readonly TextBox connectEndpoint = new() { Header = "Connection endpoint (IP:port)" };
    private readonly ComboBox services = new() { Header = "Discovered services", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox devices = new() { Header = "Select an online device", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock metrics = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox log = new() { IsReadOnly = true, AcceptsReturn = true, Height = 110, TextWrapping = TextWrapping.Wrap };
    private readonly SwapChainPanel panel = new();
    private readonly UserControl viewport = new() { IsTabStop = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly List<Button> deviceButtons = [];
    private Button disconnect = null!, enable = null!;
    private readonly List<Button> touchButtons = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private AdbRuntime? adb;
    private NativeMedia? media;
    private MirrorSession? session;
    private Task? sessionTask;
    private UserSettings settings = UserSettings.Load();
    private bool busy, closing, closeReady, directHeld;
    private long directGeneration;
    private ulong previousReceived, previousDecoded, previousPresented;
    private DateTimeOffset lastMetrics = DateTimeOffset.UtcNow;

    public MainWindow()
    {
        Title = "MobileMapper — Wireless Mirroring (developer build)";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 850));
        var root = new Grid { Padding = new Thickness(12), ColumnSpacing = 12 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(335) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var side = new StackPanel { Spacing = 8 };
        side.Children.Add(new TextBlock { Text = "Device", FontSize = 24 });
        side.Children.Add(adbPath); adbPath.Text = settings.AdbPath;
        AddDeviceButton(side, "Browse adb.exe", BrowseAsync);
        side.Children.Add(new TextBlock { Text = "On the phone: Developer options → Wireless debugging → Pair device with pairing code. Keep both devices on the same Wi-Fi.", TextWrapping = TextWrapping.Wrap });
        AddDeviceButton(side, "Refresh discovery / devices", RefreshAsync);
        side.Children.Add(services);
        services.SelectionChanged += (_, _) => {
            if (services.SelectedItem is MdnsService s) {
                if (s.IsPairing) pairingEndpoint.Text = s.Endpoint.ToString();
                else connectEndpoint.Text = s.Endpoint.ToString();
            }
        };
        side.Children.Add(pairingEndpoint); side.Children.Add(pairingCode);
        AddDeviceButton(side, "Pair device", PairAsync);
        side.Children.Add(connectEndpoint);
        AddDeviceButton(side, "Connect endpoint", ConnectEndpointAsync);
        side.Children.Add(devices);
        AddDeviceButton(side, "Start mirroring selected device", StartAsync);
        disconnect = MakeButton("Disconnect / cancel recovery", async () => await StopAsync()); side.Children.Add(disconnect);
        side.Children.Add(new TextBlock { Text = "Status", FontSize = 18 }); side.Children.Add(status);
        side.Children.Add(new TextBlock { Text = "Uses FFmpeg under LGPL-2.1-or-later. Licenses and corresponding source are in the developer package.", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var right = new Grid { RowSpacing = 8 };
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(right, 1); root.Children.Add(right);
        var black = new Grid { Background = new SolidColorBrush(Colors.Black), Children = { panel } };
        viewport.Content = black; right.Children.Add(viewport);
        viewport.PointerPressed += OnPointerPressed;
        viewport.PointerMoved += OnPointerMoved;
        viewport.PointerReleased += OnPointerReleased;
        viewport.PointerCanceled += async (_, _) => await ReleaseAsync();
        viewport.PointerCaptureLost += async (_, _) => { if (directHeld) await ReleaseAsync(); };
        viewport.LostFocus += async (_, _) => { if (directHeld) await ReleaseAsync(); };
        panel.SizeChanged += (_, _) => ResizeMedia();
        panel.CompositionScaleChanged += (_, _) => ResizeMedia();
        var diagnostic = new StackPanel { Spacing = 5 };
        Grid.SetRow(diagnostic, 1); right.Children.Add(diagnostic);
        diagnostic.Children.Add(metrics);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        enable = MakeButton("Enable touch", () => { session?.Arm(); UpdateControls(); return Task.CompletedTask; });
        actions.Children.Add(enable); actions.Children.Add(MakeButton("Release all (Esc)", ReleaseAsync));
        diagnostic.Children.Add(actions);
        diagnostic.Children.Add(new TextBlock { Text = "Touch diagnostic: A=(25%,50%), B=(50%,50%), C=(75%,50%). Move shifts down 10%. Use a safe touch-test screen." });
        var contacts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        for (int index = 0; index < 3; index++)
        {
            int i = index; string owner = ((char)('A' + i)).ToString();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            row.Children.Add(new TextBlock { Text = owner, VerticalAlignment = VerticalAlignment.Center });
            foreach (var action in new[] { TouchAction.Down, TouchAction.Move, TouchAction.Up })
            {
                var a = action;
                var button = MakeButton(a.ToString(), () => {
                    session?.Touch("diagnostic-" + owner, a, new((i + 1) * .25, a == TouchAction.Move ? .6 : .5), session.InputGeneration);
                    UpdateControls(); return Task.CompletedTask;
                });
                touchButtons.Add(button); row.Children.Add(button);
            }
            contacts.Children.Add(row);
        }
        diagnostic.Children.Add(contacts); diagnostic.Children.Add(log);
        root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(async (_, e) => {
            if (e.Key == VirtualKey.Escape) { e.Handled = true; await ReleaseAsync(); }
        }), true);
        Content = root;
        Activated += async (_, e) => {
            if (e.WindowActivationState == WindowActivationState.Deactivated) await ReleaseAsync();
        };
        AppWindow.Changed += async (_, _) => {
            if (AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized) await ReleaseAsync();
        };
        AppWindow.Closing += async (_, e) => {
            if (closeReady) return;
            e.Cancel = true;
            if (closing) return;
            closing = true; lifetime.Cancel();
            await StopAsync();
            timer.Stop();
            if (media is not null) { panel.As<ISwapChainPanelNative>().SetSwapChain(0); media.Dispose(); media = null; }
            if (adb is not null) await adb.DisposeAsync();
            closeReady = true; Close();
        };
        timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => UpdateMetrics(); timer.Start();
        status.Text = "Idle — select official Platform-Tools, then refresh or pair.";
        UpdateControls();
    }
    private Button MakeButton(string text, Func<Task> action)
    {
        var b = new Button { Content = text };
        b.Click += async (_, _) => {
            try { await action(); }
            catch (Exception e) { ShowError(e); }
        };
        return b;
    }
    private void AddDeviceButton(StackPanel target, string text, Func<Task> operation)
    {
        var b = MakeButton(text, async () => {
            busy = true; UpdateControls();
            try { await operation(); }
            finally { busy = false; UpdateControls(); }
        });
        deviceButtons.Add(b); target.Children.Add(b);
    }
    private async Task BrowseAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".exe");
        var file = await picker.PickSingleFileAsync(); if (file is not null) adbPath.Text = file.Path;
    }
    private async Task<AdbRuntime> EnsureAdbAsync()
    {
        if (adb is not null && adb.Runner.Executable != adbPath.Text)
        { await adb.DisposeAsync(); adb = null; }
        adb ??= await AdbRuntime.StartAsync(adbPath.Text, lifetime.Token);
        if (!adb.IsAlive) throw new IOException("The private ADB daemon stopped. Restart MobileMapper.");
        settings = settings with { AdbPath = adbPath.Text }; settings.Save();
        return adb;
    }
    private async Task RefreshAsync()
    {
        status.Text = "Discovering — searching local Wireless Debugging services…";
        var runtime = await EnsureAdbAsync();
        IReadOnlyList<MdnsService> discovered = [];
        try { discovered = await runtime.Client.DiscoverAsync(lifetime.Token); }
        catch (IOException) { status.Text = "Discovery unavailable. Enter the current connection endpoint manually."; }
        services.ItemsSource = discovered;
        // Connection requires the host's existing authorization; this does not pair unknown devices.
        foreach (var service in discovered.Where(s => !s.IsPairing))
            try { await runtime.Client.ConnectAsync(service.Endpoint, lifetime.Token); } catch (IOException) { }
        var found = await runtime.Client.DevicesAsync(lifetime.Token);
        devices.ItemsSource = found.Where(d => d.State == "device").ToArray();
        foreach (var device in found.Where(d => d.State == "device"))
        {
            try { if (settings.RememberedIdentity.Length > 0 && await runtime.Client.IdentityAsync(device.Serial, lifetime.Token) == settings.RememberedIdentity) devices.SelectedItem = device; }
            catch (IOException) { }
        }
        status.Text = found.Any(d => d.State == "device")
            ? "Idle — select the phone, then Start mirroring selected device."
            : "NeedsPairing / NeedsUserAction — pair the phone, or enter its current connection endpoint. Pairing and connection ports differ.";
    }
    private async Task PairAsync()
    {
        status.Text = "Pairing — keep the pairing-code screen open on the phone.";
        var endpoint = Endpoint.Parse(pairingEndpoint.Text);
        string code = pairingCode.Password;
        pairingCode.Password = "";
        try { await (await EnsureAdbAsync()).Client.PairAsync(endpoint, code, lifetime.Token); }
        finally { code = ""; pairingCode.Password = ""; }
        status.Text = "Paired. Return to the main Wireless Debugging screen; refresh discovery or enter its connection endpoint.";
    }
    private async Task ConnectEndpointAsync()
    {
        status.Text = "Connecting — checking the connection endpoint…";
        await (await EnsureAdbAsync()).Client.ConnectAsync(Endpoint.Parse(connectEndpoint.Text), lifetime.Token);
        await RefreshAsync();
    }
    private async Task StartAsync()
    {
        if (devices.SelectedItem is not AdbDevice selected) throw new InvalidOperationException("Select an online device first.");
        var runtime = await EnsureAdbAsync();
        string identity = await runtime.Client.IdentityAsync(selected.Serial, lifetime.Token);
        if (media is null)
        {
            media = new NativeMedia();
            nint chain = media.GetSwapChain();
            try { panel.As<ISwapChainPanelNative>().SetSwapChain(chain); }
            finally { Marshal.Release(chain); }
            ResizeMedia();
        }
        settings = settings with { RememberedIdentity = identity }; settings.Save();
        previousReceived = 0;
        previousDecoded = media.Stats.Decoded; previousPresented = media.Stats.Presented; lastMetrics = DateTimeOffset.UtcNow;
        session = new MirrorSession(runtime, media, Path.Combine(AppContext.BaseDirectory, "third-party", "scrcpy-server-v5.0.1"));
        session.Changed += () => DispatcherQueue.TryEnqueue(() => { UpdateControls(); UpdateStatus(); });
        sessionTask = ObserveSessionAsync(session.RunAsync(selected.Serial, identity, lifetime.Token));
        UpdateControls();
    }
    private async Task ObserveSessionAsync(Task task)
    {
        try { await task; } catch (Exception e) { ShowError(e); }
        finally { DispatcherQueue.TryEnqueue(() => { UpdateStatus(); UpdateControls(); }); }
    }
    private async Task StopAsync()
    {
        directHeld = false; viewport.ReleasePointerCaptures();
        if (session is not null) { session.RequestStop(); await session.ReleaseAsync(); }
        if (sessionTask is not null) await sessionTask;
        sessionTask = null; UpdateControls(); UpdateStatus();
    }
    private async Task ReleaseAsync()
    {
        directHeld = false; viewport.ReleasePointerCaptures();
        if (session is not null) await session.ReleaseAsync();
        UpdateControls();
    }
    private void ResizeMedia()
    {
        if (media is null || panel.ActualWidth <= 0 || panel.ActualHeight <= 0) return;
        try { media.Resize(Math.Max(1, (int)Math.Round(panel.ActualWidth * panel.CompositionScaleX)),
            Math.Max(1, (int)Math.Round(panel.ActualHeight * panel.CompositionScaleY)), panel.CompositionScaleX, panel.CompositionScaleY); }
        catch (Exception e) { ShowError(e); session?.RequestStop(); }
    }
    private NormalizedPoint? Map(PointerRoutedEventArgs e)
    {
        if (session?.Geometry is not { } geometry) return null;
        var p = e.GetCurrentPoint(viewport).Position;
        return Viewport.Map(p.X, p.Y, viewport.ActualWidth, viewport.ActualHeight, geometry);
    }
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (directHeld || !e.GetCurrentPoint(viewport).Properties.IsLeftButtonPressed || Map(e) is not { } point || session is null) return;
        try {
            viewport.Focus(FocusState.Pointer); directGeneration = session.InputGeneration;
            directHeld = session.Touch("direct", TouchAction.Down, point, directGeneration);
            if (directHeld) { viewport.CapturePointer(e.Pointer); e.Handled = true; }
        } catch (Exception error) { ShowError(error); }
    }
    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!directHeld || Map(e) is not { } point) return;
        try { session?.Touch("direct", TouchAction.Move, point, directGeneration); }
        catch (Exception error) { ShowError(error); }
    }
    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!directHeld) return;
        directHeld = false;
        try { session?.Touch("direct", TouchAction.Up, new(0, 0), directGeneration); }
        catch (Exception error) { ShowError(error); }
        viewport.ReleasePointerCapture(e.Pointer); e.Handled = true;
    }
    private void UpdateStatus()
    {
        if (session is not null) status.Text = $"{session.State}\nDevice: {session.SelectedDevice}\n{session.LastError}";
    }
    private void UpdateControls()
    {
        bool running = sessionTask is { IsCompleted: false };
        foreach (var b in deviceButtons) b.IsEnabled = !busy && !running && !closing;
        adbPath.IsEnabled = devices.IsEnabled = services.IsEnabled = !busy && !running;
        disconnect.IsEnabled = running;
        enable.IsEnabled = session?.State == SessionState.Streaming && !session.InputEnabled;
        foreach (var b in touchButtons) b.IsEnabled = session?.InputEnabled == true;
    }
    private void UpdateMetrics()
    {
        if (session is null || media is null) return;
        var m = media.Stats;
        ulong received = (ulong)Interlocked.Read(ref session.Counters.Received);
        double seconds = Math.Max(.001, (DateTimeOffset.UtcNow - lastMetrics).TotalSeconds);
        metrics.Text = $"{m.Width}×{m.Height} | FPS received {(received - previousReceived) / seconds:F1}, decoded {(m.Decoded - previousDecoded) / seconds:F1}, presented {(m.Presented - previousPresented) / seconds:F1}\n" +
            $"Replaced {m.Replaced} | Queue {session.Counters.QueueDepth}, {session.Counters.QueuedBytes} bytes | Reconnects {session.Counters.Reconnects} | Active contacts {session.ActiveContacts} | Touch {(session.InputEnabled ? "enabled" : "paused")}";
        previousReceived = received; previousDecoded = m.Decoded; previousPresented = m.Presented; lastMetrics = DateTimeOffset.UtcNow;
        log.Text = session.Log.Snapshot(); UpdateControls();
    }
    private void ShowError(Exception error)
    {
        status.Text = error is OperationCanceledException ? "Operation cancelled." : error.Message;
        // No raw process output, passwords or device content is written to the session log.
    }
    [ComImport, Guid("63aad0b8-7c24-40ff-85a8-640d944cc325"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISwapChainPanelNative { void SetSwapChain(nint swapChain); }
}
