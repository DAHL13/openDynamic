using OpenDynamic.Core.Clipboard;
using OpenDynamic.Tests.Timer;
using Xunit;

namespace OpenDynamic.Tests.Clipboard;

public sealed class ClipboardHistoryTests
{
    [Fact]
    public void TryAddText_ValidPlainText_SanitizesPreviewAndAddsItem()
    {
        var manager = new ClipboardHistoryManager();
        string input = "  Hello \r\n  World \t from openDynamic!   ";

        bool added = manager.TryAddText(input, out var item);

        Assert.True(added);
        Assert.NotNull(item);
        Assert.Equal(ClipboardItemKind.Text, item.Kind);
        Assert.Equal("Hello World from openDynamic!", item.DisplayPreview);
        Assert.Equal(input, item.RawContent);
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void TryAddText_HttpUrl_ClassifiesAsUrl()
    {
        var manager = new ClipboardHistoryManager();
        string url = "https://github.com/DAHL13/openDynamic";

        bool added = manager.TryAddText(url, out var item);

        Assert.True(added);
        Assert.NotNull(item);
        Assert.Equal(ClipboardItemKind.Url, item.Kind);
        Assert.Equal(url, item.DisplayPreview);
    }

    [Fact]
    public void TryAddText_EmptyOrWhitespace_ReturnsFalse()
    {
        var manager = new ClipboardHistoryManager();

        Assert.False(manager.TryAddText("", out var item1));
        Assert.Null(item1);
        Assert.False(manager.TryAddText("   \t\r\n  ", out var item2));
        Assert.Null(item2);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void TryAddText_ConsecutiveDuplicate_ReturnsFalseAndRefreshesTimestamp()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var manager = new ClipboardHistoryManager(clock);

        bool added1 = manager.TryAddText("Important note", out var item1);
        Assert.True(added1);
        Assert.NotNull(item1);
        var initialTimestamp = item1.TimestampUtc;

        clock.Advance(TimeSpan.FromMinutes(2));

        bool added2 = manager.TryAddText("Important note", out var item2);
        Assert.False(added2);
        Assert.NotNull(item2);
        Assert.Same(item1, item2);
        Assert.Equal(clock.GetUtcNow(), item2.TimestampUtc);
        Assert.True(item2.TimestampUtc > initialTimestamp);
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void TryAddText_NonConsecutiveDuplicate_AddsAsNewItem()
    {
        var manager = new ClipboardHistoryManager();

        Assert.True(manager.TryAddText("Alpha", out _));
        Assert.True(manager.TryAddText("Beta", out _));
        Assert.True(manager.TryAddText("Alpha", out _));

        Assert.Equal(3, manager.Count);
        var recent = manager.GetRecentItems();
        Assert.Equal("Alpha", recent[0].RawContent);
        Assert.Equal("Beta", recent[1].RawContent);
        Assert.Equal("Alpha", recent[2].RawContent);
    }

    [Fact]
    public void Capacity_LimitsMaxItems_TrimsOldest()
    {
        var manager = new ClipboardHistoryManager(capacity: 3);

        for (int i = 1; i <= 5; i++)
        {
            manager.TryAddText($"Item {i}", out _);
        }

        Assert.Equal(3, manager.Count);
        var items = manager.GetRecentItems();
        Assert.Equal("Item 5", items[0].RawContent);
        Assert.Equal("Item 4", items[1].RawContent);
        Assert.Equal("Item 3", items[2].RawContent);
    }

    [Fact]
    public void Expiration_WithFakeTimeProvider_PurgesExpiredItems()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var manager = new ClipboardHistoryManager(clock, expiration: TimeSpan.FromMinutes(10));

        manager.TryAddText("Item 1", out _);
        clock.Advance(TimeSpan.FromMinutes(5));
        manager.TryAddText("Item 2", out _);

        Assert.Equal(2, manager.Count);

        // Advance 6 more minutes (Total: Item 1 is 11 min old -> expired; Item 2 is 6 min old -> valid)
        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(1, manager.Count);
        var recent = manager.GetRecentItems();
        Assert.Single(recent);
        Assert.Equal("Item 2", recent[0].RawContent);

        // Advance 5 more minutes -> both expired
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, manager.Count);
        Assert.Empty(manager.GetRecentItems());
    }

    [Fact]
    public void TryAddFiles_ValidCount_AddsFilesItemWithSafePreview()
    {
        var manager = new ClipboardHistoryManager();

        bool addedSingle = manager.TryAddFiles(1, out var item1);
        Assert.True(addedSingle);
        Assert.NotNull(item1);
        Assert.Equal(ClipboardItemKind.Files, item1.Kind);
        Assert.Equal("1 archivo", item1.DisplayPreview);
        Assert.Null(item1.RawContent);
        Assert.Equal(1, item1.Length);

        bool addedMulti = manager.TryAddFiles(4, out var item2);
        Assert.True(addedMulti);
        Assert.NotNull(item2);
        Assert.Equal("4 archivos", item2.DisplayPreview);
        Assert.Null(item2.RawContent);
        Assert.Equal(4, item2.Length);
    }

    [Fact]
    public void TryAddFiles_ConsecutiveDuplicate_Suppressed()
    {
        var clock = new FakeTimeProvider();
        var manager = new ClipboardHistoryManager(clock);

        Assert.True(manager.TryAddFiles(3, out var item1));
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(manager.TryAddFiles(3, out var item2));

        Assert.Same(item1, item2);
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void TryAddImage_AddsImageItemWithSafeLabel()
    {
        var manager = new ClipboardHistoryManager();

        bool added = manager.TryAddImage(out var item);

        Assert.True(added);
        Assert.NotNull(item);
        Assert.Equal(ClipboardItemKind.Image, item.Kind);
        Assert.Equal("Imagen copiada", item.DisplayPreview);
        Assert.Null(item.RawContent);
    }

    [Fact]
    public void TryAddImage_ConsecutiveDuplicate_Suppressed()
    {
        var manager = new ClipboardHistoryManager();

        Assert.True(manager.TryAddImage(out _));
        Assert.False(manager.TryAddImage(out _));
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void Clear_RemovesAllItemsImmediately()
    {
        var manager = new ClipboardHistoryManager();
        manager.TryAddText("Text 1", out _);
        manager.TryAddImage(out _);
        manager.TryAddFiles(2, out _);

        Assert.Equal(3, manager.Count);

        manager.Clear();

        Assert.Equal(0, manager.Count);
        Assert.Empty(manager.GetRecentItems());
    }

    [Fact]
    public void SanitizeTextPreview_TruncatesAtCustomLimit()
    {
        string longText = new string('a', 100);
        string preview = ClipboardFormatter.SanitizeTextPreview(longText, maxLength: 20);

        Assert.Equal(20, preview.Length);
        Assert.EndsWith("…", preview);
    }
}
