using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using MongoDB.Driver.Builders;
using System.Threading;

namespace ScanMonitorApp
{
    class ScanFileMonitor
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private List<ScanFolderWatcher> _scanFolderWatchers;
        private List<string> _foldersToMonitor = new List<string>();
        private MongoClient _dbClient;
        public delegate void ReportStatus(string str);
        ReportStatus _reportStatusFn;
        private bool _monitorRunning = false;
        string _pendingDocFolder;
        string _pendingTmpFolder;
        int _maxPagesForImages = 0;
        int _maxPagesForText = 0;
        string _dbNameForDocs;
        string _dbCollectionForDocs;
        DateTime _lastTimeMongoDbConnErrLogged = DateTime.MinValue;

        public ScanFileMonitor(ReportStatus reportStatusFn)
        {
            _reportStatusFn = reportStatusFn;
        }

        public void Start(List<string> foldersToMonitor, string pendingDocFolder, string pendingTmpFolder, 
                    int maxPagesForImages, int maxPagesForText, string dbNameForDocs, string dbCollectionForDocs)
        {
            _pendingDocFolder = pendingDocFolder;
            _pendingTmpFolder = pendingTmpFolder;
            _maxPagesForImages = maxPagesForImages;
            _maxPagesForText = maxPagesForText;
            _dbNameForDocs = dbNameForDocs;
            _dbCollectionForDocs = dbCollectionForDocs;
            InitDatabase();
            MonitorFolders(foldersToMonitor);
            _monitorRunning = true;
        }

        public void Stop()
        {
            _monitorRunning = false;
        }

        public void InitDatabase()
        {
            // Mongo init
            try
            {
                var connectionString = "mongodb://localhost";
                _dbClient = new MongoClient(connectionString);
            }
            catch (Exception excp)
            {
                string excpStr = String.Format("Cannot connect to mongo {0}", excp.Message);
                logger.Error(excpStr);
                _reportStatusFn(excpStr);
            }
        }

        public int MonitorFolders(List<string> foldersToMonitor)
        {
            // Monitor folders for file creation
            int watcherCount = 0;
            _foldersToMonitor = foldersToMonitor;
            _scanFolderWatchers = new List<ScanFolderWatcher>();
            foreach (string folder in _foldersToMonitor)
            {
                ScanFolderWatcher watcher = new ScanFolderWatcher();
                if (watcher.WatchFolder(folder, WatchFolderChanged))
                {
                    _scanFolderWatchers.Add(watcher);
                    watcherCount++;
                }
            }
            return watcherCount;
        }

        public void WatchFolderChanged(string fileName, WatcherChangeTypes changeInfo)
        {
            MoveAndProcessPdfFile(fileName);
        }

        private bool CheckFileReadyToMove(string fileName)
        {
            // Check the file is writeable
            bool checkOk = false;
            try
            {
                if (File.Exists(fileName))
                {
                    using (FileStream f = new FileStream(fileName, FileMode.Open, FileAccess.Write, FileShare.None))
                    {
                        checkOk = true;
                    }
                }
            }
            catch
            {
                checkOk = false;
            }
            return checkOk;
        }

        public bool WaitForFileToBeMoveable(string fileName)
        {
            bool fileCheckOk = false;
            // Verify that the file contains text - wait until it does
            for (int checkIdx = 0; checkIdx < 20; checkIdx++)
            {
                // Check file is ready to be moved
                fileCheckOk = CheckFileReadyToMove(fileName);
                if (fileCheckOk)
                    break;

                // Sleep to allow the remote computer to finish processing
                Thread.Sleep(1000);
            }
            if (!fileCheckOk)
            {
                logger.Info("File {0} detected but cannot be moved", fileName);
                return false;
            }
            return true;
        }

        public bool MoveFile(string srcName, string destName, ref string errStr)
        {
            bool bResult = false;
            try
            {
                File.Move(srcName, destName);
                bResult = true;
            }
            catch (Exception e)
            {
                errStr = e.Message;
            }
            return bResult;
        }

