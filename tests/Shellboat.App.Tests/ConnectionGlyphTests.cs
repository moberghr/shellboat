using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;

namespace Shellboat.App.Tests;

public class ConnectionGlyphTests
{
    [Fact]
    public void Ssh_and_rdp_rows_get_distinct_glyphs_and_tints()
    {
        var ssh = new ConnectionNodeViewModel(new ConnectionEntry { Id = Guid.NewGuid(), Name = "web", Kind = ConnectionKind.Ssh, Host = "web" });
        var rdp = new ConnectionNodeViewModel(new ConnectionEntry { Id = Guid.NewGuid(), Name = "dc", Kind = ConnectionKind.Rdp, Host = "dc" });
        var folder = new FolderNodeViewModel(new Folder(Guid.NewGuid(), "prod", null, 0));

        Assert.Equal(("Icon.Ssh", "Kind.Ssh"), (ssh.IconKey, ssh.IconBrushKey));
        Assert.Equal(("Icon.Rdp", "Kind.Rdp"), (rdp.IconKey, rdp.IconBrushKey));
        Assert.Equal(("Icon.Folder", "Text.Muted"), (folder.IconKey, folder.IconBrushKey));
    }
}
