using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        private ScanPages _curDocScanPages;
        private ScanDocInfo _curDocScanDocInfo;
        private DocTypeMatchResult _latestMatchResult;
        private DocType _curSelectedDocType = null;

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
                ShowDocumentFirstTime("");
                return;
            }
            if ((docIdx < 0) || (docIdx >= _docsToBeFiledUniqNames.Count))
                return;
            ShowDocumentFirstTime(_docsToBeFiledUniqNames[docIdx]);
            _curDocToBeFiledIdxInList = docIdx;
        }

        private void ShowDocumentFirstTime(string uniqName)
        {
            // Load document info from db
            ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
            if ((scanDocAllInfo == null) || (scanDocAllInfo.scanDocInfo == null))
            {
                _curDocScanPages = null;
                _curDocScanDocInfo = null;
                _latestMatchResult = null;
                _curSelectedDocType = null;
            }
            else
            {
                _curDocScanPages = scanDocAllInfo.scanPages;
                _curDocScanDocInfo = scanDocAllInfo.scanDocInfo;
            }

            // Re-check the document
            if (_curDocScanPages != null)
                _latestMatchResult = _docTypesMatcher.GetMatchingDocType(_curDocScanPages);
            else if (_curDocScanDocInfo != null)
                _latestMatchResult = _curDocScanDocInfo.docTypeMatchResult;
            else
                _latestMatchResult = new DocTypeMatchResult();

            // Display image of first page
            DisplayScannedDocImage(1);

            // Show type and date
            ShowDocumentTypeAndDate(_latestMatchResult.docTypeName);
        }

        private void ShowDocumentTypeAndDate(string docTypeName)
        {
            // Get the current doc type
            _curSelectedDocType = _docTypesMatcher.GetDocType(docTypeName);

            // Extract date info again and update latest match result
            List<ExtractedDate> extractedDates = DocTextAndDateExtractor.ExtractDatesFromDoc(_curDocScanPages, 
                                                    (_curSelectedDocType == null) ? "" : _curSelectedDocType.dateExpression);
            _latestMatchResult.datesFoundInDoc = extractedDates;
            if (extractedDates.Count > 0)
                _latestMatchResult.docDate = extractedDates[0].dateTime;
            else
                _latestMatchResult.docDate = DateTime.MinValue;

            // Show doc type
            txtDocTypeName.Text = (_curSelectedDocType == null) ? "" : _curSelectedDocType.docTypeName;

            // Set doc date
            DateTime dateToUse = DateTime.Now;
            if (_latestMatchResult.docDate != DateTime.MinValue)
                dateToUse = _latestMatchResult.docDate;
            SetDateRollers(dateToUse.Year, dateToUse.Month, dateToUse.Day);
        }

        private void DisplayScannedDocImage(int pageNum)
        {
            bool bNoImage = (_curDocScanDocInfo == null);
            string imgFileName = "";
            if (!bNoImage)
            {
                imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, _curDocScanDocInfo.uniqName, pageNum, false);
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
            btnNextPage.IsEnabled = (pageNum < _curDocScanDocInfo.numPagesWithText);
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
            DisplayScannedDocImage(_curDocDisplay_pageNum - 1);
        }

        private void btnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if ((_curDocToBeFiledIdxInList < 0) || (_curDocToBeFiledIdxInList >= _docsToBeFiledUniqNames.Count))
                return;
            DisplayScannedDocImage(_curDocDisplay_pageNum + 1);
        }

        private void btnViewDocTypes_Click(object sender, RoutedEventArgs e)
        {
            DocTypeView dtv = new DocTypeView(_scanDocHandler, _docTypesMatcher);
            dtv.ShowDocTypeList((_curSelectedDocType == null) ? "" : _curSelectedDocType.docTypeName, _curDocScanDocInfo, _curDocScanPages);
            dtv.ShowDialog();
            CheckForNewDocs();
            ShowDocToBeFiled(_curDocToBeFiledIdxInList);
        }

        private DateTime ExtractDateTime()
        {
            DateTime dt = DateTime.Now;
            try
            {
                string dateStr = lblYearVal.Text + "-" + lblMonthVal.Text + "-" + lblDayVal.Text;
                dt = DateTime.ParseExact(dateStr, "yyyy-MMMM-d", CultureInfo.InvariantCulture);
            }
            catch
            {
            }
            return dt;
        }

        private void SetDateRollers(int year, int mon, int day)
        {
            if (year > DateTime.MaxValue.Year)
                year = DateTime.MaxValue.Year;
            if (year < DateTime.MinValue.Year)
                year = DateTime.MinValue.Year;
            if (mon > 12)
                mon = 12;
            if (mon < 1)
                mon = 1;
            if (day > DateTime.DaysInMonth(year, mon))
                day = DateTime.DaysInMonth(year, mon);
            if (day < 1)
                day = 1;
            DateTime dt = new DateTime(year, mon, day);
            lblDayVal.Text = day.ToString();
            lblMonthVal.Text = dt.ToString("MMMM");
            lblYearVal.Text = dt.Year.ToString();
        }

        private void btnDayUp_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = ExtractDateTime();
            SetDateRollers(dt.Year, dt.Month, dt.Day+1);
        }

        private void btnMonthUp_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = ExtractDateTime();
            SetDateRollers(dt.Year, dt.Month+1, dt.Day);
        }

        private void btnYearUp_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = ExtractDateTime();
            SetDateRollers(dt.Year+1, dt.Month, dt.Day);
        }

        private void btnDayDown_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = ExtractDateTime();
            SetDateRollers(dt.Year, dt.Month, dt.Day - 1);
        }

        private void btnMonthDown_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = ExtractDateTime();
            SetDateRollers(dt.Year, dt.Month - 1, dt.Day);
        }

        private void btnYearDown_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = ExtractDateTime();
            SetDateRollers(dt.Year - 1, dt.Month, dt.Day);
        }

        private void imageDocToFile_MouseMove(object sender, MouseEventArgs e)
        {
            Point curMousePoint = e.GetPosition(imageDocToFile);
            Point docCoords = ConvertImagePointToDocPoint(imageDocToFile, curMousePoint.X, curMousePoint.Y);
            DocRectangle docRect = new DocRectangle(docCoords.X, docCoords.Y, 0, 0);
            bool bToolTipSet = false;
            if ((_curDocScanDocInfo != null) && (_curDocScanPages != null))
                if ((_curDocDisplay_pageNum > 0) && (_curDocDisplay_pageNum <= _curDocScanPages.scanPagesText.Count))
                {
                    if (!imageDocToFileToolTip.IsOpen)
                        imageDocToFileToolTip.IsOpen = true;
                    imageDocToFileToolTip.HorizontalOffset = curMousePoint.X - 50;
                    imageDocToFileToolTip.VerticalOffset = curMousePoint.Y;
                    List<ScanTextElem> scanTextElems = _curDocScanPages.scanPagesText[_curDocDisplay_pageNum - 1];
                    foreach (ScanTextElem el in scanTextElems)
                        if (el.bounds.Intersects(docRect))
                        {
                            imageDocToFileToolText.Text = el.text;
                            bToolTipSet = true;
                            break;
                        }
                }
            if (!bToolTipSet)
            {
                imageDocToFileToolText.Text = "";
                imageDocToFileToolTip.IsOpen = false;
            }
            e.Handled = true;
        }

        public static Point ConvertImagePointToDocPoint(Image img, double x, double y)
        {
            double tlx = 100 * x / img.ActualWidth;
            double tly = 100 * y / img.ActualHeight;
            return new Point(tlx, tly);
        }

        private void imageDocToFile_MouseLeave(object sender, MouseEventArgs e)
        {
            Point curMousePoint = e.GetPosition(imageDocToFile);
            if (curMousePoint.X < 0 || curMousePoint.X > imageDocToFile.ActualWidth)
                imageDocToFileToolTip.IsOpen = false;
            else if (curMousePoint.Y < 0 || curMousePoint.Y > imageDocToFile.ActualHeight)
                imageDocToFileToolTip.IsOpen = false;
        }

        private void imageDocToFile_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_curDocScanPages == null)
                return;

            Point curMousePoint = e.GetPosition(imageDocToFile);
            Point docCoords = ConvertImagePointToDocPoint(imageDocToFile, curMousePoint.X, curMousePoint.Y);
            DocRectangle docRect = new DocRectangle(docCoords.X, docCoords.Y, 0, 0);
            List<ExtractedDate> extractedDates = new List<ExtractedDate>();
            int pgNum = _curDocDisplay_pageNum;
            if (_curDocDisplay_pageNum < 1)
                pgNum = 1;
            if (_curDocDisplay_pageNum >= _curDocScanPages.scanPagesText.Count)
                pgNum = _curDocScanPages.scanPagesText.Count;
            DocTextAndDateExtractor.SearchForDateItem(_curDocScanPages, "", docRect, extractedDates, pgNum);
            if (extractedDates.Count > 0)
            {
                SetDateRollers(extractedDates[0].dateTime.Year, extractedDates[0].dateTime.Month, extractedDates[0].dateTime.Day);
            }
        
        }

        private void btnPickDocType_Click(object sender, RoutedEventArgs e)
        {
            DocTypePicker _docTypePicker;
            _docTypePicker = new DocTypePicker(_docTypesMatcher);
            _docTypePicker.ShowDialog();
            if (_docTypePicker.ResultDocType != "")
            {
                // Show type and date
                ShowDocumentTypeAndDate(_docTypePicker.ResultDocType);
            }
        }

    }
}
