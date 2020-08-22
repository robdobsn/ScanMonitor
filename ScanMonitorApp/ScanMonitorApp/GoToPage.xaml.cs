using System;
using System.Collections.Generic;
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
using MahApps.Metro.Controls;

namespace ScanMonitorApp
{
    /// <summary>
    /// Interaction logic for GoToPage.xaml
    /// </summary>
    public partial class GoToPage : MetroWindow
    {
        public bool dlgResult = false;
        private UIElement _fromElem = null;
        private Window _fromWin = null;
        public int pageNum = 1;
        public GoToPage(int curPage, UIElement fromElem, Window fromWin)
        {
            InitializeComponent();
            txtBoxDocNum.Text = curPage.ToString();
            _fromElem = fromElem;
            _fromWin = fromWin;
            this.Owner = fromWin;
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            pageNum = Convert.ToInt32(txtBoxDocNum.Text);
            dlgResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            dlgResult = false;
            Close();
        }

        private void GoToPage1_Loaded(object sender, RoutedEventArgs e)
        {
            //if (_fromElem != null)
            //{
            //    var positionTransform = _fromElem.TransformToAncestor(_fromWin);
            //    var areaPosition = positionTransform.Transform(new Point(0, 0));
            //    Application curApp = Application.Current;
            //    Window mainWindow = curApp.MainWindow;
            //    this.Left = mainWindow.Left + areaPosition.X + 100 - this.ActualWidth;
            //    this.Top = mainWindow.Top + areaPosition.Y + 150;
            //}
            //else
            //{
            //    Left = _fromWin.Left + (_fromWin.ActualWidth - ActualWidth) / 2;
            //    Top = _fromWin.Top + (_fromWin.ActualHeight - ActualHeight) / 2;
            //}
        }

    }
}
