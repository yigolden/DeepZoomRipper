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
            command.AddOption(UseSharedQuantizationTables());

            command.AddArgument(new Argument<string>()
            {
                Name = "source",
                Description = "Source file location. (.dzi file)",
                Arity = ArgumentArity.ZeroOrOne
            });

            command.Handler = CommandHandler.Create<string, FileInfo, int, bool, bool, CancellationToken>(RipActions.Rip);

            Option Output() =>
                new Option<FileInfo>(new[] { "--output", "--out", "-o" }, "Output TIFF file location.")
                {
                    Arity = ArgumentArity.ExactlyOne
                };

            Option TileSize() =>
                new Option<int>("--tile-size", () => 256, "Tile size in the output TIFF.")
                {
                    Arity = ArgumentArity.ExactlyOne
                };

            Option NoSoftwareField() =>
                new Option<bool>("--no-software-field", () => false, "Skip writting Software field.")
                {
                    Arity = ArgumentArity.ZeroOrOne
                };

            Option UseSharedQuantizationTables() =>
                new Option<bool>("--use-shared-quantization-tables", () => false, "Use shared JPEG quantization tables between tiles. [false]")
                {
                    Arity = ArgumentArity.ZeroOrOne
                };

        }

    }
}
