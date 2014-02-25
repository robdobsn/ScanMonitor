using MongoDB.Bson;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    public class ScanDocAllInfo
    {
        public ScanDocAllInfo(ScanDocInfo sdi, ScanPages spages, FiledDocInfo fdi)
        {
            scanDocInfo = sdi;
            scanPages = spages;
            filedDocInfo = fdi;
        }
        public ObjectId Id;
        public ScanDocInfo scanDocInfo { get; set; }
        public ScanPages scanPages { get; set; }
        public FiledDocInfo filedDocInfo { get; set; }
    }

    public class ScanDocInfo
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public ScanDocInfo(string a_uniqname, int a_numPages, int a_numPagesWithText, DateTime a_createDate)
        {
            uniqName = a_uniqname;
            numPages = a_numPages;
            numPagesWithText = a_numPagesWithText;
            createDate = a_createDate;
            docTypeMatchResult = null;
        }
        public ObjectId Id;
        public string uniqName { get; set; }
        public int numPages { get; set; }
        public int numPagesWithText { get; set; }
        public DateTime createDate { get; set; }
        public DocTypeMatchResult docTypeMatchResult { get; set; }

        public static string GetUniqNameForFile(string fileName, DateTime dt)
        {
            // If not prepend date and time
            string dtStr = dt.ToString("yyyy_MM_dd_hh_mm_ss");
            return GetUniqNameForFile(fileName, dtStr);
        }

        public static string GetUniqNameForFile(string fileName, string dateTimeStr)
        {
            // Check if file name already starts with date and time (14 digits)
            string uniqName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            int digsFound = 0;
            foreach (char ch in uniqName)
            {
                if (Char.IsDigit(ch))
                    digsFound++;
                else if (ch == '_')
                    continue;
                else
                    break;
                if (digsFound >= 14)
                    break;
            }
            if (digsFound >= 14)
                return uniqName;
            // If not prepend date and time
            dateTimeStr = dateTimeStr.Replace("-", "_");
            dateTimeStr = dateTimeStr.Replace(" ", "_");
            dateTimeStr = dateTimeStr.Replace(":", "_");
            uniqName = dateTimeStr + "_" + uniqName;
            return uniqName;
        }

        public static string GetImageFolderForFile(string baseFolderForImages, string uniqName, bool bCreateIfReqd)
        {
            // Get a subfolder name to use for processing the images from the file
            // Assuming the uniq name is the date in YYYYMMDD format the subfolder will be YYYYMM
            string subFolderName = uniqName;
            subFolderName.Replace("_", "");
            subFolderName = subFolderName.Substring(0, 6);
            string imgFolder = Path.Combine(baseFolderForImages, subFolderName).Replace('\\', '/');

            // Check exists and create if not
            if (bCreateIfReqd)
            {
                if (!Directory.Exists(imgFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(imgFolder);
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Can't create folder {0} excp {1}", imgFolder, excp.Message);
                    }
                }
            }
            return imgFolder;
        }
    }

    public class FiledDocInfo
    {
        public ObjectId Id;
        public string uniqName { get; set; }
        public string docTypeFiled { get; set; }
        public DateTime docDateFiled { get; set; }
        public string pathFiledTo { get; set; }
        public string filingResult { get; set; }
        public string filingErrorMsg { get; set;}
        public bool docSuitableForCrossCheckingDoctypes { get; set; }
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
