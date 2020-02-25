using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading;

namespace DeepZoomRipperCli
{
    internal static class CommandDefinitions
    {

        public static void SetupRipCommand(Command command)
        {
            command.Description = "Download deepzoom files and save them into a Pyramid TIFF file.";

            command.AddOption(Output());
            command.AddOption(TileSize());
            command.AddOption(NoSoftwareField());

            command.AddArgument(new Argument<string>()
            {
                Name = "source",
                Description = "Source file location. (.dzi file)",
                Arity = ArgumentArity.ZeroOrOne
            });

            command.Handler = CommandHandler.Create<string, FileInfo, int, bool, CancellationToken>(RipActions.Rip);

            Option Output() =>
                new Option(new[] { "--output", "--out", "-o" }, "Output TIFF file location.")
                {
                    Argument = new Argument<FileInfo>() { Arity = ArgumentArity.ExactlyOne }
                };

            Option TileSize() =>
                new Option("--tile-size", "Tile size in the output TIFF. [256]")
                {
                    Argument = new Argument<int>(() => 256) { Arity = ArgumentArity.ExactlyOne }
                };

            Option NoSoftwareField() =>
                new Option("--no-software-field", "Skip writting Software field. [false]")
                {
                    Argument = new Argument<bool>(() => false) { Arity = ArgumentArity.ZeroOrOne }
                };

        }

    }
}
