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

        public ScanDocInfo(string a_uniqname, int a_numPages, int a_numPagesWithText, DateTime a_createDate, string a_origFileName, bool a_flagForHelpFiling)
        {
            uniqName = a_uniqname;
            numPages = a_numPages;
            numPagesWithText = a_numPagesWithText;
            createDate = a_createDate;
            origFileName = a_origFileName;
            flagForHelpFiling = a_flagForHelpFiling;
        }
        public ObjectId Id;
        public string uniqName { get; set; }
        public int numPages { get; set; }
        public int numPagesWithText { get; set; }
        public DateTime createDate { get; set; }
        public string origFileName { get; set; }
        public bool flagForHelpFiling { get; set; }

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
            subFolderName = subFolderName.Replace("_", "");
            if (uniqName.Trim().Length == 0)
                return "";
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
        public enum DocFinalStatus
        {
            STATUS_NONE, STATUS_DELETED, STATUS_FILED, STATUS_DELETED_AFTER_EDIT
        }

        public static string GetFinalStatusStr(DocFinalStatus stat)
        {
            switch(stat)
            {
                case DocFinalStatus.STATUS_NONE: return "None";
                case DocFinalStatus.STATUS_DELETED: return "Deleted";
                case DocFinalStatus.STATUS_FILED: return "Filed";
                case DocFinalStatus.STATUS_DELETED_AFTER_EDIT: return "DeletedAfterEdit";
            }
            return "Unknown";
        }

        public FiledDocInfo(string a_uniqName)
        {
            uniqName = a_uniqName;
            filedAs_docType = "";
            filedAs_pathAndFileName = "";
            filedAs_dateOfDoc = DateTime.MinValue;
            filedAs_moneyInfo = "";
            filedAs_followUpNeeded = "";
            filedAs_addToCalendar = "";
            filedAs_flagForRefilingInfo = "";
            filedAs_eventName = "";
            filedAs_eventDateTime = DateTime.MinValue;
            filedAs_eventDuration = new TimeSpan();
            filedAs_eventDescr = "";
            filedAs_eventLocation = "";
            filedAs_flagAttachFile = "";
            includeInXCheck = false;
            filedAt_dateAndTime = DateTime.Now;
            filedAt_errorMsg = "";
            filedAt_finalStatus = DocFinalStatus.STATUS_NONE;            
        }

        public void SetDeletedInfo(bool deletedDueToFileEditing)
        {
            includeInXCheck = false;
            filedAt_dateAndTime = DateTime.Now;
            filedAt_errorMsg = "";
            filedAt_finalStatus = deletedDueToFileEditing ? DocFinalStatus.STATUS_DELETED_AFTER_EDIT : DocFinalStatus.STATUS_DELETED;
        }

        public void SetDocFilingInfo(string a_filedAs_docType, string a_filedAs_pathAndFileName, DateTime a_filedAs_dateOfDoc,
                            string a_filedAs_moneyInfo, string a_filedAs_followUpNeeded, string a_filedAs_addToCalendar,
                            string a_filedAs_eventName, DateTime a_filedAs_eventDateTime, TimeSpan a_filedAs_eventDuration, string a_filedAs_eventDescr, string a_filedAs_eventLocation,
                            string a_filedAs_flagAttachFile)
        {
            filedAs_docType = a_filedAs_docType;
            filedAs_pathAndFileName = a_filedAs_pathAndFileName;
            filedAs_dateOfDoc = a_filedAs_dateOfDoc;
            filedAs_moneyInfo = a_filedAs_moneyInfo;
            filedAs_followUpNeeded = a_filedAs_followUpNeeded;
            filedAs_addToCalendar = a_filedAs_addToCalendar;
            filedAs_flagAttachFile = a_filedAs_flagAttachFile;
            filedAs_eventName = a_filedAs_eventName;
            filedAs_eventDateTime = a_filedAs_eventDateTime;
            filedAs_eventDuration = a_filedAs_eventDuration;
            filedAs_eventDescr = a_filedAs_eventDescr;
            filedAs_eventLocation = a_filedAs_eventLocation;
        }

        public void SetFiledAtInfo(bool a_includeInXCheck, DateTime a_filedAt_dateAndTime, string a_filedAt_errorMsg, DocFinalStatus a_filedAt_finalStatus)
        {
            includeInXCheck = a_includeInXCheck;
            filedAt_dateAndTime = a_filedAt_dateAndTime;
            filedAt_errorMsg = a_filedAt_errorMsg;
            filedAt_finalStatus = a_filedAt_finalStatus;            
        }

        public ObjectId Id;

        // uniqname file
        public string uniqName { get; set; }

        // Info set
        public string filedAs_docType { get; set; }
        public string filedAs_pathAndFileName { get; set; }
        public DateTime filedAs_dateOfDoc { get; set; }
        public string filedAs_moneyInfo { get; set; }
        public string filedAs_followUpNeeded { get; set; }
        public string filedAs_addToCalendar { get; set; }
        public string filedAs_flagForRefilingInfo { get; set; }  // not currently used but left to avoid database errors on read older records
        public string filedAs_eventName { get; set; }
        public DateTime filedAs_eventDateTime { get; set; }
        public TimeSpan filedAs_eventDuration { get; set; }
        public string filedAs_eventDescr { get; set; }
        public string filedAs_eventLocation { get; set; }
        public string filedAs_flagAttachFile { get; set; }

        // Flag relating to use of file in cross checking
        public bool includeInXCheck { get; set; }

        // Time and status of filing operation
        public DateTime filedAt_dateAndTime { get; set; }
        public string filedAt_errorMsg { get; set; }
        public DocFinalStatus filedAt_finalStatus { get; set; }

    }

    public class ScanPages
    {
        public ObjectId Id;
        public string uniqName { get; set; }
        public List<List<ScanTextElem>> scanPagesText { get; set; }
        public List<int> pageRotations { get; set; }

        public ScanPages(string a_uniqName)
        {
            uniqName = a_uniqName;
            scanPagesText = new List<List<ScanTextElem>>();
            pageRotations = new List<int>();
        }

        public ScanPages(string a_uniqName, List<int> pageRots, List<List<ScanTextElem>> spagesText)
        {
            uniqName = a_uniqName;
            scanPagesText = spagesText;
            pageRotations = pageRots;
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

    public class ExistingFileInfoRec
    {
        public ObjectId Id;
        public string filename;
        public byte[] md5Hash;
        public long fileLength;
    }


}
