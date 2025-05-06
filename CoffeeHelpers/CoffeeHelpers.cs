using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace CoffeeHelpers
{
    public static class CoffeeHelpers
    {
        // AppData folder name to be used by CoffeeHelpers
        public static string HelpersAppDataFolder { get; set; }
        static CoffeeHelpers()
        {
            #region Assign value to `HelpersAppDataFolder`
            var entryAssembly = Assembly.GetEntryAssembly();
            HelpersAppDataFolder = entryAssembly != null
                ? Path.GetFileNameWithoutExtension(entryAssembly.Location)
                : "HelpersClass";
            #endregion
        }

        #region Web-related
        /// <summary>
        /// Download file and returns a `temporary name` ("yyyyMMdd_HHmmssfff.tmp") if `FilePath` was not supplied.
        /// </summary>
        /// <param name="UseAppData">Functional Options: null, true, false</param>
        public static async Task<string> DownloadAsync(this string DownloadUrl, string? FilePath = null, bool? UseAppData = null)
        {
            string tempFileName = $"file_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.tmp";
            string appDataTempFilePath = Path.Combine(HelpersAppDataFolder.CreateAppDataFolder(), tempFileName);

            // If `UseAppData` == null: Use app data for operations but nothing else
            // If `UseAppData` == true: Use app data for operations and as destination
            // If `UseAppData` == false: Do not use app data. Download to tempFileName, move to FilePath if exists.
            string tempFilePath = UseAppData == null || (UseAppData != null && (bool)UseAppData) ? appDataTempFilePath : tempFileName;

            try
            {
                using HttpClient client = new HttpClient();
                using HttpResponseMessage response = await client.GetAsync(DownloadUrl);
                response.EnsureSuccessStatusCode();

                await using FileStream fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);
                fs.Close();

                if (FilePath != null)
                {
                    if (UseAppData != null && (bool)UseAppData)
                    {
                        string newAppDataFilePath = Path.Combine(HelpersAppDataFolder, Path.GetFileName(FilePath));
                        File.Move(tempFilePath, newAppDataFilePath);
                        return newAppDataFilePath;
                    }

                    else
                    {
                        // Return supplied `filepath`
                        File.Move(tempFilePath, FilePath, overwrite: true);
                        return FilePath;
                    }
                }

                else
                {
                    // Return *automatically-generated* `filepath`
                    return tempFilePath;
                }
            }

            catch
            {
                throw;
            }
        }

        public static async Task<string> DownloadTextAsync(this string Url)
        {
            if (Url.IsValidUrl() == false)
                throw new ArgumentException($"'{Url}' is not a valid URL.");

            try
            {
                using HttpClient client = new HttpClient();
                return await client.GetStringAsync(Url);
            }

            catch
            {
                throw;
            }
        }

        public static bool IsValidUrl(this string Url)
        {
            return Uri.TryCreate(Url, UriKind.Absolute, out Uri? uriResult)
                   && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }
        #endregion

        #region Json Shortcut
        private static readonly JsonSerializerOptions DefaultOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true, // ← This ignores casing when deserializing
            WriteIndented = false               // Optional: make this true for pretty output
        };

        public static T? JsonToObject<T>(this string jsonString)
        {
            return JsonSerializer.Deserialize<T>(jsonString, DefaultOptions);
        }

        public static string ObjectToJson(this object yourObject)
        {
            return JsonSerializer.Serialize(yourObject, DefaultOptions);
        }
        #endregion

        #region IO-related
        public static void AppendTextToFile(this string Text, string TextFilePath, bool NewlinePrefix = false, bool NewlineSuffix = true)
        {
            File.AppendAllText(TextFilePath, (NewlinePrefix ? Environment.NewLine : null) + Text + (NewlineSuffix ? Environment.NewLine : null));
        }

        /// <summary>
        /// Create "../AppDataFolder/" and returns its full path.
        /// </summary>
        public static string CreateAppDataFolder(this string FolderName)
        {
            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), new DirectoryInfo(FolderName.TrimEnd(Path.DirectorySeparatorChar)).Name);

            if (!Directory.Exists(appDataFolder))
            {
                Directory.CreateDirectory(appDataFolder);
                Console.WriteLine($"AppData folder created at: `{appDataFolder}`");
            }

            else
            {
                Console.WriteLine($"AppData folder already exists at: `{appDataFolder}`");
            }

            return appDataFolder;
        }

        /// <summary>
        /// Extracts a specific file from a ZIP archive.
        /// </summary>
        /// <param name="ZipArchivePath">The path to the ZIP archive.</param>
        /// <param name="TargetFileName">The name of the file inside the ZIP to extract (case-insensitive).</param>
        /// <param name="DestinationFilePath">
        /// Optional. The full path where the file should be extracted. 
        /// If null, the file is extracted to the same folder as the ZIP using the original file name.
        /// </param>
        /// <returns>Exctracted file if found in zip and extracted successfully; otherwise, null.</returns>
        public static async Task<string?> ExtractFileFromZip(this string ZipArchivePath, string TargetFileName, string? DestinationFilePath = null)
        {
            if (!File.Exists(ZipArchivePath))
                throw new FileNotFoundException("ZIP file not found.", ZipArchivePath);

            if (DestinationFilePath != null)
            {
                if (Path.GetDirectoryName(DestinationFilePath) == string.Empty)
                {
                    DestinationFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DestinationFilePath);
                }
            }

            using ZipArchive archive = ZipFile.OpenRead(ZipArchivePath);
            ZipArchiveEntry? entry = null;

            foreach (var e in archive.Entries)
            {
                if (string.Equals(e.Name, TargetFileName, StringComparison.OrdinalIgnoreCase))
                {
                    entry = e;
                    break;
                }
            }

            if (entry == null)
            {
                Console.WriteLine("File not found in ZIP");
                return null; // File not found in ZIP
            }

            string outputPath = DestinationFilePath ?? Path.Combine(
                Path.GetDirectoryName(ZipArchivePath)!,
                entry.Name
            );

            // Ensure the directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            await using Stream sourceStream = entry.Open();
            await using FileStream outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            await sourceStream.CopyToAsync(outputStream);

            return outputPath;
        }
        #endregion

        /// <summary>
        /// Converts a dot-separated version string (e.g., "1.2.3") to a <see cref="Version"/> object, 
        /// supporting 1 to 4 numeric components.
        /// </summary>
        /// <param name="Text">The version string to convert.</param>
        /// <returns>A <see cref="Version"/> object constructed from the string.</returns>
        /// <exception cref="ArgumentException">Thrown if the string contains more than 4 components.</exception>
        public static Version AsVersion(this string Text)
        {
            int[] versionInts = Text.Split('.')
                .Select(int.Parse)
            .ToArray();

            return versionInts.Length switch
            {
                1 => new Version(versionInts[0], 0),
                2 => new Version(versionInts[0], versionInts[1]),
                3 => new Version(versionInts[0], versionInts[1], versionInts[2]),
                4 => new Version(versionInts[0], versionInts[1], versionInts[2], versionInts[3]),
                _ => throw new ArgumentException("Array must have 1 to 4 elements.", nameof(versionInts))
            };
        }
    }
}
