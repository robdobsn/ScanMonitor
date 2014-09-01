using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MahApps.Metro.Controls;
using NLog;
using System.IO;
using System.Windows.Forms;
using System.Reflection;
using System.ComponentModel;
using System.Collections;
using System.Net;
using System.Collections.Specialized;
using System.Web;
using System.Threading;
using System.Security.Cryptography;
using MongoDB.Driver;

namespace ScanMonitorApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private const bool TEST_MODE = false;
        BackgroundWorker _bwThread_forAuditLoading;
        BackgroundWorker _bwThread_forFileHashCreation = null;

//        private List<string> foldersToMonitor = new List<string> { Properties.Settings.Default.FoldersToMonitor };

        private ScanDocHandlerConfig _scanDocHandlerConfig = new ScanDocHandlerConfig(Properties.Settings.Default.DocAdminImgFolderBase,
            Properties.Settings.Default.MaxPagesForImages,
            Properties.Settings.Default.MaxPagesForText,
            Properties.Settings.Default.DbNameForDocs,
            Properties.Settings.Default.DbCollectionForDocInfo,
            Properties.Settings.Default.DbCollectionForDocPages,
            Properties.Settings.Default.DbCollectionForFiledDocs,
            Properties.Settings.Default.DbCollectionForExistingFiles
            );

        private static Logger logger = LogManager.GetCurrentClassLogger();
        private DocTypesMatcher _docTypesMatcher;
        private ScanFileMonitor _scanFileMonitor = null;
        private ScanDocHandler _scanDocHandler;

#if USE_NOTIFY_ICON
        private System.Windows.Forms.NotifyIcon _notifyIcon;

        private void InitNotifyIcon()
        {
            // Notify icon
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Icon = ScanMonitorApp.Properties.Resources.simley64;
            _notifyIcon.Visible = true;
            _notifyIcon.MouseUp +=
                new System.Windows.Forms.MouseEventHandler(delegate(object sender, System.Windows.Forms.MouseEventArgs args)
                {
                    if (args.Button == MouseButtons.Left)
                    {
                        if (!this.IsVisible)
                            ShowPopupWindow();
                        else
                            HidePopupWindow();
                    }
                    else
                    {
                        System.Windows.Forms.ContextMenu cm = new System.Windows.Forms.ContextMenu();
                        cm.MenuItems.Add("Exit...", new System.EventHandler(ExitApp));
                        _notifyIcon.ContextMenu = cm;
                        MethodInfo mi = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
                        mi.Invoke(_notifyIcon, null);
                    }
                });
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _notifyIcon.Visible = false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            Properties.Settings.Default.Save();
            if (_scanFileMonitor != null)
                _scanFileMonitor.Stop();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                HidePopupWindow();
            }

            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;

            base.OnStateChanged(e);
        }

        private void BringWindowToFront()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            this.Topmost = true;
            this.Topmost = false;
            this.Focus();
        }

        private void ShowPopupWindow()
        {
            BringWindowToFront();
        }

        private void HidePopupWindow()
        {
            this.Hide();
        }
#else
        private void InitNotifyIcon()
        {
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            Properties.Settings.Default.Save();
            if (_scanFileMonitor != null)
                _scanFileMonitor.Stop();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
        }
