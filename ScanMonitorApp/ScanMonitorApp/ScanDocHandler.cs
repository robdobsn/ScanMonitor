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

            // Extract images from file
            if (bExtractImages)
            {
                bool procImages = (!bDontOverwriteExistingImages) | (!File.Exists(PdfRasterizer.GetFilenameOfImageOfPage(_scanConfig._docAdminImgFolderBase, uniqName, 1, false)));
                if (procImages)
                {
                    PdfRasterizer rs = new PdfRasterizer();
                    List<string> imgFileNames = rs.Start(fileName, uniqName, _scanConfig._docAdminImgFolderBase, _scanConfig._maxPagesForImages);
                }
            }

            // Extract text blocks from file
            ScanPages scanPages = new ScanPages(uniqName, new List<List<ScanTextElem>>());
            int totalNumPages = 0;
            if (bExtractText)
            {
                PdfTextAndLocExtractor pdfExtractor = new PdfTextAndLocExtractor();
                scanPages = pdfExtractor.ExtractDocInfo(uniqName, fileName, _scanConfig._maxPagesForText, ref totalNumPages);
            }

            // Form partial document info
            DateTime fileDateTime = File.GetCreationTime(fileName);
            ScanDocInfo scanDocInfo = new ScanDocInfo(uniqName, totalNumPages, scanPages.scanPagesText.Count, fileDateTime);

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

        public void AddFiledDocRecToMongo(FiledDocInfo filedDocInfo)
        {
            // Mongo append
            try
            {
                MongoCollection<FiledDocInfo> collection_fileddoc = GetFiledDocsCollection();
                collection_fileddoc.Insert(filedDocInfo);
                // Log it
                logger.Info("Added scandocpages record for {0}", filedDocInfo.uniqName);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot insert scandocpages into {0} Coll... {1} for file {2} excp {3}",
                            _scanConfig._dbNameForDocs, _scanConfig._dbCollectionForFiledDocs, filedDocInfo.uniqName,
                            excp.Message);
            }
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

        public string GetScanDocJson(string uniqName)
        {
            // Get first matching documents
            MongoCollection<ScanDocInfo> collection_sdinfo = GetDocInfoCollection();
            ScanDocInfo scanDoc = collection_sdinfo.FindOne(Query.EQ("uniqName", uniqName));
            return JsonConvert.SerializeObject(scanDoc);
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
