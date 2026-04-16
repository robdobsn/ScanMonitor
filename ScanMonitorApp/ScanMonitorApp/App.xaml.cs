using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace ScanMonitorApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ApplyServerName();
        }

        private void ApplyServerName()
        {
            const string placeholder = "MACALLAN";
            string server = ScanMonitorApp.Properties.Settings.Default.ServerName;
            if (string.IsNullOrEmpty(server) || server == placeholder)
                return;

            var s = ScanMonitorApp.Properties.Settings.Default;
            s.DbConnectionString = s.DbConnectionString.Replace(placeholder, server);
            s.DocAdminImgFolderBase = s.DocAdminImgFolderBase.Replace(placeholder, server);
            s.BasePathForFilingFolderSelection = s.BasePathForFilingFolderSelection.Replace(placeholder, server);
            s.DocArchiveFolder = s.DocArchiveFolder.Replace(placeholder, server);
            s.PdfEditorOutFolder = s.PdfEditorOutFolder.Replace(placeholder, server);
            s.FoldersToSearchForFiledDocs = s.FoldersToSearchForFiledDocs.Replace(placeholder, server);

            // Set NLog variable so the log file target uses the correct server
            if (NLog.LogManager.Configuration != null)
                NLog.LogManager.Configuration.Variables["server"] = server;
        }
    }
}
