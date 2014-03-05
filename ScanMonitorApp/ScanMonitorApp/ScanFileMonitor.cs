using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using System.Threading;
using System.ComponentModel;

namespace ScanMonitorApp
{
    class ScanFileMonitor
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private List<ScanFolderWatcher> _scanFolderWatchers;
        private List<string> _foldersToMonitor = new List<string>();
        public delegate void ReportStatus(string str);
        private ReportStatus _reportStatusFn;
        private bool _monitorRunning = true;
        private ScanDocHandler _scanDocHandler;
        private bool _testMode = false;
        private static readonly object _lockForDocProcessing = new object();
        private static bool _requestDocProcessingForNewFile = false;
        BackgroundWorker _bwFileMonitorThread;

        public ScanFileMonitor(ReportStatus reportStatusFn, ScanDocHandler scanDocHandler)
        {
            _reportStatusFn = reportStatusFn;
            _scanDocHandler = scanDocHandler;

            // Monitor thread
            _bwFileMonitorThread = new BackgroundWorker();
            _bwFileMonitorThread.WorkerSupportsCancellation = true;
            _bwFileMonitorThread.WorkerReportsProgress = true;
            _bwFileMonitorThread.DoWork += new DoWorkEventHandler(FileMonitorThread_DoWork);
            _bwFileMonitorThread.RunWorkerAsync();
        }

        ~ScanFileMonitor()
        {
            if (_bwFileMonitorThread != null)
                if (_bwFileMonitorThread.WorkerSupportsCancellation)
                    _bwFileMonitorThread.CancelAsync();
            _monitorRunning = false;
        }

        public void Start(List<string> foldersToMonitor, bool testMode)
        {
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
                HandleNewPdfFile(fileName);
            else
                logger.Info("Doc changed but ignored as TEST_MODE {0}", fileName);
        }

        private bool CheckFileReadyToBeAccessed(string fileName)
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

        public bool WaitForFileToBeAccessible(string fileName)
        {
            bool fileCheckOk = false;
            // Verify that the file contains text - wait until it does
            for (int checkIdx = 0; checkIdx < 60; checkIdx++)
            {
                // Check file is ready to be accessed
                fileCheckOk = CheckFileReadyToBeAccessed(fileName);
                if (fileCheckOk)
                    break;

                // Sleep to allow the remote computer to finish processing - it may be doing OCR etc
                Thread.Sleep(1000);
            }
            if (!fileCheckOk)
            {
                logger.Info("File {0} detected but cannot be accessed", fileName);
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

        private void HandleNewPdfFile(string fileName)
        {
            // Set flag to request that any regular processing of files stops while the new file is processed
            _requestDocProcessingForNewFile = true;

            // Try to obtain a lock on the lock object but with a timeout to ensure we don't wait here for a long time
            if(Monitor.TryEnter(_lockForDocProcessing, new TimeSpan(0, 0, 10)))
            {
                try 
                {
                    HandleASinglePdfFile(fileName);
                }
                finally 
                {
                    Monitor.Exit(_lockForDocProcessing);
                }
            }

            // Clear flag indicating request to stop
            _requestDocProcessingForNewFile = false;
        }

        private void HandleASinglePdfFile(string fileName)
        {
            // Wait until file is moveable - or time-out
            if (!WaitForFileToBeAccessible(fileName))
                return;

            // Process Pdf file
            DateTime fileDateTime = File.GetCreationTime(fileName);
            string uniqName = ScanDocInfo.GetUniqNameForFile(fileName, fileDateTime);
            _scanDocHandler.ProcessPdfFile(fileName, uniqName, true, true, true, true, true, true);
        }

        private List<FileSystemInfo> GetListOfFilesInWatchedFolder(string folderName)
        {
            try
            {
                DirectoryInfo di = new DirectoryInfo(folderName);
                FileSystemInfo[] fps = di.GetFileSystemInfos("*.pdf");
                IEnumerable<FileSystemInfo> rslt = fps.OrderBy(f => f.FullName);
                return rslt.ToList<FileSystemInfo>();
            }
            catch (Exception excp)
            {
                logger.Error("Failed to get file list from {0} excp {1}", folderName, excp.Message);
            }
            return new List<FileSystemInfo>();
        }

        public void FileMonitorThread_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            while (_monitorRunning)
            {
                // Check for cancellation
                if ((worker.CancellationPending == true))
                {
                    e.Cancel = true;
                    break;
                }

                // Obtain lock to allow processing
                lock (_lockForDocProcessing)
                {
                    // Check for new files
                    for (int folderIdx = 0; folderIdx < _foldersToMonitor.Count; folderIdx++)
                    {
                        List<FileSystemInfo> fsInfos = GetListOfFilesInWatchedFolder(_foldersToMonitor[folderIdx]);

                        // Check if each file in the folder is already in the database
                        foreach (FileSystemInfo fsi in fsInfos)
                        {
                            // Check for cancellation
                            if ((worker.CancellationPending == true))
                            {
                                e.Cancel = true;
                                break;
                            }

                            DateTime fileDateTime = File.GetCreationTime(fsi.FullName);
                            string uniqName = ScanDocInfo.GetUniqNameForFile(fsi.FullName, fileDateTime);

                            // Check if doc not already in database
                            ScanDocInfo sdi = _scanDocHandler.GetScanDocInfo(uniqName);
                            if (sdi == null)
                            {
                                // Process the doc
                                HandleASinglePdfFile(fsi.FullName);
                            }

                            // Check if we should terminate to allow a new file to be processed
                            if (_requestDocProcessingForNewFile)
                                break;
                        }
                        // Check if we should terminate to allow a new file to be processed
                        if (_requestDocProcessingForNewFile)
                            break;
                    }
                }
                
                // Clear any request
                _requestDocProcessingForNewFile = false;

                // Wait a while to avoid thrashing constantly
                for (int i = 0; i < 600; i++ )
                {
                    // Check for cancellation
                    if ((worker.CancellationPending == true))
                    {
                        e.Cancel = true;
                        break;
                    }
                    Thread.Sleep(1000);
                }
            }
        }
    }
}
