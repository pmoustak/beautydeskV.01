using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace BeautyDesk;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, "beautydesk-single-instance", out bool first);
        if (!first) return;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { Log(e.Exception.ToString()); e.Handled = true; };
        var host = new Host();
        app.Startup += (_, _) => host.Start();
        app.Run();
    }

    public static void Log(string text)
    {
        try { Directory.CreateDirectory(Config.Dir); File.AppendAllText(Path.Combine(Config.Dir, "error.log"), $"{DateTime.Now:s} {text}\n"); } catch { }
    }
}

// ---------- config ----------

public sealed class Project
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Action { get; set; } = "open";
    public string Args { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "";
}

public sealed class Row
{
    public string Title { get; set; } = "";
    public bool Pinned { get; set; }
    public List<Project> Projects { get; set; } = new();
}

public sealed class PanelConfig
{
    public string Monitor { get; set; } = "";    // screen device name; "" = main screen
    public string Edge { get; set; } = "top";    // top, right, bottom, left
    public string Image { get; set; } = "";      // this panel's background picture; "" = the main one
    public List<Row> Rows { get; set; } = new();
}

public sealed class Config
{
    public List<PanelConfig> Panels { get; set; } = new();
    public List<Row> Rows { get; set; }           // older format: becomes the first panel
    public List<Project> Projects { get; set; }   // oldest format: becomes its first row
    public double Opacity { get; set; } = 0.92;
    public string Color { get; set; } = "#16181E";
    public string Image { get; set; } = "";
    public int AnimationMs { get; set; } = 260;
    public int HideDelayMs { get; set; } = 700;
    public int TriggerDelayMs { get; set; } = 100;

    public static readonly string Dir = Environment.GetEnvironmentVariable("BEAUTYDESK_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "beautydesk");
    public static readonly string FilePath = Path.Combine(Dir, "config.json");

    const string Header = """
        // beautydesk settings. The gear button on a panel edits most of this; the rest can be edited here.
        //
        // panels: each panel sits on one "edge" (top, right, bottom, left) of a "monitor" ("" = main screen),
        //         can have its own background "image" ("" = the main one) and has its own "rows".
        //         Each row has a "title", "pinned" (true keeps it on screen) and "projects".
        // Each project:
        //   "name"   : label under the icon
        //   "path"   : folder, file, .exe, .bat, .ps1, .lnk or URL (%USERPROFILE% and other variables work)
        //   "action" : "open" (default), "vscode", "explorer", "terminal"
        //   "args"   : optional arguments for "open"
        //   "icon"   : optional. A name (folder, code, book, globe, terminal, bank, chart, pie, db, gear, game,
        //              music, photo, video, doc, mail, cart, box, work, home, star, heart, bolt, cloud, wrench,
        //              people, school, calendar, puzzle, palette, bug, shield, robot, chat, flask, layers,
        //              translate, monitor, phone, key, apps), 1-2 letters, or a .png/.ico file path
        //   "color"  : optional accent, e.g. "#6EA8FE"
        // opacity: 0.1 to 1. color: panel colour. image: optional background picture for every panel.
        """;

    static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static Config Load(out string error)
    {
        error = null;
        Directory.CreateDirectory(Dir);
        if (!File.Exists(FilePath)) Save(Normalize(new Config()));
        try
        {
            return Normalize(JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), ReadOptions) ?? new Config());
        }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }
    }

    static Config Normalize(Config c)
    {
        c.Panels ??= new();
        if (c.Panels.Count == 0) c.Panels.Add(new PanelConfig());
        var first = c.Panels[0];
        first.Rows ??= new();
        if (c.Rows is { Count: > 0 }) first.Rows.InsertRange(0, c.Rows);
        c.Rows = null;
        if (c.Projects is { Count: > 0 })
        {
            if (first.Rows.Count == 0) first.Rows.Add(new Row { Title = "Projects" });
            first.Rows[0].Projects.InsertRange(0, c.Projects);
        }
        c.Projects = null;
        foreach (var p in c.Panels)
        {
            p.Monitor ??= "";
            p.Image ??= "";
            p.Edge =Monitors.Edges.Contains(p.Edge?.ToLowerInvariant()) ? p.Edge.ToLowerInvariant() : "top";
            p.Rows ??= new();
            if (p.Rows.Count == 0) p.Rows.Add(new Row { Title = "Projects" });
            foreach (var r in p.Rows)
            {
                r.Projects ??= new();
                r.Title ??= "";
            }
        }
        c.Opacity = Math.Clamp(c.Opacity, 0.1, 1);
        c.Color = string.IsNullOrWhiteSpace(c.Color) ? "#16181E" : c.Color.Trim();
        c.Image ??= "";
        return c;
    }

    public static void Save(Config c)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        sb.AppendLine("{");
        sb.AppendLine("  \"panels\": [");
        for (int n = 0; n < c.Panels.Count; n++)
        {
            var panel = c.Panels[n];
            sb.AppendLine("    {");
            sb.AppendLine($"      \"monitor\": {Str(panel.Monitor)},");
            sb.AppendLine($"      \"edge\": {Str(panel.Edge)},");
            sb.AppendLine($"      \"image\": {Str(panel.Image)},");
            sb.AppendLine("      \"rows\": [");
            for (int r = 0; r < panel.Rows.Count; r++)
            {
                var row = panel.Rows[r];
                sb.AppendLine("        {");
                sb.AppendLine($"          \"title\": {Str(row.Title)},");
                sb.AppendLine($"          \"pinned\": {(row.Pinned ? "true" : "false")},");
                sb.AppendLine("          \"projects\": [");
                for (int i = 0; i < row.Projects.Count; i++)
                    sb.Append("            ").Append(Line(row.Projects[i])).AppendLine(i < row.Projects.Count - 1 ? "," : "");
                sb.AppendLine("          ]");
                sb.AppendLine(r < panel.Rows.Count - 1 ? "        }," : "        }");
            }
            sb.AppendLine("      ]");
            sb.AppendLine(n < c.Panels.Count - 1 ? "    }," : "    }");
        }
        sb.AppendLine("  ],");
        sb.AppendLine($"  \"opacity\": {c.Opacity.ToString("0.##", CultureInfo.InvariantCulture)},");
        sb.AppendLine($"  \"color\": {Str(c.Color)},");
        sb.AppendLine($"  \"image\": {Str(c.Image)},");
        sb.AppendLine($"  \"animationMs\": {c.AnimationMs.ToString(CultureInfo.InvariantCulture)},");
        sb.AppendLine($"  \"hideDelayMs\": {c.HideDelayMs.ToString(CultureInfo.InvariantCulture)},");
        sb.AppendLine($"  \"triggerDelayMs\": {c.TriggerDelayMs.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine("}");
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, sb.ToString());
        File.Move(tmp, FilePath, overwrite: true);
    }

    static string Str(string s) => JsonSerializer.Serialize(s ?? "", Readable);

    static string Line(Project p)
    {
        var parts = new List<string>();
        void Add(string key, string value, string skip = "")
        {
            if (!string.IsNullOrEmpty(value) && value != skip) parts.Add($"\"{key}\": {Str(value)}");
        }
        Add("name", p.Name);
        Add("path", p.Path);
        Add("action", p.Action, "open");
        Add("args", p.Args);
        Add("icon", p.Icon);
        Add("color", p.Color);
        return "{ " + string.Join(", ", parts) + " }";
    }
}

// ---------- screens ----------

static class Monitors
{
    public static readonly string[] Edges = { "top", "right", "bottom", "left" }; // clockwise

    public static string Clockwise(string edge) => Edges[(Array.IndexOf(Edges, edge) + 1) % Edges.Length];
    public static Forms.Screen[] All => Forms.Screen.AllScreens;
    public static double Scale => Forms.Screen.PrimaryScreen.Bounds.Width / SystemParameters.PrimaryScreenWidth;

    public static Forms.Screen Find(string key) =>
        string.IsNullOrEmpty(key) ? Forms.Screen.PrimaryScreen : All.FirstOrDefault(s => s.DeviceName == key) ?? Forms.Screen.PrimaryScreen;

    public static string Key(Forms.Screen s) => s.Primary ? "" : s.DeviceName;
    public static bool Same(string a, string b) => Find(a).DeviceName == Find(b).DeviceName;

    /// <summary>Screen pixels to WPF units (the app is system-DPI aware, so one factor fits every screen).</summary>
    public static Rect Dip(Drawing.Rectangle r)
    {
        double s = Scale;
        return new Rect(r.Left / s, r.Top / s, r.Width / s, r.Height / s);
    }

    public static string Describe(PanelConfig p)
    {
        var screen = Find(p.Monitor);
        int number = Array.FindIndex(All, s => s.DeviceName == screen.DeviceName) + 1;
        var main = Forms.Screen.PrimaryScreen.Bounds;
        var b = screen.Bounds;
        string where = screen.Primary ? "main" : b.Right <= main.Left ? "left" : b.Left >= main.Right ? "right" : b.Bottom <= main.Top ? "above" : "below";
        return $"{char.ToUpperInvariant(p.Edge[0])}{p.Edge[1..]} of screen {number} ({where})";
    }
}

