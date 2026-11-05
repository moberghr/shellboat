using System.Globalization;
using Shellboat.Core.Sessions;

namespace Shellboat.App.ViewModels;

/// <summary>One row of the SFTP browser.</summary>
public sealed class RemoteFileItemViewModel(RemoteFileEntry entry)
{
    public RemoteFileEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    public bool IsDirectory => Entry.IsDirectory;

    public string IconKey => Entry.IsDirectory ? "Icon.Folder" : "Icon.File";

    public string IconBrushKey => Entry.IsDirectory ? "Kind.Ssh" : "Text.Muted";

    public string SizeText => Entry.IsDirectory ? "" : FormatSize(Entry.Size);

    public string ModifiedText => Entry.Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"drwxr-xr-x (0755)" — ls-style type + permissions, with setuid/setgid/sticky folded in as s/S, t/T.</summary>
    public static string FormatMode(int mode, bool isDirectory, bool isSymbolicLink)
    {
        var c = new char[10];
        c[0] = isSymbolicLink ? 'l' : isDirectory ? 'd' : '-';
        for (var i = 0; i < 9; i++)
        {
            c[i + 1] = (mode & (0x100 >> i)) != 0 ? "rwx"[i % 3] : '-';
        }

        c[3] = Special(c[3], (mode & 0x800) != 0, 's');
        c[6] = Special(c[6], (mode & 0x400) != 0, 's');
        c[9] = Special(c[9], (mode & 0x200) != 0, 't');
        return $"{new string(c)} ({Convert.ToString(mode & 0xFFF, 8).PadLeft(4, '0')})";

        static char Special(char exec, bool set, char letter) => !set ? exec : exec == 'x' ? letter : char.ToUpperInvariant(letter);
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }
}
