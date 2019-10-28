using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace DeepZoomRipperCli
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            Console.WriteLine(ThisAssembly.AssemblyTitle);
            Console.WriteLine(ThisAssembly.AssemblyInformationalVersion);
            Console.WriteLine();

            var builder = new CommandLineBuilder();

            CommandDefinitions.SetupRipCommand(builder.Command);

            builder.UseVersionOption();

            builder.UseHelp();
            builder.UseSuggestDirective();
            builder.RegisterWithDotnetSuggest();
            builder.UseParseErrorReporting();
            builder.UseExceptionHandler();

            Parser parser = builder.Build();
            await parser.InvokeAsync(args);
        }
    }
}