#endif

        public void ExitApp(object sender, EventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }


        public void AddToStatusText(string str)
        {
            const int MAX_LINES_IN_STATUS = 20;
            string[] strs = statusText.Content.ToString().Split('\n');
            if (strs.Length > MAX_LINES_IN_STATUS)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = strs.Length - MAX_LINES_IN_STATUS + 1; i < strs.Length; i++)
                    sb.Append(strs[i] + "\n");
                statusText.Content = sb.ToString();
            }
            statusText.Content += str + "\n";
        }

        public MainWindow()
        {
            InitializeComponent();
            InitNotifyIcon();
            logger.Info("App Started");

            // Document matcher
            _docTypesMatcher = new DocTypesMatcher();
            if (!_docTypesMatcher.Setup())
            {
                MessageBoxButton btnMessageBox = MessageBoxButton.OK;
                MessageBoxImage icnMessageBox = MessageBoxImage.Error;
                MessageBoxResult rsltMessageBox = System.Windows.MessageBox.Show("Database may not be started - cannot continue", "Database problem", btnMessageBox, icnMessageBox);
                System.Windows.Application.Current.Shutdown();
                return;
            }

            // Scanned document handler
            _scanDocHandler = new ScanDocHandler(AddToStatusText, _docTypesMatcher, _scanDocHandlerConfig);

            // Scan folder watcher
            statusRunningMonitor.Content = "This PC is " + System.Environment.MachineName;
            if (Properties.Settings.Default.PCtoRunMonitorOn.Trim() == System.Environment.MachineName.Trim())
            {
                _scanFileMonitor = new ScanFileMonitor(AddToStatusText, _scanDocHandler);
                string[] foldersToMonitorArray = Properties.Settings.Default.FoldersToMonitor.Split(';');
                List<string> foldersToMonitor = new List<string>();
                foreach (string folder in foldersToMonitorArray)
                {
                    try
                    {
                        if (System.IO.Directory.Exists(folder))
                        {
                            foldersToMonitor.Add(folder);
                            continue;
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Watch folder {0} exception {1}", folder, excp);
                    }
                    AddToStatusText("Watch folder not found " + folder);
                }
                _scanFileMonitor.Start(foldersToMonitor, TEST_MODE);
                statusRunningMonitor.Content += " and is Running Folder Monitor";
            }

            // Start the web server
            WebServer ws = new WebServer("http://localhost:8080");
            ws.RegisterEndPoint(new WebEndPoint_ScanDocs(_scanDocHandler));
            ws.RegisterEndPoint(new WebEndPoint_DocTypes(_docTypesMatcher));
            ws.Run();
         }

        private void butAddOldDocTypes_Click(object sender, RoutedEventArgs e)
        {
            // Configure open file dialog box
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.FileName = Properties.Settings.Default.OldRulesFile;
            dlg.InitialDirectory = System.IO.Path.GetDirectoryName(dlg.FileName);
            dlg.DefaultExt = ".xml"; // Default file extension
            dlg.Filter = "XML documents (.xml)|*.xml"; // Filter files by extension 

            // Show open file dialog box
            Nullable<bool> result = dlg.ShowDialog();

            // Process open file dialog box results 
            if (result == true)
            {
                // Open document 
                string filename = dlg.FileName;
                MigrateFromOldApp.AddOldDocTypes(filename, _docTypesMatcher);
            }
        }

        private void butViewAuditData_Click(object sender, RoutedEventArgs e)
        {
            AuditView av = new AuditView(_scanDocHandler, _docTypesMatcher);
            av.ShowDialog();
        }

        private void butViewDocTypes_Click(object sender, RoutedEventArgs e)
        {
            DocTypeView dtv = new DocTypeView(_scanDocHandler, _docTypesMatcher);
            dtv.ShowDocTypeList("", null, null);
            dtv.ShowDialog();
        }

        private void butAddOldLogRecs_Click(object sender, RoutedEventArgs e)
        {
            // Configure open file dialog box
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.FileName = System.IO.Path.GetFileName(Properties.Settings.Default.OldScanLogFile);
            dlg.InitialDirectory = System.IO.Path.GetDirectoryName(Properties.Settings.Default.OldScanLogFile);
            dlg.DefaultExt = ".log"; // Default file extension
            dlg.Filter = "Log documents (.log)|*.log"; // Filter files by extension 

            // Show open file dialog box
            Nullable<bool> result = false;
            try
            {
                result = dlg.ShowDialog();
            }
            catch(Exception excp)
            {
                logger.Error("Exception {0}", excp.Message);
            }

            // Process open file dialog box results 
            if (result == true)
            {
                // Open document 
                string filename = dlg.FileName;

                // Matcher thread
                _bwThread_forAuditLoading = new BackgroundWorker();
                _bwThread_forAuditLoading.WorkerSupportsCancellation = false;
                _bwThread_forAuditLoading.WorkerReportsProgress = false;
                _bwThread_forAuditLoading.DoWork += new DoWorkEventHandler(LoadAuditFileToDb_DoWork);
                _bwThread_forAuditLoading.RunWorkerCompleted += new RunWorkerCompletedEventHandler(LoadAuditFileToDb_RunWorkerCompleted);
                _bwThread_forAuditLoading.RunWorkerAsync(filename);
                butAddOldLogRecs.IsEnabled = false;
            }
        }

        private void LoadAuditFileToDb_DoWork(object sender, DoWorkEventArgs e)
        {
            string filename = (string)e.Argument;
            MigrateFromOldApp.LoadAuditFileToDb(filename, _scanDocHandler);
        }

        private void LoadAuditFileToDb_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            butAddOldLogRecs.IsEnabled = true;
        }

        private void butViewScanFiling_Click(object sender, RoutedEventArgs e)
        {
            DocFilingView dfv = new DocFilingView(_scanDocHandler, _docTypesMatcher);
            dfv.ShowDialog();
        }

        private void btnMigrate1_Click(object sender, RoutedEventArgs e)
        {
            MigrateFromOldApp.ReplaceDocTypeThumbnailStrs(_docTypesMatcher);
        }

        private void butViewSubstMacros_Click(object sender, RoutedEventArgs e)
        {
            PathSubstView ptv = new PathSubstView(_docTypesMatcher);
            ptv.ShowDialog();
        }

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Thread.Sleep(2000);
            DocFilingView dfv = new DocFilingView(_scanDocHandler, _docTypesMatcher);
            dfv.ShowDialog();
        }

        private void AddFileToDb(string filename, byte[] md5Hash, long fileLength)
        {
            ExistingFileInfoRec ir = new ExistingFileInfoRec();
            ir.filename = filename;
            ir.md5Hash = md5Hash;
            ir.fileLength = fileLength;
            _scanDocHandler.AddExistingFileRecToMongo(ir);
        }

        string[] fileFilters = new string[] { "*.pdf", "*.jpg", "*.png" };

        private void RecurseFoldersAddingFilesToDb(string startingFolder, BackgroundWorker worker, DoWorkEventArgs e, int totalNumFilesEstimate, ref int filesAdded)
        {
            try
            {
                if (!Directory.Exists(startingFolder))
                    return;
                foreach (string fileFilter in fileFilters)
                {
                    foreach (string f in Directory.GetFiles(startingFolder, fileFilter))
                    {
                        // Check for cancel
                        if ((worker.CancellationPending == true))
                        {
                            e.Cancel = true;
                            object[] rslt = {"Cancelled", filesAdded} ;
                            e.Result = rslt;
                            return;
                        }
                        using (var md5 = MD5.Create())
                        {
                            using (var stream = File.OpenRead(f))
                            {
                                AddFileToDb(f, md5.ComputeHash(stream), stream.Length);
                                filesAdded++;
                            }
                        }
                    }
                }
                foreach (string d in Directory.GetDirectories(startingFolder))
                {
                    RecurseFoldersAddingFilesToDb(d, worker, e, totalNumFilesEstimate, ref filesAdded);
                    worker.ReportProgress((int)(5 + (filesAdded * 95 / totalNumFilesEstimate)));
                    if ((worker.CancellationPending == true))
                    {
                        e.Cancel = true;
                        object[] rslt = { "Cancelled", filesAdded };
                        return;
                    }
                }
            }
            catch (System.Exception excpt)
            {
                logger.Error("Failed to create MD5 {0}", excpt.Message);
                object[] rslt = { "Error - check log", filesAdded };
                e.Result = rslt;
            }
            object[] finalRslt = { String.Format("Added {0} files", filesAdded), filesAdded };
            e.Result = finalRslt;
        }

        private void EstimateNumFilesToAdd(string startingFolder, BackgroundWorker worker, DoWorkEventArgs e, ref int numFilesFound)
        {
            try
            {
                if (!Directory.Exists(startingFolder))
                    return;
                foreach (string fileFilter in fileFilters)
                    numFilesFound += Directory.GetFiles(startingFolder, fileFilter).Length;
                foreach (string d in Directory.GetDirectories(startingFolder))
                {
                    EstimateNumFilesToAdd(d, worker, e, ref numFilesFound);
                    if ((worker.CancellationPending == true))
                    {
                        e.Cancel = true;
                        e.Result = "Cancelled";
                        return;
                    }
                }
            }
            catch (System.Exception excpt)
            {
                logger.Error("Error estimating number of files already filed", excpt.Message);
                e.Result = "Error - check log";
            }
        }

        private void butRecomputeMD5_Click(object sender, RoutedEventArgs e)
        {
            if ((_bwThread_forFileHashCreation != null) && (_bwThread_forFileHashCreation.IsBusy))
            {
                if (butRecomputeMD5.Content.ToString().ToLower().Contains("cancel"))
                {
                    _bwThread_forAuditLoading.CancelAsync();
                    hashCreateStatus.Content = "Cancelling...";
                }
                return;
            }

            // Go through all folders in the filing area and compute MD5 hashes for them
            // Matcher thread
            _bwThread_forFileHashCreation = new BackgroundWorker();
            _bwThread_forFileHashCreation.WorkerSupportsCancellation = true;
            _bwThread_forFileHashCreation.WorkerReportsProgress = true;
            _bwThread_forFileHashCreation.DoWork += new DoWorkEventHandler(FileHashCreation_DoWork);
            _bwThread_forFileHashCreation.RunWorkerCompleted += new RunWorkerCompletedEventHandler(FileHashCreation_RunWorkerCompleted);
            _bwThread_forFileHashCreation.ProgressChanged += new ProgressChangedEventHandler(FileHashCreation_ProgressChanged);
            _bwThread_forFileHashCreation.RunWorkerAsync(Properties.Settings.Default.FoldersToSearchForFiledDocs);

            // Change button to cancel
            butRecomputeMD5.Content = "Cancel MD5";
            hashCreateStatus.Content = "Busy...";
        }

        private void FileHashCreation_DoWork(object sender, DoWorkEventArgs e)
        {
            string startFolders = (string)e.Argument;
            string[] startFolderList = startFolders.Split(';');
            BackgroundWorker worker = sender as BackgroundWorker;

            // Empty database initially
            _scanDocHandler.EmptyExistingFileRecDB();

            // Process
            int numFilesFound = 0;
            int folderIdx = 0;
            foreach (string startFolder in startFolderList)
            {
                worker.ReportProgress((int)((folderIdx+1) * 5 / startFolderList.Length));
                EstimateNumFilesToAdd(startFolder, worker, e, ref numFilesFound);
                folderIdx++;
            }

            int filesAdded = 0;
            foreach (string startFolder in startFolderList)
            {
                RecurseFoldersAddingFilesToDb(startFolder, worker, e, numFilesFound, ref filesAdded);
                if (e.Cancel)
                    break;
            }
        }

        private void FileHashCreation_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progBar.Value = (e.ProgressPercentage);
        }        

        private void FileHashCreation_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            object[] rslt = (object[])e.Result;
            string rsltMessage = (string)rslt[0];
            int rsltFilesAdded = (int)rslt[1];
            hashCreateStatus.Content = rsltMessage;

            progBar.Value = (100);
            butRecomputeMD5.Content = "Recompute MD5";
        }

    }
}
