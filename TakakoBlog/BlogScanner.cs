using Codaxy.WkHtmlToPdf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Diagnostics;

namespace TakakoBlog
{
    internal class BlogScanner
    {
        Uri baseUri;

        Uri blogUri;

        string blogUrl;

        string previousDownloadsPath = "previous_downloads.txt";

        List<String> previousDownloads;

        private string ArchiveUrl => this.blogUrl + "arcv/";
        private string EntryUrl => this.blogUrl + "e/";

        public string PdfOutputPath { get; set; }

        public string HtmlOutputPath { get; set; }

        public BlogScanner(Uri baseUri, Uri blogUri)
        {
            this.baseUri = baseUri;
            this.blogUri = blogUri;
            this.blogUrl = blogUri.AbsoluteUri;
            if (!this.blogUrl.EndsWith("/"))
            {
                this.blogUrl += "/";
            }
            

            this.previousDownloads = GetPreviousDownloads();

            this.PdfOutputPath = Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "output");
            this.HtmlOutputPath = Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "webpages");
        }

        private List<String> GetPreviousDownloads()
        {
            var result = new List<string>();

            if (!File.Exists(this.previousDownloadsPath))
            {
                Console.WriteLine("Previous downloads file does not exist. Creating.");
                var file = File.CreateText(this.previousDownloadsPath);
                file.Close();
                return result;
            }

            return new List<string>(File.ReadAllLines(this.previousDownloadsPath));
        }

        public void AddToPreviousDownloads(string processedUrl)
        {
            this.previousDownloads.Add(processedUrl);
            File.AppendAllLines(this.previousDownloadsPath, new List<string>() { processedUrl }, Encoding.UTF8);
        }

        public List<string> ScanForUpdates(bool scanToLastPage = false, int debugMaxPages = 0)
        {
            List<string> result = new List<string>();

            bool finished = false;
            var archiveUrl = this.ArchiveUrl;

            using (var client = new WebClient())
            {
                var pageNo = 1;
                do
                {
                    if (debugMaxPages > 0 && pageNo > debugMaxPages)
                    {
                        break;
                    }

                    // Get contents
                    Console.WriteLine($"Scanning archive page {pageNo}...");
                    client.Encoding = Encoding.UTF8;
                    var contents = client.DownloadString(archiveUrl);

                    /*
                        <li><span class="mod-arcv-tit"><a href="http://blog.goo.ne.jp/guldfisk/e/e25fba10ef6bf9466a8d869365ce0b0c">お疲れ様パーティー</a></span><br>(2016-10-29&nbsp;|&nbsp;<a href="http://blog.goo.ne.jp/guldfisk/arcv/?c=0b4be9a5db83d526c03deba5a2c9ad29&amp;st=0">イベント</a>)<br>
                        <a href="http://blog.goo.ne.jp/guldfisk/e/e25fba10ef6bf9466a8d869365ce0b0c">10/29(土) サッカーのシーズンオフお疲...</a></li>
                    */
                    var titleMatches = Regex.Matches(contents, "<li><span class=\"mod-arcv-tit\">(.*)</span>.*\\(([-0-9]+)&nbsp;");

                    // Finished?
                    if (titleMatches.Count == 0)
                    {
                        break;
                    }

                    foreach (Match titleMatch in titleMatches)
                    {
                        var titleContent = titleMatch.Groups[1].Value;
                        var titleContentMatch = Regex.Match(titleContent, "<a href=\"(.*)\">(.*)</a>");
                        if (!titleContentMatch.Success)
                        {
                            Console.WriteLine($"ERROR parsing line {titleContent}");
                            continue;
                        }
                        var entryDate = titleMatch.Groups[2].Value;
                        var url = titleContentMatch.Groups[1].Value;
                        var title = titleContentMatch.Groups[2].Value;

                        if (!url.EndsWith("/"))
                        {
                            url += "/";
                        }

                        if (!this.previousDownloads.Any(pd => pd.StartsWith(url)))
                        {
                            result.Add($"{url} {entryDate} {title}");
                        }
                        else
                        {
                            if (!scanToLastPage)
                            {
                                finished = true;
                            }
                        }
                    }

                    if (finished)
                    {
                        break;
                    }

                    // Next page (with older posts)
                    // http://blog.goo.ne.jp/guldfisk/arcv/?page=2&c=&st=0
                    pageNo++;
                    archiveUrl = this.ArchiveUrl + $"?page={pageNo}";
                }
                while (true);

                return result;
            }
        }

        public void DownloadEntry(string url, string title, string entryDate)
        {
            if (!Directory.Exists(this.PdfOutputPath))
            {
                Directory.CreateDirectory(this.PdfOutputPath);
            }
            var name = entryDate.Replace("-", "");
            var outputFilePath = Path.Combine(this.PdfOutputPath, $"{name}.pdf");
            var index = 1;
            while (File.Exists(outputFilePath))
            {
                index++;
                outputFilePath = Path.Combine(this.PdfOutputPath, $"{name}-{index}.pdf");
            }

            var doc = new PdfDocument
            {
                Url = url
            };

            PdfConvert.ConvertHtmlToPdf(doc, new PdfOutput
            {
                OutputFilePath = outputFilePath
            });
        }

        public void DownloadHtml(string url, string title, string entryDate)
        {
            if (!Directory.Exists(this.HtmlOutputPath))
            {
                Directory.CreateDirectory(this.HtmlOutputPath);
            }

            var name = entryDate.Replace("-", "");
            var outputPath = Path.Combine(this.HtmlOutputPath, name);
            var index = 1;
            while (Directory.Exists(outputPath))
            {
                index++;
                outputPath = Path.Combine(this.HtmlOutputPath, $"{name}-{index}");
            }
            Directory.CreateDirectory(outputPath);

            using (var client = new WebClient())
            {
                var htmlRaw = client.DownloadData(url);
                var html = Encoding.UTF8.GetString(htmlRaw);

                html = ReplaceRelativeUrls(html);
                var fileName = Path.Combine(outputPath, "index.html");
                var fileNameIndex = 1;
                while (File.Exists(fileName))
                {
                    fileNameIndex++;
                    fileName = Path.Combine(outputPath, $"index({fileNameIndex}).html");
                }

                var imgMatches = Regex.Matches(html, "<img.*src=\"([^\"]*)\"");

                foreach (Match imgMatch in imgMatches)
                {
                    var imgUrl = imgMatch.Groups[1].Value;
                    if (imgUrl.Contains(".jpg"))
                    {
                        var imgUri = new Uri(imgUrl);
                        string imgFileName = imgUri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
                        var imgFileNamePos = imgFileName.LastIndexOf("/");
                        imgFileName = imgFileName.Substring(imgFileNamePos + 1);
                        var imgFilePath = Path.Combine(outputPath, imgFileName);

                        if (!File.Exists(imgFilePath))
                        {
                            client.DownloadFile(imgUri, imgFilePath);
                        }

                        html.Replace(imgUrl, imgFileName);
                    }
                }

                File.WriteAllText(fileName, html, Encoding.UTF8);
            }
        }

        public void DownloadEntry2(string url, string title, string entryDate)
        {
            var fileName = Path.GetFileName(url);

            if (!Directory.Exists(this.PdfOutputPath))
            {
                Directory.CreateDirectory(this.PdfOutputPath);
            }

            var outputFilePath = Path.Combine(this.PdfOutputPath, $"{entryDate.Replace("-", "")}.pdf");

            using (var client = new WebClient())
            {
                var html = client.DownloadString(url);
                html = ReplaceRelativeUrls(html);
                //html = html.Replace("<head>", "<head><meta charset=\"UTF-8\" />");

                var programPath = GetWkhtmlToPdfExeLocation();
                var command = $"--page-size A4 --margin-top 25 --margin-bottom 25 --encoding 'utf-8' \"-\" \"{outputFilePath}\""; 
                ProcessStartInfo cmdsi = new ProcessStartInfo(programPath); 
                cmdsi.Arguments = command;
                cmdsi.RedirectStandardInput = true;
                cmdsi.UseShellExecute = false;
                Process cmd = Process.Start(cmdsi);

                using (var stream = cmd.StandardInput)
                {
                    byte[] buffer = Encoding.UTF8.GetBytes(html);
                    stream.BaseStream.Write(buffer, 0, buffer.Length);
                    stream.WriteLine();
                }

                cmd.WaitForExit();
            }
        }

        private static string GetWkhtmlToPdfExeLocation()
        {
            string programFilesPath = System.Environment.GetEnvironmentVariable("ProgramFiles");
            string filePath = Path.Combine(programFilesPath, @"wkhtmltopdf\wkhtmltopdf.exe");

            if (File.Exists(filePath))
                return filePath;

            string programFilesx86Path = System.Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            filePath = Path.Combine(programFilesx86Path, @"wkhtmltopdf\wkhtmltopdf.exe");

            if (File.Exists(filePath))
                return filePath;

            filePath = Path.Combine(programFilesPath, @"wkhtmltopdf\bin\wkhtmltopdf.exe");
            if (File.Exists(filePath))
                return filePath;

            return Path.Combine(programFilesx86Path, @"wkhtmltopdf\bin\wkhtmltopdf.exe");
        }

        private string ReplaceRelativeUrls(string originalHtml)
        {
            var pattern = @"(?<name>src|href)=""(?<value>/[^""]*)""";
            var matchEvaluator = new MatchEvaluator(
                match =>
                {
                    var value = match.Groups["value"].Value;
                    Uri uri;

                    if (Uri.TryCreate(this.baseUri, value, out uri))
                    {
                        var name = match.Groups["name"].Value;
                        return string.Format("{0}=\"{1}\"", name, uri.AbsoluteUri);
                    }

                    return null;
                });
            return Regex.Replace(originalHtml, pattern, matchEvaluator);
        }
    }
}

