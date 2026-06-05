# MarkWin2D

基于 **Win2D** 的 **GitHub Flavored Markdown** 渲染控件，用于 **WinUI 3** 应用。

## 功能

- 标题 (H1–H6)
- 粗体、斜体、删除线
- 行内代码 & 围栏代码块
- 有序 / 无序 / 任务列表
- 嵌套块引用
- 表格（自动列宽）
- 分隔线
- 超链接（可点击，支持开关）
- **图片**（通过 `IImageProvider` 接入）
- **图片对齐** — 左对齐 / 居中 / 右对齐

## 安装

```
dotnet add package MarkWin2D
```

## 快速开始

### XAML

```xml
<UserControl
    xmlns:md="using:MarkWin2D.Controls">
    <md:MarkWin2DControl x:Name="MarkdownView" />
</UserControl>
```

### 代码

```csharp
MarkdownView.Text = "# Hello\n**粗体** `代码`";
MarkdownView.AreLinksEnabled = true;   // 开启链接点击
```

## 图片

控件不自带下载。实现 `IImageProvider` 后挂载：

```csharp
public class MyImageProvider : IImageProvider
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
        var bytes = await _http.GetByteArrayAsync(url);
        using var ms = new MemoryStream(bytes);
        var bmp = await CanvasBitmap.LoadAsync(device, ms.AsRandomAccessStream());
        _cache[url] = bmp;
        ImagesInvalidated?.Invoke(this, EventArgs.Empty);
    }
}

MarkdownView.ImageProvider = new MyImageProvider();
```

## 图片对齐

```csharp
MarkdownView.DocumentStyle.ImageAlignment = ImageAlignment.Center;
// Left（默认）、Center、Right
```

## 样式

所有样式属性通过 `MarkdownStyle` 暴露：

```csharp
MarkdownView.DocumentStyle = new MarkdownStyle
{
    FontFamily = "Consolas",
    BodyFontSize = 14,
    TextColor = Colors.White,
    ImageAlignment = ImageAlignment.Center
};
```
