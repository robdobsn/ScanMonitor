using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NLog;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ScanMonitorApp
{
    public class WebServer
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private readonly HttpListener _listener = new HttpListener();
        private readonly Func<HttpListenerRequest, string> _responderMethod;
        private List<WebEndPoint> _allowedEndPoints = new List<WebEndPoint> ();
        private string _urlBase;

        public WebServer(string urlBase)
        {
            _urlBase = urlBase;
        }

        public void RegisterEndPoint(WebEndPoint endPoint)
        {
            _allowedEndPoints.Add(endPoint);
            _listener.Prefixes.Add(_urlBase + "/" + endPoint.Name + "/");
        }

        public void Run()
        {
            _listener.Start();
            ThreadPool.QueueUserWorkItem((o) =>
            {
                logger.Info("Webserver running");
                try
                {
                    while (_listener.IsListening)
                    {
                        ThreadPool.QueueUserWorkItem((c) =>
                        {
                            var ctx = c as HttpListenerContext;
                            try
                            {
                                string returnStr = "Unknown Command";
                                string command = null;
                                string[] urlParts = ctx.Request.RawUrl.Split('/');
                                if (urlParts.Length >= 2)
                                {
                                    command = urlParts[1].ToLower();
                                    // Find the EndPoint associated with this command
                                    WebEndPoint returnEndPoint = null;
                                    foreach (WebEndPoint endPoint in _allowedEndPoints)
                                    {
                                        if (command != null && endPoint.Name.ToLower() == command)
                                        {
                                            returnEndPoint = endPoint;
                                            break;
                                        }
                                    }
                                    if (returnEndPoint != null)
                                    {
                                        List<string> args = new List<string>();
                                        for (int i = 0; i < urlParts.Length - 2; i++)
                                            args.Add(urlParts[i + 2]);
                                        returnEndPoint.Arguments = args;
                                        returnStr = returnEndPoint.Execute();
                                    }
                                }
                                byte[] buf = Encoding.UTF8.GetBytes(returnStr);
                                ctx.Response.ContentLength64 = buf.Length;
                                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                            }
                            catch { } // suppress any exceptions
                            finally
                            {
                                // always close the stream
                                ctx.Response.OutputStream.Close();
                            }
                        }, _listener.GetContext());
                    }
                }
                catch { } // suppress any exceptions
            });
        }

        public void Stop()
        {
            _listener.Stop();
            _listener.Close();
        }

    }
}

