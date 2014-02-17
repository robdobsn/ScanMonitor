using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace ScanMonitorApp
{
    class WebEndPoint_ScanDocs : WebEndPoint
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private ScanDocHandler _scanDocHandler;

        public WebEndPoint_ScanDocs(ScanDocHandler scanDocHandler)
        {
            _scanDocHandler = scanDocHandler;
            Action = Echo;
            Name = "echo";
            Description = "echo to logger";
        }

        private string Echo(List<string> items)
        {
            String text = "";
            if (items != null && items.Count > 0)
            {
                foreach (var item in items)
                {
                    text += item + " ";
                }
            }
            else
            {
                text = "No arguments!";
            }

            logger.Info("Write {0}", text);

            return "OK. Wrote out: " + (text.Length == 0 ? "n/a" : text);
        }

    }
}
