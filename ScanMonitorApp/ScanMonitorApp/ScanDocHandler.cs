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
        private string _dbCollectionForDocInfo;
        private string _dbCollectionForDocPages;
        public delegate void ReportStatus(string str);
        private ReportStatus _reportStatusFn;
        private DateTime _lastTimeMongoDbConnErrLogged = DateTime.MinValue;
        private string _pendingDocFolder;
        private string _docAdminImgFolderBase;
        private int _maxPagesForImages = 0;
        private int _maxPagesForText = 0;
        private MongoClient _dbClient;
        private DocTypesMatcher _docTypesMatcher;

        public ScanDocHandler(ReportStatus reportStatusFn, DocTypesMatcher docTypesMatcher, string pendingDocFolder, string docAdminImgFolderBase,
                    int maxPagesForImages, int maxPagesForText, string dbNameForDocs, string dbCollectionForDocInfo, string dbCollectionForDocPages)
        {
            _reportStatusFn = reportStatusFn;
            _docTypesMatcher = docTypesMatcher;
            _pendingDocFolder = pendingDocFolder;
            _docAdminImgFolderBase = docAdminImgFolderBase;
            _maxPagesForImages = maxPagesForImages;
            _maxPagesForText = maxPagesForText;
            _dbNameForDocs = dbNameForDocs;
            _dbCollectionForDocInfo = dbCollectionForDocInfo;
            _dbCollectionForDocPages = dbCollectionForDocPages;
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

        private MongoCollection<ScanDocInfo> GetDocInfoCollection()
        {
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_dbNameForDocs); // the name of the database
            return database.GetCollection<ScanDocInfo>(_dbCollectionForDocInfo);
        }

        private MongoCollection<ScanPages> GetDocPagesCollection()
        {
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_dbNameForDocs); // the name of the database
            return database.GetCollection<ScanPages>(_dbCollectionForDocPages);
        }


            // when processing file
            // - first move the file
            // - then update the doc record to say processed

        public string GetImageFolderForFile(string uniqName)
        {
            // Get a subfolder name to use for processing the images from the file
            // Assuming the uniq name is the date in YYYYMMDD format the subfolder will be YYYYMM
            string subFolderName = uniqName;
            subFolderName.Replace("_", "");
            subFolderName = subFolderName.Substring(0, 6);
            return Path.Combine(_docAdminImgFolderBase, subFolderName).Replace('\\', '/');
        }

        public void ProcessPdfFile(string fileName)
        {
            // Get unique name for file
            string uniqName = ScanDocInfo.GetUniqNameForFile(fileName);

            // First check if doc details are already in db
            if (!CheckOkToAddToDb(uniqName))
                return;

            // Get folder to process images to
            string imageFolder = GetImageFolderForFile(uniqName);

            // Extract images from file
            PdfRasterizer rs = new PdfRasterizer();
            List<string> imgFileNames = rs.Start(fileName, _docAdminImgFolderBase, _maxPagesForImages);

            // Extract text blocks from file
            PdfTextAndLocExtractor pdfExtractor = new PdfTextAndLocExtractor();
            ScanDocAllInfo scanDocAllInfo = pdfExtractor.ExtractDocInfo(uniqName, fileName, _maxPagesForText);

            // Complete info
            scanDocAllInfo.scanDocInfo.scanPageImageFileBase = Path.Combine(GetImageFolderForFile(scanDocAllInfo.scanDocInfo.uniqName), fileName).Replace('\\', '/');

            // Find matching doc type
            DocTypeMatchResult docTypeMatchResult = _docTypesMatcher.GetMatchingDocType(scanDocAllInfo);
            scanDocAllInfo.scanDocInfo.docTypeFiled = docTypeMatchResult.docTypeName;
            scanDocAllInfo.scanDocInfo.docDateFiled = docTypeMatchResult.docDate;
            scanDocAllInfo.scanDocInfo.docTypeMatchResult = docTypeMatchResult;

            // Add record to mongo database
            AddScanDocRecToMongo(scanDocAllInfo);
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
                    logger.Error("Mongo db error checking doc exists {0} coll {1} excp {2}", _dbNameForDocs, _dbCollectionForDocInfo, excp.Message);
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
                    logger.Error("Mongo db not connected {0} coll {1} excp {2}", _dbNameForDocs, _dbCollectionForDocInfo, excp.Message);
                    _lastTimeMongoDbConnErrLogged = DateTime.Now;
                }
            }
            return bOk;
        }

        public void AddScanDocRecToMongo(ScanDocAllInfo scanDocAllInfo)
        {
            AddDocInfoRecToMongo(scanDocAllInfo.scanDocInfo);
            AddDocPagesRecToMongo(scanDocAllInfo.scanPages);
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
                            _dbNameForDocs, _dbCollectionForDocInfo, scanDocInfo.uniqName,
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
                            _dbNameForDocs, _dbCollectionForDocPages, scanPages.uniqName,
                            excp.Message);
            }
        }

        public string ListScanDocs()
        {
            // Get list of documents
            MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();
            MongoCursor<ScanDocInfo> scanDocs = collection_sdinfo.FindAll();
            List<ScanDocInfo> scanDocInfo = new List<ScanDocInfo>();
            foreach (ScanDocInfo docInfo in scanDocs)
                scanDocInfo.Add(docInfo);
            return JsonConvert.SerializeObject(scanDocInfo);
        }

        public string GetScanDoc(string scanDocUniqName)
        {
            // Get first matching documents
            MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();
            ScanDocInfo scanDoc = collection_sdinfo.FindOne(Query.EQ ( "uniqName", scanDocUniqName ) );
            return JsonConvert.SerializeObject(scanDoc);
        }
    }
}
