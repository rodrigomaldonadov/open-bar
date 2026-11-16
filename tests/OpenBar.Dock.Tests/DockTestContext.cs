using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.Application.Dock;
using OpenBar.Dock;
using OpenBar.Dock.Tests.Fakes;

namespace OpenBar.Dock.Tests;

internal sealed class DockTestContext : IDisposable
{
    public DockTestContext()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "OpenBar.Dock.Tests",
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(DirectoryPath);

        Pinned = new FakePinnedApplicationService();
        Layouts = new FakeDockLayoutService();
        Catalog = new FakeApplicationCatalogService();
        Resolver = new FakeShortcutResolver();
        Launcher = new FakeApplicationLauncher();
        Windows = new FakeAppWindowService();
        Time = new ManualTimeProvider();

        Controller = new DockController(
            Pinned,
            Layouts,
            Catalog,
            Resolver,
            Launcher,
            Windows,
            Time,
            NullLogger<DockController>.Instance);
    }

    public string DirectoryPath { get; }

    public FakePinnedApplicationService Pinned { get; }

    public FakeDockLayoutService Layouts { get; }

    public FakeApplicationCatalogService Catalog { get; }

    public FakeShortcutResolver Resolver { get; }

    public FakeApplicationLauncher Launcher { get; }

    public FakeAppWindowService Windows { get; }

    public ManualTimeProvider Time { get; }

    public DockController Controller { get; }

    public string CreateExecutable(string name = "app.exe")
    {
        var path = Path.Combine(DirectoryPath, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    public string CreateFile(string name)
    {
        var path = Path.Combine(DirectoryPath, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    public static DiscoveredApplication Discovered(string path, string name = "App")
        => new(name, path, OpenBar.Domain.Dock.PinnedTargetKind.Executable, IconPath: path);

    public void Dispose()
    {
        Controller.Dispose();

        try
        {
            System.IO.Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
