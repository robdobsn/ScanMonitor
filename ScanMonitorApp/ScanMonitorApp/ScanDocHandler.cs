﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using MongoDB.Driver;
using System.IO;
using MongoDB.Driver.Builders;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Threading;
using System.Net.Mail;
using System.Net;

namespace ScanMonitorApp
{
    public class ScanDocHandler
    {
        const int MAX_FILE_PROCESS_RETRIES = 3;
        const int TIME_BETWEEN_RETRIES_SECS = 5;

        private static Logger logger = LogManager.GetCurrentClassLogger();
        public delegate void ReportStatus(string str);
        private ReportStatus _reportStatusFn;
        private DateTime _lastTimeMongoDbConnErrLogged = DateTime.MinValue;
        private MongoClient _dbClient;
        private DocTypesMatcher _docTypesMatcher;
        private ScanDocHandlerConfig _scanConfig;
        private BackgroundWorker _fileProcessBkgndWorker = new BackgroundWorker();
        private ReportStatus _docFilingReportStatus;
        private ReportStatus _filingCompleteCallback;
        private string _docFilingStatusStr = "";

        #region Init

        public ScanDocHandler(ReportStatus reportStatusFn, DocTypesMatcher docTypesMatcher, ScanDocHandlerConfig scanConfig)
        {
            _reportStatusFn = reportStatusFn;
            _docTypesMatcher = docTypesMatcher;
            _scanConfig = scanConfig;

            // Init the background worker used for processing docs
            _fileProcessBkgndWorker.WorkerSupportsCancellation = true;
            _fileProcessBkgndWorker.WorkerReportsProgress = true;
            _fileProcessBkgndWorker.DoWork += new DoWorkEventHandler(FileProcessDoWork);
            _fileProcessBkgndWorker.ProgressChanged += new ProgressChangedEventHandler(FileProcessProgressChanged);
            _fileProcessBkgndWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(FileProcessRunWorkerCompleted);

            // Init db
            InitDatabase();
        }

        #endregion

        #region Database Common Code

        public void InitDatabase()
        {
            // Mongo init
            try
            {
                var connectionString = Properties.Settings.Default.DbConnectionString;
                _dbClient = new MongoClient(connectionString);
            }
            catch (Exception excp)
            {
                string excpStr = String.Format("Cannot connect to mongo {0}", excp.Message);
                logger.Error(excpStr);
                _reportStatusFn(excpStr);
            }
        }

        public bool CheckMongoConnection()
        {
            bool bOk = false;
            try
            {
                // Check connection active
                MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();

                // Check if record exists already
                MongoCursor<ScanDocInfo> foundSdf = collection_sdinfo.Find(Query.EQ("uniqName", ""));
                foreach (ScanDocInfo foundRec in foundSdf)
                {
                    bOk = true;
                    break;
                }
                bOk = true;
            }
            catch (Exception excp)
            {
                if ((DateTime.Now - _lastTimeMongoDbConnErrLogged).TotalMinutes > 60)
                {
                    logger.Error("Mongo db not connected {0} coll {1} excp {2}", _scanConfig._dbNameForDocs, _scanConfig._dbCollectionForDocInfo, excp.Message);
                    _lastTimeMongoDbConnErrLogged = DateTime.Now;
                }
            }
            return bOk;
        }

        public ScanDocAllInfo GetScanDocAllInfo(string uniqName)
        {
            if (uniqName.Trim() == "")
                return null;

            // Get first matching documents
            MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();
            ScanDocInfo scanDoc = collection_sdinfo.FindOne(Query.EQ("uniqName", uniqName));
            MongoCollection<ScanPages> collection_spages = GetDocPagesCollection();
            ScanPages scanPages = collection_spages.FindOne(Query.EQ("uniqName", uniqName));
            MongoCollection<FiledDocInfo> collection_filedinfo = GetFiledDocsCollection();
            FiledDocInfo filedDocInfo = collection_filedinfo.FindOne(Query.EQ("uniqName", uniqName));
            return new ScanDocAllInfo(scanDoc, scanPages, filedDocInfo);
        }

        #endregion

        #region ScanDocInfo Collection Handling

