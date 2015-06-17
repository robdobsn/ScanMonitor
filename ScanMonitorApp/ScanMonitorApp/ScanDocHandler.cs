using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using MongoDB.Driver;
using System.IO;
using MongoDB.Driver.Builders;
using MongoDB.Driver.Linq;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Threading;
using System.Net.Mail;
using System.Net;
using System.Security.Cryptography;

namespace ScanMonitorApp
{
    public class ScanDocHandler
    {
        const int MAX_FILE_PROCESS_RETRIES = 3;
        const int TIME_BETWEEN_RETRIES_SECS = 5;
        const int THUMBNAIL_POINTS_PER_INCH = 150;

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
        // Cache used only to speed up doc-type checking - circumvents database multiuser so don't rely on results! 100%
        private ScanDocInfoCache _scanDocInfoCache = null;
        private ScanDocLikelyDocType _scanDocLikelyDocType = null;
        private List<string> _lastNDocTypesFiled = new List<string>();

        #region Init

        public ScanDocHandler(ReportStatus reportStatusFn, DocTypesMatcher docTypesMatcher, ScanDocHandlerConfig scanConfig, string dbConnectionStr)
        {
            // Setup
            _reportStatusFn = reportStatusFn;
            _docTypesMatcher = docTypesMatcher;
            _scanConfig = scanConfig;

            // Init db
            InitDatabase(dbConnectionStr);

            // Create info cache etc
            _scanDocInfoCache = new ScanDocInfoCache(this);
            _scanDocLikelyDocType = new ScanDocLikelyDocType(this, _docTypesMatcher);

            // Init the background worker used for processing docs
            _fileProcessBkgndWorker.WorkerSupportsCancellation = true;
            _fileProcessBkgndWorker.WorkerReportsProgress = true;
            _fileProcessBkgndWorker.DoWork += new DoWorkEventHandler(FileProcessDoWork);
            _fileProcessBkgndWorker.ProgressChanged += new ProgressChangedEventHandler(FileProcessProgressChanged);
            _fileProcessBkgndWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(FileProcessRunWorkerCompleted);

        }

        #endregion

        #region Database Common Code

        public void InitDatabase(string dbConnectionStr)
        {
            // Mongo init
            try
            {
                _dbClient = new MongoClient(dbConnectionStr);
            }
            catch (Exception excp)
            {
                string excpStr = String.Format("Cannot connect to mongo {0}", excp.Message);
                logger.Error(excpStr);
                _reportStatusFn(excpStr);
            }

            // Setup indexes
            MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();
            collection_sdinfo.EnsureIndex(new IndexKeysBuilder().Ascending("uniqName"), IndexOptions.SetUnique(true));
            MongoCollection<ScanPages> collection_spages = GetDocPagesCollection();
            collection_spages.EnsureIndex(new IndexKeysBuilder().Ascending("uniqName"), IndexOptions.SetUnique(true));
            MongoCollection<FiledDocInfo> collection_fdinfo = GetFiledDocsCollection();
            collection_fdinfo.EnsureIndex(new IndexKeysBuilder().Ascending("uniqName"), IndexOptions.SetUnique(true));
            MongoCollection<ExistingFileInfoRec> collection_existingFiles = GetExistingFileInfoCollection();
            collection_existingFiles.EnsureIndex(new IndexKeysBuilder().Ascending("filename"), IndexOptions.SetUnique(false));
            collection_existingFiles.EnsureIndex(new IndexKeysBuilder().Ascending("md5Hash"), IndexOptions.SetUnique(false));
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
                ScanDocInfo foundRec = foundSdf.First();
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

        public ScanDocAllInfo GetScanDocAllInfoCached(string uniqName)
        {
            return _scanDocInfoCache.GetScanDocAllInfo(uniqName);
        }

        public MongoClient GetMongoClient()
        {
            return _dbClient;
        }

        #endregion

        #region Scan Sw Settings

        public MongoCollection<ScanSwSettings> GetScanSwSettingsCollection()
        {
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_scanConfig._dbNameForDocs); // the name of the database
            return database.GetCollection<ScanSwSettings>(Properties.Settings.Default.DbCollectionForSwSettings);
        }

