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
        public ScanDocAllInfo(ScanDocInfo sdi, ScanPages spages)
        {
            scanDocInfo = sdi;
            scanPages = spages;
        }
        public ObjectId Id;
        public ScanDocInfo scanDocInfo { get; set; }
        public ScanPages scanPages { get; set; }
    }

    public class ScanDocInfo
    {
        public ScanDocInfo(string a_uniqname, int a_numPages, int a_numPagesWithText, string a_docTypeFiled, 
                        DateTime a_docDateFiled, DateTime a_createDate, string a_docDescr, string scanPgBase)
        {
            uniqName = a_uniqname;
            numPages = a_numPages;
            numPagesWithText = a_numPagesWithText;
            docTypeFiled = a_docTypeFiled;
            docDateFiled = a_docDateFiled;
            createDate = a_createDate;
            docDescr = a_docDescr;
            scanPageImageFileBase = scanPgBase;
            docTypeMatchResult = null;
        }
        public ObjectId Id;
        public string uniqName { get; set; }
        public int numPages { get; set; }
        public int numPagesWithText { get; set; }
        public string docTypeFiled { get; set; }
        public DateTime docDateFiled { get; set; }
        public DateTime createDate { get; set; }
        public string docDescr { get; set; }
        public string scanPageImageFileBase { get; set; }
        public DocTypeMatchResult docTypeMatchResult { get; set; }

        public static string GetUniqNameForFile(string fileName)
        {
            return System.IO.Path.GetFileNameWithoutExtension(fileName);
        }
    }

    public class ScanPages
    {
        public ObjectId Id;
        public string uniqName { get; set; }
        public List<List<ScanTextElem>> scanPagesText { get; set; }

        public ScanPages(string a_uniqName, List<List<ScanTextElem>> spagesText)
        {
            uniqName = a_uniqName;
            scanPagesText = spagesText;
        }
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
}
