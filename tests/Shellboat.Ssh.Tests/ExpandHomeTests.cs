namespace Shellboat.Ssh.Tests;

public class ExpandHomeTests
{
    [Fact]
    public void Tilde_expands_to_the_home_directory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".ssh/id_rsa"), SshShellChannel.ExpandHome("~/.ssh/id_rsa"));
        Assert.Equal("/abs/key", SshShellChannel.ExpandHome("/abs/key"));
    }
}
