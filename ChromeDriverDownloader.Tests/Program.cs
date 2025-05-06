namespace ChromeDriverDownloader.Tests
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Testing Download");

            await ChromeDriverDownloader.Download();

            Console.WriteLine("Testing Complete. Press any key to exit.");
            Console.ReadKey();
            Environment.Exit(0);
        }
    }
}
