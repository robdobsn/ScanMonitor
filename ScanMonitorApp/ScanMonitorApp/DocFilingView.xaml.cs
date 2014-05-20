using MahApps.Metro.Controls;
using Microsoft.WindowsAPICodePack.Dialogs;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
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
using System.Windows.Threading;

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
        private FiledDocInfo _curFiledDocInfo; 
        private DocTypeMatchResult _latestMatchResult;
        private DocType _curSelectedDocType = null;
        private BackgroundWorker _bwThreadForImagesPopup;
        ObservableCollection<DocTypeCacheEntry> _thumbnailsOfDocTypes = new ObservableCollection<DocTypeCacheEntry>();
        private ObservableCollection<DocTypeMatchResult> _listOfPossibleDocMatches = new ObservableCollection<DocTypeMatchResult>();
        private DateTime _lastDocFiledAsDateTime = DateTime.Now;
        private enum TouchFromPageText { TOUCH_NONE, TOUCH_DATE, TOUCH_SUFFIX, TOUCH_MONEY, TOUCH_EVENT_NAME, TOUCH_EVENT_DATE, TOUCH_EVENT_DESC, TOUCH_EVENT_LOCN }
        private TouchFromPageText _touchFromPageText = TouchFromPageText.TOUCH_NONE;
        private System.Windows.Threading.DispatcherTimer _timerForNewDocumentCheck;
        private string _overrideFolderForFiling = "";

        #region Init

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

            //  DispatcherTimer setup
            _timerForNewDocumentCheck = new System.Windows.Threading.DispatcherTimer();
            _timerForNewDocumentCheck.Tick += new EventHandler(NewDocumentTimer_Tick);
            _timerForNewDocumentCheck.Interval = new TimeSpan(0, 0, 2);
            _timerForNewDocumentCheck.Start();

            // Use a background worker to populate
            _bwThreadForImagesPopup.RunWorkerAsync();
        }

        private void NewDocumentTimer_Tick(object sender, EventArgs e)
        {
            if (txtDestFileSuffix.Text.Trim() == "")
                if (NewDocsReady())
                {
                    CheckForNewDocs(true);
                    ShowDocToBeFiled(_docsToBeFiledUniqNames.Count - 1);
                }
        }

        #endregion

        #region Current Doc To Be Filed

        private void GetListOfDocsToFile()
        {
            _docsToBeFiledUniqNames = _scanDocHandler.GetListOfUnfiledDocUniqNames();
        }

        private void CheckForNewDocs(bool assumeChanges)
        {
            if (assumeChanges || (_docsToBeFiledUniqNames.Count != _scanDocHandler.GetCountOfUnfiledDocs()))
            {
                GetListOfDocsToFile();
            }
        }

        private bool NewDocsReady()
        {
            return _docsToBeFiledUniqNames.Count != _scanDocHandler.GetCountOfUnfiledDocs();
        }

        private void ShowDocToBeFiled(int docIdx)
        {
            // Check for nothing to be filed
            if (_docsToBeFiledUniqNames.Count == 0)
            {
                _curDocToBeFiledIdxInList = 0;
                ShowDocumentFirstTime("");
                return;
            }

            // Handle range errors
            if (_curDocToBeFiledIdxInList >= _docsToBeFiledUniqNames.Count)
                _curDocToBeFiledIdxInList = _docsToBeFiledUniqNames.Count - 1;
            if (_curDocToBeFiledIdxInList < 0)
                _curDocToBeFiledIdxInList = 0;
            if ((docIdx < 0) || (docIdx >= _docsToBeFiledUniqNames.Count))
                return;

            // Show the doc
            _curDocToBeFiledIdxInList = docIdx;
            ShowDocumentFirstTime(_docsToBeFiledUniqNames[docIdx]);
        }

        #endregion

        #region Show Document Information

        private void ShowDocumentFirstTime(string uniqName)
        {
            // Load document info from db
            List<DocTypeMatchResult> possMatches = new List<DocTypeMatchResult>();
            ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
            if ((scanDocAllInfo == null) || (scanDocAllInfo.scanDocInfo == null))
            {
                _curDocScanPages = null;
                _curDocScanDocInfo = null;
                _curFiledDocInfo = null;
                _latestMatchResult = null;
                _curSelectedDocType = null;
            }
            else
            {
                _curDocScanPages = scanDocAllInfo.scanPages;
                _curDocScanDocInfo = scanDocAllInfo.scanDocInfo;
                _curFiledDocInfo = scanDocAllInfo.filedDocInfo;
            }

            // Re-check the document
            if (_curDocScanPages != null)
                _latestMatchResult = _docTypesMatcher.GetMatchingDocType(_curDocScanPages, possMatches);
            else
                _latestMatchResult = new DocTypeMatchResult();

            // Update the doc type list view for popup
            _listOfPossibleDocMatches.Clear();
            foreach(DocTypeMatchResult res in possMatches)
                _listOfPossibleDocMatches.Add(res);

            // Add list of previously used doctypes
            List<string> lastUsedDocTypes = _scanDocHandler.GetLastNDocTypesUsed(10);
            foreach (string s in lastUsedDocTypes)
            {
                DocTypeMatchResult mr = new DocTypeMatchResult();
                mr.docTypeName = s;
                _listOfPossibleDocMatches.Add(mr);
            }
                
            // Display image of first page
            DisplayScannedDocImage(1);

            // Show type and date
            ShowDocumentTypeAndDate(_latestMatchResult.docTypeName);
        }

        private void ShowDocumentTypeAndDate(string docTypeName)
        {
            // Get the current doc type
            _curSelectedDocType = _docTypesMatcher.GetDocType(docTypeName);

            // Reset the override folder
            _overrideFolderForFiling = "";
            if (btnMoveToUndo.IsEnabled != false)
                btnMoveToUndo.IsEnabled = false;

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
            string docTypeNameStr = (_curSelectedDocType == null) ? "" : _curSelectedDocType.docTypeName;
            if (((string)lblDocTypeName.Content) != docTypeNameStr)
                lblDocTypeName.Content = docTypeNameStr;

            // Field enables
            SetFieldEnable(txtDestFilePrefix, false);
            SetFieldEnable(btnChangePrefix, true);
            string destFilePrefixStr = (_curSelectedDocType == null) ? "" : _curSelectedDocType.GetFileNamePrefix();
            if (txtDestFilePrefix.Text != destFilePrefixStr)
                txtDestFilePrefix.Text = destFilePrefixStr;
            if (txtDestFileSuffix.Text != "")
            {
                txtDestFileSuffix.IsEnabled = false;
                txtDestFileSuffix.Text = "";
                txtDestFileSuffix.IsEnabled = true;
            }
            if (txtMoneySum.Text != "")
                txtMoneySum.Text = "";
            SetFieldEnable(txtMoneySum, true);
            chkFollowUpA.IsChecked = false;
            chkFollowUpB.IsChecked = false;
            chkCalendarEntry.IsChecked = false;
            _touchFromPageText = TouchFromPageText.TOUCH_NONE;

            // Display email addresses
            List<MailAddress> mailTo = ScanDocHandler.GetEmailToAddresses();
            if (chkFollowUpA.Visibility != System.Windows.Visibility.Hidden)
                chkFollowUpA.Visibility = System.Windows.Visibility.Hidden;
            if (mailTo.Count > 0)
            {
                string[] nameSplit = mailTo[0].DisplayName.Split(' ');
                if (nameSplit.Length > 0)
                {
                    chkFollowUpA.Content = "Follow up " + nameSplit[0];
                    chkFollowUpA.Tag = nameSplit[0];
                    chkFollowUpA.Visibility = System.Windows.Visibility.Visible;
                }
            }
            if (chkFollowUpB.Visibility != System.Windows.Visibility.Hidden)
                chkFollowUpB.Visibility = System.Windows.Visibility.Hidden;
            if (mailTo.Count > 1)
            {
                string[] nameSplit = mailTo[1].DisplayName.Split(' ');
                if (nameSplit.Length > 0)
                {
                    chkFollowUpB.Content = "Follow up " + nameSplit[0];
                    chkFollowUpB.Tag = nameSplit[0];
                    chkFollowUpB.Visibility = System.Windows.Visibility.Visible;
                }
            }

            // Visibility of event fields
            SetEventVisibility(false, false);
            SetFieldText(lblEventDuration, "1 Hour");
            SetFieldText(txtEventName, (txtDestFileSuffix.Text.Trim() != "") ? txtDestFileSuffix.Text.Trim() : txtDestFilePrefix.Text.Trim());
            SetFieldText(txtEventLocn, "");
            SetFieldText(txtEventDesc, "");
            SetFieldText(txtEventTime, "08:00");
            chkAttachFile.IsChecked = true;

            // Set doc date
            DateTime dateToUse = DateTime.Now;
            if (_latestMatchResult.docDate != DateTime.MinValue)
                dateToUse = _latestMatchResult.docDate;
            SetDateRollers(dateToUse.Year, dateToUse.Month, dateToUse.Day);

            // Show File number in list
            SetLabelContent(lblStatusBarFileNo, (_curDocToBeFiledIdxInList + 1).ToString() + " / " + _docsToBeFiledUniqNames.Count.ToString());

            // Show status of filing
            string statusStr = "Unfiled";
            Brush foreColour = Brushes.Black;
            SetFieldEnable(btnDeleteDoc, true);
            SetFieldEnable(btnProcessDoc, true);
            if (_curFiledDocInfo != null)
            {
                switch (_curFiledDocInfo.filedAt_finalStatus)
                {
                    case FiledDocInfo.DocFinalStatus.STATUS_DELETED:
                        statusStr = "DELETED";
                        foreColour = Brushes.Red;
                        SetFieldEnable(btnProcessDoc, false);
                        SetFieldEnable(btnDeleteDoc, false);
                        break;
                    case FiledDocInfo.DocFinalStatus.STATUS_DELETED_AFTER_EDIT:
                        statusStr = "DELETED AFTER EDIT";
                        foreColour = Brushes.Red;
                        SetFieldEnable(btnProcessDoc, false);
                        SetFieldEnable(btnDeleteDoc, false);
                        break;
                    case FiledDocInfo.DocFinalStatus.STATUS_FILED:
                        statusStr = "FILED";
                        foreColour = Brushes.Red;
                        SetFieldEnable(btnProcessDoc, false);
                        SetFieldEnable(btnDeleteDoc, false);
                        break;
                }
            }
            else if (_curDocScanDocInfo.flagForHelpFiling)
            {
                statusStr = "FLAGGED";
                foreColour = Brushes.Red;
            }
            SetLabelContent(lblStatusBarFileName, _curDocScanDocInfo.uniqName + " " + statusStr);
            lblStatusBarFileName.Foreground = foreColour;
            ShowFilingPath();
        }

        private void SetFieldEnable(UIElement el, bool en)
        {
            if (el.IsEnabled != en)
                el.IsEnabled = en;
        }

        private void SetFieldText(TextBox tb, string s)
        {
            if (tb.Text != s)
                tb.Text = s;
        }

        private void SetLabelContent(Label lb, string s)
        {
            if (((string)lb.Content) != s)
                lb.Content = s;
        }

        private void SetEventVisibility(bool vis, bool calendarEntry)
        {
            lblEventName.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            txtEventName.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            btnEventNameFromPageText.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            lblEventDate.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            datePickerEventDate.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            btnEventDateFromPageText.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            lblEventDesc.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            txtEventDesc.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            btnEventDescFromPageText.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            lblEventLocn.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            txtEventLocn.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            btnEventLocnFromPageText.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            txtEventTime.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            btnEventTime.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            lblEventDuration.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            btnEventDuration.Visibility = (vis & calendarEntry) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            lblEmailPassword.Visibility = vis ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            txtEmailPassword.Visibility = vis ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            chkAttachFile.Visibility = vis ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
        }

        private void ShowFilingPath()
        {
            bool pathContainsMacros = false;
            string destPath = GetFilingPath(ref pathContainsMacros);
            SetLabelContent(lblMoveToName, destPath);
            lblMoveToName.ToolTip = destPath;
        }

        #endregion

        #region Display of Scanned Image

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
                BitmapImage bi = new BitmapImage(new Uri("File:" + imgFileName));
                imageDocToFile.Source = bi;
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
            lblPageNum.Content = pageNum.ToString() + " / " + _curDocScanDocInfo.numPagesWithText.ToString() + ((_curDocScanDocInfo.numPages > _curDocScanDocInfo.numPagesWithText) ? "*" : "") ;
        }

        #endregion

        #region Button & Form Events

        private void btnFirstDoc_Click(object sender, RoutedEventArgs e)
        {
            CheckForNewDocs(false);
            ShowDocToBeFiled(0);
        }

        private void btnPrevDoc_Click(object sender, RoutedEventArgs e)
        {
            CheckForNewDocs(false);
            ShowDocToBeFiled(_curDocToBeFiledIdxInList - 1);
        }

        private void btnNextDoc_Click(object sender, RoutedEventArgs e)
        {
#if PERFORMANCE_CHECK
            DateTime dtDebug = DateTime.Now;
            Stopwatch stopWatch1 = new Stopwatch();
            stopWatch1.Start();
#endif
            CheckForNewDocs(false);
#if PERFORMANCE_CHECK
            stopWatch1.Stop();
            Stopwatch stopWatch2 = new Stopwatch();
            stopWatch2.Start();
#endif
            ShowDocToBeFiled(_curDocToBeFiledIdxInList + 1);
#if PERFORMANCE_CHECK
            stopWatch2.Stop();
            DateTime dtEndDebug = DateTime.Now;
            Dispatcher.BeginInvoke(new Action(() => logger.Info("DisplayUpdate: {0}ms", (DateTime.Now-dtDebug).TotalMilliseconds)), DispatcherPriority.ContextIdle, null);
            logger.Info("CheckForNewDocs : {0}ms, ShowDocToBeFiled : {0}ms, DateTime CrossCheck {0}ms", stopWatch1.ElapsedMilliseconds, stopWatch2.ElapsedMilliseconds, (dtEndDebug - dtDebug).TotalMilliseconds);
#endif
        }

        private void btnLastDoc_Click(object sender, RoutedEventArgs e)
        {
            CheckForNewDocs(false);
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
            CheckForNewDocs(true);
            ShowDocToBeFiled(_curDocToBeFiledIdxInList);
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

        private void btnPrefixErase_Click(object sender, RoutedEventArgs e)
        {
            txtDestFilePrefix.Text = "";
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

        private void btnSuffixErase_Click(object sender, RoutedEventArgs e)
        {
            txtDestFileSuffix.Text = "";
        }

        private void btnSuffixFromPageText_Click(object sender, RoutedEventArgs e)
        {
            _touchFromPageText = TouchFromPageText.TOUCH_SUFFIX;
        }

        private void btnMoneySumErase_Click(object sender, RoutedEventArgs e)
        {
            txtMoneySum.Text = "";
        }

        private void btnMoneySumFromPageText_Click(object sender, RoutedEventArgs e)
        {
            _touchFromPageText = TouchFromPageText.TOUCH_MONEY;
        }

        private void btnEventNameFromPageText_Click(object sender, RoutedEventArgs e)
        {
            _touchFromPageText = TouchFromPageText.TOUCH_EVENT_NAME;
        }

        private void btnEventDateFromPageText_Click(object sender, RoutedEventArgs e)
        {
            _touchFromPageText = TouchFromPageText.TOUCH_EVENT_DATE;
        }

        private void btnEventDescFromPageText_Click(object sender, RoutedEventArgs e)
        {
            _touchFromPageText = TouchFromPageText.TOUCH_EVENT_DESC;
        }

        private void btnEventLocnFromPageText_Click(object sender, RoutedEventArgs e)
        {
            _touchFromPageText = TouchFromPageText.TOUCH_EVENT_LOCN;
        }

        private void chkCalendarEntry_Checked(object sender, RoutedEventArgs e)
        {
            SetEventVisibility(true, true);
            datePickerEventDate.SelectedDate = GetDateFromRollers();
        }

        private void chkCalendarEntry_Unchecked(object sender, RoutedEventArgs e)
        {
            SetEventVisibility(false, false);
        }


        private void chkFollowUpB_Checked(object sender, RoutedEventArgs e)
        {
            SetEventVisibility(true, false);
        }

        private void chkFollowUpB_Unchecked(object sender, RoutedEventArgs e)
        {
            SetEventVisibility(false, false);
        }

        private void chkFollowUpA_Checked(object sender, RoutedEventArgs e)
        {
            SetEventVisibility(true, false);
        }

        private void chkFollowUpA_Unchecked(object sender, RoutedEventArgs e)
        {
            SetEventVisibility(false, false);
        }

        private void btnEventDuration_Click(object sender, RoutedEventArgs e)
        {
            FillDurationMenu();
            menuEventDurationList.IsOpen = true;
        }

        private void lblMoveToCtxt_Click(object sender, RoutedEventArgs e)
        {
            string filePath = lblMoveToName.Content.ToString();
            try
            {
                ScanDocHandler.ShowFileInExplorer(filePath.Replace("/", @"\"), false);
            }
            finally
            {

            }
        }

        private void FillDurationMenu()
        {
            List<TimeSpan> tss = new List<TimeSpan>();
            tss.Add(new TimeSpan(0, 30, 0));
            tss.Add(new TimeSpan(1, 0, 0));
            tss.Add(new TimeSpan(2, 0, 0));
            tss.Add(new TimeSpan(3, 0, 0));
            tss.Add(new TimeSpan(6, 0, 0));
            tss.Add(new TimeSpan(12, 0, 0));
            tss.Add(new TimeSpan(1, 0, 0, 0));
            tss.Add(new TimeSpan(2, 0, 0, 0));
            tss.Add(new TimeSpan(7, 0, 0, 0));
            menuEventDurationList.Items.Clear();
            foreach (TimeSpan ts in tss)
            {
                MenuItem mi = new MenuItem();
                string dayStr = (ts.Days > 0) ? (ts.Days + " Day" + ((ts.Days > 1) ? "s" : "")) : "";
                string hourStr = (ts.Hours > 0) ? (ts.Hours + " Hour" + ((ts.Hours > 1) ? "s" : "")) : "";
                string minStr = (ts.Minutes > 0) ? (ts.Minutes + " Minute" + ((ts.Minutes > 1) ? "s" : "")) : "";
                mi.Header = dayStr + (dayStr == "" ? "" : " ") + hourStr + (hourStr == "" ? "" : " ") + minStr;
                mi.Tag = ts.Days + ":" + ts.Hours + ":" + ts.Minutes;
                menuEventDurationList.Items.Add(mi);
            }
            menuEventDurationList.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(EventDurationSet));
        }

        private void EventDurationSet(object sender, RoutedEventArgs e)
        {
            RoutedEventArgs args = e as RoutedEventArgs;
            MenuItem item = args.OriginalSource as MenuItem;
            lblEventDuration.Text = item.Header.ToString();
        }

        private void btnEventTime_Click(object sender, RoutedEventArgs e)
        {
            FillTimeMenu();
            menuEventTimeList.IsOpen = true;
        }

        private void FillTimeMenu()
        {
            List<TimeSpan> tss = new List<TimeSpan>();
            for (int timeIdx = 0; timeIdx < 30; timeIdx++ )
                tss.Add(new TimeSpan(7+timeIdx/2, (timeIdx%2)*30, 0));
            menuEventTimeList.Items.Clear();
            foreach (TimeSpan ts in tss)
            {
                MenuItem mi = new MenuItem();
                mi.Header = ts.Hours.ToString("D2") + ":" + ts.Minutes.ToString("D2");
                menuEventTimeList.Items.Add(mi);
            }
            menuEventTimeList.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(EventTimeSet));
        }

        private void EventTimeSet(object sender, RoutedEventArgs e)
        {
            RoutedEventArgs args = e as RoutedEventArgs;
            MenuItem item = args.OriginalSource as MenuItem;
            txtEventTime.Text = item.Header.ToString();
        }

        private void btnFlagForHelpFiling_Click(object sender, RoutedEventArgs e)
        {
            if (_curDocScanDocInfo == null)
                return;

            // Get scan doc info
            _curDocScanDocInfo.flagForHelpFiling = !_curDocScanDocInfo.flagForHelpFiling;

            // Update db
            _scanDocHandler.AddOrUpdateScanDocRecInDb(_curDocScanDocInfo);

            // Re-show
            CheckForNewDocs(true);
            ShowDocToBeFiled(_curDocToBeFiledIdxInList);
        }

        private void btnShowMoveToFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_curSelectedDocType == null)
                return;

            // Check what path to use
            bool pathContainsMacros = false;
            string destPath = GetFilingPath(ref pathContainsMacros);

            CommonOpenFileDialog cofd = new CommonOpenFileDialog("Folder for filing");
            cofd.IsFolderPicker = true;
            cofd.Multiselect = false;
            cofd.InitialDirectory = destPath;
            cofd.DefaultDirectory = destPath;
            cofd.EnsurePathExists = true;
            CommonFileDialogResult result = cofd.ShowDialog(this);
            if (result == CommonFileDialogResult.Ok)
            {
                string folderName = cofd.FileName;
                // If the folder for this doctype was the base folder then accept the change
                if (_curSelectedDocType.moveFileToPath.Trim() != Properties.Settings.Default.BasePathForFilingFolderSelection.Trim())
                {
                    // Ask the user if they are sure
                    MessageDialog.MsgDlgRslt rslt = MessageDialog.Show("File to " + folderName + " ?\n" + "Are you sure?", "Yes", "No", "Cancel", btnShowMoveToFolder, this);
                    if (rslt == MessageDialog.MsgDlgRslt.RSLT_YES)
                    {
                        _overrideFolderForFiling = folderName;
                        btnMoveToUndo.IsEnabled = true;
                    }
                }
            }
            ShowFilingPath();
        }

        private void btnMoveToUndo_Click(object sender, RoutedEventArgs e)
        {
            _overrideFolderForFiling = "";
            btnMoveToUndo.IsEnabled = false;
            ShowFilingPath();
        }

        private void btnEditPdf_Click(object sender, RoutedEventArgs e)
        {
            if (_curDocScanDocInfo == null)
                return;

            // Check not already busy filing a doc
            if (_scanDocHandler.IsBusy())
                return;

            PdfEditorWindow pew = new PdfEditorWindow();
            pew.OpenEmbeddedPdfEditor(_curDocScanDocInfo.origFileName, HandlePdfEditSaveComplete);
            pew.ShowDialog();
        }

        private void HandlePdfEditSaveComplete(string originalFileName, List<string> savedFileNames)
        {
            // New files should be picked up by the folder watcher

            // So all we have to do is delete the original
            bool deletedOk = _scanDocHandler.DeleteFile(_curDocScanDocInfo.uniqName, _curFiledDocInfo, _curDocScanDocInfo.origFileName, true);
            if (!deletedOk)
            {
                lblStatusBarProcStatus.Content = "Failed to remove original file";
                lblStatusBarProcStatus.Foreground = Brushes.Red;
            }
            else
            {
                lblStatusBarProcStatus.Content = "Ok";
                lblStatusBarProcStatus.Foreground = Brushes.Black;
            }

            // Goto a file if there is one
            CheckForNewDocs(true);
            ShowDocToBeFiled(_curDocToBeFiledIdxInList);
        }

        private void btnAuditTrail_Click(object sender, RoutedEventArgs e)
        {
            AuditView av = new AuditView(_scanDocHandler, _docTypesMatcher);
            av.ShowDialog();
        }

        #endregion

        #region Handle deletion of document

        private void btnDeleteDoc_Click(object sender, RoutedEventArgs e)
        {
            if (_curDocScanDocInfo == null)
                return;

            // Check not already busy filing a doc
            if (_scanDocHandler.IsBusy())
                return;

            // Ask user if sure
            MessageDialog.MsgDlgRslt rslt = MessageDialog.Show("Delete " + _curDocScanDocInfo.uniqName + " ?\n" + "Are you sure?", "Yes", "No", "Cancel", btnDeleteDoc, this);
            if (rslt == MessageDialog.MsgDlgRslt.RSLT_YES)
            {
                // Delete file
                bool deletedOk = _scanDocHandler.DeleteFile(_curDocScanDocInfo.uniqName, _curFiledDocInfo, _curDocScanDocInfo.origFileName, false);
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
                CheckForNewDocs(true);
                ShowDocToBeFiled(_curDocToBeFiledIdxInList-1);
            }
        }

        #endregion

        #region Handle processing of the document

        private void btnProcessDoc_Click(object sender, RoutedEventArgs e)
        {
            if (_curDocScanDocInfo == null)
                return;

            // Check not already busy filing a doc
            if (_scanDocHandler.IsBusy())
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
            string fullPathAndFileNameForFilingTo = FormFullPathAndFileNameForFiling();
            bool rslt = _scanDocHandler.CheckOkToFileDoc(fullPathAndFileNameForFilingTo, out rsltText);
            if (!rslt)
            {
                lblStatusBarProcStatus.Content = rsltText;
                lblStatusBarProcStatus.Foreground = Brushes.Red;
                return;
            }

            // Get follow up strings
            string followUpStr = (bool)chkFollowUpB.IsChecked ? chkFollowUpB.Tag.ToString() : " ";
            followUpStr = followUpStr + ((bool)chkFollowUpA.IsChecked ? chkFollowUpA.Tag.ToString() : "");
            followUpStr = followUpStr.Trim();
            string addToCalendarStr = (bool)chkCalendarEntry.IsChecked ? "Calendar" : "";
            string flagAttachFile = (bool)chkAttachFile.IsChecked ? "Attached" : "";

            // Check if email is required but password not set
            if ((followUpStr.Trim() != "") || (addToCalendarStr.Trim() != ""))
            {
                if (txtEmailPassword.Password.Trim() == "")
                {
                    lblStatusBarProcStatus.Content = "Email password must be set";
                    lblStatusBarProcStatus.Foreground = Brushes.Red;
                    return;
                }
            }

            // Save time as filed
            _lastDocFiledAsDateTime = GetDateFromRollers();

            // Process the doc
            rsltText = "";
            FiledDocInfo fdi = _curFiledDocInfo;
            if (fdi == null)
                fdi = new FiledDocInfo(_curDocScanDocInfo.uniqName);
            DateTime selectedDateTime = DateTime.MinValue;
            TimeSpan eventDuration = new TimeSpan();

            // Check for calendar entry
            if (addToCalendarStr != "")
            {
                selectedDateTime = GetEventDateAndTime((DateTime)datePickerEventDate.SelectedDate, txtEventTime.Text);
                if (selectedDateTime == DateTime.MinValue)
                {
                    lblStatusBarProcStatus.Content = "Event time/date problem";
                    lblStatusBarProcStatus.Foreground = Brushes.Red;
                    return;
                }

                eventDuration = GetEventDurationFromString((string)lblEventDuration.Text);
                if (eventDuration.TotalDays == 0)
                {
                    lblStatusBarProcStatus.Content = "Event duration problem";
                    lblStatusBarProcStatus.Foreground = Brushes.Red;
                    return;
                }
            }

            // Set the filing information
            fdi.SetDocFilingInfo(_curSelectedDocType.docTypeName, fullPathAndFileNameForFilingTo, GetDateFromRollers(), txtMoneySum.Text, followUpStr,
                        addToCalendarStr, txtEventName.Text, selectedDateTime, eventDuration, txtEventDesc.Text, txtEventLocn.Text, 
                        flagAttachFile);

            // Start filing the document
            lblStatusBarProcStatus.Content = "Processing ...";
            lblStatusBarProcStatus.Foreground = Brushes.Black;
            _scanDocHandler.StartProcessFilingOfDoc(SetStatusText, FilingCompleteCallback, _curDocScanDocInfo, fdi, txtEmailPassword.Password, out rsltText);
        }

        #endregion

        #region Image events

        private void imageDocToFile_MouseMove(object sender, MouseEventArgs e)
        {
            // Only show tooltips when there is no touch select going on
            if (_touchFromPageText != TouchFromPageText.TOUCH_NONE)
                return;

            // Show tool tip
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
            if ((_touchFromPageText == TouchFromPageText.TOUCH_DATE) || (_touchFromPageText == TouchFromPageText.TOUCH_EVENT_DATE))
            {
                bool earliestDateReq = false;
                bool latestDateReq = false;
                List<ExtractedDate> extractedDates = new List<ExtractedDate>();
                DocTextAndDateExtractor.SearchForDateItem(_curDocScanPages, "", docRect, 0, extractedDates, ref latestDateReq, ref earliestDateReq, pgNum, false);
                if (extractedDates.Count <= 0)
                    DocTextAndDateExtractor.SearchForDateItem(_curDocScanPages, "", docRect, 0, extractedDates, ref latestDateReq, ref earliestDateReq, pgNum, true);
                if (extractedDates.Count > 0)
                {
                    if (_touchFromPageText == TouchFromPageText.TOUCH_DATE)
                        SetDateRollers(extractedDates[0].dateTime.Year, extractedDates[0].dateTime.Month, extractedDates[0].dateTime.Day);
                    else
                        datePickerEventDate.SelectedDate = new DateTime(extractedDates[0].dateTime.Year, extractedDates[0].dateTime.Month, extractedDates[0].dateTime.Day);
                }
            }
            else if ((_touchFromPageText == TouchFromPageText.TOUCH_SUFFIX) || (_touchFromPageText == TouchFromPageText.TOUCH_EVENT_NAME) || (_touchFromPageText == TouchFromPageText.TOUCH_EVENT_DESC) || (_touchFromPageText == TouchFromPageText.TOUCH_EVENT_LOCN))
            {
                string extractedText = DocTextAndDateExtractor.ExtractTextFromPage(_curDocScanPages, docRect, pgNum);
                if (extractedText != "")
                {
                    if (_touchFromPageText == TouchFromPageText.TOUCH_SUFFIX)
                        txtDestFileSuffix.Text = txtDestFileSuffix.Text + (txtDestFileSuffix.Text.Trim() == "" ? "" : " ") + extractedText;
                    else if (_touchFromPageText == TouchFromPageText.TOUCH_EVENT_NAME)
                        txtEventName.Text = txtEventName.Text + (txtEventName.Text.Trim() == "" ? "" : " ") + extractedText;
                    else if (_touchFromPageText == TouchFromPageText.TOUCH_EVENT_DESC)
                        txtEventDesc.Text = txtEventDesc.Text + (txtEventDesc.Text.Trim() == "" ? "" : " ") + extractedText;
                    else if (_touchFromPageText == TouchFromPageText.TOUCH_EVENT_LOCN)
                        txtEventLocn.Text = txtEventLocn.Text + (txtEventLocn.Text.Trim() == "" ? "" : " ") + extractedText;
                }
            }
            else if (_touchFromPageText == TouchFromPageText.TOUCH_MONEY)
            {
                string extractedText = DocTextAndDateExtractor.ExtractTextFromPage(_curDocScanPages, docRect, pgNum);
                if (extractedText != "")
                {
                    // Get currency symbol if available
                    int currencyLen = 1;
                    int currencyPos = extractedText.IndexOf('$');
                    if (currencyPos < 0)
                        currencyPos = extractedText.IndexOf('£');
                    if (currencyPos < 0)
                        currencyPos = extractedText.IndexOf('€');

                    // Find number matching money format
                    Match match = Regex.Match(extractedText, @"((?:^\d{1,3}(?:\.?\d{3})*(?:,\d{2})?$))|((?:^\d{1,3}(?:,?\d{3})*(?:\.\d{2})?$)((\d+)?(\.\d{1,2})?))");
                    if ((match.Success) && (match.Groups.Count > 1))
                    {
                        // Found string may be ###,###.## or ###.###,##
                        string foundStr = match.Groups[1].Value;
                        if (foundStr.Trim() == "")
                        {
                            foundStr = match.Groups[2].Value;
                            foundStr = foundStr.Replace(",", "");
                        }
                        else
                        {
                            foundStr = foundStr.Replace(".", "");
                        }

                        // Form string
                        if (currencyPos > match.Index)
                            currencyPos = -1;
                        string numberText = (currencyPos >= 0 ? extractedText.Substring(currencyPos, currencyLen) : "") + foundStr;
                        txtMoneySum.Text = txtMoneySum.Text + (txtMoneySum.Text.Trim() == "" ? "" : " ") + numberText;
                    }
                    else if (currencyPos >= 0)
                    {
                        txtMoneySum.Text = extractedText.Substring(currencyPos, currencyLen) + txtMoneySum.Text;
                    }
                }
            }

            // Cancel touch activity
            _touchFromPageText = TouchFromPageText.TOUCH_NONE;
        }

        #endregion

        #region Thumbnail picker

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

        #endregion

        #region Utility functions

        private void DisplayDestFileName()
        {
            if ((_curDocScanDocInfo != null) && (_curSelectedDocType != null))
            {
                string dfn = ScanDocHandler.FormatFileNameFromMacros(_curDocScanDocInfo.origFileName, _curSelectedDocType.renameFileTo, GetDateFromRollers(), txtDestFilePrefix.Text, txtDestFileSuffix.Text, _curSelectedDocType.docTypeName);
                if (((string)lblDestFileName.Content) != dfn) 
                    lblDestFileName.Content = dfn;
            }
            else
            {
                lblDestFileName.Content = "";
            }
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
            if (lblDayVal.Text != day.ToString())
                lblDayVal.Text = day.ToString();
            if (lblMonthVal.Text != dt.ToString("MMMM"))
                lblMonthVal.Text = dt.ToString("MMMM");
            if (lblYearVal.Text != dt.Year.ToString())
                lblYearVal.Text = dt.Year.ToString();

            // Show dest file name (which can change based on date)
            DisplayDestFileName();
        }

        public static Point ConvertImagePointToDocPoint(Image img, double x, double y)
        {
            double tlx = 100 * x / img.ActualWidth;
            double tly = 100 * y / img.ActualHeight;
            return new Point(tlx, tly);
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

        private string GetFilingPath(ref bool pathContainsMacros)
        {
            pathContainsMacros = false;

            // Check whether to use overridden path or not
            string destPath = "";
            if (_curSelectedDocType != null)
            {
                if (_overrideFolderForFiling.Trim() != "")
                    destPath = _overrideFolderForFiling.Trim();
                else
                    destPath = _docTypesMatcher.ComputeExpandedPath(_curSelectedDocType.moveFileToPath, GetDateFromRollers(), false, ref pathContainsMacros);
            }

            return destPath;
        }

        private string FormFullPathAndFileNameForFiling()
        {
            // Get the fully expanded path
            bool pathContainsMacros = false;
            string destPath = GetFilingPath(ref pathContainsMacros);

            // Create folder if the path contains macros
            try
            {
                if (!Directory.Exists(destPath) && pathContainsMacros)
                    Directory.CreateDirectory(destPath);
            }
            catch
            {
            }
            return System.IO.Path.Combine(destPath, (string)lblDestFileName.Content);
        }

        public void SetStatusText(string str)
        {
            lblStatusBarProcStatus.Content = str;
        }
    
        public void FilingCompleteCallback(string str)
        {
            lblStatusBarProcStatus.Content = str;
            // Show next document
            CheckForNewDocs(true);
            ShowDocToBeFiled(_curDocToBeFiledIdxInList-1);
        }

        private static DateTime GetEventDateAndTime(DateTime dateFromPicker, string timeField)
        {
            string[] timeSplit = timeField.Split(':');
            if (timeSplit.Length < 2)
                return DateTime.MinValue;
            int hour = 0;
            int min = 0;
            Int32.TryParse(timeSplit[0], out hour);
            Int32.TryParse(timeSplit[1], out min);
            if (hour < 0 || hour > 23)
                return DateTime.MinValue;
            if (min < 0 || min > 59)
                return DateTime.MinValue;
            return dateFromPicker.Add(new TimeSpan(hour, min, 0));
        }

        private static TimeSpan GetEventDurationFromString(string durstr)
        {
            TimeSpan ts = new TimeSpan();
            Match match = Regex.Match(durstr, @"(\d+)");
            if ((!match.Success) || (match.Groups.Count < 2))
                return ts;
            int durVal = 0;
            Int32.TryParse(match.Groups[1].Value, out durVal);
            if (durstr.IndexOf("day", StringComparison.OrdinalIgnoreCase) >= 0)
                ts = new TimeSpan(durVal, 0, 0, 0);
            else if (durstr.IndexOf("hour", StringComparison.OrdinalIgnoreCase) >= 0)
                ts = new TimeSpan(durVal, 0, 0);
            else
                ts = new TimeSpan(0, durVal, 0);
            return ts;
        }

        #endregion


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