// ---------- launching ----------

public static class Launcher
{
    public static string Expand(string s) => Environment.ExpandEnvironmentVariables((s ?? "").Trim());
    static string Q(string s) => "\"" + s + "\"";
    public static bool IsUrl(string s) => s.Contains("://") || s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    public static void Start(Project p)
    {
        string path = Expand(p.Path), args = Expand(p.Args);
        if (path.Length == 0) throw new InvalidOperationException("No path set for this project in config.json.");
        bool url = IsUrl(path);
        bool rooted = !url && Path.IsPathRooted(path);
        if (rooted && !File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Not found: " + path);

        string dir = Directory.Exists(path) ? path : rooted ? Path.GetDirectoryName(path) : null;
        var psi = (p.Action ?? "open").ToLowerInvariant() switch
        {
            "vscode" => VsCode(path),
            "explorer" => new ProcessStartInfo("explorer.exe", Directory.Exists(path) ? Q(path) : "/select," + Q(path)),
            "terminal" => new ProcessStartInfo("wt.exe", "-d " + Q((dir ?? path).TrimEnd('\\') + (dir?.Length == 3 ? "\\" : ""))) { UseShellExecute = true },
            _ when path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) =>
                new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File {Q(path)} {args}") { UseShellExecute = true, WorkingDirectory = dir },
            _ => new ProcessStartInfo(path, args) { UseShellExecute = true, WorkingDirectory = url ? null : dir },
        };
        Native.AllowSetForegroundWindow(-1); // let the launched app come to the front
        Process.Start(psi)?.Dispose();
    }

    static ProcessStartInfo VsCode(string path)
    {
        string exe = FindCode();
        return exe != null
            ? new ProcessStartInfo(exe, Q(path)) { UseShellExecute = false }
            : new ProcessStartInfo("cmd.exe", "/c code " + Q(path)) { UseShellExecute = false, CreateNoWindow = true };
    }

    static string FindCode()
    {
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                if (d.Length > 0 && File.Exists(Path.Combine(d, "code.cmd")))
                {
                    var exe = Path.GetFullPath(Path.Combine(d, "..", "Code.exe"));
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { }
        }
        foreach (var c in new[] { @"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe", @"%ProgramFiles%\Microsoft VS Code\Code.exe" })
            if (File.Exists(Expand(c))) return Expand(c);
        return null;
    }
}

public static class Startup
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "beautydesk";
    static string Command => "\"" + Environment.ProcessPath + "\"";

    public static bool IsOn
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(Key); return (k?.GetValue(Name) as string) == Command; }
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        if (on) k.SetValue(Name, Command); else k.DeleteValue(Name, false);
    }
}

// ---------- host: tray, edge watcher, moving panels ----------

sealed class PanelState
{
    public PanelWindow Win;
    public long DwellStart = -1, LeaveStart = -1, ManualUntil;
    public bool Entered;
}

sealed class Host
{
    readonly List<PanelState> panels = new();
    SettingsWindow settings;
    Forms.NotifyIcon tray;
    Config cfg = new();
    DateTime cfgStamp;
    DispatcherTimer saveTimer;
    readonly Stopwatch clock = Stopwatch.StartNew();

    // moving a panel to another edge
    PanelState moving;
    Window ghost, marker;
    Vector grab;
    string dropMonitor, dropEdge;

    public void Start()
    {
        MakeTray();
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        saveTimer.Tick += (_, _) => SaveNow();
        ghost = Overlay();
        marker = Overlay();
        marker.Content = new Border { Background = new SolidColorBrush(PanelWindow.Hex("#6EA8FE")), CornerRadius = new CornerRadius(3) };
        Reload();

        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        if (Environment.GetEnvironmentVariable("BEAUTYDESK_DEMO") == "1") OpenSettings(null); // for screenshots without the mouse
    }

