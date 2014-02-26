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
            DocRectangle docRect = new DocRectangle(0, 0, 100, 100);
            int docRectValIdx = 0;
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
                    // We've reached a terminal token (string to match to text in the document)
                    string stringToMatch = token;
                    // See if there is a location defined by the next token
                    while ((st.PeekNextToken() != null) && (st.PeekNextToken() == ""))
                        st.GetNextToken();
                    if ((st.PeekNextToken() != null) && (st.PeekNextToken() == "{"))
                    {
                        while ((token = st.GetNextToken()) != null)
                        {
                            if (token == "")
                                continue;
                            else if (token == "{")
                                docRectValIdx = 0;
                            else if (token == ",")
                                docRectValIdx++;
                            else if (token == "}")
                                break;
                            else
                            {
                                double rectVal = Double.Parse(token);
                                docRect.SetVal(docRectValIdx, rectVal);
                            }
                        }
                    }

                    // Process the match string using the location rectangle
                    bool tmpRslt = MatchString(stringToMatch, docRect, scanPages);
                    if (opIsInverse)
                        tmpRslt = !tmpRslt;
                    if (curOpIsOr)
                        result |= tmpRslt;
                    else
                        result &= tmpRslt;

                    // Set the docRect to the entire page (ready for next term)
                    docRect = new DocRectangle(0,0,100,100);
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

            public string PeekNextToken()
            {
                if (curPos >= tokens.Length)
                    return null;
                return tokens[curPos];
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

        public int ParserAddTextToPoint(List<ExprParseTerm> parseTermsList, string matchExpression, int lastTxtStartIdx, int chIdx, int curBracketDepth)
        {
            string s = matchExpression.Substring(lastTxtStartIdx, chIdx-lastTxtStartIdx);
            if (s.Trim().Length > 0)
                parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Text, lastTxtStartIdx, chIdx-lastTxtStartIdx, curBracketDepth));
            return chIdx + 1;
        }

        public List<ExprParseTerm> ParseDocMatchExpression(string matchExpression, int cursorPosForBracketMatching)
        {
            // Go through the matchExpression finding parse terms
            List<ExprParseTerm> parseTermsList = new List<ExprParseTerm>();
            int curBracketDepth = 0;
            int matchBracketDepth = -1;
            int lastLocStartIdx = 0;
            int lastTxtStartIdx = 0;
            for (int chIdx = 0; chIdx < matchExpression.Length; chIdx++)
            {
                switch (matchExpression[chIdx])
                {
                    case '(':
                        {
                            // Add text upto this point
                            lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);

                            // Add bracket
                            if ((cursorPosForBracketMatching == chIdx) || (cursorPosForBracketMatching == chIdx + 1))
                                matchBracketDepth = curBracketDepth;
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Brackets, chIdx, 1, curBracketDepth++));
                            break;
                        }
                    case ')':
                        {
                            // Add text upto this point
                            lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);

                            // Add bracket
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Brackets, chIdx, 1, --curBracketDepth));
                            if ((cursorPosForBracketMatching == chIdx) || (cursorPosForBracketMatching == chIdx + 1))
                                matchBracketDepth = curBracketDepth;
                            break;
                        }
                    case '{':
                        {
                            // Add text upto this point
                            lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);

                            // Add location bracket
                            if ((cursorPosForBracketMatching == chIdx) || (cursorPosForBracketMatching == chIdx + 1))
                                matchBracketDepth = curBracketDepth;
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_LocationBrackets, chIdx, 1, curBracketDepth++));
                            lastLocStartIdx = chIdx + 1;
                            break;
                        }
                    case '}':
                        {
                            // Add location text and closing bracket
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Location, lastLocStartIdx, chIdx - lastLocStartIdx, curBracketDepth));
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_LocationBrackets, chIdx, 1, --curBracketDepth));
                            if ((cursorPosForBracketMatching == chIdx) || (cursorPosForBracketMatching == chIdx + 1))
                                matchBracketDepth = curBracketDepth;
                            lastTxtStartIdx = chIdx + 1;
                            break;
                        }
                    case '&':
                    case '|':
                    case '!':
                        {
                            // Add text upto this point
                            lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);

                            // Add operator
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Operator, chIdx, 1, curBracketDepth));
                            lastTxtStartIdx = chIdx + 1;
                            break;
                        }
                }
            }
            lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, matchExpression.Length, curBracketDepth);
            return parseTermsList;
        }

        public class ExprParseTerm
        {
            public ExprParseTerm(ExprParseTermType type, int pos, int leng, int brackDepth)
            {
                termType = type;
                stPos = pos;
                termLen = leng;
                bracketDepth = brackDepth;
            }

            public enum ExprParseTermType
            {
                exprTerm_None,
                exprTerm_LocationBrackets,
                exprTerm_Location,
                exprTerm_Text,
                exprTerm_Operator,
                exprTerm_Brackets,
            }
            public int stPos = 0;
            public int termLen = 0;
            public ExprParseTermType termType = ExprParseTermType.exprTerm_None;
            public bool matchingBracket = false;
            public int bracketDepth = 0;
        }

    }
}
