using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScanMonitorApp
{
    public class LocationRectangleHandler
    {
        private static readonly double DragThreshold = 5;
        List<VisRect> _visMatchRectangles = new List<VisRect>();
        private bool _dragSelectActive = false;
        private Point _dragSelectFromPoint;
        private Point _dragSelectOppositeCorner;
        private int _dragSelect_nextLocationIdx = 0;
        private bool _selectionEnabled = false;
        private Image _masterImage;
        private Canvas _uiOverlayCanvas;
        private enum DRAG_FROM { NONE, CENTRE, TOPLEFT, BOTTOMRIGHT, NEW }
        private DRAG_FROM _dragFrom = DRAG_FROM.NONE;
        private string _dragSelectionRectName = "";
        bool _dragSelectOverThreshold = false;
        Action<Point, DocRectangle> _tooltipMoveCallback;
        Action<Point> _tooltipCloseCallback;
        Action<string, int, DocRectangle> _changesCompleteCallback;

        public LocationRectangleHandler(Image masterVisualElement, Canvas uiOverlayCanvas,
                                Action<Point, DocRectangle> tooltipMoveCallback, Action<Point> tooltipCloseCallback,
                                Action<string, int, DocRectangle> changesCompleteCallback)
        {
            _masterImage = masterVisualElement;
            _tooltipMoveCallback = tooltipMoveCallback;
            _tooltipCloseCallback = tooltipCloseCallback;
            _changesCompleteCallback = changesCompleteCallback;
            _uiOverlayCanvas = uiOverlayCanvas;
        }

        public void SelectionEnable(bool bEnable)
        {
            _selectionEnabled = bEnable;
        }

        #region Location Rectangle Display

        public void ClearVisRectangles()
        {
            _visMatchRectangles.Clear();
        }

        public void AddVisRectangle(string rectLocStr, string txtBoxCorresponding, int bracketIdx, Brush colour)
        {
            VisRect visRect = new VisRect();
            visRect.docRectPercent = new DocRectangle(rectLocStr);
            visRect.locationBracketIdx = bracketIdx;
            visRect.matchingTextBox = txtBoxCorresponding;
            visRect.rectColour = colour;
            _visMatchRectangles.Add(visRect);
        }

        public void DrawVisRectangles()
        {
            _uiOverlayCanvas.Children.Clear();
            if (_masterImage.ActualHeight <= 0 || double.IsNaN(_masterImage.ActualHeight))
                return;
            int nextLocationIdx = 0;
            foreach (VisRect visRect in _visMatchRectangles)
            {
                visRect.rectName = AddVisRectToCanvas(visRect);
                nextLocationIdx++;
            }
            _dragSelect_nextLocationIdx = nextLocationIdx;
        }

        private DocRectangle ConvertDocPercentRectToCanvas(DocRectangle docPercentRect)
        {
            double tlx = _masterImage.ActualWidth * docPercentRect.X / 100;
            double tly = _masterImage.ActualHeight * docPercentRect.Y / 100;
            Point tlPoint = _masterImage.TranslatePoint(new Point(tlx, tly), _uiOverlayCanvas);
            double wid = _masterImage.ActualWidth * docPercentRect.Width / 100;
            double hig = _masterImage.ActualHeight * docPercentRect.Height / 100;
            Point brPoint = _masterImage.TranslatePoint(new Point(tlx + wid, tly + hig), _uiOverlayCanvas);
            return new DocRectangle(tlPoint.X, tlPoint.Y, brPoint.X - tlPoint.X, brPoint.Y - tlPoint.Y);
        }

        private DocRectangle ConvertCanvasRectToDocPercent(DocRectangle canvasRect)
        {
            Point tlPoint = _uiOverlayCanvas.TranslatePoint(new Point(canvasRect.X, canvasRect.Y), _masterImage);
            double tlx = 100 * tlPoint.X / _masterImage.ActualWidth;
            double tly = 100 * tlPoint.Y / _masterImage.ActualHeight;
            Point brPoint = _uiOverlayCanvas.TranslatePoint(new Point(canvasRect.BottomRightX, canvasRect.BottomRightY), _masterImage);
            double brx = 100 * brPoint.X / _masterImage.ActualWidth;
            double bry = 100 * brPoint.Y / _masterImage.ActualHeight;
            return new DocRectangle(tlx, tly, brx - tlx, bry - tly);
        }

        private string AddVisRectToCanvas(VisRect visRect)
        {
            Rectangle rect = new Rectangle();
            rect.Opacity = 0.5;
            rect.Fill = visRect.rectColour;
            DocRectangle canvasRect = ConvertDocPercentRectToCanvas(visRect.docRectPercent);
            rect.Width = canvasRect.Width;
            rect.Height = canvasRect.Height;
            rect.Name = visRect.matchingTextBox + "_" + visRect.locationBracketIdx.ToString();
            _uiOverlayCanvas.Children.Add(rect);
            rect.SetValue(Canvas.LeftProperty, canvasRect.X);
            rect.SetValue(Canvas.TopProperty, canvasRect.Y);
            rect.MouseDown += new MouseButtonEventHandler(HandleMouseDown);
            rect.MouseMove += new MouseEventHandler(HandleMouseMove);
            rect.MouseUp += new MouseButtonEventHandler(HandleMouseUp);
            return rect.Name;
        }

        #endregion

        #region Mouse handling

        public void HandleMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Only start if document editing is enabled
            if (!_selectionEnabled)
                return;

            if (e.ChangedButton == MouseButton.Left)
            {
                _dragSelectActive = true;

                // Handle different kinds of moving/changing/creating new rectangles depending on where user clicked
                _dragSelectFromPoint = e.GetPosition(_masterImage);
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
                _masterImage.CaptureMouse();
                e.Handled = true;
            }
        }

        public void HandleMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragSelectOverThreshold)
            {
                Point curMouseDownPoint = e.GetPosition(_masterImage);
                RubberbandRect(_dragSelectFromPoint, curMouseDownPoint, _dragSelectionRectName, false);
                e.Handled = true;
            }
            else if (_dragSelectActive)
            {
                Point curMouseDownPoint = e.GetPosition(_masterImage);
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
                Point curMousePoint = e.GetPosition(_masterImage);
                Point ptOnCanvas = _masterImage.TranslatePoint(curMousePoint, _uiOverlayCanvas);
                DocRectangle docCoords = ConvertCanvasRectToDocPercent(new DocRectangle(ptOnCanvas.X, ptOnCanvas.Y, 0, 0));                
                _tooltipMoveCallback(curMousePoint, docCoords);
                e.Handled = true;
            }
        }

        public void HandleMouseLeave(object sender, MouseEventArgs e)
        {
            Point curMouseDownPoint = e.GetPosition(_masterImage);
            _tooltipCloseCallback(curMouseDownPoint);
            e.Handled = true;
        }

        public void HandleMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (_dragSelectOverThreshold)
                {
                    // Drag selection complete - find the rectangle that has been changed/created
                    _dragSelectOverThreshold = false;
                    Rectangle rect = null;
                    foreach (UIElement child in _uiOverlayCanvas.Children)
                        if (child.GetType() == typeof(Rectangle))
                            if (((Rectangle)child).Name == _dragSelectionRectName)
                            {
                                rect = (Rectangle)child;
                                break;
                            }
                    // Handle the change
                    if (rect != null)
                    {
                        // Get coords from rectangle
                        double topLeftX = (double)rect.GetValue(Canvas.LeftProperty);
                        double topLeftY = (double)rect.GetValue(Canvas.TopProperty);
                        double width = rect.Width;
                        double height = rect.Height;

                        // Convert coords to image
                        DocRectangle docRectPercent = ConvertCanvasRectToDocPercent(new DocRectangle(topLeftX, topLeftY, width, height));
                        //Console.WriteLine("DocRectPerc " + docRectPercent.X + " " +
                        //            docRectPercent.Y + " " +
                        //            docRectPercent.BottomRightX + " " +
                        //            docRectPercent.BottomRightY);

                        // Insert/change location in expression
                        string[] rectParts = _dragSelectionRectName.Split('_');
                        if (rectParts.Length < 2)
                            return;
                        string matchingTextBox = rectParts[0];
                        int rectIdx = Convert.ToInt32(rectParts[1]);
                        _changesCompleteCallback(matchingTextBox, rectIdx, docRectPercent);
                    }
                    e.Handled = true;
                }

                if (_dragSelectActive)
                {
                    _dragSelectActive = false;
                    _masterImage.ReleaseMouseCapture();
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
            if (pt2.X > _masterImage.ActualWidth)
                pt2.X = _masterImage.ActualWidth - 1;
            if (pt2.Y > _masterImage.ActualHeight)
                pt2.Y = _masterImage.ActualHeight - 1;
            if (pt2.X < 0)
                pt2.X = 0;
            if (pt2.Y < 0)
                pt2.Y = 0;

            // Convert to canvas coords
            Point mousePt = _masterImage.TranslatePoint(pt2, _uiOverlayCanvas);
            Point initialPt = _masterImage.TranslatePoint(pt1, _uiOverlayCanvas);

            // If we're creating a new rectangle
            if ((_dragFrom == DRAG_FROM.NEW) && (firstTime))
            {
                // Rectangle size doesn't matter initially as it will be changed on subsequent mouse moves
                Rect rect = new Rect(initialPt, mousePt);
                _dragSelectionRectName = CreateNewCanvasRect(rect, ExprParseTerm.GetBrushForLocationIdx(_dragSelect_nextLocationIdx), _dragSelect_nextLocationIdx);
                _dragSelectOppositeCorner = new Point(mousePt.X, mousePt.Y);
                return;
            }

            // Move vis rect
            foreach (UIElement child in _uiOverlayCanvas.Children)
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

        private string CreateNewCanvasRect(Rect size, Brush brushForPaint, int locationBracketIdx)
        {
            Rectangle rect = new Rectangle();
            rect.Opacity = 0.5;
            rect.Fill = brushForPaint;
            rect.Width = size.Width;
            rect.Height = size.Height;
            rect.Name = "New_" + locationBracketIdx.ToString();
            _uiOverlayCanvas.Children.Add(rect);
            rect.SetValue(Canvas.LeftProperty, size.X);
            rect.SetValue(Canvas.TopProperty, size.Y);
            rect.MouseDown += new MouseButtonEventHandler(HandleMouseDown);
            rect.MouseMove += new MouseEventHandler(HandleMouseMove);
            rect.MouseUp += new MouseButtonEventHandler(HandleMouseUp);
            return rect.Name;
        }

        #endregion


        public class VisRect
        {
            public Brush rectColour;
            public string matchingTextBox;
            public int locationBracketIdx;
            public DocRectangle docRectPercent;
            public string rectName;
            public Point BottomRightPoint()
            {
                return new Point(docRectPercent.BottomRightX, docRectPercent.BottomRightY);
            }
        }
    }
}