    void Notify(string title, string text) => tray.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Warning);

    /// <summary>Picks up hand edits of config.json. Returns false if the file has an error.</summary>
    bool Reload()
    {
        if (settings != null || saveTimer.IsEnabled || moving != null) return true; // unsaved changes in memory win
        var stamp = File.Exists(Config.FilePath) ? File.GetLastWriteTimeUtc(Config.FilePath) : DateTime.MinValue;
        if (stamp == cfgStamp && stamp != DateTime.MinValue && panels.Count > 0) return true;
        var loaded = Config.Load(out var error);
        cfgStamp = File.GetLastWriteTimeUtc(Config.FilePath);
        if (loaded == null)
        {
            Notify("config.json has an error", error + "\nKeeping the previous settings.");
            return false;
        }
        cfg = loaded;
        SyncPanels();
        return true;
    }

    /// <summary>One window per configured panel, each placed on its edge.</summary>
    void SyncPanels()
    {
        while (panels.Count > cfg.Panels.Count) { panels[^1].Win.Close(); panels.RemoveAt(panels.Count - 1); }
        while (panels.Count < cfg.Panels.Count) panels.Add(new PanelState { Win = NewPanel() });
        for (int i = 0; i < panels.Count; i++) panels[i].Win.Load(cfg, cfg.Panels[i]);
    }

    PanelWindow NewPanel()
    {
        var win = new PanelWindow();
        win.LaunchRequested = p =>
        {
            try { Launcher.Start(p); win.Retract(); }
            catch (Exception e) { Notify($"Couldn't open {p.Name}", e.Message); }
        };
        win.AddRequested = row => PickFiles(win, row);
        win.FilesDropped = (files, row) => AddFiles(win, files, row);
        win.PinToggled = row => TogglePin(win, row);
        win.SettingsRequested = () => OpenSettings(win.Pc);
        win.AddRowRequested = () => AddRow(win);
        win.RemoveRequested = (row, p) => Remove(win, row, p);
        win.DragOpenRequested = () => Open(State(win), manual: false);
        win.MoveStarted = () => BeginMove(State(win));
        win.MoveMoved = MoveTo;
        win.MoveEnded = EndMove;
        return win;
    }

    PanelState State(PanelWindow win) => panels.First(s => s.Win == win);
    PanelState State(PanelConfig pc) => panels.FirstOrDefault(s => s.Win.Pc == pc);

    void SaveSoon()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    void SaveNow()
    {
        saveTimer.Stop();
        try
        {
            Config.Save(cfg);
            cfgStamp = File.GetLastWriteTimeUtc(Config.FilePath);
        }
        catch (Exception e) { Notify("Couldn't save the settings", e.Message); }
    }

    // ----- edits from the panels -----

    void TogglePin(PanelWindow win, int row)
    {
        if (row < 0 || row >= win.Pc.Rows.Count) return;
        win.Pc.Rows[row].Pinned = !win.Pc.Rows[row].Pinned;
        win.Load(cfg, win.Pc);
        settings?.RefreshRows();
        SaveSoon();
    }

    void AddRow(PanelWindow win)
    {
        if (!Reload()) return;
        win.Pc.Rows.Add(new Row { Title = $"Row {win.Pc.Rows.Count + 1}" });
        win.Load(cfg, win.Pc);
        settings?.RefreshRows();
        SaveSoon();
    }

    void Remove(PanelWindow win, int row, Project project)
    {
        if (!Reload() || row < 0 || row >= win.Pc.Rows.Count) return;
        var items = win.Pc.Rows[row].Projects;
        int i = items.IndexOf(project);
        if (i < 0) i = items.FindIndex(p => p.Path == project.Path && p.Name == project.Name); // config was reloaded meanwhile
        if (i < 0) return;
        items.RemoveAt(i);
        win.Load(cfg, win.Pc);
        SaveNow();
    }

    void PickFiles(PanelWindow win, int row)
    {
        var dialog = new OpenFileDialog { Multiselect = true, DereferenceLinks = false, Title = "Add to the panel" };
        if (dialog.ShowDialog() == true) AddFiles(win, dialog.FileNames, row);
    }

    /// <summary>Adds files, folders or shortcuts (dropped from the desktop or picked) to a row.</summary>
    void AddFiles(PanelWindow win, string[] files, int row)
    {
        if (!Reload()) return;
        var rows = win.Pc.Rows;
        row = Math.Clamp(row, 0, rows.Count - 1);
        var known = cfg.Panels.SelectMany(p => p.Rows).SelectMany(r => r.Projects).Select(p => Launcher.Expand(p.Path)).ToList();
        bool added = false;
        foreach (var file in files)
        {
            if (known.Any(k => string.Equals(k, file, StringComparison.OrdinalIgnoreCase))) continue;
            string name = Directory.Exists(file) ? new DirectoryInfo(file).Name : Path.GetFileNameWithoutExtension(file);
            rows[row].Projects.Add(new Project { Name = name, Path = file });
            added = true;
        }
        if (!added) return;
        win.Load(cfg, win.Pc);
        SaveNow();
    }

    // ----- settings -----

    void OpenSettings(PanelConfig pc)
    {
        if (settings != null) { settings.Select(pc ?? settings.Selected); settings.Activate(); return; }
        Reload();
        pc ??= cfg.Panels[0];
        settings = new SettingsWindow(this, cfg, pc);
        settings.Closed += (_, _) =>
        {
            settings = null;
            if (saveTimer.IsEnabled) SaveNow();
        };
        var st = State(pc);
        Open(st, manual: true);
        PlaceSettings(st.Win);
        settings.Show();
        settings.Activate();
    }

    /// <summary>Next to the open panel, so the live preview stays visible.</summary>
    void PlaceSettings(PanelWindow win)
    {
        var wa = Monitors.Dip(win.Screen.WorkingArea);
        double w = settings.Width, h = 720;
        double left = wa.Left + (wa.Width - w) / 2, top = wa.Top + (wa.Height - h) / 2;
        switch (win.Pc.Edge)
        {
            case "top": top = win.Top + win.Height - 28; h = Math.Min(h, wa.Bottom - top - 12); break;
            case "bottom": h = Math.Min(h, win.Top + 28 - wa.Top - 12); top = win.Top + 28 - h; break;
            case "left": left = win.Left + win.Width - 28; break;
            case "right": left = win.Left + 28 - w; break;
        }
        if (h < 360) { h = Math.Min(720, wa.Height - 24); top = wa.Top + 12; }
        settings.Height = h;
        settings.Left = Math.Clamp(left, wa.Left, Math.Max(wa.Left, wa.Right - w));
        settings.Top = Math.Clamp(top, wa.Top, Math.Max(wa.Top, wa.Bottom - h));
    }

    public void AppearanceChanged()
    {
        foreach (var st in panels) st.Win.ApplyAppearance(cfg);
        SaveSoon();
    }

    public void RowsChanged(PanelConfig pc)
    {
        State(pc)?.Win.Load(cfg, pc);
        SaveSoon();
    }

    public void PanelSelected(PanelConfig pc)
    {
        foreach (var st in panels) if (st.Win.Pc != pc && st.Win.IsOpen) st.Win.Retract();
        if (State(pc) is PanelState selected) Open(selected, manual: true);
    }

    /// <summary>A new panel goes to the first free edge, main screen first. It starts pinned so you can see and move it.</summary>
    public PanelConfig AddPanel()
    {
        string monitor = "", edge = "top";
        bool found = false;
        foreach (var screen in Monitors.All.OrderByDescending(s => s.Primary))
        {
            foreach (var e in Monitors.Edges)
            {
                if (cfg.Panels.Any(p => Monitors.Same(p.Monitor, Monitors.Key(screen)) && p.Edge == e)) continue;
                (monitor, edge, found) = (Monitors.Key(screen), e, true);
                break;
            }
            if (found) break;
        }
        var pc = new PanelConfig { Monitor = monitor, Edge = edge, Rows = { new Row { Title = "Projects", Pinned = true } } };
        cfg.Panels.Add(pc);
        SyncPanels();
        SaveSoon();
        return pc;
    }

    /// <summary>Removes a panel; its rows move to another panel so nothing is lost.</summary>
    public void RemovePanel(PanelConfig pc)
    {
        if (cfg.Panels.Count < 2) return;
        var keep = cfg.Panels.First(p => p != pc);
        keep.Rows.AddRange(pc.Rows);
        cfg.Panels.Remove(pc);
        SyncPanels();
        SaveSoon();
    }

    // ----- moving a panel -----

    void BeginMove(PanelState st)
    {
        if (moving != null || !Native.GetCursorPos(out var pt)) return;
        moving = st;
        var r = st.Win.PanelBounds;
        double s = Monitors.Scale;
        grab = new Vector(pt.X / s - r.Left, pt.Y / s - r.Top);
        ghost.Content = new Image { Source = st.Win.Snapshot(), Opacity = 0.85 };
        ghost.Width = r.Width;
        ghost.Height = r.Height;
        ghost.Left = r.Left;
        ghost.Top = r.Top;
        ghost.Show();
        st.Win.SetMoving(true);
        MoveTo();
    }

    /// <summary>The ghost follows the cursor; a bar marks the nearest edge of the screen under it.</summary>
    void MoveTo()
    {
        if (moving == null || !Native.GetCursorPos(out var pt)) return;
        double s = Monitors.Scale;
        ghost.Left = pt.X / s - grab.X;
        ghost.Top = pt.Y / s - grab.Y;

        var screen = Forms.Screen.FromPoint(new Drawing.Point(pt.X, pt.Y));
        var b = screen.Bounds;
        var distances = new (string Edge, int Distance)[]
        {
            ("top", pt.Y - b.Top), ("right", b.Right - 1 - pt.X), ("bottom", b.Bottom - 1 - pt.Y), ("left", pt.X - b.Left),
        };
        dropEdge = distances.MinBy(d => d.Distance).Edge;
        dropMonitor = Monitors.Key(screen);

        var wa = Monitors.Dip(screen.WorkingArea);
        bool across = dropEdge is "top" or "bottom";
        double length = across ? Math.Min(Math.Max(ghost.Width, 240), wa.Width - 40) : Math.Min(Math.Max(ghost.Height, 320), wa.Height - 40);
        marker.Width = across ? length : 6;
        marker.Height = across ? 6 : length;
        marker.Left = dropEdge switch { "left" => wa.Left + 3, "right" => wa.Right - 9, _ => wa.Left + (wa.Width - length) / 2 };
        marker.Top = dropEdge switch { "top" => wa.Top + 3, "bottom" => wa.Bottom - 9, _ => wa.Top + (wa.Height - length) / 2 };
        if (!marker.IsVisible) marker.Show();
    }

    void EndMove()
    {
        if (moving == null) return;
        var st = moving;
        moving = null;
        ghost.Hide();
        marker.Hide();
        st.Win.SetMoving(false);
        if (dropEdge == null) return;
        Dock(st.Win.Pc, dropMonitor, dropEdge);
        dropEdge = null;
        SyncPanels();
        settings?.RefreshPanels();
        SaveNow();
    }

    /// <summary>Puts a panel on an edge. A panel already there shifts clockwise to the next free edge of that screen.</summary>
    void Dock(PanelConfig pc, string monitor, string edge)
    {
        if (Monitors.Same(pc.Monitor, monitor) && pc.Edge == edge) return;
        string oldMonitor = pc.Monitor, oldEdge = pc.Edge;
        var occupant = cfg.Panels.FirstOrDefault(p => p != pc && Monitors.Same(p.Monitor, monitor) && p.Edge == edge);
        pc.Monitor = monitor;
        pc.Edge = edge;
        if (occupant == null) return;
        string next = edge;
        for (int k = 0; k < 3; k++)
        {
            next = Monitors.Clockwise(next);
            if (cfg.Panels.Any(p => p != occupant && Monitors.Same(p.Monitor, monitor) && p.Edge == next)) continue;
            occupant.Edge = next;
            return;
        }
        occupant.Monitor = oldMonitor; // every edge of that screen is taken: swap places
        occupant.Edge = oldEdge;
    }

    static Window Overlay()
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
            ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize, Width = 10, Height = 10,
        };
        w.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(w).Handle;
            const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            Native.SetWindowLong(h, GWL_EXSTYLE, Native.GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        return w;
    }

    // ----- watching the screen edges -----

    void Tick()
    {
        if (moving != null || !Native.GetCursorPos(out var pt)) return;
        double s = Monitors.Scale;
        var cursor = new Point(pt.X / s, pt.Y / s);
        bool buttons = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x02) & 0x8000) != 0;
        long now = clock.ElapsedMilliseconds;
        foreach (var st in panels.ToList()) TickPanel(st, pt, cursor, buttons, now);
    }

    void TickPanel(PanelState st, Native.POINT pt, Point cursor, bool buttons, long now)
    {
        var win = st.Win;
        if (win.Screen == null) return;
        bool onScreen = win.Screen.Bounds.Contains(pt.X, pt.Y);
        bool fullscreen = Native.IsFullscreenApp(win.Screen.Bounds);
        bool inTrigger = onScreen && win.InTrigger(cursor);

        if (!win.IsOpen)
        {
            win.Suppress(fullscreen); // pinned rows step aside for games and fullscreen video
            // With a button held, open only for drags (icons from the desktop), never while moving a window.
            bool allowed = !buttons || !Native.IsMovingWindow();
            if (inTrigger && allowed && !fullscreen)
            {
                if (st.DwellStart < 0) st.DwellStart = now;
                int delay = Math.Max(cfg.TriggerDelayMs, buttons ? 250 : 0);
                if (win.Pc.Edge == "bottom") delay = Math.Max(delay, 300); // the taskbar lives there
                if (now - st.DwellStart >= delay) { st.DwellStart = -1; Open(st, manual: false); }
            }
            else st.DwellStart = -1;
            return;
        }

        if (settings != null) { st.LeaveStart = -1; return; }   // stays open while you personalise it
        bool inside = onScreen && (inTrigger || win.InKeepZone(cursor));
        if (inside) { st.Entered = true; st.LeaveStart = -1; return; }
        if (!st.Entered && now < st.ManualUntil) return;        // opened from the tray: give the cursor time to arrive
        if (st.LeaveStart < 0) st.LeaveStart = now;
        if (now - st.LeaveStart >= cfg.HideDelayMs) win.Retract();
    }

    void Open(PanelState st, bool manual)
    {
        Reload();
        st.Entered = false;
        st.LeaveStart = -1;
        st.ManualUntil = manual ? clock.ElapsedMilliseconds + 4000 : 0;
        st.Win.Open();
    }

    void OpenConfig()
    {
        if (saveTimer.IsEnabled) SaveNow();
        Config.Load(out _); // creates the file on first use
        try { Process.Start(new ProcessStartInfo(Config.FilePath) { UseShellExecute = true }); }
        catch { Process.Start("notepad.exe", "\"" + Config.FilePath + "\""); }
    }

    // ----- tray -----

    void MakeTray()
    {
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new DarkRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Font = new Drawing.Font("Segoe UI", 9.5f),
        };
        var title = new Forms.ToolStripMenuItem("beautydesk") { Font = new Drawing.Font("Segoe UI", 9.5f, Drawing.FontStyle.Bold) };
        title.Click += (_, _) => OpenFirst();
        var startup = new Forms.ToolStripMenuItem("Start with Windows") { Checked = Startup.IsOn };
        startup.Click += (_, _) => { Startup.Set(!Startup.IsOn); startup.Checked = Startup.IsOn; };

        menu.Items.Add(title);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Open panel", null, (_, _) => OpenFirst());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings(null));
        menu.Items.Add("Edit config file…", null, (_, _) => OpenConfig());
        menu.Items.Add("Open config folder", null, (_, _) => Process.Start("explorer.exe", "\"" + Config.Dir + "\""));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            if (saveTimer.IsEnabled) SaveNow();
            tray.Visible = false;
            tray.Dispose();
            Application.Current.Shutdown();
        });
        menu.Opening += (_, _) => startup.Checked = Startup.IsOn;

        tray = new Forms.NotifyIcon { Icon = AppIcon(), Text = "beautydesk", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenFirst(); };
    }

    void OpenFirst()
    {
        if (panels.Count > 0) Open(panels[0], manual: true);
    }

    /// <summary>The blue pixel lightning bolt (beautydesk.ico, also the exe's icon) at tray size.</summary>
    static Drawing.Icon AppIcon()
    {
        using var stream = AppIconStream();
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public static Stream AppIconStream() => typeof(Host).Assembly.GetManifestResourceStream("beautydesk.ico");
}

