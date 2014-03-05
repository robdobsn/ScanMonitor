using NLog;
using System;
using System.Collections.Generic;
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
    /// Interaction logic for DocFilingView.xaml
    /// </summary>
    public partial class DocFilingView : Window
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private ScanDocHandler _scanDocHandler;
        private DocTypesMatcher _docTypesMatcher;
        private int _curDocDisplay_pageNum;
        private List<string> _docsToBeFiledUniqNames = new List<string>();
        private int _curDocToBeFiledIdxInList = 0;
        private ScanDocAllInfo _curDocScanDocAllInfo;

        public DocFilingView(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher)
        {
            InitializeComponent();
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;
            GetListOfDocsToFile();
            ShowDocToBeFiled(0);
        }

        private void GetListOfDocsToFile()
        {
            _docsToBeFiledUniqNames = _scanDocHandler.GetListOfUnfiledDocUniqNames();
        }

        private void CheckForNewDocs()
        {
            if (_docsToBeFiledUniqNames.Count != _scanDocHandler.GetCountOfUnfiledDocs())
            {
                GetListOfDocsToFile();
            }
        }

        private void ShowDocToBeFiled(int docIdx)
        {
            if (_curDocToBeFiledIdxInList >= _docsToBeFiledUniqNames.Count)
                _curDocToBeFiledIdxInList = _docsToBeFiledUniqNames.Count - 1;
            if (_curDocToBeFiledIdxInList < 0)
                _curDocToBeFiledIdxInList = 0;
            if (_docsToBeFiledUniqNames.Count == 0)
            {
                ShowDocument("");
                return;
            }
            if ((docIdx < 0) || (docIdx >= _docsToBeFiledUniqNames.Count))
                return;
            ShowDocument(_docsToBeFiledUniqNames[docIdx]);
            _curDocToBeFiledIdxInList = docIdx;
        }

        private void ShowDocument(string uniqName)
        {
            // Load document info from db
            _curDocScanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
            if ((_curDocScanDocAllInfo == null) || (_curDocScanDocAllInfo.scanDocInfo == null))
                _curDocScanDocAllInfo = null;

            // Re-check the document
            if (_curDocScanDocAllInfo != null)
            {
                DocTypeMatchResult docTypeMatchResult = _docTypesMatcher.GetMatchingDocType(_curDocScanDocAllInfo.scanPages);
                _curDocScanDocAllInfo.scanDocInfo.docTypeMatchResult = docTypeMatchResult;
            }

            // Display image of first page
            DisplayScannedDocImage(_curDocScanDocAllInfo, 1);

            // Show doc type
            txtDocTypeName.Text = (_curDocScanDocAllInfo == null) ? "" : _curDocScanDocAllInfo.scanDocInfo.docTypeMatchResult.docTypeName;

        }

        private void DisplayScannedDocImage(ScanDocAllInfo scanDocAllInfo, int pageNum)
        {
            bool bNoImage = (scanDocAllInfo == null);
            string imgFileName = "";
            if (!bNoImage)
            {
                imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, scanDocAllInfo.scanDocInfo.uniqName, pageNum, false);
                if (!File.Exists(imgFileName))
                    bNoImage = true;
            }
            if (bNoImage)
            {
                imageDocToFile.Source = null;
                _curDocDisplay_pageNum = 1;
                btnBackPage.IsEnabled = false;
                btnNextPage.IsEnabled = false;
                return;
            }

            // Display image
            try
            {
                imageDocToFile.Source = new BitmapImage(new Uri("File:" + imgFileName));
                _curDocDisplay_pageNum = pageNum;
            }
            catch (Exception excp)
            {
                logger.Error("Loading bitmap file {0} excp {1}", imgFileName, excp.Message);
                _curDocDisplay_pageNum = 1;
            }

            // Show back/next buttons
            btnBackPage.IsEnabled = (pageNum > 1);
            btnNextPage.IsEnabled = (pageNum < scanDocAllInfo.scanPages.scanPagesText.Count);
        }

        private void btnFirstDoc_Click(object sender, RoutedEventArgs e)
        {
            CheckForNewDocs();
            ShowDocToBeFiled(0);
        }

        private void btnPrevDoc_Click(object sender, RoutedEventArgs e)
        {
            CheckForNewDocs();
            ShowDocToBeFiled(_curDocToBeFiledIdxInList - 1);
        }

        private void btnNextDoc_Click(object sender, RoutedEventArgs e)
        {
            CheckForNewDocs();
            ShowDocToBeFiled(_curDocToBeFiledIdxInList + 1);
        }

        private void btnLastDoc_Click(object sender, RoutedEventArgs e)
        {
            CheckForNewDocs();
            ShowDocToBeFiled(_docsToBeFiledUniqNames.Count - 1);
        }

        private void btnBackPage_Click(object sender, RoutedEventArgs e)
        {
            if ((_curDocToBeFiledIdxInList < 0) || (_curDocToBeFiledIdxInList >= _docsToBeFiledUniqNames.Count))
                return;
            DisplayScannedDocImage(_curDocScanDocAllInfo, _curDocDisplay_pageNum - 1);
        }

        private void btnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if ((_curDocToBeFiledIdxInList < 0) || (_curDocToBeFiledIdxInList >= _docsToBeFiledUniqNames.Count))
                return;
            DisplayScannedDocImage(_curDocScanDocAllInfo, _curDocDisplay_pageNum + 1);
        }

        private void btnViewDocTypes_Click(object sender, RoutedEventArgs e)
        {
            DocTypeView dtv = new DocTypeView(_scanDocHandler, _docTypesMatcher);
            dtv.ShowDocTypeList(_curDocScanDocAllInfo.scanDocInfo.docTypeMatchResult.docTypeName, _curDocScanDocAllInfo);
            dtv.ShowDialog();
            CheckForNewDocs();
            ShowDocToBeFiled(_curDocToBeFiledIdxInList);
        }

    }
}
