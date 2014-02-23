﻿using System;
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
        private const bool TEST_ON_MACAIR = true;

        private List<string> foldersToMonitor = new List<string> { Properties.Settings.Default.FolderToMonitor };
        private string pendingDocFolder = TEST_ON_MACAIR ? Properties.Settings.Default.TestPendingDocFolder : Properties.Settings.Default.PendingDocFolder;
        private string pendingTmpFolder = TEST_ON_MACAIR ? Properties.Settings.Default.TestPendingTmpFolder : Properties.Settings.Default.PendingTmpFolder;
        private int maxPagesForImages = Properties.Settings.Default.MaxPagesForImages;
        private int _maxPagesForText = Properties.Settings.Default.MaxPagesForText;
        private string _dbNameForDocs = Properties.Settings.Default.DbNameForDocs;
        private string _dbCollectionForDocs = Properties.Settings.Default.DbCollectionForDocs;
        private string _dbNameForDocTypes = Properties.Settings.Default.DbNameForDocs;
        private string _dbCollectionForDocTypes = Properties.Settings.Default.DbCollectionForDocTypes;

        private static Logger logger = LogManager.GetCurrentClassLogger();
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private DocTypesMatcher _docTypesMatcher;
        private ScanFileMonitor _scanFileMonitor;
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
            _docTypesMatcher = new DocTypesMatcher(_dbNameForDocs, _dbCollectionForDocTypes);

            // Scanned document handler
            _scanDocHandler = new ScanDocHandler(AddToStatusText, _docTypesMatcher, pendingDocFolder, pendingTmpFolder,
                            maxPagesForImages, _maxPagesForText, _dbNameForDocs, _dbCollectionForDocs);

            // Scan folder watcher
            _scanFileMonitor = new ScanFileMonitor(AddToStatusText, _scanDocHandler);
            _scanFileMonitor.Start(foldersToMonitor, pendingDocFolder, TEST_MODE);

            // Start the web server
            WebServer ws = new WebServer("http://localhost:8080");
            ws.RegisterEndPoint(new WebEndPoint_ScanDocs(_scanDocHandler));
            ws.RegisterEndPoint(new WebEndPoint_DocTypes(_docTypesMatcher));
            ws.Run();
         }

        private void Test1_Click(object sender, RoutedEventArgs e)
        {
            _scanFileMonitor.Test1();
        }

        private void Test2_Click(object sender, RoutedEventArgs e)
        {
            _scanFileMonitor.Test2();
        }

        private void Test3_Click(object sender, RoutedEventArgs e)
        {
            _scanFileMonitor.Test3();
        }

        private void AddOldDocTypes_Click(object sender, RoutedEventArgs e)
        {
            // Configure open file dialog box
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
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
                _docTypesMatcher.AddOldDocTypes(filename);
            }
        }

        private void butViewAuditData_Click(object sender, RoutedEventArgs e)
        {
            // Configure open file dialog box
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
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
                AuditView av = new AuditView();
                av.ReadAuditFile(filename);
                av.ShowDialog();
            }
        }
    }
}