// ---------- a panel ----------

sealed class PanelWindow : Window
{
    public Action<Project> LaunchRequested;
    public Action<int> AddRequested;
    public Action<string[], int> FilesDropped;
    public Action<int> PinToggled;
    public Action SettingsRequested;
    public Action AddRowRequested;
    public Action<int, Project> RemoveRequested;
    public Action DragOpenRequested;
    public Action MoveStarted, MoveMoved, MoveEnded;

    public bool IsOpen { get; private set; }
    public PanelConfig Pc { get; private set; } = new();
    public Forms.Screen Screen { get; private set; }

    readonly TranslateTransform slide = new();
    readonly Border panel, scrim;
    readonly StackPanel rowsStack = new();
    readonly List<Border> wraps = new();   // one per row, then the "add row" button
    Config cfg = new();
    string edge = "top";
    BitmapImage image;
    string imagePath;
    ImageBrush imageBrush;
    bool light, suppressed;
    int animMs = 260;
    Color textMain, textHover, textMuted, washColor;
    SolidColorBrush dropBrush;

    bool Columns => edge is "left" or "right";      // side panels: each row is a column
    bool FarEdge => edge is "bottom" or "right";     // rows grow away from the edge
    DependencyProperty Axis => Columns ? WidthProperty : HeightProperty;
    DependencyProperty SlideAxis => Columns ? TranslateTransform.XProperty : TranslateTransform.YProperty;

    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    static readonly Color Blue = Hex("#6EA8FE");
    static readonly string[] Palette = { "#6EA8FE", "#A78BFA", "#2DD4BF", "#FBBF24", "#FB7185", "#4ADE80", "#38BDF8", "#FB923C" };
    static readonly Dictionary<string, string> Glyphs = new()
    {
        ["folder"] = "", ["code"] = "", ["book"] = "", ["library"] = "", ["globe"] = "",
        ["terminal"] = "", ["bank"] = "", ["chart"] = "", ["pie"] = "", ["db"] = "",
        ["gear"] = "", ["game"] = "", ["music"] = "", ["photo"] = "", ["video"] = "",
        ["doc"] = "", ["mail"] = "", ["cart"] = "", ["box"] = "", ["work"] = "",
        ["home"] = "", ["star"] = "", ["heart"] = "", ["bolt"] = "", ["cloud"] = "",
        ["wrench"] = "", ["people"] = "", ["school"] = "", ["calendar"] = "", ["puzzle"] = "",
        ["palette"] = "", ["bug"] = "", ["shield"] = "", ["robot"] = "", ["chat"] = "",
        ["flask"] = "", ["layers"] = "", ["translate"] = "", ["monitor"] = "", ["phone"] = "",
        ["key"] = "", ["apps"] = "", ["add"] = "",
    };

    public PanelWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 400;
        Height = 300;
        Title = "beautydesk panel";
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        UseLayoutRounding = true;

