using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Shellboat.Persistence;

/// <summary>
/// Runs a helper CLI (security, secret-tool, keepassxc-cli) with an argument list — no shell, no quoting —
/// optional stdin, and both output pipes drained concurrently. Secrets go via stdin wherever the tool allows.
/// </summary>
public interface ICliRunner
{
    /// <exception cref="FileNotFoundException">The executable is not installed / not on PATH.</exception>
    Task<CliResult> RunAsync(string file, IReadOnlyList<string> args, string? stdin, CancellationToken ct);
}

public sealed class CliRunner : ICliRunner
{
    public static CliRunner Instance { get; } = new();

    public async Task<CliResult> RunAsync(string file, IReadOnlyList<string> args, string? stdin, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new FileNotFoundException($"Could not start {file}.", file);
        }
        catch (Win32Exception ex)
        {
            throw new FileNotFoundException($"{file} could not be started: {ex.Message}", file, ex);
        }

        using (proc)
        {
            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct);
            if (stdin is not null)
            {
                var bytes = Encoding.UTF8.GetBytes(stdin);
                try
                {
                    await proc.StandardInput.BaseStream.WriteAsync(bytes, ct).ConfigureAwait(false);
                    proc.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The tool exited before reading stdin; its exit code and stderr say why.
                }
                finally
                {
                    Array.Clear(bytes);
                }
            }

            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return new CliResult(proc.ExitCode, stdout.Result, stderr.Result);
        }
    }
}

public readonly record struct CliResult(int Exit, string Stdout, string Stderr)
{
    /// <summary>The helper's stderr when it said something, otherwise the exit code. Never contains stdout,
    /// which for some helpers is the secret itself.</summary>
    public string Detail => Stderr.Trim() is { Length: > 0 } err ? err : $"exit code {Exit}";

    /// <summary>Never print stdout (it can hold a password).</summary>
    public override string ToString() => $"CliResult {{ Exit = {Exit}, Stderr = {Stderr.Trim()} }}";
}
