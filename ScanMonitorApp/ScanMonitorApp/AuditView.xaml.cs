using DnsClient;
using Microsoft.WindowsAPICodePack.Dialogs;
using MongoDB.Bson;
using MongoDB.Driver;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ServiceModel.Configuration;
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
using System.Text.RegularExpressions;

namespace ScanMonitorApp
{
    /// <summary>
    /// Interaction logic for AuditView.xaml
    /// </summary>
    public partial class AuditView : Window
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private ScanDocHandler _scanDocHandler;
        private DocTypesMatcher _docTypesMatcher;
        private BackgroundWorker _bwThreadForPopulateList;
        private BackgroundWorker _bwThreadForSearch;
        const int MAX_NUM_DOCS_TO_ADD_TO_LIST = 100;
        const string SRCH_DOC_TYPE_ANY = "<<<ANY>>>";
        private WindowClosingDelegate _windowClosingCB;
        private IFindFluent<ScanPages, ScanPages> _lastSearchResult = null;
        private int _resultListCurSkip = 0;
        private int _resultListNumToShow = MAX_NUM_DOCS_TO_ADD_TO_LIST;
        private int _resultListCount = 0;
        private AuditData _curDocDisplayed;
        private ScanDocAllInfo _curDocAllInfo;
        private int _curDocDisplayed_pageNum = 1;

        public AuditView(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher, WindowClosingDelegate windowClosingCB)
        {
            InitializeComponent();
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;
            _windowClosingCB = windowClosingCB;

            // List view for comparisons
            auditListView.ItemsSource = AuditDataColl;

            // Search thread
            _bwThreadForSearch = new BackgroundWorker();
            _bwThreadForSearch.WorkerSupportsCancellation = true;
            _bwThreadForSearch.WorkerReportsProgress = true;
            _bwThreadForSearch.DoWork += new DoWorkEventHandler(Search_DoWork);
            _bwThreadForSearch.RunWorkerCompleted += new RunWorkerCompletedEventHandler(Search_RunWorkerCompleted);

            // Populate list thread
            _bwThreadForPopulateList = new BackgroundWorker();
            _bwThreadForPopulateList.WorkerSupportsCancellation = true;
            _bwThreadForPopulateList.WorkerReportsProgress = true;
            _bwThreadForPopulateList.DoWork += new DoWorkEventHandler(PopulateList_DoWork);
            _bwThreadForPopulateList.ProgressChanged += new ProgressChangedEventHandler(PopulateList_ProgressChanged);
            _bwThreadForPopulateList.RunWorkerCompleted += new RunWorkerCompletedEventHandler(PopulateList_RunWorkerCompleted);
        }

