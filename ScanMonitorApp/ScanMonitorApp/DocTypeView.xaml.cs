﻿using NLog;
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
        private static readonly double DragThreshold = 5;
        private bool bInTextChangedHandler = false;

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
                worker.ReportProgress((int) (docIdx * 100 / fdiList.Count));
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
                //this.CaptureMouse();
                e.Handled = true;
            }
        }

        private void dragSelectionCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragSelectOverThreshold)
            {
                Point curMouseDownPoint = e.GetPosition(exampleFileImage);
                DrawOverlayRect(_dragSelectFromPoint, curMouseDownPoint);
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
                    InitDragSelectionRect(_dragSelectFromPoint, curMouseDownPoint);
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
                    //this.ReleaseMouseCapture();
                    e.Handled = true;
                }
            }
        }

        private void InitDragSelectionRect(Point pt1, Point pt2)
        {
            DrawOverlayRect(pt1, pt2);
            dragSelectionBorder.Visibility = Visibility.Visible;
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
            Canvas.SetLeft(dragSelectionBorder, topLeftX);
            Canvas.SetTop(dragSelectionBorder, topLeftY);
            dragSelectionBorder.Width = width;
            dragSelectionBorder.Height = height;
        }

        private void exampleFileImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
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

            // Generate the rich text to highlight string elements
            Paragraph para = new Paragraph();
            foreach (DocTypesMatcher.ExprParseTerm parseTerm in exprParseTermList)
            {
                Run txtRun = new Run(txtExpr.Substring(parseTerm.stPos, parseTerm.termLen));
                txtRun.Foreground = parseTerm.GetColour();
                para.Inlines.Add(txtRun);
                //TextRange range = new TextRange(m_tags[i].StartPosition, m_tags[i].EndPosition);
                //range.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Blue));
                //range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
            }

            bInTextChangedHandler = true;
            txtMatchExpression.Document.Blocks.Clear();
            txtMatchExpression.Document.Blocks.Add(para);
            SetCaretPos(txtMatchExpression, curCaretPos);
            bInTextChangedHandler = false;
        }

    }

    public class DocCompareRslt
    {
        public string uniqName { get; set; }
        public string typeMatchOk { get; set; }
        public string docTypeFiled { get; set; }
    }
}