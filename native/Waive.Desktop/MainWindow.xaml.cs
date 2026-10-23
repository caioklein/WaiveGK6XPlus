using System.ComponentModel;
using System.Buffers.Binary;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Waive.Core;
using Waive.Device.Windows;

namespace Waive.Desktop;

public partial class MainWindow : Window
{
    Rgb color = new(104, 228, 204);
    readonly List<Border> keycaps = [];
    readonly List<string> log = [];
    readonly object logLock = new();
    readonly DispatcherTimer hotplugDebounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    HidTransport? transport;
    LightingController? controller;
    DeviceIdentity? identity;
    CancellationTokenSource? keepalive;
    Task? keepaliveTask;
    bool ready, busy, validColor = true, live, closing, allowClose;
    string? smokePath;
    readonly Stopwatch startup = Stopwatch.StartNew();

    public MainWindow()
    {
        InitializeComponent();
        smokePath = ReadArgument("--smoke");
        foreach (var hex in new[] { "#FF4F70", "#FFB649", "#F5ED64", "#68E4CC", "#55B9FF", "#A08CFA", "#F18BAA", "#F4F5F8", "#000000" })
        {
            var button = new Button { Background = Brush(hex), Width = 25, Height = 25, Padding = new Thickness(0), Margin = new Thickness(0, 0, 7, 5), ToolTip = hex, Content = "" };
            System.Windows.Automation.AutomationProperties.SetName(button, "Choose " + hex);
            button.Click += (_, _) => HexBox.Text = hex;
            Swatches.Children.Add(button);
        }
        DrawKeyboard();
        InitializeProfiles();
        InitializeVisualEditing();
        ready = true;
        RefreshSelection();
        RefreshPreview();
        hotplugDebounce.Tick += async (_, _) =>
        {
            hotplugDebounce.Stop();
            if (busy || closing) return;
            try
            {
                var current = await Task.Run(HidDiscovery.Enumerate);
                if (transport is not null && !current.Any(d => d.Path == connectedPath))
                {
                    await DisconnectAsync(false);
                    SetResult("Keyboard disconnected. Your color selection is preserved.", true);
                }
                else if (transport is null) await ScanAsync();
            }
            catch (Exception error) { Record("Device notification: " + error.Message); }
        };
    }

