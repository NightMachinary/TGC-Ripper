using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DocoptNet;

namespace TGCPripper
{
    class Program
    {
        static string homePath = ((Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX) ? Environment.GetEnvironmentVariable("HOME") : Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%")) + Path.DirectorySeparatorChar;
        static string cookiesPath = homePath + ".tgc-ripper" + Path.DirectorySeparatorChar + "cookies.txt";
        static string listPath = homePath + ".tgc-ripper" + Path.DirectorySeparatorChar + "list.txt";

        private const string usage = @"The Great Courses Plus Ripper, aka tgc-ripper.
    It is not guaranteed that this tool would work on Windows.
    You should put cookies.txt and list.txt in ~/.tgc-ripper. Put the links to the course page on each line of list.txt.

    Usage:
      tgc-ripper [--ls] [--out-dir=<out>]
      tgc-ripper (-h | --help)
      tgc-ripper --version

    Options:
      -h --help          Show this screen.
      --version          Show version.
      -o <out> --out-dir=<out>    Output directory [default: dls].
      -l --ls            The program won't download; The download links of the requested lectures will be written to lecture-links.txt.

    https://github.com/NightMachinary/TGC-Ripper. Forked from https://github.com/alfablac/TGC-Ripper.
    ";

        private static double GetFileSize(string uriPath)
        {
            var webRequest = WebRequest.Create(uriPath);
            webRequest.Method = "HEAD";

            using (var webResponse = webRequest.GetResponse())
            {
                double fileSize = Convert.ToDouble(webResponse.Headers.Get("Content-Length"));
                double fileSizeInMegaByte = Math.Round(Convert.ToDouble(fileSize) / 1024 / 1024, 2);
                return fileSizeInMegaByte;
            }
        }

        static void Main(string[] args)
        {
            new Program().Ripper(args);
        }

        private void Ripper(string[] args)
        {
            var arguments = new Docopt().Apply(usage, args, version: "tgc-ripper 0.1", exit: true);
            //Console.WriteLine("Assembly location: " + Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            //Console.WriteLine("PWD: " + System.IO.Directory.GetCurrentDirectory());
            //Console.WriteLine("Args: " + string.Join(",", args));

            StringBuilder path = new StringBuilder();
            StringBuilder cookie_file = new StringBuilder();
            StringBuilder output_file = new StringBuilder();

            if (File.Exists(cookiesPath))
            {
                Console.WriteLine("Cookies found!");
            }
            else
            {
                Console.WriteLine("Import your cookies.txt file and place it in " + cookiesPath);

                return;
            }

            if (File.Exists(listPath))
            {
                Console.WriteLine("List found!");
            }
            else
            {
                Console.WriteLine("Put links to desired courses in " + listPath);

                return;
            }

            try
            {
                using (StreamReader sr = new StreamReader(cookiesPath))
                {
                    String line;
                    int contador = 0;
                    while ((line = sr.ReadLine()) != null)
                    {
                        contador++;
                        if (contador > 7)
                        {
                            string[] split = line.Split('\t');
                            cookie_file.Append(split[5]).Append("=").Append(split[6]).Append("; ");
                        }
                    }
                }
            }
            catch (Exception e)
            {

                Console.WriteLine("The file could not be read:");
                Console.WriteLine(e.Message);

            }

            ChromeOptions options = new ChromeOptions();
            options.AddArguments("--headless", "--silent", "--log-level=3", "--allow-file-access-from-files", "--disable-gpu");
            var service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;
            IWebDriver driver = new ChromeDriver(service, options);

            try
            {
                using (StreamReader sr = new StreamReader(listPath))
                {
                    String line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        string path_to_courseURL = line;

                        Console.WriteLine("\n\n Scrapping page " + path_to_courseURL + "\n\n");
                        driver.Navigate().GoToUrl(path_to_courseURL);
                        IList<IWebElement> elements = driver.FindElements(By.TagName("a"));
                        string course_name = driver.FindElement(By.XPath("//*[@class='course-info']/h1")).Text;
                        int counter = 0, pos_start = 1;
                        foreach (IWebElement element in elements)
                        {
                            if (element.GetAttribute("data-film-id") != null)
                            {
                                WebClient webClient = new WebClient();
                                webClient.Headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/63.0.3239.132 Safari/537.36";
                                webClient.Headers["cookie"] = cookie_file.ToString();
                                webClient.Headers["dnt"] = "1";
                                string video_url = webClient.DownloadString("https://www.thegreatcoursesplus.com/embed/player?filmId=" + element.GetAttribute("data-film-id"));
                                var file_regex = new Regex(@"\b(?:https?://vt)\S+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                                List<string> links_to_video = new List<string>();
                                foreach (Match m in file_regex.Matches(video_url))
                                    links_to_video.Add(m.Value);
                                String download_video_url = "";
                                double maior = 0;
                                foreach (string item in links_to_video)
                                {
                                    if (GetFileSize(item) > maior)
                                    {
                                        maior = GetFileSize(item);
                                        download_video_url = item;
                                    }
                                }

                                string title = element.GetAttribute("data-title");
                                title = title.Replace(":", "").Replace("?", "");
                                course_name = course_name.Replace(":", "");
                                course_name = course_name.Replace("?", "");
                                Console.WriteLine("\n\n-----Lecture " + pos_start.ToString() + " of Course " + course_name);
                                if ((bool)(arguments["--ls"].Value)) //(args.Length >= 1 && args[0] == "ls")
                                {
                                    string currentLecture = course_name + Path.DirectorySeparatorChar + pos_start.ToString() + " - " + title + "\n" + download_video_url;
                                    string lsArgs = "-c \"echo '" + currentLecture.Replace("'", "'\\\"'\\\"'") + "' >> '" + arguments["--out-dir"].Value + "/lecture-links.txt'\""; // This line might not work in Windows.
                                    ProcessStartInfo bash_runner = new ProcessStartInfo("bash", lsArgs);
                                    bash_runner.UseShellExecute = false;
                                    var proc = Process.Start(bash_runner);
                                    proc.WaitForExit();
                                }
                                else
                                {
                                    //Their server sucks, so we do need to use multiconnections for the download.
                                    string eargs = "-s16 -j16 -x16 --file-allocation=none --console-log-level=error --dir \"" + arguments["--out-dir"].Value + "\" -o \"" + course_name + Path.DirectorySeparatorChar + pos_start.ToString() + " - " + title + ".mp4\" " + download_video_url;
                                    Console.WriteLine(eargs);
                                    ProcessStartInfo start_aria = new ProcessStartInfo(@"aria2c", eargs);
                                    start_aria.UseShellExecute = false;
                                    var proc = Process.Start(start_aria);
                                    proc.WaitForExit();
                                }

                                pos_start++;

                            }
                            counter++;
                        }
                        driver.Close();
                        driver.Quit();

                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);

            }

        }
    }
}
