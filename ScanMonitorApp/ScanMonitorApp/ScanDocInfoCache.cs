using MongoDB.Driver;
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
        private List<string> _uniqNamesOfDocsToBeFiled = new List<string>();
        private BackgroundWorker _bwUnfiledListUpdateThread;
        private static readonly object _lockForUnfiledListAccess = new object();
        private bool _threadRunning = true;
        private bool _requestUnfiledListUpdate = false;
        private bool _scanDocAllInfoPrimed = false;

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
                    _cacheScanDocAllInfo.Add(uniqName, sdAllInfo);
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
                listCopy = new List<string>(_uniqNamesOfDocsToBeFiled);
            }
            return listCopy;
        }

        public void UnfiledListUpdateThread_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            while (_threadRunning)
            {
                // Get doc uniq names for scanned docs
                MongoCollection<ScanDocInfo> collection_sdinfo = _scanDocHandler.GetDocInfoCollection();
                MongoCursor<ScanDocInfo> scannedDocs = collection_sdinfo.FindAll();
                List<string> scannedDocUniqNames = new List<string>();
                foreach (ScanDocInfo sdi in scannedDocs)
                    scannedDocUniqNames.Add(sdi.uniqName);

                // Get doc uniq names for filed docs
                MongoCollection<FiledDocInfo> collection_fdinfo = _scanDocHandler.GetFiledDocsCollection();
                MongoCursor<FiledDocInfo> filedDocs = collection_fdinfo.FindAll();
                List<string> filedDocUniqNames = new List<string>();
                foreach (FiledDocInfo fdi in filedDocs)
                    filedDocUniqNames.Add(fdi.uniqName);

                // Create list of unfiled doc uniq names
                List<string> unfiledDocs = scannedDocUniqNames.Except(filedDocUniqNames).ToList();

                // Obtain lock to access
                lock (_lockForUnfiledListAccess)
                {
                    _uniqNamesOfDocsToBeFiled = unfiledDocs;
                }

                // See if the ScanDocAllInfo cache needs to be primed
                if (!_scanDocAllInfoPrimed)
                {
                    for (int docIdx = 0; docIdx < GetCountOfUnfiledDocs(); docIdx++)
                    {
                        string uniqName = GetUniqNameOfDocToBeFiled(docIdx);
                        GetScanDocAllInfo(uniqName);
                    }
                    _scanDocAllInfoPrimed = true;
                }

                // Wait for a bit - checking if a prompt has been received
                for (int checkCtr = 0; checkCtr < Properties.Settings.Default.UnfiledDocCheckSeconds * 10; checkCtr++)
                {
                    // See if a request (new file scanned etc) has been received
                    if (_requestUnfiledListUpdate)
                    {
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
                listCount = _uniqNamesOfDocsToBeFiled.Count();
            }
            return listCount;
        }

        public string GetUniqNameOfDocToBeFiled(int docIdx)
        {
            string uniqName = "";
            lock (_lockForUnfiledListAccess)
            {
                // Handle range errors
                if (docIdx < 0)
                    docIdx = 0;
                if ((docIdx >= 0) && (docIdx < _scanDocHandler.GetCountOfUnfiledDocs()))
                     uniqName = _uniqNamesOfDocsToBeFiled[docIdx];
            }
            return uniqName;
        }

        public void RemoveDocFromUnfiledCache(string uniqName)
        {
            lock (_lockForUnfiledListAccess)
            {
                _uniqNamesOfDocsToBeFiled.Remove(uniqName);
            }            
        }

    }

}
