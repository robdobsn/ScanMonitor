﻿using MongoDB.Driver;
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

        #region Init

        public DocTypesMatcher()
        {
            var connectionString = Properties.Settings.Default.DbConnectionString;
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

        #endregion

        #region Check Docs against DocTypes

        public DocTypeMatchResult CheckIfDocMatches(ScanPages scanPages, DocType docType, bool extractDates)
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
            double matchFactorTotal = 0;
            if (MatchAgainstDocText(docType.matchExpression, scanPages, ref matchFactorTotal))
            {
                matchResult.matchCertaintyPercent = 100;
                matchResult.matchResultCode = DocTypeMatchResult.MatchResultCodes.FOUND_MATCH;
            }
            matchResult.docTypeName = docType.docTypeName;
            matchResult.matchFactor = matchFactorTotal;

            // Extract date
            if (extractDates)
            {
                List<ExtractedDate> extractedDates = DocTextAndDateExtractor.ExtractDatesFromDoc(scanPages, docType.dateExpression);
                matchResult.datesFoundInDoc = extractedDates;
                if (extractedDates.Count > 0)
                    matchResult.docDate = extractedDates[0].dateTime;
            }

            return matchResult;
        }

        private bool MatchAgainstDocText(string matchExpression, ScanPages scanPages, ref double matchFactorTotal)
        {
            StringTok st = new StringTok(matchExpression);
            return EvalMatch(st, scanPages, ref matchFactorTotal);
        }

        private bool EvalMatch(StringTok st, ScanPages scanPages, ref double matchFactorTotal)
        {
            bool result = false;
            string token = "";
            bool curOpIsOr = true;
            bool opIsInverse = false;
            DocRectangle docRectPercent = new DocRectangle(0, 0, 100, 100);
            int docRectValIdx = 0;
            double matchFactorForTerm = 0;
            while((token = st.GetNextToken()) != null)
            {
                if (token.Trim() == "")
                    continue;
                else if (token == ")")
                    return result;
                else if (token == "(")
                {
                    bool tmpRslt = EvalMatch(st, scanPages, ref matchFactorTotal);
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

                    // Check for matchFactor - must have some text before it
                    if (token == ":")
                        return result;
                    // See if there is a location defined by the next token
                    while ((st.PeekNextToken() != null) && (st.PeekNextToken() == ""))
                        st.GetNextToken();
                    if ((st.PeekNextToken() != null) && (st.PeekNextToken() == ":"))
                    {
                        matchFactorForTerm = 0;
                        st.GetNextToken();
                        while ((st.PeekNextToken() != null) && (st.PeekNextToken() == ""))
                            st.GetNextToken();
                        token = st.GetNextToken();
                        if (token != null)
                        {
                            try
                            {
                                matchFactorForTerm = Double.Parse(token);
                            }
                            catch
                            {
                            }
                        }
                    }

                    // Check for location on empty string
                    if (token == "{")
                        return result;
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
                                double rectVal = 0;
                                try
                                {
                                    rectVal = Double.Parse(token);
                                }
                                catch
                                {
                                }
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

                        // Handle match factor
                        if (tmpRslt)
                            matchFactorTotal += matchFactorForTerm;
                    }

                    // Set the docRect to the entire page (ready for next term)
                    docRectPercent = new DocRectangle(0,0,100,100);
                    matchFactorForTerm = 0;
                }
            }
            return result;
        }

        private bool MatchString(string str, DocRectangle docRectPercent, ScanPages scanPages)
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

        private class StringTok
        {
            public string[] tokens;
            public int curPos = 0;

            public StringTok (string inStr)
            {
                string pattern = @"(\()|(\))|(\&)|(\|)|(\!)|(\{)|(\})|(\,)|(\:)";
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

        #endregion

        #region DocTypes Database Access

        private MongoCollection<DocType> GetDocTypesCollection()
        {
            // Setup db connection
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(Properties.Settings.Default.DbNameForDocs); // the name of the database
            return database.GetCollection<DocType>(Properties.Settings.Default.DbCollectionForDocTypes);
        }

        public DocTypeMatchResult GetMatchingDocType(ScanPages scanPages, int maxMatchesToReturn = 1, List<DocTypeMatchResult> listOfPossibleMatches = null)
        {
            // Get list of types
            DocTypeMatchResult bestMatchResult = new DocTypeMatchResult();
            var collection_doctypes = GetDocTypesCollection();
            MongoCursor<DocType> foundSdf = collection_doctypes.FindAll();
            foreach (DocType doctype in foundSdf)
            {
                // Check if document matches
                DocTypeMatchResult matchResult = CheckIfDocMatches(scanPages, doctype, false);
                if (matchResult.matchCertaintyPercent == 100)
                {
                    // Redo match to get date and time info
                    matchResult = CheckIfDocMatches(scanPages, doctype, true);
                }

                // Find the best match
                if (bestMatchResult.matchCertaintyPercent <= matchResult.matchCertaintyPercent)
                    if (bestMatchResult.matchFactor < matchResult.matchFactor)
                        bestMatchResult = matchResult;

                // Check if a list is to be returned
                if (listOfPossibleMatches != null)
                {
                    if (listOfPossibleMatches.Count < maxMatchesToReturn)
                        listOfPossibleMatches.Add(matchResult);
                }

            }

            // If no exact match get date info from entire doc
            if (bestMatchResult.matchCertaintyPercent != 100)
            {
                List<ExtractedDate> extractedDates = DocTextAndDateExtractor.ExtractDatesFromDoc(scanPages, "");
                bestMatchResult.datesFoundInDoc = extractedDates;
                if (extractedDates.Count > 0)
                    bestMatchResult.docDate = extractedDates[0].dateTime;
            }
            return bestMatchResult;
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
                            Properties.Settings.Default.DbNameForDocs, Properties.Settings.Default.DbCollectionForDocTypes, docType.docTypeName,
                            excp.Message);
                return false;
            }
            return true;
        }

        #endregion

        #region Parse DocMatch Expression for Text Highlighting

        public static List<ExprParseTerm> ParseDocMatchExpression(string matchExpression, int cursorPosForBracketMatching)
        {
            // Go through the matchExpression finding parse terms
            List<ExprParseTerm> parseTermsList = new List<ExprParseTerm>();
            int curBracketDepth = 0;
            int matchBracketDepth = -1;
            int lastLocStartIdx = 0;
            int lastTxtStartIdx = 0;
            int locationBracketIdx = 0;
            bool bInMatchFactorTerm = false;
            for (int chIdx = 0; chIdx < matchExpression.Length; chIdx++)
            {
                switch (matchExpression[chIdx])
                {
                    case '(':
                        {
                            // Add text upto this point
                            if (bInMatchFactorTerm)
                                lastTxtStartIdx = ParserAddMatchFactor(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);
                            else
                                lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);
                            bInMatchFactorTerm = false;

                            // Add bracket
                            if ((cursorPosForBracketMatching == chIdx) || (cursorPosForBracketMatching == chIdx + 1))
                                matchBracketDepth = curBracketDepth;
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Brackets, chIdx, 1, curBracketDepth++, 0));
                            break;
                        }
                    case ')':
                        {
                            // Add text upto this point
                            if (bInMatchFactorTerm)
                                lastTxtStartIdx = ParserAddMatchFactor(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);
                            else
                                lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);
                            bInMatchFactorTerm = false;

                            // Add bracket
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Brackets, chIdx, 1, --curBracketDepth, 0));
                            if ((cursorPosForBracketMatching == chIdx) || (cursorPosForBracketMatching == chIdx + 1))
                                matchBracketDepth = curBracketDepth;
                            break;
                        }
                    case '{':
                        {
                            // Add text upto this point
                            if (bInMatchFactorTerm)
                                lastTxtStartIdx = ParserAddMatchFactor(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);
                            else
                                lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);
                            bInMatchFactorTerm = false;

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
                    case ':':
                        {
                            lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);
                            lastTxtStartIdx = chIdx;
                            bInMatchFactorTerm = true;
                            break;
                        }
                    case '&':
                    case '|':
                    case '!':
                        {
                            // Add text upto this point
                            if (bInMatchFactorTerm)
                                lastTxtStartIdx = ParserAddMatchFactor(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);
                            else
                                lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);
                            bInMatchFactorTerm = false;

                            // Add operator
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Operator, chIdx, 1, curBracketDepth, locationBracketIdx));
                            lastTxtStartIdx = chIdx + 1;
                            break;
                        }
                }
            }
            if (bInMatchFactorTerm)
                lastTxtStartIdx = ParserAddMatchFactor(parseTermsList, matchExpression, lastTxtStartIdx, matchExpression.Length, curBracketDepth);
            else
                lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, matchExpression.Length, curBracketDepth);
            return parseTermsList;
        }

        private static int ParserAddMatchFactor(List<ExprParseTerm> parseTermsList, string matchExpression, int lastTxtStartIdx, int chIdx, int curBracketDepth)
        {
            string s = matchExpression.Substring(lastTxtStartIdx, chIdx - lastTxtStartIdx);
            if (s.Length > 0)
                parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_MatchFactor, lastTxtStartIdx, chIdx - lastTxtStartIdx, curBracketDepth, 0));
            return chIdx + 1;
        }

        private static int ParserAddTextToPoint(List<ExprParseTerm> parseTermsList, string matchExpression, int lastTxtStartIdx, int chIdx, int curBracketDepth)
        {
            string s = matchExpression.Substring(lastTxtStartIdx, chIdx - lastTxtStartIdx);
            if (s.Length > 0)
                parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Text, lastTxtStartIdx, chIdx - lastTxtStartIdx, curBracketDepth, 0));
            return chIdx + 1;
        }

        #endregion

        #region Substitution Macros (Paths)

        private MongoCollection<PathSubstMacro> GetPathSubstCollection()
        {
            // Setup db connection
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(Properties.Settings.Default.DbNameForDocs); // the name of the database
            return database.GetCollection<PathSubstMacro>(Properties.Settings.Default.DbCollectionForPathMacros);
        }

        public List<PathSubstMacro> ListPathSubstMacros()
        {
            // Get list
            MongoCollection<PathSubstMacro> collection_pathSubst = GetPathSubstCollection();
            return collection_pathSubst.FindAll().ToList<PathSubstMacro>();
        }

        public bool AddOrUpdateSubstMacroRecInDb(PathSubstMacro pathSubstMacro)
        {
            // Mongo append
            try
            {
                MongoCollection<PathSubstMacro> collection_pathSubst = GetPathSubstCollection();
                collection_pathSubst.Save(pathSubstMacro, SafeMode.True);
                // Log it
                logger.Info("Added/updated pathSubstMacro record for {0}", pathSubstMacro.origText);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot insert pathSubstMacro rec into {0} Coll... {1} for file {2} excp {3}",
                            Properties.Settings.Default.DbNameForDocs, Properties.Settings.Default.DbCollectionForPathMacros, pathSubstMacro.origText,
                            excp.Message);
                return false;
            }
            return true;
        }

        public void DeletePathSubstMacro(PathSubstMacro pathSubstMacro)
        {
            MongoCollection<PathSubstMacro> collection_pathSubst = GetPathSubstCollection();
            var query = Query<PathSubstMacro>.EQ(e => e.Id, pathSubstMacro.Id);
            collection_pathSubst.Remove(query);
        }

        public string ComputeMinimalPath(string folderName)
        {
            List<PathSubstMacro> pathSubstMacros = ListPathSubstMacros();

            // Process each substitution until no substitutions done - or max exceeded
            for (int i = 0; i < 20; i++)
            {
                bool bAnySubstDone = false;
                foreach (PathSubstMacro psm in pathSubstMacros)
                {
                    int pos = folderName.ToLower().IndexOf(psm.replaceText.ToLower());
                    if (pos >= 0)
                    {
                        folderName = folderName.Substring(0, pos) + psm.origText + folderName.Substring(pos + psm.replaceText.Length);
                        bAnySubstDone = true;
                    }
                }
                if (!bAnySubstDone)
                    break;
            }

            // Replace datetime strings
            folderName = Regex.Replace(folderName, @"[1-2]\d\d\d\\", @"[year]\");  // replace if occurs as end of folder name
            folderName = Regex.Replace(folderName, @"[1-2]\d\d\d$", @"[year]");       // replace if occurs at end of path name
            folderName = Regex.Replace(folderName, @"[1-2]\d\d\d\-\d\d\\", @"[year-month]\");
            folderName = Regex.Replace(folderName, @"[1-2]\d\d\d\-\d\d$", @"[year-month]");
            folderName = Regex.Replace(folderName, @"[1-2]\d\d\d\ Q[1-4]\\", @"[year-qtr]\");
            folderName = Regex.Replace(folderName, @"[1-2]\d\d\d\ Q[1-4]$", @"[year-qtr]");

            return folderName;
        }

        #endregion

        public void ApplyAliasList(string origDocType, string newDocType)
        {
            // read list from file - or database??
            // use list generated when doc types read in - prob in database
            // when doc types read in use info like [georgeheriots] to indicate GHS - 
            // add an enable / disable field to database and set in UI to turn on / off rules??
            // 
        }


    }

    public class ExprParseTerm
    {
        private static Brush[] locBrushes = new Brush[]
                {
                    new SolidColorBrush(Colors.Pink),
                    new SolidColorBrush(Colors.Goldenrod),
                    new SolidColorBrush(Colors.Indigo),
                    new SolidColorBrush(Colors.Chartreuse),
                    new SolidColorBrush(Colors.DarkBlue),
                    new SolidColorBrush(Colors.Cyan), 
                    new SolidColorBrush(Colors.DarkOrange) 
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
            exprTerm_MatchFactor
        }
        public Brush GetBrush(int offset = 0)
        {
            if (termType == ExprParseTermType.exprTerm_Location)
                return GetLocationBrush(locationBracketIdx + offset);
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
