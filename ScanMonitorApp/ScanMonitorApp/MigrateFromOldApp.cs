﻿using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    class MigrateFromOldApp
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private static List<TextSubst> textSubst = new List<TextSubst>
                {
                    new TextSubst(@"\RobAndJudyPersonal\Info\Manuals - ", @"\RobAndJudyPersonal\"),
                    new TextSubst(@"\8 Dick Place\Self storage", @"\8 Dick Place\Removals & Storage\Self storage")
                };

        // Test code
        public static void AddOldDocTypes(string filename, DocTypesMatcher docTypesMatcher)
        {
            ScanMan.OldXmlRulesManager xmlRulesManager = new ScanMan.OldXmlRulesManager(filename);
            List<ScanMan.OldXmlRulesManager.DocType> oldDocTypeList = xmlRulesManager.GetAllDocTypes();

            // Handle match expression
            foreach (ScanMan.OldXmlRulesManager.DocType oldDocType in oldDocTypeList)
            {
                DocType newDocType = new DocType();
                newDocType.docTypeName = oldDocType.dtName;
                if (oldDocType.goodStrings.Count > 0)
                {
                    newDocType.matchExpression = "";
                    foreach (ScanMan.OldXmlRulesManager.CheckItem chkItem in oldDocType.goodStrings)
                    {
                        if (newDocType.matchExpression != "")
                            newDocType.matchExpression += " & ";
                        chkItem.checkString = chkItem.checkString.Replace(",", "&");
                        if (chkItem.checkString.Contains('|'))
                            newDocType.matchExpression += "( " + chkItem.checkString + " )";
                        else
                            newDocType.matchExpression += chkItem.checkString;
                    }
                }
                string notStr = "";
                if (oldDocType.badStrings.Count > 0)
                {
                    notStr = "( ";
                    foreach (ScanMan.OldXmlRulesManager.CheckItem chkItem in oldDocType.badStrings)
                    {
                        if (notStr != "( ")
                            notStr += " & ";
                        chkItem.checkString = chkItem.checkString.Replace(",", "&");
                        if (chkItem.checkString.Contains('|'))
                            notStr += "( " + chkItem.checkString + " )";
                        else
                            notStr += chkItem.checkString;
                    }
                    notStr += " )";
                }
                if (notStr != "")
                    newDocType.matchExpression = "( " + newDocType.matchExpression + " ) & !" + notStr;

                // Handle thumbnail
                if (oldDocType.thumbFileNames.Count > 0)
                    newDocType.thumbnailForDocType = oldDocType.thumbFileNames[0].Replace('\\', '/');
                else
                    newDocType.thumbnailForDocType = "";
                docTypesMatcher.AddOrUpdateDocTypeRecInDb(newDocType);
            }

            logger.Info("Finished loading legacy doc types");

        }

        public static void LoadAuditFileToDb(string fileName, ScanDocHandler scanDocHandler)
        {
            bool TEST_ON_LOCAL_DATA = true;

            // Read file
            using (StreamReader sr = new StreamReader(fileName))
            {
                while (sr.Peek() >= 0)
                {
                    string line = sr.ReadLine();
                    string[] fields = line.Split('\t');
                    if ((fields[6] != "OK") || ((fields[5] == "TEST") || (fields[5] == "DELETED")))
                        continue;
                    AuditData ad = new AuditData();
                    ad.ProcDateAndTime = fields[0];
                    ad.DocType = fields[1];
                    if (ad.DocType == "")
                        continue;
                    ad.OrigFileName = fields[2];
                    string uniqName = ScanDocInfo.GetUniqNameForFile(fields[4], fields[0]);
                    ad.UniqName = uniqName;
                    ad.DestFile = DoTextSubst(fields[3]);
                    ad.ArchiveFile = fields[4];
                    if (TEST_ON_LOCAL_DATA)
                        ad.ArchiveFile = ad.ArchiveFile.Replace(@"\\N7700PRO\Archive\ScanAdmin\ScanBackups\", @"C:\Users\Rob\Dropbox\20140227 Train\ScanBackups\");
                    ad.ProcMessage = fields[5];
                    ad.ProcStatus = fields[6];
                    ad.DestOk = "?"; // File.Exists(ad.DestFile) ? "" : "NO";
                    bool arcvExists = File.Exists(ad.ArchiveFile);
                    if (!arcvExists)
                    {
//                        Console.WriteLine("File missing " + ad.ArchiveFile);
                        continue;
                    }
                    ad.ArcvOk = arcvExists ? "" : "NO";

                    // Process file
                    scanDocHandler.ProcessPdfFile(ad.ArchiveFile, uniqName, true, true, true, false, true, true);

                    // Create filed info record
                    FiledDocInfo fdi = new FiledDocInfo();
                    fdi.docDateFiled = DateTime.ParseExact(ad.ProcDateAndTime, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    fdi.docSuitableForCrossCheckingDoctypes = true;
                    fdi.docTypeFiled = ad.DocType;
                    fdi.filingErrorMsg = ad.ProcMessage;
                    fdi.filingResult = ad.ProcStatus;
                    fdi.pathFiledTo = ad.DestFile.Replace('\\', '/');
                    fdi.uniqName = ad.UniqName;
                    scanDocHandler.AddFiledDocRecToMongo(fdi);

                }
            }

            logger.Info("Finished loading from old log");

            //// Sort to find duplicates
            //bool sortIt = false;
            //if (sortIt)
            //{
            //    var sortedAd = from item in _auditDataColl
            //                   orderby item.UniqName
            //                   select item;

            //    string lastuniq = "";
            //    foreach (AuditData ad in sortedAd)
            //    {
            //        if (lastuniq == ad.UniqName)
            //        {
            //            Console.WriteLine("Duplicate name " + ad.UniqName);
            //        }
            //        lastuniq = ad.UniqName;
            //    }
            //}
            //// Check validity
            //for (int rowidx = 0; rowidx < auditListView.Items.Count; rowidx++)
            //{
            //    AuditData audData = (AuditData)(auditListView.Items[rowidx]);
            //    string destFile = audData.DestFile;
            //    if (File.Exists(destFile))
            //        auditListView.
            //}
        }

        private static string DoTextSubst(string inStr)
        {
            foreach (TextSubst ts in textSubst)
            {
                inStr = inStr.Replace(ts.origText, ts.newText);
            }
            return inStr;
        }

        private class OldAuditData
        {
            public string UniqName { get; set; }
            public string DestOk { get; set; }
            public string ArcvOk { get; set; }
            public string DocType { get; set; }
            public string ProcStatus { get; set; }
            public string DestFile { get; set; }
            public string ArchiveFile { get; set; }
            public string OrigFileName { get; set; }
            public string ProcDateAndTime { get; set; }
            public string ProcMessage { get; set; }
        }

    }

    public class TextSubst
    {
        public TextSubst(string o, string n)
        {
            origText = o;
            newText = n;
        }
        public string origText;
        public string newText;
    }
}
