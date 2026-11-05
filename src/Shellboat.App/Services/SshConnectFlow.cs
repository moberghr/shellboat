using Shellboat.Core.Connections;
using Shellboat.Core.Secrets;
using Shellboat.Core.Sessions;

namespace Shellboat.App.Services;

/// <summary>How one SSH connect attempt ended: a connected channel, or the message to show.</summary>
public sealed record SshConnectResult<T>(T? Channel, string? Error, bool HostKeyRejected)
    where T : class;

/// <summary>
/// The credential loop every SSH-based tab shares: saved secret → prompt when none, build a channel, connect,
/// re-prompt on <see cref="AuthenticationFailedException"/> (up to <see cref="MaxAuthAttempts"/>), never retry a
/// rejected host key. Failed channels are disposed here; the caller owns a returned one.
/// </summary>
public static class SshConnectFlow
{
    public const int MaxAuthAttempts = 3;

    public static async Task<SshConnectResult<T>> ConnectAsync<T>(
        ConnectionEntry connection,
        CredentialResolver credentials,
        Func<SshCredentials, T> create,
        Func<T, CancellationToken, Task> connect,
        Func<T, ValueTask> dispose,
        CancellationToken ct)
        where T : class
    {
        var isKey = connection.Ssh?.Auth == SshAuthMethod.PrivateKey;
        var purpose = isKey ? SecretPurpose.KeyPassphrase : SecretPurpose.Password;
        var secret = await credentials.GetSavedAsync(connection, purpose, ct);
        if (secret is null && !isKey)
        {
            secret = await credentials.PromptAsync(connection, purpose, null, ct);
            if (secret is null)
            {
                return Failed<T>("Cancelled.");
            }
        }

        for (var attempt = 1; ; attempt++)
        {
            var creds = isKey ? new SshCredentials(null, secret) : new SshCredentials(secret, null);
            var channel = create(creds);
            try
            {
                await connect(channel, ct);
                return new SshConnectResult<T>(channel, null, false);
            }
            catch (AuthenticationFailedException ex) when (attempt < MaxAuthAttempts)
            {
                await dispose(channel);
                secret = await credentials.PromptAsync(connection, purpose, ex.Message, ct);
                if (secret is null)
                {
                    return Failed<T>("Cancelled.");
                }
            }
            catch (OperationCanceledException)
            {
                await dispose(channel);
                return Failed<T>("Cancelled.");
            }
            catch (Exception ex)
            {
                await dispose(channel);
                return new SshConnectResult<T>(null, ex.Message, ex is HostKeyRejectedException);
            }
        }
    }

    private static SshConnectResult<T> Failed<T>(string message)
        where T : class => new(null, message, false);
}
