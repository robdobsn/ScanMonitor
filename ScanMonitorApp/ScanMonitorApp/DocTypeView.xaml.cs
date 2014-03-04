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
        bool _dragSelectActive = false;
        Point _dragSelectFromPoint;
        bool _dragSelectOverThreshold = false;
        string _dragSelectionRectName;
        DRAG_FROM _dragFrom = DRAG_FROM.NONE;
        Point _dragSelectOppositeCorner;
        int _dragSelect_nextLocationIdx = 0;
        private static readonly double DragThreshold = 5;
        private bool bInTextChangedHandler = false;
        List<VisRect> _visMatchRectangles = new List<VisRect>();
        private const int TOP_ELLIPSE_OFFSET = 6;
        private const int BOTTOM_ELLIPSE_OFFSET = 5;
        enum DRAG_FROM {  NONE, CENTRE, TOPLEFT, BOTTOMRIGHT, NEW }
        private string _curDocDisplay_uniqName = "";
        private int _curDocDisplay_pageNum = 1;
        private DocCompareRslt _curDocDisplay_docCompareResult;

        public DocTypeView(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher)
        {
            InitializeComponent();
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;

            // List view for comparisons
            listMatchResults.ItemsSource = _docCompareRslts;
            listMatchResults.Items.SortDescriptions.Add(new SortDescription("matchStatus", ListSortDirection.Ascending));

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

        public void ShowDocTypeList(string selDocTypeName)
        {
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
            string txtExpr = GetMatchExprFromEditBox();
            chkDocType.matchExpression = txtExpr;
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
            List<FiledDocInfo> fdiList = _scanDocHandler.GetListOfFiledDocs();
            int docIdx = 0;
            foreach (FiledDocInfo fdi in fdiList)
            {
                if ((worker.CancellationPending == true))
                {
                    e.Cancel = true;
                    break;
                }

                DocType docTypeToMatch = (DocType)e.Argument;
                DocCompareRslt rslt = CheckIfDocMatches(fdi, docTypeToMatch);
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
                worker.ReportProgress((int)(docIdx * 100 / fdiList.Count));
                if (((docIdx % 10) == 0) || (docIdx == fdiList.Count - 1))
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

        private DocCompareRslt CheckIfDocMatches(FiledDocInfo fdi, DocType docTypeToMatch)
        {
            DocCompareRslt compRslt = new DocCompareRslt();
            ScanPages scanPages = _scanDocHandler.GetScanPages(fdi.uniqName);
            DocTypeMatchResult matchResult = _docTypesMatcher.CheckIfDocMatches(scanPages, docTypeToMatch);
            if (matchResult.matchCertaintyPercent == 100)
            {
                compRslt.bMatches = true;
                compRslt.bMatchesButShouldnt = (fdi.docTypeFiled != docTypeToMatch.docTypeName);
                compRslt.uniqName = fdi.uniqName;
                compRslt.docTypeFiled = fdi.docTypeFiled;
                compRslt.matchStatus = compRslt.bMatchesButShouldnt ? "MATCH-BUT-SHOULDN'T" : "OK";
                compRslt.scanPages = scanPages;
            }
            else
            {
                compRslt.bDoesntMatchButShould = (fdi.docTypeFiled == docTypeToMatch.docTypeName);
                if (compRslt.bDoesntMatchButShould)
                {
                    compRslt.uniqName = fdi.uniqName;
                    compRslt.docTypeFiled = fdi.docTypeFiled;
                    compRslt.matchStatus = "SHOULD-BUT-DOESN'T";
                    compRslt.scanPages = scanPages;
                }
            }
            compRslt.matchResult = matchResult;
            return compRslt;
        }

        private void CheckDisplayedDocForMatchAndShowResult()
        {
            if (_curDocDisplay_docCompareResult == null)
                return;
            DocType chkDocType = GetDocTypeFromForm();
            DocTypeMatchResult matchRslt = _docTypesMatcher.CheckIfDocMatches(_curDocDisplay_docCompareResult.scanPages, chkDocType);
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
                    _curDocDisplay_docCompareResult = docCompRslt;
                    DisplayFiledDoc(docCompRslt.uniqName, 1);
                    // Re-check the document and display result - the expression could have changed since table was populated
                    CheckDisplayedDocForMatchAndShowResult();
                }
            }
        }

        private void DisplayFiledDoc(string uniqName, int pageNum)
        {
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
            DisplayFiledDoc(_curDocDisplay_uniqName, _curDocDisplay_pageNum + 1);
        }

        private void DisplayFiledDoc_PrevPage()
        {
            DisplayFiledDoc(_curDocDisplay_uniqName, _curDocDisplay_pageNum - 1);
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

        private void SetTxtMatchExprBoxText(string txtStr)
        {
            Paragraph para = new Paragraph(new Run(txtStr));
            txtMatchExpression.Document.Blocks.Clear();
            txtMatchExpression.Document.Blocks.Add(para);
        }

        private string GetMatchExprFromEditBox()
        {
            string txtExpr = new TextRange(txtMatchExpression.Document.ContentStart, txtMatchExpression.Document.ContentEnd).Text;
            txtExpr = txtExpr.Replace("\r", "");
            txtExpr = txtExpr.Replace("\n", "");
            return txtExpr;
        }

        private void txtMatchExpression_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Avoid re-entering when we change the text programmatically
            if (bInTextChangedHandler)
                return;

            // Check the richtextbox is valid
            if (txtMatchExpression.Document == null)
                return;

            // Extract string
            string txtExpr = GetMatchExprFromEditBox();

            // Handle button/field enable/disable
            UpdateUIForDocTypeChanges();

            // Parse using our grammar
            List<ExprParseTerm> exprParseTermList = _docTypesMatcher.ParseDocMatchExpression(txtExpr, 0);

            // Get current caret position
            int curCaretPos = GetCaretPos(txtMatchExpression);

            // Clear visual rectangles
            ClearVisRectangles();

            // Generate the rich text to highlight string elements
            Paragraph para = new Paragraph();
            foreach (ExprParseTerm parseTerm in exprParseTermList)
            {
                Run txtRun = new Run(txtExpr.Substring(parseTerm.stPos, parseTerm.termLen));
                txtRun.Foreground = parseTerm.GetBrush();
                para.Inlines.Add(txtRun);
                // Check for location rectangle
                if (parseTerm.termType == ExprParseTerm.ExprParseTermType.exprTerm_Location)
                {
                    AddVisRectangle(txtExpr.Substring(parseTerm.stPos, parseTerm.termLen), parseTerm);
                }
            }

            // Switch to new doc contents
            bInTextChangedHandler = true;
            txtMatchExpression.Document.Blocks.Clear();
            txtMatchExpression.Document.Blocks.Add(para);
            SetCaretPos(txtMatchExpression, curCaretPos);
            bInTextChangedHandler = false;

            // Draw the location rectangles
            DrawVisRectangles();

            // Redisplay the check status for the currently displayed document
            CheckDisplayedDocForMatchAndShowResult();
        }

        private void UpdateExprBasedOnRectangleChange()
        {
            // Find the rectangle that has changed/been-created
            foreach (UIElement child in docOverlayCanvas.Children)
            {
                if (child.GetType() == typeof(Rectangle))
                {
                    Rectangle rect = (Rectangle)child;
                    if (rect.Name == _dragSelectionRectName)
                    {
                        // Get coords from rectangle
                        double topLeftX = (double)rect.GetValue(Canvas.LeftProperty);
                        double topLeftY = (double)rect.GetValue(Canvas.TopProperty);
                        double width = rect.Width;
                        double height = rect.Height;

                        // Convert coords to image
                        DocRectangle docRectPercent = ConvertCanvasRectToDocPercent(new DocRectangle(topLeftX, topLeftY, width, height));
                        Console.WriteLine("DocRectPerc " + docRectPercent.X + " " +
                                    docRectPercent.Y + " " +
                                    docRectPercent.BottomRightX + " " +
                                    docRectPercent.BottomRightY);

                        // Insert/change location in expression
                        string[] rectParts = _dragSelectionRectName.Split('_');
                        if (rectParts.Length < 2)
                            return;
                        int rectIdx = Convert.ToInt32(rectParts[1]);

                        // Extract string
                        string txtExpr = GetMatchExprFromEditBox(); 

                        // Parse using our grammar
                        List<ExprParseTerm> exprParseTermList = _docTypesMatcher.ParseDocMatchExpression(txtExpr, 0);

                        // Get current caret position
                        int curCaretPos = GetCaretPos(txtMatchExpression);

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
                                if (parseTerm.locationBracketIdx == rectIdx)
                                {
                                    newTextExpr = txtExpr.Substring(0, parseTerm.stPos) + FormatLocationStr(docRectPercent) + txtExpr.Substring(parseTerm.stPos + parseTerm.termLen);
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
                            newTextExpr = txtExpr.Substring(0, bestNewRectPos) + "{" + FormatLocationStr(docRectPercent) + "}";
                            string endOfStr = txtExpr.Substring(bestNewRectPos);
                            if (endOfStr.Trim().Length > 0)
                            {
                                if (endOfStr.Trim().Substring(0, 1) == "{")
                                {
                                    int closePos = endOfStr.IndexOf('}');
                                    if (closePos > 0)
                                        endOfStr = endOfStr.Substring(closePos+1);
                                }
                            }
                            newTextExpr += endOfStr;
                            SetTxtMatchExprBoxText(newTextExpr);
                        }

                        // All rectangles will get redrawn as text expression is changed and causes trigger to refresh
                        SetTxtMatchExprBoxText(newTextExpr);

                        break;
                    }
                }
            }
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

            if (e.ChangedButton == MouseButton.Left)
            {
                _dragSelectActive = true;

                // Handle different kinds of moving/changing/creating new rectangles depending on where user clicked
                _dragSelectFromPoint = e.GetPosition(exampleFileImage);
                if (sender.GetType() == typeof(Rectangle))
                {
                    if (e.GetPosition((Rectangle)sender).X < 20 && e.GetPosition((Rectangle)sender).Y < 20)
                    {
                        _dragFrom = DRAG_FROM.TOPLEFT;
                    }
                    else if ((e.GetPosition((Rectangle)sender).X > ((Rectangle)sender).Width - 20) &&
                                (e.GetPosition((Rectangle)sender).Y > ((Rectangle)sender).Height - 20))
                    {
                        _dragFrom = DRAG_FROM.BOTTOMRIGHT;
                    }
                    else
                    {
                        _dragFrom = DRAG_FROM.CENTRE;
                    }
                    _dragSelectionRectName = ((Rectangle)sender).Name;
                }
                else
                {
                    _dragFrom = DRAG_FROM.NEW;
                    _dragSelectionRectName = "";
                }

                // Capture mouse
                exampleFileImage.CaptureMouse();
                e.Handled = true;
            }
        }

        private void exampleFileImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragSelectOverThreshold)
            {
                Point curMouseDownPoint = e.GetPosition(exampleFileImage);
                RubberbandRect(_dragSelectFromPoint, curMouseDownPoint, _dragSelectionRectName, false);
                e.Handled = true;
            }
            else if (_dragSelectActive)
            {
                Point curMouseDownPoint = e.GetPosition(exampleFileImage);
                var dragDelta = curMouseDownPoint - _dragSelectFromPoint;
                double dragDistance = Math.Abs(dragDelta.Length);
                if (dragDistance > DragThreshold)
                {
                    //
                    // When the mouse has been dragged more than the threshold value commence drag selection.
                    //
                    _dragSelectOverThreshold = true;
                    RubberbandRect(_dragSelectFromPoint, curMouseDownPoint, _dragSelectionRectName, true);
                }
                e.Handled = true;
            }
            else
            {
                bool bToolTipSet = false;
                if (_curDocDisplay_docCompareResult != null)
                    if ((_curDocDisplay_pageNum > 0) && (_curDocDisplay_pageNum <= _curDocDisplay_docCompareResult.scanPages.scanPagesText.Count))
                    {
                        Point curMousePoint = e.GetPosition(docOverlayCanvas);
                        if (!exampleFileImageToolTip.IsOpen)
                            exampleFileImageToolTip.IsOpen = true;
                        exampleFileImageToolTip.HorizontalOffset = curMousePoint.X - 100;
                        exampleFileImageToolTip.VerticalOffset = curMousePoint.Y;
                        DocRectangle docCoords = ConvertCanvasRectToDocPercent(new DocRectangle(curMousePoint.X, curMousePoint.Y, 0, 0));
                        List<ScanTextElem> scanTextElems = _curDocDisplay_docCompareResult.scanPages.scanPagesText[_curDocDisplay_pageNum-1];
                        foreach (ScanTextElem el in scanTextElems)
                            if (el.bounds.Intersects(docCoords))
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
                e.Handled = true;
            }
        }

        private void exampleFileImage_MouseLeave(object sender, MouseEventArgs e)
        {
            Point curMouseDownPoint = e.GetPosition(exampleFileImage);
            if (curMouseDownPoint.X < 0 || curMouseDownPoint.X > exampleFileImage.ActualWidth)
                exampleFileImageToolTip.IsOpen = false;
            else if (curMouseDownPoint.Y < 0 || curMouseDownPoint.Y > exampleFileImage.ActualHeight)
                exampleFileImageToolTip.IsOpen = false;
            e.Handled = true;
        }

        private void exampleFileImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (_dragSelectOverThreshold)
                {
                    //
                    // Drag selection has ended, apply the 'selection rectangle'.
                    //
                    _dragSelectOverThreshold = false;
                    UpdateExprBasedOnRectangleChange();
                    e.Handled = true;
                }

                if (_dragSelectActive)
                {
                    _dragSelectActive = false;
                    exampleFileImage.ReleaseMouseCapture();
                    e.Handled = true;
                }
            }
        }

        private void RubberbandRect(Point pt1, Point pt2, string rectName, bool firstTime)
        {
            double topLeftX = 0;
            double topLeftY = 0;
            double width = 0;
            double height = 0;

            // Bounding checks
            if (pt2.X > exampleFileImage.ActualWidth)
                pt2.X = exampleFileImage.ActualWidth - 1;
            if (pt2.Y > exampleFileImage.ActualHeight)
                pt2.Y = exampleFileImage.ActualHeight - 1;
            if (pt2.X < 0)
                pt2.X = 0;
            if (pt2.Y < 0)
                pt2.Y = 0;

            // Convert to canvas coords
            Point mousePt = exampleFileImage.TranslatePoint(pt2, docOverlayCanvas);
            Point initialPt = exampleFileImage.TranslatePoint(pt1, docOverlayCanvas);

            // If we're creating a new rectangle
            if ((_dragFrom == DRAG_FROM.NEW) && (firstTime))
            {
                // Rectangle size doesn't matter initially as it will be changed on subsequent mouse moves
                DocRectangle canvasRect = new DocRectangle(0, 0, 1, 1);
                _dragSelectionRectName = AddVisRectToCanvas(canvasRect, ExprParseTerm.GetBrushForLocationIdx(_dragSelect_nextLocationIdx), _dragSelect_nextLocationIdx);
                _dragSelectOppositeCorner = new Point(mousePt.X, mousePt.Y);
                return;
            }

            // Move vis rect
            foreach (UIElement child in docOverlayCanvas.Children)
            {
                if (child.GetType() == typeof(Rectangle))
                {
                    Rectangle rect = (Rectangle)child;
                    if (rect.Name == rectName)
                    {
                        if (firstTime)
                        {
                            if (_dragFrom == DRAG_FROM.TOPLEFT)
                                _dragSelectOppositeCorner = new Point(((double)(rect.GetValue(Canvas.LeftProperty))) + rect.Width,
                                            ((double)(rect.GetValue(Canvas.TopProperty))) + rect.Height);
                            else if ((_dragFrom == DRAG_FROM.BOTTOMRIGHT) || (_dragFrom == DRAG_FROM.CENTRE))
                                _dragSelectOppositeCorner = new Point((double)rect.GetValue(Canvas.LeftProperty),
                                            (double)rect.GetValue(Canvas.TopProperty));
                        }
                        else
                        {
                            if ((_dragFrom == DRAG_FROM.TOPLEFT) || (_dragFrom == DRAG_FROM.BOTTOMRIGHT) || (_dragFrom == DRAG_FROM.NEW))
                            {
                                topLeftX = Math.Min(mousePt.X, _dragSelectOppositeCorner.X);
                                topLeftY = Math.Min(mousePt.Y, _dragSelectOppositeCorner.Y);
                                width = Math.Abs(mousePt.X - _dragSelectOppositeCorner.X);
                                height = Math.Abs(mousePt.Y - _dragSelectOppositeCorner.Y);
                                rect.SetValue(Canvas.LeftProperty, topLeftX);
                                rect.SetValue(Canvas.TopProperty, topLeftY);
                                rect.Width = width;
                                rect.Height = height;
                            }
                            else if (_dragFrom == DRAG_FROM.CENTRE)
                            {
                                topLeftX = _dragSelectOppositeCorner.X + (mousePt.X - initialPt.X);
                                topLeftY = _dragSelectOppositeCorner.Y + (mousePt.Y - initialPt.Y);
                                rect.SetValue(Canvas.LeftProperty, topLeftX);
                                rect.SetValue(Canvas.TopProperty, topLeftY);
                            }
                        }
                        break;
                    }
                }
            }
        }

        #endregion

        #region Location Rectangle Display

        private void ClearVisRectangles()
        {
            _visMatchRectangles.Clear();
        }

        private void AddVisRectangle(string rectLocStr, ExprParseTerm parseTerm)
        {
            VisRect visRect = new VisRect();
            visRect.docRectPercent = new DocRectangle(rectLocStr);
            visRect.parseTerm = parseTerm;
            _visMatchRectangles.Add(visRect);
        }

        private void DrawVisRectangles()
        {
            docOverlayCanvas.Children.Clear();
            if (exampleFileImage.ActualHeight <= 0 || double.IsNaN(exampleFileImage.ActualHeight))
                return;
            foreach (VisRect visRect in _visMatchRectangles)
            {
                visRect.rectName = AddVisRectToCanvas(visRect.docRectPercent, visRect.parseTerm.GetBrush(), visRect.parseTerm.locationBracketIdx);
                if (_dragSelect_nextLocationIdx <= visRect.parseTerm.locationBracketIdx)
                    _dragSelect_nextLocationIdx = visRect.parseTerm.locationBracketIdx + 1;
            }
        }

        private DocRectangle ConvertDocPercentRectToCanvas(DocRectangle docPercentRect)
        {
            double tlx = exampleFileImage.ActualWidth * docPercentRect.X / 100;
            double tly = exampleFileImage.ActualHeight * docPercentRect.Y / 100;
            Point tlPoint = exampleFileImage.TranslatePoint(new Point(tlx, tly), docOverlayCanvas);
            double wid = exampleFileImage.ActualWidth * docPercentRect.Width / 100;
            double hig = exampleFileImage.ActualHeight * docPercentRect.Height / 100;
            Point brPoint = exampleFileImage.TranslatePoint(new Point(tlx + wid, tly + hig), docOverlayCanvas);
            return new DocRectangle(tlPoint.X, tlPoint.Y, brPoint.X - tlPoint.X, brPoint.Y - tlPoint.Y);
        }

        private DocRectangle ConvertCanvasRectToDocPercent(DocRectangle canvasRect)
        {
            Point tlPoint = docOverlayCanvas.TranslatePoint(new Point(canvasRect.X, canvasRect.Y), exampleFileImage);
            double tlx = 100 * tlPoint.X / exampleFileImage.ActualWidth;
            double tly = 100 * tlPoint.Y / exampleFileImage.ActualHeight;
            Point brPoint = docOverlayCanvas.TranslatePoint(new Point(canvasRect.BottomRightX, canvasRect.BottomRightY), exampleFileImage);
            double brx = 100 * brPoint.X / exampleFileImage.ActualWidth;
            double bry = 100 * brPoint.Y / exampleFileImage.ActualHeight;
            return new DocRectangle(tlx, tly, brx - tlx, bry - tly);
        }

        private string AddVisRectToCanvas(DocRectangle docRectPercent, Brush brushForPaint, int locationBracketIdx)
        {
            Rectangle rect = new Rectangle();
            rect.Opacity = 0.5;
            rect.Fill = brushForPaint;
            DocRectangle canvasRect = ConvertDocPercentRectToCanvas(docRectPercent);
            rect.Width = canvasRect.Width;
            rect.Height = canvasRect.Height;
            rect.Name = "visRect_" + locationBracketIdx.ToString();
            docOverlayCanvas.Children.Add(rect);
            rect.SetValue(Canvas.LeftProperty, canvasRect.X);
            rect.SetValue(Canvas.TopProperty, canvasRect.Y);
            rect.MouseDown += new MouseButtonEventHandler(exampleFileImage_MouseDown);
            rect.MouseMove += new MouseEventHandler(exampleFileImage_MouseMove);
            rect.MouseUp += new MouseButtonEventHandler(exampleFileImage_MouseUp);
            return rect.Name;
        }

        #endregion

        #region Form events and buttons

        private void docOverlayCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawVisRectangles();
        }

        private void btnEditDocType_Click(object sender, RoutedEventArgs e)
        {
            btnEditDocType.IsEnabled = false;
            btnRenameDocType.IsEnabled = false;
            btnNewDocType.IsEnabled = false;
            chkEnabledDocType.IsEnabled = true;
            txtMatchExpression.IsEnabled = true;
            btnCancelTypeChanges.IsEnabled = true;
        }

        private void btnRenameDocType_Click(object sender, RoutedEventArgs e)
        {
            btnEditDocType.IsEnabled = false;
            btnRenameDocType.IsEnabled = false;
            btnNewDocType.IsEnabled = false;
            txtDocTypeName.IsEnabled = true;
            chkEnabledDocType.IsEnabled = true;
            txtMatchExpression.IsEnabled = true;
            btnCancelTypeChanges.IsEnabled = true;
        }

        private void btnNewDocType_Click(object sender, RoutedEventArgs e)
        {
            btnEditDocType.IsEnabled = false;
            btnRenameDocType.IsEnabled = false;
            btnNewDocType.IsEnabled = false;
            txtDocTypeName.IsEnabled = true;
            chkEnabledDocType.IsEnabled = true;
            txtMatchExpression.IsEnabled = true;
            _selectedDocType = null;
            docTypeListView.SelectedItem = null;
            txtDocTypeName.Text = "";
            chkEnabledDocType.IsChecked = true;
            SetTxtMatchExprBoxText("");
            btnCancelTypeChanges.IsEnabled = true;
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
                newDocType.matchExpression = GetMatchExprFromEditBox();
                newDocType.isEnabled = (bool)chkEnabledDocType.IsChecked;

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
                newDocType.matchExpression = GetMatchExprFromEditBox();
                newDocType.isEnabled = (bool)chkEnabledDocType.IsChecked;

                // Create the new record
                _docTypesMatcher.AddOrUpdateDocTypeRecInDb(newDocType);
            }
            else
            {
                // Make changes to record
                _selectedDocType.matchExpression = GetMatchExprFromEditBox();
                _selectedDocType.isEnabled = (bool)chkEnabledDocType.IsChecked;

                // Update the record
                _docTypesMatcher.AddOrUpdateDocTypeRecInDb(_selectedDocType);
            }

            // Ensure no longer able to save/cancel
            btnCancelTypeChanges.IsEnabled = false;
            btnSaveTypeChanges.IsEnabled = false;

            // Reload the form (selecting appropriate item)
            ShowDocTypeList(curDocTypeName);
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
            txtDocTypeName.IsEnabled = false;
            if (docType == null)
            {
                txtDocTypeName.Text = "";
                chkEnabledDocType.IsChecked = false;
                SetTxtMatchExprBoxText("");
            }
            else
            {
                txtDocTypeName.Text = docType.docTypeName;
                chkEnabledDocType.IsChecked = docType.isEnabled;
                SetTxtMatchExprBoxText(docType.matchExpression);
            }
        }

        private bool AreDocTypeChangesPendingSaveOrCancel()
        {
            return btnSaveTypeChanges.IsEnabled;
        }

        private void UpdateUIForDocTypeChanges()
        {
            bool somethingChanged = false;
            if (_selectedDocType != null)
            {
                string txtExpr = GetMatchExprFromEditBox();
                string compareStr = _selectedDocType.matchExpression;
                if (compareStr == null)
                    compareStr = "";
                bool matchExprChanged = (compareStr.Trim() != txtExpr.Trim());
                bool docTypeEnabledChanged = (_selectedDocType.isEnabled != chkEnabledDocType.IsChecked);
                bool docTypeRenamed = (_selectedDocType.docTypeName != txtDocTypeName.Text) && (txtDocTypeName.Text.Trim() != "");
                somethingChanged = (matchExprChanged || docTypeEnabledChanged || docTypeRenamed);
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

        #endregion

    }

    public class VisRect
    {
        public ExprParseTerm parseTerm;
        public DocRectangle docRectPercent;
        public string rectName;
        public Point BottomRightPoint()
        {
            return new Point(docRectPercent.BottomRightX, docRectPercent.BottomRightY);
        }
    }

    public class DocCompareRslt
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
}
