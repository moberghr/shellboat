using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;

namespace Shellboat.App.Tests;

public class ConnectionEditorViewModelTests
{
    [Fact]
    public void Host_is_required_and_port_must_be_valid()
    {
        var vm = new ConnectionEditorViewModel(null, null, true, false);
        Assert.Null(vm.TryBuild());
        Assert.NotNull(vm.Error);

        vm.Host = "h";
        vm.Port = "70000";
        Assert.Null(vm.TryBuild());

        vm.Port = "";
        var entry = vm.TryBuild();
        Assert.NotNull(entry);
        Assert.Equal(("h", "h", 22), (entry!.Name, entry.Host, entry.EffectivePort));
    }

    [Fact]
    public void Key_auth_needs_a_key_path()
    {
        var vm = new ConnectionEditorViewModel(null, null, true, false) { Host = "h", AuthIndex = 1 };
        Assert.True(vm.ShowKey);
        Assert.Null(vm.TryBuild());
        vm.PrivateKeyPath = "~/.ssh/id_ed25519";
        Assert.Equal(SshAuthMethod.PrivateKey, vm.TryBuild()!.Ssh!.Auth);
    }

    [Fact]
    public void Editing_keeps_id_and_folder_and_rdp_settings_round_trip()
    {
        var folder = Guid.NewGuid();
        var original = new ConnectionEntry
        {
            Id = Guid.NewGuid(),
            Name = "dc",
            FolderId = folder,
            Kind = ConnectionKind.Rdp,
            Host = "dc",
            UserName = "a",
            Rdp = new RdpSettings("CORP", RdpResolutionMode.Fixed, 1600, 900, 24),
        };
        var vm = new ConnectionEditorViewModel(original, null, true, true);
        Assert.True(vm.IsRdp);
        var built = vm.TryBuild()!;
        Assert.Equal((original.Id, folder), (built.Id, built.FolderId));
        Assert.Equal(original.Rdp, built.Rdp);
        Assert.Null(built.Ssh);
    }
}
