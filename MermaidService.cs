using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace MdViewer;

/// <summary>
/// Renderiza diagramas Mermaid con un WebView nativo oculto y devuelve PNG transparentes.
/// Si el motor no está disponible, RenderAsync devuelve un error y el llamador muestra el código.
/// </summary>
public sealed class MermaidService
{
    public static MermaidService? Instance { get; set; }

    const double Scale = 2;
    static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(20);

    readonly NativeWebView _view;
    readonly TaskCompletionSource _ready = new();
    readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    readonly Dictionary<string, MermaidImage> _cache = new();
    readonly SemaphoreSlim _gate = new(1, 1);
    int _nextId;

    public sealed record MermaidImage(Bitmap Bitmap, double Width, double Height);

    public MermaidService(Panel host)
    {
        _view = new NativeWebView
        {
            Width = 2,
            Height = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };
        _view.NavigationCompleted += (_, _) => _ready.TrySetResult();
        _view.WebMessageReceived += OnMessage;
        Grid.SetRow(_view, 2);
        host.Children.Add(_view);

        var page = Prepare();
        _view.Navigate(new Uri(page));
    }

    public bool TryGetCached(string code, bool dark, out MermaidImage? image) =>
        _cache.TryGetValue(Key(code, dark), out image);

    public async Task<(MermaidImage? Image, string? Error)> RenderAsync(string code, bool dark)
    {
        var key = Key(code, dark);
        if (_cache.TryGetValue(key, out var hit)) return (hit, null);

        await _gate.WaitAsync();
        try
        {
            if (_cache.TryGetValue(key, out hit)) return (hit, null);
            if (await Task.WhenAny(_ready.Task, Task.Delay(ReadyTimeout)) != _ready.Task)
                return (null, "El motor de diagramas (WebView) no está disponible en este equipo.");

            var id = ++_nextId;
            var tcs = new TaskCompletionSource<JsonElement>();
            _pending[id] = tcs;
            var theme = dark ? "dark" : "default";
            var js = $"renderMermaid({id},{JsonSerializer.Serialize(code)},{JsonSerializer.Serialize(theme)},{Scale})";
            await Dispatcher.UIThread.InvokeAsync(async () => await _view.InvokeScript(js));

            if (await Task.WhenAny(tcs.Task, Task.Delay(RenderTimeout)) != tcs.Task)
            {
                _pending.Remove(id);
                return (null, "Tiempo de espera agotado al renderizar el diagrama.");
            }

            var r = tcs.Task.Result;
            if (!r.GetProperty("ok").GetBoolean())
                return (null, r.GetProperty("error").GetString());

            var bytes = Convert.FromBase64String(r.GetProperty("png").GetString()!);
            var image = new MermaidImage(
                new Bitmap(new MemoryStream(bytes)),
                r.GetProperty("w").GetDouble(),
                r.GetProperty("h").GetDouble());
            if (_cache.Count > 200) _cache.Clear();
            _cache[key] = image;
            return (image, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    void OnMessage(object? sender, WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.Body ?? "{}");
            var root = doc.RootElement.Clone();
            if (root.TryGetProperty("id", out var idEl) && _pending.Remove(idEl.GetInt32(), out var tcs))
                tcs.TrySetResult(root);
        }
        catch (JsonException) { }
    }

    static string Key(string code, bool dark) => (dark ? "d:" : "l:") + code;

    // Extrae mermaid.js y la página a disco: NavigateToString tiene un límite de 2 MB y mermaid pesa ~3,5 MB.
    static string Prepare()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MdViewer", "mermaid");
        Directory.CreateDirectory(dir);

        var js = Path.Combine(dir, "mermaid.min.js");
        using (var src = AssetLoader.Open(new Uri("avares://MdViewer/Assets/mermaid.min.js")))
        {
            if (!File.Exists(js) || new FileInfo(js).Length != src.Length)
            {
                using var dst = File.Create(js);
                src.CopyTo(dst);
            }
        }

        var page = Path.Combine(dir, "render.html");
        File.WriteAllText(page, PageHtml);
        return page;
    }

    const string PageHtml = """
<!doctype html>
<html><head><meta charset="utf-8"></head><body>
<script src="mermaid.min.js"></script>
<script>
function send(o) { invokeCSharpAction(JSON.stringify(o)); }
async function renderMermaid(id, code, theme, scale) {
  try {
    mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: theme,
      htmlLabels: false, flowchart: { htmlLabels: false } });
    const r = await mermaid.render('m' + id, code);
    const svg = new DOMParser().parseFromString(r.svg, 'image/svg+xml').documentElement;
    const vb = (svg.getAttribute('viewBox') || '').split(/[\s,]+/).map(Number);
    const w = Math.ceil(vb.length === 4 ? vb[2] : parseFloat(svg.getAttribute('width')));
    const h = Math.ceil(vb.length === 4 ? vb[3] : parseFloat(svg.getAttribute('height')));
    svg.setAttribute('width', w);
    svg.setAttribute('height', h);
    svg.removeAttribute('style');
    const xml = new XMLSerializer().serializeToString(svg);
    const img = new Image();
    img.onload = () => {
      const k = Math.min(scale, 8000 / Math.max(w, h));
      const c = document.createElement('canvas');
      c.width = Math.ceil(w * k); c.height = Math.ceil(h * k);
      c.getContext('2d').drawImage(img, 0, 0, c.width, c.height);
      send({ id: id, ok: true, w: w, h: h, png: c.toDataURL('image/png').split(',')[1] });
    };
    img.onerror = () => send({ id: id, ok: false, error: 'No se pudo rasterizar el diagrama.' });
    img.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(xml);
  } catch (e) {
    send({ id: id, ok: false, error: String((e && e.message) || e) });
  }
}
</script></body></html>
""";
}
