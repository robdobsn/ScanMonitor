using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ScanMonitorApp
{
    /// <summary>
    /// Interaction logic for AuditView.xaml
    /// </summary>
    public partial class AuditView : Window
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        ObservableCollection<AuditData> _auditDataColl = new ObservableCollection<AuditData>();
        private ScanDocHandler _scanDocHandler;
        private DocTypesMatcher _docTypesMatcher;
        BackgroundWorker _bwThreadForPopulateList;
        const int MAX_NUM_DOCS_TO_ADD_TO_LIST = 500;

        public AuditView(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher)
        {
            InitializeComponent();
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;

            // List view for comparisons
            auditListView.ItemsSource = _auditDataColl;

            // Populate list thread
            _bwThreadForPopulateList = new BackgroundWorker();
            _bwThreadForPopulateList.WorkerSupportsCancellation = true;
            _bwThreadForPopulateList.WorkerReportsProgress = true;
            _bwThreadForPopulateList.DoWork += new DoWorkEventHandler(PopulateList_DoWork);
            _bwThreadForPopulateList.ProgressChanged += new ProgressChangedEventHandler(PopulateList_ProgressChanged);
            _bwThreadForPopulateList.RunWorkerCompleted += new RunWorkerCompletedEventHandler(PopulateList_RunWorkerCompleted);
        }

        public ObservableCollection<AuditData> AuditDataColl
        {
            get { return _auditDataColl; }
        }

        private void PopulateList_DoWork(object sender, DoWorkEventArgs e)
        {
            e.Result = "";
            BackgroundWorker worker = sender as BackgroundWorker;
            List<ScanDocInfo> sdiList = _scanDocHandler.GetListOfScanDocs();
            this.Dispatcher.BeginInvoke((Action)delegate()
            {
                lblTotalsInfo.Content = sdiList.Count().ToString() + " total docs";
            });
            
            object[] args = (object[])e.Argument;
            string rsltFilter = (string)args[0];
            int includeInListFromIdxNonInclusive = (int)args[1];

            // Start filling list from the end
            int nEndIdx = includeInListFromIdxNonInclusive-1;
            if (nEndIdx >= sdiList.Count - 1)
                nEndIdx = sdiList.Count - 1;
            int numFound = 0;
            int nDocIdx = nEndIdx;
            while (nDocIdx >= 0)
            {
                // Check for cancel
                if ((worker.CancellationPending == true))
                {
                    e.Cancel = true;
                    break;
                }

                // Update status
                worker.ReportProgress((int)(numFound * 100 / MAX_NUM_DOCS_TO_ADD_TO_LIST));

                // Get scan doc info and filed doc info
                ScanDocInfo sdi = sdiList[nDocIdx];
                FiledDocInfo fdi = _scanDocHandler.GetFiledDocInfo(sdi.uniqName);

                // Filter out records that aren't interesting
                bool bInclude = true;
                switch (rsltFilter)
                {
                    case "filed":
                    case "filedNotFound":
                        if (fdi == null)
                        {
                            bInclude = false;
                        }
                        else
                        {
                            if (fdi.filedAt_finalStatus != FiledDocInfo.DocFinalStatus.STATUS_FILED)
                                bInclude = false;
                        }
                        break;
                    default:
                        break;
                }
                if (!bInclude)
                {
                    nDocIdx--;
                    continue;
                }

                // Show this record
                AuditData audDat = new AuditData();
                audDat.UniqName = sdi.uniqName;
                string statStr = "Unfiled";
                bool filedFileNotFound = false;
                if (fdi != null)
                {
                    statStr = FiledDocInfo.GetFinalStatusStr(fdi.filedAt_finalStatus);
                    statStr += " on " + fdi.filedAt_dateAndTime.ToShortDateString();
                    // Check if the file is where it was placed
                    if (fdi.filedAt_finalStatus == FiledDocInfo.DocFinalStatus.STATUS_FILED)
                    {
                        filedFileNotFound = true;
                        try
                        {
                            if (File.Exists(fdi.filedAs_pathAndFileName))
                                filedFileNotFound = false;
                        }
                        catch
                        {

                        }
                    }
                }

                // Check special result filters
                if ((fdi != null) && !filedFileNotFound && (rsltFilter == "filedNotFound"))
                {
                    nDocIdx--;
                    continue;
                }
               
                audDat.FinalStatus = statStr;
                audDat.FiledDocPresent = (fdi == null) ? "" : (filedFileNotFound ? "Not found" : "Ok");

                // See if we can find a file which matches this one in the existing files database
                string movedToFileName = "";
                string archiveFileName = System.IO.Path.Combine(Properties.Settings.Default.DocArchiveFolder, sdi.uniqName + ".pdf");
                if ((filedFileNotFound) && (File.Exists(archiveFileName)))
                {
                    using (var md5 = MD5.Create())
                    {
                        using (var stream = File.OpenRead(archiveFileName))
                        {
                            List<ExistingFileInfoRec> efirList = _scanDocHandler.FindExistingFileRecsByHash(md5.ComputeHash(stream));
                            foreach (ExistingFileInfoRec efir in efirList)
                                if (stream.Length == efir.fileLength)
                                {
                                    movedToFileName = efir.filename;
                                    break;
                                }
                        }
                    }
                }
                audDat.MovedToFileName = movedToFileName;

                this.Dispatcher.BeginInvoke((Action)delegate()
                    {
                        _auditDataColl.Add(audDat);
                    });

                numFound++;
                if (numFound >= MAX_NUM_DOCS_TO_ADD_TO_LIST)
                    break;
                nDocIdx--;
            }
            e.Result = "Ok";
            this.Dispatcher.BeginInvoke((Action)delegate()
            {
                txtStart.Text = (nDocIdx).ToString();
            });
        }

        private void PopulateList_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progBar.Value = (e.ProgressPercentage);
        }

        void PopulateList_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            progBar.Value = (100);
        }

        private void auditListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                AuditData selectedRow = e.AddedItems[0] as AuditData;
                if (selectedRow != null)
                {
                    string uniqName = selectedRow.UniqName;
                    string imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, uniqName, 1, false);
                    try
                    {
                        auditFileImage.Source = new BitmapImage(new Uri(imgFileName));
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Loading bitmap file {0} excp {1}", imgFileName, excp.Message);
                    }


                    // Doc info
                    ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(selectedRow.UniqName);
                    
                    btnOpenFiled.IsEnabled = (scanDocAllInfo.filedDocInfo != null) && (scanDocAllInfo.filedDocInfo.filedAt_finalStatus == FiledDocInfo.DocFinalStatus.STATUS_FILED);

                    txtScanDocInfo.Text = ScanDocHandler.GetScanDocInfoText(scanDocAllInfo.scanDocInfo);
                    txtScanDocInfo.IsReadOnly = true;

                    txtFiledDocInfo.Text = ScanDocHandler.GetFiledDocInfoText(scanDocAllInfo.filedDocInfo);
                    txtFiledDocInfo.IsReadOnly = true;

                    string pagesStr = "";
                    if (scanDocAllInfo.scanPages != null)
                    {
                        foreach (List<ScanTextElem> stElemList in scanDocAllInfo.scanPages.scanPagesText)
                        {
                            foreach (ScanTextElem el in stElemList)
                            {
                                pagesStr += el.text + " ";
                            }
                        }
                    }
                    txtPageText.Text = pagesStr;
                    txtPageText.IsReadOnly = true;

                    //// Get doc type info
                    //DocType dtype = _docTypesMatcher.GetDocType(selectedRow.DocType);
                    //if (dtype != null)
                    //{
                    //    txtOrigDocTypeName.Text = dtype.docTypeName;
                    //    txtOrigExpression.Text = dtype.matchExpression;
                    //}

                    //ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(selectedRow.UniqName);
                    //txtNewDocTypeName.Text = scanDocAllInfo.scanDocInfo.docTypeMatchResult.docTypeName + "(" + scanDocAllInfo.scanDocInfo.docTypeMatchResult.matchCertaintyPercent.ToString() + "%)";
                    //DocType dtype2 = _docTypesMatcher.GetDocType(scanDocAllInfo.scanDocInfo.docTypeMatchResult.docTypeName);
                    //if (dtype2 != null)
                    //{
                    //    txtNewExpression.Text = dtype2.matchExpression;
                    //}

                    // Get file name
                    //string fileName = selectedRow.ArchiveFile;
                    //if (File.Exists(fileName))
                    //{
                    //    string imgName = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(selectedRow.ArchiveFile), System.IO.Path.GetFileNameWithoutExtension(selectedRow.ArchiveFile) + ".jpg");
                    //    if (File.Exists(imgName))
                    //        auditFileImage.Source = new BitmapImage(new Uri(imgName));

                        //System.Drawing.Image img = PdfRasterizer.GetImageOfPage(fileName, 1);
                        //MemoryStream ms = new MemoryStream();
                        //img.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        //System.Windows.Media.Imaging.BitmapImage bImg = new System.Windows.Media.Imaging.BitmapImage();
                        //bImg.BeginInit();
                        //bImg.StreamSource = new MemoryStream(ms.ToArray());
                        //bImg.EndInit();
                        //auditFileImage.Source = bImg;
                    // }
                }
            }
        }

        private string GetListViewType()
        {
            return ((ComboBoxItem)(comboListView.SelectedItem)).Tag.ToString();
        }

        private void btnGo_Click(object sender, RoutedEventArgs e)
        {
            // Clear list
            _auditDataColl.Clear();

            int startVal = 1000000;
            if (!_bwThreadForPopulateList.IsBusy)
            {
                object[] args = { GetListViewType(), startVal };
                _bwThreadForPopulateList.RunWorkerAsync(args);
            }
        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            // Clear list
            _auditDataColl.Clear();

            int startVal = 1000000;
            Int32.TryParse(txtStart.Text, out startVal);
            if (!_bwThreadForPopulateList.IsBusy)
            {
                object[] args = { GetListViewType(), startVal };
                _bwThreadForPopulateList.RunWorkerAsync(args);
            }
        }

        private void btnOpenOrig_Click(object sender, RoutedEventArgs e)
        {
            AuditData selectedRow = auditListView.SelectedItem as AuditData;
            if (selectedRow != null)
            {
                string uniqName = selectedRow.UniqName;
                ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
                ScanDocHandler.ShowFileInExplorer(scanDocAllInfo.scanDocInfo.origFileName.Replace("/", @"\"));
            }
        }

        private void btnOpenArchive_Click(object sender, RoutedEventArgs e)
        {
            AuditData selectedRow = auditListView.SelectedItem as AuditData;
            if (selectedRow != null)
            {
                string uniqName = selectedRow.UniqName;
                string imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, uniqName, 1, false);
                try
                {
                    ScanDocHandler.ShowFileInExplorer(imgFileName.Replace("/", @"\"));
                }
                finally
                {

                }
            }
        }

        private void btnOpenFiled_Click(object sender, RoutedEventArgs e)
        {
            AuditData selectedRow = auditListView.SelectedItem as AuditData;
            if (selectedRow != null)
            {
                string uniqName = selectedRow.UniqName;
                ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
                if (scanDocAllInfo.filedDocInfo != null)
                    ScanDocHandler.ShowFileInExplorer(scanDocAllInfo.filedDocInfo.filedAs_pathAndFileName.Replace("/", @"\"));
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.WindowState = System.Windows.WindowState.Maximized;
            if (!_bwThreadForPopulateList.IsBusy)
            {
                object[] args = { GetListViewType(), 100000 };
                _bwThreadForPopulateList.RunWorkerAsync(args);
            }

        }

        //private void auditListView_Loaded(object sender, RoutedEventArgs e)
        //{
        //    List<ScanDocInfo> sdiList = _scanDocHandler.GetListOfScanDocs();
        //    if (sdiList.Count() > 0)
        //    {
        //        Binding bind = new Binding();
        //        auditListView.DataContext = sdiList;
        //        auditListView.SetBinding(ListView.ItemsSourceProperty, bind);
        //    }
        //}

    }

    public class AuditData
    {
        public string UniqName {get; set;}
        public string FinalStatus { get; set; }
        public string FiledDocPresent { get; set; }
        public string MovedToFileName { get; set; }
    }

}
