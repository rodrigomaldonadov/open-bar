using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using OpenBar.Application.Dock;
using OpenBar.Domain.Dock;

namespace OpenBar.Infrastructure.Windows.Dock;

/// <summary>
/// Resolves Windows shortcuts with the shell COM interfaces. The launch target
/// stays the .lnk path (so shell behavior and arguments are preserved); the
/// resolved executable and AUMID are cached for running-window matching.
/// </summary>
public sealed partial class ShortcutResolver : IShortcutResolver
{
    private const uint SlgpRawPath = 0x00000004;
    private const uint StgmRead = 0x00000000;

    public Task<ResolvedApplicationTarget> ResolveAsync(
        string target,
        PinnedTargetKind targetKind,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Resolve(target, targetKind), cancellationToken);

    private static ResolvedApplicationTarget Resolve(string target, PinnedTargetKind targetKind)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return new ResolvedApplicationTarget(null, null, null);
        }

        if (targetKind == PinnedTargetKind.Executable)
        {
            return new ResolvedApplicationTarget(target, null, ExtractAppUserModelId(target));
        }

        if (targetKind != PinnedTargetKind.Shortcut
            || !File.Exists(target)
            || !string.Equals(Path.GetExtension(target), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedApplicationTarget(null, null, ExtractAppUserModelId(target));
        }

        object? shellLinkObject = null;

        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
            if (type is null)
            {
                return new ResolvedApplicationTarget(null, null, null);
            }

            shellLinkObject = Activator.CreateInstance(type);
            if (shellLinkObject is not IShellLinkW shellLink || shellLinkObject is not IPersistFile persistFile)
            {
                return new ResolvedApplicationTarget(null, null, null);
            }

            persistFile.Load(target, StgmRead);

            var pathBuilder = new StringBuilder(1_024);
            shellLink.GetPath(pathBuilder, pathBuilder.Capacity, IntPtr.Zero, SlgpRawPath);

            var argumentsBuilder = new StringBuilder(1_024);
            shellLink.GetArguments(argumentsBuilder, argumentsBuilder.Capacity);

            var executable = pathBuilder.ToString();
            var arguments = argumentsBuilder.ToString();

            return new ResolvedApplicationTarget(
                string.IsNullOrWhiteSpace(executable) ? null : executable,
                string.IsNullOrWhiteSpace(arguments) ? null : arguments,
                ExtractAppUserModelId(arguments) ?? ExtractAppUserModelId(executable));
        }
        catch (COMException)
        {
            return new ResolvedApplicationTarget(null, null, null);
        }
        catch (FileNotFoundException)
        {
            return new ResolvedApplicationTarget(null, null, null);
        }
        finally
        {
            if (shellLinkObject is not null && Marshal.IsComObject(shellLinkObject))
            {
                Marshal.FinalReleaseComObject(shellLinkObject);
            }
        }
    }

    private static string? ExtractAppUserModelId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = AppUserModelIdPattern().Match(value);
        return match.Success ? match.Groups["aumid"].Value.Trim() : null;
    }

    [GeneratedRegex(@"shell:AppsFolder\\(?<aumid>[^""\s]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AppUserModelIdPattern();

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cch,
            IntPtr pfd,
            uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    }

    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, bool fRemember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
