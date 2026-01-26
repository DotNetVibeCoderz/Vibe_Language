using System;
using System.IO;
using BosoEngine.Engine;

namespace BosoEngine
{
    internal class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                PrintHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            string? input = args.Length > 1 ? args[1] : null;
            string? output = GetOption(args, "-o") ?? GetOption(args, "--output");

            try
            {
                switch (command)
                {
                    case "transpile":
                        RequireInput(input);
                        return Commands.Transpile(input!, output);
                    case "run":
                        RequireInput(input);
                        return Commands.Run(input!);
                    case "compile":
                        RequireInput(input);
                        return Commands.Compile(input!, output);
                    default:
                        Console.WriteLine($"Unknown command: {command}");
                        PrintHelp();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return 1;
            }
        }

        private static void RequireInput(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Input file is required.");
            if (!File.Exists(input))
                throw new FileNotFoundException("Input file not found.", input);
        }

        private static string? GetOption(string[] args, string name)
        {
            int idx = Array.IndexOf(args, name);
            if (idx >= 0 && idx < args.Length - 1)
                return args[idx + 1];
            return null;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("BosoEngine - Boso Script Tool");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  boso transpile <file.boso> [-o output.cs]");
            Console.WriteLine("  boso run <file.boso>");
            Console.WriteLine("  boso compile <file.boso> [-o output.exe]");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  boso transpile samples/hello.boso -o out/hello.cs");
            Console.WriteLine("  boso run samples/hello.boso");
            Console.WriteLine("  boso compile samples/hello.boso -o out/hello.exe");
        }
    }
}
