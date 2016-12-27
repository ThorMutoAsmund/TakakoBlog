using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TakakoBlog
{
    class Program
    {
        static void Main(string[] args)
        {
            // Setup
            var baseUrl = "http://blog.goo.ne.jp/";
            var siteUrl = "http://blog.goo.ne.jp/guldfisk";

            BlogScanner scanner = new BlogScanner(new Uri(baseUrl), new Uri(siteUrl));

            // Scan for updates
            var updates = scanner.ScanForUpdates(scanToLastPage: true);

            if (updates.Count() == 0)
            {
                Console.WriteLine("No new updates");
                Console.ReadKey();
                return;
            }

            Console.WriteLine($"Found {updates.Count()} new entries. Download? (Y/N)");

            var key = Console.ReadLine().Trim().ToUpperInvariant();
            if (key != "Y")
            {
                return;
            }

            foreach (var entryInfo in updates)
            {
                string url = entryInfo;

                var split = entryInfo.Split(new String[] { " " }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length != 3)
                {
                    Console.WriteLine($"ERROR in splitting internal entry info '{entryInfo}'");
                    Console.ReadKey();
                    return;
                }
                url = split[0];
                var entryDate = split[1];
                var title = split[2];

                try
                {
                    scanner.DownloadHtml(url, title, entryDate);
                    Console.WriteLine($"SUCCESS downloading HTML {entryDate}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR downloading HTML page with url {url} {ex.Message}");
                }

                try
                {
                    scanner.DownloadEntry(url, title, entryDate);
                    scanner.AddToPreviousDownloads(entryInfo);
                    Console.WriteLine($"SUCCESS downloading PDF {entryDate}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR downloading/creating PDF with url {url} {ex.Message}");
                }
            }

            // Save info about updated URLs
            

            Console.WriteLine("Finished!");

            Console.ReadKey();
        }
    }
}
