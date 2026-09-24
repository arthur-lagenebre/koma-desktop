namespace Koma.Cli;

/// <summary>
/// The console the commands write to.
/// </summary>
/// <remarks>
/// The point of the program is to let a person put a real file in front of
/// the reader and see what it says, which no test does.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args) => Commands.Run(args, Console.Out, Console.Error);
}
