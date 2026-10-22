using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Kxc.Core.Connections;

// ssh chiama l'helper con la domanda come primo argomento e legge la risposta da stdout.
// Exit code diverso da zero: ssh considera la domanda senza risposta e non ripete.
var channel = Environment.GetEnvironmentVariable(AskPassProtocol.ChannelVariable);
if (string.IsNullOrWhiteSpace(channel) || args.Length == 0)
{
    return 1;
}

var answer = GC.AllocateArray<byte>(AskPassProtocol.MaxMessageBytes, pinned: true);
try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    using var pipe = new NamedPipeClientStream(".", channel, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
    await pipe.ConnectAsync(TimeSpan.FromSeconds(5), timeout.Token);

    await AskPassProtocol.WriteMessageAsync(pipe, Encoding.UTF8.GetBytes(string.Join(' ', args)), timeout.Token);

    var status = new byte[1];
    await pipe.ReadExactlyAsync(status, timeout.Token);
    var length = await AskPassProtocol.ReadMessageAsync(pipe, answer, timeout.Token);
    if (status[0] != AskPassProtocol.Answer)
    {
        return 1;
    }

    using var stdout = Console.OpenStandardOutput();
    stdout.Write(answer, 0, length);
    stdout.WriteByte((byte)'\n');
    stdout.Flush();
    return 0;
}
catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or InvalidDataException)
{
    return 1;
}
finally
{
    CryptographicOperations.ZeroMemory(answer);
}
