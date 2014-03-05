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
    public partial class DocTypeView : Window
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private ScanDocHandler _scanDocHandler;
        private DocTypesMatcher _docTypesMatcher;
        private DocType _selectedDocType;
        BackgroundWorker _bwThread;
        ObservableCollection<DocType> _docTypeColl = new ObservableCollection<DocType>();
        ObservableCollection<DocCompareRslt> _docCompareRslts = new ObservableCollection<DocCompareRslt>();
        private bool bInTextChangedHandler = false;
        private string _curDocDisplay_uniqName = "";
        private int _curDocDisplay_pageNum = 1;
        private ScanPages _curDocDisplay_scanPages;
        private ScanDocAllInfo _curUnfiledScanDocAllInfo = null;
        private string _curDocTypeThumbnail = "";
        private bool bInSetupDocTypeForm = false;
        // Cache last parse result
        private Dictionary<string, ParseResultCacheElem> _parseResultCache = new Dictionary<string,ParseResultCacheElem>();

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
            locRectHandler.SelectionEnable(true);

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

        public void ShowDocTypeList(string selDocTypeName, ScanDocAllInfo unfiledScanDocAllInfo)
        {
            _curUnfiledScanDocAllInfo = unfiledScanDocAllInfo;
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
            if ((_curUnfiledScanDocAllInfo != null) && (_curUnfiledScanDocAllInfo.scanDocInfo != null))
            {
                DisplayExampleDoc(_curUnfiledScanDocAllInfo.scanDocInfo.uniqName, 1, _curUnfiledScanDocAllInfo.scanPages);
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
                DocType chkDocType = GetDocTypeFromForm();
                _bwThread.RunWorkerAsync(chkDocType);
                btnTestMatch.Content = "Stop Finding";
                SetDocMatchStatusText("Working...");
                _docCompareRslts.Clear();
            }
        }

        private DocType GetDocTypeFromForm()
        {
            DocType chkDocType = new DocType();
            chkDocType.docTypeName = txtDocTypeName.Text;
            chkDocType.isEnabled = true;
            chkDocType.matchExpression = GetTextFromRichTextBox(txtMatchExpression);
            chkDocType.dateExpression = GetTextFromRichTextBox(txtDateLocations);
            return chkDocType;
        }

        private void SetDocMatchStatusText(string inStr)
        {
            List<Brush> colrBrushes = new List<Brush> 
            {
                Brushes.Black, Brushes.Green, Brushes.Red, Brushes.Orange, Brushes.Purple, Brushes.Peru, Brushes.Purple
            };

            FlowDocument fd = new FlowDocument();
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
        }

        private void FindMatchingDocs_DoWork(object sender, DoWorkEventArgs e)
        {
            MatchDocsResult mdResult = new MatchDocsResult();
            e.Result = mdResult;
            BackgroundWorker worker = sender as BackgroundWorker;
            List<ScanDocInfo> sdiList = _scanDocHandler.GetListOfScanDocs();
            int docIdx = 0;
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
                    this.Dispatcher.BeginInvoke((Action)delegate()
                    {
                        _docCompareRslts.Add(rslt);
                    });
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
            DocTypeMatchResult matchResult = _docTypesMatcher.CheckIfDocMatches(scanPages, docTypeToMatch);
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
            compRslt.matchResult = matchResult;
            return compRslt;
        }

        private void CheckDisplayedDocForMatchAndShowResult()
        {
            if (_curDocDisplay_scanPages == null)
                return;
            DocType chkDocType = GetDocTypeFromForm();
            DocTypeMatchResult matchRslt = _docTypesMatcher.CheckIfDocMatches(_curDocDisplay_scanPages, chkDocType);
            DisplayMatchResultForDoc(matchRslt);
        }

        private void DisplayMatchResultForDoc(DocTypeMatchResult matchRslt)
        {
            if (matchRslt.matchCertaintyPercent == 100)
            {
                txtCheckResult.Text = "MATCHES";
                txtCheckResult.Foreground = Brushes.White;
                txtCheckResult.Background = Brushes.Green;
            }
            else
            {
                txtCheckResult.Text = "FAILED";
                txtCheckResult.Foreground = Brushes.White;
                txtCheckResult.Background = Brushes.Red;
            }
        }

        private string DocMatchFormatResultStr(MatchDocsResult mdResult)
        {
            return  "Files: " + mdResult.totalFilesProcessed.ToString() + "\n" +
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
            //if ((e.Cancelled == true))
            //    SetDocMatchStatusText("Cancelled");
            //else if (!(e.Error == null))
            //    SetDocMatchStatusText("Error: ~1" + e.Error.Message);
            //else
            //    SetDocMatchStatusText("Finished");
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
                    // Re-check the document and display result - the expression could have changed since table was populated
                    CheckDisplayedDocForMatchAndShowResult();
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
            rtb.Document.Blocks.Clear();
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
            List<ExprParseTerm> exprParseTermList = _docTypesMatcher.ParseDocMatchExpression(txtExpr, 0);
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
            // Only start if document editing is enabled
            if (!txtMatchExpression.IsEnabled)
                return;
            locRectHandler.HandleMouseDown(sender, e);
        }

        private void exampleFileImage_MouseMove(object sender, MouseEventArgs e)
        {
            locRectHandler.HandleMouseMove(sender, e);
        }

        private void exampleFileImage_MouseLeave(object sender, MouseEventArgs e)
        {
            locRectHandler.HandleMouseLeave(sender, e);
        }

        private void exampleFileImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            locRectHandler.HandleMouseUp(sender, e);
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
                SetTextInRichTextBox(rtb, newTextExpr);
            }

            // All rectangles will get redrawn as text expression is changed and causes trigger to refresh
            SetTextInRichTextBox(rtb, newTextExpr);

        }

        #endregion

        #region Form events and buttons

        private void docOverlayCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            locRectHandler.DrawVisRectangles();
        }

        private void btnEditDocType_Click(object sender, RoutedEventArgs e)
        {
            btnEditDocType.IsEnabled = false;
            btnRenameDocType.IsEnabled = false;
            btnNewDocType.IsEnabled = false;
            chkEnabledDocType.IsEnabled = true;
            txtMatchExpression.IsEnabled = true;
            txtDateLocations.IsEnabled = true;
            btnCancelTypeChanges.IsEnabled = true;
            btnUseCurrentDocImageAsThumbnail.IsEnabled = true;
            btnClearThumbail.IsEnabled = true;
        }

        private void btnRenameDocType_Click(object sender, RoutedEventArgs e)
        {
            btnEditDocType.IsEnabled = false;
            btnRenameDocType.IsEnabled = false;
            btnNewDocType.IsEnabled = false;
            txtDocTypeName.IsEnabled = true;
            chkEnabledDocType.IsEnabled = true;
            txtDateLocations.IsEnabled = true;
            txtMatchExpression.IsEnabled = true;
            btnCancelTypeChanges.IsEnabled = true;
            btnUseCurrentDocImageAsThumbnail.IsEnabled = true;
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
            _selectedDocType = null;
            docTypeListView.SelectedItem = null;
            txtDocTypeName.Text = "";
            chkEnabledDocType.IsChecked = true;
            SetTextInRichTextBox(txtMatchExpression, "");
            SetTextInRichTextBox(txtDateLocations, "");
            btnCancelTypeChanges.IsEnabled = true;
            ShowDocTypeThumbnail("");
            btnUseCurrentDocImageAsThumbnail.IsEnabled = true;
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
                DocType newDocType = new DocType();
                newDocType.CloneForRenaming(txtDocTypeName.Text, _selectedDocType);
                newDocType.matchExpression = GetTextFromRichTextBox(txtMatchExpression);
                newDocType.dateExpression = GetTextFromRichTextBox(txtDateLocations);
                newDocType.isEnabled = (bool)chkEnabledDocType.IsChecked;
                newDocType.thumbnailForDocType = _curDocTypeThumbnail;

                // Create the new record
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

                // Create a new record
                DocType newDocType = new DocType();
                newDocType.docTypeName = txtDocTypeName.Text;
                newDocType.matchExpression = GetTextFromRichTextBox(txtMatchExpression);
                newDocType.dateExpression = GetTextFromRichTextBox(txtDateLocations);
                newDocType.isEnabled = (bool)chkEnabledDocType.IsChecked;
                newDocType.thumbnailForDocType = _curDocTypeThumbnail;

                // Create the new record
                _docTypesMatcher.AddOrUpdateDocTypeRecInDb(newDocType);
            }
            else
            {
                // Make changes to record
                _selectedDocType.matchExpression = GetTextFromRichTextBox(txtMatchExpression);
                _selectedDocType.dateExpression = GetTextFromRichTextBox(txtDateLocations);
                _selectedDocType.isEnabled = (bool)chkEnabledDocType.IsChecked;
                _selectedDocType.thumbnailForDocType = _curDocTypeThumbnail;

                // Update the record
                _docTypesMatcher.AddOrUpdateDocTypeRecInDb(_selectedDocType);
            }

            // Ensure no longer able to save/cancel
            btnCancelTypeChanges.IsEnabled = false;
            btnSaveTypeChanges.IsEnabled = false;

            // Reload the form (selecting appropriate item)
            ShowDocTypeList(curDocTypeName, null);
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
            txtMatchExpression.IsEnabled = false;
            txtDateLocations.IsEnabled = false;
            txtDocTypeName.IsEnabled = false;
            btnClearThumbail.IsEnabled = false;
            btnUseCurrentDocImageAsThumbnail.IsEnabled = false;
            bInSetupDocTypeForm = true;
            if (docType == null)
            {
                txtDocTypeName.Text = "";
                chkEnabledDocType.IsChecked = false;
                SetTextInRichTextBox(txtMatchExpression, "");
                SetTextInRichTextBox(txtDateLocations, "");
                ShowDocTypeThumbnail("");
            }
            else
            {
                txtDocTypeName.Text = docType.docTypeName;
                chkEnabledDocType.IsChecked = docType.isEnabled;
                SetTextInRichTextBox(txtMatchExpression, docType.matchExpression);
                SetTextInRichTextBox(txtDateLocations, docType.dateExpression);
                ShowDocTypeThumbnail(docType.thumbnailForDocType);
            }
            bInSetupDocTypeForm = false;
            UpdateUIForDocTypeChanges();
        }

        private void ShowDocTypeThumbnail(string uniqName)
        {
            _curDocTypeThumbnail = uniqName;
            if (uniqName == "")
            {
                imgDocThumbnail.Source = null;
                return;
            }
            string[] splitNameAndPageNum = uniqName.Split('~');
            string uniqNameOnly = (splitNameAndPageNum.Length > 0) ? splitNameAndPageNum[0] : "";
            string pageNumStr = (splitNameAndPageNum.Length > 1) ? splitNameAndPageNum[1] : "";
            int pageNum = 1;
            if (pageNumStr.Trim().Length > 0)
            {
                try { pageNum = Convert.ToInt32(pageNumStr); }
                catch { pageNum = 1; }
            }
            string imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, uniqNameOnly, pageNum, false);
            if (!File.Exists(imgFileName))
            {
                logger.Info("Thumbnail file doesn't exist for {0}", uniqNameOnly);
            }
            try
            {
                imgDocThumbnail.Source = new BitmapImage(new Uri("File:" + imgFileName));
            }
            catch (Exception excp)
            {
                logger.Error("Loading thumbnail file {0} excp {1}", imgFileName, excp.Message);
            }
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
                somethingChanged = (matchExprChanged || dateExprChanged || docTypeEnabledChanged || docTypeRenamed || thumbnailChanged);
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
            if ((_curUnfiledScanDocAllInfo != null) && (_curUnfiledScanDocAllInfo.scanDocInfo != null))
                DisplayExampleDoc(_curUnfiledScanDocAllInfo.scanDocInfo.uniqName, 1, _curDocDisplay_scanPages);
        }

        private void btnUseCurrentDocImageAsThumbnail_Click(object sender, RoutedEventArgs e)
        {
            ShowDocTypeThumbnail(_curDocDisplay_uniqName + "~" + _curDocDisplay_pageNum.ToString());
            UpdateUIForDocTypeChanges();
        }

        private void btnClearThumbail_Click(object sender, RoutedEventArgs e)
        {
            ShowDocTypeThumbnail("");
            UpdateUIForDocTypeChanges();
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
            public DocTypeMatchResult matchResult { get; set; }
        }

        private class ParseResultCacheElem
        {
            public string parseText = "";
            public List<ExprParseTerm> parseTerms = new List<ExprParseTerm>();
            public int locationBracketCount = 0;
        }
    }
}
