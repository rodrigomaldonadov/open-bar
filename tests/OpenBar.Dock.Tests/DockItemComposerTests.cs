using OpenBar.Application.Dock;
using OpenBar.Dock;
using OpenBar.Domain.Dock;
using Xunit;

namespace OpenBar.Dock.Tests;

public sealed class DockItemComposerTests
{
    [Fact]
    public void PinnedExecutableMatchesItsRunningWindow()
    {
        var pinned = Pinned("Clock", @"C:\Apps\clock.exe", PinnedTargetKind.Executable, sortOrder: 0);
        var window = Window(@"C:\Apps\clock.exe", "clock-aumid");

        var items = DockItemComposer.Compose([pinned], [window]);

        var item = Assert.Single(items);
        Assert.True(item.IsPinned);
        Assert.True(item.IsRunning);
        Assert.Equal(window.Handle, item.WindowHandle);
    }

    [Fact]
    public void AppUserModelIdMatchWinsForPackagedApplications()
    {
        var pinned = Pinned("Terminal", "shortcut.lnk", PinnedTargetKind.Shortcut, sortOrder: 0);
        pinned.AppUserModelId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";

        var matching = Window(@"C:\Windows\System32\ApplicationFrameHost.exe", pinned.AppUserModelId);
        var unrelated = Window(@"C:\Windows\System32\notepad.exe", "notepad-aumid", "Notepad", "Notepad");

        var items = DockItemComposer.Compose([pinned], [matching, unrelated]);

        Assert.Equal(2, items.Count);
        Assert.True(items[0].IsRunning);
        Assert.Equal(matching.Handle, items[0].WindowHandle);
        Assert.False(items[1].IsPinned);
        Assert.Equal("Notepad", items[1].DisplayName);
    }

    [Fact]
    public void ShortcutPointingAtExplorerDoesNotMatchEveryExplorerWindow()
    {
        var pinned = Pinned("Folder", @"C:\Shortcuts\folder.lnk", PinnedTargetKind.Shortcut, sortOrder: 0);
        pinned.ResolvedTarget = @"C:\Windows\explorer.exe";

        var explorerWindow = Window(@"C:\Windows\explorer.exe", "explorer-aumid");

        var items = DockItemComposer.Compose([pinned], [explorerWindow]);

        Assert.Equal(2, items.Count);
        Assert.False(items[0].IsRunning);
        Assert.False(items[1].IsPinned);
    }

    [Fact]
    public void MultipleWindowsOfOneProcessProduceOneRunningTile()
    {
        var first = Window(@"C:\Apps\editor.exe", "editor-aumid", "Document A");
        var second = Window(@"C:\Apps\editor.exe", "editor-aumid", "Document B");

        var items = DockItemComposer.Compose([], [first, second]);

        var item = Assert.Single(items);
        Assert.False(item.IsPinned);
        Assert.True(item.IsRunning);
        Assert.Equal(first.Handle, item.WindowHandle);
    }

    [Fact]
    public void PinnedItemsKeepTheirSortOrderBeforeRunningItems()
    {
        var second = Pinned("Second", @"C:\Apps\second.exe", PinnedTargetKind.Executable, sortOrder: 1);
        var first = Pinned("First", @"C:\Apps\first.exe", PinnedTargetKind.Executable, sortOrder: 0);
        var running = Window(@"C:\Apps\running.exe", "running-aumid");

        var items = DockItemComposer.Compose([second, first], [running]);

        Assert.Equal(["First", "Second", "Running app"], items.Select(item => item.DisplayName));
        Assert.True(items[0].IsPinned);
        Assert.True(items[1].IsPinned);
        Assert.False(items[2].IsPinned);
    }

    [Fact]
    public void ExecutableAliasMatchesByFileName()
    {
        var pinned = Pinned("Notepad", @"C:\Windows\System32\notepad.exe", PinnedTargetKind.Executable, 0);
        var window = Window(
            @"C:\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.0.0.0_x64__8wekyb3d8bbwe\Notepad.exe",
            "notepad-aumid");

        var items = DockItemComposer.Compose([pinned], [window]);

        var item = Assert.Single(items);
        Assert.True(item.IsRunning);
    }

    private static PinnedApplication Pinned(
        string name,
        string target,
        PinnedTargetKind kind,
        int sortOrder)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Target = target,
            TargetKind = kind,
            SortOrder = sortOrder
        };

    private static AppWindowSnapshot Window(
        string path,
        string? aumid,
        string title = "Running app",
        string displayName = "Running app")
        => new(Handle: (IntPtr)(path.GetHashCode() & 0x7FFFFFF), ProcessId: 42, path, aumid, title, displayName);
}
