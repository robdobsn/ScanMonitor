using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    public class ScanDocAllInfo
    {
        public ScanDocAllInfo(ScanDocInfo sdi, List<ScanPageText> spt)
        {
            scanDocInfo = sdi;
            scanPages = spt;
        }
        public ObjectId Id;
        public ScanDocInfo scanDocInfo { get; set; }
        public List<ScanPageText> scanPages { get; set; }
    }

    public class ScanDocInfo
    {
        public ScanDocInfo(string a_name, int a_numPages, int a_numPagesWithText, string a_docTypeFiled, 
                        DateTime a_docDateFiled, DateTime a_createDate, string a_docDescr, string scanPgBase)
        {
            docName = a_name;
            numPages = a_numPages;
            numPagesWithText = a_numPagesWithText;
            docTypeFiled = a_docTypeFiled;
            docDateFiled = a_docDateFiled;
            createDate = a_createDate;
            docDescr = a_docDescr;
            scanPageImageFileBase = scanPgBase;
            docTypeMatchResult = null;
        }
        public string docName { get; set; }
        public int numPages { get; set; }
        public int numPagesWithText { get; set; }
        public string docTypeFiled { get; set; }
        public DateTime docDateFiled { get; set; }
        public DateTime createDate { get; set; }
        public string docDescr { get; set; }
        public string scanPageImageFileBase { get; set; }
        public DocTypeMatchResult docTypeMatchResult { get; set; }
    }

    public class ScanTextElem
    {
        public ScanTextElem(string a_text, DocRectangle a_bounds)
        {
            text = a_text;
            bounds = a_bounds;
        }
        public string text { get; set; }
        public DocRectangle bounds { get; set; }
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