    static string? ReadArgument(string name)
    {
        var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    void Record(string message) { lock (logLock) { log.Add($"{DateTimeOffset.Now:O} {message}"); if (log.Count > 100) log.RemoveAt(0); } }
    string[] SnapshotLog() { lock (logLock) return log.ToArray(); }
    string? connectedPath;

    void DrawKeyboard()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "96xs-keymap.json")));
            var entries = json.RootElement.EnumerateArray().Where(k => k.TryGetProperty("Position", out _)).ToArray();
            var minX = entries.Min(k => k.GetProperty("Position").GetProperty("Left").GetDouble());
            var minY = entries.Min(k => k.GetProperty("Position").GetProperty("Top").GetDouble());
            double width = 0, height = 0;
            foreach (var key in entries)
            {
                var p = key.GetProperty("Position");
                var x = p.GetProperty("Left").GetDouble() - minX;
                var y = p.GetProperty("Top").GetDouble() - minY;
                var w = p.GetProperty("Width").GetDouble(); var h = p.GetProperty("Height").GetDouble();
                if (w <= 0 || h <= 0) continue;
                var label = key.TryGetProperty("Show", out var show) ? show.GetString() ?? "" : "";
                var text=new TextBlock { Text = label, FontSize = label.Length > 5 ? 9 : 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
                var glow=new Border{Width=5,Height=5,CornerRadius=new(2),Background=Brush("#FFCD68"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(3),Visibility=Visibility.Collapsed,IsHitTestVisible=false};
                var content=new Grid();content.Children.Add(text);content.Children.Add(glow);
                var cap = new Border { Width = Math.Max(4, w - 4), Height = Math.Max(4, h - 4), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child=content };
                int slot = key.GetProperty("LogicCode").GetInt32();
                slotsByKey[cap] = slot; cap.Focusable = true;
                cap.MouseLeftButtonDown += (_, args) => { cap.Focus();BeginKeyDrag(slot,args.GetPosition(KeyboardCanvas),System.Windows.Input.Keyboard.Modifiers);args.Handled = true; };
                cap.KeyDown += (_, args) => { if (args.Key == System.Windows.Input.Key.Space) { SelectKey(slot); args.Handled = true; } };
                System.Windows.Automation.AutomationProperties.SetName(cap,label+" keyboard key");
                Canvas.SetLeft(cap, x); Canvas.SetTop(cap, y);
                keyBounds.Add(new(slot,x,y,cap.Width,cap.Height));
                KeyboardCanvas.Children.Add(cap); keycaps.Add(cap);
                width = Math.Max(width, x + w); height = Math.Max(height, y + h);
            }
            KeyboardCanvas.Width = width; KeyboardCanvas.Height = height;
        }
        catch (Exception error) { Record("Layout: " + error.Message); PreviewLabel.Text = "Keyboard preview unavailable"; }
    }

    void RefreshPreview()
    {
        if (!ready) return;
        UpdatePreviewTimer();
        var effect=keyEffectPreview??profile.Effects.FirstOrDefault(e=>e.Id==profile.EffectId);
        var frame=ProfileCompiler.Frame(profile,effect,previewTimer.IsEnabled?previewClock.ElapsedMilliseconds/50:0);
        foreach(var key in keycaps)
        {
            var index=slotsByKey[key];var at=(KeyboardLightingMap.LedForKey(index)??throw new InvalidOperationException("A preview key has no LED location."))*4;
            var c=new Rgb(frame[at],frame[at+1],frame[at+2]);var scale=BrightnessSlider.Value/100d;
            var fillColor=Color.FromRgb((byte)(c.R*scale*.72+16),(byte)(c.G*scale*.72+19),(byte)(c.B*scale*.72+23));
            var fill=new SolidColorBrush(fillColor);
            fill.Freeze();key.Background=fill;key.BorderBrush=selectedKeys.Contains(index)?Brush("#FFFFFF"):Brush("#40505E");key.BorderThickness=new Thickness(selectedKeys.Contains(index)?2:1);
            if(key.Child is Grid content){((TextBlock)content.Children[0]).Foreground=Brush(fillColor.R*.2126+fillColor.G*.7152+fillColor.B*.0722>135?"#081A20":"#F5FAFF");((Border)content.Children[1]).Visibility=profile.KeyEffects.ContainsKey(index)?Visibility.Visible:Visibility.Collapsed;}
            key.ToolTip=profile.KeyColors.GetValueOrDefault(index,profile.Color)+" · "+profile.Keys.GetValueOrDefault(index,KeyCatalog.Decode(defaultKeys.GetValueOrDefault(index)))+(profile.KeyEffects.ContainsKey(index)?" · Key-press glow":"")+" · Drag to select keys";
        }
        BrightnessText.Text = $"{BrightnessSlider.Value:0}%";
        RefreshColorControls();
        UpdateButtons();
    }
    void HexChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready || loadingProfile) return;
        try { color = Rgb.Parse(HexBox.Text); validColor = true; PaintSelection(); SetResult("Color edited. Save on this PC or Apply to keyboard."); RefreshPreview(); }
        catch (FormatException error) { validColor = false; SetResult(error.Message, true); UpdateButtons(); }
    }
    void BrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!ready || loadingProfile) return; BeforeEdit(); profile.Brightness=(int)BrightnessSlider.Value; Changed(); }
    void SetResult(string message, bool error = false)
    {
        ResultText.Text = message; ResultText.Foreground = Brush(error ? "#FFB5AA" : "#A3BAC9");
        Record(message);
    }
    void UpdateButtons()
    {
        ApplyButton.IsEnabled = !busy && validColor && identity?.ModelId == 656801936 && controller is not null;
        ConnectButton.IsEnabled = !busy; HexBox.IsEnabled = !busy; BrightnessSlider.IsEnabled = !busy;
        Swatches.IsEnabled = !busy;
        StoreLightingButton.IsEnabled=ApplyButton.IsEnabled;
        StoreProfileButton.IsEnabled=ApplyButton.IsEnabled&&returnLayer>1;
        StopButton.IsEnabled = !busy;
        StopButton.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        EditorTabs.IsEnabled = !busy;
        if(ProfileList.Parent is FrameworkElement profilePanel)profilePanel.IsEnabled = !busy;
        RefreshModeText();
    }
    void SetBusy(bool value)
    {
        busy = value; ApplyProgress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook((nint hwnd, int message, nint wparam, nint lparam, ref bool handled) =>
        {
            if (message == 0x219 && !closing) { hotplugDebounce.Stop(); hotplugDebounce.Start(); }
            return 0;
        });
        await ScanAsync();
        InitializeBackgroundOptions();
        if(Environment.GetCommandLineArgs().Contains("--minimized"))WindowState=WindowState.Minimized;
        if (smokePath is not null) await SmokeAsync(smokePath);
    }
    async void ScanClicked(object sender, RoutedEventArgs e)
    {
        if (DeviceSelector.Visibility == Visibility.Visible && DeviceSelector.SelectedItem is HidDevice chosen && transport is null)
            await ConnectAsync(chosen);
        else await ScanAsync();
    }
    async Task ScanAsync()
    {
        if (busy || closing) return;
        SetBusy(true); SetResult("Looking for the keyboard's USB control interface…");
        try
        {
            await DisconnectAsync(true);
            var devices = await Task.Run(HidDiscovery.Enumerate);
            Record($"Discovery: {devices.Count} matching vendor collections.");
            DeviceSelector.ItemsSource = devices;
            DeviceSelector.Visibility = devices.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (devices.Count == 0)
            {
                ConnectButton.Content = "Scan for keyboard";
                DeviceName.Text = "Not connected";
                DeviceDetail.Text = "Use USB mode and close GK6+ or other keyboard utilities, then scan again.";
                SetResult("No supported USB control interface found. The keyboard above is a visual preview.");
                return;
            }
            if (devices.Count > 1)
            {
                DeviceName.Text = "Choose a keyboard"; DeviceDetail.Text = "Several control interfaces were found. Choose one before connecting.";
                ConnectButton.Content = "Connect selected"; SetResult("Select the keyboard in the list, then connect."); return;
            }
            await ConnectInternalAsync(devices[0]);
        }
        catch (Exception error) { await DisconnectAsync(false); SetResult(error.Message, true); }
        finally { SetBusy(false); }
    }
    async Task ConnectAsync(HidDevice candidate)
    {
        SetBusy(true);
        try { await ConnectInternalAsync(candidate); }
        catch (Exception error) { await DisconnectAsync(false); SetResult(error.Message, true); }
        finally { SetBusy(false); }
    }
    async Task ConnectInternalAsync(HidDevice candidate)
    {
        var session = await Task.Run(() => new HidTransport(candidate));
        session.Trace = Record;
        session.KeyChanged += (index,down) => playback?.Key(index,down);
        session.ModeChanged += mode => { if(mode!=5) _ = Dispatcher.InvokeAsync(async()=>{ if(live && !busy){ await StopPlaybackAsync();await StopKeepaliveAsync();live=false;UpdateActivity();UpdateButtons();SetResult("The keyboard switched to an onboard layer. Live playback stopped.");} }); };
        try
        {
            var identified = await DeviceIdentity.ReadAsync(session);
            transport = session; identity = identified; connectedPath = candidate.Path;
            controller = new LightingController(session);
            DeviceName.Text = identified.ModelId == 656801936 ? "96XS · RGB" : $"Model {identified.ModelId}";
            DeviceDetail.Text = $"Firmware {identified.Version}\n{identified.Columns} × {identified.Rows} matrix · USB connected";
            ConnectButton.Content = "Reconnect";
            if (identified.ModelId != 656801936)
                SetResult("Identified successfully. Color writes are limited to 96XS-RGB in this first build.", true);
            else SetResult("Connected. Choose a color and click Apply to keyboard.");
        }
        catch { session.Dispose(); throw; }
    }

    async void ApplyClicked(object sender, RoutedEventArgs e)=>await ApplyProfileAsync(false);
    async void StoreLightingClicked(object sender, RoutedEventArgs e)=>await ApplyProfileAsync(true);
    async void StoreProfileClicked(object sender, RoutedEventArgs e)=>await ApplyProfileAsync(true,true);
    KeyboardProfile ProfileForAction(bool stored,bool includeBindings=false)
    {
        var applied=profile.Clone();applied.Mode=stored?returnLayer:0;applied.ReturnLayer=returnLayer;
        if(stored&&!includeBindings){applied.Keys.Clear();applied.FnKeys.Clear();applied.Macros.Clear();}
        return applied;
    }
    async Task ApplyProfileAsync(bool stored,bool includeBindings=false)
    {
        if (controller is null || identity?.ModelId != 656801936 || busy) return;
        var started = false;
        SetBusy(true); ApplyProgress.Value = 0;
        try
        {
            var applied=ProfileForAction(stored,includeBindings);applied.Validate(identity.Columns*identity.Rows);
            if(stored)ProfileCompiler.StoredPlan(applied,defaultKeys,identity.Columns*identity.Rows);else ProfileCompiler.Compile(applied,defaultKeys,identity.Columns*identity.Rows);
            await StopPlaybackAsync();
            await StopKeepaliveAsync();
            started = true;
            var progress = new Progress<int>(value => ApplyProgress.Value = value);
            if (stored)
            {
                SetResult(includeBindings?"Saving lighting, key assignments, and macros to keyboard memory…":"Saving lighting to keyboard memory…");
                await controller.ApplyProfileStoredAsync(applied,defaultKeys,identity.Columns*identity.Rows,progress);
                live = false; UpdateActivity();
                SetResult(includeBindings?"Profile saved onboard. The app can be closed.":"Lighting saved onboard. Key assignments and macros were preserved. The app can be closed.");
            }
            else
            {
                SetResult("Applying through the app…");
                // Mark before entering live mode so failure cleanup also restores onboard mode.
                live = true;
                await controller.ApplyProfileLiveAsync(applied,defaultKeys,identity.Columns*identity.Rows,progress);
                playback=new LivePlayback(transport!,controller,applied);
                playback.OpenRequested+=target=>{_=Dispatcher.InvokeAsync(()=>{try{HostAction.Validate(target);Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}catch(Exception error){SetResult("Could not open the assigned item: "+error.Message,true);}});};
                playback.Failed += error => { _ = Dispatcher.InvokeAsync(async()=>{Record(error.Message);await DisconnectAsync(true);SetResult("Live playback stopped: "+error.Message,true);}); };
                playback.Start();
                StartKeepalive();
                SetResult("Applied. Keep the app running or minimized to the tray. Click Apply now after further edits.");
            }
        }
        catch (Exception error)
        {
            var recovered = false;
            if (started && controller is not null)
            {
                try { await controller.RestoreOnboardAsync(returnLayer); live = false; recovered = true; UpdateActivity(); }
                catch (Exception restoreError) { Record("Return to onboard mode failed: " + restoreError.Message); }
            }
            if (started && !recovered) await DisconnectAsync(false);
            var next = !started ? "" : recovered ? " Returned to built-in lighting. You can try again." : " Reconnect before retrying.";
            SetResult(error.Message + next + (stored && started ? " Stored lighting may be partially written." : ""), true);
        }
        finally { SetBusy(false); }
    }
    byte[] CompileLiveKeys()
    {
        if (identity is null) throw new InvalidOperationException("Connect the keyboard first.");
        var slots = identity.Columns * identity.Rows;
        var table = new byte[slots * 4];
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "96xs-live-defaults.json")));
        foreach (var key in json.RootElement.GetProperty("KeySet").EnumerateArray())
        {
            var index = key.GetProperty("Index").GetInt32();
            if (index < 0 || index >= slots) throw new NotSupportedException("This firmware's key matrix does not match the bundled standard layout. Live writes were not started.");
            var value = key.GetProperty("DriverValue").GetString()!;
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(index * 4), uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        return table;
    }
    void StartKeepalive()
    {
        var cancellation = new CancellationTokenSource(); keepalive = cancellation;
        var current = transport!;
        keepaliveTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellation.Token))
                    await current.RequestAsync(Protocol.Ping);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error)
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    current.Dispose();
                    await StopPlaybackAsync();
                    if (ReferenceEquals(transport, current)) { transport = null; controller = null; identity = null; live = false; DeviceName.Text = "Connection lost"; DeviceDetail.Text = "Reconnect the keyboard to resume."; UpdateActivity(); UpdateButtons(); SetResult(error.Message, true); }
                });
            }
        });
        UpdateActivity();
    }
    async Task StopKeepaliveAsync()
    {
        var cancellation = keepalive; var task = keepaliveTask;
        keepalive = null; keepaliveTask = null;
        if (cancellation is null) return;
        cancellation.Cancel();
        if (task is not null) await task;
        cancellation.Dispose();
    }
    async Task DisconnectAsync(bool restore)
    {
        await StopPlaybackAsync();
        await StopKeepaliveAsync();
        if (restore && live && controller is not null)
        {
            try { await controller.RestoreOnboardAsync(returnLayer); }
            catch (Exception error) { Record("Could not return to onboard mode: " + error.Message); }
        }
        transport?.Dispose(); transport = null; controller = null; identity = null; connectedPath = null; live = false;
        UpdateActivity(); UpdateButtons();
    }
    async void StopClicked(object sender, RoutedEventArgs e)
    {
        if (busy || controller is null) return;
        SetBusy(true);
        try { await StopPlaybackAsync(); await StopKeepaliveAsync(); await controller.RestoreOnboardAsync(returnLayer); live = false; UpdateActivity(); SetResult("Returned to the selected onboard layer. No background USB work."); }
        catch (Exception error) { await DisconnectAsync(false); SetResult(error.Message, true); }
        finally { SetBusy(false); }
    }
    void UpdateActivity()
    {
        FooterStatus.Text = live ? "Live mode · USB keepalive active" : "No background work";
    }
    void WindowStateChanged(object? sender, EventArgs e)
    {
        if(ready&&WindowState==WindowState.Minimized)MinimizeToTray();
        if(ready)UpdatePreviewTimer();
        if (ready) Record(WindowState == WindowState.Minimized ? "Minimized: no UI timers or rendering loop." : "Window restored.");
    }
    async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        if (busy) { SetResult("Wait for the current operation to finish before closing.", true); return; }
        if(!LeaveProfile())return;
        closing = true; SetBusy(true); hotplugDebounce.Stop();previewTimer.Stop();
        await DisconnectAsync(true);
        tray?.Dispose();tray=null;
        allowClose = true;
        _ = Dispatcher.BeginInvoke(Close);
    }

    void DiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Diagnostics"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"waive-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { version = "0.4.0", device = identity, live, minimized = WindowState == WindowState.Minimized, events = SnapshotLog() }, new JsonSerializerOptions { WriteIndented = true }));
            SetResult("Diagnostics saved to the Diagnostics folder beside WaiveGK6XPlus.exe.");
        }
        catch (Exception error) { SetResult("Could not save diagnostics: " + error.Message, true); }
    }

    async Task SmokeAsync(string output)
    {
        try
        {
            Directory.CreateDirectory(output);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var uiChecks = new Dictionary<string, bool>();
            if(!Environment.GetCommandLineArgs().Contains("--idle-smoke"))
            {
            var initialProfile=profile.Clone();
            profile=new();SyncProfileControls();SelectAllClicked(this,new RoutedEventArgs());
            var previousHex = HexBox.Text;
            HexBox.Text = "not rgb";
            uiChecks["invalidColorDisablesApply"] = !ApplyButton.IsEnabled;
            HexBox.Text = "#FF8040";
            uiChecks["validColorMatchesConnectionState"] = ApplyButton.IsEnabled == (identity?.ModelId == 656801936);
            BrightnessSlider.Value = 35;
            uiChecks["brightnessUpdatesLabel"] = BrightnessText.Text == "35%";
            uiChecks["explicitSaveDestinations"] = StoreLightingButton.Content.ToString()=="Save lighting onboard"&&ApplyButton.Content.ToString()=="Apply now"&&LayerCaption.Text=="KEYBOARD MEMORY SLOT";
            profile.Keys[47]="A";
            var storedLighting=ProfileForAction(true);
            var storedPlan=ProfileCompiler.StoredPlan(storedLighting,defaultKeys,112);
            uiChecks["lightingSavePreservesBindings"]=storedLighting.Keys.Count==0&&storedLighting.FnKeys.Count==0&&profile.Keys[47]=="A"&&storedPlan.All(step=>step.Command.Code is not (0x22 or 0x31 or 0x25));
            uiChecks["applyNowIgnoresLegacyStoredMode"]=ProfileForAction(false).Mode==0;
            profile.Keys.Clear();
            HexBox.Text = previousHex;
            BrightnessSlider.Value = 100;
            SelectKey(47);HexBox.Text="#AABBCC";
            uiChecks["singleKeyPaint"]=profile.KeyColors.GetValueOrDefault(47)=="#AABBCC";
            UndoClicked(this,new RoutedEventArgs());
            uiChecks["undoPerKeyPaint"]=!profile.KeyColors.ContainsKey(47);
            RedoClicked(this,new RoutedEventArgs());
            uiChecks["redoPerKeyPaint"]=profile.KeyColors.GetValueOrDefault(47)=="#AABBCC";
            BindingChoice.Text="LCtrl+Up";AssignBindingClicked(this,new RoutedEventArgs());
            uiChecks["assignShortcut"]=profile.Keys.GetValueOrDefault(47)=="LCtrl+Up";
            CheckVisualEditing(uiChecks,output);
            EditorTabs.SelectedIndex=1;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            CaptureSmokeImage(Path.Combine(output,"key-mapping.png"));
            var libraryProfile=profile.Clone();
            libraryProfile.Macros.Add(new(){Name="Example macro",Events=[new(){Kind="KeyDown",Value="A"},new(){Kind="KeyUp",Value="A"}]});
            libraryProfile.Effects.Add(new(){Name="Example lighting",Frames=[new(){Data=new(){{47,"#AABBCC"}}}]});
            foreach(var window in new Window[]{new MacroWindow(libraryProfile),new EffectWindow(libraryProfile)})
            {
                window.Owner=this;window.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                var capture=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);capture.Render(window);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(capture));using(var file=File.Create(Path.Combine(output,window is MacroWindow?"macros.png":"effects.png")))encoder.Save(file);
                window.Close();
            }
            profile=libraryProfile;profile.EffectId=profile.Effects[0].Id;AnimatePreview.IsChecked=true;
            uiChecks["previewRunsWhenEnabled"]=previewTimer.IsEnabled;
            WindowState=WindowState.Minimized;
            uiChecks["previewStopsWhenMinimized"]=!previewTimer.IsEnabled;
            uiChecks["nativeMinimizeHidesToTray"]=WindowState==WindowState.Minimized&&!ShowInTaskbar&&!IsVisible&&tray is not null&&!closing;
            RestoreFromTray();
            uiChecks["trayRestoresWindow"]=WindowState==WindowState.Normal&&ShowInTaskbar&&IsVisible;
            WindowState=WindowState.Minimized;
            uiChecks["nativeMinimizeAlsoUsesTray"]=!IsVisible&&!ShowInTaskbar&&tray is not null;
            RestoreFromTray();AnimatePreview.IsChecked=false;
            profile=initialProfile;SyncProfileControls();SelectAllClicked(this,new RoutedEventArgs());EditorTabs.SelectedIndex=0;
            uiChecks["fullBrandName"]=Title=="WaiveGK6XPlus"&&System.Reflection.Assembly.GetExecutingAssembly().GetName().Name=="WaiveGK6XPlus";
            BrightnessSlider.ApplyTemplate();LayerSelector.ApplyTemplate();BindingChoice.ApplyTemplate();
            uiChecks["themedSliderHasWorkingTrack"]=BrightnessSlider.Template.FindName("PART_Track",BrightnessSlider) is System.Windows.Controls.Primitives.Track;
            uiChecks["editableDropdownRetainsTextBox"]=BindingChoice.Template.FindName("PART_EditableTextBox",BindingChoice) is TextBox;
            LayerSelector.IsDropDownOpen=true;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var popup=(System.Windows.Controls.Primitives.Popup)LayerSelector.Template.FindName("PART_Popup",LayerSelector);
            File.WriteAllText(Path.Combine(output,"dropdown-state.json"),JsonSerializer.Serialize(new{comboOpen=LayerSelector.IsDropDownOpen,popupOpen=popup.IsOpen,LayerSelector.IsEnabled,LayerSelector.IsVisible,state=WindowState.ToString(),width=(popup.Child as FrameworkElement)?.ActualWidth}));
            uiChecks["styledDropdownOpens"]=popup.IsOpen&&popup.Child is FrameworkElement child&&child.ActualWidth>0;
            if(popup.Child is FrameworkElement panel){var capture=new RenderTargetBitmap((int)panel.ActualWidth,(int)panel.ActualHeight,96,96,PixelFormats.Pbgra32);capture.Render(panel);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(capture));using var file=File.Create(Path.Combine(output,"dropdown.png"));encoder.Save(file);}
            LayerSelector.IsDropDownOpen=false;
            File.WriteAllText(Path.Combine(output,"ui-checks.json"),JsonSerializer.Serialize(uiChecks,new JsonSerializerOptions{WriteIndented=true}));
            if (uiChecks.Values.Any(value => !value)) throw new InvalidOperationException("UI smoke checks failed: "+string.Join(", ",uiChecks.Where(c=>!c.Value).Select(c=>c.Key)));
            dirty=false;undo.Clear();redo.Clear();RefreshProfileStatus();
            }
            var startupMs = startup.ElapsedMilliseconds;
            var process = Process.GetCurrentProcess();
            WindowState = WindowState.Minimized;
            await Task.Delay(5000);
            process.Refresh(); var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
            await Task.Delay(20000);
            process.Refresh();
            var report = new { startupMilliseconds = startupMs, minimizedSeconds = watch.Elapsed.TotalSeconds, minimizedCpuOneCorePercent = (process.TotalProcessorTime - cpu).TotalSeconds / watch.Elapsed.TotalSeconds * 100, privateBytes = process.PrivateMemorySize64, workingSetBytes = process.WorkingSet64, layoutKeycaps = keycaps.Count, detectedDevice = identity, uiChecks, framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, deviceEvents = SnapshotLog() };
            File.WriteAllText(Path.Combine(output, "smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            // Main-window capture follows the idle sample; editor captures are exercised earlier.
            RestoreFromTray();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var image = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(this); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(Path.Combine(output, "window.png"))) png.Save(file);
        }
        catch (Exception error) { dirty=false;File.WriteAllText(Path.Combine(output, "smoke-error.txt"), error.ToString()); }
        finally { Close(); }
    }
    void CaptureSmokeImage(string path)
    {
        var capture=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);capture.Render(this);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(capture));using var file=File.Create(path);encoder.Save(file);
    }
}