        private string MoveFileToPendingFolder(string fileName)
        {
            // Move the file
            string statusStr = "";
            string destFileName = Path.Combine(_pendingDocFolder, Path.GetFileName(fileName));

            // Check if destination exists
            bool bMoveFile = true;
            try
            {
                if (File.Exists(destFileName))
                {
                    bMoveFile = false;
                    FileInfo fi1 = new FileInfo(fileName);
                    FileInfo fi2 = new FileInfo(destFileName);
                    if (fi1.Length == fi2.Length)
                    {
                        // Assume identical so delete
                        File.Delete(fileName);
                        logger.Info("A suspected duplicate file found {0} - deleted", fileName);
                    }
                    else
                    {
                        bMoveFile = true;
                        // Try to make the name unique
                        destFileName = Path.Combine(_pendingDocFolder, Path.GetFileNameWithoutExtension(fileName) + "_A", Path.GetExtension(fileName));
                        if (File.Exists(destFileName))
                        {
                            logger.Info("File {0} name conflicts with {1} cannot copy", fileName, destFileName);
                            bMoveFile = false;
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Error in testing file (0) duplicate {1}", fileName, excp.Message);
            }

            // Check if file to be moved
            if (bMoveFile)
            {
                bool bOk = MoveFile(fileName, destFileName, ref statusStr);
                if (!bOk)
                {
                    logger.Info("File {0} failed to move to pending, excp {1}", fileName, statusStr);
                    return "";
                }
                logger.Info("File {0} moved to pending ok", fileName);
            }
            return destFileName;
        }

        public void MoveAndProcessPdfFile(string fileName)
        {
            logger.Info("START {0}", fileName);

            // First check database connection is ok
            if (!CheckMongoConnection())
                return;

            logger.Info("HERE");

            // Wait until file is moveable - or time-out
            if (!WaitForFileToBeMoveable(fileName))
                return;

            logger.Info("THERE");

            // Move file to pending folder
            string destFileName = MoveFileToPendingFolder(fileName);
            if (destFileName == "")
                return;

            logger.Info("ANOTHER");

            // Process Pdf file
            ProcessPdfFile(destFileName);

            logger.Info("DONE");
        }

        public void ProcessPdfFile(string fileName)
        {
            // Extract images from file
            PdfRasterizer rs = new PdfRasterizer();
            List<string> imgFileNames = rs.Start(fileName, _pendingTmpFolder, _maxPagesForImages);

            // Extract text blocks from file
            PdfTextAndLocExtractor pdfExtractor = new PdfTextAndLocExtractor();
            ScanDocAllInfo scanDocAllInfo = pdfExtractor.ExtractDocInfo(fileName, _maxPagesForText);

            // Complete info
            scanDocAllInfo.scanPageImageFileBase = Path.Combine(_pendingTmpFolder, scanDocAllInfo.docName);

            // Add record to mongo database
            AddScanDocRecToMongo(scanDocAllInfo);
        }

        public void AddRecToMongo(string fileName)
        {
            // Create a record to indicate a file has been found and processing is pending
            var server = _dbClient.GetServer();
            var database = server.GetDatabase("ScanDocsDb"); // the name of the database
            var collection_sdfound = database.GetCollection<ScanDocFound>("scandocfound");

            // Check if record exists already
            MongoCursor<ScanDocFound> foundSdf = collection_sdfound.Find(Query.EQ("FileName", fileName));
            if (foundSdf.Count() == 0)
            {
                // Insert record for file
                ScanDocFound sdf = new ScanDocFound(fileName);
                collection_sdfound.Insert(sdf);
                logger.Info("DocFound added for {0}", fileName);
            }
            else
            {
                logger.Info("DocFound record already present for {0}", fileName);
            }
        }

        public bool CheckMongoConnection()
        {
            bool bOk = false;
            try
            {
                // Check connection active
                var server = _dbClient.GetServer();
                var database = server.GetDatabase(_dbNameForDocs); // the name of the database
                var collection_sdinfo = database.GetCollection<ScanDocAllInfo>(_dbCollectionForDocs);

                // Check if record exists already
                MongoCursor<ScanDocAllInfo> foundSdf = collection_sdinfo.Find(Query.EQ("docName", ""));
                foreach (ScanDocAllInfo foundRec in foundSdf)
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
                    logger.Error("Mongo db not connected {0} coll {1} excp {2}", _dbNameForDocs, _dbCollectionForDocs, excp.Message);
                    _lastTimeMongoDbConnErrLogged = DateTime.Now;
                }
            }
            return bOk;
        }

        public void AddScanDocRecToMongo(ScanDocAllInfo scanDocAllInfo)
        {

            // Mongo append
            try
            {
                var server = _dbClient.GetServer();
                var database = server.GetDatabase(_dbNameForDocs); // the name of the database
                var collection_sdinfo = database.GetCollection<ScanDocAllInfo>(_dbCollectionForDocs);
                collection_sdinfo.Insert(scanDocAllInfo);
                // Log it
                logger.Info("Added record for {0}", scanDocAllInfo.docName);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot insert rec into {0} Coll... {1} for file {2} excp {3}", 
                            _dbNameForDocs, _dbCollectionForDocs, scanDocAllInfo.docName,
                            excp.Message);
            }
        }

        public void FileMonitorThread()
        {
            while (_monitorRunning)
            {
                Thread.Sleep(5000);
            }

        }

    }
}
