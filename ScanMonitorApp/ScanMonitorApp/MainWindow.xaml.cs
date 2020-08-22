#define USE_NOTIFY_ICON

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
    public delegate void WindowClosingDelegate();

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private const bool TEST_MODE = false;
        private string _dbConnectionStr = Properties.Settings.Default.DbConnectionString;
        private string _foldersToMonitor = Properties.Settings.Default.FoldersToMonitor;

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
        private bool _thisPCIsScanningPC = false;

        private System.Windows.Threading.DispatcherTimer _uiUpdateTimer = new System.Windows.Threading.DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();
            logger.Info("App Started");

            // Document matcher
            _docTypesMatcher = new DocTypesMatcher();
            if (!_docTypesMatcher.Setup(_dbConnectionStr))
            {
                MessageBoxButton btnMessageBox = MessageBoxButton.OK;
                MessageBoxImage icnMessageBox = MessageBoxImage.Error;
                MessageBoxResult rsltMessageBox = System.Windows.MessageBox.Show("Database may not be started - cannot continue", "Database problem", btnMessageBox, icnMessageBox);
                System.Windows.Application.Current.Shutdown();
                return;
            }

            // Scanned document handler
            _scanDocHandler = new ScanDocHandler(AddToStatusText, _docTypesMatcher, 
                        _scanDocHandlerConfig, _dbConnectionStr);

            // Scan folder watcher
            statusRunningMonitor.Content = "This PC is " + System.Environment.MachineName;
            if (Properties.Settings.Default.PCtoRunMonitorOn.Trim() == System.Environment.MachineName.Trim())
            {
                _thisPCIsScanningPC = true;
                _scanFileMonitor = new ScanFileMonitor(AddToStatusText, _scanDocHandler);
                string[] foldersToMonitorArray = _foldersToMonitor.Split(';');
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
                _scanFileMonitor.Start(foldersToMonitor, Properties.Settings.Default.LocalFolderToMoveFiledTo, TEST_MODE);
                statusRunningMonitor.Content += " and is Running Folder Monitor";
                InitNotifyIcon();
            }
            else
            {
                statusRunningMonitor.Content += " and is not monitoring for new scans";
            }
        }

        private void butViewAuditData_Click(object sender, RoutedEventArgs e)
        {
            disableButtons();
            AuditView av = new AuditView(_scanDocHandler, _docTypesMatcher, curViewClosedCB);
            av.Show();
        }

        private void butViewScanFiling_Click(object sender, RoutedEventArgs e)
        {
            disableButtons();
            DocFilingView dfv = new DocFilingView(_scanDocHandler, _docTypesMatcher, 
                        curViewClosedCB, _thisPCIsScanningPC);
            dfv.Show();
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            disableButtons();
            SettingsView sv = new SettingsView(curViewClosedCB);
            sv.Show();
        }

        private void butMaintenance_Click(object sender, RoutedEventArgs e)
        {
            disableButtons();
            MaintenanceView maintenanceView = new MaintenanceView(_scanDocHandler, _docTypesMatcher, curViewClosedCB);
            maintenanceView.ShowDialog();
        }

        private void disableButtons()
        {
            btnAuditTrail.IsEnabled = false;
            btnFilingView.IsEnabled = false;
            btnSettings.IsEnabled = false;
            btnMaintenance.IsEnabled = false;
        }

        private void curViewClosedCB()
        {
            btnAuditTrail.IsEnabled = true;
            btnFilingView.IsEnabled = true;
            btnSettings.IsEnabled = true;
            btnMaintenance.IsEnabled = true;
        }

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Position window
            PresentationSource MainWindowPresentationSource = PresentationSource.FromVisual(this);
            Matrix m = MainWindowPresentationSource.CompositionTarget.TransformToDevice;
            double DpiWidthFactor = m.M11;
            double DpiHeightFactor = m.M22;
            double ScreenHeight = SystemParameters.PrimaryScreenHeight * DpiHeightFactor;
            double ScreenWidth = SystemParameters.PrimaryScreenWidth * DpiWidthFactor;

            logger.Info("W " + Screen.PrimaryScreen.WorkingArea.Width.ToString() + " . " + Width.ToString() + " . " + ScreenWidth.ToString() +
                " H " + Screen.PrimaryScreen.WorkingArea.Height.ToString() + " . " + Height.ToString() + " . " + ScreenHeight.ToString());
            if ((ScreenWidth - Width > 0) && (ScreenHeight - Height > 0))
            {
                Left = ScreenWidth - Width;
                Top = ScreenHeight - Height - 100;
            }

            // UI update timer
            _uiUpdateTimer.Tick += new EventHandler(uiUpdateTimer_Tick);
            _uiUpdateTimer.Interval = new TimeSpan(0, 0, 1);
            _uiUpdateTimer.Start();

            //// Show filing view
            //DocFilingView dfv = new DocFilingView(_scanDocHandler, _docTypesMatcher);
            //dfv.ShowDialog();
        }

        private void uiUpdateTimer_Tick(object sender, EventArgs e)
        {
            // Updating the Label which displays the current second
            if (_scanFileMonitor != null)
            {
                statusFilingMonitor.Content = _scanFileMonitor.GetCurrentInfo();
                string oldStatus = statusEvents.Text;
                string newStatus = _scanFileMonitor.GetLastEvents();
                if (oldStatus != newStatus)
                {
                    statusEvents.Text = newStatus;
                    statusEvents.Focus();
                    statusEvents.CaretIndex = statusEvents.Text.Length;
                    statusEvents.ScrollToEnd();
                }
            }
        }

        public void ExitApp(object sender, EventArgs e)
        {
            logger.Info("App Exit");
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

#if USE_NOTIFY_ICON
        private System.Windows.Forms.NotifyIcon _notifyIcon;

        private void InitNotifyIcon()
        {
            // Notify icon
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Icon = ScanMonitorApp.Properties.Resources.ScanMonitorIcon;
            _notifyIcon.Visible = true;
            _notifyIcon.MouseUp +=
                new System.Windows.Forms.MouseEventHandler(delegate (object sender, System.Windows.Forms.MouseEventArgs args)
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
            if (_notifyIcon != null)
                _notifyIcon.Visible = false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_thisPCIsScanningPC)
            {
                e.Cancel = true;
                WindowState = WindowState.Minimized;
            }
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
    }

}
