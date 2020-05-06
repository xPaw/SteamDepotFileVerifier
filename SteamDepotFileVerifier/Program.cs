using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using SteamKit2;

namespace SteamDepotFileVerifier
{
    public static class SteamDepotFileVerifierProgram
    {
        static int Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine();
            Console.WriteLine("[>] Follow https://twitter.com/thexpaw :)");
            Console.WriteLine();
            Console.ResetColor();

            if (args.Length == 0 || !uint.TryParse(args[0], out var appid))
            {
                Console.WriteLine("Provide appid as the first argument, for example: ./SteamDepotFileVerifier 730");
                return 1;
            }

            Console.WriteLine($"AppID: {appid}");

            string steamLocation = null;

            var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam") ??
                      RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey("SOFTWARE\\Valve\\Steam");

            if (key != null && key.GetValue("SteamPath") is string steamPath)
            {
                steamLocation = steamPath;
            }

            if (steamLocation == null)
            {
                Console.Error.WriteLine("Can not find Steam");
                return 1;
            }

            Console.WriteLine($"Steam: {steamLocation}");

            var acf = Path.Join(steamLocation, "steamapps", $"appmanifest_{appid}.acf");

            if (!File.Exists(acf))
            {
                throw new FileNotFoundException("App manifest not found", acf);
            }

            var kv = KeyValue.LoadAsText(acf);
            var mountedDepots = kv["MountedDepots"];
            var gamePath = Path.Join(steamLocation, "steamapps", "common", kv["installdir"].Value);

            if (!Directory.Exists(gamePath))
            {
                throw new DirectoryNotFoundException($"Game not found: {gamePath}");
            }

            var allKnownDepotFiles = new Dictionary<string, ulong>();

            foreach (var mountedDepot in mountedDepots.Children)
            {
                var manifestPath = Path.Join(steamLocation, "depotcache", $"{mountedDepot.Name}_{mountedDepot.Value}.manifest");

                if (!File.Exists(manifestPath))
                {
                    throw new FileNotFoundException("Manifest not found", manifestPath);
                }

                var manifest = DumbDepotManifestHack(File.ReadAllBytes(manifestPath));

                foreach (var file in manifest.Files)
                {
                    allKnownDepotFiles[file.FileName] = file.TotalSize;
                }

                Console.WriteLine($"{manifestPath} - {manifest.Files.Count} files");
            }

            Console.WriteLine($"{allKnownDepotFiles.Count} files in depot manifests");

            var filesOnDisk = Directory.GetFiles(gamePath, "*", SearchOption.AllDirectories);

            Console.WriteLine($"{filesOnDisk.Length} files on disk");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;

            foreach (var file in filesOnDisk)
            {
                var unprefixedPath = file.Substring(gamePath.Length + 1);

                if (unprefixedPath == "installscript.vdf")
                {
                    continue;
                }

                if (!allKnownDepotFiles.ContainsKey(unprefixedPath))
                {
                    Console.WriteLine("Unknown file: " + unprefixedPath);
                    continue;
                }

                var length = new FileInfo(file).Length;

                if (allKnownDepotFiles[unprefixedPath] != (ulong)length)
                {
                    Console.WriteLine($"Mismatching file size: {unprefixedPath} (is {length}, should be {allKnownDepotFiles[unprefixedPath]})");
                    continue;
                }
            }

            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("If some files are listed as unknown but shouldn't be, they are probably from the SDK.");
            Console.WriteLine("After deleting files, verify the game files in Steam.");

            return 0;
        }

        private static DepotManifest DumbDepotManifestHack(byte[] data)
        {
            var exampleType = typeof(DepotManifest);
            var argTypes = new[] { typeof(byte[]) };
            var ctor = exampleType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, argTypes, null);
            return (DepotManifest)ctor.Invoke(new []{ data });
        }
    }
}
