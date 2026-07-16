using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Roletopia.Installer
{
    internal static class Program
    {
        private const string AmongUsFolderName = "Among Us";

        private static int Main(string[] args)
        {
            Console.Title = "Roletopia Installer";
            PrintWelcome();

            var sourceModPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "roletopia");
            if (!Directory.Exists(sourceModPath))
            {
                WriteError("Mod files were not found next to the installer. Please keep the 'roletopia' folder beside Roletopia-Installer.exe.");
                WaitForExit();
                return 1;
            }

            var amongUsPath = ResolveAmongUsPath(args);
            if (string.IsNullOrEmpty(amongUsPath))
            {
                WriteError("Installation cancelled.");
                WaitForExit();
                return 1;
            }

            var installPath = Path.Combine(amongUsPath, "Mods", "Roletopia");

            try
            {
                Console.WriteLine();
                Console.WriteLine("Installing Roletopia...");
                CopyDirectory(sourceModPath, installPath);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Success! Roletopia is installed.");
                Console.ResetColor();
                Console.WriteLine("Installed to: " + installPath);
                Console.WriteLine("You can now launch Among Us.");
                WaitForExit();
                return 0;
            }
            catch (Exception ex)
            {
                WriteError("Installation failed: " + ex.Message);
                WaitForExit();
                return 1;
            }
        }

        private static void PrintWelcome()
        {
            Console.WriteLine("Roletopia Among Us Installer");
            Console.WriteLine("----------------------------");
            Console.WriteLine("This installer copies Roletopia mod files into your Among Us folder.");
        }

        private static string ResolveAmongUsPath(string[] args)
        {
            var candidates = new List<string>();
            if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                candidates.Add(args[0]);
            }

            candidates.AddRange(GetDefaultAmongUsPaths());

            foreach (var candidate in candidates)
            {
                if (TryGetValidAmongUsPath(candidate, out var resolvedPath))
                {
                    Console.WriteLine();
                    Console.WriteLine("Among Us detected: " + resolvedPath);
                    return resolvedPath;
                }
            }

            Console.WriteLine();
            Console.WriteLine("Among Us was not detected automatically.");
            Console.WriteLine("Please enter your Among Us install folder (the folder containing Among Us.exe).");

            while (true)
            {
                Console.Write("> ");
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    return null;
                }

                if (TryGetValidAmongUsPath(input, out var resolvedPath))
                {
                    return resolvedPath;
                }

                WriteError("That path is invalid. Please enter a folder containing Among Us.exe, or press Enter to cancel.");
            }
        }

        private static IEnumerable<string> GetDefaultAmongUsPaths()
        {
            var paths = new List<string>();

            AddSteamPath(paths, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            AddSteamPath(paths, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                paths.Add(Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common", AmongUsFolderName));
                paths.Add(Path.Combine(drive.RootDirectory.FullName, "Games", "SteamLibrary", "steamapps", "common", AmongUsFolderName));
            }

            return paths.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static void AddSteamPath(ICollection<string> paths, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return;
            }

            paths.Add(Path.Combine(rootPath, "Steam", "steamapps", "common", AmongUsFolderName));
        }

        private static bool TryGetValidAmongUsPath(string path, out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalizedPath = path.Trim().Trim('"');
            var expandedPath = Environment.ExpandEnvironmentVariables(normalizedPath);

            if (LooksLikeAmongUsFolder(expandedPath))
            {
                resolvedPath = Path.GetFullPath(expandedPath);
                return true;
            }

            var nestedPath = Path.Combine(expandedPath, AmongUsFolderName);
            if (LooksLikeAmongUsFolder(nestedPath))
            {
                resolvedPath = Path.GetFullPath(nestedPath);
                return true;
            }

            return false;
        }

        private static bool LooksLikeAmongUsFolder(string path)
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            var executablePath = Path.Combine(path, "Among Us.exe");
            var dataFolderPath = Path.Combine(path, "Among Us_Data");

            return File.Exists(executablePath) && Directory.Exists(dataFolderPath);
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = GetRelativePath(sourceDirectory, directory);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
            }

            foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = GetRelativePath(sourceDirectory, file);
                var destination = Path.Combine(destinationDirectory, relative);
                File.Copy(file, destination, true);
            }
        }

        private static string GetRelativePath(string sourceDirectory, string path)
        {
            var normalizedSource = Path.GetFullPath(sourceDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path);

            if (normalizedPath.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedPath.Substring(normalizedSource.Length);
            }

            return Path.GetFileName(normalizedPath);
        }

        private static void WriteError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private static void WaitForExit()
        {
            Console.WriteLine();
            Console.WriteLine("Press Enter to close.");
            Console.ReadLine();
        }
    }
}
