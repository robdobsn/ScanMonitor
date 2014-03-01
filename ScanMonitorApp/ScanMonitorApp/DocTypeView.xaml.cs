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
        bool _dragFromTopLeft = false;
        Point _dragSelectOppositeCorner;
        private static readonly double DragThreshold = 5;
        private bool bInTextChangedHandler = false;
        List<VisRect> _visMatchRectangles = new List<VisRect>();
        private const int TOP_ELLIPSE_OFFSET = 6;
        private const int BOTTOM_ELLIPSE_OFFSET = 5;

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
                    Paragraph para = new Paragraph(new Run(selDocType.matchExpression));
                    txtMatchExpression.Document.Blocks.Clear();
                    txtMatchExpression.Document.Blocks.Add(para);
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
                _bwThread.RunWorkerAsync(_selectedDocType);
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

        private void dragSelectionCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _dragSelectActive = true;
                _dragSelectFromPoint = e.GetPosition(exampleFileImage);
                if (sender.GetType() == typeof(Rectangle))
                {
                    if (e.GetPosition((Rectangle)sender).X < 20 && e.GetPosition((Rectangle)sender).Y < 20)
                    {
                        _dragFromTopLeft = true;
                        _dragSelectionRectName = ((Rectangle)sender).Name;
                    }
                    else if ((e.GetPosition((Rectangle)sender).X > ((Rectangle)sender).Width - 20) &&
                                (e.GetPosition((Rectangle)sender).Y > ((Rectangle)sender).Height - 20))
                    {
                        _dragFromTopLeft = false;
                        _dragSelectionRectName = ((Rectangle)sender).Name;
                    }
                    else
                    {
                        _dragSelectionRectName = "";
                    }
                }
                else
                {
                    _dragSelectionRectName = "";
                }

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
                    //                    ApplyDragSelectionRect();
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

        private void InitDragSelectionRect(Point pt1, Point pt2)
        {
            DrawOverlayRect(pt1, pt2);
        }

        private void DrawOverlayRect(Point pt1, Point pt2)
        {
            // Convert to canvas coords
            pt1 = exampleFileImage.TranslatePoint(pt1, docOverlayCanvas);
            pt2 = exampleFileImage.TranslatePoint(pt2, docOverlayCanvas);

            // Find top corner
            double topLeftX = Math.Min(pt1.X, pt2.X);
            double topLeftY = Math.Min(pt1.Y, pt2.Y);
            double width = Math.Abs(pt1.X - pt2.X);
            double height = Math.Abs(pt1.Y - pt2.Y);

            txtDebug.Text = topLeftX.ToString() + ", " + topLeftY.ToString() + ", " + width.ToString() + ", " + height.ToString();

            // Draw rect
            //Canvas.SetLeft(dragSelectionBorder, topLeftX);
            //Canvas.SetTop(dragSelectionBorder, topLeftY);
            //dragSelectionBorder.Width = width;
            //dragSelectionBorder.Height = height;
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

        private void txtMatchExpression_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Avoid re-entering when we change the text programmatically
            if (bInTextChangedHandler)
                return;

            // Check the richtextbox is valid
            if (txtMatchExpression.Document == null)
                return;

            // Extract string
            string txtExpr = new TextRange(txtMatchExpression.Document.ContentStart, txtMatchExpression.Document.ContentEnd).Text;
            txtExpr = txtExpr.Replace("\r", "");
            txtExpr = txtExpr.Replace("\n", "");

            // Get current caret position
            int curCaretPos = GetCaretPos(txtMatchExpression);
            //            TextPointer curCaretPos = txtMatchExpression.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);

            // Parse using our grammar
            List<DocTypesMatcher.ExprParseTerm> exprParseTermList = _docTypesMatcher.ParseDocMatchExpression(txtExpr, 0);

            // Clear visual rectangles
            ClearVisRectangles();

            // Generate the rich text to highlight string elements
            Paragraph para = new Paragraph();
            foreach (DocTypesMatcher.ExprParseTerm parseTerm in exprParseTermList)
            {
                Run txtRun = new Run(txtExpr.Substring(parseTerm.stPos, parseTerm.termLen));
                txtRun.Foreground = parseTerm.GetColour();
                para.Inlines.Add(txtRun);
                // Check for location rectangle
                if (parseTerm.termType == DocTypesMatcher.ExprParseTerm.ExprParseTermType.exprTerm_Location)
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

            DrawVisRectangles();
        }

        private void ClearVisRectangles()
        {
            _visMatchRectangles.Clear();
        }

        private void AddVisRectangle(string rectLocStr, DocTypesMatcher.ExprParseTerm parseTerm)
        {
            VisRect visRect = new VisRect();
            visRect.docRect = new DocRectangle(rectLocStr);
            visRect.parseTerm = parseTerm;
            _visMatchRectangles.Add(visRect);
        }

        private void DrawVisRectangles()
        {
            docOverlayCanvas.Children.Clear();
            if (exampleFileImage.ActualHeight <= 0 || double.IsNaN(exampleFileImage.ActualHeight))
                return;
            foreach (VisRect visRect in _visMatchRectangles)
                DrawVisRect(visRect);
        }

        private DocRectangle ConvertImgRectToCanvas(DocRectangle imgRect)
        {
            double tlx = exampleFileImage.ActualWidth * imgRect.topLeftXPercent / 100;
            double tly = exampleFileImage.ActualHeight * imgRect.topLeftYPercent / 100;
            Point tlPoint = exampleFileImage.TranslatePoint(new Point(tlx, tly), docOverlayCanvas);
            double wid = exampleFileImage.ActualWidth * imgRect.widthPercent / 100;
            double hig = exampleFileImage.ActualHeight * imgRect.heightPercent / 100;
            Point brPoint = exampleFileImage.TranslatePoint(new Point(tlx + wid, tly + hig), docOverlayCanvas);
            return new DocRectangle(tlPoint.X, tlPoint.Y, brPoint.X - tlPoint.X, brPoint.Y - tlPoint.Y);
        }

        private void DrawVisRect(VisRect visRect)
        {
            Rectangle rect = new Rectangle();
            rect.Opacity = 0.5;
            rect.Fill = visRect.parseTerm.GetColour();
            DocRectangle canvasRect = ConvertImgRectToCanvas(visRect.docRect);
            rect.Width = canvasRect.widthPercent;
            rect.Height = canvasRect.heightPercent;
            rect.Name = "visRect_" + visRect.parseTerm.locationBracketIdx.ToString();
            docOverlayCanvas.Children.Add(rect);
            rect.SetValue(Canvas.LeftProperty, canvasRect.topLeftXPercent);
            rect.SetValue(Canvas.TopProperty, canvasRect.topLeftYPercent);
/*            Ellipse ell1 = new Ellipse();
            ell1.Opacity = 0.75;
            ell1.Stroke = visRect.parseTerm.GetColour();
            ell1.StrokeThickness = 2;
            ell1.Width = 10;
            ell1.Height = 10;
            ell1.Name = "ell1_" + visRect.parseTerm.locationBracketIdx.ToString();
            docOverlayCanvas.Children.Add(ell1);
            ell1.SetValue(Canvas.LeftProperty, canvasRect.topLeftXPercent - TOP_ELLIPSE_OFFSET);
            ell1.SetValue(Canvas.TopProperty, canvasRect.topLeftYPercent - TOP_ELLIPSE_OFFSET);
            Ellipse ell2 = new Ellipse();
            ell2.Opacity = 0.75;
            ell2.Stroke = visRect.parseTerm.GetColour();
            ell2.StrokeThickness = 2;
            ell2.Width = 10;
            ell2.Height = 10;
            ell2.Name = "ell2_" + visRect.parseTerm.locationBracketIdx.ToString();
            docOverlayCanvas.Children.Add(ell2);
            ell2.SetValue(Canvas.LeftProperty, canvasRect.bottomRightXPercent - BOTTOM_ELLIPSE_OFFSET);
            ell2.SetValue(Canvas.TopProperty, canvasRect.bottomRightYPercent - BOTTOM_ELLIPSE_OFFSET);
 */ 
            rect.MouseDown += new MouseButtonEventHandler(dragSelectionCanvas_MouseDown);
            rect.MouseMove += new MouseEventHandler(dragSelectionCanvas_MouseMove);
            rect.MouseUp += new MouseButtonEventHandler(dragSelectionCanvas_MouseUp);
            //                rect.AddHandler(UIElement.MouseDownEvent, visRectDragHandler_MouseDown);
        }

        private void RubberbandRect(Point pt1, Point pt2, string rectName, bool firstTime)
        {
            double topLeftX = 0;
            double topLeftY = 0;
            double width = 0;
            double height = 0;
            string[] rectParts = rectName.Split('_');
            if (rectParts.Length < 2)
                return;
//            string ell1Name = "ell1_" + rectParts[1];
//            string ell2Name = "ell2_" + rectParts[1];
            if (pt2.X > exampleFileImage.ActualWidth)
                pt2.X = exampleFileImage.ActualWidth - 1;
            if (pt2.Y > exampleFileImage.ActualHeight)
                pt2.Y = exampleFileImage.ActualHeight - 1;
            if (pt2.X < 0)
                pt2.X = 0;
            if (pt2.Y < 0)
                pt2.Y = 0;
            Point mousePt = exampleFileImage.TranslatePoint(pt2, docOverlayCanvas);

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
                            _dragSelectOppositeCorner = new Point(_dragFromTopLeft ? ((double)(rect.GetValue(Canvas.LeftProperty))) + rect.Width : (double)rect.GetValue(Canvas.LeftProperty),
                                            _dragFromTopLeft ? ((double)(rect.GetValue(Canvas.TopProperty))) + rect.Height : (double)rect.GetValue(Canvas.TopProperty));
                        }
                        else
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
                        break;
                    }
                }
            }
