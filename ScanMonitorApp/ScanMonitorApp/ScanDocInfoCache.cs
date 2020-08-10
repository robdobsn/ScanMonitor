using MongoDB.Driver;
using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    class ScanDocInfoCache
    {
        // Cache scan and filing info
        private Dictionary<string, ScanDocAllInfo> _cacheScanDocAllInfo = new Dictionary<string, ScanDocAllInfo>();
        private ScanDocHandler _scanDocHandler = null;
        private List<string> _unfiledDocsUniqNames = new List<string>();
        private string _unfiledDocsHashStr = "";
        private BackgroundWorker _bwUnfiledListUpdateThread;
        private static readonly object _lockForUnfiledListAccess = new object();
        private bool _threadRunning = true;
        private bool _requestUnfiledListUpdate = false;
        private bool _scanDocAllInfoPrimed = false;
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public ScanDocInfoCache(ScanDocHandler scanDocHandler)
        {
            _scanDocHandler = scanDocHandler;

            // List update thread
            _bwUnfiledListUpdateThread = new BackgroundWorker();
            _bwUnfiledListUpdateThread.DoWork += new DoWorkEventHandler(UnfiledListUpdateThread_DoWork);        
                        
            // Run background thread to get unfiled list
            _bwUnfiledListUpdateThread.RunWorkerAsync();
        }

        ~ScanDocInfoCache()
        {
            _threadRunning = false;
        }

        public void RequestUnfiledListUpdate()
        {
            _requestUnfiledListUpdate = true;
        }

        public ScanDocAllInfo GetScanDocAllInfo(string uniqName)
        {
            ScanDocAllInfo sdAllInfo = null;

            if (_cacheScanDocAllInfo.ContainsKey(uniqName))
            {
                sdAllInfo = _cacheScanDocAllInfo[uniqName];
            }
            else
            {
                sdAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
                if (sdAllInfo != null)
                    try
                    {
                        if (!_cacheScanDocAllInfo.ContainsKey(uniqName))
                            _cacheScanDocAllInfo.Add(uniqName, sdAllInfo);
                    }
                    catch (Exception)
                    {
                        logger.Error("Key {uniqName} already cached");
                    }
            }
            return sdAllInfo;
        }

        public void UpdateDocInfo(string uniqName)
        {
            if (_cacheScanDocAllInfo.ContainsKey(uniqName))
            {
                ScanDocAllInfo sdAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
                if (sdAllInfo != null)
                    _cacheScanDocAllInfo[uniqName] = sdAllInfo;
                else
                    _cacheScanDocAllInfo.Remove(uniqName);
            }
            else
            {
                ScanDocAllInfo sdAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
                if (sdAllInfo != null)
                    _cacheScanDocAllInfo.Add(uniqName, sdAllInfo);
            }
        }

        public List<string> GetListOfUnfiledDocUniqNames()
        {
            List<string> listCopy = new List<string>();
            lock (_lockForUnfiledListAccess)
            {
                listCopy = new List<string>(_unfiledDocsUniqNames);
            }
            return listCopy;
        }

        private async Task<List<string>> GetListOfScanDocNames()
        {
            // Get doc uniq names for scanned docs
            IMongoCollection<ScanDocInfo> collection_sdinfo = _scanDocHandler.GetDocInfoCollection();
            List<string> scannedDocUniqNames = new List<string>();
            using (IAsyncCursor<ScanDocInfo> cursor = await collection_sdinfo.FindAsync(_ => true))
            {
                while (await cursor.MoveNextAsync())
                {
                    IEnumerable<ScanDocInfo> batch = cursor.Current;
                    foreach (ScanDocInfo sdi in batch)
                    {
                        scannedDocUniqNames.Add(sdi.uniqName);
                    }
                }
            }
            return scannedDocUniqNames;
        }

        private async Task<List<string>> GetListOfFiledDocNames()
        {
            // Get doc uniq names for filed docs
            IMongoCollection<FiledDocInfo> collection_fdinfo = _scanDocHandler.GetFiledDocsCollection();
            List<string> filedDocUniqNames = new List<string>();
            using (IAsyncCursor<FiledDocInfo> cursor = await collection_fdinfo.FindAsync(_ => true))
            {
                while (await cursor.MoveNextAsync())
                {
                    IEnumerable<FiledDocInfo> batch = cursor.Current;
                    foreach (FiledDocInfo fdi in batch)
                    {
                        filedDocUniqNames.Add(fdi.uniqName);
                    }
                }
            }
            return filedDocUniqNames;
        }

        private void UnfiledListUpdateThread_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            while (_threadRunning)
            {
                // Get doc uniq names for scanned docs
                Task<List<string>> scanDocNamesRslt = GetListOfScanDocNames();
                scanDocNamesRslt.Wait(60000);
                List<string> scannedDocUniqNames = scanDocNamesRslt.Result;

                // Get doc uniq names for filed docs
                Task<List<string>> filedDocNamesRslt = GetListOfFiledDocNames();
                filedDocNamesRslt.Wait(60000);
                List<string> filedDocUniqNames = filedDocNamesRslt.Result;

                // Create list of unfiled doc uniq names
                List<string> unfiledDocs = scannedDocUniqNames.Except(filedDocUniqNames).ToList();

                // Obtain lock to access
                lock (_lockForUnfiledListAccess)
                {
                    _unfiledDocsUniqNames = unfiledDocs;
                    if (unfiledDocs.Count > 1000)
                        _unfiledDocsHashStr = unfiledDocs.Count.ToString() + unfiledDocs[0] + unfiledDocs[unfiledDocs.Count - 1];
                    else
                        _unfiledDocsHashStr = string.Join("", unfiledDocs);
                }

                // See if the ScanDocAllInfo cache needs to be primed
                if (!_scanDocAllInfoPrimed)
                {
                    _scanDocAllInfoPrimed = true;
                    for (int docIdx = 0; docIdx < GetCountOfUnfiledDocs(); docIdx++)
                    {
                        string uniqName = GetUniqNameOfDocToBeFiled(docIdx);
                        GetScanDocAllInfo(uniqName);
                    }
                }

                // Debug
                logger.Info("UnfiledListUpdateCompleted");

                // Wait for a bit - checking if a prompt has been received
                for (int checkCtr = 0; checkCtr < Properties.Settings.Default.UnfiledDocCheckSeconds * 10; checkCtr++)
                {
                    // See if a request (new file scanned etc) has been received
                    if (_requestUnfiledListUpdate)
                    {
                        logger.Info("UnfiledListUpdateStarted");
                        _requestUnfiledListUpdate = false;
                        break;
                    }

                    // Sleep a while
                    Thread.Sleep(100);
                }
            }
        }

        public int GetCountOfUnfiledDocs()
        {
            int listCount = 0;
            lock (_lockForUnfiledListAccess)
            {
                listCount = _unfiledDocsUniqNames.Count();
            }
            return listCount;
        }

        public string GetHashOfUnfiledDocs()
        {
            string rtnStr = "";
            lock (_lockForUnfiledListAccess)
            {
                rtnStr = _unfiledDocsHashStr;
            }
            return rtnStr;
        }

        public string GetUniqNameOfDocToBeFiled(int docIdx)
        {
            string uniqName = "";
            lock (_lockForUnfiledListAccess)
            {
                // Handle range errors
                if (docIdx < 0)
                    docIdx = 0;
                if ((docIdx >= 0) && (docIdx < _unfiledDocsUniqNames.Count()))
                     uniqName = _unfiledDocsUniqNames[docIdx];
            }
            return uniqName;
        }

        public void RemoveDocFromUnfiledCache(string uniqName)
        {
            lock (_lockForUnfiledListAccess)
            {
                _unfiledDocsUniqNames.Remove(uniqName);
            }            
        }

        public void AddDocToUnfiledCache(string uniqName)
        {
            lock (_lockForUnfiledListAccess)
            {
                if (!_unfiledDocsUniqNames.Contains(uniqName))
                    _unfiledDocsUniqNames.Add(uniqName);
            }
        }

    }

}
