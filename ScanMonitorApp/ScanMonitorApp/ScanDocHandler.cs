using System;
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

namespace ScanMonitorApp
{
    public class ScanDocHandler
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        public delegate void ReportStatus(string str);
        private ReportStatus _reportStatusFn;
        private DateTime _lastTimeMongoDbConnErrLogged = DateTime.MinValue;
        private MongoClient _dbClient;
        private DocTypesMatcher _docTypesMatcher;
        private ScanDocHandlerConfig _scanConfig;

        public ScanDocHandler(ReportStatus reportStatusFn, DocTypesMatcher docTypesMatcher, ScanDocHandlerConfig scanConfig)
        {
            _reportStatusFn = reportStatusFn;
            _docTypesMatcher = docTypesMatcher;
            _scanConfig = scanConfig;
            InitDatabase();
        }

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

        private MongoCollection<ScanDocInfo> GetDocInfoCollection()
        {
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_scanConfig._dbNameForDocs); // the name of the database
            return database.GetCollection<ScanDocInfo>(_scanConfig._dbCollectionForDocInfo);
        }

        private MongoCollection<ScanPages> GetDocPagesCollection()
        {
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_scanConfig._dbNameForDocs); // the name of the database
            return database.GetCollection<ScanPages>(_scanConfig._dbCollectionForDocPages);
        }

        private MongoCollection<FiledDocInfo> GetFiledDocsCollection()
        {
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_scanConfig._dbNameForDocs); // the name of the database
            return database.GetCollection<FiledDocInfo>(_scanConfig._dbCollectionForFiledDocs);
        }

            // when processing file
            // - first move the file
            // - then update the doc record to say processed

        public void ProcessPdfFile(string fileName, string uniqName, bool bExtractImages, bool bDontOverwriteExistingImages, bool bExtractText, bool bRecogniseDoc, 
                                bool bAddToDocInfoDb, bool bAddToDocPagesDb)
        {
            // First check if doc details are already in db
            if (!CheckOkToAddToDb(uniqName))
                return;

            return;

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
            ScanDocInfo scanDocInfo = new ScanDocInfo(uniqName, totalNumPages, scanPages.scanPagesText.Count, fileDateTime, fileName.Replace('\\', '/'));

            // Find matching doc type
            if (bRecogniseDoc)
            {
                DocTypeMatchResult docTypeMatchResult = _docTypesMatcher.GetMatchingDocType(scanPages);
                scanDocInfo.docTypeMatchResult = docTypeMatchResult;
            }

            // Add records to mongo databases
            if (bAddToDocInfoDb)
                AddDocInfoRecToMongo(scanDocInfo);
            if (bAddToDocPagesDb)
                AddDocPagesRecToMongo(scanPages);
        }

        public bool CheckOkToAddToDb(string uniqName)
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

        public void AddDocPagesRecToMongo(ScanPages scanPages)
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

        public ScanPages GetScanPages(string uniqName)
        {
            MongoCollection<ScanPages> collection_spages = GetDocPagesCollection();
            return collection_spages.FindOne(Query.EQ("uniqName", uniqName));
        }

        public ScanDocAllInfo GetScanDocAllInfo(string uniqName)
        {   
            // Get first matching documents
            MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();
            ScanDocInfo scanDoc = collection_sdinfo.FindOne(Query.EQ("uniqName", uniqName));
            MongoCollection<ScanPages> collection_spages = GetDocPagesCollection();
            ScanPages scanPages = collection_spages.FindOne(Query.EQ("uniqName", uniqName));
            MongoCollection<FiledDocInfo> collection_filedinfo = GetFiledDocsCollection();
            FiledDocInfo filedDocInfo = collection_filedinfo.FindOne(Query.EQ("uniqName", uniqName));
            return new ScanDocAllInfo(scanDoc, scanPages, filedDocInfo);
        }

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

            return (int) (scannedCount - filedCount);
        }

        public bool DeleteFile(string uniqName, string fullSourceName)
        {
            // Delete the physical file
            bool fileDeleteOk = false;
            string errStr = "";
            if (fullSourceName != "")
            {
                try
                {
                    // Check file exists
                    if (!File.Exists(fullSourceName))
                        return true;

                    File.Delete(fullSourceName);
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
            FiledDocInfo fdi = new FiledDocInfo(uniqName, "", DateTime.Now, "", "Deleted", "Deleted", false, FiledDocInfo.DocFinalStatus.STATUS_DELETED);
            return AddFiledDocRecToMongo(fdi);
        }

        public bool CheckOkToFileDoc(ScanDocInfo scanDocInfo, DocType docType, DateTime dateTimeStamp, out string rsltText)
        {
            // Check path is valid
            if (docType.moveFileToPath.Trim() == "")
            {
                rsltText = "DocType path is blank";
                return false;
            }

            // Get the fully expanded path
            bool pathContainsMacros = false;
            string destPath = _docTypesMatcher.ComputeExpandedPath(docType.moveFileToPath, dateTimeStamp, false, ref pathContainsMacros);

            // Create folder if the path contains macros
            try
            {
                if (!Directory.Exists(destPath) && pathContainsMacros)
                    Directory.CreateDirectory(destPath);
            }
            catch
            {
            }

            // Check folder exists
            if (!Directory.Exists(destPath))
            {
                rsltText = "DocType folder not found";
                return false;
            }

            //// Check file name validity
            //string fileNameToCheck = FormFileName(GetCurrentlySelectedPath(), fileNewName.Text);
            //try
            //{
            //    System.IO.FileInfo fi = new System.IO.FileInfo(fileNameToCheck);
            //    if (Path.GetFileNameWithoutExtension(fi.FullName) == "")
            //    {
            //        MessageBox.Show("The file name cannot be blank.", "Dest Name Invalid", MessageBoxButtons.OK);
            //        return;
            //    }
            //}
            //catch (ArgumentException e1)
            //{
            //    MessageBox.Show("The destination file name is invalid. " + e1.Message, "Dest Name Invalid", MessageBoxButtons.OK);
            //    return;
            //}
            //catch (System.IO.PathTooLongException e2)
            //{
            //    MessageBox.Show("The destination file path is invalid. " + e2.Message, "Dest Name Invalid", MessageBoxButtons.OK);
            //    return;
            //}
            //catch (NotSupportedException e3)
            //{
            //    MessageBox.Show("The destination file name is invalid. " + e3.Message, "Dest Name Invalid", MessageBoxButtons.OK);
            //    return;
            //}

            //// Check if dest file exists
            //if (File.Exists(fileNameToCheck))
            //{
            //    MessageBox.Show("A destination file of this name already exists", "Dest Name Invalid", MessageBoxButtons.OK);
            //    return;
            //}

            rsltText = "Checked Ok";
            return true;
        }

        public void StartProcessFilingOfDoc(out string rsltText)
        {
            rsltText = "";
        }

        private string ReplaceStringAnyCase(string input, string pattern, string replacement)
        {
            int pos = input.IndexOf(pattern, StringComparison.CurrentCultureIgnoreCase);
            if (pos >= 0)
                return input.Substring(0, pos) + replacement + input.Substring(pos + pattern.Length);
            return input;
        }

        private static string MakeValidFileName(string name)
        {
            name = name.Replace(@"\r\n", " ");
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string invalidReStr = string.Format(@"[{0}]+", invalidChars);
            return Regex.Replace(name, invalidReStr, "_");
        }

        public string FormatFileNameFromMacros(string origName, string renameTo, DateTime fileAsDateTime, string prefix, string suffix, string docTypeName)
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
