//#define TEST_PERF_SHOWDOCFIRSTTIME
//#define PERFORMANCE_CHECK
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
        const bool USE_QUICK_DOC_MENU = false;
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private ScanDocHandler _scanDocHandler;
        private DocTypesMatcher _docTypesMatcher;
        private int _curDocDisplay_pageNum;
        private int _curDocToBeFiledIdxInList = 0;
        private ScanPages _curDocScanPages;
        private ScanDocInfo _curDocScanDocInfo;
        private FiledDocInfo _curFiledDocInfo;
        private DocType _curSelectedDocType = null;
        private BackgroundWorker _bwThreadForImagesPopup;
        private BackgroundWorker _bwThreadForCurDocDisplay;
        private BackgroundWorker _bwThreadForDocTypeDisplay;
        private bool _newCurDocProcessingCancel = false;
        private EventWaitHandle _newCurDocSignal = new EventWaitHandle(false, EventResetMode.AutoReset);
        private EventWaitHandle _newDocTypeSignal = new EventWaitHandle(false, EventResetMode.AutoReset);
        private string _curSelectedDocTypeName;
        ObservableCollection<DocTypeCacheEntry> _thumbnailsOfDocTypes = new ObservableCollection<DocTypeCacheEntry>();
        private ObservableCollection<DocTypeMatchResult> _listOfPossibleDocMatches = new ObservableCollection<DocTypeMatchResult>();
        private DateTime _lastDocFiledAsDateTime = DateTime.Now;
        private enum TouchFromPageText { TOUCH_NONE, TOUCH_DATE, TOUCH_SUFFIX, TOUCH_MONEY, TOUCH_EVENT_NAME, TOUCH_EVENT_DATE, TOUCH_EVENT_DESC, TOUCH_EVENT_LOCN }
        private TouchFromPageText _touchFromPageText = TouchFromPageText.TOUCH_SUFFIX;
        private System.Windows.Threading.DispatcherTimer _timerForNewDocumentCheck;
        private string _overrideFolderForFiling = "";
        private string _lastHashOfUnfiledDocs = "";
        private enum dateRollerChange
        {
            none, drc_dayPlus, drc_dayMinus, drc_monthPlus, drc_monthMinus, drc_yearPlus, drc_yearMinus
        }
        private WindowClosingDelegate _windowClosingCB;
        private bool _openFullScreen = false;

        #region Init

        public DocFilingView(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher, 
                    WindowClosingDelegate windowClosingCB, bool openFullScreen)
        {
            InitializeComponent();
            _openFullScreen = openFullScreen;
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;
            _windowClosingCB = windowClosingCB;
            popupDocTypePickerThumbs.ItemsSource = _thumbnailsOfDocTypes;
            popupDocTypeResultList.ItemsSource = _listOfPossibleDocMatches;
            ShowDocToBeFiled(0);

            // Image filler thread
            _bwThreadForImagesPopup = new BackgroundWorker();
            _bwThreadForImagesPopup.WorkerSupportsCancellation = true;
            _bwThreadForImagesPopup.WorkerReportsProgress = true;
            _bwThreadForImagesPopup.DoWork += new DoWorkEventHandler(AddImages_DoWork);

            // Timer to display latest doc
            _timerForNewDocumentCheck = new System.Windows.Threading.DispatcherTimer();
            _timerForNewDocumentCheck.Tick += new EventHandler(NewDocumentTimer_Tick);
            _timerForNewDocumentCheck.Interval = new TimeSpan(0, 0, 2);
            _timerForNewDocumentCheck.Start();

            // Current document display thread
            _bwThreadForCurDocDisplay = new BackgroundWorker();
            _bwThreadForCurDocDisplay.WorkerSupportsCancellation = true;
            _bwThreadForCurDocDisplay.WorkerReportsProgress = false;
            _bwThreadForCurDocDisplay.DoWork += new DoWorkEventHandler(CurDocDisplay_DoWork);

            // DocType display thread
            _bwThreadForDocTypeDisplay = new BackgroundWorker();
            _bwThreadForDocTypeDisplay.WorkerSupportsCancellation = true;
            _bwThreadForDocTypeDisplay.WorkerReportsProgress = false;
            _bwThreadForDocTypeDisplay.DoWork += new DoWorkEventHandler(DocTypeChanged_DoWork);

            // Use a background worker to populate
            _bwThreadForImagesPopup.RunWorkerAsync();

            // Use a background worker to populate
            _bwThreadForCurDocDisplay.RunWorkerAsync();
            _bwThreadForDocTypeDisplay.RunWorkerAsync();

        }

        private void NewDocumentTimer_Tick(object sender, EventArgs e)
        {
            if (txtDestFileSuffix.Text.Trim() == "")
                if (_lastHashOfUnfiledDocs != _scanDocHandler.GetHashOfUnfiledDocs())
                    ShowDocToBeFiled(_curDocToBeFiledIdxInList);
        }

        #endregion

        #region Current Doc To Be Filed

        private void ShowDocToBeFiled(int docIdx)
        {
#if TEST_PERF_SHOWDOCFIRSTTIME
            Stopwatch stopWatch1 = new Stopwatch();
            stopWatch1.Start();
#endif
            // Check if docIdx is valid
            if (docIdx >= _scanDocHandler.GetCountOfUnfiledDocs())
                docIdx = _scanDocHandler.GetCountOfUnfiledDocs() - 1;
            if (docIdx < 0)
                docIdx = 0;

#if TEST_PERF_SHOWDOCFIRSTTIME
            stopWatch1.Stop();
            Stopwatch stopWatch2 = new Stopwatch();
            stopWatch2.Start();
#endif
            // Get name of doc
            string uniqName = _scanDocHandler.GetUniqNameOfDocToBeFiled(docIdx);

#if TEST_PERF_SHOWDOCFIRSTTIME
            stopWatch2.Stop();
            Stopwatch stopWatch3 = new Stopwatch();
            stopWatch3.Start();
#endif
            // Save docIdx and show doc
            if (uniqName != "")
                _curDocToBeFiledIdxInList = docIdx;
            ShowDocumentFirstTime(uniqName);

#if TEST_PERF_SHOWDOCFIRSTTIME
            stopWatch3.Stop();
            Stopwatch stopWatch4 = new Stopwatch();
            stopWatch4.Start();
#endif
            // Save hash of unfiled docs
            _lastHashOfUnfiledDocs = _scanDocHandler.GetHashOfUnfiledDocs();

#if TEST_PERF_SHOWDOCFIRSTTIME
            stopWatch4.Stop();
            logger.Info("ShowDocToBeFiled: A {0:0.00}, B {1:0.00}, C {2:0.00}, D {3:0.00}", stopWatch1.ElapsedTicks * 1000.0 / Stopwatch.Frequency,
                stopWatch2.ElapsedTicks * 1000.0 / Stopwatch.Frequency, stopWatch3.ElapsedTicks * 1000.0 / Stopwatch.Frequency,
                stopWatch4.ElapsedTicks * 1000.0 / Stopwatch.Frequency);
#endif
        }

        #endregion

        #region Show Document Information

        private void ShowDocumentFirstTime(string uniqName)
        {
            // Load document info from db
            ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfoCached(uniqName);
            if ((scanDocAllInfo == null) || (scanDocAllInfo.scanDocInfo == null))
            {
                _curDocScanPages = null;
                _curDocScanDocInfo = null;
                _curFiledDocInfo = null;
                _curSelectedDocType = null;
            }
            else
            {
                _curDocScanPages = scanDocAllInfo.scanPages;
                _curDocScanDocInfo = scanDocAllInfo.scanDocInfo;
                _curFiledDocInfo = scanDocAllInfo.filedDocInfo;
            }


            // Display image of first page
            DisplayScannedDocImage(1);

            // Signal that the cur doc has changed
            _newCurDocProcessingCancel = true;
            _newCurDocSignal.Set();

        }

        private void CurDocDisplay_DoWork(object sender, EventArgs e)
        {
            while (true)
            {
                // Wait here until a new doc is requested
                _newCurDocSignal.WaitOne();
                _newCurDocProcessingCancel = false;

                HandleDocMatchingAndDisplay();

            }
        }

        private void HandleDocMatchingAndDisplay()
        {
#if TEST_PERF_SHOWDOCFIRSTTIME
            Stopwatch stopWatch1 = new Stopwatch();
            Stopwatch stopWatch2 = new Stopwatch();
            Stopwatch stopWatch3 = new Stopwatch();
            Stopwatch stopWatch4 = new Stopwatch();
            Stopwatch stopWatch5 = new Stopwatch();
            Stopwatch stopWatch6 = new Stopwatch();
            stopWatch1.Start();
#endif

#if TEST_PERF_SHOWDOCFIRSTTIME
            stopWatch1.Stop();
            stopWatch2.Start();
#endif
            // Re-check the document
            DocTypeMatchResult latestMatchResult;
            List<DocTypeMatchResult> possMatches = new List<DocTypeMatchResult>();
            if (_curDocScanPages != null)
            {
                Task<DocTypeMatchResult> rslt = _docTypesMatcher.GetMatchingDocType(_curDocScanPages, possMatches);
                rslt.Wait(30000);
                latestMatchResult = rslt.Result;
            }
            else
            {
                latestMatchResult = new DocTypeMatchResult();
            }

            // Check for a new doc - so cancel processing this one
            if (!_newCurDocProcessingCancel)
            {
#if TEST_PERF_SHOWDOCFIRSTTIME
                stopWatch2.Stop();
                stopWatch3.Start();
#endif

                // Update the doc type list view for popup
                List<DocTypeMatchResult> possDocMatches = new List<DocTypeMatchResult>();
                foreach (DocTypeMatchResult res in possMatches)
                    possDocMatches.Add(res);

                // Check for a new doc - so cancel processing this one
                if (!_newCurDocProcessingCancel)
                {

#if TEST_PERF_SHOWDOCFIRSTTIME
                    stopWatch3.Stop();
                    stopWatch4.Start();
#endif

                    // Add list of previously used doctypes
                    List<string> lastUsedDocTypes = _scanDocHandler.GetLastNDocTypesUsed(10);
                    foreach (string s in lastUsedDocTypes)
                    {
                        DocTypeMatchResult mr = new DocTypeMatchResult();
                        mr.docTypeName = s;
                        possDocMatches.Add(mr);
                    }

                    // Check for a new doc - so cancel processing this one
                    if (!_newCurDocProcessingCancel)
                    {

#if TEST_PERF_SHOWDOCFIRSTTIME
                        stopWatch4.Stop();
                        stopWatch5.Start();
#endif

                        this.Dispatcher.BeginInvoke((Action)delegate()
                        {
                            _listOfPossibleDocMatches.Clear();
                            foreach (DocTypeMatchResult dtmr in possDocMatches)
                                _listOfPossibleDocMatches.Add(dtmr);
                        });

                        // Check for a new doc - so cancel processing this one
                        if (!_newCurDocProcessingCancel)
                        {

#if TEST_PERF_SHOWDOCFIRSTTIME
                            stopWatch5.Stop();
                            stopWatch6.Start();
#endif

                            // Show type and date
                            ShowDocumentTypeAndDate(latestMatchResult.docTypeName);
                        }
                    }
                }
            }

#if TEST_PERF_SHOWDOCFIRSTTIME
            stopWatch6.Stop();
            logger.Info("ShowDocFirstTime: {6} A {0:0.00}, B {1:0.00}, C {2:0.00}, D {3:0.00}, E {4:0.00}, F {5:0.00}", stopWatch1.ElapsedTicks * 1000.0 / Stopwatch.Frequency,
                stopWatch2.ElapsedTicks * 1000.0 / Stopwatch.Frequency, stopWatch3.ElapsedTicks * 1000.0 / Stopwatch.Frequency,
                stopWatch4.ElapsedTicks * 1000.0 / Stopwatch.Frequency, stopWatch5.ElapsedTicks * 1000.0 / Stopwatch.Frequency,
                stopWatch6.ElapsedTicks * 1000.0 / Stopwatch.Frequency, _newCurDocProcessingCancel ? "CANCELLED" : "");
#endif
        }

        private void ShowDocumentTypeAndDate(string docTypeName)
        {
            _curSelectedDocTypeName = docTypeName;
            _newDocTypeSignal.Set();

        }


        private void DocTypeChanged_DoWork(object sender, EventArgs e)
        {
            while (true)
            {
                // Wait here until a doc type change occurs
                _newDocTypeSignal.WaitOne();

                // Get the current doc type
                _curSelectedDocType = _docTypesMatcher.GetDocType(_curSelectedDocTypeName);

                // Update the UI on its thread
                this.Dispatcher.BeginInvoke((Action)delegate()
                {
                    CompleteDocTypeChange(_curSelectedDocTypeName);
                });
            }
        }

        private void CompleteDocTypeChange(string docTypeName)
        {

            // Reset the override folder
            _overrideFolderForFiling = "";
            if (btnMoveToUndo.IsEnabled != false)
                btnMoveToUndo.IsEnabled = false;

            // Show email password
            txtEmailPassword.Password = _scanDocHandler.GetEmailPassword();

            // Extract date info again and update latest match result
            int bestDateIdx = 0;
            List<ExtractedDate> extractedDates = DocTextAndDateExtractor.ExtractDatesFromDoc(_curDocScanPages,
                                                    (_curSelectedDocType == null) ? "" : _curSelectedDocType.dateExpression,
                                                    out bestDateIdx);
            DateTime docDate = docDate = DateTime.MinValue;
            if (extractedDates.Count > 0)
                docDate = extractedDates[bestDateIdx].dateTime;

            // Show doc type
            txtDocTypeName.IsEnabled = false;
            string docTypeNameStr = (_curSelectedDocType == null) ? "" : _curSelectedDocType.docTypeName;
            if (txtDocTypeName.Text != docTypeNameStr)
                txtDocTypeName.Text = docTypeNameStr;

            // Field enables
            SetFieldEnable(txtDestFilePrefix, false);
            SetFieldEnable(btnChangePrefix, true);
            string destFilePrefixStr = (_curSelectedDocType == null) ? "" : DocType.GetFileNamePrefix(_curSelectedDocType.docTypeName);
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
            _touchFromPageText = TouchFromPageText.TOUCH_SUFFIX;

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
            if (docDate != DateTime.MinValue)
                dateToUse = docDate;
            SetDateRollers(dateToUse.Year, dateToUse.Month, dateToUse.Day, dateRollerChange.none);

            // Show File number in list
            if (_scanDocHandler.GetCountOfUnfiledDocs() == 0)
                SetLabelContent(lblStatusBarFileNo, "None");
            else
                SetLabelContent(lblStatusBarFileNo, (_curDocToBeFiledIdxInList + 1).ToString() + " / " + _scanDocHandler.GetCountOfUnfiledDocs().ToString());

            // Show status of filing
            string statusStr = "Unfiled";
            Brush foreColour = Brushes.Black;
            bool processButtonsEnabled = true;
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
                        statusStr = "REPLACED/EDITED";
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
            else if ((_curDocScanDocInfo != null) && (_curDocScanDocInfo.flagForHelpFiling))
            {
                statusStr = "FLAGGED";
                foreColour = Brushes.Red;
            }
            else if (_curDocScanDocInfo != null)
            {
                if (!File.Exists(_curDocScanDocInfo.GetOrigFileNameWin()))
                {
                    string archiveFileName = ScanDocHandler.GetArchiveFileName(_curDocScanDocInfo.uniqName);
                    if (!File.Exists(archiveFileName))
                    {
                        statusStr = "ORIGINAL MISSING!";
                        foreColour = Brushes.Red;
                        btnEditPdf.IsEnabled = false;
                        processButtonsEnabled = false;
                    }
                    else
                    {
                        statusStr = "Unfiled (original moved but backup ok)";
                    }
                }
            }
            btnEditPdf.IsEnabled = processButtonsEnabled;
            btnProcessDoc.IsEnabled = processButtonsEnabled;
            SetLabelContent(lblStatusBarFileName, (_curDocScanDocInfo != null) ? (_curDocScanDocInfo.uniqName + " " + statusStr) : "");
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

        private void SetLabelContent(object lb, string s)
        {
            if (lb.GetType() == typeof(Label))
            {
                if ((((Label)lb).Content.ToString()) != s)
                    ((Label)lb).Content = s;
            }
            else
            {
                if ((((TextBlock)lb).Text.ToString()) != s)
                    ((TextBlock)lb).Text = s;
            }
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
            lblMoveToNameToolTipText.Text = ScanUtils.GetFolderContentsAsString(destPath);
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
            lblPageNum.Content = pageNum.ToString() + " / " + _curDocScanDocInfo.numPagesWithText.ToString() + ((_curDocScanDocInfo.numPages > _curDocScanDocInfo.numPagesWithText) ? "*" : "");
        }

        #endregion

        #region Button & Form Events

        private void btnFirstDoc_Click(object sender, RoutedEventArgs e)
        {
            ShowDocToBeFiled(0);
        }

        private void btnPrevDoc_Click(object sender, RoutedEventArgs e)
        {
            ShowDocToBeFiled(_curDocToBeFiledIdxInList - 1);
        }

        private void btnNextDoc_Click(object sender, RoutedEventArgs e)
        {
#if PERFORMANCE_CHECK
            DateTime dtDebug = DateTime.Now;
            Stopwatch stopWatch1 = new Stopwatch();
            stopWatch1.Start();
#endif
            ShowDocToBeFiled(_curDocToBeFiledIdxInList + 1);
#if PERFORMANCE_CHECK
            stopWatch1.Stop();
            DateTime dtEndDebug = DateTime.Now;
            //Dispatcher.BeginInvoke(new Action(() => logger.Info("DisplayUpdate: {0}ms", (DateTime.Now-dtDebug).TotalMilliseconds)), DispatcherPriority.ContextIdle, null);
            logger.Info("btnNextDoc_Click : {0}ms, DateTime CrossCheck {1}ms", stopWatch1.ElapsedMilliseconds, (dtEndDebug - dtDebug).TotalMilliseconds);
#endif
        }

        private void btnLastDoc_Click(object sender, RoutedEventArgs e)
        {
            ShowDocToBeFiled(_scanDocHandler.GetCountOfUnfiledDocs() - 1);
        }

        private void btnBackPage_Click(object sender, RoutedEventArgs e)
        {
            if ((_curDocToBeFiledIdxInList < 0) || (_curDocToBeFiledIdxInList >= _scanDocHandler.GetCountOfUnfiledDocs()))
                return;
            DisplayScannedDocImage(_curDocDisplay_pageNum - 1);
        }

        private void btnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if ((_curDocToBeFiledIdxInList < 0) || (_curDocToBeFiledIdxInList >= _scanDocHandler.GetCountOfUnfiledDocs()))
                return;
            DisplayScannedDocImage(_curDocDisplay_pageNum + 1);
        }

        private void btnViewDocTypes_Click(object sender, RoutedEventArgs e)
        {
            DocTypeView dtv = new DocTypeView(_scanDocHandler, _docTypesMatcher);
            dtv.ShowDocTypeList((_curSelectedDocType == null) ? "" : _curSelectedDocType.docTypeName, _curDocScanDocInfo, _curDocScanPages);
            dtv.ShowDialog();
            ShowDocToBeFiled(_curDocToBeFiledIdxInList);
            // Use a background worker to repopulate the list of images
            if (!_bwThreadForImagesPopup.IsBusy)
                _bwThreadForImagesPopup.RunWorkerAsync();
        }

        private void btnDayUp_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = GetDateFromRollers();
            SetDateRollers(dt.Year, dt.Month, dt.Day + 1, dateRollerChange.drc_dayPlus);
        }

        private void btnMonthUp_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = GetDateFromRollers();
            SetDateRollers(dt.Year, dt.Month + 1, dt.Day, dateRollerChange.drc_monthPlus);
        }

        private void btnYearUp_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = GetDateFromRollers();
            SetDateRollers(dt.Year + 1, dt.Month, dt.Day, dateRollerChange.drc_yearPlus);
        }

        private void btnDayDown_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = GetDateFromRollers();
            SetDateRollers(dt.Year, dt.Month, dt.Day - 1, dateRollerChange.drc_dayMinus);
        }

        private void btnMonthDown_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = GetDateFromRollers();
            SetDateRollers(dt.Year, dt.Month - 1, dt.Day, dateRollerChange.drc_monthMinus);
        }

        private void btnYearDown_Click(object sender, RoutedEventArgs e)
        {
            DateTime dt = GetDateFromRollers();
            SetDateRollers(dt.Year - 1, dt.Month, dt.Day, dateRollerChange.drc_yearMinus);
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
            if (_openFullScreen)
                this.WindowState = System.Windows.WindowState.Maximized;
        }

        private void btnUseScanDate_Click(object sender, RoutedEventArgs e)
        {
            if (_curDocScanDocInfo != null)
                SetDateRollers(_curDocScanDocInfo.createDate.Year, _curDocScanDocInfo.createDate.Month, _curDocScanDocInfo.createDate.Day, dateRollerChange.none);
        }

        private void btnLastUsedDate_Click(object sender, RoutedEventArgs e)
        {
            if (_lastDocFiledAsDateTime != null)
                SetDateRollers(_lastDocFiledAsDateTime.Year, _lastDocFiledAsDateTime.Month, _lastDocFiledAsDateTime.Day, dateRollerChange.none);
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
            txtDestFilePrefix.Text = (_curSelectedDocType == null) ? "" : DocType.GetFileNamePrefix(_curSelectedDocType.docTypeName);
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
            string filePath = lblMoveToName.Text.ToString();
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
            for (int timeIdx = 0; timeIdx < 30; timeIdx++)
                tss.Add(new TimeSpan(7 + timeIdx / 2, (timeIdx % 2) * 30, 0));
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
            ShowDocToBeFiled(_curDocToBeFiledIdxInList);
        }

        private void btnShowMoveToFolder_Click(object sender, RoutedEventArgs e)
        {
            // Check what path to use
            bool pathContainsMacros = false;
            string destPath = GetFilingPath(ref pathContainsMacros);
            if (destPath == "")
                destPath = Properties.Settings.Default.BasePathForFilingFolderSelection.Trim();

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
                _overrideFolderForFiling = folderName;
                btnMoveToUndo.IsEnabled = true;
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

            // Check file to edit is present
            string fileToEdit = _curDocScanDocInfo.GetOrigFileNameWin();
            if (!File.Exists(fileToEdit))   
            {
                fileToEdit = ScanDocHandler.GetArchiveFileName(_curDocScanDocInfo.uniqName);
                if (!File.Exists(fileToEdit))
                {
                    MessageDialog.Show("Neither original not archive file can be found", "", "OK", "", null, this);
                    return;
                }
            }

            PdfEditorWindow pew = new PdfEditorWindow();
            pew.OpenEmbeddedPdfEditor(fileToEdit, HandlePdfEditSaveComplete, Properties.Settings.Default.PdfEditorOutFolder);
            pew.ShowDialog();
        }

        private void HandlePdfEditSaveComplete(string originalFileName, List<string> savedFileNames)
        {
            // Changed handling of saving of PDF - now saves PDF to archive location - not to original folder (on scanning machine)
            // And the processing of the file into the database is now handled explicitly rather than waiting for the monitoring 
            // machine to do it

            // Delete the original
            bool deletedOk = _scanDocHandler.DeleteFile(_curDocScanDocInfo.uniqName, _curFiledDocInfo, _curDocScanDocInfo.GetOrigFileNameWin(), true);
            if (!deletedOk)
            {
                lblStatusBarProcStatus.Content = "Last filing: Failed to remove original";
                logger.Error("PDFSaveComplete failed to delete {0}", _curDocScanDocInfo.GetOrigFileNameWin());
                lblStatusBarProcStatus.Foreground = Brushes.Red;
            }
            else
            {
                lblStatusBarProcStatus.Content = "Last Filing: Ok";
                lblStatusBarProcStatus.Foreground = Brushes.Black;
            }

            // Process the output files in a worker thread
            if (!_scanDocHandler.backgroundProcessPdfFiles(savedFileNames))
            {
                logger.Error("PDFSaveComplete failed process output files as background worker busy");
                lblStatusBarProcStatus.Content = "Last filing: File processor busy";
                lblStatusBarProcStatus.Foreground = Brushes.Red;
            }

            // Goto a file if there is one
            ShowDocToBeFiled(_curDocToBeFiledIdxInList);
        }

        private void btnAuditTrail_Click(object sender, RoutedEventArgs e)
        {
            AuditView av = new AuditView(_scanDocHandler, _docTypesMatcher, _windowClosingCB);
            av.ShowDialog();
        }

        private void QuickNewType_Click(object sender, RoutedEventArgs e)
        {
            QuickNewDocType qndt = new QuickNewDocType(_scanDocHandler, _docTypesMatcher, _curDocScanDocInfo == null ? "" : _curDocScanDocInfo.uniqName, _curDocDisplay_pageNum);
            bool? rslt = qndt.ShowDialog();
            if (rslt != null && rslt == true)
                ShowDocumentTypeAndDate(qndt._newDocTypeName);
        }

        private void btnDocTypeSel_Click(object sender, RoutedEventArgs e)
        {
            // Clear menu
            btnDocTypeSelContextMenu.Items.Clear();

            //if (USE_QUICK_DOC_MENU)
            //{
            //    // Add a quick type menu item
            //    MenuItem quickTypeMenuItem = new MenuItem();
            //    quickTypeMenuItem.Header = "<Quick New Type>";
            //    quickTypeMenuItem.Click += QuickNewType_Click;
            //    btnDocTypeSelContextMenu.Items.Add(quickTypeMenuItem);
            //}

            // Reload menu
            const int MAX_MENU_LEVELS = 6;
            List<string> docTypeStrings = new List<string>();
            List<DocType> docTypeList = _docTypesMatcher.ListDocTypes();
            foreach (DocType dt in docTypeList)
            {
                if (!dt.isEnabled)
                    continue;
                docTypeStrings.Add(dt.docTypeName);
            }
            docTypeStrings.Sort();
            string[] prevMenuStrs = null;
            MenuItem[] prevMenuItems = new MenuItem[MAX_MENU_LEVELS];
            foreach (string docTypeString in docTypeStrings)
            {
                string[] elemsS = docTypeString.Split(new string[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                if ((elemsS.Length <= 0) || (elemsS.Length > MAX_MENU_LEVELS))
                    continue;
                for (int i = 0; i < elemsS.Length; i++)
                    elemsS[i] = elemsS[i].Trim();
                int reqMenuLev = -1;
                for (int menuLev = 0; menuLev < elemsS.Length; menuLev++)
                {
                    if ((prevMenuStrs == null) || (menuLev >= prevMenuStrs.Length))
                    {
                        reqMenuLev = menuLev;
                        break;
                    }
                    if (prevMenuStrs[menuLev] != elemsS[menuLev])
                    {
                        reqMenuLev = menuLev;
                        break;
                    }
                }
                // Check if identical to previous type - discard if so
                if (reqMenuLev == -1)
                    continue;
                // Create new menu items
                for (int createLev = reqMenuLev; createLev < elemsS.Length; createLev++)
                {
                    MenuItem newMenuItem = new MenuItem();
                    newMenuItem.Header = elemsS[createLev];
                    if (createLev == elemsS.Length - 1)
                    {
                        newMenuItem.Tag = docTypeString;
                        newMenuItem.Click += DocTypeSubMenuItem_Click;
                    }
                    // Add the menu item to the appropriate level
                    if (createLev == 0)
                        btnDocTypeSelContextMenu.Items.Add(newMenuItem);
                    else
                        prevMenuItems[createLev - 1].Items.Add(newMenuItem);
                    prevMenuItems[createLev] = newMenuItem;
                }
                // Update lists
                prevMenuStrs = elemsS;
            }
            // Show menu
            if (!btnDocTypeSel.ContextMenu.IsOpen)
                btnDocTypeSel.ContextMenu.IsOpen = true;
        }

        private void DocTypeSubMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MenuItem menuItem = sender as MenuItem;
            // Ignore if not tip of menu hierarchy
            if (menuItem.HasItems)
                return;
            ContextMenu contextMenu = menuItem.CommandParameter as ContextMenu;
            btnDocTypeSel.ContextMenu.IsOpen = false;
            object tag = menuItem.Tag;
            if (tag.GetType() == typeof(string))
                ShowDocumentTypeAndDate((string)tag);
        }

        private void docImgCtxtOriginal_Click(object sender, RoutedEventArgs e)
        {
            if (_curDocScanDocInfo != null)
            {
                ScanDocHandler.ShowFileInExplorer(_curDocScanDocInfo.GetOrigFileNameWin());
            }

        }

        private void docImgCtxtArchive_Click(object sender, RoutedEventArgs e)
        {
            if (_curDocScanDocInfo != null)
            {
                string imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, _curDocScanDocInfo.uniqName, 1, false);
                try
                {
                    ScanDocHandler.ShowFileInExplorer(imgFileName.Replace("/", @"\"));
                }
                finally
                {

                }
            }
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsView sv = new SettingsView(_windowClosingCB);
            string oldViewOrder = Properties.Settings.Default.UnfiledDocListOrder;
            sv.ShowDialog();
            if (Properties.Settings.Default.UnfiledDocListOrder != oldViewOrder)
                ShowDocToBeFiled(_curDocToBeFiledIdxInList);
        }

        private void btnQuickDocType_Click(object sender, RoutedEventArgs e)
        {
            txtDocTypeName.IsEnabled = true;

        }

        private void txtDocTypeName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isQuickDocTypeMode() && !txtDestFilePrefix.IsEnabled)
            {
                txtDestFilePrefix.Text = DocType.GetFileNamePrefix(txtDocTypeName.Text);
            }
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
            int nextDocToShow = _curDocToBeFiledIdxInList;
            MessageDialog.MsgDlgRslt rslt = MessageDialog.Show("Delete " + _curDocScanDocInfo.uniqName + " ?\n" + "Are you sure?", "Yes", "No", "Cancel", btnDeleteDoc, this);
            if (rslt == MessageDialog.MsgDlgRslt.RSLT_YES)
            {
                // Delete file
                bool deletedOk = _scanDocHandler.DeleteFile(_curDocScanDocInfo.uniqName, _curFiledDocInfo, _curDocScanDocInfo.GetOrigFileNameWin(), false);
                if (!deletedOk)
                {
                    if (!File.Exists(_curDocScanDocInfo.GetOrigFileNameWin()))
                    {
                        rslt = MessageDialog.Show("Original File " + _curDocScanDocInfo.uniqName + " not found\n" + "Remove file from to-file-list?", "Yes", "No", "Cancel", btnDeleteDoc, this);
                        if (rslt == MessageDialog.MsgDlgRslt.RSLT_YES)
                        {
                            lblStatusBarProcStatus.Content = "Last File: Removed from list";
                            lblStatusBarProcStatus.Foreground = Brushes.Red;

                            // Update the doc list cache
                            _scanDocHandler.RemoveDocFromUnfiledCache(_curDocScanDocInfo.uniqName, "");
                            nextDocToShow = _curDocToBeFiledIdxInList - 1;
                        }
                    }
                }
                else
                {
                    lblStatusBarProcStatus.Content = "Last File: Deleted";
                    lblStatusBarProcStatus.Foreground = Brushes.Black;

                    // Update the doc list cache
                    _scanDocHandler.RemoveDocFromUnfiledCache(_curDocScanDocInfo.uniqName, "");
                    nextDocToShow = _curDocToBeFiledIdxInList - 1;
                }

                // Goto a file if there is one
                ShowDocToBeFiled(nextDocToShow);
            }
        }

        #endregion

        #region Handle processing of the document

        private bool isQuickDocTypeMode()
        {
            bool quickDocTypeMode = false;
            if (_curSelectedDocType == null)
            {
                quickDocTypeMode = true;
            }
            else
            {
                if ((txtDocTypeName.IsEnabled) && (txtDocTypeName.Text != _curSelectedDocType.docTypeName))
                    quickDocTypeMode = true;
            }

            return quickDocTypeMode;
        }

        private void btnProcessDoc_Click(object sender, RoutedEventArgs e)
        {
            if (_curDocScanDocInfo == null)
                return;

            // Check not already busy filing a doc
            if (_scanDocHandler.IsBusy())
                return;

            // Check a doc type has been selected
            bool quickDocTypeMode = isQuickDocTypeMode();
            if (txtDocTypeName.Text.Trim() == "")
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

            // Warn if date outside ranges
            if (_lastDocFiledAsDateTime > DateTime.Now)
            {
                MessageBoxButton btnMessageBox = MessageBoxButton.YesNoCancel;
                MessageBoxImage icnMessageBox = MessageBoxImage.Question;
                MessageBoxResult rsltMessageBox = MessageBox.Show("Date is in the future - is this Correct?", "Date Question", btnMessageBox, icnMessageBox);
                if ((rsltMessageBox == MessageBoxResult.Cancel) || (rsltMessageBox == MessageBoxResult.No))
                    return;
            }

            // Warn if more than X years old
            const int MAX_YEARS_OLD = 10;
            if (_lastDocFiledAsDateTime < (DateTime.Now - new TimeSpan(365 * MAX_YEARS_OLD, 0, 0, 0)))
            {
                MessageBoxButton btnMessageBox = MessageBoxButton.YesNoCancel;
                MessageBoxImage icnMessageBox = MessageBoxImage.Question;
                MessageBoxResult rsltMessageBox = MessageBox.Show("Date is several years ago - is this Correct?", "Date Question", btnMessageBox, icnMessageBox);
                if ((rsltMessageBox == MessageBoxResult.Cancel) || (rsltMessageBox == MessageBoxResult.No))
                    return;
            }

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

            // If in quick doc type mode check that the doc type is valid
            string fileAsDocTypeName = "";
            if (quickDocTypeMode)
            {
                fileAsDocTypeName = txtDocTypeName.Text.Trim();
                // Ensure doc type contains a dash
                if (!fileAsDocTypeName.Contains("-"))
                {
                    MessageBoxButton btnMessageBox = MessageBoxButton.OK;
                    MessageBoxImage icnMessageBox = MessageBoxImage.Information;
                    MessageBoxResult rsltMessageBox = MessageBox.Show("Document Type name should be in a 'MasterType - SubType' format", "Quick DocType Problem", btnMessageBox, icnMessageBox);
                    return;
                }
                // Ensure the new name is unique
                DocType testDocType = _docTypesMatcher.GetDocType(fileAsDocTypeName);
                if (testDocType != null)
                {
                    MessageBoxButton btnMessageBox = MessageBoxButton.OK;
                    MessageBoxImage icnMessageBox = MessageBoxImage.Information;
                    MessageBoxResult rsltMessageBox = MessageBox.Show("There is already a Document Type with this name", "Quick DocType Problem", btnMessageBox, icnMessageBox);
                    return;
                }

                // Create the new doctype
                DocType docType = new DocType();
                docType.docTypeName = fileAsDocTypeName;
                docType.isEnabled = true;
                docType.matchExpression = "";
                docType.dateExpression = "";
                docType.moveFileToPath = _docTypesMatcher.ComputeMinimalPath(System.IO.Path.GetDirectoryName(fullPathAndFileNameForFilingTo));
                string defaultRenameToContents = Properties.Settings.Default.DefaultRenameTo;
                docType.renameFileTo = defaultRenameToContents;
                _docTypesMatcher.AddOrUpdateDocTypeRecInDb(docType);
                _scanDocHandler.DocTypeAddedOrChanged(docType.docTypeName);
            }
            else
            {
                fileAsDocTypeName = _curSelectedDocType.docTypeName;
            }

            // Set the filing information
            fdi.SetDocFilingInfo(fileAsDocTypeName, fullPathAndFileNameForFilingTo, GetDateFromRollers(), txtMoneySum.Text, followUpStr,
                        addToCalendarStr, txtEventName.Text, selectedDateTime, eventDuration, txtEventDesc.Text, txtEventLocn.Text, flagAttachFile);

            // Start filing the document
            lblStatusBarProcStatus.Content = "Processing ...";
            lblStatusBarProcStatus.Foreground = Brushes.Black;
            _scanDocHandler.StartProcessFilingOfDoc(SetStatusText, FilingCompleteCallback, _curDocScanDocInfo, fdi, txtEmailPassword.Password, out rsltText);

            // Save password to db
            _scanDocHandler.SetEmailPassword(txtEmailPassword.Password);
        }

        #endregion

        #region Image events

        private void imageDocToFile_MouseMove(object sender, MouseEventArgs e)
        {
            // Check if window is in foreground - remove popup if it isn't
            if (!IsActive)
            {
                imageDocToFileToolTip.IsOpen = false;
                return;
            }

            // Show tool tip
            Point curMousePoint = e.GetPosition(imageDocToFile);
            Point docCoords = ConvertImagePointToDocPoint(imageDocToFile, curMousePoint.X, curMousePoint.Y);
            DocRectangle docRect = new DocRectangle(docCoords.X, docCoords.Y, 0, 0);
            bool bToolTipSet = false;
            if ((_curDocScanDocInfo != null) && (_curDocScanPages != null))
                if ((_curDocDisplay_pageNum > 0) && (_curDocDisplay_pageNum <= _curDocScanPages.scanPagesText.Count))
                {
                    if (!imageDocToFileToolTip.IsOpen)
                    {
                        imageDocToFileToolTip.IsOpen = true;
                        imageDocToFileToolTip.IsHitTestVisible = false;
                    }
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

        private void imageDocToFile_LostFocus(object sender, RoutedEventArgs e)
        {
            imageDocToFileToolTip.IsOpen = false;
        }

        private void backgroundGrid_MouseEnter(object sender, MouseEventArgs e)
        {
            imageDocToFileToolTip.IsOpen = false;
        }

        private void MetroWindow_Deactivated(object sender, EventArgs e)
        {
            imageDocToFileToolTip.IsOpen = false;
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
                        SetDateRollers(extractedDates[0].dateTime.Year, extractedDates[0].dateTime.Month, extractedDates[0].dateTime.Day, dateRollerChange.none);
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
                    string noCurrencyStr = extractedText;
                    if ((currencyPos >= 0) && (extractedText.Length > currencyPos + 1))
                    {
                        noCurrencyStr = extractedText.Substring(currencyPos + 1);
                        int whitespacePos = noCurrencyStr.IndexOf(' ');
                        if (whitespacePos > 0)
                            noCurrencyStr = noCurrencyStr.Substring(0, whitespacePos);
                    }
                    Match match = Regex.Match(noCurrencyStr, @"((?:^\d{1,3}(?:\.?\d{3})*(?:,\d{2})?$))|((?:^\d{1,3}(?:,?\d{3})*(?:\.\d{2})?$)((\d+)?(\.\d{1,2})?))");
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
                        string numberText = (currencyPos >= 0 ? extractedText.Substring(currencyPos, currencyLen) : "") + foundStr;
                        txtMoneySum.Text = txtMoneySum.Text + (txtMoneySum.Text.Trim() == "" ? "" : " ") + numberText;
                    }
                    else if (currencyPos >= 0)
                    {
                        txtMoneySum.Text = extractedText.Substring(currencyPos, currencyLen) + txtMoneySum.Text;
                    }
                }
            }

            // Back to default touch activity
            _touchFromPageText = TouchFromPageText.TOUCH_SUFFIX;
        }

        #endregion

        #region Thumbnail picker

        private void AddImages_DoWork(object sender, DoWorkEventArgs e)
        {
            this.Dispatcher.BeginInvoke((Action)delegate()
            {
                _thumbnailsOfDocTypes.Clear();
            });
            Thread.Sleep(500);
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
            if (_curDocScanDocInfo != null)
            {
                string docFormat = ((_curSelectedDocType == null) || isQuickDocTypeMode()) ? "" : _curSelectedDocType.renameFileTo;
                string dfn = ScanDocHandler.FormatFileNameFromMacros(_curDocScanDocInfo.GetOrigFileNameWin(), docFormat, GetDateFromRollers(), txtDestFilePrefix.Text, txtDestFileSuffix.Text, txtDocTypeName.Text.Trim());
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

        private void SetDateRollers(int year, int mon, int day, dateRollerChange drc)
        {
            if (year > DateTime.MaxValue.Year)
                year = DateTime.MaxValue.Year;
            if (year < DateTime.MinValue.Year)
                year = DateTime.MinValue.Year;
            if (mon > 12)
            {
                mon = 12;
                if (drc == dateRollerChange.drc_monthPlus)
                    mon = 1;
            }
            if (mon < 1)
            {
                mon = 1;
                if (drc == dateRollerChange.drc_monthMinus)
                    mon = 1;
            }
            if (day > DateTime.DaysInMonth(year, mon))
            {
                day = DateTime.DaysInMonth(year, mon);
                if (drc == dateRollerChange.drc_dayPlus)
                    day = 1;
            }
            if (day < 1)
            {
                day = 1;
                if (drc == dateRollerChange.drc_dayMinus)
                    day = DateTime.DaysInMonth(year, mon);
            }
            DateTime dt = new DateTime(year, mon, day);
            if (lblDayVal.Text != day.ToString())
                lblDayVal.Text = day.ToString();
            if (lblMonthVal.Text != dt.ToString("MMMM"))
                lblMonthVal.Text = dt.ToString("MMMM");
            if (lblYearVal.Text != dt.Year.ToString())
                lblYearVal.Text = dt.Year.ToString();

            // Show dest file name (which can change based on date)
            DisplayDestFileName();

            // Update filing path if it hasn't been overridden
            if (!btnMoveToUndo.IsEnabled)
                ShowFilingPath();
        }

        public static Point ConvertImagePointToDocPoint(Image img, double x, double y)
        {
            double tlx = 100 * x / img.ActualWidth;
            double tly = 100 * y / img.ActualHeight;
            return new Point(tlx, tly);
        }

        private string GetFilingPath(ref bool pathContainsMacros)
        {
            pathContainsMacros = false;

            // Check whether to use overridden path or not
            string destPath = "";
            if (_overrideFolderForFiling.Trim() != "")
            {
                destPath = _overrideFolderForFiling.Trim();
            }
            else
            {
                if (_curSelectedDocType != null)
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
            ShowDocToBeFiled(_curDocToBeFiledIdxInList - 1);
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

        private void lblStatusBarFileNo_MouseUp(object sender, MouseButtonEventArgs e)
        {
            GoToPage gtp = new GoToPage(_curDocToBeFiledIdxInList+1, lblStatusBarFileNo, this);
            gtp.ShowDialog();
            if (gtp.dlgResult)
            {
                ShowDocToBeFiled(gtp.pageNum-1);
            }
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            _windowClosingCB();
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
