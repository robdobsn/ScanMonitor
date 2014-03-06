using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
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

namespace ScanMonitorApp
{
    /// <summary>
    /// Interaction logic for DocTypePicker.xaml
    /// </summary>
    public partial class DocTypePicker : Window
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private static DocTypesMatcher _docTypesMatcher;
        private static BackgroundWorker _bwThread;
        public string ResultDocType { get; set; }
        private static List<Image> _thumbnailsOfDocTypes;

        public DocTypePicker(DocTypesMatcher docTypesMatcher)
        {
            InitializeComponent();
            ResultDocType = "";
            int thumbnailHeight = Properties.Settings.Default.PickThumbHeight;

            // Display images
            lock(_thumbnailsOfDocTypes)
            {
                foreach (Image img in _thumbnailsOfDocTypes)
                {
                    // Assume landscape paper shaped cells
                    int actualWidth = 400;
                    if (!double.IsNaN(gridOfImages.ActualWidth))
                        actualWidth = (int)gridOfImages.ActualWidth;
                    int numAcross = actualWidth / (int)(thumbnailHeight * 0.7);
                    gridOfImages.Columns = numAcross;
                    // Add the image
                    img.MouseDown += new MouseButtonEventHandler(HandleMouseDown);
                    gridOfImages.Children.Add(img);
                }
            }
        }

        public static void UpdateThumbnails(DocTypesMatcher docTypesMatcher)
        {
            // DocTypes
            _docTypesMatcher = docTypesMatcher;

            // Image filler thread
            _bwThread = new BackgroundWorker();
            _bwThread.WorkerSupportsCancellation = true;
            _bwThread.WorkerReportsProgress = true;
            _bwThread.DoWork += new DoWorkEventHandler(AddImages_DoWork);

            // Use a background worker to populate
            _bwThread.RunWorkerAsync();
        }

        private static void AddImages_DoWork(object sender, DoWorkEventArgs e)
        {
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

                if (dt.thumbnailForDocType != "")
                {
                    //Image img = new Image();
                    //img.Height = thumbnailHeight;
                    //BitmapImage bitmap = DocTypeDisplayHelper.LoadDocThumbnail(dt.thumbnailForDocType, thumbnailHeight);
                    //img.Tag = dt.docTypeName;
                    //img.Source = bitmap;
                    //lock (_thumbnailsOfDocTypes)
                    //{
                    //    _thumbnailsOfDocTypes.Add(img);
                    //}
                }
            }
        }

        private void HandleMouseDown(object sender, MouseButtonEventArgs e)
        {
            Image uiElem = (Image)sender;
            string docTypeName = (string)uiElem.Tag;
            e.Handled = true;
            ResultDocType = docTypeName;
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_bwThread.WorkerSupportsCancellation)
                _bwThread.CancelAsync();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            int thumbnailHeight = Properties.Settings.Default.PickThumbHeight;
            int actualWidth = 400;
            if (!double.IsNaN(gridOfImages.ActualWidth))
                actualWidth = (int)gridOfImages.ActualWidth;
            int numAcross = actualWidth / (int)(thumbnailHeight * 0.7);
            gridOfImages.Columns = numAcross;
        }
    }


}