        public string GetEmailPassword()
        {
            // Get first matching document
            MongoCollection<ScanSwSettings> collection_swsettings = GetScanSwSettingsCollection();
            ScanSwSettings swSettings = collection_swsettings.FindOne();
            if (swSettings == null)
                return "";
            return swSettings._emailPassword;
        }

        public bool SetEmailPassword(string pw)
        {
            // Mongo save
            try
            {
                ScanSwSettings settings = new ScanSwSettings(pw);
                MongoCollection<ScanSwSettings> collection_swsettings = GetScanSwSettingsCollection();
                collection_swsettings.RemoveAll();
                collection_swsettings.Save(settings);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot update password in db {0}", excp.Message);
                return false;
            }
            return true;
        }

        #endregion

        #region ScanDocInfo Collection Handling

        public MongoCollection<ScanDocInfo> GetDocInfoCollection()
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

                // Update cache
                _scanDocInfoCache.UpdateDocInfo(scanDocInfo.uniqName);
                _scanDocLikelyDocType.UpdateDocInfo(scanDocInfo.uniqName);
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
                collection_sdinfo.Save(scanDocInfo);
                // Log it
                logger.Info("Added/updated scandocinfo record for {0}", scanDocInfo.uniqName);
                // Update cache
                _scanDocInfoCache.UpdateDocInfo(scanDocInfo.uniqName);
                _scanDocLikelyDocType.UpdateDocInfo(scanDocInfo.uniqName);
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

        public MongoCollection<FiledDocInfo> GetFiledDocsCollection()
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
                // Update cache
                _scanDocInfoCache.UpdateDocInfo(filedDocInfo.uniqName);
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
                collection_fdinfo.Save(filedDocInfo);
                // Log it
                logger.Info("Added/updated fileddoc record for {0} filedAt {1}", filedDocInfo.uniqName, filedDocInfo.filedAs_pathAndFileName);
                // Update cache
                _scanDocInfoCache.UpdateDocInfo(filedDocInfo.uniqName);
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

        public List<string> GetLastNDocTypesUsed(int numToReturn)
        {
            // Use cached copy if it isn't empty
            List<string> docTypesList = new List<string>();
            if (_lastNDocTypesFiled.Count == 0)
            {
                try
                {
                    MongoCollection<FiledDocInfo> collection_fdinfo = GetFiledDocsCollection();
                    List<string> partres = (from fd in collection_fdinfo.AsQueryable<FiledDocInfo>() orderby fd.filedAt_dateAndTime descending select (string)fd.filedAs_docType).Take<string>(200).ToList<string>();
                    partres = partres.Select(s => s).Distinct<string>().ToList<string>();
                    docTypesList = partres.Take(numToReturn).ToList<string>();
                }
                catch (Exception excp)
                {
                    logger.Error("Cannot get list of last used doctypes {0}", excp.Message);
                }
                _lastNDocTypesFiled = docTypesList;
            }
            else
            {
                docTypesList = _lastNDocTypesFiled.Take(numToReturn).ToList<string>();
            }
            return docTypesList;
        }

        #endregion

        #region Existing File Hash Information

        public MongoCollection<ExistingFileInfoRec> GetExistingFileInfoCollection()
        {
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_scanConfig._dbNameForDocs); // the name of the database
            return database.GetCollection<ExistingFileInfoRec>(_scanConfig._dbCollectionForExistingFiles);
        }

        public bool AddExistingFileRecToMongo(ExistingFileInfoRec infoRec)
        {
            // Mongo append
            bool bOk = false;
            try
            {
                MongoCollection<ExistingFileInfoRec> collection_existingFile = GetExistingFileInfoCollection();
                WriteConcernResult rslt = collection_existingFile.Insert(infoRec);
                bOk = rslt.Ok;
                // Log it
                //logger.Info("Added existing-file-info record for {0}", System.IO.Path.GetFileName(infoRec.filename));
            }
            catch (Exception excp)
            {
                logger.Error("Cannot insert existing-file-info into {0} Coll... {1} for file {2} excp {3}",
                            _scanConfig._dbNameForDocs, _scanConfig._dbCollectionForExistingFiles, infoRec.filename,
                            excp.Message);
            }
            return bOk;
        }

        public void EmptyExistingFileRecDB()
        {
            try
            {
                MongoCollection<ExistingFileInfoRec> collection_existingFile = GetExistingFileInfoCollection();
                collection_existingFile.Drop();
                // Log it
                logger.Info("Emptied existing file db");
            }
            catch (Exception excp)
            {
                logger.Error("Cannot empty existing-file db");
            }
        }

