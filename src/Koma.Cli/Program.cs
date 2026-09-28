using System.Text;

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
    private static int Main(string[] args)
    {
        // A Windows console still speaks its ancient code page unless told
        // otherwise, and a letterer called Moscow(star)Eye came out as
        // Moscow?Eye — which looked like a fault in the conversion and was a
        // fault in the terminal. The package held the right character all
        // along; now so does what is printed about it.
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException)
        {
            // A console that refuses is a console writing somewhere else — a
            // pipe, a file, a service — and the program has nothing to say
            // about it.
        }

        return Commands.Run(args, Console.Out, Console.Error);
    }
}
