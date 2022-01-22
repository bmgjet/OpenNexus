using System;
using System.IO;
using System.Text;
using System.Net;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace VoiceChatHandler
{
    class Program
    {
        public static HttpListener listener;
        public static DateTime StartTime = DateTime.Now;
        public static TimeSpan t;
        public static List<string> Servers = new List<string>();
        public static async Task HandleIncomingConnections()
        {
            bool runServer = true;
            List<Dictionary<string, string>> packets = new List<Dictionary<string, string>>();
            string Key = "bmgjet123";
            while (runServer)
            {
                HttpListenerContext ctx = await listener.GetContextAsync();
                HttpListenerRequest req = ctx.Request;
                HttpListenerResponse resp = ctx.Response;
                if (req.HttpMethod == "POST" && Key == req.Headers.Get("User-Agent"))
                {
                    string data = "";
                    string Server = req.Headers.Get("Data-For");
                    if(!Servers.Contains(Server))
                    {
                        Servers.Add(Server);
                    }
                    var dict = packets.FirstOrDefault(d => d.ContainsKey(req.Headers.Get("Data-Form")));
                    if (dict != null)
                    {
                        dict.TryGetValue(req.Headers.Get("Data-Form"), out data);
                        foreach (var x in packets) x.Remove(req.Headers.Get("Data-Form"));
                    }

                    string testdata;
                    using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                    {
                        testdata = reader.ReadToEnd();
                    }
                    byte[] databytes;
                    if (testdata != null && testdata == "<OpenNexus>Ping")
                    {
                        if (Servers.Count < 2)
                        {
                            data = "WAIT";
                            StartTime = DateTime.Now.AddSeconds(60);
                        }
                        else
                        {
                            data = t.TotalSeconds.ToString();
                        }
                    }
                    else if (testdata != null && testdata != "")
                    {
                        Dictionary<string, string> thispacket = new Dictionary<string, string> { { req.Headers.Get("Data-For"), testdata } };
                        packets.Add(thispacket);
                    }
                    databytes = Encoding.UTF8.GetBytes(data);
                    resp.ContentType = "text/html";
                    resp.ContentEncoding = Encoding.UTF8;
                    resp.ContentLength64 = databytes.LongLength;
                    await resp.OutputStream.WriteAsync(databytes, 0, data.Length);
                    resp.Close();
                }
                else
                {
                    byte[] data = Encoding.UTF8.GetBytes("OpenNexus");
                    resp.ContentType = "text/html";
                    resp.ContentEncoding = Encoding.UTF8;
                    resp.ContentLength64 = data.LongLength;
                    await resp.OutputStream.WriteAsync(data, 0, data.Length);
                    resp.Close();
                }
            }
        }

        public static void syncclock()
        {
            while (true)
            {
                 t = StartTime - DateTime.Now;
                if (t.TotalSeconds<= 0)
                {
                    StartTime = DateTime.Now.AddSeconds(60);
                }
                Thread.Sleep(100);
            }
        }

        public static void Main(string[] args)
        {
            Thread thr = new Thread(new ThreadStart(syncclock));
            thr.Start();

            listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:9000/");
            listener.Start();
            Console.WriteLine("Started");
            Task listenTask = HandleIncomingConnections();
            listenTask.GetAwaiter().GetResult();
            listener.Close();
        }
    }
}
