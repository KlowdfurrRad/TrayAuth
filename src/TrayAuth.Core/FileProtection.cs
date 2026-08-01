using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TrayAuth.Core;

/// <summary>
/// Restricts vault and export files to the current user, using whichever mechanism the OS
/// actually has: NTFS ACLs on Windows, POSIX modes elsewhere. Best effort by design - the
/// real at-rest protection is the vault encryption, and a permissions failure should never
/// stop the app from working.
/// </summary>
public static class FileProtection
{
    public static void HardenDirectory(string directory)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                HardenDirectoryWindows(directory);
            }
            else
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        catch
        {
            // Non-fatal everywhere.
        }
    }

    public static void HardenFile(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                HardenFileWindows(path);
            }
            else
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Non-fatal everywhere.
        }
    }

    /// <summary>Breaks inheritance and grants only the current user, recursively for children.</summary>
    [SupportedOSPlatform("windows")]
    private static void HardenDirectoryWindows(string directory)
    {
        var info = new DirectoryInfo(directory);
        DirectorySecurity security = info.GetAccessControl();

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (FileSystemAccessRule rule in security
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>())
        {
            security.RemoveAccessRule(rule);
        }

        SecurityIdentifier? user = WindowsIdentity.GetCurrent().User;
        if (user is null)
        {
            return;
        }

        security.AddAccessRule(new FileSystemAccessRule(
            user,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        info.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void HardenFileWindows(string path)
    {
        var info = new FileInfo(path);
        FileSecurity security = info.GetAccessControl();

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (FileSystemAccessRule rule in security
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>())
        {
            security.RemoveAccessRule(rule);
        }

        SecurityIdentifier? user = WindowsIdentity.GetCurrent().User;
        if (user is null)
        {
            return;
        }

        security.AddAccessRule(new FileSystemAccessRule(
            user,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        info.SetAccessControl(security);
    }
}
