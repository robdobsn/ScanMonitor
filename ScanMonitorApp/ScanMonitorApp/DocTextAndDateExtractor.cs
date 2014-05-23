using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    public class DocTextAndDateExtractor
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        const int MATCH_FACTOR_BUMP_FOR_TEXT_MONTH = 10;
        const int MATCH_FACTOR_BUMP_FOR_4_DIGIT_YEAR = 10;
        const int MATCH_FACTOR_BUMP_FOR_TOP_40_PC_OF_PAGE = 10;
        const int MATCH_FACTOR_BUMP_FOR_DAY_MISSING = -5;
        const int MATCH_FACTOR_BUMP_FOR_PAGE1 = 30;
        const int MATCH_FACTOR_BUMP_FOR_PAGE2 = 10;
        const int MAX_TEXT_ELEMS_TO_JOIN = 10;
        const int MAX_SEP_CHARS_BETWEEN_DATE_ELEMS = 4;
        const int MATCH_FACTOR_BUMP_FOR_EARLIEST_DATE = 40;
        const int MATCH_FACTOR_BUMP_FOR_LATEST_DATE = 40;

        // TEST TEST
        const bool TEST_AGAINST_OLD_DATE_ALGORITHM = false;

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

        public static string[] shortMonthStrings = new string[]
        {
            "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" 
        };


        public static string ExtractTextFromPage(ScanPages scanPages, DocRectangle docRect, int pageNum)
        {
            int pageIdx = pageNum-1;
            if ((pageIdx < 0) || (pageIdx >= scanPages.scanPagesText.Count))
                return "";

            // Get page to search
            List<ScanTextElem> scanPageText = scanPages.scanPagesText[pageNum-1];

            // Iterate text elements
            foreach (ScanTextElem textElem in scanPageText)
            {
                // Check rectangle bounds
                if (!docRect.Intersects(textElem.bounds))
                    continue;

                // Return first match
                return textElem.text;
            }
            return "";
        }

        public static List<ExtractedDate> ExtractDatesFromDoc(ScanPages scanPages, string dateExpr, out int bestDateIdx)
        {
            bestDateIdx = 0;
            List<ExtractedDate> datesResult = new List<ExtractedDate>();
            if (scanPages == null)
                return datesResult;

            // Extract location rectangles from doctype
            List<ExprParseTerm> parseTerms = DocTypesMatcher.ParseDocMatchExpression(dateExpr, 0);
            bool bAtLeastOneExprSearched = false;
            string lastDateSearchTerm = "";
            double lastDateSearchMatchFactor = 0;
            bool latestDateRequested = false;
            bool earliestDateRequested = false;
            foreach (ExprParseTerm parseTerm in parseTerms)
            {
                if (parseTerm.termType == ExprParseTerm.ExprParseTermType.exprTerm_Text)
                {
                    if (lastDateSearchTerm != "")
                    {
                        SearchForDateItem(scanPages, lastDateSearchTerm, new DocRectangle(0, 0, 100, 100), lastDateSearchMatchFactor, datesResult, ref latestDateRequested, ref earliestDateRequested);
                        bAtLeastOneExprSearched = true;
                    }
                    lastDateSearchTerm = dateExpr.Substring(parseTerm.stPos, parseTerm.termLen);
                    // Reset matchFactor for next search term
                    lastDateSearchMatchFactor = 0;
                }
                else if (parseTerm.termType == ExprParseTerm.ExprParseTermType.exprTerm_Location)
                {
                    string locStr = dateExpr.Substring(parseTerm.stPos, parseTerm.termLen);
                    DocRectangle lastDateSearchRect = new DocRectangle(locStr);
                    SearchForDateItem(scanPages, lastDateSearchTerm, lastDateSearchRect, lastDateSearchMatchFactor, datesResult, ref latestDateRequested, ref earliestDateRequested);
                    lastDateSearchTerm = "";
                    lastDateSearchMatchFactor = 0;
                    bAtLeastOneExprSearched = true;
                }
                else if (parseTerm.termType == ExprParseTerm.ExprParseTermType.exprTerm_MatchFactor)
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
                SearchForDateItem(scanPages, lastDateSearchTerm, new DocRectangle(0, 0, 100, 100), lastDateSearchMatchFactor, datesResult, ref latestDateRequested, ref earliestDateRequested);

            // If required check for the earliest and/or latest dates and bump their factors
            DateTime earliestDate = DateTime.MaxValue;
            DateTime latestDate = DateTime.MinValue;
            int earliestIdx = -1;
            int latestIdx = -1;
            for (int dateIdx = 0; dateIdx < datesResult.Count; dateIdx++)
            {
                if (earliestDate > datesResult[dateIdx].dateTime)
                {
                    earliestDate = datesResult[dateIdx].dateTime;
                    earliestIdx = dateIdx;
                }
                if (latestDate < datesResult[dateIdx].dateTime)
                {
                    latestDate = datesResult[dateIdx].dateTime;
                    latestIdx = dateIdx;
                }
            }
            if (earliestDateRequested && (earliestIdx != -1))
                datesResult[earliestIdx].matchFactor += MATCH_FACTOR_BUMP_FOR_EARLIEST_DATE;
            if (latestDateRequested && (latestIdx != -1))
                datesResult[latestIdx].matchFactor += MATCH_FACTOR_BUMP_FOR_LATEST_DATE;

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

        public static void SearchForDateItem(ScanPages scanPages, string dateSearchTerm, DocRectangle dateDocRect, double matchFactor, List<ExtractedDate> datesResult,
                                    ref bool latestDateRequested, ref bool earliestDateRequested, int limitToPageNumN = -1, bool ignoreWhitespace = false)
        {
            // Get date search info
            DateSrchInfo dateSrchInfo = GetDateSearchInfo(dateSearchTerm);
            if (dateSrchInfo.bEarliestDate)
                earliestDateRequested = true;
            if (dateSrchInfo.bLatestDate)
                latestDateRequested = true;

            // Find first and last pages to search
            int firstPageIdx = 0;
            int lastPageIdxPlusOne = scanPages.scanPagesText.Count;
            if (limitToPageNumN != -1)
            {
                firstPageIdx = limitToPageNumN - 1;
                lastPageIdxPlusOne = limitToPageNumN;
            }

            // Iterate pages
            for (int pageIdx = firstPageIdx; pageIdx < lastPageIdxPlusOne; pageIdx++)
            {
                List<ScanTextElem> scanPageText = scanPages.scanPagesText[pageIdx];
                string joinedText = "";     // This maybe used if ~join macrocommand used
                int joinCount = 0;

                double matchFactorForThisPage = matchFactor + (pageIdx == 0 ? MATCH_FACTOR_BUMP_FOR_PAGE1 : (pageIdx == 1 ? MATCH_FACTOR_BUMP_FOR_PAGE2 : 0));

                // Iterate text elements
                foreach (ScanTextElem textElem in scanPageText)
                {
                    // Check that the text contains at least two digits together to avoid wasting time looking for dates where there can be none
                    if (!Regex.IsMatch(textElem.text, @"\d\d"))
                        continue;

                    // Check rectangle bounds
                    if (!dateDocRect.Intersects(textElem.bounds))
                        continue;

                    // Check for join
                    if (dateSrchInfo.bJoinTextInRect)
                    {
                        if (joinCount < MAX_TEXT_ELEMS_TO_JOIN)
                            joinedText += textElem.text + " ";
                        joinCount++;
                        continue;
                    }

                    // Search within the found text
                    SearchWithinString(textElem.text, textElem.bounds, dateSearchTerm, dateSrchInfo, matchFactorForThisPage, pageIdx, datesResult, ignoreWhitespace);

                }

                // If joined then search just once
                if (dateSrchInfo.bJoinTextInRect)
                    SearchWithinString(joinedText, dateDocRect, dateSearchTerm, dateSrchInfo, matchFactorForThisPage, pageIdx, datesResult, ignoreWhitespace);
            }

            // TEST TEST TEST
#if TEST_AGAINST_OLD_DATE_ALGORITHM
            {
                List<ExtractedDate> testDatesResult = new List<ExtractedDate>();
                SearchForDateItem2(scanPages, dateSearchTerm, dateDocRect, matchFactor, testDatesResult, limitToPageNumN);
                stp2.Stop();

                Console.WriteLine("File: " + scanPages.uniqName + " OldTime = " + stp2.ElapsedMilliseconds.ToString() + " NewTime = " + stp.ElapsedMilliseconds.ToString());

                foreach (ExtractedDate newD in datesResult)
                {
                    bool bFound = false;
                    foreach (ExtractedDate oldD in testDatesResult)
                    {
                        if (oldD.dateTime == newD.dateTime)
                        {
                            bFound = true;
                            break;
                        }
                    }
                    if (!bFound)
                    {
                        Console.WriteLine("Date Mismatch New=" + newD.dateTime.ToLongDateString());
                    }
                }
                foreach (ExtractedDate oldD in testDatesResult)
                {
                    bool bFound = false;
                    foreach (ExtractedDate newD in datesResult)
                    {
                        if (oldD.dateTime == newD.dateTime)
                        {
                            bFound = true;
                            break;
                        }
                    }
                    if (!bFound)
                    {
                        Console.WriteLine("Date Mismatch Old=" + oldD.dateTime.ToLongDateString());
                    }
                }
            }
#endif
        }

        private static void SearchWithinString(string inStr, DocRectangle textBounds, string dateSearchTerm, DateSrchInfo dateSrchInfo, double matchFactor, int pageIdx, List<ExtractedDate> datesResult, bool ignoreWhitespace)
        {
            // Start at the beginning of the string
            string s = inStr;
            if (ignoreWhitespace)
                s = s.Replace(" ", "");
            int dateSrchPos = 0;
            int chIdx = 0;
            string curStr = "";
            int day = -1;
            int month = -1;
            bool bMonthFromChars = false;
            int year = -1;
            s = s.ToLower();
            bool strIsDigits = false;
            int firstMatchPos = -1;
            int lastMatchPos = 0;
            int commaCount = 0;
            bool bRangeIndicatorFound = false;
            int numDatesFoundInString = 0;
            int numSepChars = 0;
            for (chIdx = 0; chIdx < s.Length; chIdx++)
            {
                char ch = s[chIdx];
                bool bResetNeeded = false;

                // Search element
                DateElemSrch el = null;
                int minChars = 1;
                int maxChars = 9;
                if (dateSrchPos < dateSrchInfo.dateEls.Count)
                {
                    el = dateSrchInfo.dateEls[dateSrchPos];
                    minChars = el.minChars;
                    maxChars = el.maxChars;
                }

                // Check if digits required
                if ((el == null) || (el.isDigits))
                {
                    char testCh = ch;
                    if ((testCh == 'l') || (testCh == 'o'))
                    {
                        if (((strIsDigits) && (curStr.Length > 0)) || ((chIdx+1 < s.Length) && (Char.IsDigit(s[chIdx+1]))))
                        {
                            if (testCh == 'l')
                                testCh = '1';
                            else if (testCh == 'o')
                                testCh = '0';
                            else if (testCh == 'i')
                                testCh = '1';
                        }
                    }
                    if (Char.IsDigit(testCh))
                    {
                        numSepChars = 0;
                        // Ignore if it's a zero and we're not allowed leading zeroes
                        //                        if ((el != null) && (!el.allowLeadingZeroes) && (curStrPos == 0) && (ch == '0'))
                        //                            continue;

                        if (!strIsDigits)
                            curStr = "";
                        curStr += testCh;
                        strIsDigits = true;
                        if (curStr.Length < minChars)
                            continue;

                        // Check max chars
                        if (curStr.Length > maxChars)
                        {
                            curStr = "";
                            continue;
                        }

                        // Check if the next char is also a digit - if not then we've found what we're looking for
                        if (((chIdx + 1 >= s.Length) || (!Char.IsDigit(s[chIdx + 1]))) && (curStr != "0"))
                        {
                            // Is this a day / month or year??
                            DateElemSrch.DateElType elType = DateElemSrch.DateElType.DE_NONE;
                            if (el != null)
                                elType = el.dateElType;
                            else
                            {
                                // Handle one and two digit numbers
                                if (curStr.Length <= 2)
                                {
                                    // Already had a char based month?
                                    if (bMonthFromChars)
                                    {
                                        if (!dateSrchInfo.bIsUsDate)
                                            elType = DateElemSrch.DateElType.DE_YEAR;
                                        else
                                            elType = DateElemSrch.DateElType.DE_DAY;
                                    }
                                    else
                                    {
                                        // Position for standard month?
                                        if ((dateSrchPos == 1) && (!dateSrchInfo.bIsUsDate))
                                            elType = DateElemSrch.DateElType.DE_MONTH;
                                        // Position for US month?
                                        else if ((dateSrchPos == 0) && (dateSrchInfo.bIsUsDate))
                                            elType = DateElemSrch.DateElType.DE_MONTH;
                                        else if (dateSrchPos < 2)
                                            elType = DateElemSrch.DateElType.DE_DAY;
                                        else if ((dateSrchPos > 0) && (curStr.Length == 2))
                                            elType = DateElemSrch.DateElType.DE_YEAR;
                                    }
                                }
                                else if (curStr.Length == 4)
                                {
                                    // Num digits == 4
                                    if (dateSrchPos > 0)
                                        elType = DateElemSrch.DateElType.DE_YEAR;
                                }
                            }

                            // Handle the value
                            if (elType == DateElemSrch.DateElType.DE_DAY)
                            {
                                Int32.TryParse(curStr, out day);
                                if ((day < 1) || (day > 31))
                                    day = -1;
                            }
                            else if (elType == DateElemSrch.DateElType.DE_MONTH)
                            {
                                Int32.TryParse(curStr, out month);
                                if ((month < 1) || (month > 12))
                                    month = -1;
                                bMonthFromChars = false;
                            }
                            else if (elType == DateElemSrch.DateElType.DE_YEAR)
                            {
                                Int32.TryParse(curStr, out year);
                                if (curStr.Length == 2)
                                {
                                    if ((year < 0) || (year > 100))
                                        year = -1;
                                }
                                else if (curStr.Length == 4)
                                {
                                    if ((year < 1800) || (year > 2200))
                                        year = -1;
                                }

                                // If no date formatting string is used then year must be the last item
                                if ((el == null) && (year != -1))
                                    bResetNeeded = true;
                            }
                            else
                            {
                                curStr = "";
                                continue;
                            }
                            if (firstMatchPos == -1)
                                firstMatchPos = chIdx - curStr.Length;
                            lastMatchPos = chIdx;
                            dateSrchPos++;
                            curStr = "";
                        }
                    }
                }
                if ((el == null) || (!el.isDigits))
                {
                    if (Char.IsLetter(ch))
                    {
                        if (strIsDigits)
                            curStr = "";
                        strIsDigits = false;

                        // Check we're still looking for a month value
                        if (month != -1)
                        {
                            numSepChars++;
                            continue;
                        }

                        // Form a sub-string to test for month names
                        curStr += ch;

                        // Check for range indicator
                        if (numDatesFoundInString == 1)
                        {
                            if (chIdx - curStr.Length - 1 > 0)
                            {
                                string testStr = s.Substring(chIdx - curStr.Length - 1);
                                if (testStr.Contains(" to") || testStr.Contains(" to"))
                                    bRangeIndicatorFound = true;
                            }
                        }

                        // No point checking for month strings until 3 chars got
                        if (curStr.Length < 3)
                            continue;

                        // Check for a month name
                        if (shortMonthStrings.Any(curStr.Contains))
                        {
                            for (int monIdx = 0; monIdx < shortMonthStrings.Length; monIdx++)
                                if (curStr.Contains(shortMonthStrings[monIdx]))
                                {
                                    month = monIdx + 1;
                                    bMonthFromChars = true;
                                    break;
                                }
                            if (firstMatchPos == -1)
                                firstMatchPos = chIdx - curStr.Length;
                            lastMatchPos = chIdx;
                            dateSrchPos++;
                            curStr = "";
                            numSepChars = 0;

                            // Move chIdx on to skip to next non letter
                            while ((chIdx < s.Length-1) && (Char.IsLetter(s[chIdx+1])))
                                chIdx++;

                            // Check for another valid month string in next few chars to detect ranges without a year
                            // e.g. should find ranges like 3 Jan - 4 Mar 2011 or 1st Jan to 31st May 2013
                            // but exlude ranges like 3 Jan 2012 - 4 Mar 2012 which would be seen as two separate dates
                            if (!dateSrchInfo.bNoDateRanges)
                            {
                                string strNextStr = "";
                                bool bStrRangeIndicatorFound = false;
                                int digitGroups = 0;
                                bool isInDigitGroup = false;
                                for (int chNext = chIdx+1; (chNext < s.Length) && (chNext < chIdx + 15); chNext++)
                                {
                                    // Count the groups of digits 
                                    // (if we find two groups then break out as it's probably a range that contains separate years)
                                    if (Char.IsDigit(s[chNext]))
                                    {
                                        if (!isInDigitGroup)
                                        {
                                            isInDigitGroup = true;
                                            digitGroups++;
                                            if (digitGroups >= 2)
                                                break;
                                        }
                                    }

                                    // Form a string from letters found
                                    else if (Char.IsLetter(s[chNext]))
                                    {
                                        isInDigitGroup = false;
                                        strNextStr += s[chNext];

                                        // Check if the string contains "to"
                                        if (strNextStr.Length >= 2)
                                            if (strNextStr.Contains("to"))
                                                bStrRangeIndicatorFound = true;

                                        // Check if the string contains a short month name
                                        if (bStrRangeIndicatorFound && (strNextStr.Length >= 3))
                                            if (shortMonthStrings.Any(strNextStr.Contains))
                                            {
                                                bResetNeeded = true;
                                                break;
                                            }
                                    }
                                    else
                                    {
                                        // Check punctuation - this assumes a - is a range seperator
                                        isInDigitGroup = false;
                                        if (s[chNext] == '-')
                                            bStrRangeIndicatorFound = true;
                                        strNextStr = "";
                                    }
                                }
                            }
                            else
                            {
                                bResetNeeded = true;
                            }
                        }
                    }
                }

                // Check for whitespace/punctuation/etc
                if (!Char.IsLetterOrDigit(ch))
                {
                    if ((day != -1) || (month != -1) || (year != -1))
                    {
                        numSepChars++;
                        if (numSepChars > MAX_SEP_CHARS_BETWEEN_DATE_ELEMS)
                        {
                            bResetNeeded = true;
                            numSepChars = 0;
                        }
                    }

                    curStr = "";
                    switch (ch)
                    {
                        case ':':
                            {
                                if (!dateSrchInfo.bAllowColons)
                                    bResetNeeded = true;
                                break;
                            }
                        case ',':
                            {
                                commaCount++;
                                if ((!dateSrchInfo.bAllowTwoCommas) && (commaCount > 1))
                                    bResetNeeded = true;
                                break;
                            }
                        case '.':
                            {
                                if (!dateSrchInfo.bAllowDots)
                                    bResetNeeded = true;
                                break;
                            }
                        case '-':
                            {
                                if (numDatesFoundInString == 1)
                                    bRangeIndicatorFound = true;
                                break;
                            }
                    }
                }

                // Check for complete date
                if ((year != -1) && (month != -1) && ((day != -1) || (bMonthFromChars)))
                {
                    // Add result
                    AddCompletedDateToList(s, textBounds, matchFactor, year, month, day, bMonthFromChars, bRangeIndicatorFound, firstMatchPos, 
                                            lastMatchPos, dateSrchInfo, pageIdx+1, datesResult);
                    numDatesFoundInString++;

                    // Start again to see if another date can be found
                    curStr = "";
                    bResetNeeded = true;
                }

                // Restart the process of finding a date if required
                if (bResetNeeded)
                {
                    dateSrchPos = 0;
                    day = -1;
                    month = -1;
                    year = -1;
                    bMonthFromChars = false;
                    strIsDigits = false;
                    firstMatchPos = -1;
                    bResetNeeded = false;
                    commaCount = 0;
                    numSepChars = 0;
                }
            }
        }

        private static void AddCompletedDateToList(string srcStr, DocRectangle textBounds, double matchFactor, int year, int month, int day, bool bMonthFromChars, 
                                bool bRangeIndicatorFound, int firstMatchPos, int lastMatchPos, DateSrchInfo dateSrchInfo, int pageNum, List<ExtractedDate> datesResult)
        {
            double finalMatchFactor = matchFactor;
            ExtractedDate fd = new ExtractedDate();
            if (bRangeIndicatorFound)
                finalMatchFactor += 10;

            // Bump the match factor for dates in the top 40% of page - letterhead dates
            if (textBounds.Y < 40)
                finalMatchFactor += MATCH_FACTOR_BUMP_FOR_TOP_40_PC_OF_PAGE;

            // Year
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
            else
            {
                finalMatchFactor += MATCH_FACTOR_BUMP_FOR_4_DIGIT_YEAR;
            }
                
            // Month
            if (bMonthFromChars)
                finalMatchFactor += MATCH_FACTOR_BUMP_FOR_TEXT_MONTH;

            // Check for bump
            if (dateSrchInfo.bPlusOneMonth)
            {
                month += 1;
                if (month > 12)
                {
                    month = 1;
                    year++;
                }
            }

            // Day
            if (day == -1)
            {
                day = 1;
                fd.dayWasMissing = true;
                finalMatchFactor += MATCH_FACTOR_BUMP_FOR_DAY_MISSING;
            }
            if (day > DateTime.DaysInMonth(year, month))
                day = DateTime.DaysInMonth(year, month);
            if (day < 1)
                day = 1;

            // Create datetime
            DateTime dt = DateTime.MinValue;
            try
            {
                dt = new DateTime(year, month, day);
            }
            catch
            {

            }

            // Add date to list
            fd.foundInText = srcStr;
            fd.pageNum = pageNum;
            fd.posnInText = firstMatchPos;
            fd.matchLength = lastMatchPos-firstMatchPos+1;
            fd.dateTime = dt;
            fd.dateMatchType = ExtractedDate.DateMatchType.LongDate;
            fd.locationOfDateOnPagePercent = textBounds;
            fd.matchFactor = finalMatchFactor;
            datesResult.Add(fd);
        }

        private static DateSrchInfo GetDateSearchInfo(string dateSearchTerm)
        {
            DateSrchInfo dateSrchInfo = new DateSrchInfo();
            // Extract flags
            dateSrchInfo.bIsUsDate = (dateSearchTerm.IndexOf("~USDate", StringComparison.OrdinalIgnoreCase) >= 0);
            dateSrchInfo.bAllowColons = (dateSearchTerm.IndexOf("~AllowColons", StringComparison.OrdinalIgnoreCase) >= 0);
            dateSrchInfo.bAllowDots = (dateSearchTerm.IndexOf("~AllowDots", StringComparison.OrdinalIgnoreCase) >= 0);
            dateSrchInfo.bAllowTwoCommas = (dateSearchTerm.IndexOf("~AllowTwoCommas", StringComparison.OrdinalIgnoreCase) >= 0);
            dateSrchInfo.bPlusOneMonth = (dateSearchTerm.IndexOf("~PlusOneMonth", StringComparison.OrdinalIgnoreCase) >= 0);
            dateSrchInfo.bJoinTextInRect = (dateSearchTerm.IndexOf("~join", StringComparison.OrdinalIgnoreCase) >= 0);
            dateSrchInfo.bNoDateRanges = (dateSearchTerm.IndexOf("~NoDateRanges", StringComparison.OrdinalIgnoreCase) >= 0);
            dateSrchInfo.bLatestDate = (dateSearchTerm.IndexOf("~latest", StringComparison.OrdinalIgnoreCase) >= 0);
            dateSrchInfo.bEarliestDate = (dateSearchTerm.IndexOf("~earliest", StringComparison.OrdinalIgnoreCase) >= 0);

            // Pattern str
            string patternStr = "";
            int squigPos = dateSearchTerm.IndexOf('~');
            if (squigPos >= 0)
                patternStr = dateSearchTerm.Substring(0, squigPos).ToLower();
            // Find order of elements
            bool bDayFound = false;
            bool bMonthFound = false;
            bool bYearFound = false;
            for (int chIdx = 0; chIdx < patternStr.Length; chIdx++)
            {
                char ch = patternStr[chIdx];

                // Handle day pattern = d or dd
                if (!bDayFound && (ch == 'd'))
                {
                    DateElemSrch el = new DateElemSrch();
                    el.dateElType = DateElemSrch.DateElType.DE_DAY;
                    el.isDigits = true;
                    el.minChars = 1;
                    el.maxChars = 2;
                    el.allowLeadingZeroes = false;
                    dateSrchInfo.dateEls.Add(el);
                    bDayFound = true;
                }

                // Handle month pattern = m or mm or mmm or mmmm
                if (!bMonthFound && (ch == 'm'))
                {
                    DateElemSrch el = new DateElemSrch();
                    el.dateElType = DateElemSrch.DateElType.DE_MONTH;
                    el.isDigits = true;
                    el.minChars = 1;
                    el.maxChars = 2;
                    el.allowLeadingZeroes = false;
                    if ((chIdx + 1 < patternStr.Length) && (patternStr[chIdx + 1] == 'm'))
                    {
                        el.allowLeadingZeroes = true;
                        if ((chIdx + 2 < patternStr.Length) && (patternStr[chIdx + 2] == 'm'))
                        {
                            el.isDigits = false;
                            el.minChars = 3;
                            el.maxChars = 3;
                            if ((chIdx + 3 < patternStr.Length) && (patternStr[chIdx + 3] == 'm'))
                            {
                                el.isDigits = false;
                                el.minChars = 3;
                                el.minChars = 9;
                            }
                        }
                    }
                    dateSrchInfo.dateEls.Add(el);
                    bMonthFound = true;
                }

                // Handle year pattern = yy or yyyy
                if (!bYearFound && (ch == 'y'))
                {
                    DateElemSrch el = new DateElemSrch();
                    el.dateElType = DateElemSrch.DateElType.DE_YEAR;
                    el.isDigits = true;
                    el.minChars = 2;
                    el.maxChars = 2;
                    el.allowLeadingZeroes = false;
                    if ((chIdx + 2 < patternStr.Length) && (patternStr[chIdx + 2] == 'y'))
                        el.minChars = 4;
                        el.maxChars = 4;
                    dateSrchInfo.dateEls.Add(el);
                    bYearFound = true;
                }
            }
            return dateSrchInfo;
        }

        private class DateSrchInfo
        {
            public List<DateElemSrch> dateEls = new List<DateElemSrch>();
            public bool bIsUsDate = false;
            public bool bAllowColons = false;
            public bool bAllowTwoCommas = false;
            public bool bAllowDots = false;
            public bool bPlusOneMonth = false;
            public bool bJoinTextInRect = false;
            public bool bNoDateRanges = false;
            public bool bLatestDate = false;
            public bool bEarliestDate = false;
        }

        private class DateElemSrch
        {
            public enum DateElType
            {
                DE_NONE, DE_DAY, DE_MONTH, DE_YEAR
            }
            public DateElType dateElType = DateElType.DE_DAY;
            public bool isDigits = false;
            public int minChars = 1;
            public int maxChars = 2;
            public bool allowLeadingZeroes = true;
        }

        public static void SearchForDateItem2(ScanPages scanPages, string dateSearchTerm, DocRectangle dateDocRect, double matchFactor, List<ExtractedDate> datesResult, int limitToPageNumN = -1)
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
                    yrStr = yrStr.ToLower().Replace("l", "1");
                    yrStr = yrStr.ToLower().Replace("o", "0");           
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
        public int pageNum;
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
