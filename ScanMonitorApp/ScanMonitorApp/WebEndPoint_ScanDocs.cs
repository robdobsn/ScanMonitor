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
            Action = ScanDocs;
            Name = "scandocs";
            Description = "scandocs actions";
        }

        private string ScanDocs(List<string> webCmdParams)
        {
            if (webCmdParams[0] == "list")
            {
                if (webCmdParams.Count > 0)
                    return _scanDocHandler.ListScanDocs();
            }
            else if (webCmdParams[0] == "get")
            {
                if (webCmdParams.Count > 1)
                    return _scanDocHandler.GetScanDoc(webCmdParams[1]);
            }
            return "Incorrect parameter to scandocs";
        }


    }
}
