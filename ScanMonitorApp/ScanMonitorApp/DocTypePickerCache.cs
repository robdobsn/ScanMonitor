using iTextSharp.text;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ScanMonitorApp
{
    class DocTypePickerCache
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private DocTypesMatcher _docTypesMatcher;
        private BackgroundWorker _bwThread;
        ObservableCollection<DocTypeCacheEntry> _thumbnailsOfDocTypes = new ObservableCollection<DocTypeCacheEntry>();
        private Window _parentWindow;

        public ObservableCollection<DocTypeCacheEntry> GetThumbnailCollection() { return _thumbnailsOfDocTypes; }

        public DocTypePickerCache(DocTypesMatcher docTypesMatcher, Window parentWindow)
        {
            _docTypesMatcher = docTypesMatcher;
            _parentWindow = parentWindow;
        }

        public void Start()
        {
            // Image filler thread
            _bwThread = new BackgroundWorker();
            _bwThread.WorkerSupportsCancellation = true;
            _bwThread.WorkerReportsProgress = true;
            _bwThread.DoWork += new DoWorkEventHandler(AddImages_DoWork);

            // Use a background worker to populate
            _bwThread.RunWorkerAsync();
        }

        private void AddImages_DoWork(object sender, DoWorkEventArgs e)
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
                    BitmapImage bitmap = DocTypeDisplayHelper.LoadDocThumbnail(dt.thumbnailForDocType, thumbnailHeight);
                    DocTypeCacheEntry2 ce = new DocTypeCacheEntry2();
                    ce.ThumbUniqName = dt.thumbnailForDocType;
                    ce.ThumbBitmap = bitmap;
                    _parentWindow.Dispatcher.BeginInvoke((Action)delegate()
                    {
                        //_thumbnailsOfDocTypes.Add(ce);
                    });

                    //img.Tag = dt.docTypeName;
                    //lock (_thumbnailsOfDocTypes)
                    //{
                    //    _thumbnailsOfDocTypes.Add(img);
                    //}
                }
            }
        }
    }

    class DocTypeCacheEntry2 : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private string _thumbnailUniqName;
        public string ThumbUniqName 
        {
            get { return _thumbnailUniqName; }
            set { _thumbnailUniqName = value; NotifyPropertyChanged("ThumbUniqName"); }
        }
        private BitmapImage _thumbnailBitmap;
        public BitmapImage ThumbBitmap
        {
            get { return _thumbnailBitmap; }
            set { _thumbnailBitmap = value; NotifyPropertyChanged("ThumbBitmap"); }
        }

    }
}