        scrim = new Border { CornerRadius = new CornerRadius(19), Padding = new Thickness(16, 12, 16, 14), Child = rowsStack };
        panel = new Border
        {
            Child = scrim,
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(1),
            MinWidth = 120,
            RenderTransform = slide,
            Opacity = 0,
            Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black },
        };
        Content = panel;
        panel.SizeChanged += (_, _) => FitImage();

        // Drop files, folders or shortcuts (e.g. desktop icons) onto a row to add them there.
        panel.AllowDrop = true;
        panel.DragEnter += OnDrag;
        panel.DragOver += OnDrag;
        panel.DragLeave += (_, _) => Highlight(-1);
        panel.Drop += (_, e) =>
        {
            int row = RowAt(e);
            Highlight(-1);
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) FilesDropped?.Invoke(files, row);
            e.Handled = true;
        };

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            Native.SetWindowLong(h, GWL_EXSTYLE, Native.GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        ApplyAppearance(cfg, rebuild: false);
    }

    /// <summary>The panel's rectangle on screen, in WPF units, as currently laid out.</summary>
    public Rect PanelBounds
    {
        get
        {
            var offset = VisualTreeHelper.GetOffset(panel);
            return new Rect(Left + offset.X, Top + offset.Y, panel.ActualWidth, panel.ActualHeight);
        }
    }

    public void Load(Config c, PanelConfig pc)
    {
        var screen = Monitors.Find(pc.Monitor);
        bool moved = Screen != null && (Screen.DeviceName != screen.DeviceName || edge != pc.Edge);
        if (moved)
        {
            // Docked somewhere else: vanish here, then slide in from the new edge.
            IsOpen = false;
            if (IsVisible) Hide();
        }
        cfg = c;
        Pc = pc;
        Screen = screen;
        edge = pc.Edge;
        animMs = Math.Clamp(c.AnimationMs, 80, 1500);
        ApplyAppearance(c, rebuild: false);
        Place();
        Rebuild();
    }

    void Place()
    {
        rowsStack.Orientation = Columns ? Orientation.Horizontal : Orientation.Vertical;
        (panel.HorizontalAlignment, panel.VerticalAlignment, panel.Margin) = edge switch
        {
            "bottom" => (HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 40, 0, 10)),
            "left" => (HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(10, 0, 40, 0)),
            "right" => (HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(40, 0, 10, 0)),
            _ => (HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, 10, 0, 40)),
        };
    }

    // ----- look -----

    public void ApplyAppearance(Config c, bool rebuild = true)
    {
        cfg = c;
        double opacity = Math.Clamp(c.Opacity, 0.1, 1);
        bool hasImage = false;
        string path = Launcher.Expand(string.IsNullOrWhiteSpace(Pc.Image) ? c.Image : Pc.Image); // this panel's picture, else the main one
        if (path.Length > 0 && File.Exists(path))
        {
            try
            {
                if (image == null || imagePath != path)
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 1600;
                    bmp.EndInit();
                    bmp.Freeze();
                    image = bmp;
                    imagePath = path;
                }
                imageBrush = new ImageBrush(image) { Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute, Opacity = opacity };
                panel.Background = imageBrush;
                scrim.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0, 0, 0)); // keeps labels readable
                hasImage = true;
                FitImage();
            }
            catch (Exception e) { Program.Log("background image: " + e.Message); }
        }
        var colour = TryHex(c.Color, out var parsed) ? parsed : Hex("#16181E");
        if (!hasImage)
        {
            image = null;
            imagePath = null;
            imageBrush = null;
            byte a = (byte)(opacity * 255);
            panel.Background = new LinearGradientBrush(WithAlpha(Mix(colour, Colors.White, 0.04), a), WithAlpha(colour, a), 90);
            scrim.Background = null;
        }

        bool wasLight = light;
        light = !hasImage && Luminance(colour) > 0.55;
        textMain = Hex(light ? "#24272E" : "#CDD1D9");
        textHover = light ? Colors.Black : Colors.White;
        textMuted = Hex(light ? "#5C616B" : "#9AA0AB");
        washColor = light ? Colors.Black : Colors.White;
        dropBrush = new SolidColorBrush(WithAlpha(washColor, 0x16));
        panel.BorderBrush = new SolidColorBrush(light ? Hex("#26000000") : Hex("#2AFFFFFF"));
        if (rebuild && light != wasLight) Rebuild();
    }

    /// <summary>The image is anchored at the panel's edge, so more rows reveal more of it.</summary>
    void FitImage()
    {
        if (imageBrush == null || image == null || panel.ActualWidth <= 0 || image.PixelHeight == 0) return;
        double width = panel.ActualWidth, height = panel.ActualHeight, aspect = (double)image.PixelWidth / image.PixelHeight;
        double w, h;
        if (Columns) { h = Math.Max(height, width / aspect); w = h * aspect; }
        else { w = Math.Max(width, height * aspect); h = w / aspect; }
        double x = edge == "right" ? width - w : edge == "left" ? 0 : (width - w) / 2;
        double y = edge == "bottom" ? height - h : edge == "top" ? 0 : (height - h) / 2;
        imageBrush.Viewport = new Rect(x, y, w, h);
    }

    // ----- rows -----

    void Rebuild()
    {
        rowsStack.Children.Clear();
        wraps.Clear();
        var rows = Pc.Rows;
        int head = rows.FindIndex(r => r.Pinned);   // the gear (and move handle) live on the first pinned row
        bool anyPinned = head >= 0;
        if (head < 0) head = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int index = i;
            var tools = new List<FrameworkElement>();
            if (i == head)
            {
                tools.Add(IconButton("", false, () => SettingsRequested?.Invoke()));
                if (anyPinned) tools.Add(MoveHandle());   // moving is possible while the panel is pinned
            }
            var title = new TextBlock
            {
                Text = row.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(textMuted),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var pin = IconButton("", row.Pinned, () => PinToggled?.Invoke(index));

            var cards = new WrapPanel { Orientation = Columns ? Orientation.Vertical : Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            if (Columns) cards.MaxHeight = Math.Max(240, Monitors.Dip(Screen.WorkingArea).Height - 200);
            else cards.MaxWidth = 8 * 108;
            int n = 0;
            foreach (var p in row.Projects)
            {
                var accent = Accent(p.Color, n++);
                cards.Children.Add(Card(p.Name, IconFor(p, accent), accent, () => LaunchRequested?.Invoke(p), () => RemoveRequested?.Invoke(index, p)));
            }
            cards.Children.Add(Card("Add", GlyphText(Glyphs["add"], textMuted), textMuted, () => AddRequested?.Invoke(index), remove: null));

            var content = new StackPanel { Margin = Columns ? new Thickness(4, 0, 4, 0) : new Thickness(0, 4, 0, 4) };
            content.Children.Add(Columns ? ColumnHeader(tools, title, pin) : RowHeader(tools, title, pin));
            content.Children.Add(cards);
            AddWrap(content, row.Pinned);
        }

        // "Add row" sits beyond the last row (a new column on side panels) and slides away with the unpinned rows.
        var addRow = IconButton("", false, () => AddRowRequested?.Invoke());
        addRow.Margin = new Thickness(4);
        addRow.HorizontalAlignment = Columns ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        addRow.VerticalAlignment = Columns ? VerticalAlignment.Top : VerticalAlignment.Center;
        AddWrap(addRow, pinned: false);

        foreach (var w in FarEdge ? Enumerable.Reverse(wraps) : wraps) rowsStack.Children.Add(w);
        FitWindow();
        if (!IsOpen) SyncClosed();
    }

    static FrameworkElement RowHeader(List<FrameworkElement> tools, TextBlock title, FrameworkElement pin)
    {
        double side = tools.Count > 1 ? 64 : 32;   // equal sides keep the title centred
        var header = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(side) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(side) });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var t in tools) left.Children.Add(t);
        header.Children.Add(left);
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        pin.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(pin, 2);
        header.Children.Add(pin);
        return header;
    }

    static FrameworkElement ColumnHeader(List<FrameworkElement> tools, TextBlock title, FrameworkElement pin)
    {
        var buttons = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(pin, Dock.Right);
        buttons.Children.Add(pin);
        foreach (var t in tools) { DockPanel.SetDock(t, Dock.Left); buttons.Children.Add(t); }
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(buttons);
        title.Margin = new Thickness(0, 4, 0, 0);
        header.Children.Add(title);
        return header;
    }

    void AddWrap(FrameworkElement content, bool pinned)
    {
        var wrap = new Border { Child = content, ClipToBounds = true, CornerRadius = new CornerRadius(12) };
        if (!IsOpen && !pinned) wrap.SetValue(Axis, 0.0);
        if (!IsOpen && !pinned) wrap.Opacity = 0;
        wraps.Add(wrap);
    }

    /// <summary>The window is a strip along its edge, as deep as the fully open panel; rows animate inside it.</summary>
    void FitWindow()
    {
        var wa = Monitors.Dip(Screen.WorkingArea);
        double depth = wraps.Sum(FullExtent) + 10 + 40 + 2 + (Columns ? 32 : 26) + 12;
        if (Columns)
        {
            Width = Math.Min(depth, wa.Width);
            Height = wa.Height;
            Top = wa.Top;
            Left = edge == "left" ? wa.Left : wa.Right - Width;
        }
        else
        {
            Width = wa.Width;
            Height = Math.Min(depth, wa.Height);
            Left = wa.Left;
            Top = edge == "top" ? wa.Top : wa.Bottom - Height;
        }
    }

    double FullExtent(Border wrap)
    {
        var child = (FrameworkElement)wrap.Child;
        child.Measure(Columns ? new Size(double.PositiveInfinity, Monitors.Dip(Screen.WorkingArea).Height) : new Size(2000, double.PositiveInfinity));
        return Columns ? child.DesiredSize.Width : child.DesiredSize.Height;
    }

    double Extent(Border w) => Columns ? w.ActualWidth : w.ActualHeight;
    bool Collapsed(Border w) => !double.IsNaN((double)w.GetValue(Axis));
    bool IsPinned(int i) => i < Pc.Rows.Count && Pc.Rows[i].Pinned;

    void Expand(Border wrap)
    {
        var a = new DoubleAnimation(Extent(wrap), FullExtent(wrap), TimeSpan.FromMilliseconds(animMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var axis = Axis;
        wrap.Tag = a;
        a.Completed += (_, _) =>
        {
            if (wrap.Tag != a) return;
            wrap.BeginAnimation(axis, null);
            wrap.SetValue(axis, double.NaN);
        };
        wrap.BeginAnimation(axis, a);
        Animate(wrap, OpacityProperty, 1, animMs, EasingMode.EaseOut);
    }

    void Collapse(Border wrap)
    {
        var a = new DoubleAnimation(Extent(wrap), 0, TimeSpan.FromMilliseconds(animMs * 0.8)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        wrap.Tag = a;
        wrap.BeginAnimation(Axis, a);
        Animate(wrap, OpacityProperty, 0, (int)(animMs * 0.5), EasingMode.EaseOut);
    }

    void Reset(Border wrap, bool open)
    {
        wrap.Tag = null;
        wrap.BeginAnimation(WidthProperty, null);
        wrap.BeginAnimation(HeightProperty, null);
        wrap.BeginAnimation(OpacityProperty, null);
        wrap.Width = double.NaN;
        wrap.Height = double.NaN;
        if (!open) wrap.SetValue(Axis, 0.0);
        wrap.Opacity = open ? 1 : 0;
    }

    // ----- sliding in and out -----

    double HiddenOffset => edge switch
    {
        "top" => -(panel.ActualHeight + 20),
        "bottom" => panel.ActualHeight + 20,
        "left" => -(panel.ActualWidth + 20),
        _ => panel.ActualWidth + 20,
    };

    void ShowHidden()
    {
        Show();
        UpdateLayout();
        slide.BeginAnimation(TranslateTransform.XProperty, null);
        slide.BeginAnimation(TranslateTransform.YProperty, null);
        slide.X = 0;
        slide.Y = 0;
        slide.SetValue(SlideAxis, HiddenOffset);
        panel.BeginAnimation(OpacityProperty, null);
        panel.Opacity = 0;
    }

    void SlideIn()
    {
        Animate(slide, SlideAxis, 0, animMs, EasingMode.EaseOut);
        Animate(panel, OpacityProperty, 1, (int)(animMs * 0.6), EasingMode.EaseOut);
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        if (!IsVisible)
        {
            foreach (var w in wraps) Reset(w, open: true);
            ShowHidden();
        }
        else
        {
            foreach (var w in wraps) if (Collapsed(w)) Expand(w);
        }
        SlideIn();
    }

    public void Retract()
    {
        if (!IsOpen) return;
        IsOpen = false;
        SyncClosed();
    }

    /// <summary>Hides the pinned rows while a fullscreen app is in front.</summary>
    public void Suppress(bool on)
    {
        if (on == suppressed) return;
        suppressed = on;
        if (on) { if (IsVisible && !IsOpen) Hide(); }
        else SyncClosed();
    }

    /// <summary>Closed state: pinned rows stay on screen, everything else slides away.</summary>
    void SyncClosed()
    {
        if (IsOpen) return;
        if (!Pc.Rows.Any(r => r.Pinned) || suppressed)
        {
            if (!IsVisible) return;
            if (suppressed) { Hide(); return; }
            int ms = (int)(animMs * 0.8);
            var a = new DoubleAnimation(HiddenOffset, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, _) => { if (!IsOpen && !Pc.Rows.Any(r => r.Pinned)) Hide(); };
            slide.BeginAnimation(SlideAxis, a);
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms * 0.6)) { BeginTime = TimeSpan.FromMilliseconds(ms * 0.4) });
            return;
        }

        if (!IsVisible)
        {
            for (int i = 0; i < wraps.Count; i++) Reset(wraps[i], IsPinned(i));
            ShowHidden();
        }
        SlideIn();
        for (int i = 0; i < wraps.Count; i++)
        {
            var w = wraps[i];
            if (IsPinned(i)) { if (Collapsed(w)) Expand(w); }
            else if (!Collapsed(w) || Extent(w) > 0) Collapse(w);
        }
    }

    // ----- edge zones -----

    /// <summary>The cursor is pushing against this panel's screen edge, beside the panel.</summary>
    public bool InTrigger(Point c)
    {
        var b = Monitors.Dip(Screen.Bounds);
        var r = PanelBounds;
        if (!Columns)
        {
            double center = Left + Width / 2, half = Math.Max(r.Width, 480) / 2;
            bool atEdge = edge == "top" ? c.Y <= b.Top + 1 : c.Y >= b.Bottom - 2;
            return atEdge && Math.Abs(c.X - center) <= half;
        }
        else
        {
            double center = Top + Height / 2, half = Math.Max(r.Height, 320) / 2;
            bool atEdge = edge == "left" ? c.X <= b.Left + 1 : c.X >= b.Right - 2;
            return atEdge && Math.Abs(c.Y - center) <= half;
        }
    }

    /// <summary>On the panel, or between it and its edge.</summary>
    public bool InKeepZone(Point c)
    {
        var r = PanelBounds;
        r.Inflate(24, 24);
        return edge switch
        {
            "top" => c.X >= r.Left && c.X <= r.Right && c.Y <= r.Bottom,
            "bottom" => c.X >= r.Left && c.X <= r.Right && c.Y >= r.Top,
            "left" => c.Y >= r.Top && c.Y <= r.Bottom && c.X <= r.Right,
            _ => c.Y >= r.Top && c.Y <= r.Bottom && c.X >= r.Left,
        };
    }

    // ----- moving -----

    FrameworkElement MoveHandle()
    {
        var text = new TextBlock
        {
            Text = "", FontFamily = IconFont, FontSize = 13, Foreground = new SolidColorBrush(textMuted),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var wash = new SolidColorBrush(washColor) { Opacity = 0 };
        var handle = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = wash, Child = text, Cursor = Cursors.SizeAll,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        bool dragging = false;
        handle.MouseEnter += (_, _) => wash.Opacity = 0.1;
        handle.MouseLeave += (_, _) => wash.Opacity = 0;
        handle.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            dragging = handle.CaptureMouse();
            if (dragging) MoveStarted?.Invoke();
        };
        handle.MouseMove += (_, _) => { if (dragging) MoveMoved?.Invoke(); };
        handle.MouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            e.Handled = true;
            dragging = false;
            handle.ReleaseMouseCapture();
            MoveEnded?.Invoke();
        };
        handle.LostMouseCapture += (_, _) =>
        {
            if (!dragging) return;
            dragging = false;
            MoveEnded?.Invoke();
        };
        return handle;
    }

    /// <summary>While moving, the real panel is invisible (it keeps the mouse) and a ghost follows the cursor.</summary>
    public void SetMoving(bool on)
    {
        panel.BeginAnimation(OpacityProperty, null);
        panel.Opacity = on ? 0 : 1;
    }

    public BitmapSource Snapshot()
    {
        int w = Math.Max(1, (int)Math.Ceiling(panel.ActualWidth)), h = Math.Max(1, (int)Math.Ceiling(panel.ActualHeight));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(panel) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h), Stretch = Stretch.Fill }, null, new Rect(0, 0, w, h));
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // ----- drag and drop of files -----

    void OnDrag(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = files ? DragDropEffects.Link : DragDropEffects.None;
        if (files && !IsOpen) DragOpenRequested?.Invoke(); // dragging onto the pinned bar opens every row
        Highlight(files ? RowAt(e) : -1);
        e.Handled = true;
    }

    int RowAt(DragEventArgs e)
    {
        var p = e.GetPosition(rowsStack);
        double pos = Columns ? p.X : p.Y;
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < Pc.Rows.Count && i < wraps.Count; i++)
        {
            double extent = Extent(wraps[i]);
            if (extent < 1) continue;
            var origin = wraps[i].TranslatePoint(new Point(0, 0), rowsStack);
            double start = Columns ? origin.X : origin.Y;
            if (pos >= start && pos <= start + extent) return i;
            double distance = Math.Abs(pos - (start + extent / 2));
            if (distance < bestDistance) { bestDistance = distance; best = i; }
        }
        return best;
    }

    void Highlight(int row)
    {
        for (int i = 0; i < wraps.Count; i++) wraps[i].Background = i == row ? dropBrush : null;
    }

    // ----- pieces -----

    FrameworkElement IconButton(string glyph, bool active, Action click)
    {
        var text = new TextBlock
        {
            Text = glyph, FontFamily = IconFont, FontSize = 13, Foreground = new SolidColorBrush(active ? Blue : textMuted),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var wash = new SolidColorBrush(active ? Blue : washColor) { Opacity = active ? 0.2 : 0 };
        var button = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = wash, Child = text, Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        button.MouseEnter += (_, _) => wash.Opacity = active ? 0.3 : 0.1;
        button.MouseLeave += (_, _) => wash.Opacity = active ? 0.2 : 0;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    FrameworkElement Card(string name, UIElement icon, Color accent, Action click, Action remove)
    {
        bool add = remove == null;
        bool neutral = add || icon is Image; // real app icons sit on a plain tile
        var glow = new DropShadowEffect { Color = accent, BlurRadius = 22, ShadowDepth = 0, Opacity = 0 };
        var tile = new Border
        {
            Width = 56, Height = 56, CornerRadius = new CornerRadius(16), Child = icon,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = neutral ? new SolidColorBrush(WithAlpha(washColor, 0x0D)) : new LinearGradientBrush(WithAlpha(accent, 0x55), WithAlpha(accent, 0x18), 45),
            BorderBrush = neutral ? new SolidColorBrush(WithAlpha(washColor, 0x33)) : new SolidColorBrush(WithAlpha(accent, 0x70)),
            BorderThickness = new Thickness(1),
            Effect = glow,
        };
        var label = new TextBlock
        {
            Text = name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(textMain),
            TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 9, 0, 0), MaxWidth = 92,
        };

        // Small ✕ on the tile's top-right corner, shown while hovering: removes the item from its row.
        var tileArea = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        tileArea.Children.Add(tile);
        Border close = null;
        if (!add)
        {
            var closeFill = new SolidColorBrush(Hex("#2A2D35"));
            close = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = closeFill,
                BorderBrush = new SolidColorBrush(Hex("#55FFFFFF")), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -6, -6, 0), Cursor = Cursors.Hand, Visibility = Visibility.Hidden, Opacity = 0,
                Child = new TextBlock
                {
                    Text = "", FontFamily = IconFont, FontSize = 8, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            close.MouseEnter += (_, _) => closeFill.Color = Hex("#E5484D");
            close.MouseLeave += (_, _) => closeFill.Color = Hex("#2A2D35");
            close.MouseLeftButtonDown += (_, e) => e.Handled = true;          // no press animation, no launch
            close.MouseLeftButtonUp += (_, e) => { e.Handled = true; remove(); };
            tileArea.Children.Add(close);
        }

        var body = new StackPanel();
        body.Children.Add(tileArea);
        body.Children.Add(label);

        var wash = new SolidColorBrush(washColor) { Opacity = 0 };
        var scale = new ScaleTransform(1, 1);
        var card = new Border
        {
            Width = 104, Padding = new Thickness(6, 10, 6, 8), Margin = new Thickness(2), CornerRadius = new CornerRadius(14),
            Background = wash, Child = body, RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5), Cursor = Cursors.Hand,
        };

        void Hover(bool on)
        {
            Animate(scale, ScaleTransform.ScaleXProperty, on ? 1.06 : 1, 140, EasingMode.EaseOut);
            Animate(scale, ScaleTransform.ScaleYProperty, on ? 1.06 : 1, 140, EasingMode.EaseOut);
            Animate(wash, System.Windows.Media.Brush.OpacityProperty, on ? 0.07 : 0, 140, EasingMode.EaseOut);
            Animate(glow, DropShadowEffect.OpacityProperty, on ? 0.75 : 0, 180, EasingMode.EaseOut);
            label.Foreground = new SolidColorBrush(on ? textHover : textMain);
            if (close != null)
            {
                if (on) close.Visibility = Visibility.Visible;
                var fade = new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(120));
                if (!on) fade.Completed += (_, _) => { if (!card.IsMouseOver) close.Visibility = Visibility.Hidden; };
                close.BeginAnimation(OpacityProperty, fade);
            }
        }

        card.MouseEnter += (_, _) => Hover(true);
        card.MouseLeave += (_, _) => Hover(false);
        card.MouseLeftButtonDown += (_, _) =>
        {
            Animate(scale, ScaleTransform.ScaleXProperty, 0.97, 80, EasingMode.EaseOut);
            Animate(scale, ScaleTransform.ScaleYProperty, 0.97, 80, EasingMode.EaseOut);
        };
        card.MouseLeftButtonUp += (_, _) => { Hover(card.IsMouseOver); if (card.IsMouseOver) click(); };
        return card;
    }

    static UIElement IconFor(Project p, Color accent)
    {
        string icon = (p.Icon ?? "").Trim();
        string path = Launcher.Expand(p.Path);
        if (icon.Length == 0)
        {
            string action = (p.Action ?? "").ToLowerInvariant();
            icon = action == "vscode" ? "code"
                 : action == "terminal" ? "terminal"
                 : Launcher.IsUrl(path) ? "globe"
                 : Directory.Exists(path) ? "folder" : "";
            if (icon.Length == 0 && File.Exists(path) && Native.ShellIcon(path) is BitmapSource shell)
            {
                var img = new Image { Source = shell, Width = 40, Height = 40 };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                return img; // the same icon the desktop shows
            }
        }
        if (Glyphs.TryGetValue(icon.ToLowerInvariant(), out var glyph)) return GlyphText(glyph, Lighten(accent));

        var file = Launcher.Expand(icon);
        if (icon.Length > 4 && File.Exists(file))
        {
            try { return new Image { Source = new BitmapImage(new Uri(file)), Width = 34, Height = 34 }; }
            catch { }
        }
        if (icon.Length == 0) icon = Initials(p.Name);
        return new TextBlock
        {
            Text = icon, FontSize = icon.Length > 2 ? 14 : 21, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Lighten(accent)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    static TextBlock GlyphText(string glyph, Color color) => new()
    {
        Text = glyph, FontFamily = IconFont, FontSize = 24,
        Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    static string Initials(string name)
    {
        var parts = (name ?? "?").Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        return parts.Length == 1 ? parts[0][..1].ToUpperInvariant() : (parts[0][..1] + parts[1][..1]).ToUpperInvariant();
    }

    static Color Accent(string hex, int i) => TryHex(hex, out var c) ? c : Hex(Palette[i % Palette.Length]);

    static void Animate(Animatable target, DependencyProperty property, double to, int ms, EasingMode mode) =>
        target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = mode } });

    static void Animate(UIElement target, DependencyProperty property, double to, int ms, EasingMode mode) =>
        target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = mode } });

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static bool TryHex(string hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex) || !hex.Trim().StartsWith("#")) return false;
        try { color = Hex(hex.Trim()); return true; } catch { return false; }
    }

    static Color WithAlpha(Color c, byte a) => System.Windows.Media.Color.FromArgb(a, c.R, c.G, c.B);
    static Color Mix(Color a, Color b, double t) => System.Windows.Media.Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static Color Lighten(Color c) => Mix(c, Colors.White, 0.3);
    static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
}

