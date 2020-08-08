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
using System.Diagnostics.Tracing;
using System.CodeDom;
using System.Windows.Documents;

namespace ScanMonitorApp
{
    class ScanFileMonitor
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private List<ScanFolderWatcher> _scanFolderWatchers;
        private List<string> _foldersToMonitor = new List<string>();
        private string _localFolderForFiledDocs = "";
        public delegate void ReportStatus(string str);
        private ReportStatus _reportStatusFn;
        private bool _monitorRunning = true;
        private ScanDocHandler _scanDocHandler;
        private bool _testMode = false;
        private static readonly object _lockForDocProcessing = new object();
        private static bool _fileIsBeingProcessed = false;
        BackgroundWorker _bwFileMonitorThread;
        private volatile string _lastFileProcessed = "";
        private volatile string _lastTimeFilesChecked = "";
        private volatile string _lastStatusOfFileProcessing = "";
        private const int MAX_PROC_EVENTS_TO_STORE = 50;
        private volatile List<string> _lastProcessEvents = new List<string>();
        private Dictionary<string, DateTime> _lastDateTimeOnReprocessedDoc = new Dictionary<string, DateTime>();

        public ScanFileMonitor(ReportStatus reportStatusFn, ScanDocHandler scanDocHandler)
        {
            _reportStatusFn = reportStatusFn;
            _scanDocHandler = scanDocHandler;

            // Monitor thread
            _bwFileMonitorThread = new BackgroundWorker();
            _bwFileMonitorThread.WorkerSupportsCancellation = true;
            _bwFileMonitorThread.WorkerReportsProgress = true;
            _bwFileMonitorThread.DoWork += new DoWorkEventHandler(FileMonitorThread_DoWork);
        }

        ~ScanFileMonitor()
        {
            if (_bwFileMonitorThread != null)
                if (_bwFileMonitorThread.WorkerSupportsCancellation)
                    _bwFileMonitorThread.CancelAsync();
            _monitorRunning = false;
        }

        public void Start(List<string> foldersToMonitor, string localFolderForFiledDocs, bool testMode)
        {
            // Monitor folders
            MonitorFolders(foldersToMonitor);
            _monitorRunning = true;
            _testMode = testMode;
            _localFolderForFiledDocs = localFolderForFiledDocs;

            // Run background thread to test for new files
            _bwFileMonitorThread.RunWorkerAsync();
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

        public string GetCurrentInfo()
        {
            string lastProcStr = "Last File Processed: ";
            if (_lastFileProcessed.Length == 0)
                lastProcStr += "None since restart";
            else
                lastProcStr += _lastFileProcessed;
            return lastProcStr + "\n" + _lastTimeFilesChecked + "\n" + _lastStatusOfFileProcessing;
        }

        public string GetLastEvents()
        {
            return procEventsGet();
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
            catch (Exception excp)
            {
                addProcEvent(LogLevel.Error, String.Format("Failed with excp {0}", excp.Message));
                checkOk = false;
            }
            return checkOk;
        }

        public bool WaitForFileToBeAccessible(string fileName)
        {
            bool fileCheckOk = false;
            // Verify that the file contains text - wait until it does
            for (int checkIdx = 0; checkIdx < 20; checkIdx++)
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
                addProcEvent(LogLevel.Debug, String.Format("File {0} detected but cannot be accessed", fileName));
                return false;
            }
            return true;
        }

        private void HandleNewPdfFile(string fileName)
        {
            // Set flag to request that any regular processing of files stops while the new file is processed
            _fileIsBeingProcessed = true;

            // Try to obtain a lock on the lock object but with a timeout to ensure we don't wait here for a long time
            if (Monitor.TryEnter(_lockForDocProcessing, new TimeSpan(0, 0, 10)))
            {
                try
                {
                    HandleASinglePdfFile(fileName, true, false, "");
                }
                finally
                {
                    Monitor.Exit(_lockForDocProcessing);
                }
            }
            else
            {
                addProcEvent(LogLevel.Warn, String.Format("Unable to obtain lock for processing file {0} - will try again later", fileName));
            }

            // Clear flag indicating request to stop
            _fileIsBeingProcessed = false;
        }

