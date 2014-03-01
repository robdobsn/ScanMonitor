using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

        public DocTypeView(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher)
        {
            InitializeComponent();
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;

            // List view for comparisons
            listMatchResults.ItemsSource = _docCompareRslts;

            // Matcher thread
            _bwThread = new BackgroundWorker();
            _bwThread.WorkerSupportsCancellation = true;
            _bwThread.WorkerReportsProgress = true;
            _bwThread.DoWork += new DoWorkEventHandler(FindMatchingDocs_DoWork);
            _bwThread.ProgressChanged += new ProgressChangedEventHandler(FindMatchingDocs_ProgressChanged);
            _bwThread.RunWorkerCompleted += new RunWorkerCompletedEventHandler(FindMatchingDocs_RunWorkerCompleted);
        }

        public ObservableCollection<DocType> DocTypeColl
        {
            get { return _docTypeColl; }
        }

        public void ShowDocTypeList()
        {
            List<DocType> docTypes = _docTypesMatcher.ListDocTypes();
            _docTypeColl.Clear();
            foreach (DocType dt in docTypes)
                _docTypeColl.Add(dt);
            docTypeListView.ItemsSource = _docTypeColl;
        }

        private void docTypeListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                DocType selDocType = e.AddedItems[0] as DocType;
                _selectedDocType = selDocType;
                if (selDocType != null)
                {
                    txtDocTypeName.Text = selDocType.docTypeName;
                    SetTxtMatchExprBoxText(selDocType.matchExpression);
                }
            }
        }

        private void btnTestMatch_Click(object sender, RoutedEventArgs e)
        {
            if (_bwThread.IsBusy)
            {
                FindMatchingDocs_Stop();
            }
            else
            {
                if (_selectedDocType != null)
                {
                    FindMatchingDocs_Start();
                }
            }
        }

        private void FindMatchingDocs_Start()
        {
            if (!_bwThread.IsBusy)
            {
                string txtExpr = GetMatchExprFromEditBox();
                DocType chkDocType = new DocType();
                chkDocType.docTypeName = _selectedDocType.docTypeName;
                chkDocType.matchExpression = txtExpr;
                _bwThread.RunWorkerAsync(chkDocType);
                btnTestMatch.Content = "Stop Finding";
                lblMatchStatus.Content = "Working...";
                _docCompareRslts.Clear();
            }
        }

        private void FindMatchingDocs_DoWork(object sender, DoWorkEventArgs e)
        {
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

                ScanPages scanPages = _scanDocHandler.GetScanPages(fdi.uniqName);
                DocType docTypeToMatch = (DocType)e.Argument;
                if (_docTypesMatcher.CheckIfDocMatches(scanPages, docTypeToMatch).matchCertaintyPercent == 100)
                    this.Dispatcher.BeginInvoke((Action)delegate()
                    {
                        DocCompareRslt compRslt = new DocCompareRslt();
                        compRslt.uniqName = fdi.uniqName;
                        compRslt.docTypeFiled = fdi.docTypeFiled;
                        compRslt.typeMatchOk = (fdi.docTypeFiled == docTypeToMatch.docTypeName) ? "" : "NO";
                        _docCompareRslts.Add(compRslt);
                    });
                docIdx++;
                worker.ReportProgress((int)(docIdx * 100 / fdiList.Count));
            }
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
            {
                lblMatchStatus.Content = "Cancelled";
            }

            else if (!(e.Error == null))
            {
                lblMatchStatus.Content += ("Error: " + e.Error.Message);
            }

            else
            {
                lblMatchStatus.Content = "Finished";
            }
            btnTestMatch.Content = "Find Matches";
        }

        private void listMatchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                DocCompareRslt docCompRslt = e.AddedItems[0] as DocCompareRslt;
                if (docCompRslt != null)
                {
                    string uniqName = docCompRslt.uniqName;
                    string imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, uniqName, 1, false);
                    try
                    {
                        exampleFileImage.Source = new BitmapImage(new Uri("File:" + imgFileName));
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Loading bitmap file {0} excp {1}", imgFileName, excp.Message);
                    }
                }
            }
        }

        private void SetTxtMatchExprBoxText(string txtStr)
        {
            Paragraph para = new Paragraph(new Run(txtStr));
            txtMatchExpression.Document.Blocks.Clear();
            txtMatchExpression.Document.Blocks.Add(para);
        }

        private void dragSelectionCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
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

        private void dragSelectionCanvas_MouseMove(object sender, MouseEventArgs e)
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
        }

        private void dragSelectionCanvas_MouseUp(object sender, MouseButtonEventArgs e)
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
        }

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
                        Console.WriteLine("DocRectPerc " + docRectPercent.topLeftXPercent + " " +
                                    docRectPercent.topLeftYPercent + " " +
                                    docRectPercent.bottomRightXPercent + " " +
                                    docRectPercent.bottomRightYPercent);

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
            string st = String.Format("{0:0},{1:0},{2:0},{3:0}", docRect.topLeftXPercent, docRect.topLeftYPercent, docRect.widthPercent, docRect.heightPercent);
            return st;
        }

        private DocRectangle ConvertDocPercentRectToCanvas(DocRectangle docPercentRect)
        {
            double tlx = exampleFileImage.ActualWidth * docPercentRect.topLeftXPercent / 100;
            double tly = exampleFileImage.ActualHeight * docPercentRect.topLeftYPercent / 100;
            Point tlPoint = exampleFileImage.TranslatePoint(new Point(tlx, tly), docOverlayCanvas);
            double wid = exampleFileImage.ActualWidth * docPercentRect.widthPercent / 100;
            double hig = exampleFileImage.ActualHeight * docPercentRect.heightPercent / 100;
            Point brPoint = exampleFileImage.TranslatePoint(new Point(tlx + wid, tly + hig), docOverlayCanvas);
            return new DocRectangle(tlPoint.X, tlPoint.Y, brPoint.X - tlPoint.X, brPoint.Y - tlPoint.Y);
        }

        private DocRectangle ConvertCanvasRectToDocPercent(DocRectangle canvasRect)
        {
            Point tlPoint = docOverlayCanvas.TranslatePoint(new Point(canvasRect.topLeftXPercent, canvasRect.topLeftYPercent), exampleFileImage);
            double tlx = 100 * tlPoint.X / exampleFileImage.ActualWidth;
            double tly = 100 * tlPoint.Y / exampleFileImage.ActualHeight;
            Point brPoint = docOverlayCanvas.TranslatePoint(new Point(canvasRect.bottomRightXPercent, canvasRect.bottomRightYPercent), exampleFileImage);
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
            rect.Width = canvasRect.widthPercent;
            rect.Height = canvasRect.heightPercent;
            rect.Name = "visRect_" + locationBracketIdx.ToString();
            docOverlayCanvas.Children.Add(rect);
            rect.SetValue(Canvas.LeftProperty, canvasRect.topLeftXPercent);
            rect.SetValue(Canvas.TopProperty, canvasRect.topLeftYPercent);
            rect.MouseDown += new MouseButtonEventHandler(dragSelectionCanvas_MouseDown);
            rect.MouseMove += new MouseEventHandler(dragSelectionCanvas_MouseMove);
            rect.MouseUp += new MouseButtonEventHandler(dragSelectionCanvas_MouseUp);
            return rect.Name;
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
                DocRectangle canvasRect = new DocRectangle(0,0,1,1);
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

        private void RubberbandRect2(Point pt1, Point pt2, string rectName, bool firstTime)
        {
            // Find rect in list
            string[] rectParts = rectName.Split('_');
            if (rectParts.Length < 2)
                return;
            int rectIdx = Convert.ToInt32(rectParts[1]);
            if (rectIdx >= _visMatchRectangles.Count)
                return;
            VisRect visRect = _visMatchRectangles[rectIdx];

            // Convert to canvas coords
            //pt1 = exampleFileImage.TranslatePoint(pt1, docOverlayCanvas);
            //pt2 = exampleFileImage.TranslatePoint(pt2, docOverlayCanvas);

            if (_dragFrom == DRAG_FROM.TOPLEFT)
            {
                // Find top corner of new rect
                Point brPoint = _dragSelectOppositeCorner;
                Point mousePoint = new Point(pt2.X * 100 / exampleFileImage.ActualWidth, pt2.Y * 100 / exampleFileImage.ActualHeight);
                double topLeftX = Math.Min(brPoint.X, mousePoint.X);
                double topLeftY = Math.Min(brPoint.Y, mousePoint.Y);

                // Find width / height of new rect
                double width = Math.Abs(brPoint.X - mousePoint.X);
                double height = Math.Abs(brPoint.Y - mousePoint.Y);

                // Change rect info
                //Console.WriteLine("Mouse: " + mousePoint.X + " " + mousePoint.Y);
                //Console.WriteLine("OldVis: " + visRect.docRect.topLeftXPercent  + " " +
                //    visRect.docRect.topLeftYPercent + " " +
                //    visRect.docRect.bottomRightXPercent + " " +
                //    visRect.docRect.bottomRightYPercent);
                visRect.docRectPercent = new DocRectangle(topLeftX, topLeftY, width, height);
    //            Console.WriteLine("NewVis: " + topLeftX + " " +
    //topLeftY + " " +
    //(width + topLeftX) + " " +
    //(height+topLeftY));

          
            }

            // Move vis rect
            foreach (UIElement child in docOverlayCanvas.Children)
            {
                if (child.GetType() == typeof(Rectangle))
                {
                    Rectangle rect = (Rectangle)child;

                }
                    if (((Rectangle)child).Name == rectName)
                    {
                        
                        break;
                    }
            }
            

            // Redraw this rect
            string ell1Name = "ell1_" + rectParts[1];
            string ell2Name = "ell2_" + rectParts[1];
            foreach (UIElement child in docOverlayCanvas.Children)
            {
                if (child.GetType() == typeof(Rectangle))
                    if (((Rectangle)child).Name == rectName)
                    {
                        docOverlayCanvas.Children.Remove(child);
                        break;
                    }
            }
            foreach (UIElement child in docOverlayCanvas.Children)
            {
                if (child.GetType() == typeof(Ellipse))
                    if (((Ellipse)child).Name == ell1Name)
                    {
                        docOverlayCanvas.Children.Remove(child);
                        break;
                    }
            }
            foreach (UIElement child in docOverlayCanvas.Children)
            {
                if (child.GetType() == typeof(Ellipse))
                    if (((Ellipse)child).Name == ell2Name)
                    {
                        docOverlayCanvas.Children.Remove(child);
                        break;
                    }
            }
            AddVisRectToCanvas(visRect.docRectPercent, visRect.parseTerm.GetBrush(), visRect.parseTerm.locationBracketIdx);

        }

        private void docOverlayCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawVisRectangles();
        }
    }

    public class VisRect
    {
        public ExprParseTerm parseTerm;
        public DocRectangle docRectPercent;
        public string rectName;
        public Point BottomRightPoint()
        {
            return new Point(docRectPercent.bottomRightXPercent, docRectPercent.bottomRightYPercent);
        }
    }

    public class DocCompareRslt
    {
        public string uniqName { get; set; }
        public string typeMatchOk { get; set; }
        public string docTypeFiled { get; set; }
    }
}
