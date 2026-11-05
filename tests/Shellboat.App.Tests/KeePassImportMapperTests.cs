using Shellboat.App.Connections;
using Shellboat.Core.Connections;
using Shellboat.Core.Import;

namespace Shellboat.App.Tests;

public class KeePassImportMapperTests
{
    private static ImportedEntry E(string title, string user, string url) => new(title, user, "pw", url, "", []);

    [Fact]
    public void Ssh_url_with_port_and_userinfo()
    {
        var c = KeePassImportMapper.ToConnection(E("web", "", "ssh://deploy@web.example:2222"), null);
        Assert.Equal((ConnectionKind.Ssh, "web.example", 2222, "deploy"), (c.Kind, c.Host, c.Port, c.UserName));
    }

    [Fact]
    public void Rdp_url_and_domain_user_split()
    {
        var c = KeePassImportMapper.ToConnection(E("dc", "CORP\\admin", "rdp://dc.corp.local"), null);
        Assert.Equal((ConnectionKind.Rdp, "dc.corp.local", "admin", "CORP"), (c.Kind, c.Host, c.UserName, c.Rdp?.Domain));
    }

    [Fact]
    public void Bare_host_port_3389_is_rdp()
    {
        var c = KeePassImportMapper.ToConnection(E("x", "u", "10.0.0.5:3389"), null);
        Assert.Equal((ConnectionKind.Rdp, "10.0.0.5", 3389), (c.Kind, c.Host, c.Port));
    }

    [Fact]
    public void No_url_falls_back_to_title_as_host()
    {
        var c = KeePassImportMapper.ToConnection(E("jump.example", "u", ""), null);
        Assert.Equal(("jump.example", ConnectionKind.Ssh, 22), (c.Host, c.Kind, c.EffectivePort));
    }
}
