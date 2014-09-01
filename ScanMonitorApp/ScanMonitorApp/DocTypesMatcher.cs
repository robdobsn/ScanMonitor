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
using System.Diagnostics;

namespace ScanMonitorApp
{
    public class DocTypesMatcher
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private MongoClient _dbClient;

        #region Init

        public DocTypesMatcher()
        {
        }

        public bool Setup()
        {
            try
            {
                var connectionString = Properties.Settings.Default.DbConnectionString;
                _dbClient = new MongoClient(connectionString);
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

        #region DocTypes Database Access

        private MongoCollection<DocType> GetDocTypesCollection()
        {
            // Setup db connection
            var server = _dbClient.GetServer();
            var database = server.GetDatabase(Properties.Settings.Default.DbNameForDocs); // the name of the database
            return database.GetCollection<DocType>(Properties.Settings.Default.DbCollectionForDocTypes);
        }

        public DocTypeMatchResult GetMatchingDocType(ScanPages scanPages, List<DocTypeMatchResult> listOfPossibleMatches = null)
        {
            // Get list of types
            DocTypeMatchResult bestMatchResult = new DocTypeMatchResult();
            var collection_doctypes = GetDocTypesCollection();
            MongoCursor<DocType> foundSdf = collection_doctypes.Find(Query.EQ("isEnabled", true));
#if TEST_PERF_GETMATCHINGDOCTYPE
            Stopwatch stopWatch1 = new Stopwatch();
            Stopwatch stopWatch2 = new Stopwatch();
#endif
            foreach (DocType doctype in foundSdf)
            {
#if TEST_PERF_GETMATCHINGDOCTYPE
                stopWatch1.Start();
#endif
                // Check if document matches
                DocTypeMatchResult matchResult = CheckIfDocMatches(scanPages, doctype, false, null);

#if TEST_PERF_GETMATCHINGDOCTYPE
                stopWatch1.Stop();
                stopWatch2.Start();
#endif

                // Find the best match
                bool bThisIsBestMatch = false;
                if (bestMatchResult.matchCertaintyPercent < matchResult.matchCertaintyPercent)
                    bThisIsBestMatch = true;
                else if (bestMatchResult.matchCertaintyPercent == matchResult.matchCertaintyPercent)
                    if (bestMatchResult.matchFactor < matchResult.matchFactor)
                        bThisIsBestMatch = true;

                // Redo match to get date and time info
                if (bThisIsBestMatch)
                {
                    matchResult = CheckIfDocMatches(scanPages, doctype, true, null);
                    bestMatchResult = matchResult;
                }

                // Check if this should be returned in the list of best matches
                if (listOfPossibleMatches != null)
                    if ((matchResult.matchCertaintyPercent > 0) || (matchResult.matchFactor > 0))
                        listOfPossibleMatches.Add(matchResult);

#if TEST_PERF_GETMATCHINGDOCTYPE
                stopWatch2.Stop();
#endif
            }
#if TEST_PERF_GETMATCHINGDOCTYPE
            logger.Info("T1 : {0}ms, T2 : {1}ms", stopWatch1.ElapsedMilliseconds, stopWatch2.ElapsedMilliseconds);
#endif


            // If no exact match get date info from entire doc
            if (bestMatchResult.matchCertaintyPercent != 100)
            {
                int bestDateIdx = 0;
                List<ExtractedDate> extractedDates = DocTextAndDateExtractor.ExtractDatesFromDoc(scanPages, "", out bestDateIdx);
                bestMatchResult.datesFoundInDoc = extractedDates;
                if (extractedDates.Count > 0)
                    bestMatchResult.docDate = extractedDates[bestDateIdx].dateTime;
            }

            // If list of best matches to be returned then sort that list now
            if (listOfPossibleMatches != null)
            {
                listOfPossibleMatches = listOfPossibleMatches.OrderByDescending(o => o.matchCertaintyPercent).ThenBy(o => o.matchFactor).ToList();
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
                collection_docTypes.Save(docType);
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

        #region Check Docs against DocTypes

        public DocTypeMatchResult CheckIfDocMatches(ScanPages scanPages, DocType docType, bool extractDates, List<DocMatchingTextLoc> matchingTextLocs)
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
            if (MatchAgainstDocText(docType.matchExpression, scanPages, ref matchFactorTotal, matchingTextLocs))
            {
                matchResult.matchCertaintyPercent = 100;
                matchResult.matchResultCode = DocTypeMatchResult.MatchResultCodes.FOUND_MATCH;
            }
            matchResult.docTypeName = docType.docTypeName;
            matchResult.matchFactor = matchFactorTotal;

            // Extract date
            if (extractDates)
            {
                int bestDateIdx = 0;
                List<ExtractedDate> extractedDates = DocTextAndDateExtractor.ExtractDatesFromDoc(scanPages, docType.dateExpression, out bestDateIdx);
                matchResult.datesFoundInDoc = extractedDates;
                if (extractedDates.Count > 0)
                    matchResult.docDate = extractedDates[bestDateIdx].dateTime;
            }

            return matchResult;
        }

        private bool MatchAgainstDocText(string matchExpression, ScanPages scanPages, ref double matchFactorTotal, List<DocMatchingTextLoc> matchingTextLocs)
        {
            int curExpressionIdx = 0;
            StringTok st = new StringTok(matchExpression);
            return EvalMatch(matchExpression, st, scanPages, ref matchFactorTotal, ref curExpressionIdx, matchingTextLocs);
        }

        private bool EvalMatch(string matchExpression, StringTok st, ScanPages scanPages, ref double matchFactorTotal, ref int curExpressionIdx, List<DocMatchingTextLoc> matchingTextLocs)
        {
            bool result = false;
            string token = "";
            bool curOpIsOr = true;
            bool opIsInverse = false;
            DocRectangle docRectPercent = new DocRectangle(0, 0, 100, 100);
            int docRectValIdx = 0;
            double matchFactorForTerm = 0;

#if TEST_PERF_EVALMATCH
            Stopwatch stopWatch1 = new Stopwatch();
            stopWatch1.Start();
#endif

            while((token = st.GetNextToken()) != null)
            {
                if (token.Trim() == "")
                    continue;
                else if (token == ")")
                    return result;
                else if (token == "(")
                {
                    bool tmpRslt = EvalMatch(matchExpression, st, scanPages, ref matchFactorTotal, ref curExpressionIdx, matchingTextLocs);
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
                            Double.TryParse(token, out matchFactorForTerm);
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
                                Double.TryParse(token, out rectVal);
                                docRectPercent.SetVal(docRectValIdx, rectVal);
                            }
                        }
                    }

                    // Process the match string using the location rectangle
                    // The check for curOpIsOr || result is to avoid unnecessary work if the expression is already false and we're doing a AND
                    if ((stringToMatch.Trim().Length >= 0) && (curOpIsOr || result))
                    {
                        bool tmpRslt = MatchString(stringToMatch, docRectPercent, scanPages, curExpressionIdx, matchingTextLocs);
                        if (opIsInverse)
                            tmpRslt = !tmpRslt;
                        if (curOpIsOr)
                            result |= tmpRslt;
                        else
                            result &= tmpRslt;

                        // Clear the inverse operator after 1 use
                        opIsInverse = false;
                        // Handle match factor
                        if (tmpRslt)
                            matchFactorTotal += matchFactorForTerm;
                    }

                    // Set the docRect to the entire page (ready for next term)
                    docRectPercent = new DocRectangle(0,0,100,100);
                    matchFactorForTerm = 0;
                    curExpressionIdx++;
                }
            }

#if TEST_PERF_EVALMATCH
            stopWatch1.Stop();
            logger.Info("EvalMatch : {0:0.00} uS, expr {1}", stopWatch1.ElapsedTicks * 1000000.0 / Stopwatch.Frequency, matchExpression);
#endif
            return result;
        }