// ---------- settings ----------

#pragma warning disable WPF0001 // Fluent theme
sealed class SettingsWindow : Window
{
    readonly Host host;
    readonly Config cfg;
    readonly StackPanel panelsList = new(), rowsList = new();
    readonly TextBlock rowsCaption = new() { FontSize = 13, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 8) };
    readonly WrapPanel swatches = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox hex = new() { Width = 110, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock imageName = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock panelImageName = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };

    static readonly string[] Colours = { "#16181E", "#0A0B0E", "#1B2333", "#231C33", "#0F2A27", "#2B1F1A", "#2A2D35", "#EEF0F4" };

    public PanelConfig Selected { get; private set; }

    public SettingsWindow(Host host, Config cfg, PanelConfig selected)
    {
        this.host = host;
        this.cfg = cfg;
        Selected = selected;
        ThemeMode = ThemeMode.Dark;
        Title = "beautydesk settings";
        using (var icon = Host.AppIconStream()) Icon = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Width = 540;
        Height = 720;
        MinWidth = 460;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var root = new StackPanel { Margin = new Thickness(28, 16, 28, 28) };

        root.Children.Add(Heading("Appearance", first: true));

        root.Children.Add(Caption("Transparency"));
        var percent = new TextBlock { Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = 0, Maximum = 90, Value = Math.Round((1 - cfg.Opacity) * 100),
            IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center,
        };
        percent.Text = $"{slider.Value:0} %";
        slider.ValueChanged += (_, e) =>
        {
            percent.Text = $"{e.NewValue:0} %";
            cfg.Opacity = Math.Round(1 - e.NewValue / 100, 2);
            host.AppearanceChanged();
        };
        root.Children.Add(Pair(slider, percent));

        root.Children.Add(Caption("Colour"));
        hex.Text = cfg.Color;
        hex.TextChanged += (_, _) =>
        {
            if (!PanelWindow.TryHex(hex.Text, out _)) return;
            cfg.Color = hex.Text.Trim();
            BuildSwatches();
            host.AppearanceChanged();
        };
        BuildSwatches();
        var colourRow = new DockPanel();
        DockPanel.SetDock(hex, Dock.Right);
        colourRow.Children.Add(hex);
        colourRow.Children.Add(swatches);
        root.Children.Add(colourRow);

        root.Children.Add(Caption("Background image (all panels)"));
        var choose = new Button { Content = "Choose image…" };
        choose.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Background image", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff" };
            if (dialog.ShowDialog(this) != true) return;
            cfg.Image = dialog.FileName;
            UpdateImageName();
            host.AppearanceChanged();
        };
        var remove = new Button { Content = "Remove", Margin = new Thickness(8, 0, 0, 0) };
        remove.Click += (_, _) => { cfg.Image = ""; UpdateImageName(); host.AppearanceChanged(); };
        var imageRow = new StackPanel { Orientation = Orientation.Horizontal };
        imageRow.Children.Add(choose);
        imageRow.Children.Add(remove);
        imageRow.Children.Add(imageName);
        root.Children.Add(imageRow);
        UpdateImageName();

        root.Children.Add(Heading("Panels"));
        root.Children.Add(Caption("Pin a row, then drag a panel by its move button to any edge of any screen."));
        root.Children.Add(panelsList);
        var addPanel = new Button { Content = "Add panel", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        addPanel.Click += (_, _) => Select(host.AddPanel());
        root.Children.Add(addPanel);

        root.Children.Add(Caption("Image for the selected panel"));
        var choosePanelImage = new Button { Content = "Choose image…" };
        choosePanelImage.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Image for this panel", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff" };
            if (dialog.ShowDialog(this) != true) return;
            Selected.Image = dialog.FileName;
            UpdateImageName();
            host.AppearanceChanged();
        };
        var useMain = new Button { Content = "Use the main image", Margin = new Thickness(8, 0, 0, 0) };
        useMain.Click += (_, _) => { Selected.Image = ""; UpdateImageName(); host.AppearanceChanged(); };
        var panelImageRow = new StackPanel { Orientation = Orientation.Horizontal };
        panelImageRow.Children.Add(choosePanelImage);
        panelImageRow.Children.Add(useMain);
        panelImageRow.Children.Add(panelImageName);
        root.Children.Add(panelImageRow);

        root.Children.Add(Heading("Rows"));
        root.Children.Add(rowsCaption);
        root.Children.Add(rowsList);
        var addRow = new Button { Content = "Add row", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        addRow.Click += (_, _) =>
        {
            Selected.Rows.Add(new Row { Title = $"Row {Selected.Rows.Count + 1}" });
            RefreshRows();
            host.RowsChanged(Selected);
        };
        root.Children.Add(addRow);

        root.Children.Add(Heading("General"));
        var startup = new CheckBox { Content = "Start with Windows", IsChecked = Startup.IsOn };
        startup.Click += (_, _) =>
        {
            try { Startup.Set(startup.IsChecked == true); } catch { }
            startup.IsChecked = Startup.IsOn;
        };
        root.Children.Add(startup);
        root.Children.Add(new TextBlock { Text = "beautydesk 0.1 · by Periklis Moustaka", FontSize = 12, Opacity = 0.5, Margin = new Thickness(0, 28, 0, 0) });

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        RefreshPanels();
        RefreshRows();
    }

    public void Select(PanelConfig pc)
    {
        Selected = cfg.Panels.Contains(pc) ? pc : cfg.Panels[0];
        RefreshPanels();
        RefreshRows();
        host.PanelSelected(Selected);
    }

    public void RefreshPanels()
    {
        if (!cfg.Panels.Contains(Selected)) Selected = cfg.Panels[0];
        UpdateImageName();
        panelsList.Children.Clear();
        foreach (var pc in cfg.Panels)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var choice = new RadioButton { Content = Monitors.Describe(pc), GroupName = "panels", IsChecked = pc == Selected, VerticalAlignment = VerticalAlignment.Center };
            choice.Checked += (_, _) => { if (Selected != pc) Select(pc); };
            var remove = new Button { Content = "Remove", IsEnabled = cfg.Panels.Count > 1, VerticalAlignment = VerticalAlignment.Center };
            remove.Click += (_, _) =>
            {
                host.RemovePanel(pc); // its rows move to another panel
                Select(cfg.Panels[0]);
            };
            Grid.SetColumn(remove, 1);
            grid.Children.Add(choice);
            grid.Children.Add(remove);
            panelsList.Children.Add(grid);
        }
    }

    public void RefreshRows()
    {
        if (!cfg.Panels.Contains(Selected)) Selected = cfg.Panels[0];
        rowsCaption.Text = "Rows of the panel at the " + Monitors.Describe(Selected).ToLowerInvariant();
        rowsList.Children.Clear();
        var rows = Selected.Rows;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int index = i;
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBox { Text = row.Title, VerticalContentAlignment = VerticalAlignment.Center };
            title.TextChanged += (_, _) => { row.Title = title.Text; host.RowsChanged(Selected); };
            var pinned = new CheckBox { Content = "Pinned", IsChecked = row.Pinned, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            pinned.Click += (_, _) => { row.Pinned = pinned.IsChecked == true; host.RowsChanged(Selected); };
            var remove = new Button { Content = "Remove", Margin = new Thickness(6, 0, 0, 0), IsEnabled = rows.Count > 1, VerticalAlignment = VerticalAlignment.Center };
            remove.Click += (_, _) =>
            {
                if (rows.Count < 2) return;
                var keep = rows[index == 0 ? 1 : index - 1]; // its projects move to a neighbouring row
                if (index == 0) keep.Projects.InsertRange(0, row.Projects); else keep.Projects.AddRange(row.Projects);
                rows.RemoveAt(index);
                RefreshRows();
                host.RowsChanged(Selected);
            };
            Grid.SetColumn(pinned, 1);
            Grid.SetColumn(remove, 2);
            grid.Children.Add(title);
            grid.Children.Add(pinned);
            grid.Children.Add(remove);
            rowsList.Children.Add(grid);
        }
    }

    void BuildSwatches()
    {
        swatches.Children.Clear();
        foreach (var c in Colours)
        {
            bool selected = string.Equals(c, cfg.Color, StringComparison.OrdinalIgnoreCase);
            var swatch = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(PanelWindow.Hex(c)),
                BorderBrush = new SolidColorBrush(selected ? PanelWindow.Hex("#6EA8FE") : PanelWindow.Hex("#40FFFFFF")),
                BorderThickness = new Thickness(selected ? 2 : 1),
            };
            swatch.MouseLeftButtonUp += (_, _) => hex.Text = c;
            swatches.Children.Add(swatch);
        }
    }

    void UpdateImageName()
    {
        imageName.Text = string.IsNullOrEmpty(cfg.Image) ? "None" : Path.GetFileName(cfg.Image);
        if (Selected != null) panelImageName.Text = string.IsNullOrEmpty(Selected.Image) ? "Main image" : Path.GetFileName(Selected.Image);
    }

    static TextBlock Heading(string text, bool first = false) => new()
    {
        Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, first ? 0 : 28, 0, 4),
    };

    static TextBlock Caption(string text) => new() { Text = text, FontSize = 13, Opacity = 0.75, Margin = new Thickness(0, 14, 0, 6), TextWrapping = TextWrapping.Wrap };

    static Grid Pair(UIElement left, UIElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }
}
#pragma warning restore WPF0001

