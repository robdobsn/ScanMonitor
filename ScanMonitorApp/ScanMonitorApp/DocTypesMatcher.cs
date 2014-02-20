using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using MongoDB.Driver.Builders;

namespace ScanMonitorApp
{
    class DocTypesMatcher
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private MongoClient _dbClient;
        private string _dbNameForDocTypes;
        private string _dbCollectionForDocTypes;

        public DocTypesMatcher(string dbNameForDocTypes, string dbCollectionForDocTypes)
        {
            _dbCollectionForDocTypes = dbCollectionForDocTypes;
            _dbNameForDocTypes = dbNameForDocTypes;
            var connectionString = "mongodb://localhost";
            _dbClient = new MongoClient(connectionString);
        }

        private MongoCollection<DocType> GetDocTypesCollection()
        {
            // Setup db connection
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_dbNameForDocTypes); // the name of the database
            return database.GetCollection<DocType>(_dbCollectionForDocTypes);
        }

        public DocTypeMatchResult GetMatchingDocType(ScanDocAllInfo scanDocAllInfo)
        {
            // Setup db connection
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(_dbNameForDocTypes); // the name of the database
            var collection_doctypes = database.GetCollection<DocType>(_dbCollectionForDocTypes);

            // Get list of types
            MongoCursor<DocType> foundSdf = collection_doctypes.FindAll();
            foreach (DocType doctype in foundSdf)
            {
                // Check if document matches
                DocTypeMatchResult matchResult = CheckIfDocMatches(scanDocAllInfo, doctype);
                if (matchResult.matchesMustHaveTexts && matchResult.matchesMustNotHaveTexts)
                    return matchResult;
            }
            return new DocTypeMatchResult();
        }

        public bool MatchAgainstDocText(DocPatternText patternText, List<ScanPageText> scanPages)
        {
            bool bFound = false;
            for (int pageIdx = 0; pageIdx < scanPages.Count; pageIdx++)
            {
                ScanPageText scanPageText = scanPages[pageIdx];
                foreach (ScanTextElem textElem in scanPageText.textElems)
                {
                    // Check bounds
                    if (patternText.textBounds.Contains(textElem.bounds))
                    {
                        Match match = Regex.Match(textElem.text, patternText.textToMatch, RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            bFound = true;
                            break;
                        }
                    }
                }
                if (bFound)
                    break;
            }
            return bFound;
        }

        public DocTypeMatchResult CheckIfDocMatches(ScanDocAllInfo scanDocAllInfo, DocType docType)
        {
            // Setup check info
            DocTypeMatchResult matchResult = new DocTypeMatchResult();
            matchResult.matchesMustHaveTexts = (docType.mustHaveTexts.Count > 0) && (scanDocAllInfo.scanDocInfo.numPagesWithText > 0);
            matchResult.matchesMustNotHaveTexts = true;

            // Check strings that must be in file to match
            foreach (DocPatternText patternText in docType.mustHaveTexts)
            {
                if (!MatchAgainstDocText(patternText, scanDocAllInfo.scanPages))
                {
                    // If any pattern isn't matched then doc doesn't match type
                    matchResult.matchesMustHaveTexts = false;
                    break;
                }
            }

            // Check strings that Must NOT be in the file
            foreach (DocPatternText patternText in docType.mustNotHaveTexts)
            {
                if (MatchAgainstDocText(patternText, scanDocAllInfo.scanPages))
                {
                    // If any pattern does match then doc doesn't match type
                    matchResult.matchesMustNotHaveTexts = false;
                    break;
                }
            }
            return matchResult;
        }

        public string ListDocTypes()
        {
            // Get list of documents
            MongoCollection<DocType> collection_doctypes = GetDocTypesCollection();
            List<DocType> docTypeList = collection_doctypes.FindAll().ToList<DocType>();
            return JsonConvert.SerializeObject(docTypeList);
        }

        public string GetDocType(string docTypeName)
        {
            // Get first matching document
            MongoCollection<DocType> collection_doctypes = GetDocTypesCollection();
            DocType docType = collection_doctypes.FindOne(Query.EQ("docTypeName", docTypeName));
            return JsonConvert.SerializeObject(docType);
        }

        public void AddDocTypeRecToMongo(DocType docType)
        {
            // Mongo append
            try
            {
                MongoCollection<DocType> collection_docTypes = GetDocTypesCollection();
                collection_docTypes.Insert(docType);
                // Log it
                logger.Info("Added doctype record for {0}", docType.docTypeName);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot insert doctype rec into {0} Coll... {1} for file {2} excp {3}",
                            _dbNameForDocTypes, _dbCollectionForDocTypes, docType.docTypeName,
                            excp.Message);
            }
        }

        // Test code
        public void AddOldDocTypes(string filename)
        {
            ScanMan.OldXmlRulesManager xmlRulesManager = new ScanMan.OldXmlRulesManager(filename);
            List<ScanMan.OldXmlRulesManager.DocType> oldDocTypeList = xmlRulesManager.GetAllDocTypes();
            foreach (ScanMan.OldXmlRulesManager.DocType oldDocType in oldDocTypeList)
            {
                DocType newDocType = new DocType();
                newDocType.docTypeName = oldDocType.dtName;
                newDocType.mustHaveTexts = new List<DocPatternText>();
                foreach (ScanMan.OldXmlRulesManager.CheckItem chkItem in oldDocType.goodStrings)
                    newDocType.mustHaveTexts.Add(new DocPatternText(chkItem.checkString, new DocRectangle(0, 0, 100, 100)));
                newDocType.mustNotHaveTexts = new List<DocPatternText>();
                foreach (ScanMan.OldXmlRulesManager.CheckItem chkItem in oldDocType.badStrings)
                    newDocType.mustNotHaveTexts.Add(new DocPatternText(chkItem.checkString, new DocRectangle(0, 0, 100, 100)));
                if (oldDocType.thumbFileNames.Count > 0)
                    newDocType.thumbnailForDocType = oldDocType.thumbFileNames[0].Replace('\\', '/');
                else
                    newDocType.thumbnailForDocType = "";
                AddDocTypeRecToMongo(newDocType);
            }
        }
    }
}