        private bool MatchString(string str, DocRectangle docRectPercent, ScanPages scanPages, int exprIdx, List<DocMatchingTextLoc> matchingTextLocs)
        {
#if TEST_PERF_MATCHSTRING
            Stopwatch stopWatch1 = new Stopwatch();
            stopWatch1.Start();
#endif
            bool result = false;
            int elemCount = 0;
            for (int pageIdx = 0; pageIdx < scanPages.scanPagesText.Count; pageIdx++)
            {
                List<ScanTextElem> scanPageText = scanPages.scanPagesText[pageIdx];
                for (int elemIdx = 0; elemIdx < scanPageText.Count; elemIdx++)
                {
                    ScanTextElem textElem = scanPageText[elemIdx];
                    // Check bounds
                    if (docRectPercent.Intersects(textElem.bounds))
                    {
                        int mtchPos = textElem.text.IndexOf(str.Trim(), StringComparison.OrdinalIgnoreCase);
                        if (mtchPos >= 0)
                        {
                            result = true;
                            if (matchingTextLocs != null)
                            {
                                DocMatchingTextLoc dtml = new DocMatchingTextLoc();
                                dtml.pageIdx = pageIdx;
                                dtml.elemIdx = elemIdx;
                                dtml.exprIdx = exprIdx;
                                dtml.posInText = mtchPos;
                                dtml.matchLen = str.Trim().Length;
                                dtml.foundInTxtLen = textElem.text.Length;
                                matchingTextLocs.Add(dtml);
                            }
                            else
                            {
                                // If not compiling all text match locations then return immediately to save time
                                return true;
                            }
                        }
                    }
                    elemCount++;
                }
            }
#if TEST_PERF_MATCHSTRING
            stopWatch1.Stop();
            logger.Info("CheckForNewDocs : {0:0.00} uS, count {1}", stopWatch1.ElapsedTicks * 1000000.0 / Stopwatch.Frequency, elemCount);
#endif
            return result;
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

        #region Parse DocMatch Expression for Text Highlighting

        public static List<ExprParseTerm> ParseDocMatchExpression(string matchExpression, int cursorPosForBracketMatching)
        {
            // Go through the matchExpression finding parse terms
            List<ExprParseTerm> parseTermsList = new List<ExprParseTerm>();
            int curBracketDepth = 0;
            int matchBracketDepth = -1;
            int lastLocStartIdx = -1;
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
                            if (lastLocStartIdx != -1)
                            {
                                parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_Location, lastLocStartIdx, chIdx - lastLocStartIdx, curBracketDepth, locationBracketIdx));
                            }
                            else
                            {
                                // Add text upto this point
                                if (bInMatchFactorTerm)
                                    lastTxtStartIdx = ParserAddMatchFactor(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);
                                else
                                    lastTxtStartIdx = ParserAddTextToPoint(parseTermsList, matchExpression, lastTxtStartIdx, chIdx, curBracketDepth);
                                bInMatchFactorTerm = false;
                            }
                            parseTermsList.Add(new ExprParseTerm(ExprParseTerm.ExprParseTermType.exprTerm_LocationBrackets, chIdx, 1, --curBracketDepth, locationBracketIdx));
                            if ((cursorPosForBracketMatching == chIdx) || (cursorPosForBracketMatching == chIdx + 1))
                                matchBracketDepth = curBracketDepth;
                            lastTxtStartIdx = chIdx + 1;
                            locationBracketIdx++;
                            lastLocStartIdx = -1;
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
                collection_pathSubst.Save(pathSubstMacro);
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
            folderName = Regex.Replace(folderName, @"[\\\s](19[6789]\d|20[01234]\d)\\", @"[year]\");  // replace if occurs as end of folder name
            folderName = Regex.Replace(folderName, @"[\\\s](19[6789]\d|20[01234]\d)$", @"[year]");       // replace if occurs at end of path name
            folderName = Regex.Replace(folderName, @"[\\\s](19[6789]\d|20[01234]\d)\-(0[123456789]|1[012])\\", @"[year-month]\");
            folderName = Regex.Replace(folderName, @"[\\\s](19[6789]\d|20[01234]\d)\-(0[123456789]|1[012])$", @"[year-month]");
            folderName = Regex.Replace(folderName, @"[\\\s](19[6789]\d|20[01234]\d)\ Q[1-4]\\", @"[year-qtr]\");
            folderName = Regex.Replace(folderName, @"[\\\s](19[6789]\d|20[01234]\d)\ Q[1-4]$", @"[year-qtr]");
            folderName = Regex.Replace(folderName, @"[\\\s](19[6789]\d|20[01234]\d)\ F[1-4]\\", @"[year-fqtr]\");
            folderName = Regex.Replace(folderName, @"[\\\s](19[6789]\d|20[01234]\d)\ F[1-4]$", @"[year-fqtr]");
            folderName = Regex.Replace(folderName, @"[\\\s](19[6789]\d|20[01234]\d)-(19[6789]\d|20[01234]\d)\\", @"[finyear]\");
            folderName = Regex.Replace(folderName, @"[\\\s](19[6789]\d|20[01234]\d)-(19[6789]\d|20[01234]\d)$", @"[finyear]");

            return folderName;
        }

        public string ComputeExpandedPath(string minimalPath, DateTime dateTimeOfFile, bool removeDateMacros, ref bool bPathContainsDateMacros)
        {
            bPathContainsDateMacros = false;
            string inPath = minimalPath.ToLower();
            List<PathSubstMacro> pathSubstMacros = ListPathSubstMacros();

            // Process each substitution until no substitutions done - or max exceeded
            for (int i = 0; i < 20; i++)
            {
                bool bAnySubstDone = false;
                foreach (PathSubstMacro psm in pathSubstMacros)
                {
                    int pos = inPath.ToLower().IndexOf(psm.origText.ToLower());
                    if (pos >= 0)
                    {
                        inPath = inPath.Substring(0, pos) + psm.replaceText + inPath.Substring(pos + psm.origText.Length);
                        bAnySubstDone = true;
                    }
                }
                if (!bAnySubstDone)
                    break;
            }

            // Store path to compare later
            string compareToPath = inPath;

            // Remove/Replace datetime strings
            if (removeDateMacros)
            {
                int brackPos = inPath.IndexOf('[');
                if (brackPos >= 0)
                {
                    // Find prior / or \
                    for (int chIdx = brackPos; chIdx >= 0; chIdx--)
                        if ((inPath[chIdx] == '/') || (inPath[chIdx] == '\\'))
                        {
                            brackPos = chIdx;
                            break;
                        }
                    inPath = inPath.Substring(0, brackPos);
                }
            }
            else
            {
                string yearStr = string.Format("{0:yyyy}", dateTimeOfFile);
                inPath = inPath.Replace("[year]", yearStr);
                string yearMonStr = string.Format("{0:yyyy-MM}", dateTimeOfFile);
                inPath = inPath.Replace("[year-month]", yearMonStr);
                string finYearStr = string.Format("{0:yyyy}-{1:yyyy}", dateTimeOfFile.AddYears(-1), dateTimeOfFile);
                inPath = inPath.Replace("[finyear]", finYearStr);

                // Check for year-quarter
                if (inPath.ToLower().Contains("[year-qtr]"))
                {
                    // Calculate quarter
                    int quarterNo = 1 + (dateTimeOfFile.Month - 1) / 3;
                    string replStr = string.Format("{0:yyyy} Q{1}", dateTimeOfFile, quarterNo);
                    inPath = inPath.Replace("[year-qtr]", replStr);
                }
                // Check for financial year-quarter
                if (inPath.ToLower().Contains("[year-fqtr]"))
                {
                    // Calculate quarter
                    int quarterNo = (2 + (dateTimeOfFile.Month - 1) / 3) % 4;
                    string replStr = string.Format("{0:yyyy} F{1}", dateTimeOfFile, quarterNo);
                    inPath = inPath.Replace("[year-fqtr]", replStr);
                }
            }

            // Check if date macros expanded
            bPathContainsDateMacros = (compareToPath != inPath);

            return inPath;
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
                case ExprParseTermType.exprTerm_MatchFactor: { return new SolidColorBrush(Colors.Orange); }
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

    public class DocMatchingTextLoc
    {
        public int pageIdx = 0;
        public int elemIdx = 0;
        public int exprIdx = 0;
        public int posInText = 0;
        public int matchLen = 0;
        public int foundInTxtLen = 0;
    }
}
