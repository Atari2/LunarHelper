using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using AsarCLR;

namespace SMWPatcher
{
    class Program
    {
        static public Config Config { get; private set; }

        static private readonly Regex LevelRegex = new Regex("[0-9a-fA-F]{3}");
        static private Process RetroArchProcess;

        static void Main(string[] args)
        {
            bool running = true;
            while (running)
            {
                Log("Welcome to Lunar Helper ^_^", ConsoleColor.Cyan);
                Log("B - Build, T - Build and Test, O - Test Only, ESC - Exit");
                Console.WriteLine();

                var key = Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.B:
                        if (Init())
                            Build();
                        break;

                    case ConsoleKey.T:
                        if (Init() && Build())
                            Test();
                        break;

                    case ConsoleKey.O:
                        if (Init())
                            Test();
                        break;

                    case ConsoleKey.Escape:
                        running = false;
                        Log("Have a nice day!", ConsoleColor.Cyan);
                        Console.ForegroundColor = ConsoleColor.White;
                        break;

                    default:
                        Log("Key not recognized!!", ConsoleColor.Red);
                        Console.WriteLine();
                        break;
                }
            }
        }

        static private bool Init()
        {
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            // load config
            Config = Config.Load();
            if (Config == null)
            {
                Error("Could not open config.txt");
                return false;
            }

            // set the working directory
            if (!string.IsNullOrWhiteSpace(Config.WorkingDirectory))
                Directory.SetCurrentDirectory(Config.WorkingDirectory);

            // some error checks
            if (string.IsNullOrWhiteSpace(Config.InputPath))
            {
                Error("No Input ROM path provided!");
                return false;
            }
            else if (!File.Exists(Config.InputPath))
            {
                Error($"Input ROM file '{Config.InputPath}' does not exist!");
                return false;
            }
            else if (string.IsNullOrWhiteSpace(Config.OutputPath))
            {
                Error("No Output ROM path provided!");
                return false;
            }
            else if (string.IsNullOrWhiteSpace(Config.TempPath))
            {
                Error("No Temp ROM path provided!");
                return false;
            }

            return true;
        }

        static private bool Build()
        {
            // create temp ROM to operate on, in case something goes wrong
            if (File.Exists(Config.TempPath))
                File.Delete(Config.TempPath);
            File.Copy(Config.InputPath, Config.TempPath);

            // apply asar patches
            Log("1 - Patches", ConsoleColor.Cyan);
            if (Config.Patches.Count == 0)
                Log("Path to Asar provided, but no patches were registerd to be applied.", ConsoleColor.Red);
            else
            {
                if (!Asar.init()) {
                    Log("Failed to initialize asar dll", ConsoleColor.Red);
                    return false;
                }
                byte[] romData = File.ReadAllBytes(Config.TempPath);
                var headerSize = romData.Length & 0x7FFF;
                var romsize = romData.Length - headerSize;
                byte[] realRomData = romData[headerSize..];
                byte[] headerData = romData[0..headerSize];
                foreach (var patch in Config.Patches)
                {
                    Lognl($"- Applying patch '{patch}'...  ", ConsoleColor.Yellow);
                    if (!Asar.patch(patch, ref realRomData)) {
                        var errors = Asar.geterrors();
                        Log($"Patching failure! {errors.Aggregate("", (x, b) => x += b.Fullerrdata + '\n')}", ConsoleColor.Red);
                        Asar.close();
                        return false;
                    }
                }
                using var romStream = new FileStream(Config.TempPath, FileMode.Truncate);
                romStream.Write(headerData);
                romStream.Write(realRomData);
                Log("Patching Success!", ConsoleColor.Green);
                Asar.close();
                Console.WriteLine();
            }

            // run GPS
            Log("2 - GPS", ConsoleColor.Cyan);
            if (string.IsNullOrWhiteSpace(Config.GPSPath))
                Log("No path to GPS provided, no music will be inserted.", ConsoleColor.Red);
            else if (!File.Exists(Config.GPSPath))
                Log("GPS not found at provided path, no music will be inserted.", ConsoleColor.Red);
            else
            {
                var dir = Path.GetFullPath(Path.GetDirectoryName(Config.GPSPath));
                var rom = Path.GetRelativePath(dir, Path.GetFullPath(Config.TempPath));

                ProcessStartInfo psi = new ProcessStartInfo(Config.GPSPath, $"-l \"{dir}/list.txt\" {rom}") {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = dir
                };

                var p = Process.Start(psi);
                p.WaitForExit();

                if (p.ExitCode == 0)
                    Log("GPS Success!", ConsoleColor.Green);
                else
                {
                    Log("GPS Failure!", ConsoleColor.Red);
                    //Error(p.StandardError.ReadToEnd());
                    return false;
                }

                Console.WriteLine();
            }

            // run AddMusicK
            Log("3 - AddMusicK", ConsoleColor.Cyan);
            if (string.IsNullOrWhiteSpace(Config.AddMusicKPath))
                Log("No path to AddMusicK provided, no music will be inserted.", ConsoleColor.Red);
            else if (!File.Exists(Config.AddMusicKPath))
                Log("AddMusicK not found at provided path, no music will be inserted.", ConsoleColor.Red);
            else
            {
                var dir = Path.GetFullPath(Path.GetDirectoryName(Config.AddMusicKPath));
                var rom = Path.GetRelativePath(dir, Path.GetFullPath(Config.TempPath));
                Console.ForegroundColor = ConsoleColor.Gray;

                ProcessStartInfo psi = new ProcessStartInfo(Config.AddMusicKPath, rom) {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = dir
                };

                var p = Process.Start(psi);
                while (!p.HasExited)
                    p.StandardInput.Write('a');

                if (p.ExitCode == 0)
                    Log("AddMusicK Success!", ConsoleColor.Green);
                else
                {
                    Log("AddMusicK Failure!", ConsoleColor.Red);
                    Error(p.StandardError.ReadToEnd());
                    return false;
                }

                Console.WriteLine();
            }

            // import gfx
            Log("4 - Graphics", ConsoleColor.Cyan);
            if (string.IsNullOrWhiteSpace(Config.LunarMagicPath))
                Log("No path to Lunar Magic provided, no graphics will be imported.", ConsoleColor.Red);
            else if (!File.Exists(Config.LunarMagicPath))
                Log("Lunar Magic not found at provided path, no graphics will be imported.", ConsoleColor.Red);
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                ProcessStartInfo psi = new ProcessStartInfo(Config.LunarMagicPath,
                            $"-ImportAllGraphics {Config.TempPath}");
                var p = Process.Start(psi);
                p.WaitForExit();

                if (p.ExitCode == 0)
                    Log("Import Graphics Success!", ConsoleColor.Green);
                else
                {
                    Log("Import Graphics Failure!", ConsoleColor.Red);
                    return false;
                }

                // rename MSC file so music track names work in LM
                var msc_at = $"{Path.GetFileNameWithoutExtension(Config.TempPath)}.msc";
                var msc_to = $"{Path.GetFileNameWithoutExtension(Config.OutputPath)}.msc";
                if (File.Exists(msc_to))
                    File.Delete(msc_to);
                if (File.Exists(msc_at))
                    File.Move(msc_at, msc_to);

                Console.WriteLine();
            }