/*            foreach (UIElement child in docOverlayCanvas.Children)
            {
                if (child.GetType() == typeof(Ellipse))
                {
                    Ellipse ellip = (Ellipse)child;
                    if (ellip.Name == ell1Name)
                    {
                        ellip.SetValue(Canvas.LeftProperty, topLeftX-TOP_ELLIPSE_OFFSET);
                        ellip.SetValue(Canvas.TopProperty, topLeftY-TOP_ELLIPSE_OFFSET);
                    }
                    else if (ellip.Name == ell2Name)
                    {
                        ellip.SetValue(Canvas.LeftProperty, topLeftX + width - BOTTOM_ELLIPSE_OFFSET);
                        ellip.SetValue(Canvas.TopProperty, topLeftY + height - BOTTOM_ELLIPSE_OFFSET);
                    }
                }
            }
 * */
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
            if (firstTime)
            {
                _dragSelectOppositeCorner = new Point(_dragFromTopLeft ? visRect.docRect.bottomRightXPercent : visRect.docRect.topLeftXPercent,
                                _dragFromTopLeft ? visRect.docRect.bottomRightYPercent : visRect.docRect.topLeftYPercent);
            }

            // Convert to canvas coords
            //pt1 = exampleFileImage.TranslatePoint(pt1, docOverlayCanvas);
            //pt2 = exampleFileImage.TranslatePoint(pt2, docOverlayCanvas);

            if (_dragFromTopLeft)
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
                visRect.docRect = new DocRectangle(topLeftX, topLeftY, width, height);
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
            DrawVisRect(visRect);

        }

        private void docOverlayCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawVisRectangles();
        }
    }

    public class VisRect
    {
        public DocTypesMatcher.ExprParseTerm parseTerm;
        public DocRectangle docRect;
        public Point BottomRightPoint()
        {
            return new Point(docRect.bottomRightXPercent, docRect.bottomRightYPercent);
        }
    }

    public class DocCompareRslt
    {
        public string uniqName { get; set; }
        public string typeMatchOk { get; set; }
        public string docTypeFiled { get; set; }
    }
}
