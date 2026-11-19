using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Markdig;

namespace MdViewer;

public partial class MainWindow : Window
{
    sealed record HistoryEntry(string Path, int Line, double Delta);

    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    readonly AppState _state = StateStore.Load();
    readonly ObservableCollection<Project> _projects;
    readonly Dictionary<string, TreeViewItem> _treeItems = new(StringComparer.OrdinalIgnoreCase);
    readonly List<HistoryEntry> _history = new();
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    int _historyIndex = -1;
    Project? _project;
    string? _currentFile;
    MarkdownRenderer? _renderer;
    bool _loading;
    bool _suppressTree;

    List<(Avalonia.Controls.SelectableTextBlock Block, int Start, int Length)> _matches = new();
    int _matchIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        _projects = new ObservableCollection<Project>(_state.Projects.Where(p => Directory.Exists(p.Path)));
        ProjectCombo.ItemsSource = _projects;
        ApplyTheme();

        MainGrid.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(_state.TreeWidth, 120, 800));

        BackButton.Click += (_, _) => Navigate(-1);
        ForwardButton.Click += (_, _) => Navigate(1);
        OpenButton.Click += async (_, _) => await AddFolderAsync();
        RemoveButton.Click += (_, _) => RemoveProject();
        SearchButton.Click += (_, _) => ToggleSearch(true);
        CloseSearch.Click += (_, _) => ToggleSearch(false);
        NextMatch.Click += (_, _) => StepMatch(1);
        PrevMatch.Click += (_, _) => StepMatch(-1);
        SearchBox.TextChanged += (_, _) => RunSearch();
        ThemeButton.Click += (_, _) => ToggleTheme();
        ProjectCombo.SelectionChanged += (_, _) =>
        {
            if (ProjectCombo.SelectedItem is Project p) OpenProject(p);
        };
        Tree.SelectionChanged += OnTreeSelection;
        Scroll.ScrollChanged += (_, _) =>
        {
            if (_loading) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SavePosition();
        };
        KeyDown += OnKeyDown;
        Closing += (_, _) =>
        {
            _state.TreeWidth = MainGrid.ColumnDefinitions[0].ActualWidth;
            SavePosition();
        };
        Opened += (_, _) =>
        {
            var last = _projects.FirstOrDefault(p => string.Equals(p.Path, _state.LastProjectPath, StringComparison.OrdinalIgnoreCase))
                       ?? _projects.FirstOrDefault();
            if (last != null) ProjectCombo.SelectedItem = last;
        };
        UpdateNavButtons();
    }


    // ---------- Proyectos ----------

    async Task AddFolderAsync()
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleccioná la carpeta con archivos .md",
            AllowMultiple = false
        });
        var path = result.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;

        var project = _projects.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        if (project == null)
        {
            project = new Project { Name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/')), Path = path };
            if (string.IsNullOrEmpty(project.Name)) project.Name = path;
            _projects.Add(project);
            PersistProjects();
        }
        ProjectCombo.SelectedItem = project;
        if (_project != project) OpenProject(project);
    }

    void RemoveProject()
    {
        if (_project == null) return;
        var removed = _project;
        _project = null;
        _currentFile = null;
        _projects.Remove(removed);
        Host.Child = null;
        Tree.Items.Clear();
        _treeItems.Clear();
        _history.Clear();
        _historyIndex = -1;
        UpdateNavButtons();
        PersistProjects();
        Title = "MdViewer";
    }

    void PersistProjects()
    {
        _state.Projects = _projects.ToList();
        _state.LastProjectPath = _project?.Path;
        StateStore.Save(_state);
    }

    void OpenProject(Project project)
    {
        SavePosition();
        _project = project;
        _state.LastProjectPath = project.Path;
        _history.Clear();
        _historyIndex = -1;
        _currentFile = null;

        BuildTree(project);

        var file = project.LastFile != null && File.Exists(project.LastFile) ? project.LastFile : FirstFile(project.Path);
        if (file == null)
        {
            Host.Child = null;
            Status.Text = "La carpeta no contiene archivos .md";
            UpdateNavButtons();
            StateStore.Save(_state);
            return;
        }
        OpenFile(file, project.LastFile == file ? project.LastLine : 0, project.LastFile == file ? project.LastDelta : 0, null, true);
    }

    static string? FirstFile(string dir)
    {
        var readme = Directory.EnumerateFiles(dir, "README.md").FirstOrDefault();
        if (readme != null) return readme;
        return Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Split('\\', '/').Any(s => s.StartsWith('.') || s is "node_modules"))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    void BuildTree(Project project)
    {
        _treeItems.Clear();
        Tree.Items.Clear();
        var root = FileNode.Build(project.Path);
        if (root == null) return;
        foreach (var child in root.Children) Tree.Items.Add(ToItem(child));
    }

    TreeViewItem ToItem(FileNode node)
    {
        var item = new TreeViewItem { Header = (node.IsFile ? "📄 " : "📁 ") + node.Name, Tag = node.IsFile ? node.FullPath : null };
        if (node.IsFile) _treeItems[node.FullPath] = item;
        foreach (var c in node.Children) item.Items.Add(ToItem(c));
        return item;
    }

    void OnTreeSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressTree) return;
        if (Tree.SelectedItem is TreeViewItem { Tag: string path } && !SamePath(path, _currentFile))
            OpenFile(path, 0, 0, null, true);
    }

    void SelectInTree(string path)
    {
        if (!_treeItems.TryGetValue(path, out var item)) return;
        _suppressTree = true;
        try
        {
            for (var p = item.Parent as TreeViewItem; p != null; p = p.Parent as TreeViewItem) p.IsExpanded = true;
            Tree.SelectedItem = item;
        }
        finally { _suppressTree = false; }
    }

    static bool SamePath(string? a, string? b) =>
        a != null && b != null && string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // ---------- Apertura / navegación ----------

    void OpenFile(string path, int line, double delta, string? anchor, bool pushHistory)
    {
        if (_currentFile != null) SavePosition();
        if (pushHistory)
        {
            if (_historyIndex >= 0) _history[_historyIndex] = CaptureEntry() ?? _history[_historyIndex];
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            _history.Add(new HistoryEntry(path, line, delta));
            _historyIndex = _history.Count - 1;
        }
        LoadFile(path, line, delta, anchor);
        UpdateNavButtons();
    }

    void Navigate(int step)
    {
        var target = _historyIndex + step;
        if (target < 0 || target >= _history.Count) return;
        if (_historyIndex >= 0) _history[_historyIndex] = CaptureEntry() ?? _history[_historyIndex];
        SavePosition();
        _historyIndex = target;
        var e = _history[target];
        LoadFile(e.Path, e.Line, e.Delta, null);
        UpdateNavButtons();
    }

    void UpdateNavButtons()
    {
        BackButton.IsEnabled = _historyIndex > 0;
        ForwardButton.IsEnabled = _historyIndex >= 0 && _historyIndex < _history.Count - 1;
    }

    void LoadFile(string path, int line, double delta, string? anchor)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex)
        {
            Status.Text = $"No se pudo abrir {path}: {ex.Message}";
            return;
        }

        _loading = true;
        _currentFile = path;
        var doc = Markdown.Parse(text, Pipeline);
        _renderer = new MarkdownRenderer(System.IO.Path.GetDirectoryName(path)!, _state.Dark, HandleLink);
        Host.Child = _renderer.Render(doc);
        _matches.Clear();
        _matchIndex = -1;

        var rel = _project != null ? System.IO.Path.GetRelativePath(_project.Path, path) : path;
        Title = $"{_project?.Name} — {rel} — MdViewer";
        Status.Text = rel;
        SelectInTree(path);

        Dispatcher.UIThread.Post(() =>
        {
            Scroll.UpdateLayout();
            if (anchor != null && _renderer.Headings.TryGetValue(anchor, out var h))
                ScrollTo(YOf(h));
            else
                RestoreLine(line, delta);
            _loading = false;
            if (SearchBar.IsVisible) RunSearch();
            SavePosition();
        }, DispatcherPriority.Loaded);
    }

    double YOf(Control c) => c.TranslatePoint(default, Host)?.Y ?? 0;

    void ScrollTo(double y) => Scroll.Offset = new Vector(0, Math.Max(0, y));

    void RestoreLine(int line, double delta)
    {
        if (_renderer == null) return;
        var candidates = _renderer.Anchors.Where(a => a.Line == line).ToList();
        if (candidates.Count == 0)
            candidates = _renderer.Anchors.Where(a => a.Line <= line).GroupBy(a => a.Line).OrderByDescending(g => g.Key).FirstOrDefault()?.ToList() ?? new();
        if (candidates.Count == 0)
        {
            ScrollTo(delta);
            return;
        }
        var best = candidates.MaxBy(a => YOf(a.Control)).Control;
        ScrollTo(YOf(best) + delta + Host.Margin.Top);
    }

    HistoryEntry? CaptureEntry()
    {
        if (_currentFile == null || _renderer == null) return null;
        var (line, delta) = CurrentPosition();
        return new HistoryEntry(_currentFile, line, delta);
    }

    (int Line, double Delta) CurrentPosition()
    {
        var offset = Scroll.Offset.Y - Host.Margin.Top;
        Control? best = null;
        var bestY = double.MinValue;
        var bestLine = 0;
        foreach (var (line, control) in _renderer!.Anchors)
        {
            var y = YOf(control);
            if (y <= offset + 1 && (y > bestY || (y == bestY && line > bestLine)))
            {
                best = control;
                bestY = y;
                bestLine = line;
            }
        }
        return best == null ? (0, Scroll.Offset.Y) : (bestLine, offset - bestY);
    }

    void SavePosition()
    {
        if (_project == null || _currentFile == null || _renderer == null || _loading) return;
        var (line, delta) = CurrentPosition();
        _project.LastFile = _currentFile;
        _project.LastLine = line;
        _project.LastDelta = delta;
        _state.Projects = _projects.ToList();
        _state.LastProjectPath = _project.Path;
        StateStore.Save(_state);
    }

    void HandleLink(string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            Launch(url);
            return;
        }

        var hash = url.IndexOf('#');
        var pathPart = Uri.UnescapeDataString(hash >= 0 ? url[..hash] : url);
        var frag = hash >= 0 ? Uri.UnescapeDataString(url[(hash + 1)..]).ToLowerInvariant() : null;

        if (pathPart.Length == 0)
        {
            if (frag != null && _renderer != null && _renderer.Headings.TryGetValue(frag, out var h))
                ScrollTo(YOf(h));
            return;
        }
        if (_currentFile == null) return;

        string full;
        try { full = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_currentFile)!, pathPart)); }
        catch { Status.Text = $"Enlace inválido: {url}"; return; }

        if (File.Exists(full))
        {
            if (full.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || full.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            {
                if (SamePath(full, _currentFile) && frag != null && _renderer!.Headings.TryGetValue(frag, out var h))
                    ScrollTo(YOf(h));
                else
                    OpenFile(full, 0, 0, frag, true);
            }
            else Launch(full);
        }
        else Status.Text = $"No se encontró el archivo: {pathPart}";
    }

    void Launch(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    // ---------- Tema ----------

    void ApplyTheme() =>
        Application.Current!.RequestedThemeVariant = _state.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

    void ToggleTheme()
    {
        _state.Dark = !_state.Dark;
        ApplyTheme();
        if (_currentFile != null)
        {
            var (line, delta) = _renderer != null ? CurrentPosition() : (0, 0);
            LoadFile(_currentFile, line, delta, null);
        }
        StateStore.Save(_state);
    }

    // ---------- Búsqueda ----------

    void ToggleSearch(bool show)
    {
        SearchBar.IsVisible = show;
        if (show)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            RunSearch();
        }
        else ClearSelection();
    }

    void ClearSelection()
    {
        if (_matchIndex >= 0 && _matchIndex < _matches.Count) _matches[_matchIndex].Block.ClearSelection();
    }

    void RunSearch()
    {
        ClearSelection();
        _matches = new();
        _matchIndex = -1;
        var q = SearchBox.Text;
        if (_renderer != null && !string.IsNullOrEmpty(q))
        {
            foreach (var (block, text) in _renderer.Texts)
            {
                var i = 0;
                while ((i = text.IndexOf(q, i, StringComparison.CurrentCultureIgnoreCase)) >= 0)
                {
                    _matches.Add((block, i, q.Length));
                    i += q.Length;
                }
            }
            _matches = _matches.OrderBy(m => YOf(m.Block)).ThenBy(m => m.Start).ToList();
        }
        MatchCount.Text = string.IsNullOrEmpty(q) ? "" : $"{_matches.Count} resultados";
        if (_matches.Count > 0) ShowMatch(0);
    }

    void StepMatch(int step)
    {
        if (_matches.Count == 0) return;
        ShowMatch((_matchIndex + step + _matches.Count) % _matches.Count);
    }

    void ShowMatch(int index)
    {
        ClearSelection();
        _matchIndex = index;
        var (block, start, length) = _matches[index];
        block.SelectionStart = start;
        block.SelectionEnd = start + length;
        MatchCount.Text = $"{index + 1} de {_matches.Count}";

        var y = YOf(block);
        try { y += block.TextLayout.HitTestTextPosition(start).Y; } catch { }
        ScrollTo(y - Scroll.Viewport.Height / 3 + Host.Margin.Top);
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ToggleSearch(true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBar.IsVisible)
        {
            ToggleSearch(false);
            e.Handled = true;
        }
        else if ((e.Key == Key.Enter && SearchBox.IsFocused) || e.Key == Key.F3)
        {
            StepMatch(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            Navigate(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            Navigate(1);
            e.Handled = true;
        }
    }
}
