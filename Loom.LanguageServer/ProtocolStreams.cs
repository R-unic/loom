namespace Loom.LanguageServer;

public sealed class ProtocolStreams(Stream input, Stream output)
{
    public Stream Input { get; } = input;
    public Stream Output { get; } = output;

    public static ProtocolStreams ClaimStandardStreams() =>
        Claim(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error);

    public static ProtocolStreams Claim(Stream input, Stream output, TextWriter consoleOutput)
    {
        var streams = new ProtocolStreams(input, output);
        Console.SetOut(consoleOutput);
        Console.SetIn(TextReader.Null);

        return streams;
    }
}
