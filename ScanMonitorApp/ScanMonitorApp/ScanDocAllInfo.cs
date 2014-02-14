using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    class ScanDocAllInfo
    {
        public ScanDocAllInfo(string a_name, int a_numPages, string a_docType, 
                        DateTime a_createDate, DateTime a_docRefDate, string a_docDescr,
                        List<ScanPageText> scanpg, string scanPgBase)
        {
            docName = a_name;
            numPages = a_numPages;
            docType = a_docType;
            createDate = a_createDate;
            docRefDate = a_docRefDate;
            docDescr = a_docDescr;
            scanPages = scanpg;
            scanPageImageFileBase = scanPgBase;
        }
        public string docName { get; set; }
        public int numPages { get; set; }
        public string docType { get; set; }
        public DateTime createDate { get; set; }
        public DateTime docRefDate { get; set; }
        public string docDescr { get; set; }
        public List<ScanPageText> scanPages { get; set; }
        public string scanPageImageFileBase { get; set; }

        public class ScanTextElem
        {
            public ScanTextElem(string a_bounds, string a_text)
            {
                text = a_text;
                bounds = a_bounds;
            }
            public string text { get; set; }
            public string bounds { get; set; }
        }

        public class ScanPageText
        {
            public ScanPageText(List<ScanTextElem> ste)
            {
                textElems = ste;
            }
            public List<ScanTextElem> textElems { get; set; }
        }
    }
}
