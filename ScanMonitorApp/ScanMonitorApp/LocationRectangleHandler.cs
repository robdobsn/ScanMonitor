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
        private bool _dragSelectActive = false;
        private Point _dragSelectFromPoint;
        private Point _dragSelectOppositeCorner;
        private int _dragSelect_nextLocationIdx = 0;
        private bool _selectionEnabled = false;
        private Image _masterUiElem;
        private Canvas _uiOverlayCanvas;
        private enum DRAG_FROM { NONE, CENTRE, TOPLEFT, BOTTOMRIGHT, NEW }
        private DRAG_FROM _dragFrom = DRAG_FROM.NONE;
        private string _dragSelectionRectName = "";
        bool _dragSelectOverThreshold = false;
        Action<Point> _tooltipMoveCallback;
        Action<Point> _tooltipCloseCallback;
        Action<string> _changesCompleteCallback;

        public LocationRectangleHandler(Image masterVisualElement, Canvas uiOverlayCanvas,
                                Action<Point> tooltipMoveCallback, Action<Point> tooltipCloseCallback,
                                Action<string> changesCompleteCallback)
        {
            _masterUiElem = masterVisualElement;
            _tooltipMoveCallback = tooltipMoveCallback;
            _tooltipCloseCallback = tooltipCloseCallback;
            _changesCompleteCallback = changesCompleteCallback;
            _uiOverlayCanvas = uiOverlayCanvas;
        }

        public void SelectionEnable(bool bEnable)
        {
            _selectionEnabled = bEnable;
        }

        public void SetNextLocationIdx(int locIdx)
        {
            _dragSelect_nextLocationIdx = locIdx;
        }

        public void HandleMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Only start if document editing is enabled
            if (!_selectionEnabled)
                return;

            if (e.ChangedButton == MouseButton.Left)
            {
                _dragSelectActive = true;

                // Handle different kinds of moving/changing/creating new rectangles depending on where user clicked
                _dragSelectFromPoint = e.GetPosition(_masterUiElem);
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
                _masterUiElem.CaptureMouse();
                e.Handled = true;
            }
        }

        public void HandleMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragSelectOverThreshold)
            {
                Point curMouseDownPoint = e.GetPosition(_masterUiElem);
                RubberbandRect(_dragSelectFromPoint, curMouseDownPoint, _dragSelectionRectName, false);
                e.Handled = true;
            }
            else if (_dragSelectActive)
            {
                Point curMouseDownPoint = e.GetPosition(_masterUiElem);
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
                Point curMousePoint = e.GetPosition(_masterUiElem);
                _tooltipMoveCallback(curMousePoint);
                e.Handled = true;
            }
        }

        public void HandleMouseLeave(object sender, MouseEventArgs e)
        {
            Point curMouseDownPoint = e.GetPosition(_masterUiElem);
            _tooltipCloseCallback(curMouseDownPoint);
            e.Handled = true;
        }

        public void HandleMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (_dragSelectOverThreshold)
                {
                    //
                    // Drag selection has ended, apply the 'selection rectangle'.
                    //
                    _dragSelectOverThreshold = false;
                    _changesCompleteCallback(_dragSelectionRectName);
                    e.Handled = true;
                }

                if (_dragSelectActive)
                {
                    _dragSelectActive = false;
                    _masterUiElem.ReleaseMouseCapture();
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
            if (pt2.X > _masterUiElem.ActualWidth)
                pt2.X = _masterUiElem.ActualWidth - 1;
            if (pt2.Y > _masterUiElem.ActualHeight)
                pt2.Y = _masterUiElem.ActualHeight - 1;
            if (pt2.X < 0)
                pt2.X = 0;
            if (pt2.Y < 0)
                pt2.Y = 0;

            // Convert to canvas coords
            Point mousePt = _masterUiElem.TranslatePoint(pt2, _uiOverlayCanvas);
            Point initialPt = _masterUiElem.TranslatePoint(pt1, _uiOverlayCanvas);

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
            rect.Name = "visRect_" + locationBracketIdx.ToString();
            _uiOverlayCanvas.Children.Add(rect);
            rect.SetValue(Canvas.LeftProperty, size.X);
            rect.SetValue(Canvas.TopProperty, size.Y);
            rect.MouseDown += new MouseButtonEventHandler(HandleMouseDown);
            rect.MouseMove += new MouseEventHandler(HandleMouseMove);
            rect.MouseUp += new MouseButtonEventHandler(HandleMouseUp);
            return rect.Name;
        }

    }
}