        public List<ExistingFileInfoRec> FindExistingFileRecsByHash(byte[] md5Hash)
        {
            MongoCollection<ExistingFileInfoRec> collection_existingFile = GetExistingFileInfoCollection();
            List<ExistingFileInfoRec> efirList = collection_existingFile.Find(Query.EQ("md5Hash", md5Hash)).ToList();
            return efirList;
        }

        #endregion

        #region Unfiled Document Handling

        public List<string> GetCopyOfUnfiledDocsList()
        {
            return _scanDocInfoCache.GetListOfUnfiledDocUniqNames();
        }

        public int GetCountOfUnfiledDocs()
        {
            return _scanDocInfoCache.GetCountOfUnfiledDocs();
        }

        public string GetHashOfUnfiledDocs()
        {
            return _scanDocInfoCache.GetHashOfUnfiledDocs();
        }

        public string GetUniqNameOfDocToBeFiled(int docIdx)
        {
            return _scanDocLikelyDocType.GetUniqNameOfDocToBeFiled(docIdx, Properties.Settings.Default.UnfiledDocListOrder);
        }

        public void DocTypeAddedOrChanged(string nameOfDocTypeAddedOrChanged)
        {
            _scanDocLikelyDocType.DocTypeAddedOrChanged(nameOfDocTypeAddedOrChanged);
        }

        public void RemoveDocFromUnfiledCache(string uniqName, string docTypeFiledAs)
        {
            _scanDocInfoCache.RemoveDocFromUnfiledCache(uniqName);
            // Add to last N doc types list
            _lastNDocTypesFiled.Remove(docTypeFiledAs);
            _lastNDocTypesFiled.Insert(0, docTypeFiledAs);
        }

        #endregion

        #region Delete File

