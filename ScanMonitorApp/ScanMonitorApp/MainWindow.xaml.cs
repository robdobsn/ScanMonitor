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

namespace ScanMonitorApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private const bool TEST_MODE = true;
        BackgroundWorker _bwThread;

        private List<string> foldersToMonitor = new List<string> { Properties.Settings.Default.FolderToMonitor };

        private ScanDocHandlerConfig _scanDocHandlerConfig = new ScanDocHandlerConfig(Properties.Settings.Default.DocAdminImgFolderBase,
            Properties.Settings.Default.MaxPagesForImages,
            Properties.Settings.Default.MaxPagesForText,
            Properties.Settings.Default.DbNameForDocs,
            Properties.Settings.Default.DbCollectionForDocInfo,
            Properties.Settings.Default.DbCollectionForDocPages,
            Properties.Settings.Default.DbCollectionForFiledDocs
            );

        private static Logger logger = LogManager.GetCurrentClassLogger();
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private DocTypesMatcher _docTypesMatcher;
        private ScanFileMonitor _scanFileMonitor = null;
        private ScanDocHandler _scanDocHandler;

        private void InitNotifyIcon()
        {
            // Notify icon
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Icon = ScanMonitorApp.Properties.Resources.scanner48x48;
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

        public void ExitApp(object sender, EventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
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
            dlg.InitialDirectory = @"\\MACALLAN\Main\RobAndJudyPersonal\IT\Scanning\";
            dlg.FileName = @"\\MACALLAN\Main\RobAndJudyPersonal\IT\Scanning\rules.xml";
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
            // Configure open file dialog box
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.InitialDirectory = @"\\N7700PRO\Archive\ScanAdmin\ScanLogs\";
            dlg.FileName = @"\\N7700PRO\Archive\ScanAdmin\ScanLogs\ScanLog.log";
            dlg.DefaultExt = ".log"; // Default file extension
            dlg.Filter = "Log documents (.log)|*.log"; // Filter files by extension 

            // Show open file dialog box
            Nullable<bool> result = dlg.ShowDialog();

            // Process open file dialog box results 
            if (result == true)
            {
                // Open document 
                string filename = dlg.FileName;
                AuditView av = new AuditView(_scanDocHandler, _docTypesMatcher);
                av.ShowDialog();
            }
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
            dlg.InitialDirectory = @"\\N7700PRO\Archive\ScanAdmin\ScanLogs";
            dlg.FileName = @"ScanLog.log";
            dlg.DefaultExt = ".log"; // Default file extension
            dlg.Filter = "Log documents (.log)|*.log"; // Filter files by extension 

            // Show open file dialog box
            Nullable<bool> result = dlg.ShowDialog();

            // Process open file dialog box results 
            if (result == true)
            {
                // Open document 
                string filename = dlg.FileName;

                // Matcher thread
                _bwThread = new BackgroundWorker();
                _bwThread.WorkerSupportsCancellation = false;
                _bwThread.WorkerReportsProgress = false;
                _bwThread.DoWork += new DoWorkEventHandler(LoadAuditFileToDb_DoWork);
                _bwThread.RunWorkerCompleted += new RunWorkerCompletedEventHandler(LoadAuditFileToDb_RunWorkerCompleted);
                _bwThread.RunWorkerAsync(filename);
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

    }
}
