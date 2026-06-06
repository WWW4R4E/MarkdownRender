using MarkWin2D;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DemoPage
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
            MarkdownView.ImageProvider = new HttpImageProvider();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var readmePath = FindProjectFile("README.md");
                if (readmePath != null)
                {
                    MarkdownView.Text = await System.IO.File.ReadAllTextAsync(readmePath);
                }
                else
                {
                    MarkdownView.Text = "# README 文件未找到\n\n请确保 `README.md` 存在于项目根目录。";
                }
            }
            catch (Exception ex)
            {
                MarkdownView.Text = $"# 加载失败\n\n{ex.Message}";
            }
        }

        /// <summary>
        /// 从可执行文件目录逐级向上查找项目根目录中的指定文件。
        /// </summary>
        static string? FindProjectFile(string fileName)
        {
            var dir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            while (dir != null)
            {
                var candidate = System.IO.Path.Combine(dir, fileName);
                if (System.IO.File.Exists(candidate))
                    return candidate;
                var parent = System.IO.Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent;
            }
            return null;
        }
    }

    sealed class HttpImageProvider : IImageProvider
    {
        private readonly HttpClient _http = new();
        private readonly Dictionary<string, CanvasBitmap> _cache = new();

        public event EventHandler? ImagesInvalidated;

        public CanvasBitmap? GetImage(string url, CanvasDevice device)
        {
            if (_cache.TryGetValue(url, out var bmp)) return bmp;
            _ = LoadAsync(url, device);
            return null;
        }

        private async Task LoadAsync(string url, CanvasDevice device)
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(url);
                using var ms = new MemoryStream(bytes);
                var bmp = await CanvasBitmap.LoadAsync(device, ms.AsRandomAccessStream());
                _cache[url] = bmp;
                ImagesInvalidated?.Invoke(this, EventArgs.Empty);
            }
            catch { }
        }
    }
}