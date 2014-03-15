using MahApps.Metro.Controls;
using Microsoft.WindowsAPICodePack.Dialogs;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    /// Interaction logic for DocTypeView.xaml
    /// </summary>
    public partial class DocTypeView : MetroWindow
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        const int MAX_NUM_DOCS_TO_ADD_TO_LIST = 200;

        private ScanDocHandler _scanDocHandler;
        private DocTypesMatcher _docTypesMatcher;
        private DocType _selectedDocType;
        BackgroundWorker _bwThread;
        ObservableCollection<DocType> _docTypeColl = new ObservableCollection<DocType>();
        ObservableCollection<DocCompareRslt> _docCompareRslts = new ObservableCollection<DocCompareRslt>();
        private bool bInTextChangedHandler = false;
        private string _curDocDisplay_uniqName = "";
        private int _curDocDisplay_pageNum = 1;
        private ScanPages _curDocDisplay_scanPages = null;
        private DocTypeMatchResult _curDocDisplay_lastMatchResult = null;
        private ScanDocInfo _curUnfiledScanDocInfo = null;
        private ScanPages _curUnfiledScanDocPages = null;
        private string _curDocTypeThumbnail = "";
        private bool bInSetupDocTypeForm = false;
        // Cache last parse result
        private Dictionary<string, ParseResultCacheElem> _parseResultCache = new Dictionary<string,ParseResultCacheElem>();
        private string _lastSelectedFolder = "";
        private LocationRectangleHandler locRectHandler;

        public DocTypeView(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher)
        {
            InitializeComponent();
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;

            // List view for comparisons
            listMatchResults.ItemsSource = _docCompareRslts;
            listMatchResults.Items.SortDescriptions.Add(new SortDescription("matchStatus", ListSortDirection.Ascending));

            // Location rectangle handler
            locRectHandler = new LocationRectangleHandler(exampleFileImage, docOverlayCanvas, tooltipCallback_MouseMove, tooptipCallback_MouseLeave, docRectChangesComplete);
            locRectHandler.SelectionEnable(false);

            // Matcher thread
            _bwThread = new BackgroundWorker();
            _bwThread.WorkerSupportsCancellation = true;
            _bwThread.WorkerReportsProgress = true;
            _bwThread.DoWork += new DoWorkEventHandler(FindMatchingDocs_DoWork);
            _bwThread.ProgressChanged += new ProgressChangedEventHandler(FindMatchingDocs_ProgressChanged);
            _bwThread.RunWorkerCompleted += new RunWorkerCompletedEventHandler(FindMatchingDocs_RunWorkerCompleted);
        }

        #region DocTypeListView Management

        public ObservableCollection<DocType> DocTypeColl
        {
            get { return _docTypeColl; }
        }

        public void ShowDocTypeList(string selDocTypeName, ScanDocInfo unfiledScanDocInfo, ScanPages unfiledScanDocPages)
        {
            _curUnfiledScanDocInfo = unfiledScanDocInfo;
            _curUnfiledScanDocPages = unfiledScanDocPages;
            DocType selDocType = null;
            List<DocType> docTypes = _docTypesMatcher.ListDocTypes();
            var docTypesSorted = from docType in docTypes
                           orderby !docType.isEnabled, docType.docTypeName
                           select docType;
            _docTypeColl.Clear();
            foreach (DocType dt in docTypesSorted)
            {
                _docTypeColl.Add(dt);
                if (dt.docTypeName == selDocTypeName)
                    selDocType = dt;
            }
            docTypeListView.ItemsSource = _docTypeColl;
            if (selDocType != null)
                docTypeListView.SelectedItem = selDocType;

            // Display example doc
            if ((_curUnfiledScanDocInfo != null) && (_curUnfiledScanDocPages != null))
            {
                DisplayExampleDoc(_curUnfiledScanDocInfo.uniqName, 1, _curUnfiledScanDocPages);
                btnShowDocToBeFiled.IsEnabled = true;
            }
            else
            {
                btnShowDocToBeFiled.IsEnabled = false;
            }
        }

        private void docTypeListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // If changes are pending then restore the currently selected doc type (which can be null if nothing is selected)
            if (AreDocTypeChangesPendingSaveOrCancel())
            {
                docTypeListView.SelectedItem = _selectedDocType;
                return;
            }

            // Get the corresponding doc type to display
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                DocType selDocType = e.AddedItems[0] as DocType;
                _selectedDocType = selDocType;
                if (selDocType != null)
                {
                    SetupDocTypeForm(selDocType);
                }
            }
        }

        #endregion

        #region Test Against Already Filed Docs

        private void btnTestMatch_Click(object sender, RoutedEventArgs e)
        {
            if (_bwThread.IsBusy)
            {
                FindMatchingDocs_Stop();
            }
            else
            {
                if ((!(bool)chkEnabledDocType.IsChecked) && chkEnabledDocType.IsEnabled)
                {
                    MessageBoxButton btnMessageBox = MessageBoxButton.YesNoCancel;
                    MessageBoxImage icnMessageBox = MessageBoxImage.Question;
                    MessageBoxResult rsltMessageBox = MessageBox.Show("Document Type is DISABLED. Do you want to ENABLE it?", "DocType Disabled", btnMessageBox, icnMessageBox);
                    if (rsltMessageBox == MessageBoxResult.Cancel)
                        return;
                    if (rsltMessageBox == MessageBoxResult.Yes)
                        chkEnabledDocType.IsChecked = true;
                }
                FindMatchingDocs_Start();
            }
        }

        private void FindMatchingDocs_Start()
        {
            if (!_bwThread.IsBusy)
            {
                DocType chkDocType = GetDocTypeFromForm(new DocType());
                chkDocType.isEnabled = true;
                _bwThread.RunWorkerAsync(chkDocType);
                btnTestMatch.Content = "Stop Finding";
                SetDocMatchStatusText("Working...");
                _docCompareRslts.Clear();
            }
        }

        private DocType GetDocTypeFromForm(DocType docType)
        {
            docType.docTypeName = txtDocTypeName.Text;
            docType.isEnabled = (bool)chkEnabledDocType.IsChecked;
            docType.matchExpression = GetTextFromRichTextBox(txtMatchExpression);
            docType.dateExpression = GetTextFromRichTextBox(txtDateLocations);
            docType.moveFileToPath = txtMoveTo.Text;
            string defaultRenameToContents = Properties.Settings.Default.DefaultRenameTo;
            docType.renameFileTo = txtRenameTo.Text == defaultRenameToContents ? "" : txtRenameTo.Text;
            docType.thumbnailForDocType = _curDocTypeThumbnail;
            return docType;
        }

        private void SetDocMatchStatusText(string inStr, bool append = false)
        {
            List<Brush> colrBrushes = new List<Brush> 
            {
                Brushes.Black, Brushes.Green, Brushes.Red, Brushes.Orange, Brushes.Purple, Brushes.Peru, Brushes.Purple
            };

            FlowDocument fd = new FlowDocument();
            if (append)
                fd = rtbDocMatchStatus.Document;
            Paragraph para = new Paragraph();
            para.LineHeight = 10;
            para.Margin = new Thickness(0);
            Run txtRun = new Run();
            string[] strElems = Regex.Split(inStr, @"(\~\d)|(\r\n|\r|\n)");
            foreach (string str in strElems)
            {
                if (str.Length <= 0)
                    continue;
                if (str[0] == '~')
                {
                    if (str.Length > 1)
                    {
                        int colrIdx = Convert.ToInt32(str.Substring(1,1));
                        if (colrIdx >= 0 && colrIdx < colrBrushes.Count)
                            txtRun.Foreground = colrBrushes[colrIdx];
                    }
                }
                else if (str[0] == '\r' || str[0] == '\n')
                {
                    fd.Blocks.Add(para);
                    para = new Paragraph();
                    para.LineHeight = 10;
                    para.Margin = new Thickness(0);
                }
                else
                {
                    txtRun.Text = str;
                    para.Inlines.Add(txtRun);
                    txtRun = new Run();
                }
            }
            if (para.Inlines.Count > 0)
                fd.Blocks.Add(para);
            rtbDocMatchStatus.Document = fd;
        }

        private class MatchDocsResult
        {
            public int totalFilesProcessed = 0;
            public int totalMatchesFound = 0;
            public int totalMatchesButShouldnt = 0;
            public int totalDoesntMatchButShould = 0;
            public bool bNotAllResultsShow = false;
            public bool bCancelled = false;
            public Exception error = null;
        }

        private void FindMatchingDocs_DoWork(object sender, DoWorkEventArgs e)
        {
            MatchDocsResult mdResult = new MatchDocsResult();
            e.Result = mdResult;
            BackgroundWorker worker = sender as BackgroundWorker;
            List<ScanDocInfo> sdiList = _scanDocHandler.GetListOfScanDocs();
            int docIdx = 0;
            int docsFound = 0;
            mdResult.bNotAllResultsShow = false;
            foreach (ScanDocInfo sdi in sdiList)
            {
                if ((worker.CancellationPending == true))
                {
                    e.Cancel = true;
                    break;
                }

                DocType docTypeToMatch = (DocType)e.Argument;
                DocCompareRslt rslt = CheckIfDocMatches(sdi, docTypeToMatch);
                if (rslt.bMatches)
                    mdResult.totalMatchesFound++;
                if (rslt.bDoesntMatchButShould)
                    mdResult.totalDoesntMatchButShould++;
                if (rslt.bMatchesButShouldnt)
                    mdResult.totalMatchesButShouldnt++;
                mdResult.totalFilesProcessed++;
                if (rslt.bMatches || rslt.bDoesntMatchButShould)
                {
                    // Check for a limit to avoid swamping list
                    docsFound++;
                    if (docsFound < MAX_NUM_DOCS_TO_ADD_TO_LIST)
                    {
                        this.Dispatcher.BeginInvoke((Action)delegate()
                        {
                            _docCompareRslts.Add(rslt);
                        });
                    }
                    else
                    {
                        mdResult.bNotAllResultsShow = true;
                    }
                }

                docIdx++;
                worker.ReportProgress((int)(docIdx * 100 / sdiList.Count));
                if (((docIdx % 10) == 0) || (docIdx == sdiList.Count - 1))
                {
                    string rsltStr = DocMatchFormatResultStr(mdResult);
                    this.Dispatcher.BeginInvoke((Action)delegate()
                    {
                        SetDocMatchStatusText(rsltStr);
                    });
                }
            }
            e.Result = mdResult;
        }

        private DocCompareRslt CheckIfDocMatches(ScanDocInfo sdi, DocType docTypeToMatch)
        {
            DocCompareRslt compRslt = new DocCompareRslt();
            ScanPages scanPages = _scanDocHandler.GetScanPages(sdi.uniqName);
            // See if doc has been filed - result maybe null
            FiledDocInfo fdi = _scanDocHandler.GetFiledDocInfo(sdi.uniqName);

            // Check for a match
            DocTypeMatchResult matchResult = _docTypesMatcher.CheckIfDocMatches(scanPages, docTypeToMatch, false, null);
            if (matchResult.matchCertaintyPercent == 100)
            {
                compRslt.bMatches = true;
                compRslt.bMatchesButShouldnt = (fdi != null) && (fdi.docTypeFiled != docTypeToMatch.docTypeName);
                compRslt.uniqName = sdi.uniqName;
                compRslt.docTypeFiled = (fdi == null) ? "" : fdi.docTypeFiled;
                compRslt.matchStatus = (fdi == null) ? "NOT-FILED" : (compRslt.bMatchesButShouldnt ? "MATCH-BUT-SHOULDN'T" : "OK");
                compRslt.scanPages = scanPages;
            }
            else
            {
                compRslt.bDoesntMatchButShould = (fdi != null) && (fdi.docTypeFiled == docTypeToMatch.docTypeName);
                if (compRslt.bDoesntMatchButShould)
                {
                    compRslt.uniqName = sdi.uniqName;
                    compRslt.docTypeFiled = (fdi == null) ? "" : fdi.docTypeFiled;
                    compRslt.matchStatus = "SHOULD-BUT-DOESN'T";
                    compRslt.scanPages = scanPages;
                }
            }
            compRslt.matchFactorStr = matchResult.matchCertaintyPercent.ToString() + " + " + matchResult.matchFactor.ToString() + "%";
            return compRslt;
        }

        private void CheckDisplayedDocForMatchAndShowResult()
        {
            if (_curDocDisplay_scanPages == null)
            {
                _curDocDisplay_lastMatchResult = null;
                return;
            }
            DocType chkDocType = GetDocTypeFromForm(new DocType());
            chkDocType.isEnabled = true;
            List<DocMatchingTextLoc> matchingTextLocs = new List<DocMatchingTextLoc>();
            DocTypeMatchResult matchRslt = _docTypesMatcher.CheckIfDocMatches(_curDocDisplay_scanPages, chkDocType, true, matchingTextLocs);
            _curDocDisplay_lastMatchResult = matchRslt;
            DisplayMatchResultForDoc(matchRslt, matchingTextLocs);
        }

        private void DisplayMatchResultForDoc(DocTypeMatchResult matchRslt, List<DocMatchingTextLoc> matchingTextLocs)
        {
            List<Brush> exprColrBrushes = new List<Brush> 
            {
                Brushes.DarkMagenta, Brushes.Green, Brushes.Red, Brushes.Orange, Brushes.Purple, Brushes.Peru, Brushes.Purple
            };

            if (matchRslt.docDate != DateTime.MinValue)
                txtDateResult.Text = matchRslt.docDate.ToLongDateString();
            else
                txtDateResult.Text = "";

            // Display match status
            string matchFactorStr = String.Format("{0}", (int)matchRslt.matchFactor);
            if (matchRslt.matchCertaintyPercent == 100)
            {
                txtCheckResult.Text = "MATCHES (" + matchFactorStr + "%)";
                txtCheckResult.Foreground = Brushes.White;
                txtCheckResult.Background = Brushes.Green;
            }
            else
            {
                txtCheckResult.Text = "FAILED (" + matchFactorStr + "%)";
                txtCheckResult.Foreground = Brushes.White;
                txtCheckResult.Background = Brushes.Red;
            }

            // Display matching text locations
            locRectHandler.ClearTextMatchRect("txtMatch");
            foreach (DocMatchingTextLoc txtLoc in matchingTextLocs)
            {
                if (txtLoc.pageIdx+1 == _curDocDisplay_pageNum)
                {
                    DocRectangle inRect = _curDocDisplay_scanPages.scanPagesText[txtLoc.pageIdx][txtLoc.elemIdx].bounds;
                    Brush colrBrush = exprColrBrushes[txtLoc.exprIdx % exprColrBrushes.Count];
                    DocRectangle computedLocation = new DocRectangle(inRect.X, inRect.Y, inRect.Width, inRect.Height);
                    double wid = computedLocation.Width;
                    double inx = computedLocation.X;
                    computedLocation.X = inx + txtLoc.posInText * wid / txtLoc.foundInTxtLen;
                    computedLocation.Width = txtLoc.matchLen * wid / txtLoc.foundInTxtLen;
                    locRectHandler.DrawTextMatchRect(_curDocDisplay_scanPages.scanPagesText[txtLoc.pageIdx][txtLoc.elemIdx].bounds, colrBrush, "txtMatch");
                }
            }
        }

        private string DocMatchFormatResultStr(MatchDocsResult mdResult)
        {
            return "Files: " + mdResult.totalFilesProcessed.ToString() + (mdResult.bNotAllResultsShow ? "*" : "") +  "\n" +
                                     "~1Matched: " + mdResult.totalMatchesFound.ToString() + "\n" +
                                     "~2Shouldn't: " + mdResult.totalMatchesButShouldnt.ToString() + "\n" +
                                     "~3Should: " + mdResult.totalDoesntMatchButShould.ToString();
        }

        private void FindMatchingDocs_Stop()
        {
            if (_bwThread.WorkerSupportsCancellation)
                _bwThread.CancelAsync();
        }

        private void FindMatchingDocs_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            this.progressDocMatch.Value = (int)e.ProgressPercentage;
        }

        private void FindMatchingDocs_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if ((e.Cancelled == true))
                SetDocMatchStatusText("Cancelled", true);
            else if (!(e.Error == null))
                SetDocMatchStatusText("Error: ~1" + e.Error.Message, true);
            else
                SetDocMatchStatusText("Finished", true);
            btnTestMatch.Content = "Find Matches";
        }

        #endregion

        #region Display of Document of Selected Type

        private void listMatchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Get the compare result for the selected document and display the image and results
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                DocCompareRslt docCompRslt = e.AddedItems[0] as DocCompareRslt;
                if (docCompRslt != null)
                {
                    DisplayExampleDoc(docCompRslt.uniqName, 1, docCompRslt.scanPages);
                }
            }
        }

        private void DisplayExampleDoc(string uniqName, int pageNum, ScanPages scanPages)
        {
            _curDocDisplay_scanPages = scanPages;
            string imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, uniqName, pageNum, false);
            if (!File.Exists(imgFileName))
                return;
            try
            {
                exampleFileImage.Source = new BitmapImage(new Uri("File:" + imgFileName));
                _curDocDisplay_uniqName = uniqName;
                _curDocDisplay_pageNum = pageNum;
            }
            catch (Exception excp)
            {
                logger.Error("Loading bitmap file {0} excp {1}", imgFileName, excp.Message);
                _curDocDisplay_uniqName = "";
                _curDocDisplay_pageNum = 1;
            }
        }

        private void DisplayFiledDoc_NextPage()
        {
            DisplayExampleDoc(_curDocDisplay_uniqName, _curDocDisplay_pageNum + 1, _curDocDisplay_scanPages);
        }

        private void DisplayFiledDoc_PrevPage()
        {
            DisplayExampleDoc(_curDocDisplay_uniqName, _curDocDisplay_pageNum - 1, _curDocDisplay_scanPages);
        }

        #endregion

        #region Match Expression Handling

        // Get position of cursor in the text box
        public int GetCaretPos(RichTextBox txtMatchExpression)
        {
            TextPointer curPos = txtMatchExpression.Document.ContentStart;
            TextPointer caretTextPointer = txtMatchExpression.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            int cursorPosInPlainText = 0;
            while (curPos != null)
            {
                if (curPos.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string textRun = curPos.GetTextInRun(LogicalDirection.Forward);
                    int stComp = curPos.CompareTo(caretTextPointer);
                    int enComp = curPos.GetPositionAtOffset(textRun.Length).CompareTo(caretTextPointer);
                    if (stComp <= 0 && enComp >= 0)
                    {
                        for (int i = 0; i < textRun.Length; i++)
                            if (curPos.GetPositionAtOffset(i).CompareTo(caretTextPointer) >= 0)
                                return cursorPosInPlainText + i;
                        return cursorPosInPlainText + textRun.Length;
                    }
                    cursorPosInPlainText += textRun.Length;
                }
                curPos = curPos.GetNextContextPosition(LogicalDirection.Forward);
            }
            return cursorPosInPlainText;
        }

        // Get position of cursor in the text box
        public void SetCaretPos(RichTextBox txtMatchExpression, int caretPos)
        {
            TextPointer curPos = txtMatchExpression.Document.ContentStart;
            int cursorPosInPlainText = 0;
            while (curPos != null)
            {
                if (curPos.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string textRun = curPos.GetTextInRun(LogicalDirection.Forward);
                    if (cursorPosInPlainText + textRun.Length > caretPos)
                    {
                        TextPointer newCaretPos = curPos.GetPositionAtOffset(caretPos - cursorPosInPlainText);
                        txtMatchExpression.CaretPosition = newCaretPos;
                        //txtMatchExpression.Selection.Select(newCaretPos, newCaretPos.GetNextInsertionPosition(LogicalDirection.Forward));
                        return;
                    }
                    cursorPosInPlainText += textRun.Length;
                }
                curPos = curPos.GetNextContextPosition(LogicalDirection.Forward);
            }
            txtMatchExpression.CaretPosition = txtMatchExpression.Document.ContentEnd;
        }

        private void SetTextInRichTextBox(RichTextBox rtb, string txtStr)
        {
            Paragraph para = new Paragraph(new Run(txtStr));
            // Disable text changed handler while clearing
            bInTextChangedHandler = true;
            rtb.Document.Blocks.Clear();
            // Reenable for adding so it only gets called once
            bInTextChangedHandler = false;
            rtb.Document.Blocks.Add(para);
        }

        private string GetTextFromRichTextBox(RichTextBox rtb)
        {
            string txtExpr = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text;
            txtExpr = txtExpr.Replace("\r", "");
            txtExpr = txtExpr.Replace("\n", "");
            return txtExpr;
        }

        private void txtMatchExpression_TextChanged(object sender, TextChangedEventArgs e)
        {
            HandleTextChangedForMatchAndDate(txtMatchExpression, "Match");
        }

        private void txtDateLocations_TextChanged(object sender, TextChangedEventArgs e)
        {
            HandleTextChangedForMatchAndDate(txtDateLocations, "Date");
        }

        private List<ExprParseTerm> GetParseResultForMatchText(string txtExpr, string cacheKey)
        {
            if (_parseResultCache.ContainsKey(cacheKey))
                if (_parseResultCache[cacheKey].parseText == txtExpr)
                    return _parseResultCache[cacheKey].parseTerms;
            ParseResultCacheElem newCacheElem = new ParseResultCacheElem();
            List<ExprParseTerm> exprParseTermList = DocTypesMatcher.ParseDocMatchExpression(txtExpr, 0);
            newCacheElem.parseText = txtExpr;
            newCacheElem.parseTerms = exprParseTermList;
            int bracketCount = 0;
            foreach (ExprParseTerm exprTerm in exprParseTermList)
                if (bracketCount <= exprTerm.locationBracketIdx)
                    bracketCount = exprTerm.locationBracketIdx + 1;
            newCacheElem.locationBracketCount = bracketCount;
            _parseResultCache[cacheKey] = newCacheElem;
            return exprParseTermList;
        }

        // The following assumes the cache has been primed before this point
        private int GetBracketCountBaseForCacheKey(string cacheKey)
        {
            if (cacheKey == "Date")
            {
                if (_parseResultCache.ContainsKey("Match"))
                    return _parseResultCache["Match"].locationBracketCount;
            }
            return 0;
        }

        private void HandleTextChangedForMatchAndDate(RichTextBox rtb, string cacheKey)
        {
            // Avoid re-entering when we change the text programmatically
            if (bInTextChangedHandler)
                return;

            // Check the richtextbox is valid
            if (rtb.Document == null)
                return;

            // Handle button/field enable/disable
            UpdateUIForDocTypeChanges();

            // Extract string
            string txtExpr = GetTextFromRichTextBox(rtb);

            // Get the parse result for this text
            List<ExprParseTerm> exprParseTermList = GetParseResultForMatchText(txtExpr, cacheKey);

            // Get current caret position
            int curCaretPos = GetCaretPos(rtb);

            // Generate the rich text to highlight string elements
            Paragraph para = new Paragraph();
            foreach (ExprParseTerm parseTerm in exprParseTermList)
            {
                Run txtRun = new Run(txtExpr.Substring(parseTerm.stPos, parseTerm.termLen));
                txtRun.Foreground = parseTerm.GetBrush(GetBracketCountBaseForCacheKey(cacheKey));
                para.Inlines.Add(txtRun);
            }

            // Switch to new doc contents
            bInTextChangedHandler = true;
            rtb.Document.Blocks.Clear();
            rtb.Document.Blocks.Add(para);
            SetCaretPos(rtb, curCaretPos);
            bInTextChangedHandler = false;

            // Set visualisation rectangles for both match and date boxes
            DrawLocationRectanglesForAllBoxes();

            // Redisplay the check status for the currently displayed document
            CheckDisplayedDocForMatchAndShowResult();
        }

        private void DrawLocationRectanglesForAllBoxes()
        {
            // Clear visual rectangles
            locRectHandler.ClearVisRectangles();

            // Generate required location rectangles for match expression
            string txtExpr = GetTextFromRichTextBox(txtMatchExpression);
            string cacheTerm = "Match";
            List<ExprParseTerm> exprParseTermList = GetParseResultForMatchText(txtExpr, cacheTerm);
            int locRectMaxIdx = 0;
            foreach (ExprParseTerm parseTerm in exprParseTermList)
            {
                // Check for location rectangle
                if (parseTerm.termType == ExprParseTerm.ExprParseTermType.exprTerm_Location)
                {
                    locRectHandler.AddVisRectangle(txtExpr.Substring(parseTerm.stPos, parseTerm.termLen), cacheTerm, parseTerm.locationBracketIdx, parseTerm.GetBrush());
                    if (locRectMaxIdx < parseTerm.locationBracketIdx)
                        locRectMaxIdx = parseTerm.locationBracketIdx;
                }
            }

            // Generate location rectangles for date expressions
            string dateExpr = GetTextFromRichTextBox(txtDateLocations);
            cacheTerm = "Date";
            List<ExprParseTerm> dateParseTermList = GetParseResultForMatchText(dateExpr, cacheTerm);
            foreach (ExprParseTerm parseTerm in dateParseTermList)
            {
                // Check for location rectangle
                if (parseTerm.termType == ExprParseTerm.ExprParseTermType.exprTerm_Location)
                    locRectHandler.AddVisRectangle(dateExpr.Substring(parseTerm.stPos, parseTerm.termLen), cacheTerm, parseTerm.locationBracketIdx, parseTerm.GetBrush(locRectMaxIdx+1));
            }

            // Draw the location rectangles
            locRectHandler.DrawVisRectangles();
        }

        private string FormatLocationStr(DocRectangle docRect)
        {
            string st = String.Format("{0:0},{1:0},{2:0},{3:0}", docRect.X, docRect.Y, docRect.Width, docRect.Height);
            return st;
        }

        #endregion

        #region Location Rectangle Drag, Move, Create

        private void exampleFileImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            locRectHandler.HandleMouseDown(sender, e);
            e.Handled = true;
        }

        private void exampleFileImage_MouseMove(object sender, MouseEventArgs e)
        {
            locRectHandler.HandleMouseMove(sender, e);
            e.Handled = true;
        }

        private void exampleFileImage_MouseLeave(object sender, MouseEventArgs e)
        {
            locRectHandler.HandleMouseLeave(sender, e);
            e.Handled = true;
        }

        private void exampleFileImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            locRectHandler.HandleMouseUp(sender, e);
            e.Handled = true;
        }

        private void tooltipCallback_MouseMove(Point ptOnImage, DocRectangle ptInDocPercent)
        {
            bool bToolTipSet = false;
            if (_curDocDisplay_scanPages != null)
                if ((_curDocDisplay_pageNum > 0) && (_curDocDisplay_pageNum <= _curDocDisplay_scanPages.scanPagesText.Count))
                {
                    if (!exampleFileImageToolTip.IsOpen)
                        exampleFileImageToolTip.IsOpen = true;
                    exampleFileImageToolTip.HorizontalOffset = ptOnImage.X - 100;
                    exampleFileImageToolTip.VerticalOffset = ptOnImage.Y;
                    List<ScanTextElem> scanTextElems = _curDocDisplay_scanPages.scanPagesText[_curDocDisplay_pageNum - 1];
                    foreach (ScanTextElem el in scanTextElems)
                        if (el.bounds.Intersects(ptInDocPercent))
                        {
                            exampleFileImageToolText.Text = el.text;
                            bToolTipSet = true;
                            break;
                        }
                }
            if (!bToolTipSet)
            {
                exampleFileImageToolText.Text = "";
                exampleFileImageToolTip.IsOpen = false;
            }
        }

        private void tooptipCallback_MouseLeave(Point ptOnImage)
        {
            if (ptOnImage.X < 0 || ptOnImage.X > exampleFileImage.ActualWidth)
                exampleFileImageToolTip.IsOpen = false;
            else if (ptOnImage.Y < 0 || ptOnImage.Y > exampleFileImage.ActualHeight)
                exampleFileImageToolTip.IsOpen = false;
        }   

        private void docRectChangesComplete(string nameOfMatchingTextBox, int docRectIdx, DocRectangle rectInDocPercent)
        {
            // Check which text box the rectangle is in
            RichTextBox rtb = txtMatchExpression;
            string cacheTermForRtb = "Match";
            if ((nameOfMatchingTextBox == "Date") || ((nameOfMatchingTextBox == "New") && txtDateLocations.IsKeyboardFocused))
            {
                rtb = txtDateLocations;
                cacheTermForRtb = "Date";
            }

            // Extract string
            string txtExpr = GetTextFromRichTextBox(rtb);

            // Parse using our grammar
            List<ExprParseTerm> exprParseTermList = GetParseResultForMatchText(txtExpr, cacheTermForRtb);

            // Get current caret position
            int curCaretPos = GetCaretPos(rtb);

            // Find where to change/insert the location
            bool bInserted = false;
            int bestNewRectPos = txtExpr.Length;
            string newTextExpr = txtExpr;
            foreach (ExprParseTerm parseTerm in exprParseTermList)
            {
                Run txtRun = new Run(txtExpr.Substring(parseTerm.stPos, parseTerm.termLen));
                // Check for location rectangle
                if (parseTerm.termType == ExprParseTerm.ExprParseTermType.exprTerm_Location)
                {
                    if (parseTerm.locationBracketIdx == docRectIdx)
                    {
                        newTextExpr = txtExpr.Substring(0, parseTerm.stPos) + FormatLocationStr(rectInDocPercent) + txtExpr.Substring(parseTerm.stPos + parseTerm.termLen);
                        bInserted = true;
                        break;
                    }
                }
                else if (parseTerm.termType == ExprParseTerm.ExprParseTermType.exprTerm_Text)
                    if (curCaretPos >= parseTerm.stPos)
                        bestNewRectPos = parseTerm.stPos + parseTerm.termLen;
            }
            if (!bInserted)
            {
                newTextExpr = txtExpr.Substring(0, bestNewRectPos) + "{" + FormatLocationStr(rectInDocPercent) + "}";
                string endOfStr = txtExpr.Substring(bestNewRectPos);
                if (endOfStr.Trim().Length > 0)
                {
                    if (endOfStr.Trim().Substring(0, 1) == "{")
                    {
                        int closePos = endOfStr.IndexOf('}');
                        if (closePos > 0)
                            endOfStr = endOfStr.Substring(closePos + 1);
                    }
                }
                newTextExpr += endOfStr;
            }

            // All rectangles will get redrawn as text expression is changed and causes trigger to refresh
            SetTextInRichTextBox(rtb, newTextExpr);

        }

        #endregion

        #region Form events and buttons

        private void docOverlayCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
