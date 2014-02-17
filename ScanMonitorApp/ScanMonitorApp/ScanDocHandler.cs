using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using MongoDB.Driver;
using System.IO;
using MongoDB.Driver.Builders;

namespace ScanMonitorApp
{
    class ScanDocHandler
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private string _dbNameForDocs;
        private string _dbCollectionForDocs;
        public delegate void ReportStatus(string str);
        private ReportStatus _reportStatusFn;
        private DateTime _lastTimeMongoDbConnErrLogged = DateTime.MinValue;
        private string _pendingDocFolder;
        private string _pendingTmpFolder;
        private int _maxPagesForImages = 0;
        private int _maxPagesForText = 0;
        private MongoClient _dbClient;
        private DocTypesMatcher _docTypesMatcher;

        public ScanDocHandler(ReportStatus reportStatusFn, DocTypesMatcher docTypesMatcher, string pendingDocFolder, string pendingTmpFolder,
                    int maxPagesForImages, int maxPagesForText, string dbNameForDocs, string dbCollectionForDocs)
        {
            _reportStatusFn = reportStatusFn;
            _docTypesMatcher = docTypesMatcher;
            _pendingDocFolder = pendingDocFolder;
            _pendingTmpFolder = pendingTmpFolder;
            _maxPagesForImages = maxPagesForImages;
            _maxPagesForText = maxPagesForText;
            _dbNameForDocs = dbNameForDocs;
            _dbCollectionForDocs = dbCollectionForDocs;
            InitDatabase();
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

        public void ProcessPdfFile(string fileName)
        {
            // First check database connection is ok
            if (!CheckMongoConnection())
                return;

            // Extract images from file
            PdfRasterizer rs = new PdfRasterizer();
            List<string> imgFileNames = rs.Start(fileName, _pendingTmpFolder, _maxPagesForImages);

            // Extract text blocks from file
            PdfTextAndLocExtractor pdfExtractor = new PdfTextAndLocExtractor();
            ScanDocAllInfo scanDocAllInfo = pdfExtractor.ExtractDocInfo(fileName, _maxPagesForText);

            // Complete info
            scanDocAllInfo.scanPageImageFileBase = Path.Combine(_pendingTmpFolder, scanDocAllInfo.docName);

            // Find matching doc type
            DocTypeMatchResult docTypeMatchResult = _docTypesMatcher.GetMatchingDocType(scanDocAllInfo);
            scanDocAllInfo.docType = docTypeMatchResult.docTypeName;
            scanDocAllInfo.docTypeMatchResult = docTypeMatchResult;

            // Add record to mongo database
            AddScanDocRecToMongo(scanDocAllInfo);
        }

        public void AddRecToMongo(string fileName)
        {
            // Create a record to indicate a file has been found and processing is pending
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_dbNameForDocs); // the name of the database
            var collection_sdfound = database.GetCollection<ScanDocFound>(_dbCollectionForDocs);

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

    }
}
