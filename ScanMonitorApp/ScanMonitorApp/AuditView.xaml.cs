using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            //_bwThreadForPopulateList.ProgressChanged += new ProgressChangedEventHandler(PopulateList_ProgressChanged);
            //_bwThreadForPopulateList.RunWorkerCompleted += new RunWorkerCompletedEventHandler(PopulateList_RunWorkerCompleted);

            if (!_bwThreadForPopulateList.IsBusy)
            {
                object[] args = { "All", 100000 };
                _bwThreadForPopulateList.RunWorkerAsync(args);
            }
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
            int includeInListFromIdx = (int)args[1];

            // Start filling list
            int nStartIdx = includeInListFromIdx;
            if (nStartIdx > sdiList.Count - 1)
                nStartIdx = sdiList.Count - MAX_NUM_DOCS_TO_ADD_TO_LIST;
            int nEndIdx = includeInListFromIdx + MAX_NUM_DOCS_TO_ADD_TO_LIST - 1;
            if (nEndIdx > sdiList.Count - 1)
                nEndIdx = sdiList.Count - 1;
            for (int nDocIdx = nStartIdx; nDocIdx <= nEndIdx; nDocIdx++)
            {
                // Check for cancel
                if ((worker.CancellationPending == true))
                {
                    e.Cancel = true;
                    break;
                }

                // Get scan doc info and filed doc info
                ScanDocInfo sdi = sdiList[nDocIdx];
                FiledDocInfo fdi = _scanDocHandler.GetFiledDocInfo(sdi.uniqName);
                AuditData audDat = new AuditData();
                audDat.UniqName = sdi.uniqName;
                string statStr = "Unfiled";
                if (fdi != null)
                {
                    statStr = FiledDocInfo.GetFinalStatusStr(fdi.filedAt_finalStatus);
                    statStr += " on " + fdi.filedAt_dateAndTime.ToShortDateString();
                }
               
                audDat.FinalStatus = statStr;

                this.Dispatcher.BeginInvoke((Action)delegate()
                    {
                        _auditDataColl.Add(audDat);
                    });

                // Update status
                worker.ReportProgress((int)(nDocIdx * 100 / sdiList.Count));
                if (((nDocIdx % 10) == 0) || (nDocIdx == nEndIdx))
                {
                    string rsltStr = (nDocIdx-nStartIdx).ToString();
                    this.Dispatcher.BeginInvoke((Action)delegate()
                    {
                        progBar.Value = ((100 * (nDocIdx - nStartIdx)) / (nEndIdx - nStartIdx));
                    });
                }
            }
            e.Result = "Ok";
            this.Dispatcher.BeginInvoke((Action)delegate()
            {
                txtStart.Text = (nStartIdx + 1).ToString();
                txtEnd.Text = (nEndIdx + 1).ToString();
            });
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

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            // Clear list
            _auditDataColl.Clear();

            int startVal = 0;
            Int32.TryParse(txtStart.Text, out startVal);
            int endVal = 0;
            Int32.TryParse(txtEnd.Text, out endVal);
            if (!_bwThreadForPopulateList.IsBusy)
            {
                object[] args = { "All", endVal };
                _bwThreadForPopulateList.RunWorkerAsync(args);
            }
        }

        private void btnGo_Click(object sender, RoutedEventArgs e)
        {
            // Clear list
            _auditDataColl.Clear();

            int startVal = 0;
            Int32.TryParse(txtStart.Text, out startVal);
            int endVal = 0;
            Int32.TryParse(txtEnd.Text, out endVal);
            if (!_bwThreadForPopulateList.IsBusy)
            {
                object[] args = { "All", startVal };
                _bwThreadForPopulateList.RunWorkerAsync(args);
            }
        }

        private void btnPrev_Click(object sender, RoutedEventArgs e)
        {
            // Clear list
            _auditDataColl.Clear();

            int startVal = 0;
            Int32.TryParse(txtStart.Text, out startVal);
            int endVal = 0;
            Int32.TryParse(txtEnd.Text, out endVal);
            if (!_bwThreadForPopulateList.IsBusy)
            {
                object[] args = { "All", startVal-MAX_NUM_DOCS_TO_ADD_TO_LIST };
                _bwThreadForPopulateList.RunWorkerAsync(args);
            }
        }

        private void ShowFileInExplorer(string fileName)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "explorer";
            psi.UseShellExecute = true;
            psi.WindowStyle = ProcessWindowStyle.Normal;
            psi.Arguments = string.Format("/e,/select,\"{0}\"", fileName);
            Process.Start(psi);
        }

        private void btnOpenOrig_Click(object sender, RoutedEventArgs e)
        {
            AuditData selectedRow = auditListView.SelectedItem as AuditData;
            if (selectedRow != null)
            {
                string uniqName = selectedRow.UniqName;
                ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
                ShowFileInExplorer(scanDocAllInfo.scanDocInfo.origFileName.Replace("/", @"\"));
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
                    ShowFileInExplorer(imgFileName.Replace("/", @"\"));
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
                    ShowFileInExplorer(scanDocAllInfo.filedDocInfo.filedAs_pathAndFileName.Replace("/", @"\"));
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
    }

}