        public ObservableCollection<AuditData> AuditDataColl { get; private set; } = new ObservableCollection<AuditData>();

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            lblDocTypeToSearchFor.Content = SRCH_DOC_TYPE_ANY;
            //this.WindowState = System.Windows.WindowState.Maximized;
            //if (!_bwThreadForPopulateList.IsBusy)
            //{
            //    btnGo.Content = "Stop";
            //    btnNext.IsEnabled = false;
            //    btnSearch.IsEnabled = false;
            //    object[] args = { GetListViewType(), 100000000, "", "" };
            //    _bwThreadForPopulateList.RunWorkerAsync(args);
            //}
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _windowClosingCB();
        }

        private void Search_DoWork(object sender, DoWorkEventArgs e)
        {
            e.Result = "";
            BackgroundWorker worker = sender as BackgroundWorker;

            // Extract args
            object[] args = (object[])e.Argument;
            string searchText = (string)args[0];
            bool ignoreCase = (bool)args[1];
            bool useRegex = (bool)args[2];

            // Form database query
            IMongoCollection<ScanPages> collection_spages = _scanDocHandler.GetDocPagesCollection();
            BsonDocument query = null;
            if (searchText.Trim().Length == 0)
            {
                query = new BsonDocument();
            }
            else if (useRegex)
            {
                query = new BsonDocument{{
                    "scanPagesText", new BsonDocument {{
                        "$elemMatch", new BsonDocument {{
                            "$elemMatch", new BsonDocument {{
                                "text", new BsonDocument {
                                    {
                                        "$regex", searchText
                                    },
                                    {
                                        "$options", ignoreCase ? "i" : ""
                                    }
                                }
                            }}
                        }}
                    }}
                }};
            }
            else
            {
                query = new BsonDocument{{
                    "scanPagesText", new BsonDocument {{
                        "$elemMatch", new BsonDocument {{
                            "$elemMatch", new BsonDocument {{
                                "text", new BsonDocument {{
                                    "$text", new BsonDocument {
                                        {
                                            "$search", searchText
                                        },
                                        {
                                            "$caseSensitive", !ignoreCase
                                        }
                                    }
                                }}
                            }}
                        }}
                    }}
                }};
                Console.WriteLine(query.ToJson());
            }

            // Execute query
            if (query != null)
            {
                try
                {
                    var foundScanDoc = collection_spages.Find(query);
                    e.Result = foundScanDoc;
                }
                catch (Exception excp)
                {
                    logger.Error("Failed to search {0}", excp.ToString());
                }
            }
        }

        private void Search_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // Handle results
            if (e.Error != null)
            {
                // Exception
                logger.Error("Search error {0}", e.Error.Message);
                progBar.Value = (0);
            }
            else if (e.Cancelled)
            {
                // Cancelled
                logger.Info("Search cancelled");
                progBar.Value = (0);
            }
            else
            {
                // Successful search
                var srchRslt = (IFindFluent<ScanPages, ScanPages>)e.Result;
                _resultListCount = (int)srchRslt.CountDocuments();
                lblListStatus.Content = _resultListCount.ToString() + " found";

                // Start list population
                _resultListCurSkip = 0;
                _resultListNumToShow = MAX_NUM_DOCS_TO_ADD_TO_LIST;
                _lastSearchResult = srchRslt;
                PopulateSearchList();
            }
        }

        private void PopulateList_DoWork(object sender, DoWorkEventArgs e)
        {
            e.Result = "";
            BackgroundWorker worker = sender as BackgroundWorker;

            // Extract args
            object[] args = (object[])e.Argument;
            IFindFluent<ScanPages, ScanPages> rsltCursor = (IFindFluent<ScanPages, ScanPages>)args[0];
            int numToSkip = (int)args[1];
            int numToShow = (int)args[2];

            // Get range for list box
            List<string> foundUniqNames = new List<string>();
            foreach (var findRslt in rsltCursor.Skip(numToSkip).Limit(numToShow).ToEnumerable<ScanPages>())
            {
                foundUniqNames.Add(findRslt.uniqName);
            }

            // Join
            try
            {

                var coll = _scanDocHandler.GetDocInfoCollection();
                var filedDocColl = _scanDocHandler.GetFiledDocsCollection();

                var result = coll.Aggregate()
                    .Lookup(
                        foreignCollection: filedDocColl,
                        localField: q => q.uniqName,
                        foreignField: f => f.uniqName,
                        @as: (ScanCombinedInfo eo) => eo.filedInfo
                    )
                    .Match(p => foundUniqNames.Contains(p.uniqName))
                    .Match(q => q.filedInfo.Any(r => r.filedAt_finalStatus == FiledDocInfo.DocFinalStatus.STATUS_DELETED))
                    //.Sort(new BsonDocument("other.name", -1))
                    .ToList();

                // Successful search
                this.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    progBar.Value = (70);
                });

                // Parse records
                List<AuditData> auditDataColl = new List<AuditData>();
                foreach (var rec in result)
                {
                    var auditData = new AuditData();

                    // Get uniqName
                    string uniqName = rec.uniqName;
                    auditData.UniqName = uniqName;

                    // Get filed info
                    if (rec.filedInfo.Count() > 0)
                    {
                        FiledDocInfo fdi = rec.filedInfo.First();
                        auditData.DocTypeFiledAs = fdi.filedAs_docType;
                        auditData.FinalStatus = FiledDocInfo.GetFinalStatusStr(fdi.filedAt_finalStatus);
                    }
                    else
                    {
                        auditData.FinalStatus = "Unfiled";
                    }
                    auditDataColl.Add(auditData);
                }

                // Result returned                
                e.Result = auditDataColl;
            }
            catch (Exception excp)
            {
                logger.Error("Failed to aggregate {0}", excp.ToString());
            }
        }

        private void PopulateList_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progBar.Value = (e.ProgressPercentage);
        }

        private void PopulateList_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // Handle results
            if (e.Error != null)
            {
                // Exception
                logger.Error("Populate error {0}", e.Error.Message);
                progBar.Value = (0);
            }
            else if (e.Cancelled)
            {
                // Cancelled
                logger.Info("Populate cancelled");
                progBar.Value = (0);
            }
            else
            {
                // Update status text
                int startDocNum = _resultListCurSkip + 1;
                int numDocsShown = MAX_NUM_DOCS_TO_ADD_TO_LIST;
                if (numDocsShown > _resultListCount)
                    numDocsShown = _resultListCount;
                int endDocNum = _resultListCurSkip + numDocsShown;
                lblListStatus.Content = _resultListCount.ToString() + " found";
                if (numDocsShown > 0)
                {
                    lblListStatus.Content += ", showing " + startDocNum.ToString() + " to " + endDocNum.ToString();
                }

                // List contents returned
                List<AuditData> rslt = (List<AuditData>)e.Result;
                AuditDataColl.Clear();
                foreach (var el in rslt)
                    AuditDataColl.Add(el);
            }
            progBar.Value = (100);
            EnableSearchButtons(true);
        }

        private void auditListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                AuditData selectedRow = e.AddedItems[0] as AuditData;
                if (selectedRow != null)
                {
                    _curDocDisplayed = selectedRow;
                    _curDocAllInfo = null;
                    DisplayDocPage(1);
                    return;
                }
            }
            ClearDocView();
        }

        private void ClearDocView()
        {
            _curDocDisplayed = null;
            _curDocAllInfo = null;
            auditFileImage.Source = null;
            btnOpenFiled.IsEnabled = false;
            txtScanDocInfo.Text = "";
            txtFiledDocInfo.Text = "";
            txtPageText.Text = "";
        }

        private void DisplayDocPage(int pageNum)
        {
            if (_curDocDisplayed == null)
            {
                ClearDocView();
                return;
            }
            string uniqName = _curDocDisplayed.UniqName;

            // Doc info
            _curDocAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
            if (!((pageNum >= 1) && (pageNum <= _curDocAllInfo.scanDocInfo.numPages)))
                return;

            btnOpenFiled.IsEnabled = (_curDocAllInfo.filedDocInfo != null) && (_curDocAllInfo.filedDocInfo.filedAt_finalStatus == FiledDocInfo.DocFinalStatus.STATUS_FILED);

            txtScanDocInfo.Text = ScanDocHandler.GetScanDocInfoText(_curDocAllInfo.scanDocInfo);
            txtScanDocInfo.IsReadOnly = true;

            txtFiledDocInfo.Text = ScanDocHandler.GetFiledDocInfoText(_curDocAllInfo.filedDocInfo);
            txtFiledDocInfo.IsReadOnly = true;

            _curDocDisplayed_pageNum = pageNum;
            txtPageNumber.Text = String.Format("Page {0} of {1}", pageNum, _curDocAllInfo.scanDocInfo.numPages);

            string pagesStr = "";
            if ((_curDocAllInfo.scanPages != null) && (pageNum >= 1) && (_curDocAllInfo.scanPages.scanPagesText.Count >= pageNum))
            {
                var pageElems = _curDocAllInfo.scanPages.scanPagesText.ElementAt(pageNum - 1);
                foreach (ScanTextElem el in pageElems)
                {
                    pagesStr += el.text + " ";
                }
            }
            txtPageText.Text = pagesStr;
            txtPageText.IsReadOnly = true;

            ShowSearchPosInText();

            string imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, uniqName, pageNum, false);
            if (!File.Exists(imgFileName))
                return;
            try
            {
                auditFileImage.Source = new BitmapImage(new Uri(imgFileName));
            }
            catch (Exception excp)
            {
                logger.Error("Loading bitmap file {0} excp {1}", imgFileName, excp.Message);
            }
        }

        private void ShowSearchPosInText()
        {
            // Check search type
            string srchText = txtSearch.Text;
            bool ignoreCase = chkBoxIgnoreCase.IsChecked ?? true;
            bool useRegex = chkBoxRegEx.IsChecked ?? false;
            if (srchText.Trim().Length > 0)
            {
                int findPos = -1;
                int matchLen = 0;
                if (!useRegex)
                {
                    findPos = txtPageText.Text.IndexOf(srchText, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                    matchLen = srchText.Length;
                }
                else
                {
                    var match = System.Text.RegularExpressions.Regex.Match(txtPageText.Text, srchText, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
                    if (match.Success)
                    {
                        findPos = match.Index;
                        matchLen = match.Length;
                    }
                }
                if (findPos >= 0)
                {
                    txtPageText.Select(findPos, matchLen);
                    int lineIdx = txtPageText.GetLineIndexFromCharacterIndex(findPos);
                    txtPageText.ScrollToLine(lineIdx);
                    txtPageText.Focus();
                    txtPageText.IsInactiveSelectionHighlightEnabled = true;
                }
            }
        }

        private string GetListViewType()
        {
            return ((ComboBoxItem)(comboListView.SelectedItem)).Tag.ToString();
        }

        private void btnOpenOrig_Click(object sender, RoutedEventArgs e)
        {
            AuditData selectedRow = auditListView.SelectedItem as AuditData;
            if (selectedRow != null)
            {
                string uniqName = selectedRow.UniqName;
                ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
                ScanDocHandler.ShowFileInExplorer(scanDocAllInfo.scanDocInfo.GetOrigFileNameWin());
            }
        }

        private void btnOpenArchive_Click(object sender, RoutedEventArgs e)
        {
            AuditData selectedRow = auditListView.SelectedItem as AuditData;
            if (selectedRow != null)
            {
                string uniqName = selectedRow.UniqName;
                //                string imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, uniqName, 1, false);
                string archiveFileName = ScanDocHandler.GetArchiveFileName(uniqName);
                try
                {
                    ScanDocHandler.ShowFileInExplorer(archiveFileName.Replace("/", @"\"));
                }
                finally
                {

                }
            }
        }

        private void btnOpenFiled_Click(object sender, RoutedEventArgs e)
        {
            AuditData selectedRow = auditListView.SelectedItem as AuditData;
            if (selectedRow != null)
            {
                string uniqName = selectedRow.UniqName;
                ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
                if (scanDocAllInfo.filedDocInfo != null)
                    ScanDocHandler.ShowFileInExplorer(scanDocAllInfo.filedDocInfo.filedAs_pathAndFileName.Replace("/", @"\"));
            }
        }

        private void btnSearch_Click(object sender, RoutedEventArgs e)
        {
            // Start search
            StartSearch();

            //int startVal = 100000000;
            //string srchText = txtSearch.Text;
            //string srchDocType = lblDocTypeToSearchFor.Content.ToString();
            //if (!_bwThreadForPopulateList.IsBusy)
            //{
            //    btnGo.Content = "Stop";
            //    btnNext.IsEnabled = false;
            //    btnSearch.IsEnabled = false;
            //    object[] args = { GetListViewType(), startVal, srchText, srchDocType };
            //    _bwThreadForPopulateList.RunWorkerAsync(args);
            //}
        }

        private void listViewCtxtLocate_Click(object sender, RoutedEventArgs e)
        {
            AuditData selectedRow = auditListView.SelectedItem as AuditData;
            if (selectedRow != null)
            {
                string uniqName = selectedRow.UniqName;
                ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(uniqName);
                if (scanDocAllInfo.filedDocInfo != null)
                {
                    CommonOpenFileDialog cofd = new CommonOpenFileDialog("Locate the file");
                    cofd.Multiselect = false;
                    cofd.InitialDirectory = System.IO.Path.GetDirectoryName(scanDocAllInfo.filedDocInfo.filedAs_pathAndFileName);
                    cofd.Filters.Add(new CommonFileDialogFilter("Any", ".*"));
                    CommonFileDialogResult result = cofd.ShowDialog(this);
                    if (result == CommonFileDialogResult.Ok)
                    {
                        // Re-file at the new location
                        scanDocAllInfo.filedDocInfo.filedAs_pathAndFileName = cofd.FileName;
                        _scanDocHandler.AddOrUpdateFiledDocRecInDb(scanDocAllInfo.filedDocInfo);
                        selectedRow.MovedToFileName = "MOVEDTO " + cofd.FileName;

                        // Check if archived file is present
                        string archiveFileName = ScanDocHandler.GetArchiveFileName(scanDocAllInfo.scanDocInfo.uniqName);
                        if (!File.Exists(archiveFileName))
                        {
                            string tmpStr = "";
                            ScanDocHandler.CopyFile(cofd.FileName, archiveFileName, ref tmpStr);
                            selectedRow.MovedToFileName = "ARCVD & " + selectedRow.MovedToFileName;
                        }
                    }
                }
            }
        }

        //private void auditListView_Loaded(object sender, RoutedEventArgs e)
        //{
        //    List<ScanDocInfo> sdiList = _scanDocHandler.GetListOfScanDocs();
        //    if (sdiList.Count() > 0)
        //    {
        //        Binding bind = new Binding();
        //        auditListView.DataContext = sdiList;
        //        auditListView.SetBinding(ListView.ItemsSourceProperty, bind);
        //    }
        //}

        private void btnDocTypeSel_Click(object sender, RoutedEventArgs e)
        {
            // Clear menu
            btnDocTypeSelContextMenu.Items.Clear();

            // Reload menu
            const int MAX_MENU_LEVELS = 6;
            List<string> docTypeStrings = new List<string>();
            List<DocType> docTypeList = _docTypesMatcher.ListDocTypes();
            foreach (DocType dt in docTypeList)
            {
                if (!dt.isEnabled)
                    continue;
                docTypeStrings.Add(dt.docTypeName);
            }
            docTypeStrings.Sort();
            docTypeStrings.Insert(0, SRCH_DOC_TYPE_ANY);
            string[] prevMenuStrs = null;
            MenuItem[] prevMenuItems = new MenuItem[MAX_MENU_LEVELS];
            foreach (string docTypeString in docTypeStrings)
            {
                string[] elemsS = docTypeString.Split(new string[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                if ((elemsS.Length <= 0) || (elemsS.Length > MAX_MENU_LEVELS))
                    continue;
                for (int i = 0; i < elemsS.Length; i++)
                    elemsS[i] = elemsS[i].Trim();
                int reqMenuLev = -1;
                for (int menuLev = 0; menuLev < elemsS.Length; menuLev++)
                {
                    if ((prevMenuStrs == null) || (menuLev >= prevMenuStrs.Length))
                    {
                        reqMenuLev = menuLev;
                        break;
                    }
                    if (prevMenuStrs[menuLev] != elemsS[menuLev])
                    {
                        reqMenuLev = menuLev;
                        break;
                    }
                }
                // Check if identical to previous type - discard if so
                if (reqMenuLev == -1)
                    continue;
                // Create new menu items
                for (int createLev = reqMenuLev; createLev < elemsS.Length; createLev++)
                {
                    MenuItem newMenuItem = new MenuItem();
                    newMenuItem.Header = elemsS[createLev];
                    if (createLev == elemsS.Length - 1)
                    {
                        newMenuItem.Tag = docTypeString;
                        newMenuItem.Click += DocTypeSubMenuItem_Click;
                        newMenuItem.MouseDoubleClick += DocTypeSubMenuItem_DoubleClick;
                    }
                    // Add the menu item to the appropriate level
                    if (createLev == 0)
                        btnDocTypeSelContextMenu.Items.Add(newMenuItem);
                    else
                        prevMenuItems[createLev - 1].Items.Add(newMenuItem);
                    prevMenuItems[createLev] = newMenuItem;
                }
                // Update lists
                prevMenuStrs = elemsS;
            }
            // Show menu
            if (!btnDocTypeSel.ContextMenu.IsOpen)
                btnDocTypeSel.ContextMenu.IsOpen = true;
        }

        private void DocTypeSubMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MenuItem menuItem = sender as MenuItem;
            // Ignore if not tip of menu hierarchy
            if (menuItem.HasItems)
                return;
            btnDocTypeSel.ContextMenu.IsOpen = false;
            object tag = menuItem.Tag;
            if (tag.GetType() == typeof(string))
                lblDocTypeToSearchFor.Content = (string)tag;
        }

        private void DocTypeSubMenuItem_DoubleClick(object sender, RoutedEventArgs e)
        {
            MenuItem menuItem = sender as MenuItem;
            btnDocTypeSel.ContextMenu.IsOpen = false;
            object tag = menuItem.Tag;
            if (tag.GetType() == typeof(string))
                lblDocTypeToSearchFor.Content = (string)tag;
        }

        private void auditFileImage_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void btnListFirst_Click(object sender, RoutedEventArgs e)
        {
            if (_resultListCurSkip == 0)
                return;
            _resultListCurSkip = 0;
            _resultListNumToShow = MAX_NUM_DOCS_TO_ADD_TO_LIST;
            PopulateSearchList();
        }

        private void btnListLast_Click(object sender, RoutedEventArgs e)
        {
            int curSkip = _resultListCount - MAX_NUM_DOCS_TO_ADD_TO_LIST;
            if (curSkip < 0)
                curSkip = 0;
            if (curSkip == _resultListCurSkip)
                return;
            _resultListCurSkip = curSkip;
            _resultListNumToShow = MAX_NUM_DOCS_TO_ADD_TO_LIST;
            PopulateSearchList();
        }

        private void btnListPrev_Click(object sender, RoutedEventArgs e)
        {
            int curSkip = _resultListCurSkip - MAX_NUM_DOCS_TO_ADD_TO_LIST;
            if (curSkip < 0)
                curSkip = 0;
            if (curSkip == _resultListCurSkip)
                return;
            _resultListCurSkip = curSkip;
            _resultListNumToShow = MAX_NUM_DOCS_TO_ADD_TO_LIST;
            PopulateSearchList();
        }

        private void btnListNext_Click(object sender, RoutedEventArgs e)
        {
            int curSkip = _resultListCurSkip + MAX_NUM_DOCS_TO_ADD_TO_LIST;
            if (_resultListCount - curSkip < MAX_NUM_DOCS_TO_ADD_TO_LIST)
                curSkip = _resultListCount - MAX_NUM_DOCS_TO_ADD_TO_LIST;
            if (curSkip < 0)
                curSkip = 0;
            if (curSkip == _resultListCurSkip)
                return;
            _resultListCurSkip = curSkip;
            _resultListNumToShow = MAX_NUM_DOCS_TO_ADD_TO_LIST;
            PopulateSearchList();
        }

        private void txtSearch_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Start search
                StartSearch();
            }
        }

        private void StartSearch()
        {
            string srchText = txtSearch.Text;
            if (!_bwThreadForSearch.IsBusy)
            {
                EnableSearchButtons(false);
                bool ignoreCase = chkBoxIgnoreCase.IsChecked ?? true;
                bool useRegex = chkBoxRegEx.IsChecked ?? false;
                object[] args = { srchText, ignoreCase, useRegex };
                _bwThreadForSearch.RunWorkerAsync(args);
            }
        }

        private void EnableSearchButtons(bool en)
        {
            listNavGrid.IsEnabled = en;
            btnSearch.IsEnabled = en;
        }

        private void PopulateSearchList()
        {
            if (_bwThreadForPopulateList.IsBusy)
            {
                _bwThreadForPopulateList.CancelAsync();
                // Allow worker to stop
                Thread.Sleep(100);
            }

            // Start populating list
            string srchText = txtSearch.Text;
            if (!_bwThreadForPopulateList.IsBusy)
            {
                object[] args = { _lastSearchResult, _resultListCurSkip, _resultListNumToShow };
                _bwThreadForPopulateList.RunWorkerAsync(args);
            }
            progBar.Value = (30);
        }

        private void btnPageNext_Click(object sender, RoutedEventArgs e)
        {
            DisplayDocPage(_curDocDisplayed_pageNum + 1);
        }

        private void btnPagePrev_Click(object sender, RoutedEventArgs e)
        {
            DisplayDocPage(_curDocDisplayed_pageNum - 1);
        }

        private void FindPageWithSearchTextAndShow()
        {
            // Check doc info
            if (_curDocAllInfo == null)
                return;

            int pageNum = 0;
            string srchText = txtSearch.Text;
            if (srchText.Trim().Length == 0)
                return;
            bool ignoreCase = chkBoxIgnoreCase.IsChecked ?? true;
            foreach (var pageElems in _curDocAllInfo.scanPages.scanPagesText)
            {
                pageNum++;
                string pageStr = "";
                foreach (ScanTextElem el in pageElems)
                {
                    pageStr += el.text + " ";
                }
                int findPos = -1;
                int matchLen = 0;
                var match = System.Text.RegularExpressions.Regex.Match(pageStr, srchText, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
                if (match.Success)
                {
                    findPos = match.Index;
                    matchLen = match.Length;
                }
                if (findPos >= 0)
                {
                    DisplayDocPage(pageNum);
                    break;
                }
            }
        }

        private void btnFindText_Click(object sender, RoutedEventArgs e)
        {
            FindPageWithSearchTextAndShow();
        }
    }

    public class AuditData
    {
        public string UniqName { get; set; }
        public string FinalStatus { get; set; }
        public string FiledDocPresent { get; set; }
        public string MovedToFileName { get; set; }
        public string DocTypeFiledAs { get; set; }
    }

}
