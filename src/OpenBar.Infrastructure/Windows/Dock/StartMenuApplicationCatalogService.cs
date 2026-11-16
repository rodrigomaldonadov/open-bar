using OpenBar.Application.Dock;
using OpenBar.Domain.Dock;

namespace OpenBar.Infrastructure.Windows.Dock;

/// <summary>
/// Discovers real applications from the per-user and all-users Start Menu.
/// Only .lnk and .exe entries are returned; no application list is hardcoded.
/// </summary>
public sealed class StartMenuApplicationCatalogService : IApplicationCatalogService
{
    private const int MaxEntries = 400;

    public Task<IReadOnlyList<DiscoveredApplication>> DiscoverAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<DiscoveredApplication>>(() => Discover(cancellationToken), cancellationToken);

    private static DiscoveredApplication[] Discover(CancellationToken cancellationToken)
    {
        var roots = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                "Programs"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs")
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<DiscoveredApplication>();

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (var path in Directory.EnumerateFiles(
                             root,
                             "*",
                             new EnumerationOptions
                             {
                                 RecurseSubdirectories = true,
                                 IgnoreInaccessible = true,
                                 MatchCasing = MatchCasing.CaseInsensitive
                             }))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var extension = Path.GetExtension(path);
                    var kind = extension.ToLowerInvariant() switch
                    {
                        ".lnk" => PinnedTargetKind.Shortcut,
                        ".exe" => PinnedTargetKind.Executable,
                        _ => (PinnedTargetKind?)null
                    };

                    if (kind is null || !seen.Add(path))
                    {
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(path).Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    results.Add(new DiscoveredApplication(name, path, kind.Value, IconPath: path));

                    if (results.Count >= MaxEntries)
                    {
                        break;
                    }
                }
            }
            catch (IOException)
            {
                // A partially inaccessible Start Menu folder must not hide the
                // rest of the catalog.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return results
            .OrderBy(application => application.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
