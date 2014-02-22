using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using System.Threading;

namespace ScanMonitorApp
{
    class ScanFileMonitor
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private List<ScanFolderWatcher> _scanFolderWatchers;
        private List<string> _foldersToMonitor = new List<string>();
        public delegate void ReportStatus(string str);
        private ReportStatus _reportStatusFn;
        private bool _monitorRunning = false;
        private string _pendingDocFolder;
        private ScanDocHandler _scanDocHandler;
        private bool _testMode = false;

        public ScanFileMonitor(ReportStatus reportStatusFn, ScanDocHandler scanDocHandler)
        {
            _reportStatusFn = reportStatusFn;
            _scanDocHandler = scanDocHandler;
        }

        public void Start(List<string> foldersToMonitor, string pendingDocFolder, bool testMode)
        {
            _pendingDocFolder = pendingDocFolder;
            MonitorFolders(foldersToMonitor);
            _monitorRunning = true;
            _testMode = testMode;
        }

        public void Stop()
        {
            _monitorRunning = false;
        }

        public int MonitorFolders(List<string> foldersToMonitor)
        {
            // Monitor folders for file creation
            int watcherCount = 0;
            _foldersToMonitor = foldersToMonitor;
            _scanFolderWatchers = new List<ScanFolderWatcher>();
            foreach (string folder in _foldersToMonitor)
            {
                ScanFolderWatcher watcher = new ScanFolderWatcher();
                if (watcher.WatchFolder(folder, WatchFolderChanged))
                {
                    _scanFolderWatchers.Add(watcher);
                    watcherCount++;
                }
            }
            return watcherCount;
        }

        public void WatchFolderChanged(string fileName, WatcherChangeTypes changeInfo)
        {
            if (!_testMode)
                MoveAndProcessPdfFile(fileName);
            else
                logger.Info("Doc changed but ignored as TEST_MODE {0}", fileName);
        }

        private bool CheckFileReadyToMove(string fileName)
        {
            // Check the file is writeable
            bool checkOk = false;
            try
            {
                if (File.Exists(fileName))
                {
                    using (FileStream f = new FileStream(fileName, FileMode.Open, FileAccess.Write, FileShare.None))
                    {
                        checkOk = true;
                    }
                }
            }
            catch
            {
                checkOk = false;
            }
            return checkOk;
        }

        public bool WaitForFileToBeMoveable(string fileName)
        {
            bool fileCheckOk = false;
            // Verify that the file contains text - wait until it does
            for (int checkIdx = 0; checkIdx < 20; checkIdx++)
            {
                // Check file is ready to be moved
                fileCheckOk = CheckFileReadyToMove(fileName);
                if (fileCheckOk)
                    break;

                // Sleep to allow the remote computer to finish processing
                Thread.Sleep(1000);
            }
            if (!fileCheckOk)
            {
                logger.Info("File {0} detected but cannot be moved", fileName);
                return false;
            }
            return true;
        }

        public bool MoveFile(string srcName, string destName, ref string errStr)
        {
            bool bResult = false;
            try
            {
                File.Move(srcName, destName);
                bResult = true;
            }
            catch (Exception e)
            {
                errStr = e.Message;
            }
            return bResult;
        }

        private string MoveFileToPendingFolder(string fileName)
        {
            // Move the file
            string statusStr = "";
            string destFileName = Path.Combine(_pendingDocFolder, Path.GetFileName(fileName));

            // Check if destination exists
            bool bMoveFile = true;
            try
            {
                if (File.Exists(destFileName))
                {
                    bMoveFile = false;
                    FileInfo fi1 = new FileInfo(fileName);
                    FileInfo fi2 = new FileInfo(destFileName);
                    if (fi1.Length == fi2.Length)
                    {
                        // Assume identical so delete
                        File.Delete(fileName);
                        logger.Info("A suspected duplicate file found {0} - deleted", fileName);
                    }
                    else
                    {
                        bMoveFile = true;
                        // Try to make the name unique
                        destFileName = Path.Combine(_pendingDocFolder, Path.GetFileNameWithoutExtension(fileName) + "_A", Path.GetExtension(fileName));
                        if (File.Exists(destFileName))
                        {
                            logger.Info("File {0} name conflicts with {1} cannot copy", fileName, destFileName);
                            bMoveFile = false;
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Error in testing file (0) duplicate {1}", fileName, excp.Message);
            }

            // Check if file to be moved
            if (bMoveFile)
            {
                bool bOk = MoveFile(fileName, destFileName, ref statusStr);
                if (!bOk)
                {
                    logger.Info("File {0} failed to move to pending, excp {1}", fileName, statusStr);
                    return "";
                }
                logger.Info("File {0} moved to pending ok", fileName);
            }
            return destFileName;
        }

        public void MoveAndProcessPdfFile(string fileName)
        {
            // Wait until file is moveable - or time-out
            if (!WaitForFileToBeMoveable(fileName))
                return;

            // Move file to pending folder
            string destFileName = MoveFileToPendingFolder(fileName);
            if (destFileName == "")
                return;

            // Process Pdf file
            _scanDocHandler.ProcessPdfFile(destFileName);
        }

        public void FileMonitorThread()
        {
            while (_monitorRunning)
            {
                Thread.Sleep(5000);
            }

        }

        public void Test1()
        {
            // _scanDocHandler.ProcessPdfFile(@"M:\PendingFiling\Scans\2014_02_04_11_22_27.pdf");
            _scanDocHandler.ProcessPdfFile(@"C:\Users\Rob\Documents\20140209 Train\Scanning\TestFiles\2014_02_04_17_14_50.pdf");
        }
        public void Test2()
        {
            _scanDocHandler.ProcessPdfFile(@"M:\PendingFiling\Scans\2014_02_04_14_32_11.pdf");
        }
        public void Test3()
        {
            _scanDocHandler.ProcessPdfFile(@"M:\PendingFiling\Scans\2014_02_04_14_33_12.pdf");
        }

    }
}
