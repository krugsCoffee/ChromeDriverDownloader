
// For Windows 10+ Only

namespace ChromeDriverDownloader
{
    using CoffeeHelpers;
    using Microsoft.Win32;
    using System.Diagnostics;
    using System.Text.RegularExpressions;

    public static class ChromeDriverDownloader
    {
        const string FromChromeLabsUrl = @"https://googlechromelabs.github.io/chrome-for-testing/known-good-versions-with-downloads.json";
        const string FromGoogleApis = "https://chromedriver.storage.googleapis.com/"; 
        const string ChromeDriverExecutableName = "chromedriver.exe";
        const string GoogleChromeRegistryExecutablePath = @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe";

        internal class Root
        {
            public class ChromeVersion
            {
                public class Download
                {
                    public class Machine
                    {
                        public required string Platform { get; set; }
                        public required string Url { get; set; }
                    }

                    public List<Machine>? Chromedriver { get; set; }
                }

                public Version GetVersion()
                {
                    int[] versionInts = Version.Split('.').Select(int.Parse).ToArray();

                    return versionInts.Length switch
                    {
                        1 => new Version(versionInts[0], 0),
                        2 => new Version(versionInts[0], versionInts[1]),
                        3 => new Version(versionInts[0], versionInts[1], versionInts[2]),
                        4 => new Version(versionInts[0], versionInts[1], versionInts[2], versionInts[3]),
                        _ => throw new ArgumentException("Array must have 1 to 4 elements.", nameof(versionInts))
                    };
                }
                public required string Version { get; set; }
                public required string Revision { get; set; }
                public required Download Downloads { get; set; }
            }
            public DateTime Timestamp { get; set; }
            public required List<ChromeVersion> Versions { get; set; }
        }

        public class ChromeDriverVersion
        {
            public required Version Version { get; set; }
            public required string DownloadUrl { get; set; }

            public override string ToString()
            {
                return $"{Version} - {DownloadUrl}";
            }
        }

        public static async Task<bool> Download(string downloadDirectory, Version version)
        {
            string downloadFilePath = Path.Combine(downloadDirectory, ChromeDriverExecutableName);

            // Get All Available Versions
            List<ChromeDriverVersion> availableVersions = await GetAvailableChromeDriverVersions();
            ChromeDriverVersion? matchedVersion = availableVersions
                .Where(v => v.Version.Major == version.Major)
                .OrderBy(v =>
                {
                    // Rank: exact match = 0 (best), then by how many components match
                    if (v.Version == version) return 0;
                    if (v.Version.Major == version.Major &&
                        v.Version.Minor == version.Minor &&
                        v.Version.Build == version.Build)
                        return 1;
                    if (v.Version.Major == version.Major &&
                        v.Version.Minor == version.Minor)
                        return 2;
                    if (v.Version.Major == version.Major)
                        return 3;

                    return int.MaxValue;
                })
                .FirstOrDefault();

            if (matchedVersion != null)
            {
                // Return true if already downloaded and is up-to-date
                if (File.Exists(downloadFilePath))
                {
                    var currentVersion = FileVersionInfo
                        .GetVersionInfo(downloadFilePath)
                        .FileVersion?
                        .AsVersion();

                    if (currentVersion != null && matchedVersion.Version == currentVersion)
                    {
                        Console.WriteLine("Chromedriver is up-to-date");
                        return true;
                    }
                }

                try
                {
                    Console.WriteLine($"Downloading v{matchedVersion.Version} from `{matchedVersion.DownloadUrl}`");
                    string zipLocation = await matchedVersion.DownloadUrl.DownloadAsync();
                    await zipLocation.ExtractFileFromZip(ChromeDriverExecutableName, downloadFilePath);
                    return true;
                }

                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return false;
                }
            }

            else
            {
                Console.WriteLine($"No downloads available for v{version}");
                return false;
            }
        }

