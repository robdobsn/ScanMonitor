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
using System.Windows.Media;

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

        public bool Setup()
        {
            try
            {
                var collection_doctypes = GetDocTypesCollection();
                collection_doctypes.EnsureIndex(new IndexKeysBuilder()
                            .Ascending("docTypeName"), IndexOptions.SetUnique(true));
                return true;
            }
            catch (Exception excp)
            {
                logger.Error("Failed to add index DB may not be started, excp {0}", excp.Message);
            }
            return false;
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
            // Get list of types
            var collection_doctypes = GetDocTypesCollection();
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
            matchResult.matchResultCode = DocTypeMatchResult.MatchResultCodes.NOT_FOUND;
            if (!docType.isEnabled)
            {
                matchResult.matchResultCode = DocTypeMatchResult.MatchResultCodes.DISABLED;
                return matchResult;
            }
            if (docType.matchExpression == null)
            {
                matchResult.matchResultCode = DocTypeMatchResult.MatchResultCodes.NO_EXPR;
                return matchResult;
            }

            // Check the expression
            if (MatchAgainstDocText(docType.matchExpression, scanPages))
            {
                matchResult.matchCertaintyPercent = 100;
                matchResult.matchResultCode = DocTypeMatchResult.MatchResultCodes.FOUND_MATCH;
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
            DocRectangle docRectPercent = new DocRectangle(0, 0, 100, 100);
            int docRectValIdx = 0;
            while((token = st.GetNextToken()) != null)
            {
                if (token.Trim() == "")
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
                    // Check for location on empty string
                    if (token == "{")
                        return result;
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
                                docRectPercent.SetVal(docRectValIdx, rectVal);
                            }
                        }
                    }

                    // Process the match string using the location rectangle
                    if (stringToMatch.Trim().Length >= 0)
                    {
                        bool tmpRslt = MatchString(stringToMatch, docRectPercent, scanPages);
                        if (opIsInverse)
                            tmpRslt = !tmpRslt;
                        if (curOpIsOr)
                            result |= tmpRslt;
                        else
                            result &= tmpRslt;
                    }

                    // Set the docRect to the entire page (ready for next term)
                    docRectPercent = new DocRectangle(0,0,100,100);
                }
            }
            return result;
        }

        public bool MatchString(string str, DocRectangle docRectPercent, ScanPages scanPages)
        {
            for (int pageIdx = 0; pageIdx < scanPages.scanPagesText.Count; pageIdx++)
            {
                List<ScanTextElem> scanPageText = scanPages.scanPagesText[pageIdx];
                foreach (ScanTextElem textElem in scanPageText)
                {
                    // Check bounds
                    if (docRectPercent.Intersects(textElem.bounds))
                    {
                        if (textElem.text.IndexOf(str.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
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
                string pattern = @"(\()|(\))|(\&)|(\|)|(\!)|(\{)|(\})|(\,)";
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

        public bool AddOrUpdateDocTypeRecInDb(DocType docType)
        {
            // Mongo append
            try
            {
                MongoCollection<DocType> collection_docTypes = GetDocTypesCollection();
                collection_docTypes.Save(docType, SafeMode.True);
                // Log it
                logger.Info("Added/updated doctype record for {0}", docType.docTypeName);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot insert doctype rec into {0} Coll... {1} for file {2} excp {3}",
                            _dbNameForDocTypes, _dbCollectionForDocTypes, docType.docTypeName,
                            excp.Message);
                return false;
            }
            return true;
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
            if (s.Length > 0)
                parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Text, lastTxtStartIdx, chIdx-lastTxtStartIdx, curBracketDepth, 0));
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
            int locationBracketIdx = 0;
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
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Brackets, chIdx, 1, curBracketDepth++, 0));
                            break;
                        }
                    case ')':
                        {
                            // Add text upto this point
                            lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);

                            // Add bracket
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Brackets, chIdx, 1, --curBracketDepth, 0));
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
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_LocationBrackets, chIdx, 1, curBracketDepth++, locationBracketIdx));
                            lastLocStartIdx = chIdx + 1;
                            break;
                        }
                    case '}':
                        {
                            // Add location text and closing bracket
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Location, lastLocStartIdx, chIdx - lastLocStartIdx, curBracketDepth, locationBracketIdx));
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_LocationBrackets, chIdx, 1, --curBracketDepth, locationBracketIdx));
                            if ((cursorPosForBracketMatching == chIdx) || (cursorPosForBracketMatching == chIdx + 1))
                                matchBracketDepth = curBracketDepth;
                            lastTxtStartIdx = chIdx + 1;
                            locationBracketIdx++;
                            break;
                        }
                    case '&':
                    case '|':
                    case '!':
                        {
                            // Add text upto this point
                            lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);

                            // Add operator
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Operator, chIdx, 1, curBracketDepth, locationBracketIdx));
                            lastTxtStartIdx = chIdx + 1;
                            break;
                        }
                }
            }
            lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, matchExpression.Length, curBracketDepth);
            return parseTermsList;
        }
    }

    public class ExprParseTerm
    {
        private static Brush[] locBrushes = new Brush[]
                {
                    new SolidColorBrush(Colors.Pink),
                    new SolidColorBrush(Colors.Goldenrod),
                    new SolidColorBrush(Colors.BurlyWood),
                    new SolidColorBrush(Colors.Indigo),
                    new SolidColorBrush(Colors.Chartreuse),
                    new SolidColorBrush(Colors.Coral)
                };
        public ExprParseTerm(ExprParseTermType type, int pos, int leng, int brackDepth, int locBrackIdx)
        {
            termType = type;
            stPos = pos;
            termLen = leng;
            bracketDepth = brackDepth;
            locationBracketIdx = locBrackIdx;
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
        public Brush GetBrush()
        {
            if (termType == ExprParseTermType.exprTerm_Location)
                return GetLocationBrush(locationBracketIdx);
            return GetTermTypeBrush();
        }
        public static Brush GetBrushForLocationIdx(int locIdx)
        {
            return GetLocationBrush(locIdx);
        }
        private static Brush GetLocationBrush(int locIdx)
        {
            int idx = locIdx % locBrushes.Length;
            return locBrushes[idx];
        }
        private Brush GetTermTypeBrush()
        {
            switch (termType)
            {
                case ExprParseTermType.exprTerm_LocationBrackets: { return new SolidColorBrush(Colors.Blue); }
                case ExprParseTermType.exprTerm_Brackets: { return new SolidColorBrush(Colors.Green); }
                case ExprParseTermType.exprTerm_Text: { return new SolidColorBrush(Colors.Black); }
                case ExprParseTermType.exprTerm_Operator: { return new SolidColorBrush(Colors.Chocolate); }
            }
            return new SolidColorBrush(Colors.Black);
        }
        public int stPos = 0;
        public int termLen = 0;
        public ExprParseTermType termType = ExprParseTermType.exprTerm_None;
        public bool matchingBracket = false;
        public int bracketDepth = 0;
        public int locationBracketIdx = 0;
    }

}
