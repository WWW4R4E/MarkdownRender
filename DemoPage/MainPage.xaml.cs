using MarkWin2D;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml.Controls;
namespace DemoPage
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
            MarkdownView.ImageProvider = new HttpImageProvider();
            MarkdownView.Text = SampleMarkdown;
        }

        static string SampleMarkdown => """
# WinUIMd — Markdown 渲染引擎

基于 **Win2D** 实现的 GFM 渲染器。

## 文本样式

这是 **粗体**、*斜体*、***粗斜体*** 和 ~~删除线~~ 效果。  
行内代码：`Console.WriteLine("Hello");`

## 图片示例

![示例图片](https://picsum.photos/800/400)

## 代码块

```csharp
public class Hello
{
    public static void Main()
    {
        Console.WriteLine("Hello, Win2D!");
    }
}
```

## 列表

### 无序列表
- 苹果
- 香蕉
- 橘子

### 有序列表
1. 第一步
2. 第二步
3. 第三步

### 任务列表
- [x] 已完成任务
- [ ] 未完成任务
- [ ] 待办事项

## 块引用

> 这是一段引用文字。
> 
> 引用中可以包含 **格式化** 内容。
>
> > 嵌套引用也支持！

## 表格

| 姓名 | 年龄 | 城市 |
|------|------|------|
| 张三 | 28 | 北京 |
| 李四 | 32 | 上海 |
| 王五 | 25 | 广州 |

## 分隔线

---

## 链接

这是一个 [链接到 GitHub](https://github.com)。

---

*由 Win2D + Markdig 强力驱动*
""";
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