// ---------- tray menu look ----------

sealed class DarkRenderer : Forms.ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) => RoundedEdges = false;

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Drawing.Color.FromArgb(230, 232, 236);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        e.Graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Drawing.Pen(Drawing.Color.FromArgb(110, 168, 254), 2);
        e.Graphics.DrawLines(pen, new[]
        {
            new Drawing.Point(r.Left + 3, r.Top + r.Height / 2),
            new Drawing.Point(r.Left + r.Width / 2 - 1, r.Bottom - 4),
            new Drawing.Point(r.Right - 3, r.Top + 4),
        });
    }
}

sealed class DarkColors : Forms.ProfessionalColorTable
{
    static readonly Drawing.Color Bg = Drawing.Color.FromArgb(27, 29, 34), Hi = Drawing.Color.FromArgb(46, 49, 58), Line = Drawing.Color.FromArgb(58, 61, 70);
    public override Drawing.Color ToolStripDropDownBackground => Bg;
    public override Drawing.Color ImageMarginGradientBegin => Bg;
    public override Drawing.Color ImageMarginGradientMiddle => Bg;
    public override Drawing.Color ImageMarginGradientEnd => Bg;
    public override Drawing.Color MenuBorder => Line;
    public override Drawing.Color MenuItemBorder => Hi;
    public override Drawing.Color MenuItemSelected => Hi;
    public override Drawing.Color SeparatorDark => Line;
    public override Drawing.Color SeparatorLight => Bg;
    public override Drawing.Color CheckBackground => Bg;
    public override Drawing.Color CheckSelectedBackground => Hi;
    public override Drawing.Color CheckPressedBackground => Hi;
}

