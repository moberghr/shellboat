using Shellboat.App.Services;

namespace Shellboat.App.Tests;

public class SftpPathTests
{
    [Theory]
    [InlineData("/home/u", "/home")]
    [InlineData("/home/u/", "/home")]
    [InlineData("/home", "/")]
    [InlineData("/", null)]
    [InlineData("relative", null)]
    public void Parent_walks_up_to_the_root(string path, string? parent)
        => Assert.Equal(parent, RemotePath.Parent(path));

    [Theory]
    [InlineData("/", "a", "/a")]
    [InlineData("/home", "a", "/home/a")]
    [InlineData("/home/", "a", "/home/a")]
    public void Combine_uses_forward_slashes(string dir, string name, string expected)
        => Assert.Equal(expected, RemotePath.Combine(dir, name));

    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData(".bashrc", ".bashrc")]
    [InlineData("../evil", ".._evil")]
    [InlineData("..", "_")]
    [InlineData(".", "_")]
    [InlineData("a\\b:c*?.txt", "a_b_c__.txt")]
    [InlineData("con.txt", "_con.txt")]
    [InlineData("trailing. ", "trailing")]
    [InlineData("tab\there", "tab_here")]
    public void Safe_name_is_one_harmless_path_component(string remote, string expected)
        => Assert.Equal(expected, LocalDownloads.SafeName(remote));

    [Fact]
    public void Unique_path_never_reuses_an_existing_file_or_folder()
    {
        var dir = Directory.CreateTempSubdirectory("shellboat-unique-").FullName;
        try
        {
            Assert.Equal(Path.Combine(dir, "a.tar.gz"), LocalDownloads.UniquePath(dir, "a.tar.gz"));
            File.WriteAllText(Path.Combine(dir, "a.tar.gz"), "");
            Assert.Equal(Path.Combine(dir, "a.tar (1).gz"), LocalDownloads.UniquePath(dir, "a.tar.gz"));
            Directory.CreateDirectory(Path.Combine(dir, ".config"));
            Assert.Equal(Path.Combine(dir, ".config (1)"), LocalDownloads.UniquePath(dir, ".config"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void Sizes_are_human_readable(long bytes, string text)
        => Assert.Equal(text, Shellboat.App.ViewModels.RemoteFileItemViewModel.FormatSize(bytes));
}