        private MongoCollection<ScanDocInfo> GetDocInfoCollection()
        {
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_scanConfig._dbNameForDocs); // the name of the database
            return database.GetCollection<ScanDocInfo>(_scanConfig._dbCollectionForDocInfo);
        }

        public string GetListOfScanDocsJson()
        {
            // Get list of documents
            List<ScanDocInfo> sdi = GetListOfScanDocs();
            return JsonConvert.SerializeObject(sdi);
        }

        public List<ScanDocInfo> GetListOfScanDocs()
        {
            // Get list of documents
            MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();
            return collection_sdinfo.FindAll().ToList();
        }

        public string GetScanDocJson(string uniqName)
        {
            ScanDocInfo scanDoc = GetScanDocInfo(uniqName);
            return JsonConvert.SerializeObject(scanDoc);
        }

        public ScanDocInfo GetScanDocInfo(string uniqName)
        {
            // Get first matching documents
            MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();
            ScanDocInfo scanDoc = collection_sdinfo.FindOne(Query.EQ("uniqName", uniqName));
            return scanDoc;
        }

        public void AddDocInfoRecToMongo(ScanDocInfo scanDocInfo)
        {
            // Mongo append
            try
            {
                MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();
                collection_sdinfo.Insert(scanDocInfo);
                // Log it
                logger.Info("Added scandocinfo record for {0}", scanDocInfo.uniqName);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot insert scandoc recinfo into {0} Coll... {1} for file {2} excp {3}",
                            _scanConfig._dbNameForDocs, _scanConfig._dbCollectionForDocInfo, scanDocInfo.uniqName,
                            excp.Message);
            }
        }

        public bool ScanDocInfoRecordExists(string uniqName)
        {
            try
            {
                // Get collection
                MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();

                // Check if record exists already
                MongoCursor<ScanDocInfo> foundSdf = collection_sdinfo.Find(Query.EQ("uniqName", uniqName));
                foreach (ScanDocInfo foundRec in foundSdf)
                    return false;
            }
            catch (Exception excp)
            {
                if ((DateTime.Now - _lastTimeMongoDbConnErrLogged).TotalMinutes > 60)
                {
                    logger.Error("Mongo db error checking doc exists {0} coll {1} excp {2}", _scanConfig._dbNameForDocs, _scanConfig._dbCollectionForDocInfo, excp.Message);
                    _lastTimeMongoDbConnErrLogged = DateTime.Now;
                }
                return false;
            }
            // Ok to add to db
            return true;
        }

        public bool AddOrUpdateScanDocRecInDb(ScanDocInfo scanDocInfo)
        {
            // Mongo save
            try
            {
                MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();
                collection_sdinfo.Save(scanDocInfo, SafeMode.True);
                // Log it
                logger.Info("Added/updated scandocinfo record for {0}", scanDocInfo.uniqName);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot add/update scandocinfo into {0} Coll... {1} for file {2} excp {3}",
                            Properties.Settings.Default.DbNameForDocs, Properties.Settings.Default.DbCollectionForDocInfo, scanDocInfo.uniqName,
                            excp.Message);
                return false;
            }
            return true;
        }


        #endregion

        #region ScanDocPages Collection Handling

        private MongoCollection<ScanPages> GetDocPagesCollection()
        {
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_scanConfig._dbNameForDocs); // the name of the database
            return database.GetCollection<ScanPages>(_scanConfig._dbCollectionForDocPages);
        }

        public ScanPages GetScanPages(string uniqName)
        {
            MongoCollection<ScanPages> collection_spages = GetDocPagesCollection();
            return collection_spages.FindOne(Query.EQ("uniqName", uniqName));
        }

        public void AddScanPagesRecToMongo(ScanPages scanPages)
        {
            // Mongo append
            try
            {
                MongoCollection<ScanPages> collection_spages = GetDocPagesCollection();
                collection_spages.Insert(scanPages);
                // Log it
                logger.Info("Added scandocpages record for {0}", scanPages.uniqName);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot insert scandocpages into {0} Coll... {1} for file {2} excp {3}",
                            _scanConfig._dbNameForDocs, _scanConfig._dbCollectionForDocPages, scanPages.uniqName,
                            excp.Message);
            }
        }

        #endregion

        #region FiledDocs Collection Handling

        private MongoCollection<FiledDocInfo> GetFiledDocsCollection()
        {
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_scanConfig._dbNameForDocs); // the name of the database
            return database.GetCollection<FiledDocInfo>(_scanConfig._dbCollectionForFiledDocs);
        }

        public List<FiledDocInfo> GetListOfFiledDocs()
        {
            // Get list of documents
            MongoCollection<FiledDocInfo> collection_fdinfo = GetFiledDocsCollection();
            return collection_fdinfo.FindAll().ToList();
        }

        public FiledDocInfo GetFiledDocInfo(string uniqName)
        {
            MongoCollection<FiledDocInfo> collection_fdinfo = GetFiledDocsCollection();
            FiledDocInfo fdi = collection_fdinfo.FindOne(Query.EQ("uniqName", uniqName));
            return fdi;
        }

        public bool AddFiledDocRecToMongo(FiledDocInfo filedDocInfo)
        {
            // Mongo append
            bool bOk = false;
            try
            {
                MongoCollection<FiledDocInfo> collection_fileddoc = GetFiledDocsCollection();
                WriteConcernResult rslt = collection_fileddoc.Insert(filedDocInfo);
                bOk = rslt.Ok;
                // Log it
                logger.Info("Added fileddoc record for {0}", filedDocInfo.uniqName);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot insert fileddoc into {0} Coll... {1} for file {2} excp {3}",
                            _scanConfig._dbNameForDocs, _scanConfig._dbCollectionForFiledDocs, filedDocInfo.uniqName,
                            excp.Message);
            }
            return bOk;
        }

        public bool AddOrUpdateFiledDocRecInDb(FiledDocInfo filedDocInfo)
        {
            // Mongo save
            try
            {
                MongoCollection<FiledDocInfo> collection_fdinfo = GetFiledDocsCollection();
                collection_fdinfo.Save(filedDocInfo, SafeMode.True);
                // Log it
                logger.Info("Added/updated fileddoc record for {0}", filedDocInfo.uniqName);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot add/update fileddoc rec into {0} Coll... {1} for file {2} excp {3}",
                            Properties.Settings.Default.DbNameForDocs, Properties.Settings.Default.DbCollectionForFiledDocs, filedDocInfo.uniqName,
                            excp.Message);
                return false;
            }
            return true;
        }

        #endregion

        #region Unfiled Document Handling

        public List<string> GetListOfUnfiledDocUniqNames()
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            // Get doc uniq names for scanned docs
            MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();
            MongoCursor<ScanDocInfo> scannedDocs = collection_sdinfo.FindAll();
            List<string> scannedDocUniqNames = new List<string>();
            foreach (ScanDocInfo sdi in scannedDocs)
                scannedDocUniqNames.Add(sdi.uniqName);

            // Get doc uniq names for filed docs
            MongoCollection<FiledDocInfo> collection_fdinfo = GetFiledDocsCollection();
            MongoCursor<FiledDocInfo> filedDocs = collection_fdinfo.FindAll();
            List<string> filedDocUniqNames = new List<string>();
            foreach (FiledDocInfo fdi in filedDocs)
                filedDocUniqNames.Add(fdi.uniqName);

            // Create list of unfiled doc uniq names
            List<string> unfiledDocs = scannedDocUniqNames.Except(filedDocUniqNames).ToList();

            stopWatch.Stop();
            //Console.WriteLine("GetList Elapsed : {0}ms, recs {1}", stopWatch.ElapsedMilliseconds, unfiledDocs.Count);
            return unfiledDocs;
        }

        public int GetCountOfUnfiledDocs()
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();
            long scannedCount = collection_sdinfo.Count();
            MongoCollection<FiledDocInfo> collection_fdinfo = GetFiledDocsCollection();
            long filedCount = collection_fdinfo.Count();

            stopWatch.Stop();
            //Console.WriteLine("GetCount Elapsed : {0}ms, recs {1}", stopWatch.ElapsedMilliseconds, scannedCount - filedCount);

            return (int)(scannedCount - filedCount);
        }

        #endregion

        #region Delete File

        public bool DeleteFile(string uniqName, FiledDocInfo fdi, string fullSourceName)
        {
            // Delete the physical file
            bool fileDeleteOk = false;
            string errStr = "";
            if (fullSourceName.Trim() != "")
            {
                try
                {
                    // Check file exists
                    if (!File.Exists(fullSourceName))
                    {
                        logger.Error("Trying to delete non-existent file {0}", fullSourceName);
                        return true;
                    }

                    if (Properties.Settings.Default.TestModeFileTo == "")
                    {
                        File.Delete(fullSourceName);
                    }
                    else
                    {
                        logger.Info("TEST would be deleting {0}", fullSourceName);
                    }
                    fileDeleteOk = true;
                }
                catch (Exception e)
                {
                    errStr = e.Message;
                }
            }
            if (!fileDeleteOk)
                return false;

            // Record the deletion in the filed docs database
            if (fdi == null)
                fdi = new FiledDocInfo(uniqName);
            fdi.SetDeletedInfo();

            return AddOrUpdateFiledDocRecInDb(fdi);
        }

        #endregion

        #region Validity Checking before Filing

        public bool CheckOkToFileDoc(string destDocPathAndFileName, out string rsltText)
        {
            // Get folder
            string destPathOnly = Path.GetDirectoryName(destDocPathAndFileName);

            // Check folder exists
            if (!Directory.Exists(destPathOnly))
            {
                rsltText = "Dest folder not found";
                return false;
            }

            // Check file name validity
            try
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(destDocPathAndFileName);
                if (Path.GetFileNameWithoutExtension(fi.FullName) == "")
                {
                    rsltText = "The file name cannot be blank";
                    return false;
                }
            }
            catch (ArgumentException e1)
            {
                rsltText = "The destination file name is invalid. " + e1.Message;
                return false;
            }
            catch (System.IO.PathTooLongException e2)
            {
                rsltText = "The destination file path is invalid. " + e2.Message;
                return false;
            }
            catch (NotSupportedException e3)
            {
                rsltText = "The destination file name is invalid. " + e3.Message;
                return false;
            }

            // Check if dest file exists
            if (File.Exists(destDocPathAndFileName))
            {
                rsltText = "A destination file of this name already exists";
                return false;
            }

            rsltText = "Checked Ok";
            return true;
        }

        #endregion

        #region Doc Filing Processing

        public void StartProcessFilingOfDoc(ReportStatus reportStatusCallback, ReportStatus filingCompleteCallback, ScanDocInfo sdi, FiledDocInfo fdi, string emailPassword, out string rsltText)
        {
            _docFilingReportStatus = reportStatusCallback;
            _filingCompleteCallback = filingCompleteCallback;

            // Start the worker thread to do the copy and move
            if (!_fileProcessBkgndWorker.IsBusy)
            {
                object[] args = new object[] { sdi, fdi, emailPassword };
                _fileProcessBkgndWorker.RunWorkerAsync(args);
                rsltText = "Processing file ...";
            }
            else
            {
                rsltText = "Busy ... please wait";
            }
            _docFilingStatusStr = rsltText;
        }

        public bool IsBusy()
        {
            return _fileProcessBkgndWorker.IsBusy;
        }

        private void FileProcessDoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            _docFilingStatusStr = "";
            bool bResult = false;

            // Get args
            object[] args = (object[])e.Argument;
            e.Result = args;

            // Try several times to copy and move the file
            for (int i = 1; i <= MAX_FILE_PROCESS_RETRIES; i++)
            {
                if ((worker.CancellationPending == true))
                {
                    e.Cancel = true;
                    _docFilingStatusStr = "Cancelled by User";
                    worker.ReportProgress(0, _docFilingStatusStr);
                    return;
                }
                else
                {
                    // Attempt to perform the file operation
                    _docFilingStatusStr = "Copying file ...";
                    worker.ReportProgress(0, _docFilingStatusStr);
                    bResult = CopyAndMoveTheFile(i, args);
                    if (bResult)
                    {
                        _docFilingStatusStr = "File copied ...";
                        worker.ReportProgress(0, _docFilingStatusStr);
                        break;
                    }
                    else
                    {
                        // Try again in a few secs
                        for (int timerCount = TIME_BETWEEN_RETRIES_SECS; timerCount > 0; timerCount--)
                        {
                            string tryStr = string.Format("Copying | Retry {0} (of {1}) in {2} seconds", i, MAX_FILE_PROCESS_RETRIES, timerCount);
                            worker.ReportProgress((i * 10), tryStr);
                            Thread.Sleep(1000);
                        }
                    }
                }
            }

            // Check copy ok
            if (!bResult)
            {
                _docFilingStatusStr = "Failed to copy (check log)";
                worker.ReportProgress(0, _docFilingStatusStr);
                return;
            }

            // Complete the status information
            FiledDocInfo fdi = (FiledDocInfo)args[1]; 
            FiledDocInfo.DocFinalStatus dfs = FiledDocInfo.DocFinalStatus.STATUS_FILED;
            _docFilingStatusStr = "Updating db ...";
            worker.ReportProgress(0, _docFilingStatusStr);
            fdi.SetFiledAtInfo(true, DateTime.Now, _docFilingStatusStr, dfs);
            e.Result = args;

            // Update database
            bResult = AddOrUpdateFiledDocRecInDb(fdi);

            // Check db update
            if (!bResult)
            {
                _docFilingStatusStr = "Failed db update (check log)";
                worker.ReportProgress(0, _docFilingStatusStr);
                return;
            }

            _docFilingStatusStr = "Db updated ...";
            worker.ReportProgress(0, _docFilingStatusStr);

            // Send email(s) as required
            if ((fdi.filedAs_followUpNeeded.Trim() != "") || (fdi.filedAs_addToCalendar.Trim() != ""))
            {
                try
                {
                    _docFilingStatusStr = "Sending emails ...";
                    worker.ReportProgress(0, _docFilingStatusStr);
                    SendEmailsForFollowUpOrCalendar((FiledDocInfo)args[1], (string)args[2]);
                }
                catch (Exception excp)
                {
                    logger.Error("Failed sending mail {0}", excp.Message);
                    _docFilingStatusStr = "Ok but email sending failed";
                    worker.ReportProgress(0, _docFilingStatusStr);
                    return;
                }
            }
            _docFilingStatusStr = "Completed filing OK";
            worker.ReportProgress(0, _docFilingStatusStr);

            // This seems necessary - otherwise when refreshing display still shows old doc present!
            Thread.Sleep(1000);
        }

        private void SendEmailsForFollowUpOrCalendar(FiledDocInfo fdi, string emailPassword)
        {
            string smtpAddress = Properties.Settings.Default.EmailService;
            int portNumber = 587;
            Int32.TryParse(Properties.Settings.Default.EmailServicePort, out portNumber);
            bool enableSSL = true;
            MailAddress emailFrom = new MailAddress(Properties.Settings.Default.EmailFrom);
            List<MailAddress> emailTo = GetEmailToAddresses();

            using (MailMessage mail = new MailMessage())
            {
                mail.From = emailFrom;
                string[] followUpNames = fdi.filedAs_followUpNeeded.Split(' ');

                foreach (MailAddress emto in emailTo)
                {
                    if (fdi.filedAs_addToCalendar != "")
                    {
                        mail.To.Add(emto);
                    }
                    else
                    {
                        foreach (string name in followUpNames)
                        {
                            if (emto.DisplayName.Trim().IndexOf(name.Trim(), StringComparison.CurrentCultureIgnoreCase) == 0)
                            {
                                mail.To.Add(emto);
                                break;
                            }
                        }
                    }
                }

                if (mail.To.Count == 0)
                    return;

                mail.Subject = fdi.filedAs_eventName;
                mail.Body = fdi.filedAs_eventDescr;
                mail.IsBodyHtml = true;

                // Check for attachment
                if (fdi.filedAs_flagAttachFile != "")
                {
                    try
                    {
                        mail.Attachments.Add(new Attachment(fdi.filedAs_pathAndFileName));
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Failed to attach {0} excp {1}", fdi.filedAs_pathAndFileName, excp.Message);
                    }
                }

                if (fdi.filedAs_addToCalendar != "")
                    AddMeetingRequestToMsg(mail, fdi);

                using (SmtpClient smtp = new SmtpClient(smtpAddress, portNumber))
                {
                    smtp.Credentials = new NetworkCredential(emailFrom.Address, emailPassword);
                    smtp.EnableSsl = enableSSL;
                    smtp.Send(mail);
                }
            }
        }

        public void AddMeetingRequestToMsg(MailMessage msg, FiledDocInfo fdi)
        {
            StringBuilder str = new StringBuilder();
            str.AppendLine("BEGIN:VCALENDAR");                  
            str.AppendLine("VERSION:2.0");
            str.AppendLine("METHOD:REQUEST");
            str.AppendLine("BEGIN:VEVENT");
            str.AppendLine(string.Format("DTSTART:{0:yyyyMMddTHHmmssZ}", fdi.filedAs_eventDateTime));
            str.AppendLine(string.Format("DTSTAMP:{0:yyyyMMddTHHmmssZ}", fdi.filedAs_eventDateTime.ToUniversalTime()));
            str.AppendLine(string.Format("DTEND:{0:yyyyMMddTHHmmssZ}", fdi.filedAs_eventDateTime.Add(fdi.filedAs_eventDuration)));
            str.AppendLine(string.Format("LOCATION: {0}", fdi.filedAs_eventLocation)); 
            str.AppendLine(string.Format("UID:{0}", Guid.NewGuid()));
            str.AppendLine(string.Format("DESCRIPTION:{0}", fdi.filedAs_eventDescr));
            str.AppendLine(string.Format("X-ALT-DESC;FMTTYPE=text/html:{0}", fdi.filedAs_eventDescr));
            str.AppendLine(string.Format("SUMMARY:{0}", fdi.filedAs_eventName));
            str.AppendLine(string.Format("ORGANIZER:MAILTO:{0}", msg.From.Address));
            foreach (MailAddress emto in msg.To)
                str.AppendLine(string.Format("ATTENDEE;CN=\"{0}\";RSVP=TRUE:mailto:{1}", emto.DisplayName, emto.Address));
            str.AppendLine("BEGIN:VALARM");
            str.AppendLine("TRIGGER:-PT15M");
            str.AppendLine("ACTION:DISPLAY");
            str.AppendLine("DESCRIPTION:Reminder");
            str.AppendLine("END:VALARM");
            str.AppendLine("END:VEVENT");
            str.AppendLine("END:VCALENDAR");
            System.Net.Mime.ContentType ct = new System.Net.Mime.ContentType("text/calendar");
            ct.Parameters.Add("method", "REQUEST");
            AlternateView avCal = AlternateView.CreateAlternateViewFromString(str.ToString(), ct);
            msg.AlternateViews.Add(avCal);
        }

        private bool CopyAndMoveTheFile(int attemptNo, object[] args)
        {
            // Debug
            Console.WriteLine("CopyAndMoveTheFile Try#" + attemptNo + " starting at " + DateTime.Now.ToLongTimeString());

            // Args
            ScanDocInfo sdi = (ScanDocInfo)args[0];
            FiledDocInfo fdi = (FiledDocInfo)args[1];

            // Try to perform the copy and move
            bool bResult = false;
            bool bDelete = false;
            // Check for test mode
            if (Properties.Settings.Default.TestModeFileTo == "")
            {
                if (File.Exists(sdi.origFileName))
                {
                    bResult = CopyFile(sdi.origFileName, fdi.filedAs_pathAndFileName, ref _docFilingStatusStr);
                    bDelete = true;
                }
                else
                {
                    string archiveFileName = Path.Combine(Properties.Settings.Default.DocArchiveFolder, sdi.uniqName + ".pdf");
                    if (File.Exists(archiveFileName))
                    {
                        bResult = CopyFile(archiveFileName, fdi.filedAs_pathAndFileName, ref _docFilingStatusStr);
                    }
                    else
                    {
                        logger.Info("Attempting to file {0} original source and archive both missing", sdi.uniqName);
                        _docFilingStatusStr = "File missing " + archiveFileName;
                        bResult = false;
                    }
                    bDelete = false;
                }
            }
            else
            {
                string toFile = Path.Combine(Properties.Settings.Default.TestModeFileTo, Path.GetFileName(fdi.filedAs_pathAndFileName));
                bResult = CopyFile(sdi.origFileName, toFile, ref _docFilingStatusStr);
            }
            if (bResult && bDelete)
            {
                // Delete the original file
                try
                {
                    if (Properties.Settings.Default.TestModeFileTo == "")
                    {
                        File.Delete(sdi.origFileName);
                    }
                    else
                    {
                        logger.Info("TEST would be deleting {0}", sdi.origFileName);
                    }
                }
                catch (Exception e)
                {
                    logger.Error("Failed to delete file {0} excp {1}", sdi.origFileName, e.Message);
                }
            }
            if (!bResult)
            {
                _docFilingStatusStr = "COPY FAILED " + _docFilingStatusStr;
            }
            _docFilingStatusStr = _docFilingStatusStr.Trim();

            return bResult;
        }

        private void FileProcessProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            _docFilingReportStatus((string)e.UserState);
        }

        private void FileProcessRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                _filingCompleteCallback(_docFilingStatusStr);
            }
            else if (e.Error != null)
            {
                logger.Error("Exception in file process worker status={0} excp {1}", _docFilingStatusStr, e.Error.Message);
            }
            else if (e.Result != null)
            {
                _filingCompleteCallback(_docFilingStatusStr);
            }
        }

        #endregion

        #region PDF file processing

        // when processing file
        // - first move the file
        // - then update the doc record to say processed

        public bool ProcessPdfFile(string fileName, string uniqName, bool bExtractImages, bool bDontOverwriteExistingImages, bool bExtractText, bool bRecogniseDoc,
                                bool bAddToDocInfoDb, bool bAddToDocPagesDb)
        {
            // First check if doc details are already in db
            if (!ScanDocInfoRecordExists(uniqName))
                return false;

            // Make a copy of the file in the archive location
            string archiveFileName = Path.Combine(Properties.Settings.Default.DocArchiveFolder, uniqName + ".pdf");
            if (!File.Exists(archiveFileName))
            {
                string statusStr = "";
                bool bResult = CopyFile(fileName, archiveFileName, ref statusStr);
                if (!bResult)
                {
                    logger.Error("Can't make archive copy {0} excp {1}", archiveFileName, statusStr);
                    return false;
                }
            }
            else
            {
                logger.Info("Archive file already exists {0}", archiveFileName);
            }

            // Extract text blocks from file
            ScanPages scanPages = new ScanPages(uniqName);
            int totalNumPages = 0;
            if (bExtractText)
            {
                PdfTextAndLocExtractor pdfExtractor = new PdfTextAndLocExtractor();
                scanPages = pdfExtractor.ExtractDocInfo(uniqName, fileName, _scanConfig._maxPagesForText, ref totalNumPages);
            }

            // Extract images from file
            if (bExtractImages)
            {
                bool procImages = (!bDontOverwriteExistingImages) | (!File.Exists(PdfRasterizer.GetFilenameOfImageOfPage(_scanConfig._docAdminImgFolderBase, uniqName, 1, false)));
                if (procImages)
                {
                    PdfRasterizer rs = new PdfRasterizer();
                    List<string> imgFileNames = rs.Start(fileName, uniqName, scanPages, _scanConfig._docAdminImgFolderBase, _scanConfig._maxPagesForImages);
                }
            }

            // Form partial document info
            DateTime fileDateTime = File.GetCreationTime(fileName);
            ScanDocInfo scanDocInfo = new ScanDocInfo(uniqName, totalNumPages, scanPages.scanPagesText.Count, fileDateTime, fileName.Replace('\\', '/'), false);

            // Add records to mongo databases
            if (bAddToDocInfoDb)
                AddDocInfoRecToMongo(scanDocInfo);
            if (bAddToDocPagesDb)
                AddScanPagesRecToMongo(scanPages);
 
            return true;
        }

        #endregion

        #region Utility Functions

        private static string ReplaceStringAnyCase(string input, string pattern, string replacement)
        {
            int pos = input.IndexOf(pattern, StringComparison.CurrentCultureIgnoreCase);
            if (pos >= 0)
                return input.Substring(0, pos) + replacement + input.Substring(pos + pattern.Length);
            return input;
        }

        private static string MakeValidFileName(string name)
        {
            return Path.GetInvalidFileNameChars().Aggregate(name, (current, c) => current.Replace(c.ToString(), string.Empty));
        }

        public static string FormatFileNameFromMacros(string origName, string renameTo, DateTime fileAsDateTime, string prefix, string suffix, string docTypeName)
        {
            string fileName = renameTo;
            if (fileName.Trim() == "")
                fileName = Properties.Settings.Default.DefaultRenameTo;
            string dateStr = string.Format("{0:yyyyMMdd}", fileAsDateTime);
            fileName = ReplaceStringAnyCase(fileName, "[YMD]", dateStr);
            string dateStr2 = string.Format("{0:yyyy-MM-dd}", fileAsDateTime);
            fileName = ReplaceStringAnyCase(fileName, "[Y-M-D]", dateStr2);
            string dateStrRev = string.Format("{0:ddMMyyyy}", fileAsDateTime);
            fileName = ReplaceStringAnyCase(fileName, "[DMY]", dateStrRev);
            string yearStr = string.Format("{0:yyyy}", fileAsDateTime);
            fileName = ReplaceStringAnyCase(fileName, "[YEAR]", yearStr);
            string monthStr = string.Format("{0:MM}", fileAsDateTime);
            fileName = ReplaceStringAnyCase(fileName, "[MONTH]", monthStr);
            string domStr = string.Format("{0:dd}", fileAsDateTime);
            fileName = ReplaceStringAnyCase(fileName, "[DAY]", domStr);
            fileName = ReplaceStringAnyCase(fileName, "[PREFIX]", prefix);
            fileName = ReplaceStringAnyCase(fileName, "[SUBJECT]", suffix);
            fileName = ReplaceStringAnyCase(fileName, "[DOCTYPE]", docTypeName);
            fileName = fileName.Trim();
            fileName = fileName + Path.GetExtension(origName);
            return MakeValidFileName(fileName);
        }

        public static bool CopyFile(string srcName, string destName, ref string errStr)
        {
            bool bResult = false;
            try
            {
                File.Copy(srcName, destName);
                bResult = true;
            }
            catch (Exception e)
            {
                errStr = e.Message;
                logger.Error("Failed to copy file {0} to {1} excp {2}", srcName, destName, e.Message);
            }
            return bResult;
        }

        public static List<MailAddress> GetEmailToAddresses()
        {
            List<MailAddress> emailTo = new List<MailAddress>();
            string[] ems = Properties.Settings.Default.EmailTo.Split(',');
            foreach (string em in ems)
            {
                MailAddress emai = new MailAddress(em);
                emailTo.Add(emai);
            }
            return emailTo;
        }

        #endregion
    }

    public class ScanDocHandlerConfig
    {
        public string _dbNameForDocs;
        public string _dbCollectionForDocInfo;
        public string _dbCollectionForDocPages;
        public string _dbCollectionForFiledDocs;
        public string _docAdminImgFolderBase;
        public int _maxPagesForImages = 0;
        public int _maxPagesForText = 0;

        public ScanDocHandlerConfig(string docAdminImgFolderBase,
                    int maxPagesForImages, int maxPagesForText, string dbNameForDocs, string dbCollectionForDocInfo, string dbCollectionForDocPages,
                    string dbCollectionForFiledDocs)
        {
            _docAdminImgFolderBase = docAdminImgFolderBase;
            _maxPagesForImages = maxPagesForImages;
            _maxPagesForText = maxPagesForText;
            _dbNameForDocs = dbNameForDocs;
            _dbCollectionForDocInfo = dbCollectionForDocInfo;
            _dbCollectionForDocPages = dbCollectionForDocPages;
            _dbCollectionForFiledDocs = dbCollectionForFiledDocs;
        }
    }
}