            // import levels
            Log("5 - Levels", ConsoleColor.Cyan);
            if (string.IsNullOrWhiteSpace(Config.LevelsPath))
                Log("No path to Levels provided, no levels will be imported.", ConsoleColor.Red);
            else if (string.IsNullOrWhiteSpace(Config.LunarMagicPath))
                Log("No path to Lunar Magic provided, no levels will be imported.", ConsoleColor.Red);
            else if (!File.Exists(Config.LunarMagicPath))
                Log("Lunar Magic not found at provided path, no levels will be imported.", ConsoleColor.Red);
            else
            {
                // import levels
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    ProcessStartInfo psi = new ProcessStartInfo(Config.LunarMagicPath,
                                $"-ImportMultLevels {Config.TempPath} {Config.LevelsPath}");
                    var p = Process.Start(psi);
                    p.WaitForExit();

                    if (p.ExitCode == 0)
                        Log("Levels Import Success!", ConsoleColor.Green);
                    else
                    {
                        Log("Levels Import Failure!", ConsoleColor.Red);
                        return false;
                    }

                    Console.WriteLine();
                }             
            }

            // apply pixi, it's important to apply it after having ported levels, as pixi relies on a hijack that only gets applied
            // by LM if there are edited levels in the ROM
            Log("6 - Pixi", ConsoleColor.Cyan);
            if (string.IsNullOrWhiteSpace(Config.PixiPath))
                Log("No path to Pixi provided, no sprites will be inserted.", ConsoleColor.Red);
            else if (!File.Exists(Config.PixiPath))
                Log("Pixi not found at provided path, no sprites will be inserted.", ConsoleColor.Red);
            else {
                var dir = Path.GetFullPath(Path.GetDirectoryName(Config.PixiPath));
                var rom = Path.GetRelativePath(dir, Path.GetFullPath(Config.TempPath));
                var listpath = Path.GetDirectoryName(dir) + Path.DirectorySeparatorChar + "list.txt";
                Console.ForegroundColor = ConsoleColor.Gray;

                // pixi is a weird little tool and we need to specify the list path
                ProcessStartInfo psi = new ProcessStartInfo(Config.AddMusicKPath, $"-l {listpath} {rom}") {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = dir
                };

                var p = Process.Start(psi);
                while (!p.HasExited)
                    p.StandardInput.Write('a');

                if (p.ExitCode == 0)
                    Log("Pixi Success!", ConsoleColor.Green);
                else {
                    Log("Pixi Failure!", ConsoleColor.Red);
                    Error(p.StandardOutput.ReadToEnd());
                    return false;
                }

                Console.WriteLine();
            }

            // import map16
            Log("6 - Map16", ConsoleColor.Cyan);
            if (string.IsNullOrWhiteSpace(Config.Map16Path))
                Log("No path to Levels provided, no map16 will be imported.", ConsoleColor.Red);
            else if (string.IsNullOrWhiteSpace(Config.LunarMagicPath))
                Log("No path to Lunar Magic provided, no map16 will be imported.", ConsoleColor.Red);
            else if (!File.Exists(Config.LunarMagicPath))
                Log("Lunar Magic not found at provided path, no map16 will be imported.", ConsoleColor.Red);
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                ProcessStartInfo psi = new ProcessStartInfo(Config.LunarMagicPath,
                            $"-ImportAllMap16 {Config.TempPath} {Config.Map16Path}");
                var p = Process.Start(psi);
                p.WaitForExit();

                if (p.ExitCode == 0)
                    Log("Map16 Import Success!", ConsoleColor.Green);
                else
                {
                    Log("Map16 Import Failure!", ConsoleColor.Red);
                    return false;
                }

                Console.WriteLine();
            }

            // import overworld
            Log("6 - Overworld", ConsoleColor.Cyan);
            if (string.IsNullOrWhiteSpace(Config.OverworldPath))
                Log("No path to Overworld ROM provided, no overworld will be imported.", ConsoleColor.Red);
            else if (string.IsNullOrWhiteSpace(Config.LunarMagicPath))
                Log("No path to Lunar Magic provided, no overworld will be imported.", ConsoleColor.Red);
            else if (!File.Exists(Config.LunarMagicPath))
                Log("Lunar Magic not found at provided path, no overworld will be imported.", ConsoleColor.Red);
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                ProcessStartInfo psi = new ProcessStartInfo(Config.LunarMagicPath,
                            $"-TransferOverworld {Config.TempPath} {Config.OverworldPath}");
                var p = Process.Start(psi);
                p.WaitForExit();

                if (p.ExitCode == 0)
                    Log("Overworld Import Success!", ConsoleColor.Green);
                else
                {
                    Log("Overworld Import Failure!", ConsoleColor.Red);
                    return false;
                }

                Console.WriteLine();
            }

            // output final ROM
            if (File.Exists(Config.OutputPath))
                File.Delete(Config.OutputPath);
            File.Move(Config.TempPath, Config.OutputPath);

            Log($"ROM patched successfully to '{Config.OutputPath}'!", ConsoleColor.Cyan);
            Console.WriteLine();

            return true;
        }

        static private bool Test()
        {
            Console.WriteLine();
            Log("Initiating Test routine!", ConsoleColor.Magenta);

            // test level
            if (!string.IsNullOrWhiteSpace(Config.TestLevel) && !string.IsNullOrWhiteSpace(Config.TestLevelDest))
            {
                var files = Directory.GetFiles(Config.LevelsPath, $"*{Config.TestLevel}*.mwl");

                if (!LevelRegex.IsMatch(Config.TestLevel))
                    Log("Test Level ID must be a 3-character hex value", ConsoleColor.Red);
                else if (!LevelRegex.IsMatch(Config.TestLevelDest))
                    Log("Test Level Dest ID must be a 3-character hex value", ConsoleColor.Red);
                else if (files.Length == 0)
                    Log($"Test Level {Config.TestLevel} not found in {Config.LevelsPath}", ConsoleColor.Red);
                else
                {
                    var path = files[0];

                    Log($"Importing level {Config.TestLevel} to {Config.TestLevelDest} for testing...  ", ConsoleColor.Yellow);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    ProcessStartInfo psi = new ProcessStartInfo(Config.LunarMagicPath,
                        $"-ImportLevel {Config.OutputPath} \"{path}\" {Config.TestLevelDest}");
                    var p = Process.Start(psi);
                    p.WaitForExit();

                    if (p.ExitCode == 0)
                        Log("Test Level Import Success!", ConsoleColor.Green);
                    else
                    {
                        Log("Test Level Import Failure!", ConsoleColor.Red);
                        return false;
                    }
                }

                // retroarch
                if (!string.IsNullOrWhiteSpace(Config.RetroArchPath))
                {
                    Log("Launching RetroArch...", ConsoleColor.Yellow);
                    var fullRom = Path.GetFullPath(Config.OutputPath);

                    if (RetroArchProcess != null && !RetroArchProcess.HasExited)
                        RetroArchProcess.Kill(true);

                    ProcessStartInfo psi = new ProcessStartInfo(Config.RetroArchPath,
                        $"-L \"{Config.RetroArchCore}\" \"{fullRom}\"");
                    RetroArchProcess = Process.Start(psi);
                }
            }

            Log("Test routine complete!", ConsoleColor.Magenta);
            Console.WriteLine();

            return true;
        }

        static private void Error(string error)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"ERROR: {error}");
        }

        static private void Log(string msg, ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"{msg}");
        }

        static private void Lognl(string msg, ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
            Console.Write($"{msg}");
        }
    }
}
