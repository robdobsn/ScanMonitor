using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    class ScanDocInfoCache
    {
        // Cache scan and filing info
        private Dictionary<string, ScanDocAllInfo> _cacheScanDocAllInfo = new Dictionary<string, ScanDocAllInfo>();
        private ScanDocHandler _scanDocHandler = null;

        public ScanDocInfoCache(ScanDocHandler scanDocHandler)
        {
            _scanDocHandler = scanDocHandler;
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
    }

}
