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
    public class DocTypesMatcher
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

        public DocTypeMatchResult GetMatchingDocType(ScanPages scanPages)
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
                DocTypeMatchResult matchResult = CheckIfDocMatches(scanPages, doctype);
                if (matchResult.matchCertaintyPercent == 100)
                    return matchResult;
            }
            return new DocTypeMatchResult();
        }

        public DocTypeMatchResult CheckIfDocMatches(ScanPages scanPages, DocType docType)
        {
            // Setup check info
            DocTypeMatchResult matchResult = new DocTypeMatchResult();
            matchResult.matchCertaintyPercent = 0;
            if (docType.matchExpression == null)
                return matchResult;

            // Check the expression
            if (MatchAgainstDocText(docType.matchExpression, scanPages))
            {
                matchResult.matchCertaintyPercent = 100;
                matchResult.docTypeName = docType.docTypeName;
            }

            return matchResult;
        }

        public bool MatchAgainstDocText(string matchExpression, ScanPages scanPages)
        {
            StringTok st = new StringTok(matchExpression);
            return EvalMatch(st, scanPages);
        }

        public bool EvalMatch(StringTok st, ScanPages scanPages)
        {
            bool result = false;
            string token = "";
            bool curOpIsOr = true;
            bool opIsInverse = false;
            while((token = st.GetNextToken()) != null)
            {
                if (token == "")
                    continue;
                else if (token == ")")
                    return result;
                else if (token == "(")
                {
                    bool tmpRslt = EvalMatch(st, scanPages);
                    if (opIsInverse)
                        tmpRslt = !tmpRslt;
                    if (curOpIsOr)
                        result |= tmpRslt;
                    else
                        result &= tmpRslt;
                }
                else if (token == "&")
                    curOpIsOr = false;
                else if (token == "|")
                    curOpIsOr = true;
                else if (token == "!")
                    opIsInverse = true;
                else
                {
                    bool tmpRslt = MatchString(token, new DocRectangle(0, 0, 100, 100), scanPages);
                    if (opIsInverse)
                        tmpRslt = !tmpRslt;
                    if (curOpIsOr)
                        result |= tmpRslt;
                    else
                        result &= tmpRslt;
                }
            }
            return result;
        }

        public bool MatchString(string str, DocRectangle docRec, ScanPages scanPages)
        {
            for (int pageIdx = 0; pageIdx < scanPages.scanPagesText.Count; pageIdx++)
            {
                List<ScanTextElem> scanPageText = scanPages.scanPagesText[pageIdx];
                foreach (ScanTextElem textElem in scanPageText)
                {
                    // Check bounds
                    if (docRec.Contains(textElem.bounds))
                    {
                        if (textElem.text.IndexOf(str, StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
            }
            return false;
        }

        public class StringTok
        {
            public string[] tokens;
            public int curPos = 0;

            public StringTok (string inStr)
            {
                //char[] terms = new char[] {'(', ')', '&', '|' };
                //tokens = inStr.Split(terms);
                string pattern = @"(\()|(\))|(\&)|(\|)|(\!)";
                tokens = Regex.Split(inStr, pattern);
                curPos = 0;
            }

            public string GetNextToken()
            {
                if (curPos >= tokens.Length)
                    return null;
                return tokens[curPos++];
            }
        }

        public string ListDocTypesJson()
        {
            // Get list of documents
            List<DocType> docTypeList = ListDocTypes();
            return JsonConvert.SerializeObject(docTypeList);
        }

        public List<DocType> ListDocTypes()
        {
            // Get list of documents
            MongoCollection<DocType> collection_doctypes = GetDocTypesCollection();
            return collection_doctypes.FindAll().ToList<DocType>();
        }

        public string GetDocTypeJson(string docTypeName)
        {
            return JsonConvert.SerializeObject(GetDocType(docTypeName));
        }

        public DocType GetDocType(string docTypeName)
        {
            // Get first matching document
            MongoCollection<DocType> collection_doctypes = GetDocTypesCollection();
            return collection_doctypes.FindOne(Query.EQ("docTypeName", docTypeName));
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

        public void ApplyAliasList(string origDocType, string newDocType)
        {
            // read list from file - or database??
            // use list generated when doc types read in - prob in database
            // when doc types read in use info like [georgeheriots] to indicate GHS - 
            // add an enable / disable field to database and set in UI to turn on / off rules??
            // 
        }
    }
}
