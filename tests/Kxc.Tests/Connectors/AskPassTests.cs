using System.Diagnostics;
using Kxc.Connectors.Ssh;
using Kxc.Core.Connections;
using Kxc.Core.Security;

namespace Kxc.Tests.Connectors;

/// <summary>
/// Helper kxc-askpass vero e canale monouso (esito della fase 0: la domanda sulla host key passa
/// dallo stesso helper e una risposta sbagliata manda ssh in ciclo).
/// </summary>
public class AskPassTests
{
    private const string HostKeyPrompt =
        "The authenticity of host '192.168.178.210 (192.168.178.210)' can't be established.\n" +
        "ED25519 key fingerprint is SHA256:qskTGEszv5UB03SvrnElY66sYIDvN7Uuu7jU1GcwZ4s.\n" +
        "This key is not known by any other names.\n" +
        "Are you sure you want to continue connecting (yes/no/[fingerprint])? ";

    private sealed class RecordingConfirmation(bool answer) : IHostKeyConfirmation
    {
        public List<HostKeyQuestion> Questions { get; } = [];

        public Task<bool> ConfirmAsync(HostKeyQuestion question, CancellationToken cancellationToken)
        {
            Questions.Add(question);
            return Task.FromResult(answer);
        }
    }

    [Theory]
    [InlineData("pi@192.168.178.210's password: ", "Password")]
    [InlineData("Password: ", "Password")]
    [InlineData("(pi@host) Password: ", "Password")]
    [InlineData("Enter passphrase for key 'C:\\Users\\x\\.ssh\\id_ed25519': ", "Other")]
    [InlineData("Please type 'yes', 'no' or the fingerprint: ", "Other")]
    [InlineData("Verification code: ", "Other")]
    [InlineData(HostKeyPrompt, "HostKey")]
    public void Classifies_ssh_prompts(string prompt, string expected)
    {
        Assert.Equal(expected, AskPassPrompt.Classify(prompt).ToString());
    }

    [Fact]
    public void Parses_host_and_fingerprint_from_the_host_key_question()
    {
        var question = AskPassPrompt.ParseHostKey(HostKeyPrompt, "fallback");

        Assert.Equal("192.168.178.210 (192.168.178.210)", question.Host);
        Assert.Equal("ED25519", question.KeyType);
        Assert.Equal("SHA256:qskTGEszv5UB03SvrnElY66sYIDvN7Uuu7jU1GcwZ4s", question.Fingerprint);
    }

    [Fact]
    public async Task Password_is_delivered_once_then_refused()
    {
        await using var server = new AskPassServer(SecretBuffer.FromChars("gw-segreto è"), "pi", "host", new RecordingConfirmation(true), TimeSpan.FromSeconds(30));

        var first = await RunHelperAsync(server.ChannelName, "pi@host's password: ");
        var second = await RunHelperAsync(server.ChannelName, "pi@host's password: ");

        Assert.Equal((0, "gw-segreto è\n"), first);
        Assert.Equal(1, second.ExitCode);
        Assert.Empty(second.Output);
        Assert.True(server.PasswordDelivered);
    }

    [Theory]
    [InlineData("pi@host's password: ", true)]
    [InlineData("(pi@host) Password: ", true)]
    [InlineData("PI@HOST's password: ", true)]
    [InlineData("pi@jump.example's password: ", false)]
    [InlineData("(pi@jump.example) Password: ", false)]
    [InlineData("root@host's password: ", false)]
    [InlineData("Password: ", false)]
    public async Task S3_password_goes_only_to_the_user_and_host_of_the_entry(string prompt, bool delivered)
    {
        await using var server = new AskPassServer(SecretBuffer.FromChars("segreto"), "pi", "host", new RecordingConfirmation(true), TimeSpan.FromSeconds(30));

        var result = await RunHelperAsync(server.ChannelName, prompt);

        Assert.Equal(delivered ? (0, "segreto\n") : (1, string.Empty), result);
        Assert.Equal(delivered, server.PasswordDelivered);
    }

    [Fact]
    public async Task Passphrases_and_unknown_questions_never_get_the_password()
    {
        await using var server = new AskPassServer(SecretBuffer.FromChars("segreto"), "pi", "host", new RecordingConfirmation(true), TimeSpan.FromSeconds(30));

        var passphrase = await RunHelperAsync(server.ChannelName, "Enter passphrase for key 'id_ed25519': ");
        var retry = await RunHelperAsync(server.ChannelName, "Please type 'yes', 'no' or the fingerprint: ");

        Assert.Equal((1, string.Empty), passphrase);
        Assert.Equal((1, string.Empty), retry);
        Assert.False(server.PasswordDelivered);
    }

    [Theory]
    [InlineData(true, "yes\n")]
    [InlineData(false, "no\n")]
    public async Task Host_key_question_goes_to_the_user(bool trusted, string expected)
    {
        var confirmation = new RecordingConfirmation(trusted);
        await using var server = new AskPassServer(SecretBuffer.FromChars("segreto"), "pi", "host", confirmation, TimeSpan.FromSeconds(30));

        var result = await RunHelperAsync(server.ChannelName, HostKeyPrompt);

        Assert.Equal((0, expected), result);
        Assert.Equal("SHA256:qskTGEszv5UB03SvrnElY66sYIDvN7Uuu7jU1GcwZ4s", Assert.Single(confirmation.Questions).Fingerprint);
        Assert.False(server.PasswordDelivered);
    }

    [Fact]
    public async Task Helper_fails_without_channel_or_after_the_channel_is_closed()
    {
        var server = new AskPassServer(SecretBuffer.FromChars("segreto"), "pi", "host", new RecordingConfirmation(true), TimeSpan.FromSeconds(30));
        var channel = server.ChannelName;
        await server.DisposeAsync();

        Assert.Equal(1, (await RunHelperAsync(channel, "Password: ")).ExitCode);
        Assert.Equal(1, (await RunHelperAsync(null, "Password: ")).ExitCode);
    }

    private static async Task<(int ExitCode, string Output)> RunHelperAsync(string? channel, string prompt)
    {
        var helper = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "kxc-askpass.exe" : "kxc-askpass");
        var startInfo = new ProcessStartInfo(helper) { RedirectStandardOutput = true, UseShellExecute = false };
        startInfo.ArgumentList.Add(prompt);
        startInfo.Environment.Remove(AskPassProtocol.ChannelVariable);
        if (channel is not null)
        {
            startInfo.Environment[AskPassProtocol.ChannelVariable] = channel;
        }

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, output.Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
