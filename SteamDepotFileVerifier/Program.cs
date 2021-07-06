using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SteamKit2;

namespace SteamDepotFileVerifier
{
    public static class SteamDepotFileVerifierProgram
    {
        static int Main(string[] args)
        {
            var steamClient = new SteamClientUtils();
            var steamLibraries = steamClient.GetLibraries();

            Console.WriteLine($"Steam path: {steamClient.SteamPath}");
            Console.WriteLine();

            if (args.Length == 0 || !uint.TryParse(args[0], out var appid))
            {
                foreach (var library in steamLibraries)
                {
                    var manifests = Directory.GetFiles(library, "appmanifest_*.acf");

                    foreach (var appManifestPath in manifests)
                    {
                        VerifyApp(steamClient, appManifestPath);

                        Console.WriteLine();
                        Console.WriteLine("------------------------------");
                        Console.WriteLine();
                    }
                }
            }
            else
            {
                Console.WriteLine($"AppID: {appid}");

                var appManifestPath = steamLibraries
                    .Select(library => Path.Join(library, $"appmanifest_{appid}.acf"))
                    .FirstOrDefault(File.Exists);

                if (appManifestPath == null)
                {
                    throw new FileNotFoundException("Unable to find appmanifest anywhere.");
                }

                VerifyApp(steamClient, appManifestPath);
            }

            Console.WriteLine();
            Console.WriteLine("If some files are listed as unknown but shouldn't be, they are probably from the SDK.");
            Console.WriteLine("After deleting files, verify the game files in Steam.");

            return 0;
        }

        private static void VerifyApp(SteamClientUtils steamClient, string appManifestPath)
        {
            Console.WriteLine($"Parsing {appManifestPath}");

            var kv = KeyValue.LoadAsText(appManifestPath);
            var depotManifests = new Dictionary<string, string>();
            var gamePath = Path.Join(Path.GetDirectoryName(appManifestPath), "common", kv["installdir"].Value);

            if (!Directory.Exists(gamePath))
            {
                throw new DirectoryNotFoundException($"Game not found: {gamePath}");
            }

            Console.WriteLine($"Scanning {gamePath}");

            foreach (var mountedDepot in kv["MountedDepots"].Children)
            {
                depotManifests[mountedDepot.Name] = mountedDepot.Value;
            }

            foreach (var mountedDepot in kv["InstalledDepots"].Children)
            {
                depotManifests[mountedDepot.Name] = mountedDepot["manifest"].Value;
            }

            var allKnownDepotFiles = new Dictionary<string, DepotManifest.FileData>(StringComparer.OrdinalIgnoreCase);

            foreach (var (depotId, manifestId) in depotManifests)
            {
                var manifestPath = Path.Join(steamClient.SteamPath, "depotcache", $"{depotId}_{manifestId}.manifest");

                if (!File.Exists(manifestPath))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Error.WriteLine($"Manifest does not exist: {manifestPath}");
                    Console.Error.WriteLine($"Try verifying \"{kv["name"].Value}\" in Steam and running this again.");
                    Console.ResetColor();
                    return;
                }

                var manifest = DepotManifest.Deserialize(File.ReadAllBytes(manifestPath));

                foreach (var file in manifest.Files)
                {
                    if (file.Flags.HasFlag(EDepotFileFlag.Directory))
                    {
                        continue;
                    }

                    allKnownDepotFiles[file.FileName] = file;
                }

                Console.WriteLine($"{manifestPath} - {manifest.Files.Count} files");
            }

            Console.WriteLine($"{allKnownDepotFiles.Count} files in depot manifests");

            var filesOnDisk = Directory.GetFiles(gamePath, "*", SearchOption.AllDirectories).ToList();
            filesOnDisk.Sort();

            Console.WriteLine($"{filesOnDisk.Count} files on disk");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;

            var filenamesOnDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var file in filesOnDisk)
            {
                var unprefixedPath = file.Substring(gamePath.Length + 1);

                filenamesOnDisk.Add(unprefixedPath);

                if (unprefixedPath == "installscript.vdf" || Path.GetFileName(unprefixedPath) == "steam_appid.txt")
                {
                    continue;
                }

                if (!allKnownDepotFiles.TryGetValue(unprefixedPath, out var fileData))
                {
                    Console.WriteLine($"Unknown file: {unprefixedPath}");
                    continue;
                }

                if ((fileData.Flags & (EDepotFileFlag.UserConfig | EDepotFileFlag.VersionedUserConfig)) > 0)
                {
                    Console.WriteLine($"Skipping user config: {unprefixedPath}");
                    continue;
                }

                var length = new FileInfo(file).Length;

                if (fileData.TotalSize != (ulong)length)
                {
                    Console.WriteLine($"Mismatching file size: {unprefixedPath} (is {length} bytes, should be {fileData.TotalSize})");
                    continue;
                }
            }

            Console.ForegroundColor = ConsoleColor.Blue;

            foreach (var file in allKnownDepotFiles.Keys.Where(file => !filenamesOnDisk.Contains(file)))
            {
                Console.WriteLine($"Missing file: {file}");
            }

            Console.ResetColor();
        }
    }
}
