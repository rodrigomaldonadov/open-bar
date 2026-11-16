using OpenBar.SystemBridge;

namespace OpenBar.Infrastructure.Windows.SystemBridge;

/// <summary>Ready volumes from DriveInfo. No directory traversal is performed.</summary>
public sealed class WindowsStorageMetricsSource : IStorageMetricsSource
{
    public IReadOnlyList<StorageVolumeInfo> GetVolumes()
    {
        var volumes = new List<StorageVolumeInfo>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady
                    || drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network))
                {
                    continue;
                }

                var total = (ulong)Math.Max(0, drive.TotalSize);
                var free = (ulong)Math.Max(0, drive.AvailableFreeSpace);
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name.TrimEnd('\\')
                    : drive.VolumeLabel;

                volumes.Add(new StorageVolumeInfo(
                    drive.Name,
                    label,
                    drive.RootDirectory.FullName,
                    total,
                    free));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A volume can disappear or become inaccessible between enumeration and read.
            }
        }

        return volumes
            .OrderBy(volume => volume.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
