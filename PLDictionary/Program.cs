using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Net;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Configuration;

namespace PLDictionary
{
   static class Program
   {
      /// <summary>
      /// The main entry point for the application.
      /// </summary>
      [STAThread]
      static void Main()
      {
         AllocConsole();
         Application.EnableVisualStyles();
         Application.SetCompatibleTextRenderingDefault(false);
         //Application.Run(new Form1());
      
         int threads = Environment.ProcessorCount;
         if (int.TryParse(ConfigurationManager.AppSettings["threads"], out threads))
            _processorCount = threads;

         ServicePointManager.DefaultConnectionLimit = _processorCount;
         ServicePointManager.Expect100Continue = false;

         Console.WriteLine("Start {0:G}", DateTime.Now);
         Console.WriteLine("Ilość procesorów = {0}, Ilość równoległych wątków={1}", Environment.ProcessorCount, _processorCount);
         _tempPath = Path.GetTempPath() + @"\sjp-" + Guid.NewGuid().ToString();
         Directory.CreateDirectory(_tempPath);

         //GetWord("a.h.", "a.h.");


         Stopwatch s1 = new Stopwatch();
         s1.Start();
         ProcessMulti();
         //ProcessSingle();
         s1.Stop();
         

         Console.WriteLine("Koniec - czas {0}", s1.Elapsed.TotalSeconds);
         Console.ReadKey();
      }

      private static void ProcessMulti()
      {
         //Console.WriteLine("Start pobierania stron");
         //for (int i = 0; i < _processorCount; i++)
         //{
         //   Thread thread = new Thread(GetWordPages);
         //   thread.IsBackground = false;
         //   thread.Name = string.Format("MyThread{0}", i);
         //   thread.Start();
         //}
         //Thread.Sleep(1000);
         //Console.WriteLine("Start pobierania definicji");
         //for (int i = 0; i < _processorCount; i++)
         //{
         //   Thread thread = new Thread(GetWords);
         //   thread.IsBackground = false;
         //   thread.Name = string.Format("MyThread{0}", i);
         //   thread.Start();
         //}
         _tasks = new Task[_processorCount];
         Console.WriteLine("Start pobierania stron");
         for (int i = 0; i < _processorCount; i++)
            _tasks[i] = Task.Factory.StartNew(() => { GetWordPages(); });
         Task.WaitAll(_tasks);

         _tasks = new Task[_processorCount];
         Console.WriteLine("Start pobierania definicji");
         for (int i = 0; i < _processorCount; i++)
            _tasks[i] = Task.Factory.StartNew(() => { GetWords(); });
         Task.WaitAll(_tasks);

         Console.WriteLine("Start zapisu definicji");
         SaveWords();
      }

      private static void ProcessSingle()
      {
         Console.WriteLine("Start pobierania stron");
         GetWordPages();
         Console.WriteLine("Start pobierania definicji");
         GetWords();
         Console.WriteLine("Start zapisu definicji");
         SaveWords();
      }

      private static void SaveWords()
      {
         using (StreamWriter sw = File.CreateText(Path.Combine(_tempPath, "defs.txt")))
         {
            foreach (var i in dic.OrderBy(x=>x.Key))
               sw.WriteLine(string.Format("{0}\t{1}", i.Key, i.Value));
         }
      }

      static void GetWordPages()
      {
         bool stop = false;
         int i = 0;

         while (!stop)
         {
            lock (_lockObject)
            {
               _pageCounter++;
               i = _pageCounter;
               //if (_pageCounter > 2800)
               //   return;
            }

            Console.WriteLine("[{1, -2}] Wczytuje strone {0}", i, string.IsNullOrEmpty(Thread.CurrentThread.Name) ? Task.CurrentId.ToString() : Thread.CurrentThread.Name);
            string url = string.Format(@"{0}/slownik/lp.phtml?f_mn=2&page={1}", _sjpUrl, i);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Proxy = null;
            request.Method = "GET";
            using (WebResponse response = request.GetResponse())
            {
               using (StreamReader sr = new StreamReader(response.GetResponseStream()))
               {
                  string content = sr.ReadToEnd();
                  if (content.Contains("<tr><td><a href="))
                  {
                     string file = Path.Combine(_tempPath, string.Format("page{0:D4}.html", i));
                     File.WriteAllText(file, content, Encoding.UTF8);
                  }
                  else stop = true;
               }
            }

         }
      }

      static void GetWords()
      {
         bool stop = false;
         int i = 0;
         Regex pattern = new Regex(@"<tr><td><a href=""/(?<url>[\w\d\s+-.%]+)"">(?<name>[\w\d\s+-.&]+)</a></td>");

         while (!stop)
         {
            lock (_lockObject)
            {
               _wordCounter++;
               i = _wordCounter;
               //if (_wordCounter > 10)
               //   return;
            }
            string file = Path.Combine(_tempPath, string.Format("page{0:D4}.html", i));
            if (File.Exists(file))
            {
               string page = File.ReadAllText(file);
               //<tr><td><a href="/a+priori">a priori</a></td>
               MatchCollection matches = pattern.Matches(page);
               foreach (Match match in matches)
               {
                  string url = match.Groups["url"].Value;
                  string name = match.Groups["name"].Value;
                  GetWord(url, name);
               }
               File.Delete(file);
            }
            else stop = true;
         }
      }

      public static void GetWord(string url, string name)
      {
         Console.WriteLine("[{1, -2}] Pobieram definicje {0}", name, string.IsNullOrEmpty(Thread.CurrentThread.Name) ? Task.CurrentId.ToString() : Thread.CurrentThread.Name);
         string uri = string.Format(@"{0}/{1}", _sjpUrl, url);
         HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
         request.Proxy = null;
         request.Method = "GET";
         using (WebResponse response = request.GetResponse())
         {
            using (StreamReader sr = new StreamReader(response.GetResponseStream()))
            {
               string content = sr.ReadToEnd();
               //<p style="margin-top: .5em; font-size: medium; max-width: 32em; ">(wł. w tempie) termin muzyczny, notacja nakazująca powrót do pierwotnego tempa, które uległo chwilowej zmianie</p>
               //Regex pattern = new Regex(@"<p style=""margin-top: .5em; font-size: medium; max-width: 32em; "">(?<def>[\w\d\s\+-.:;,&\(\)/<>\[\]]+)</p>");
               //Match match = pattern.Match(content);
               //dic.AddOrUpdate(name, match.Groups["def"].Value, (x, y) => match.Groups["def"].Value);
               int i1 = content.IndexOf(_startString);
               if (i1 < 0)
                  return;
               int i2 = content.IndexOf(_endString, i1);
               if (i2 < 0)
                  return;
               string def = content.Substring(i1 + _startString.Length, i2 - i1 - _startString.Length);
               dic.AddOrUpdate(name, def, (x, y) => def);

            }
         }
      }

      [DllImport("kernel32.dll", SetLastError = true)]
      [return: MarshalAs(UnmanagedType.Bool)]
      static extern bool AllocConsole();

      private static object _lockObject = new object();
      private static int _pageCounter = 0;
      private static int _wordCounter = 0;
      private static string _tempPath = string.Empty;
      private static string _sjpUrl = "http://sjp.pl";
      private static int _processorCount = Environment.ProcessorCount;
      private static Task[] _tasks = new Task[_processorCount];
      private static string _startString = @"<p style=""margin-top: .5em; font-size: medium; max-width: 32em; "">";
      private static string _endString = @"</p>";
      private static ConcurrentDictionary<string, string> dic = new ConcurrentDictionary<string, string>();
   }
}
