using MahApps.Metro.Controls;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
    public partial class DocFilingView : MetroWindow
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
        private BackgroundWorker _bwThreadForImagesPopup;
        ObservableCollection<DocTypeCacheEntry> _thumbnailsOfDocTypes = new ObservableCollection<DocTypeCacheEntry>();
        private ObservableCollection<DocTypeMatchResult> _listOfPossibleDocMatches = new ObservableCollection<DocTypeMatchResult>();
        private DateTime _lastDocFiledAsDateTime = DateTime.Now;
        private enum TouchFromPageText { TOUCH_NONE, TOUCH_DATE, TOUCH_SUFFIX }
        private TouchFromPageText _touchFromPageText = TouchFromPageText.TOUCH_NONE;

        public DocFilingView(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher)
        {
            InitializeComponent();
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;
            popupDocTypePickerThumbs.ItemsSource = _thumbnailsOfDocTypes;
            popupDocTypeResultList.ItemsSource = _listOfPossibleDocMatches;
            GetListOfDocsToFile();
            ShowDocToBeFiled(0);

            // Image filler thread
            _bwThreadForImagesPopup = new BackgroundWorker();
            _bwThreadForImagesPopup.WorkerSupportsCancellation = true;
            _bwThreadForImagesPopup.WorkerReportsProgress = true;
            _bwThreadForImagesPopup.DoWork += new DoWorkEventHandler(AddImages_DoWork);

            // Use a background worker to populate
            _bwThreadForImagesPopup.RunWorkerAsync();
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
                _curDocToBeFiledIdxInList = 0;
                ShowDocumentFirstTime("");
                return;
            }
            if ((docIdx < 0) || (docIdx >= _docsToBeFiledUniqNames.Count))
                return;
            _curDocToBeFiledIdxInList = docIdx;
            ShowDocumentFirstTime(_docsToBeFiledUniqNames[docIdx]);
        }

        private void ShowDocumentFirstTime(string uniqName)
        {
            // Load document info from db
            List<DocTypeMatchResult> possMatches = new List<DocTypeMatchResult>();
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
                _latestMatchResult = _docTypesMatcher.GetMatchingDocType(_curDocScanPages, possMatches);
            else if (_curDocScanDocInfo != null)
                _latestMatchResult = _curDocScanDocInfo.docTypeMatchResult;
            else
                _latestMatchResult = new DocTypeMatchResult();

            // Update the doc type list view for popup
            _listOfPossibleDocMatches.Clear();
            foreach(DocTypeMatchResult res in possMatches)
                _listOfPossibleDocMatches.Add(res);
                
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
            int bestDateIdx = 0;
            List<ExtractedDate> extractedDates = DocTextAndDateExtractor.ExtractDatesFromDoc(_curDocScanPages, 
                                                    (_curSelectedDocType == null) ? "" : _curSelectedDocType.dateExpression,
                                                    out bestDateIdx);
            _latestMatchResult.datesFoundInDoc = extractedDates;
            if (extractedDates.Count > 0)
                _latestMatchResult.docDate = extractedDates[bestDateIdx].dateTime;
            else
                _latestMatchResult.docDate = DateTime.MinValue;

            // Show doc type
            txtDocTypeName.Text = (_curSelectedDocType == null) ? "" : _curSelectedDocType.docTypeName;

            // Field enables
            txtDestFilePrefix.IsEnabled = false;
            btnChangePrefix.IsEnabled = true;
            txtDestFilePrefix.Text = (_curSelectedDocType == null) ? "" : _curSelectedDocType.GetFileNamePrefix();
            txtDestFileSuffix.IsEnabled = false;
            txtDestFileSuffix.Text = "";
            txtDestFileSuffix.IsEnabled = true;
            _touchFromPageText = TouchFromPageText.TOUCH_NONE;

            // Set doc date
            DateTime dateToUse = DateTime.Now;
            if (_latestMatchResult.docDate != DateTime.MinValue)
                dateToUse = _latestMatchResult.docDate;
            SetDateRollers(dateToUse.Year, dateToUse.Month, dateToUse.Day);

            // Show Status
            lblStatusBarFileNo.Content = (_curDocToBeFiledIdxInList + 1).ToString() + " / " + _docsToBeFiledUniqNames.Count.ToString();
            lblStatusBarFileName.Content = _curDocScanDocInfo.uniqName;
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
                lblPageNum.Content = "";
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
            lblPageNum.Content = pageNum.ToString();
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

        private void DisplayDestFileName()
        {
            if ((_curDocScanDocInfo != null) && (_curSelectedDocType != null))
                lblDestFileName.Content = _scanDocHandler.FormatFileNameFromMacros(_curDocScanDocInfo.origFileName, _curSelectedDocType.renameFileTo, GetDateFromRollers(), txtDestFilePrefix.Text, txtDestFileSuffix.Text, _curSelectedDocType.docTypeName);
        }

        private DateTime GetDateFromRollers()
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

            // Show dest file name
            DisplayDestFileName();
        }

        private void btnDayUp_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = GetDateFromRollers();
            SetDateRollers(dt.Year, dt.Month, dt.Day+1);
        }

        private void btnMonthUp_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = GetDateFromRollers();
            SetDateRollers(dt.Year, dt.Month+1, dt.Day);
        }

        private void btnYearUp_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = GetDateFromRollers();
            SetDateRollers(dt.Year+1, dt.Month, dt.Day);
        }

        private void btnDayDown_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = GetDateFromRollers();
            SetDateRollers(dt.Year, dt.Month, dt.Day - 1);
        }

        private void btnMonthDown_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = GetDateFromRollers();
            SetDateRollers(dt.Year, dt.Month - 1, dt.Day);
        }

        private void btnYearDown_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = GetDateFromRollers();
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
            e.Handled = true;
        }

        private void imageDocToFile_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_curDocScanPages == null)
                return;

            if (_touchFromPageText == TouchFromPageText.TOUCH_NONE)
                return;

            // Check for touch point
            Point curMousePoint = e.GetPosition(imageDocToFile);
            Point docCoords = ConvertImagePointToDocPoint(imageDocToFile, curMousePoint.X, curMousePoint.Y);
            DocRectangle docRect = new DocRectangle(docCoords.X, docCoords.Y, 0, 0);
            int pgNum = _curDocDisplay_pageNum;
            if (_curDocDisplay_pageNum < 1)
                pgNum = 1;
            if (_curDocDisplay_pageNum >= _curDocScanPages.scanPagesText.Count)
                pgNum = _curDocScanPages.scanPagesText.Count;

            // Check for date
            if (_touchFromPageText == TouchFromPageText.TOUCH_DATE)
            {
                List<ExtractedDate> extractedDates = new List<ExtractedDate>();
                DocTextAndDateExtractor.SearchForDateItem(_curDocScanPages, "", docRect, 0, extractedDates, pgNum, false);
                if (extractedDates.Count <= 0)
                    DocTextAndDateExtractor.SearchForDateItem(_curDocScanPages, "", docRect, 0, extractedDates, pgNum, true);
                if (extractedDates.Count > 0)
                    SetDateRollers(extractedDates[0].dateTime.Year, extractedDates[0].dateTime.Month, extractedDates[0].dateTime.Day);
            }
            else if (_touchFromPageText == TouchFromPageText.TOUCH_SUFFIX)
            {
                string extractedText = DocTextAndDateExtractor.ExtractTextFromPage(_curDocScanPages, docRect, pgNum);
                if (extractedText != "")
                    txtDestFileSuffix.Text = txtDestFileSuffix.Text + (txtDestFileSuffix.Text.Trim() == "" ? "" : " ") + extractedText;
            }
        }

        private void btnPickDocType_Click(object sender, RoutedEventArgs e)
        {
            using (new WaitCursor())
            {
                if (!popupDocTypePicker.IsOpen)
                    popupDocTypePicker.IsOpen = true;
            }
        }

        private void btnClickImageDocType_Click(object sender, RoutedEventArgs e)
        {
            popupDocTypePicker.IsOpen = false;
            object tag = null;
            if (sender.GetType() == typeof(Image))
                tag = ((Image)sender).Tag;
            if (sender.GetType() == typeof(Button))
                tag = ((Button)sender).Tag;
            if (tag.GetType() == typeof(string))
                ShowDocumentTypeAndDate((string)tag);
        }

        private void AddImages_DoWork(object sender, DoWorkEventArgs e)
        {
            int thumbnailHeight = Properties.Settings.Default.PickThumbHeight;
            BackgroundWorker worker = sender as BackgroundWorker;
            List<DocType> docTypeList = _docTypesMatcher.ListDocTypes();
            foreach (DocType dt in docTypeList)
            {
                if ((worker.CancellationPending == true))
                {
                    e.Cancel = true;
                    break;
                }

                if (!dt.isEnabled)
                    continue;

                if (dt.thumbnailForDocType != "")
                {
                    this.Dispatcher.BeginInvoke((Action)delegate()
                    {
                        BitmapImage bitmap = DocTypeHelper.LoadDocThumbnail(dt.thumbnailForDocType, thumbnailHeight);
                        DocTypeCacheEntry ce = new DocTypeCacheEntry();
                        ce.ThumbUniqName = dt.thumbnailForDocType;
                        ce.ThumbBitmap = bitmap;
                        ce.DocTypeName = dt.docTypeName;
                        _thumbnailsOfDocTypes.Add(ce);
                    });
                    Thread.Sleep(50);
                }
            }
        }

        private class WaitCursor : IDisposable
        {
            private Cursor _previousCursor;

            public WaitCursor()
            {
                _previousCursor = Mouse.OverrideCursor;

                Mouse.OverrideCursor = Cursors.Wait;
            }

            #region IDisposable Members

            public void Dispose()
            {
                Mouse.OverrideCursor = _previousCursor;
            }

            #endregion
        }

        private void btnOtherDocTypes_Click(object sender, RoutedEventArgs e)
        {
            if (!popupDocTypeResult.IsOpen)
                popupDocTypeResult.IsOpen = true;
        }

        private void btnSelectDocType_Click(object sender, RoutedEventArgs e)
        {
            popupDocTypeResult.IsOpen = false;
            object tag = null;
            if (sender.GetType() == typeof(Label))
                tag = ((Label)sender).Tag;
            if (tag.GetType() == typeof(string))
                ShowDocumentTypeAndDate((string)tag);
        }

        private void btnDeleteDoc_Click(object sender, RoutedEventArgs e)
        {
            if (_curDocScanDocInfo == null)
                return;

            // Ask user if sure
            MessageDialog msgDialog = new MessageDialog("Delete " + _curDocScanDocInfo.uniqName + " ?\n" + "Are you sure?", true, true, true, btnDeleteDoc, this);
            msgDialog.ShowDialog();
            if (msgDialog.dlgResult == MessageDialog.MsgDlgRslt.RSLT_YES)
            {
                // Delete file
                bool deletedOk = _scanDocHandler.DeleteFile(_curDocScanDocInfo.uniqName, _curDocScanDocInfo.origFileName);
                if (!deletedOk)
                {
                    lblStatusBarProcStatus.Content = "Failed to delete file";
                    lblStatusBarProcStatus.Foreground = Brushes.Red;
                }
                else
                {
                    lblStatusBarProcStatus.Content = "Deleted";
                    lblStatusBarProcStatus.Foreground = Brushes.Black;
                }

                // Goto a file if there is one
                CheckForNewDocs();
                ShowDocToBeFiled(_curDocToBeFiledIdxInList);
            }
        }

        private void btnProcessDoc_Click(object sender, RoutedEventArgs e)
        {
            if (_curDocScanDocInfo == null)
                return;

            // Check a doc type has been selected
            if (_curSelectedDocType == null)
            {
                lblStatusBarProcStatus.Content = "A document type must be selected";
                lblStatusBarProcStatus.Foreground = Brushes.Red;
                return;
            }

            // Check validity
            string rsltText = "";
            bool rslt = _scanDocHandler.CheckOkToFileDoc(_curDocScanDocInfo, _curSelectedDocType, GetDateFromRollers(), out rsltText);
            if (!rslt)
            {
                lblStatusBarProcStatus.Content = rsltText;
                lblStatusBarProcStatus.Foreground = Brushes.Red;
                return;
            }

            // Save time as filed
            _lastDocFiledAsDateTime = GetDateFromRollers();

            // Process the doc
            lblStatusBarProcStatus.Content = "Processing ...";
            lblStatusBarProcStatus.Foreground = Brushes.Black;
            rsltText = "";
            _scanDocHandler.StartProcessFilingOfDoc(out rsltText);
        }

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.WindowState = System.Windows.WindowState.Maximized;
        }

        private void btnUseScanDate_Click(object sender, RoutedEventArgs e)
        {
            if (_curDocScanDocInfo != null)
                SetDateRollers(_curDocScanDocInfo.createDate.Year, _curDocScanDocInfo.createDate.Month, _curDocScanDocInfo.createDate.Day);
        }

        private void btnLastUsedDate_Click(object sender, RoutedEventArgs e)
        {
            if (_lastDocFiledAsDateTime != null)
                SetDateRollers(_lastDocFiledAsDateTime.Year, _lastDocFiledAsDateTime.Month, _lastDocFiledAsDateTime.Day);
        }

        private void txtDestFilePrefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtDestFilePrefix.IsEnabled)
                return;

            // Show dest file name
            DisplayDestFileName();
        }

        private void txtDestFileSuffix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtDestFileSuffix.IsEnabled)
                return;

            // Show dest file name
            DisplayDestFileName();
        }

        private void btnChangePrefix_Click(object sender, RoutedEventArgs e)
        {
            txtDestFilePrefix.IsEnabled = true;
            btnChangePrefix.IsEnabled = false;
        }

        private void btnDateFromPageText_Click(object sender, RoutedEventArgs e)
        {
            _touchFromPageText = TouchFromPageText.TOUCH_DATE;
        }

        private void btnSuffixFromPageText_Click(object sender, RoutedEventArgs e)
        {
            _touchFromPageText = TouchFromPageText.TOUCH_SUFFIX;
        }
    }

    class DocTypeCacheEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private string _thumbnailUniqName;
        public string ThumbUniqName
        {
            get { return _thumbnailUniqName; }
            set { _thumbnailUniqName = value; NotifyPropertyChanged("ThumbUniqName"); }
        }
        private BitmapImage _thumbnailBitmap;
        public BitmapImage ThumbBitmap
        {
            get { return _thumbnailBitmap; }
            set { _thumbnailBitmap = value; NotifyPropertyChanged("ThumbBitmap"); }
        }
        private string _docTypeName;
        public string DocTypeName
        {
            get { return _docTypeName; }
            set { _docTypeName = value; NotifyPropertyChanged("DocTypeName"); }
        }

    }
}
