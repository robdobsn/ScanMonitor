using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    public class DocTextAndDateExtractor
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        const string longDateRegex = @"(((0)[1-9])|((1|2)[0-9])|(3[0-1])|([1-9]))?(\s*?)([a-zA-Z\,\.]?[a-zA-Z\,\.]?)-?(\s*?)-?(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|January|February|March|April|May|June|July|August|September|October|November|December)[,-]?(\s*?)[,-]?(((19)?[89]\d)|((2\s*?0\s*?)?[012345l]\s*?[\dl]))([^,]|$)";
        const string USlongDateRegex = @"(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|January|February|March|April|May|June|July|August|September|October|November|December)(\s*?)(1-\s*)?(((0)[1-9])|((1|2)[0-9])|(3[0-1])|([1-9]))(\s*?)([a-zA-Z]?[a-zA-Z]?),(\s*?)(((19)?[8|9]\d)|((20)?[0|1|2]\d))";
        const string shortDateLeadingZeroesRegex = @"(0[1-9]|[12][0-9]|3[01])\s?[-/.]\s?(0[1-9]|1[012])\s?[-/.]\s?((19|20)?(\d\d))";
        const string shortDateNoLeadingZeroesRegex = @"([1-9]|[12][0-9]|3[01])\s?[-/.]\s?([1-9]|1[012])\s?[-/.]\s?((19|20)?(\d\d))";
        const string shortDateSpacesRegex = @"(0[1-9]|[12][0-9]|3[01])\s?(0[1-9]|1[012])\s?((19|20)(\d\d))";

        private static Dictionary<string, int> monthDict = new Dictionary<string, int>
            {
                { "jan", 1 }, { "feb", 2 }, { "mar", 3 }, { "apr", 4 },
                { "may", 5 }, { "jun", 6 }, { "jul", 7 }, { "aug", 8 }, 
                { "sep", 9 }, { "oct", 10 }, { "nov", 11 }, { "dec", 12 }, 
            };

        public static List<ExtractedDate> ExtractDatesFromDoc(ScanPages scanPages, string dateExpr, out int bestDateIdx)
        {
            List<ExtractedDate> datesResult = new List<ExtractedDate>();

            // Extract location rectangles from doctype
            List<ExprParseTerm> parseTerms = DocTypesMatcher.ParseDocMatchExpression(dateExpr, 0);
            bool bAtLeastOneExprSearched = false;
            string lastDateSearchTerm = "";
            double lastDateSearchMatchFactor = 0;
            foreach (ExprParseTerm parseTerm in parseTerms)
            {
                if (parseTerm.termType == ExprParseTerm.ExprParseTermType.exprTerm_Text)
                {
                    if (lastDateSearchTerm != "")
                    {
                        SearchForDateItem(scanPages, lastDateSearchTerm, new DocRectangle(0,0,100,100), lastDateSearchMatchFactor, datesResult);
                        bAtLeastOneExprSearched = true;
                    }
                    lastDateSearchTerm = dateExpr.Substring(parseTerm.stPos, parseTerm.termLen);
                    // Reset matchFactor for next search term
                    lastDateSearchMatchFactor = 0;
                }
                if (parseTerm.termType == ExprParseTerm.ExprParseTermType.exprTerm_Location)
                {
                    string locStr = dateExpr.Substring(parseTerm.stPos, parseTerm.termLen);
                    DocRectangle lastDateSearchRect = new DocRectangle(locStr);
                    SearchForDateItem(scanPages, lastDateSearchTerm, lastDateSearchRect, lastDateSearchMatchFactor, datesResult);
                    lastDateSearchTerm = "";
                    lastDateSearchMatchFactor = 0;
                    bAtLeastOneExprSearched = true;
                }
                if (parseTerm.termType == ExprParseTerm.ExprParseTermType.exprTerm_MatchFactor)
                {
                    if (dateExpr.Length > parseTerm.stPos + 1)
                    {
                        string valStr = dateExpr.Substring(parseTerm.stPos + 1, parseTerm.termLen-1);
                        Double.TryParse(valStr, out lastDateSearchMatchFactor);
                    }
                }
            }

            // There may be one last expression still to find - but be sure that at least one is searched for
            if ((lastDateSearchTerm != "") || (!bAtLeastOneExprSearched))
                SearchForDateItem(scanPages, lastDateSearchTerm, new DocRectangle(0,0,100,100), lastDateSearchMatchFactor, datesResult);

            // Find the best date index based on highest match factor
            bestDateIdx = 0;
            double highestDateMatchFactor = 0;
            for (int dateIdx = 0; dateIdx < datesResult.Count; dateIdx++)
            {
                if (highestDateMatchFactor < datesResult[dateIdx].matchFactor)
                {
                    bestDateIdx = dateIdx;
                    highestDateMatchFactor = datesResult[dateIdx].matchFactor;
                }
            }

            return datesResult;
        }

        public static void SearchForDateItem(ScanPages scanPages, string dateSearchTerm, DocRectangle dateDocRect, double matchFactor, List<ExtractedDate> datesResult, int limitToPageNumN = -1)
        {
            int firstPageIdx = 0;
            int lastPageIdxPlusOne = scanPages.scanPagesText.Count;
            if (limitToPageNumN != -1)
            {
                firstPageIdx = limitToPageNumN - 1;
                lastPageIdxPlusOne = limitToPageNumN;
            }
            for (int pageIdx = firstPageIdx; pageIdx < lastPageIdxPlusOne; pageIdx++)
            {
                List<ScanTextElem> scanPageText = scanPages.scanPagesText[pageIdx];
                foreach (ScanTextElem textElem in scanPageText)
                {
                    // Check if there are at least two digits together in the text (any date format requires this at least)
                    if (!Regex.IsMatch(textElem.text, @"\d\d"))
                        continue;

                    // Check bounds
                    if (dateDocRect.Intersects(textElem.bounds))
                    {
                        // See which date formats to try
                        bool bTryLong = false;
                        bool bTryShort = false;
                        bool bTryUS = false;
                        bool bTryNoZeroes = false;
                        bool bTrySpaceSeparated = false;
                        if (dateSearchTerm.IndexOf("~long", StringComparison.OrdinalIgnoreCase) >= 0)
                            bTryLong = true;
                        if (dateSearchTerm.IndexOf("~short", StringComparison.OrdinalIgnoreCase) >= 0)
                            bTryShort = true;
                        if (dateSearchTerm.IndexOf("~US", StringComparison.OrdinalIgnoreCase) >= 0)
                            bTryUS = true;
                        if (dateSearchTerm.IndexOf("~No0", StringComparison.OrdinalIgnoreCase) >= 0)
                            bTryNoZeroes = true;
                        if (dateSearchTerm.IndexOf("~Spaces", StringComparison.OrdinalIgnoreCase) >= 0)
                            bTrySpaceSeparated = true;
                        if (!(bTryLong | bTryShort))
                        {
                            bTryLong = true;
                            bTryShort = true;
                            bTryUS = true;
                            bTryNoZeroes = true;
                            bTrySpaceSeparated = true;
                        }

                        // Get match text if any
                        string matchText = dateSearchTerm;
                        int squigPos = dateSearchTerm.IndexOf('~');
                        if (squigPos >= 0)
                            matchText = dateSearchTerm.Substring(0, squigPos);
                        double matchResultFactor = 0;
                        if (textElem.text.IndexOf(matchText, StringComparison.OrdinalIgnoreCase) >= 0)
                            matchResultFactor = matchFactor;

                        // Try to find dates
                        if (bTryLong)
                        {
                            MatchCollection ldMatches = Regex.Matches(textElem.text, longDateRegex, RegexOptions.IgnoreCase);
                            CoerceMatchesToDates(datesResult, matchResultFactor, textElem, ldMatches, ExtractedDate.DateMatchType.LongDate, 13, 11, 1);
                            if (bTryUS)
                            {
                                MatchCollection usldMatches = Regex.Matches(textElem.text, USlongDateRegex, RegexOptions.IgnoreCase);
                                CoerceMatchesToDates(datesResult, matchResultFactor, textElem, usldMatches, ExtractedDate.DateMatchType.USLongDate, 14, 1, 4);
                            }
                        }

                        if (bTryShort)
                        {
                            MatchCollection sdlzMatches = Regex.Matches(textElem.text, shortDateLeadingZeroesRegex, RegexOptions.IgnoreCase);
                            CoerceMatchesToDates(datesResult, matchResultFactor, textElem, sdlzMatches, ExtractedDate.DateMatchType.ShortDateLeadingZeroes, 3, 2, 1);
                            if (bTryNoZeroes)
                            {
                                MatchCollection sdnlzMatches = Regex.Matches(textElem.text, shortDateNoLeadingZeroesRegex, RegexOptions.IgnoreCase);
                                CoerceMatchesToDates(datesResult, matchResultFactor, textElem, sdnlzMatches, ExtractedDate.DateMatchType.ShortDateNoLeadingZeroes, 3, 2, 1);
                            }
                            if (bTrySpaceSeparated)
                            {
                                MatchCollection sdspMatches = Regex.Matches(textElem.text, shortDateSpacesRegex, RegexOptions.IgnoreCase);
                                CoerceMatchesToDates(datesResult, matchResultFactor, textElem, sdspMatches, ExtractedDate.DateMatchType.ShortDateNoLeadingZeroes, 3, 2, 1);
                            }
                        }
                    }
                }
            }
        }

        private static void CoerceMatchesToDates(List<ExtractedDate> datesResult, double matchResultFactor, ScanTextElem textElem, MatchCollection matches, ExtractedDate.DateMatchType matchType, int yearGroupIdx, int monthGroupIdx, int dayGroupIdx)
        {
            foreach (Match match in matches)
            {
                ExtractedDate fd = new ExtractedDate();
                try
                {
                    string yrStr = match.Groups[yearGroupIdx].Value.Replace(" ", "");
                    yrStr = yrStr.Replace("l", "1");
                    int year = Convert.ToInt32(yrStr);
                    if (year < 80)
                    {
                        year += 2000;
                        fd.yearWas2Digit = true;
                    }
                    else if (year < 100)
                    {
                        year += 1900;
                        fd.yearWas2Digit = true;
                    }
                    int month = 1;
                    if (Char.IsDigit(match.Groups[monthGroupIdx].Value, 0))
                        month = Convert.ToInt32(match.Groups[2].Value);
                    else
                        month = monthDict[match.Groups[monthGroupIdx].Value.ToLower().Substring(0, 3)];
                    int day = 1;
                    fd.dayWasMissing = true;
                    if (match.Groups[dayGroupIdx].Value.Trim() != "")
                    {
                        day = Convert.ToInt32(match.Groups[dayGroupIdx].Value);
                        fd.dayWasMissing = false;
                    }
                    if (year > DateTime.MaxValue.Year)
                        year = DateTime.MaxValue.Year;
                    if (year < DateTime.MinValue.Year)
                        year = DateTime.MinValue.Year;
                    if (day > DateTime.DaysInMonth(year, month))
                        day = DateTime.DaysInMonth(year, month);
                    if (day < 1)
                        day = 1;
                    DateTime dt = new DateTime(year, month, day);

                    // Add date to list
                    fd.foundInText = textElem.text;
                    fd.posnInText = match.Index;
                    fd.matchLength = match.Length;
                    fd.dateTime = dt;
                    fd.dateMatchType = matchType;
                    fd.locationOfDateOnPagePercent = textElem.bounds;
                    fd.matchFactor = matchResultFactor;
                    datesResult.Add(fd);
                }
                catch
                {
                }

            }
        }

    }

    public class ExtractedDate
    {
        public enum DateMatchType
        {
            None,
            LongDate,
            USLongDate,
            ShortDateLeadingZeroes,
            ShortDateNoLeadingZeroes,
            ShortDateSpaceSeparators
        };

        public DateTime dateTime;
        public DocRectangle locationOfDateOnPagePercent;
        public bool yearWas2Digit = false;
        public bool dayWasMissing = false;
        public int posnInText = 0;
        public int matchLength = 0;
        public DateMatchType dateMatchType = DateMatchType.None;
        public double matchFactor = 0;

        public string foundInText;
    }
}
