using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SteamKit2;

namespace SteamDepotFileVerifier
{
    public class SteamClientUtils
    {
        public readonly string SteamPath;

        public SteamClientUtils()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam") ??
                          RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                              .OpenSubKey("SOFTWARE\\Valve\\Steam");

                if (key != null && key.GetValue("SteamPath") is string steamPath)
                {
                    SteamPath = steamPath;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var paths = new [] {".steam", ".steam/steam", ".steam/root", ".local/share/Steam"};

                SteamPath = paths
                    .Select(path => Path.Join(home, path))
                    .FirstOrDefault(steamPath => Directory.Exists(Path.Join(steamPath, "appcache")));
            }
            else
            {
                throw new PlatformNotSupportedException();
            }

            if (SteamPath == default)
            {
                throw new DirectoryNotFoundException("Unable to find Steam on your computer.");
            }

            SteamPath = Path.GetFullPath(SteamPath);
        }

        public IEnumerable<string> GetLibraries()
        {
            var libraryFoldersPath = Path.Join(SteamPath, "steamapps", "libraryfolders.vdf");
            var libraryFoldersKv = KeyValue.LoadAsText(libraryFoldersPath);
            var libraryFolders = new List<string>
            {
                Path.Join(SteamPath, "steamapps")
            };

            if (libraryFoldersKv != null)
            {
                libraryFolders.AddRange(libraryFoldersKv.Children
                    .Where(libraryFolder => int.TryParse(libraryFolder.Name, out _))
                    .Select(x => Path.Join(x.Value, "steamapps"))
                );
            }

            return libraryFolders;
        }
    }
}