        public static Version GetChromeVersion(string? chromeExecutableFilePath = null)
        {
            // Get file version of 'ChromeExecutableFilePath', or from google chrome's default registry path. Otherwise, throw exception
            string? filePathFromRegistry = Registry.GetValue(GoogleChromeRegistryExecutablePath, "", null) as string;
            string filePath = chromeExecutableFilePath != null ? chromeExecutableFilePath : filePathFromRegistry != null ? filePathFromRegistry : 
                throw new ArgumentException($"Cannot locate 'chrome.exe'. Supply this value to '{nameof(chromeExecutableFilePath)}'");

            return FileVersionInfo.GetVersionInfo(filePath).FileVersion?.AsVersion();
        }

        public static async Task<List<ChromeDriverVersion>> GetAvailableChromeDriverVersions()
        {
            List<ChromeDriverVersion> availableChromeDriverVersions = new List<ChromeDriverVersion>();

            // --- Google Apis ---
            // Download text, extract matching values based on '<Key>' tag and match those ending with 'win32.zip'
            try
            {
                string googleApisText = await FromGoogleApis.DownloadTextAsync();
                availableChromeDriverVersions.AddRange(Regex.Matches(googleApisText, @"<Key>(.*?)</Key>")
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .Where(key => key.EndsWith("win32.zip", StringComparison.OrdinalIgnoreCase))
                    .Select(key =>
                    {
                        var versionString = key.Split('/')[0];
                        var version = versionString.AsVersion();

                        return new ChromeDriverVersion
                        {
                            DownloadUrl = "https://chromedriver.storage.googleapis.com/" + key,
                            Version = version
                        };
                    })
                    .ToList());
            }

            catch (Exception e)
            {
                Console.WriteLine($"GoogleApis error: {e.Message}");
            }
            

            // --- Chromelabs ---
            // Download text, convert to root object, parse entries with 'win64' downloads available
            try
            {
                string chromeLabsText = await FromChromeLabsUrl.DownloadTextAsync();
                Root? root = chromeLabsText.JsonToObject<Root>();
                availableChromeDriverVersions.AddRange(root.Versions
                    .Where(v => v.Downloads.Chromedriver != null && v.Downloads.Chromedriver
                    .Any(p => p.Platform == "win64"))
                    .Select(v =>
                    {
                        var chromedriverWin64 = v.Downloads.Chromedriver
                            .First(p => p.Platform == "win64");

                        return new ChromeDriverVersion
                        {
                            Version = v.Version.AsVersion(),
                            DownloadUrl = chromedriverWin64.Url
                        };
                    }).ToList());
            }

            catch (Exception e)
            {
                Console.WriteLine($"ChromeLabs error: {e.Message}");
            }


            return availableChromeDriverVersions;
        }

        #region Download() Overloads
        public static async Task<bool> Download(int major)
        {
            Version version = new Version(major, 0);
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            return await Download(directory, version);
        }

        public static async Task<bool> Download(int major, int minor)
        {
            Version version = new Version(major, minor);
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            return await Download(directory, version);
        }

        public static async Task<bool> Download(int major, int minor, int build)
        {
            Version version = new Version(major, minor, build);
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            return await Download(directory, version);
        }

        public static async Task<bool> Download(int major, int minor, int build, int revision)
        {
            Version version = new Version(major, minor, build, revision);
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            return await Download(directory, version);
        }

        public static async Task<bool> Download(Version version)
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            return await Download(directory, version);
        }

        public static async Task<bool> Download(string downloadDirectory, int major)
        {
            Version version = new Version(major, 0);
            return await Download(downloadDirectory, version);
        }

        public static async Task<bool> Download(string downloadDirectory, int major, int minor)
        {
            Version version = new Version(major, minor);
            return await Download(downloadDirectory, version);
        }

        public static async Task<bool> Download(string downloadDirectory, int major, int minor, int build)
        {
            Version version = new Version(major, minor, build);
            return await Download(downloadDirectory, version);
        }

        public static async Task<bool> Download(string downloadDirectory, int major, int minor, int build, int revision)
        {
            Version version = new Version(major, minor, build, revision);
            return await Download(downloadDirectory, version);
        }
        public static async Task<bool> Download(string downloadDirectory)
        {
            Version version = new Version(GetChromeVersion().Major, 0);
            return await Download(downloadDirectory, version);
        }

        public static async Task<bool> Download()
        {
            Version version = new Version(GetChromeVersion().Major, 0);
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            return await Download(directory, version);
        }
        #endregion
    }
}