        private bool HandleASinglePdfFile(string fileName, bool fromFolderWatcher, bool fileChanged, string curUniqName)
        {
            // Wait until file is moveable - or time-out
            if (!WaitForFileToBeAccessible(fileName))
            {
                addProcEvent(LogLevel.Warn, String.Format("{1} File isn't accessible {0} - will try again later", fileName, fromFolderWatcher ? "Watcher" : "Timed"));
                return false;
            }

            // Process Pdf file
            try
            {
                if (fileChanged)
                {
                    _scanDocHandler.ProcessPdfFile(fileName, curUniqName, true, false, true, true, true, true);
                    addProcEvent(LogLevel.Debug, String.Format("{2} File {0} changed - updated ScanDocInfo - uniqName {1}", fileName, curUniqName, fromFolderWatcher ? "Watcher" : "Timed"));
                }
                else
                {
                    string uniqName = ScanDocInfo.GetUniqNameForFile(fileName);
                    // Check it really is a new file
                    if (_scanDocHandler.ScanDocInfoRecordExists(uniqName))
                        return false;
                    addProcEvent(LogLevel.Debug, String.Format("{2} File {0} is new - creating ScanDocInfo - uniqName {1}", fileName, uniqName, fromFolderWatcher ? "Watcher" : "Timed"));
                    _scanDocHandler.ProcessPdfFile(fileName, uniqName, true, true, true, true, true, true);
                }
                _lastFileProcessed = fileChanged ? "Updated " : "Created " + 
                                System.IO.Path.GetFileName(fileName) + " at " + DateTime.Now.ToLongTimeString() + " on " + DateTime.Now.ToShortDateString();
            }
            catch (Exception excp)
            {
                addProcEvent(LogLevel.Error, String.Format("{1} Failed with excp {0}", excp.Message, fromFolderWatcher ? "Watcher" : "Timed"));
                return false;
            }
            return true;
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
                addProcEvent(LogLevel.Error, String.Format("Failed to get file list from {0} excp {1}", folderName, excp.Message));
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

                // Don't check for files while another is being processed
                int filesCheckedLastPass = 0;
                if (!_fileIsBeingProcessed)
                {
                    // Obtain lock to allow processing
                    lock (_lockForDocProcessing)
                    {
                        // Check for new files
                        for (int folderIdx = 0; folderIdx < _foldersToMonitor.Count; folderIdx++)
                        {
                            List<FileSystemInfo> fsInfos = GetListOfFilesInWatchedFolder(_foldersToMonitor[folderIdx]);
                            filesCheckedLastPass += fsInfos.Count;
                            _lastTimeFilesChecked = "Last check at " + DateTime.Now.ToShortTimeString() + " on " + DateTime.Now.ToShortDateString();

                            // Check if each file in the folder is already in the database
                            foreach (FileSystemInfo fsi in fsInfos)
                            {
                                // Check for cancellation
                                if ((worker.CancellationPending == true))
                                {
                                    e.Cancel = true;
                                    break;
                                }

                                try
                                {
                                    // File unique name
                                    string uniqName = ScanDocInfo.GetUniqNameForFile(fsi.FullName);

                                    // Check if doc already in database
                                    ScanDocInfo sdi = _scanDocHandler.GetScanDocInfo(uniqName);
                                    if (sdi == null)
                                    {
                                        // Process the doc
                                        HandleASinglePdfFile(fsi.FullName, false, false, "");
                                    }
                                    else
                                    {
                                        // Check if already filed
                                        if (_scanDocHandler.AlreadyFiledCheck(fsi.FullName, uniqName))
                                        {
                                            // Move the file as it has been filed
                                            string destFilenameForFiledDoc = Delimon.Win32.IO.Path.Combine(_localFolderForFiledDocs, fsi.Name);
                                            Delimon.Win32.IO.File.Move(fsi.FullName, destFilenameForFiledDoc);
                                            addProcEvent(LogLevel.Info, String.Format("File {0} moved to {1}", fsi.FullName, destFilenameForFiledDoc));
                                        }
                                        else if (ScanDocInfo.CheckFileModified(fsi.FullName, File.GetLastWriteTime(fsi.FullName), uniqName))
                                        {
                                            // Check if already in list of files reprocessed
                                            bool reprocessFile = true;
                                            var lastFileWriteTime = File.GetLastWriteTime(fsi.FullName);
                                            if (_lastDateTimeOnReprocessedDoc.ContainsKey(uniqName))
                                            {
                                                if (_lastDateTimeOnReprocessedDoc[uniqName] == lastFileWriteTime)
                                                    reprocessFile = false;
                                            }
                                            // Update the doc if necessary - this handles the A3 scanner which adds pages to
                                            // existing files
                                            if (reprocessFile)
                                            {
                                                HandleASinglePdfFile(fsi.FullName, false, true, uniqName);
                                                _lastDateTimeOnReprocessedDoc.Add(uniqName, lastFileWriteTime);
                                            }
                                        }
                                    }
                                }
                                catch (Exception excp)
                                {
                                    addProcEvent(LogLevel.Error, String.Format("Failed to process file {0}", excp.Message));
                                }

                                // Check if we should terminate to allow a new file to be processed
                                if (_fileIsBeingProcessed)
                                    break;
                            }
                            // Check if we should terminate to allow a new file to be processed
                            if (_fileIsBeingProcessed)
                                break;
                        }
                    }
                }

                // Store info on number of files checked on the last pass
                _lastStatusOfFileProcessing = filesCheckedLastPass.ToString() + " unfiled on last pass";

                // Wait a while to avoid thrashing constantly
                for (int i = 0; i < Properties.Settings.Default.FolderMonitorSeconds; i++)
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
        void addProcEvent(LogLevel logLevel, string eventInfo)
        {
            _lastProcessEvents.Add(eventInfo);
            if (_lastProcessEvents.Count > MAX_PROC_EVENTS_TO_STORE)
            {
                _lastProcessEvents.RemoveAt(0);
            }
            logger.Log(logLevel, eventInfo);
        }

        string procEventsGet()
        {
            string evtListStr = "";
            foreach (var evtStr in _lastProcessEvents)
            {
                evtListStr += evtStr + "\n";
            }
            return evtListStr;
        }

    }
}