//            locRectHandler.DrawVisRectangles();
        }

        private void btnEditDocType_Click(object sender, RoutedEventArgs e)
        {
            btnEditDocType.IsEnabled = false;
            btnRenameDocType.IsEnabled = false;
            btnNewDocType.IsEnabled = false;
            chkEnabledDocType.IsEnabled = true;
            txtMatchExpression.IsEnabled = true;
            txtDateLocations.IsEnabled = true;
            locRectHandler.SelectionEnable(true);
            txtMoveTo.IsEnabled = true;
            btnMoveToPick.IsEnabled = true;
            btnMoveToMonthYear.IsEnabled = true;
            btnMoveToYearQtr.IsEnabled = true;
            txtRenameTo.IsEnabled = true;
            btnCancelTypeChanges.IsEnabled = true;
            btnUseCurrentDocImageAsThumbnail.IsEnabled = true;
            btnPickThumbnail.IsEnabled = true;
            btnClearThumbail.IsEnabled = true;

        }

        private void btnRenameDocType_Click(object sender, RoutedEventArgs e)
        {
            btnEditDocType.IsEnabled = false;
            btnRenameDocType.IsEnabled = false;
            btnNewDocType.IsEnabled = false;
            txtDocTypeName.IsEnabled = true;
            chkEnabledDocType.IsEnabled = true;
            txtMatchExpression.IsEnabled = true;
            txtDateLocations.IsEnabled = true;
            locRectHandler.SelectionEnable(true);
            txtMoveTo.IsEnabled = true;
            btnMoveToPick.IsEnabled = true;
            btnMoveToMonthYear.IsEnabled = true;
            btnMoveToYearQtr.IsEnabled = true;
            txtRenameTo.IsEnabled = true;
            btnCancelTypeChanges.IsEnabled = true;
            btnUseCurrentDocImageAsThumbnail.IsEnabled = true;
            btnPickThumbnail.IsEnabled = true;
            btnClearThumbail.IsEnabled = true;
        }

        private void btnNewDocType_Click(object sender, RoutedEventArgs e)
        {
            btnEditDocType.IsEnabled = false;
            btnRenameDocType.IsEnabled = false;
            btnNewDocType.IsEnabled = false;
            txtDocTypeName.IsEnabled = true;
            chkEnabledDocType.IsEnabled = true;
            txtMatchExpression.IsEnabled = true;
            txtDateLocations.IsEnabled = true;
            locRectHandler.SelectionEnable(true);
            txtMoveTo.IsEnabled = true;
            txtMoveTo.Text = "";
            btnMoveToPick.IsEnabled = true;
            btnMoveToMonthYear.IsEnabled = true;
            btnMoveToYearQtr.IsEnabled = true;
            txtRenameTo.IsEnabled = true;
            txtRenameTo.Text = Properties.Settings.Default.DefaultRenameTo;
            _selectedDocType = null;
            docTypeListView.SelectedItem = null;
            txtDocTypeName.Text = "";
            chkEnabledDocType.IsChecked = true;
            SetTextInRichTextBox(txtMatchExpression, "");
            SetTextInRichTextBox(txtDateLocations, "");
            btnCancelTypeChanges.IsEnabled = true;
            ShowDocTypeThumbnail("");
            btnUseCurrentDocImageAsThumbnail.IsEnabled = true;
            btnPickThumbnail.IsEnabled = true;
            btnClearThumbail.IsEnabled = true;
        }

        private void btnSaveTypeChanges_Click(object sender, RoutedEventArgs e)
        {
            // Remember doctype name to go back to after save
            string curDocTypeName = txtDocTypeName.Text;

            // Check for renaming
            if ((_selectedDocType != null) && (_selectedDocType.docTypeName != txtDocTypeName.Text))
            {
                // Ensure the new name is unique
                DocType testDocType = _docTypesMatcher.GetDocType(txtDocTypeName.Text);
                if (testDocType != null)
                {
                    MessageBoxButton btnMessageBox = MessageBoxButton.OK;
                    MessageBoxImage icnMessageBox = MessageBoxImage.Information;
                    MessageBoxResult rsltMessageBox = MessageBox.Show("There is already a Document Type with this name", "Naming Problem", btnMessageBox, icnMessageBox);
                    return;
                }
                else
                {
                    MessageBoxButton btnMessageBox = MessageBoxButton.YesNo;
                    MessageBoxImage icnMessageBox = MessageBoxImage.Question;
                    MessageBoxResult rsltMessageBox = MessageBox.Show("Do you want to RENAME this Document Type", "Rename?", btnMessageBox, icnMessageBox);
                    if (rsltMessageBox == MessageBoxResult.No)
                        return;
                }

                // Change the existing record to indicate it has been renamed & make it disabled
                _selectedDocType.isEnabled = false;
                _selectedDocType.renamedTo = txtDocTypeName.Text;

                // Update the original record
                _docTypesMatcher.AddOrUpdateDocTypeRecInDb(_selectedDocType);

                // Create a new record
                DocType newDocType = GetDocTypeFromForm(new DocType());
                newDocType.previousName = _selectedDocType.docTypeName;
                _docTypesMatcher.AddOrUpdateDocTypeRecInDb(newDocType);
            }
            else if (_selectedDocType == null)
            {
                // Ensure the new name is unique
                DocType testDocType = _docTypesMatcher.GetDocType(txtDocTypeName.Text);
                if (testDocType != null)
                {
                    MessageBoxButton btnMessageBox = MessageBoxButton.OK;
                    MessageBoxImage icnMessageBox = MessageBoxImage.Information;
                    MessageBoxResult rsltMessageBox = MessageBox.Show("There is already a Document Type with this name", "Naming Problem", btnMessageBox, icnMessageBox);
                    return;
                }

                // Create the new record
                DocType newDocType = GetDocTypeFromForm(new DocType());
                _docTypesMatcher.AddOrUpdateDocTypeRecInDb(newDocType);
            }
            else
            {
                // Get changes to record
                _selectedDocType = GetDocTypeFromForm(_selectedDocType);

                // Update the record
                _docTypesMatcher.AddOrUpdateDocTypeRecInDb(_selectedDocType);
            }

            // Ensure no longer able to save/cancel
            btnCancelTypeChanges.IsEnabled = false;
            btnSaveTypeChanges.IsEnabled = false;

            // Reload the form (selecting appropriate item)
            ShowDocTypeList(curDocTypeName, null, null);
        }

        private void btnCancelTypeChanges_Click(object sender, RoutedEventArgs e)
        {
            SetupDocTypeForm(_selectedDocType);
        }

        private void btnBackPage_Click(object sender, RoutedEventArgs e)
        {
            DisplayFiledDoc_PrevPage();
        }

        private void btnNextPage_Click(object sender, RoutedEventArgs e)
        {
            DisplayFiledDoc_NextPage();
        }

        private void chkEnabledDocType_Changed(object sender, RoutedEventArgs e)
        {
            UpdateUIForDocTypeChanges();
        }

        private void txtDocTypeName_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateUIForDocTypeChanges();
        }

        private void SetupDocTypeForm(DocType docType)
        {
            btnEditDocType.IsEnabled = true;
            btnRenameDocType.IsEnabled = true;
            btnNewDocType.IsEnabled = true;
            btnCancelTypeChanges.IsEnabled = false;
            btnSaveTypeChanges.IsEnabled = false;
            chkEnabledDocType.IsEnabled = false;
            txtDocTypeName.IsEnabled = false;
            txtMatchExpression.IsEnabled = false;
            txtDateLocations.IsEnabled = false;
            locRectHandler.SelectionEnable(false);
            txtMoveTo.IsEnabled = false;
            btnMoveToPick.IsEnabled = false;
            btnMoveToMonthYear.IsEnabled = false;
            btnMoveToYearQtr.IsEnabled = false;
            txtRenameTo.IsEnabled = false;
            btnClearThumbail.IsEnabled = false;
            btnUseCurrentDocImageAsThumbnail.IsEnabled = false;
            btnPickThumbnail.IsEnabled = false;
            bInSetupDocTypeForm = true;
            if (docType == null)
            {
                txtDocTypeName.Text = "";
                chkEnabledDocType.IsChecked = false;
                SetTextInRichTextBox(txtMatchExpression, "");
                SetTextInRichTextBox(txtDateLocations, "");
                ShowDocTypeThumbnail("");
                txtMoveTo.Text = "";
                txtRenameTo.Text = Properties.Settings.Default.DefaultRenameTo;
            }
            else
            {
                txtDocTypeName.Text = docType.docTypeName;
                chkEnabledDocType.IsChecked = docType.isEnabled;
                SetTextInRichTextBox(txtMatchExpression, docType.matchExpression);
                SetTextInRichTextBox(txtDateLocations, docType.dateExpression);
                ShowDocTypeThumbnail(docType.thumbnailForDocType);
                txtMoveTo.Text = docType.moveFileToPath;
                string defaultRenameToContents = Properties.Settings.Default.DefaultRenameTo;
                txtRenameTo.Text = docType.renameFileTo == defaultRenameToContents ? "" : docType.renameFileTo;
            }
            bInSetupDocTypeForm = false;
            UpdateUIForDocTypeChanges();
        }

        private void ShowDocTypeThumbnail(string thumbnailStr)
        {
            _curDocTypeThumbnail = thumbnailStr;
            int heightOfThumb = 150;
            if (!double.IsNaN(imgDocThumbnail.Height))
                heightOfThumb = (int)imgDocThumbnail.Height;
            if (thumbnailStr == "")
                imgDocThumbnail.Source = null;
            else
                imgDocThumbnail.Source = DocTypeHelper.LoadDocThumbnail(thumbnailStr, heightOfThumb);
        }

        private bool AreDocTypeChangesPendingSaveOrCancel()
        {
            return btnSaveTypeChanges.IsEnabled;
        }

        private void UpdateUIForDocTypeChanges()
        {
            if (bInSetupDocTypeForm)
                return;
            bool somethingChanged = false;
            if (_selectedDocType != null)
            {
                string compareStr = _selectedDocType.matchExpression;
                if (compareStr == null)
                    compareStr = "";
                bool matchExprChanged = (compareStr.Trim() != GetTextFromRichTextBox(txtMatchExpression).Trim());
                compareStr = _selectedDocType.dateExpression;
                if (compareStr == null)
                    compareStr = "";
                bool dateExprChanged = (compareStr.Trim() != GetTextFromRichTextBox(txtDateLocations).Trim());
                bool docTypeEnabledChanged = (_selectedDocType.isEnabled != chkEnabledDocType.IsChecked);
                bool docTypeRenamed = (_selectedDocType.docTypeName != txtDocTypeName.Text) && (txtDocTypeName.Text.Trim() != "");
                bool thumbnailChanged = (_selectedDocType.thumbnailForDocType != _curDocTypeThumbnail);
                bool renameToChanged = (_selectedDocType.renameFileTo != txtRenameTo.Text);
                bool moveToChanged = (_selectedDocType.moveFileToPath != txtMoveTo.Text);
                somethingChanged = (matchExprChanged || dateExprChanged || docTypeEnabledChanged || docTypeRenamed || thumbnailChanged || renameToChanged || moveToChanged);
            }
            else
            {
                somethingChanged = (txtDocTypeName.Text.Trim() != "");
            }
            if (somethingChanged)
            {
                btnCancelTypeChanges.IsEnabled = true;
                btnSaveTypeChanges.IsEnabled = true;
            }
            else
            {
                btnCancelTypeChanges.IsEnabled = false;
                btnSaveTypeChanges.IsEnabled = false;
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_bwThread.WorkerSupportsCancellation)
                _bwThread.CancelAsync();
        }

        private void btnShowDocToBeFiled_Click(object sender, RoutedEventArgs e)
        {
            if ((_curUnfiledScanDocInfo != null) && (_curUnfiledScanDocPages != null))
                DisplayExampleDoc(_curUnfiledScanDocInfo.uniqName, 1, _curUnfiledScanDocPages);
        }

        private void btnUseCurrentDocImageAsThumbnail_Click(object sender, RoutedEventArgs e)
        {
            ShowDocTypeThumbnail(_curDocDisplay_uniqName + "~" + _curDocDisplay_pageNum.ToString());
            UpdateUIForDocTypeChanges();
        }

        private void btnPickThumbnail_Click(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog cofd = new CommonOpenFileDialog("Select thumbnail file");
            cofd.Multiselect = false;
            cofd.InitialDirectory = Properties.Settings.Default.BasePathForFilingFolderSelection;
            cofd.Filters.Add(new CommonFileDialogFilter("Image File", ".jpg,.png"));
            CommonFileDialogResult result = cofd.ShowDialog(this);
            if (result == CommonFileDialogResult.Ok)
                ShowDocTypeThumbnail(cofd.FileName);
            UpdateUIForDocTypeChanges();
        }

        private void btnClearThumbail_Click(object sender, RoutedEventArgs e)
        {
            ShowDocTypeThumbnail("");
            UpdateUIForDocTypeChanges();
        }

        private void btnMoveToPick_Click(object sender, RoutedEventArgs e)
        {
            // Check what path to use
            string folderToUse = "";
            if (txtMoveTo.Text.Trim() != "")
            {
                bool pathContainsMacros = false;
                folderToUse = _docTypesMatcher.ComputeExpandedPath(txtMoveTo.Text.Trim(), DateTime.Now, true, ref pathContainsMacros);
            }
            else if ((_lastSelectedFolder != "") && (Directory.Exists(_lastSelectedFolder)))
            { 
                folderToUse = _lastSelectedFolder;
            }
            else if (Directory.Exists(Properties.Settings.Default.BasePathForFilingFolderSelection))
            {
                folderToUse = Properties.Settings.Default.BasePathForFilingFolderSelection;
            }

            CommonOpenFileDialog cofd = new CommonOpenFileDialog("Select Folder for filing this document type");
            cofd.IsFolderPicker = true;
            cofd.Multiselect = false;
            cofd.InitialDirectory = folderToUse;
            cofd.DefaultDirectory = folderToUse;
            cofd.EnsurePathExists = true;
            CommonFileDialogResult result = cofd.ShowDialog(this);
            if (result == CommonFileDialogResult.Ok)
            {
                string folderName = cofd.FileName;
                _lastSelectedFolder = folderName;
                txtMoveTo.Text = _docTypesMatcher.ComputeMinimalPath(folderName);
            }

            //var dialog = new System.Windows.Forms.FolderBrowserDialog();
            //dialog.Description = "Select folder for filing this type of document";
            //dialog.RootFolder = Environment.SpecialFolder.Desktop;
            //dialog.ShowNewFolderButton = true;
            //if ((_lastSelectedFolder != "") && (Directory.Exists(_lastSelectedFolder)))
            //    dialog.SelectedPath = _lastSelectedFolder;
            //else if (Directory.Exists(Properties.Settings.Default.BasePathForFilingFolderSelection))
            //    dialog.SelectedPath = Properties.Settings.Default.BasePathForFilingFolderSelection;
            //System.Windows.Forms.DialogResult result = dialog.ShowDialog();
            //if (result == System.Windows.Forms.DialogResult.OK)
            //{
            //    string folderName = dialog.SelectedPath;
            //    _lastSelectedFolder = folderName;
            //    txtMoveTo.Text = _docTypesMatcher.ComputeMinimalPath(folderName);
            //}
        }

        private void btnMoveToMonthYear_Click(object sender, RoutedEventArgs e)
        {
            // Add year & yearmonth to folder name
            txtMoveTo.Text += @"\[year]\[year-month]";
        }

        private void btnMoveToYearQtr_Click(object sender, RoutedEventArgs e)
        {
            // Add year qtr to folder name
            txtMoveTo.Text += @"\[year]\[year-qtr]";
        }

        private void txtMoveTo_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateUIForDocTypeChanges();
        }

        private void txtRenameTo_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateUIForDocTypeChanges();
        }

        private void btnMacros_Click(object sender, RoutedEventArgs e)
        {
            PathSubstView ptv = new PathSubstView(_docTypesMatcher);
            ptv.ShowDialog();
        }

        private void exampleFileImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            locRectHandler.DrawVisRectangles();
            // Re-check the document and display result
            CheckDisplayedDocForMatchAndShowResult();
        }

        #endregion

        private class DocCompareRslt
        {
            public DocCompareRslt()
            {
                bMatches = false;
                bMatchesButShouldnt = false;
                bDoesntMatchButShould = false;
            }

            public string uniqName { get; set; }
            public string matchStatus { get; set; }
            public string docTypeFiled { get; set; }
            public ScanPages scanPages { get; set; }
            public bool bMatches { get; set; }
            public bool bMatchesButShouldnt { get; set; }
            public bool bDoesntMatchButShould { get; set; }
            public string matchFactorStr { get; set; }
        }

        private class ParseResultCacheElem
        {
            public string parseText = "";
            public List<ExprParseTerm> parseTerms = new List<ExprParseTerm>();
            public int locationBracketCount = 0;
        }

        private void txtDateResult_MouseEnter(object sender, MouseEventArgs e)
        {
//            popupDateResult.HorizontalOffset = ptOnImage.X - 100;
//            popupDateResult.VerticalOffset = ptOnImage.Y;
            locRectHandler.ClearTextMatchRect("dateMatch");
            if (_curDocDisplay_lastMatchResult != null)
            {
                // Create popup string
                string datestr = "";
                foreach (ExtractedDate dat in _curDocDisplay_lastMatchResult.datesFoundInDoc)
                {
                    if (datestr.Length != 0)
                        datestr += "\n";
                    datestr += dat.dateTime.ToLongDateString() + " (" + dat.matchFactor.ToString() + "%) Page " + dat.pageNum;

                    // Display match locations
                    if (dat.pageNum == _curDocDisplay_pageNum)
                    {
                        Brush colrBrush = Brushes.Firebrick;
                        DocRectangle inRect = dat.locationOfDateOnPagePercent;
                        DocRectangle computedLocation = new DocRectangle(inRect.X, inRect.Y, inRect.Width, inRect.Height);
                        double wid = computedLocation.Width;
                        double inx = computedLocation.X;
                        computedLocation.X = inx + dat.posnInText * wid / dat.foundInText.Length;
                        computedLocation.Width = dat.matchLength * wid / dat.foundInText.Length;
                        locRectHandler.DrawTextMatchRect(computedLocation, colrBrush, "dateMatch");
                    }

                }
                popupDateResultText.Text = datestr;
                if (!popupDateResult.IsOpen)
                    popupDateResult.IsOpen = true;
            }
            e.Handled = true;
        }

        private void txtDateResult_MouseLeave(object sender, MouseEventArgs e)
        {
            popupDateResultText.Text = "";
            popupDateResult.IsOpen = false;
            locRectHandler.ClearTextMatchRect("dateMatch");
            e.Handled = true;
        }

        private static void SaveClipboardImageToFile(string filePath)
        {
            var image = Clipboard.GetImage();
            if (image == null)
                return;
            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    BitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    encoder.Save(fileStream);
                }
            }
            catch
            {

            }
        }

        private void imgDocThumbMenuPaste_Click(object sender, RoutedEventArgs e)
        {
            // Save to a file in the thumbnails folder
            string thumbnailStr = DocTypeHelper.GetNameForPastedThumbnail();
            string thumbFilename = DocTypeHelper.GetFilenameFromThumbnailStr(thumbnailStr);
            SaveClipboardImageToFile(thumbnailStr);
            ShowDocTypeThumbnail(thumbnailStr);
            UpdateUIForDocTypeChanges();
        }








    }
}
