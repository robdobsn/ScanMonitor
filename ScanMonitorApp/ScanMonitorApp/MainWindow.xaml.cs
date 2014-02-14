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

namespace ScanMonitorApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private const string pendingDocFolder = @"C:\Users\Rob\Documents\20140209 Train\Scanning\Pending\";
        private List<string> foldersToMonitor = new List<string> { @"C:\Users\Rob\Documents\20140209 Train\Scanning\TestFiles2" };
        private const string pendingTmpFolder = @"C:\Users\Rob\Documents\20140209 Train\Scanning\PendingImgs";
        private const int maxPagesForImages = 10;
        private const int _maxPagesForText = 10;
        private string _dbNameForDocs = "ScanManager";
        private string _dbCollectionForDocs = "ScanDocInfo";

        private static Logger logger = LogManager.GetCurrentClassLogger();
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private ScanFileMonitor _scanFileMonitor;

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

            // Scan folder watcher
            _scanFileMonitor = new ScanFileMonitor(AddToStatusText);
            _scanFileMonitor.Start(foldersToMonitor, pendingDocFolder, pendingTmpFolder, 
                maxPagesForImages, _maxPagesForText, _dbNameForDocs, _dbCollectionForDocs);
            
        }
    }
}
