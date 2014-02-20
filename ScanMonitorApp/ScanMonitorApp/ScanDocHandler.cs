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

        private MongoCollection<ScanDocAllInfo> GetAllInfoCollection()
        {
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_dbNameForDocs); // the name of the database
            return database.GetCollection<ScanDocAllInfo>(_dbCollectionForDocs);
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
            scanDocAllInfo.scanDocInfo.scanPageImageFileBase = Path.Combine(_pendingTmpFolder, scanDocAllInfo.scanDocInfo.docName).Replace('\\','/');

            // Find matching doc type
            DocTypeMatchResult docTypeMatchResult = _docTypesMatcher.GetMatchingDocType(scanDocAllInfo);
            scanDocAllInfo.scanDocInfo.docTypeFiled = docTypeMatchResult.docTypeName;
            scanDocAllInfo.scanDocInfo.docDateFiled = docTypeMatchResult.docDate;
            scanDocAllInfo.scanDocInfo.docTypeMatchResult = docTypeMatchResult;

            // Add record to mongo database
            AddScanDocRecToMongo(scanDocAllInfo);
        }

        public bool CheckMongoConnection()
        {
            bool bOk = false;
            try
            {
                // Check connection active
                MongoCollection<ScanDocAllInfo> collection_sdinfo = GetAllInfoCollection();

                // Check if record exists already
                MongoCursor <ScanDocAllInfo> foundSdf = collection_sdinfo.Find(Query.EQ("docName", ""));
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
                MongoCollection<ScanDocAllInfo> collection_sdinfo = GetAllInfoCollection();
                collection_sdinfo.Insert(scanDocAllInfo);
                // Log it
                logger.Info("Added scandoc record for {0}", scanDocAllInfo.scanDocInfo.docName);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot insert scandoc rec into {0} Coll... {1} for file {2} excp {3}",
                            _dbNameForDocs, _dbCollectionForDocs, scanDocAllInfo.scanDocInfo.docName,
                            excp.Message);
            }
        }

        public string ListScanDocs()
        {
            // Get list of documents
            MongoCollection<ScanDocAllInfo> collection_sdinfo = GetAllInfoCollection();
            MongoCursor<ScanDocAllInfo> scanDocs = collection_sdinfo.FindAll();
            List<ScanDocInfo> scanDocInfo = new List<ScanDocInfo>();
            foreach (ScanDocAllInfo docInfo in scanDocs)
                scanDocInfo.Add(docInfo.scanDocInfo);
            return JsonConvert.SerializeObject(scanDocInfo);
        }

        public string GetScanDoc(string scanDocUniqName)
        {
            // Get first matching documents
            MongoCollection<ScanDocAllInfo> collection_sdinfo = GetAllInfoCollection();
            ScanDocAllInfo scanDoc = collection_sdinfo.FindOne(Query.EQ ( "scanDocInfo.docName", scanDocUniqName ) );
            return JsonConvert.SerializeObject(scanDoc);
        }
    }
}