        public bool DeleteFile(string uniqName, FiledDocInfo fdi, string fullSourceName, bool dueToFileEditing)
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
                        fileDeleteOk = true;
                    }
                    else if (Properties.Settings.Default.TestModeFileTo == "")
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
            fdi.SetDeletedInfo(dueToFileEditing);

            return AddOrUpdateFiledDocRecInDb(fdi);
        }

        #endregion

        #region Validity Checking before Filing

        public bool CheckOkToFileDoc(string destDocPathAndFileName, out string rsltText)
        {
            // Check for invalid strings
            rsltText = "Dest folder invalid";
            if (destDocPathAndFileName.Trim() == "")
                return false;

            // Get folder
            string destPathOnly = "";
            try
            {
                destPathOnly = Path.GetDirectoryName(destDocPathAndFileName);
            }
            catch
            {
                return false;
            }

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
            fdi.SetFiledAtInfo(true, DateTime.Now, "OK", dfs);
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

            // Update the doc list cache
            RemoveDocFromUnfiledCache(fdi.uniqName, fdi.filedAs_docType);
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
                    string archiveFileName = ScanDocHandler.GetArchiveFileName(sdi.uniqName);
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
                        // Special case code here to handle a bug that resulted in origFileName being in the archive folder
                        string origName = System.IO.Path.GetDirectoryName(sdi.origFileName).ToLower();
                        string testName = Properties.Settings.Default.DocArchiveFolder.ToLower();
                        if (testName == origName)
                        {
                            string removalFileName = Path.Combine(@"\\SCAN2\Users\Rob\Documents\ScanSnap", Path.GetFileName(sdi.origFileName));
                            File.Delete(removalFileName);
                        }
                        else
                        {
                            File.Delete(sdi.origFileName);
                        }
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
            string archiveFileName = ScanDocHandler.GetArchiveFileName(uniqName);
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
                    PdfRasterizer rs = new PdfRasterizer(fileName, THUMBNAIL_POINTS_PER_INCH);
                    try
                    {
                        List<string> imgFileNames = rs.GeneratePageFiles(uniqName, scanPages, _scanConfig._docAdminImgFolderBase, _scanConfig._maxPagesForImages, false);
                    }
                    finally
                    {
                        rs.Close();
                    }

                }
            }

            // Form partial document info
            DateTime fileDateTime = File.GetCreationTime(fileName);
            ScanDocInfo scanDocInfo = new ScanDocInfo(uniqName, totalNumPages, scanPages.scanPagesText.Count, fileDateTime, fileName.Replace('\\', '/'), false);

            // Add records to mongo databases
            if (bAddToDocPagesDb)
                AddScanPagesRecToMongo(scanPages);
            if (bAddToDocInfoDb)
                AddDocInfoRecToMongo(scanDocInfo);

            // Request update to unfiled documents list
            _scanDocInfoCache.RequestUnfiledListUpdate();

            return true;
        }

        #endregion

        #region Utility Functions

        public static string GetArchiveFileName(string uniqName)
        {
            return Path.Combine(Properties.Settings.Default.DocArchiveFolder, uniqName + ".pdf");
        }

        public static byte[] GenHashOnFileExcludingMetadata(string filename, out long fileLen)
        {
            bool md5CreatedOk = false;
            byte[] md5Val = new byte[0];
            byte[] pdfImageTagBytes = Encoding.ASCII.GetBytes("<</Subtype/Image/Length");
            using (var md5 = MD5.Create())
            {
                try
                {
                    FileInfo finfo = new FileInfo(filename);
                    fileLen = finfo.Length;
                    if (finfo.Length < 100000000)
                    {
                        byte[] fileData = File.ReadAllBytes(filename);
                        bool completeMatch = false;
                        int matchPos = 0;
                        // Find the tell-tale PDF scanned file string of bytes
                        for (int testPos = 0; testPos < fileData.Length - pdfImageTagBytes.Length; testPos++)
                        {
                            completeMatch = true;
                            for (int i = 0; i < pdfImageTagBytes.Length; i++)
                                if (fileData[testPos + i] != pdfImageTagBytes[i])
                                {
                                    completeMatch = false;
                                    break;
                                }
                            if (completeMatch)
                            {
                                matchPos = testPos;
                                string extractedText = Encoding.UTF8.GetString(fileData, matchPos + pdfImageTagBytes.Length, 20);
                                Match match = Regex.Match(extractedText, @"\s*?(\d+)",RegexOptions.IgnoreCase);
                                if (match.Success)
                                {
                                    // Finally, we get the Group value and display it.
                                    int imgLen = 0;
                                    bool rslt = Int32.TryParse(match.Groups[1].Value, out imgLen);
                                    if (rslt && (imgLen < fileData.Length - matchPos - 100))
                                    {
                                        md5Val = md5.ComputeHash(fileData, matchPos, imgLen);
                                        md5CreatedOk = true;
                                    }
                                }
                                break;
                            }
                        }

                    }

                    if (!md5CreatedOk)
                    {
                        using (var stream = File.OpenRead(filename))
                        {
                            md5Val = md5.ComputeHash(stream);
                            md5CreatedOk = true;
                        }
                    }
                }
                finally
                {

                }
            }
            if (md5CreatedOk)
                return md5Val;
            return new byte[0];
        }

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

        public static string GetScanDocInfoText(ScanDocInfo scanDocInfo)
        {
            string scanDocStr = "UniqName:\t" + scanDocInfo.uniqName;
            scanDocStr += "\n" + "Pages:\t\t" + scanDocInfo.numPages.ToString();
            scanDocStr += "\n" + "PagesWithText:\t" + scanDocInfo.numPagesWithText.ToString();
            scanDocStr += "\n" + "CreateDate:\t" + scanDocInfo.createDate.ToShortDateString() + " " + scanDocInfo.createDate.ToShortTimeString();
            scanDocStr += "\n" + "OrigFileName:\t" + scanDocInfo.origFileName;
            scanDocStr += "\n" + "FlagForHelp:\t" + (scanDocInfo.flagForHelpFiling ? "YES" : "NO");
            return scanDocStr;
        }

        public static string GetFiledDocInfoText(FiledDocInfo filedDocInfo)
        {
            string filedDocStr = "Unfiled";
            if (filedDocInfo != null)
            {
                filedDocStr = "UniqName:\t" + filedDocInfo.uniqName;
                filedDocStr += "\n" + "FiledAt:\t\t" + filedDocInfo.filedAt_dateAndTime.ToShortDateString() + " " + filedDocInfo.filedAt_dateAndTime.ToShortTimeString();
                filedDocStr += "\n" + "FinalStatus:\t" + filedDocInfo.filedAt_finalStatus;
                filedDocStr += "\n" + "ErrorMessage:\t" + filedDocInfo.filedAt_errorMsg;
                if (filedDocInfo.filedAs_pathAndFileName != "")
                {
                    string tmpStr = "";
                    try
                    {
                        tmpStr += "\n" + "FiledToName:\t" + System.IO.Path.GetFileName(filedDocInfo.filedAs_pathAndFileName);
                        tmpStr += "\n" + "FiledToPath:\t" + System.IO.Path.GetDirectoryName(filedDocInfo.filedAs_pathAndFileName);
                        filedDocStr += tmpStr;
                    }
                    finally
                    {
                    }
                }
                if (filedDocInfo.filedAt_finalStatus == FiledDocInfo.DocFinalStatus.STATUS_FILED)
                {
                    filedDocStr += "\n" + "FiledDocType:\t" + filedDocInfo.filedAs_docType;
                    filedDocStr += "\n" + "FiledDocDate:\t" + filedDocInfo.filedAs_dateOfDoc.ToShortDateString() + " " + filedDocInfo.filedAs_dateOfDoc.ToShortTimeString();
                    filedDocStr += "\n" + "FiledMoney:\t" + filedDocInfo.filedAs_moneyInfo;
                }
                filedDocStr += "\n" + "FlagRefiling:\t" + filedDocInfo.filedAs_flagForRefilingInfo;
                filedDocStr += "\n" + "IncludeInXCheck:\t" + (filedDocInfo.includeInXCheck ? "YES" : "NO");
                filedDocStr += "\n" + "FollowUp:\t" + filedDocInfo.filedAs_followUpNeeded;
                filedDocStr += "\n" + "AddToCal:\t" + filedDocInfo.filedAs_addToCalendar;
                if (filedDocInfo.filedAs_addToCalendar != "")
                {
                    filedDocStr += "\n" + "EventName:\t" + filedDocInfo.filedAs_eventName;
                    filedDocStr += "\n" + "EventDateTime:\t" + filedDocInfo.filedAs_eventDateTime.ToShortDateString() + " " + filedDocInfo.filedAs_eventDateTime.ToShortTimeString();
                    filedDocStr += "\n" + "EventDuration:\t" + filedDocInfo.filedAs_eventDuration.ToString();
                    filedDocStr += "\n" + "EventDescr:\t" + filedDocInfo.filedAs_eventDescr;
                    filedDocStr += "\n" + "EventLocn:\t" + filedDocInfo.filedAs_eventLocation;
                    filedDocStr += "\n" + "AttachFile:\t" + filedDocInfo.filedAs_flagAttachFile;
                }
            }
            return filedDocStr;
        }

        public static void ShowFileInExplorer(string fileName, bool selParent = true)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "explorer";
            psi.UseShellExecute = true;
            psi.WindowStyle = ProcessWindowStyle.Normal;
            psi.Arguments = string.Format("/e,{1}\"{0}\"", fileName, selParent ? "/select," : "");
            Process.Start(psi);
        }

        #endregion
    }

    public class ScanDocHandlerConfig
    {
        public string _dbNameForDocs;
        public string _dbCollectionForDocInfo;
        public string _dbCollectionForDocPages;
        public string _dbCollectionForFiledDocs;
        public string _dbCollectionForExistingFiles;
        public string _docAdminImgFolderBase;
        public int _maxPagesForImages = 0;
        public int _maxPagesForText = 0;

        public ScanDocHandlerConfig(string docAdminImgFolderBase,
                    int maxPagesForImages, int maxPagesForText, string dbNameForDocs, string dbCollectionForDocInfo, string dbCollectionForDocPages,
                    string dbCollectionForFiledDocs, string dbCollectionForExistingFiles)
        {
            _docAdminImgFolderBase = docAdminImgFolderBase;
            _maxPagesForImages = maxPagesForImages;
            _maxPagesForText = maxPagesForText;
            _dbNameForDocs = dbNameForDocs;
            _dbCollectionForDocInfo = dbCollectionForDocInfo;
            _dbCollectionForDocPages = dbCollectionForDocPages;
            _dbCollectionForFiledDocs = dbCollectionForFiledDocs;
            _dbCollectionForExistingFiles = dbCollectionForExistingFiles;
        }
    }
}