// ---------- Win32 ----------

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll")] static extern int SHGetImageList(int list, ref Guid iid, out IntPtr imageList);
    [DllImport("comctl32.dll")] static extern IntPtr ImageList_GetIcon(IntPtr imageList, int index, int flags);

    [StructLayout(LayoutKind.Sequential)]
    struct GUITHREADINFO { public int cbSize, flags; public IntPtr active, focus, capture, menuOwner, moveSize, caret; public RECT caretRect; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string displayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string typeName;
    }

    /// <summary>True when the foreground window covers the whole screen without a title bar (games, F11, videos).</summary>
    public static bool IsFullscreenApp(Drawing.Rectangle screen)
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || h == GetShellWindow()) return false;
        var cls = new StringBuilder(64);
        GetClassName(h, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (!GetWindowRect(h, out var r)) return false;
        bool covers = r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
        const int GWL_STYLE = -16, WS_CAPTION = 0x00C00000;
        return covers && (GetWindowLong(h, GWL_STYLE) & WS_CAPTION) != WS_CAPTION;
    }

    /// <summary>True while the user is dragging or resizing a window (so a drag to the top edge is a window move, not a file).</summary>
    public static bool IsMovingWindow()
    {
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        const int GUI_INMOVESIZE = 0x2;
        return GetGUIThreadInfo(0, ref info) && (info.flags & GUI_INMOVESIZE) != 0;
    }

    /// <summary>The 48 px icon Explorer shows for a file or shortcut.</summary>
    public static BitmapSource ShellIcon(string path)
    {
        var info = new SHFILEINFO();
        const uint SHGFI_SYSICONINDEX = 0x4000;
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX) == IntPtr.Zero) return null;
        var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IImageList
        const int SHIL_EXTRALARGE = 2, ILD_TRANSPARENT = 1;
        if (SHGetImageList(SHIL_EXTRALARGE, ref iid, out var list) != 0) return null;
        var icon = ImageList_GetIcon(list, info.iIcon, ILD_TRANSPARENT);
        if (icon == IntPtr.Zero) return null;
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        finally { DestroyIcon(icon); }
    }
}
