using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using System.Text.RegularExpressions;

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

        public DocTypeMatchResult GetMatchingDocType(ScanDocAllInfo scanDocAllInfo)
        {
            // Setup db connection
            var server = _dbClient.GetServer();
            var database = server.GetDatabase("ScanDocsDb"); // the name of the database
            var collection_doctypes = database.GetCollection<DocType>("doctypes");

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

        public bool MatchAgainstDocText(DocPatternText patternText, List<ScanDocAllInfo.ScanPageText> scanPages)
        {
            bool bFound = false;
            for (int pageIdx = 0; pageIdx < scanPages.Count; pageIdx++)
            {
                ScanDocAllInfo.ScanPageText scanPageText = scanPages[pageIdx];
                foreach (ScanDocAllInfo.ScanTextElem textElem in scanPageText.textElems)
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
            matchResult.matchesMustHaveTexts = (docType.mustHaveTexts.Count > 0) && (scanDocAllInfo.numPagesWithText > 0);
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
            foreach (DocPatternText patternText in docType.mustNottHaveText)
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
    }
}
