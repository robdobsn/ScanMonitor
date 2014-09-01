using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    class ScanDocLikelyDocType
    {
        class OrderableUniqName
        {
            public string uniqName;
            public string bestDocType;
            public DateTime foundDateTime;
            public int origDocIdx;

            public OrderableUniqName(string iUniqName, string iBestDocType, DateTime iFoundDateTime, int iOrigDocIdx)
            {
                uniqName = iUniqName;
                bestDocType = iBestDocType;
                foundDateTime = iFoundDateTime;
                origDocIdx = iOrigDocIdx;
            }

            public bool IsEqual(OrderableUniqName other)
            {
                if (uniqName != other.uniqName)
                    return false;
                if (bestDocType != other.bestDocType)
                    return false;
                if (foundDateTime != other.foundDateTime)
                    return false;
                if (origDocIdx != other.origDocIdx)
                    return false;
                return true;
            }
        }

        // likely doc type for each unfiled document
        private ScanDocHandler _scanDocHandler = null;
        private DocTypesMatcher _docTypesMatcher = null; 
        private BackgroundWorker _bwDocTypeListUpdateThread;
        private static readonly object _lockForDocTypeListAccess = new object();
        private bool _threadRunning = true;
        private bool _requestDocTypeListUpdate = true;
        private bool _cacheIsDirty = true;
        private Dictionary<string, DocTypeMatchResult> _bestDocTypeMatch = new Dictionary<string,DocTypeMatchResult>();
        private List<OrderableUniqName> _cachedListOfSortedUniqNames = new List<OrderableUniqName>();
        private string _cacheListOrder = "";

        public ScanDocLikelyDocType(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher)
        {
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;

            // List update thread
            _bwDocTypeListUpdateThread = new BackgroundWorker();
            _bwDocTypeListUpdateThread.DoWork += new DoWorkEventHandler(DocTypeListUpdateThread_DoWork);        
                        
            // Run background thread to get unfiled list
            _bwDocTypeListUpdateThread.RunWorkerAsync();
        }

        ~ScanDocLikelyDocType()
        {
            _threadRunning = false;
        }

        private string GetUniqNameFromCacheList(int docIdx)
        {
            lock (_cachedListOfSortedUniqNames)
            {
                if ((docIdx >= 0) && (docIdx < _cachedListOfSortedUniqNames.Count))
                    return _cachedListOfSortedUniqNames[docIdx].uniqName;
                if (_cachedListOfSortedUniqNames.Count != 0)
                    return _cachedListOfSortedUniqNames[0].uniqName;
            }
            return "";
        }

        public string GetUniqNameOfDocToBeFiled(int docIdx, string sortOrder)
        {
            // Get from cached list if possible
            bool retFromCache = false;
            lock (_cachedListOfSortedUniqNames)
            {
                if ((_cacheListOrder == sortOrder) && (!_cacheIsDirty) && (_cachedListOfSortedUniqNames.Count == _scanDocHandler.GetCountOfUnfiledDocs()))
                    retFromCache = true;
            }
            if (retFromCache)
                return GetUniqNameFromCacheList(docIdx);

            // Get the list of uniq names and find best match for each
            List<OrderableUniqName> sortableUniqNames = new List<OrderableUniqName>();
            int origIdx = 0;
            List<string> namesOfDocsToBeFiled = _scanDocHandler.GetCopyOfUnfiledDocsList();
            foreach (string uniqName in namesOfDocsToBeFiled)
            {
                string bestTypeMatch = "";
                DateTime bestTypeDate = DateTime.MinValue;
                lock(_lockForDocTypeListAccess)
                {
                    if (_bestDocTypeMatch.ContainsKey(uniqName))
                    {
                        bestTypeMatch = _bestDocTypeMatch[uniqName].docTypeName;
                        bestTypeDate = _bestDocTypeMatch[uniqName].docDate;
                    }
                }
                OrderableUniqName nt = new OrderableUniqName(uniqName, bestTypeMatch, bestTypeDate, origIdx);
                sortableUniqNames.Add(nt);
                origIdx++;
            }

            // Sort the names as required
            IEnumerable<OrderableUniqName> orderedList = null;
            switch(sortOrder)
            {
                case "original":
                    orderedList = sortableUniqNames.OrderBy(x => x.origDocIdx);
                    break;

                case "docType":
                    orderedList = sortableUniqNames.OrderBy(x => string.IsNullOrWhiteSpace(x.bestDocType)).ThenBy(x => x.bestDocType).ThenBy(x => x.foundDateTime).ThenBy(x => x.uniqName);
                    break;

                case "docDate":
                    orderedList = sortableUniqNames.OrderBy(x => x.foundDateTime).ThenBy(x => x.bestDocType).ThenBy(x => x.uniqName);
                    break;

                default:
                    orderedList = sortableUniqNames.OrderBy(x => x.uniqName).ThenBy(x => x.bestDocType).ThenBy(x => x.foundDateTime);
                    break;
            }

            // Return the appropriate file name
            lock (_cachedListOfSortedUniqNames)
            {
                _cachedListOfSortedUniqNames = orderedList.ToList();
                _cacheIsDirty = false;
                _cacheListOrder = sortOrder;
            }
            return GetUniqNameFromCacheList(docIdx);
        }

        public void UpdateDocInfo(string uniqName)
        {
            ScanDocAllInfo allInfo = _scanDocHandler.GetScanDocAllInfoCached(uniqName);
            if ((allInfo != null) && (allInfo.scanPages != null))
            {
                DocTypeMatchResult matchRes = _docTypesMatcher.GetMatchingDocType(allInfo.scanPages, null);
                lock (_lockForDocTypeListAccess)
                {
                    bool addReqd = false;
                    if (!_bestDocTypeMatch.ContainsKey(uniqName))
                        addReqd = true;
                    else if ((_bestDocTypeMatch[uniqName].docTypeName != matchRes.docTypeName) || (_bestDocTypeMatch[uniqName].docDate != matchRes.docDate))
                        addReqd = true;
                    if (addReqd)
                    {
                        _bestDocTypeMatch.Add(uniqName, matchRes);
                        _cacheIsDirty = true;
                    }
                }
            }
        }

        public void DocTypeAddedOrChanged(string docTypeNameChangedOrAdded)
        {
            // Request an update to best doc type for all docs - as a change like this can make the 
            // best doc change for any document
            _requestDocTypeListUpdate = true;
        }

        private void DocTypeListUpdateThread_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            DateTime lastFullRefreshTime = DateTime.Now;

            while (_threadRunning)
            {
                // Get the current unfiled docs list
                List<string> unfiledUniqNames = _scanDocHandler.GetCopyOfUnfiledDocsList();
                
                // Remove any extraneous entries from the map of best doc types
                lock (_lockForDocTypeListAccess)
                {
                    List<string> keys = _bestDocTypeMatch.Keys.ToList();
                    foreach (string key in keys)
                        if (!unfiledUniqNames.Contains(key))
                            _bestDocTypeMatch.Remove(key);
                }

                // Check for nothing found - if so check back every second
                if (unfiledUniqNames.Count == 0)
                {
                    Thread.Sleep(1000);
                    continue;
                }

                // Check refresh time
                if ((DateTime.Now - lastFullRefreshTime).Seconds > Properties.Settings.Default.DocTypeReCheckSeconds)
                {
                    _requestDocTypeListUpdate = true;
                    lastFullRefreshTime = DateTime.Now;
                }

                // Now go through and get a doc type for any new keys
                bool doFullListUpdate = _requestDocTypeListUpdate;
                _requestDocTypeListUpdate = false;
                foreach (string uniqName in unfiledUniqNames)
                {
                    if (doFullListUpdate || (!_bestDocTypeMatch.ContainsKey(uniqName)))
                    {
                        // Update the cache of best doc type found
                        UpdateDocInfo(uniqName);

                        // See if a new request (new file scanned etc) has been received
                        if (_requestDocTypeListUpdate)
                            break;

                        // Sleep a while
                        Thread.Sleep(100);
                    }
                }

                // Wait for a bit - checking if a prompt has been received
                for (int checkCtr = 0; checkCtr < Properties.Settings.Default.UnfiledDocCheckSeconds * 10; checkCtr++)
                {
                    // See if a request (new file scanned etc) has been received
                    if (_requestDocTypeListUpdate)
                        break;

                    // Sleep a while
                    Thread.Sleep(100);
                }
            }
        }

    }

    
}
