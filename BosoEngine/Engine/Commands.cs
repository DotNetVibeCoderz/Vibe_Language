using System;
using System.Diagnostics;
using System.IO;

namespace BosoEngine.Engine
{
    /// <summary>
    /// Command handler untuk transpile, run, dan compile.
    /// </summary>
    public static class Commands
    {
        public static int Transpile(string input, string? output)
        {
            string boso = File.ReadAllText(input);
            var transpiler = new BosoTranspiler();
            string csharp = transpiler.TranspileToCSharp(boso);

            if (string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine(csharp);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
                File.WriteAllText(output, csharp);
                Console.WriteLine($"Transpiled to: {output}");
            }
            return 0;
        }

        public static int Run(string input)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "boso_run");
            Directory.CreateDirectory(tempDir);

            string tempCs = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(input) + ".cs");
            //string tempProj = Path.Combine(tempDir, "temp.csproj");

            // transpile dulu
            Transpile(input, tempCs);

			/*
            // buat csproj sederhana
            File.WriteAllText(tempProj, "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                                         "  <PropertyGroup>\n" +
                                         "    <OutputType>Exe</OutputType>\n" +
                                         "    <TargetFramework>net8.0</TargetFramework>\n" +
                                         "    <ImplicitUsings>enable</ImplicitUsings>\n" +
                                         "    <Nullable>enable</Nullable>\n" +
                                         "  </PropertyGroup>\n" +
                                         "</Project>\n");
			*/
            return RunProcess("dotnet", $"\"{tempCs}\"");
        }

        public static int Compile(string input, string? output)
        {
			var uid = Guid.NewGuid().ToString().Replace("-", "");
            string tempDir = Path.Combine(Path.GetTempPath(), $"boso_{uid}");
            Directory.CreateDirectory(tempDir);
			var projectName = Path.GetFileNameWithoutExtension(input);
            string tempCs = Path.Combine(tempDir, "Program.cs");
            string tempProj = Path.Combine(tempDir, $"{projectName}.csproj");
            string outExe = output ?? Path.Combine(tempDir, "bin", "Release", "net8.0", $"{projectName}.exe");//Path.Combine(tempDir, $"{projectName}.exe");

            // transpile dulu
            Transpile(input, tempCs);

            File.WriteAllText(tempProj, "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                                         "  <PropertyGroup>\n" +
                                         "    <OutputType>Exe</OutputType>\n" +
                                         "    <TargetFramework>net8.0</TargetFramework>\n" +
                                         "    <ImplicitUsings>enable</ImplicitUsings>\n" +
                                         "    <Nullable>enable</Nullable>\n" +
                                         "  </PropertyGroup>\n" +
                                         "</Project>\n");

            int build = RunProcess("dotnet", $"build \"{tempProj}\" -c Release");
            if (build != 0) return build;

            // Copy output (default path bin/Release/net8.0/temp.dll) ke output
            string builtDll = Path.Combine(tempDir, "bin", "Release", "net8.0", $"{projectName}.dll");
            string builtExe = Path.Combine(tempDir, "bin", "Release", "net8.0", $"{projectName}.exe");
			
            if (File.Exists(builtExe))
            {
				outExe = builtExe;
                //File.Copy(builtExe, outExe, true);
            }else
            if (File.Exists(builtDll))
            {
				outExe = builtDll;
				// jika hanya dll, buat exe dengan dotnet publish? sederhana: copy dll
				//File.Copy(builtDll, outExe, true);
			}
			
            Console.WriteLine($"Compiled output: {outExe}");
            return 0;
        }

        private static int RunProcess(string fileName, string args)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            process.Start();
            Console.WriteLine(process.StandardOutput.ReadToEnd());
            Console.Error.WriteLine(process.StandardError.ReadToEnd());
            process.WaitForExit();

            return process.ExitCode;
        }
    }
}
