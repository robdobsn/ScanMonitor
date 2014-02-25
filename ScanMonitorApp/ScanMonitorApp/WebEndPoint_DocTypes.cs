using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    class WebEndPoint_DocTypes : WebEndPoint
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private DocTypesMatcher _docTypesMatcher;

        public WebEndPoint_DocTypes(DocTypesMatcher docTypesMatcher)
        {
            _docTypesMatcher = docTypesMatcher;
            Action = DocTypes;
            Name = "doctypes";
            Description = "doctypes actions";
        }

        private string DocTypes(List<string> webCmdParams)
        {
            if (webCmdParams[0] == "list")
            {
                if (webCmdParams.Count > 0)
                    return _docTypesMatcher.ListDocTypesJson();
            }
            else if (webCmdParams[0] == "get")
            {
                if (webCmdParams.Count > 1)
                    return _docTypesMatcher.GetDocTypeJson(Uri.UnescapeDataString(webCmdParams[1]));
            }
            return "Incorrect parameter to doctypes";
        }
    }
}
