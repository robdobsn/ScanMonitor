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

namespace ScanMonitorApp
{
    /// <summary>
    /// Interaction logic for AuditView.xaml
    /// </summary>
    public partial class AuditView : Window
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        ObservableCollection<AuditData> _auditDataColl = new ObservableCollection<AuditData>();
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

        public AuditView(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher, WindowClosingDelegate windowClosingCB)
        {
            InitializeComponent();
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;
            _windowClosingCB = windowClosingCB;

            // List view for comparisons
            auditListView.ItemsSource = _auditDataColl;

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

        public ObservableCollection<AuditData> AuditDataColl
        {
            get { return _auditDataColl; }
        }

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

            // Execute database query
            IMongoCollection<ScanPages> collection_spages = _scanDocHandler.GetDocPagesCollection();
            ScanPages scanPages = null;
            //var builder = Builders<BsonDocument>.Filter;
            //var filter = builder.Eq("scanPagesText", new BsonDocument { { "$elemMatch", new BsonDocument { { "text", new BsonDocument { { "$regex", "BRADFORD" } } } } } });
            //var filter = builder.Eq("uniqName", "2013_04_15_16_17_13");
            //var foundScanPages = collection_spages.Find(filter);
            //if (foundScanPages.CountDocuments() > 0)
            //    scanPages = foundScanPages.First<ScanPages>();

            BsonDocument query = new BsonDocument{{
                "scanPagesText", new BsonDocument {{
                    "$elemMatch", new BsonDocument {{
                      "$elemMatch", new BsonDocument {{
                          "text", new BsonDocument {{
                              "$regex", searchText
                          }}
                      }}
                    }}
                }}
            }};
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
                _lastSearchResult = (IFindFluent<ScanPages, ScanPages>)e.Result;
                this.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    lblListStatus.Content = _lastSearchResult.CountDocuments().ToString() + " scans found";
                });

                // Start list population
                _resultListCurSkip = 0;
                _resultListNumToShow = MAX_NUM_DOCS_TO_ADD_TO_LIST;
                if (_bwThreadForPopulateList.IsBusy)
                {
                    _bwThreadForPopulateList.CancelAsync();
                    // Allow worker to stop
                    Thread.Sleep(1000);
                }

                // Clear list
                _auditDataColl.Clear();

                // Start search
                string srchText = txtSearch.Text;
                if (!_bwThreadForPopulateList.IsBusy)
                {
                    object[] args = { _lastSearchResult, _resultListCurSkip, _resultListNumToShow };
                    _bwThreadForPopulateList.RunWorkerAsync(args);
                }
                progBar.Value = (30);
            }
        }

        private void EnableSearchButtons(bool en)
        {
            listNavGrid.IsEnabled = en;
            btnSearch.IsEnabled = en;
        }

        public class ComboEntity
        {
            public ObjectId id;
            public string uniqName { get; set; }
            public int numPages { get; set; }
            public int numPagesWithText { get; set; }
            public DateTime createDate { get; set; }
            public string origFileName { get; set; }
            public bool flagForHelpFiling { get; set; }
            public IEnumerable<FiledDocInfo> filedInfo;
        };

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
                //Console.WriteLine(findRslt.uniqName);
                foundUniqNames.Add(findRslt.uniqName);
            }

            //var resultSet = new BsonDocument[]
            //{
            //    new BsonDocument{
            //        { "uniqName",
            //            new BsonDocument{
            //                {
            //                    "$in",
            //                    rsltCursor
            //                }
            //            }
            //        }
            //    },
            //};

            // Join
            try
            {

                var coll = _scanDocHandler.GetDocInfoCollection();
                var filedDocColl = _scanDocHandler.GetFiledDocsCollection();

                var result = coll.Aggregate()
                    .Match(p => foundUniqNames.Contains(p.uniqName))
                    .Lookup(
                        foreignCollection: filedDocColl,
                        localField: q => q.uniqName,
                        foreignField: f => f.uniqName,
                        @as: (ComboEntity eo) => eo.filedInfo
                    )
                    //.Project(p => new { p.id, p.name, other = p.others.First() })
                    //.Sort(new BsonDocument("other.name", -1))
                    .ToList();

                //var pipeline2 = new BsonDocument[]
                //{
                //        new BsonDocument{{ "$match",
                //            new BsonDocument{{
                //                    "uniqName", "2013_04_12_16_14_17"
                //                }}
                //            }}
                //};
                //var result2 = coll.Aggregate<BsonDocument>(pipeline2).ToList();
                //logger.Debug("pipe2 {0}", pipeline2.ToJson());
                //logger.Debug("result of aggregate2 {0}", result2.Count);

                //var pipeline = new BsonDocument[]
                //{
                //        new BsonDocument{
                //            { "$lookup",
                //                new BsonDocument{
                //                        { "from", "FiledDocInfo" },
                //                        { "localField", "uniqName" },
                //                        { "foreignField", "uniqName" },
                //                        { "as", "FiledInfo" }
                //                    }
                //            }
                //        },
                //        new BsonDocument{
                //            { "$limit", 10
                //            }
                //        }
                //};
                //var result = coll.Aggregate<BsonDocument>(pipeline).ToList();
                //logger.Debug("pipe {0}", pipeline.ToJson());
                //logger.Debug("result of aggregate {0}", result.Count);

                //var pipeline = new BsonDocument[]
                //{
                //        //new BsonDocument
                //        //{
                //        //    { "$match",
                //        //        new BsonDocument
                //        //        {
                //        //            { "$expr",
                //        //                new BsonDocument
                //        //                {
                //        //                    {
                //        //                        "$in",
                //        //                        new BsonDocument("uniqName", new BsonArray(foundUniqNames))
                //        //                    }
                //        //                }
                //        //            }
                //        //        }
                //        //    }
                //        //},
                //        new BsonDocument{
                //            { "$lookup",
                //                new BsonDocument{
                //                        { "from", "FiledDocInfo" },
                //                        { "localField", "uniqName" },
                //                        { "foreignField", "uniqName" },
                //                        { "as", "FiledInfo" }
                //                    }
                //            }
                //        },
                //        new BsonDocument{
                //            { "$skip", numToSkip
                //            }
                //        },
                //        new BsonDocument{
                //            { "$limit", numToShow
                //            }
                //        }
                //};
                //logger.Debug("pipe {0}", pipeline.ToJson());
                //var result = coll.Aggregate<BsonDocument>(pipeline).ToList();
                //logger.Debug("result of aggregate {0}", result.Count);

                e.Result = result;

                // Create 
                //var match = new BsonDocument
                //{
                //    {
                //        "$match",
                //        new BsonDocument
                //            {
                //                {"uniqName", "2013_04_12_16_14_17"}
                //            }
                //    }
                //};
                //var pipeline = new[] { match };
                //var result = coll.Aggregate<ScanDocInfo>(pipeline);

                //logger.Debug("result of aggregate {0}", result.ToString());
            }
            catch (Exception excp)
            {
                logger.Error("Failed to aggregate {0}", excp.ToString());
            }

            //IMongoCollection<ScanDocInfo> collection_sdinfo = _scanDocHandler.GetDocInfoCollection();
            //var foundScanDoc = collection_sdinfo.Find(a => a.uniqName == uniqName);


            //List<ScanDocInfo> sdiList = _scanDocHandler.GetListOfScanDocs();
            //this.Dispatcher.BeginInvoke((Action)delegate()
            //{
            //    lblTotalsInfo.Content = sdiList.Count().ToString() + " total docs";
            //});

            //object[] args = (object[])e.Argument;
            //string rsltFilter = (string)args[0];
            //int includeInListFromIdxNonInclusive = (int)args[1];
            //string srchText = (string)args[2];
            //string srchDocType = "";
            //if (args.Length > 3)
            //    srchDocType = (string)args[3];

            //// Start filling list from the end
            //int nEndIdx = includeInListFromIdxNonInclusive-1;
            //if (nEndIdx >= sdiList.Count - 1)
            //    nEndIdx = sdiList.Count - 1;
            //int numFound = 0;
            //int nDocIdx = nEndIdx;
            //while (nDocIdx >= 0)
            //{
            //    // Check for cancel
            //    if ((worker.CancellationPending == true))
            //    {
            //        e.Cancel = true;
            //        break;
            //    }

            //    // Update status
            //    if (srchText != "")
            //        worker.ReportProgress((int)((nEndIdx-nDocIdx) * 100 / nEndIdx));
            //    else
            //        worker.ReportProgress((int)(numFound * 100 / MAX_NUM_DOCS_TO_ADD_TO_LIST));

            //    // Get scan doc info and filed doc info
            //    ScanDocInfo sdi = sdiList[nDocIdx];
            //    FiledDocInfo fdi = _scanDocHandler.GetFiledDocInfo(sdi.uniqName);

            //    // Filter out records that aren't interesting
            //    bool bInclude = true;
            //    switch (rsltFilter)
            //    {
            //        case "filed":
            //        case "filedNotFound":
            //        case "filedAutoLocate":
            //        case "filedRemaining":
            //            if (fdi == null)
            //            {
            //                bInclude = false;
            //            }
            //            else
            //            {
            //                if ((rsltFilter != "filedRemaining") && (fdi.filedAt_finalStatus != FiledDocInfo.DocFinalStatus.STATUS_FILED))
            //                    bInclude = false;
            //            }
            //            break;
            //        default:
            //            break;
            //    }
            //    if (!bInclude)
            //    {
            //        nDocIdx--;
            //        continue;
            //    }

            //    // Check it contains required text - if requested
            //    if (srchText != "")
            //    {
            //        bInclude = false;
            //        if ((fdi != null) && (fdi.filedAs_pathAndFileName != null) && (fdi.filedAs_pathAndFileName.Trim() != ""))
            //        {
            //            if (System.IO.Path.GetFileName(fdi.filedAs_pathAndFileName).ToLower().Contains(srchText.ToLower()))
            //                bInclude = true;
            //        }
            //        if (!bInclude)
            //        {
            //            ScanPages scanPages = _scanDocHandler.GetScanPages(sdi.uniqName);
            //            if ((scanPages != null) && (scanPages.ContainText(srchText)))
            //                bInclude = true;
            //        }
            //        if (!bInclude)
            //        {
            //            nDocIdx--;
            //            continue;
            //        }
            //    }

            //    // Check it has the right type - if requested
            //    if (srchDocType != "" && srchDocType != SRCH_DOC_TYPE_ANY)
            //    {
            //        if ((fdi != null) && (fdi.filedAs_docType != srchDocType))
            //        {
            //            nDocIdx--;
            //            continue;
            //        }
            //    }

            //    // Check if we are looking for filedRemaining (i.e. it has been filed (or deleted) but the original still exists on the scan machine)
            //    if (rsltFilter == "filedRemaining")
            //    {
            //        if (!File.Exists(sdi.GetOrigFileNameWin()))
            //        {
            //            nDocIdx--;
            //            continue;
            //        }

            //    }

            //    // Show this record
            //    AuditData audDat = new AuditData();
            //    audDat.UniqName = sdi.uniqName;
            //    string statStr = "Unfiled";
            //    bool filedFileNotFound = false;
            //    if (fdi != null)
            //    {
            //        statStr = FiledDocInfo.GetFinalStatusStr(fdi.filedAt_finalStatus);
            //        statStr += " on " + fdi.filedAt_dateAndTime.ToShortDateString();
            //        // Check if the file is where it was placed
            //        if (fdi.filedAt_finalStatus == FiledDocInfo.DocFinalStatus.STATUS_FILED)
            //        {
            //            filedFileNotFound = true;
            //            try
            //            {
            //                if (File.Exists(fdi.filedAs_pathAndFileName))
            //                    filedFileNotFound = false;
            //            }
            //            catch
            //            {

            //            }
            //        }
            //    }

            //    // Check special result filters
            //    if ((fdi != null) && !filedFileNotFound && ((rsltFilter == "filedNotFound") || (rsltFilter == "filedAutoLocate")))
            //    {
            //        nDocIdx--;
            //        continue;
            //    }

            //    audDat.FinalStatus = statStr;
            //    audDat.FiledDocPresent = (fdi == null) ? "" : (filedFileNotFound ? "Not found" : "Ok");

            //    audDat.DocTypeFiledAs = (fdi == null) ? "" : fdi.filedAs_docType;

            //    // See if we can find a file which matches this one in the existing files database
            //    string movedToFileName = "";
            //    string archiveFileName = ScanDocHandler.GetArchiveFileName(sdi.uniqName);
            //    if ((filedFileNotFound) && (File.Exists(archiveFileName)))
            //    {
            //        long fileLen = 0;
            //        byte[] md5Val = ScanDocHandler.GenHashOnFileExcludingMetadata(archiveFileName, out fileLen);
            //        List<ExistingFileInfoRec> efirList = _scanDocHandler.FindExistingFileRecsByHash(md5Val);
            //        foreach (ExistingFileInfoRec efir in efirList)
            //        {
            //            // Check for a file length within 10% (metadata may have changed)
            //            if ((fileLen > efir.fileLength * 9 / 10) && (fileLen < efir.fileLength * 11 / 10))
            //            {
            //                movedToFileName = efir.filename;
            //                break;
            //            }
            //        }
            //    }
            //    audDat.MovedToFileName = movedToFileName;

            //    // Check for auto locate
            //    if ((fdi != null) && filedFileNotFound && (rsltFilter == "filedAutoLocate") && (movedToFileName != ""))
            //    {
            //        // Re-file at the new location
            //        fdi.filedAs_pathAndFileName = movedToFileName;
            //        _scanDocHandler.AddOrUpdateFiledDocRecInDb(fdi);
            //        audDat.MovedToFileName = "FIXED " + movedToFileName;
            //    }

            //    // Check for Filed-Remaining (i.e. was not removed from scanning computer)
            //    else if ((fdi != null) && (!filedFileNotFound) && (rsltFilter == "filedRemaining"))
            //    {
            //        //string rsltStr = ScanUtils.AttemptToDeleteFile(sdi.origFileName);
            //        //                    audDat.MovedToFileName = "Original file delete attempt " + rsltStr;
            //        if (sdi.GetOrigFileNameWin().ToLower().Contains("macallan"))
            //        {
            //            audDat.MovedToFileName = "Ignoring as this was filed from macallan - probably when PDF editor worked from there " + sdi.GetOrigFileNameWin();
            //        }
            //        else
            //        {
            //            if (fdi.filedAs_pathAndFileName == "")
            //            {
            //                audDat.MovedToFileName = "Going to delete as unfiled " + sdi.GetOrigFileNameWin();
            //            }
            //            else
            //            {
            //                long fileLen1 = 0;
            //                long fileLen2 = 0;
            //                byte[] md5Val1 = ScanDocHandler.GenHashOnFileExcludingMetadata(fdi.filedAs_pathAndFileName, out fileLen1);
            //                byte[] md5Val2 = ScanDocHandler.GenHashOnFileExcludingMetadata(sdi.GetOrigFileNameWin(), out fileLen2);

            //                if (md5Val1.SequenceEqual(md5Val2))
            //                {
            //                    audDat.MovedToFileName = "Deleted (same md5 as filed) " + sdi.GetOrigFileNameWin();
            //                    try
            //                    {
            //                        File.Delete(sdi.GetOrigFileNameWin());
            //                    }
            //                    catch (Exception)
            //                    {
            //                        logger.Error("Failed to delete %s", sdi.GetOrigFileNameWin());
            //                    }
            //                }
            //                else if ((fileLen1 > fileLen2 * 9 / 10) && (fileLen1 < fileLen2 * 11 / 10))
            //                {
            //                    audDat.MovedToFileName = "Probably same (filed " + fileLen1.ToString() + "/orig " + fileLen2.ToString() + ")" + sdi.GetOrigFileNameWin();
            //                }
            //                else
            //                {
            //                    audDat.MovedToFileName = "File differs frm that filed (filed " + fileLen1.ToString() + "/orig " + fileLen2.ToString() + ")" + sdi.GetOrigFileNameWin();
            //                }
            //            }
            //        }
            //    }

            //    // Add to the audit data collection
            //    this.Dispatcher.BeginInvoke((Action)delegate()
            //        {
            //            _auditDataColl.Add(audDat);
            //        });

            //    numFound++;
            //    if (numFound >= MAX_NUM_DOCS_TO_ADD_TO_LIST)
            //        break;
            //    nDocIdx--;
            //}
            //e.Result = "Ok";
            //this.Dispatcher.BeginInvoke((Action)delegate()
            //{
            //    txtStart.Text = (nDocIdx).ToString();
            //});
        }

        private void PopulateList_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progBar.Value = (e.ProgressPercentage);
        }

        private void PopulateList_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //progBar.Value = (100);
            //btnGo.Content = "Latest";
            //btnNext.IsEnabled = true;
            //btnSearch.IsEnabled = true;

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
                // Successful search
                //_lastSearchResult = (IFindFluent<ScanPages, ScanPages>)e.Result;
                //this.Dispatcher.BeginInvoke((Action)delegate ()
                //{
                //    lblListStatus.Content = _lastSearchResult.CountDocuments().ToString() + " scans found";
                //});
                progBar.Value = (70);

                // Clear list
                _auditDataColl.Clear();

                // Parse records
                List<ComboEntity> rslt = (List<ComboEntity>)e.Result;
                foreach (var rec in rslt)
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
                        //var filedInfo = rec.GetValue("FiledInfo").AsBsonArray;
                        //foreach (var infoVal in filedInfo)
                        //{
                        //    BsonValue finalStatus;
                        //    if (infoVal.AsBsonDocument.TryGetValue("filedAt_finalStatus", out finalStatus))
                        //    {
                        //        auditData.FinalStatus = FiledDocInfo.GetFinalStatusStr((FiledDocInfo.DocFinalStatus)finalStatus.AsInt32);
                        //        ////statStr += " on " + fdi.filedAt_dateAndTime.ToShortDateString();
                        //    }
                        //}
                    }

                    //List<BsonDocument> rslt = (List<BsonDocument>)e.Result;
                    //foreach (var rec in rslt)
                    //{
                    //    var auditData = new AuditData();

                    //    // Get uniqName
                    //    string uniqName = rec.GetValue("uniqName").AsString;
                    //    auditData.UniqName = uniqName;

                    //    // Get filed info
                    //    if (rec.GetValue("FiledInfo").IsBsonArray)
                    //    {
                    //        var filedInfo = rec.GetValue("FiledInfo").AsBsonArray;
                    //        foreach (var infoVal in filedInfo)
                    //        {
                    //            BsonValue finalStatus;
                    //            if (infoVal.AsBsonDocument.TryGetValue("filedAt_finalStatus", out finalStatus))
                    //            {
                    //                auditData.FinalStatus = FiledDocInfo.GetFinalStatusStr((FiledDocInfo.DocFinalStatus) finalStatus.AsInt32);
                    //                ////statStr += " on " + fdi.filedAt_dateAndTime.ToShortDateString();
                    //            }
                    //        }
                    //    }


                    //var dict = rec.ToDictionary();

                    //// UniqName
                    //object retVal = new object();
                    //if (dict.TryGetValue("uniqName", out retVal))
                    //    auditData.UniqName = (string)retVal;

                    //// Filing info
                    //object filedInfo = new object();
                    //if (dict.TryGetValue("FiledInfo", out filedInfo))
                    //    Console.WriteLine("FiledInfo {0}", filedInfo.ToString());

                    ////string statStr = FiledDocInfo.GetFinalStatusStr();
                    ////statStr += " on " + fdi.filedAt_dateAndTime.ToShortDateString();
                    _auditDataColl.Add(auditData);
                }
                progBar.Value = (100);
            }

            EnableSearchButtons(true);
        }

        private void auditListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                AuditData selectedRow = e.AddedItems[0] as AuditData;
                if (selectedRow != null)
                {
                    string uniqName = selectedRow.UniqName;
                    string imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, uniqName, 1, false);
                    try
                    {
                        auditFileImage.Source = new BitmapImage(new Uri(imgFileName));
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Loading bitmap file {0} excp {1}", imgFileName, excp.Message);
                    }


                    // Doc info
                    ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(selectedRow.UniqName);
                    
                    btnOpenFiled.IsEnabled = (scanDocAllInfo.filedDocInfo != null) && (scanDocAllInfo.filedDocInfo.filedAt_finalStatus == FiledDocInfo.DocFinalStatus.STATUS_FILED);

                    txtScanDocInfo.Text = ScanDocHandler.GetScanDocInfoText(scanDocAllInfo.scanDocInfo);
                    txtScanDocInfo.IsReadOnly = true;

                    txtFiledDocInfo.Text = ScanDocHandler.GetFiledDocInfoText(scanDocAllInfo.filedDocInfo);
                    txtFiledDocInfo.IsReadOnly = true;

                    string pagesStr = "";
                    if (scanDocAllInfo.scanPages != null)
                    {
                        foreach (List<ScanTextElem> stElemList in scanDocAllInfo.scanPages.scanPagesText)
                        {
                            foreach (ScanTextElem el in stElemList)
                            {
                                pagesStr += el.text + " ";
                            }
                        }
                    }
                    txtPageText.Text = pagesStr;
                    txtPageText.IsReadOnly = true;

                    //// Get doc type info
                    //DocType dtype = _docTypesMatcher.GetDocType(selectedRow.DocType);
                    //if (dtype != null)
                    //{
                    //    txtOrigDocTypeName.Text = dtype.docTypeName;
                    //    txtOrigExpression.Text = dtype.matchExpression;
                    //}

                    //ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(selectedRow.UniqName);
                    //txtNewDocTypeName.Text = scanDocAllInfo.scanDocInfo.docTypeMatchResult.docTypeName + "(" + scanDocAllInfo.scanDocInfo.docTypeMatchResult.matchCertaintyPercent.ToString() + "%)";
                    //DocType dtype2 = _docTypesMatcher.GetDocType(scanDocAllInfo.scanDocInfo.docTypeMatchResult.docTypeName);
                    //if (dtype2 != null)
                    //{
                    //    txtNewExpression.Text = dtype2.matchExpression;
                    //}

                    // Get file name
                    //string fileName = selectedRow.ArchiveFile;
                    //if (File.Exists(fileName))
                    //{
                    //    string imgName = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(selectedRow.ArchiveFile), System.IO.Path.GetFileNameWithoutExtension(selectedRow.ArchiveFile) + ".jpg");
                    //    if (File.Exists(imgName))
                    //        auditFileImage.Source = new BitmapImage(new Uri(imgName));

                        //System.Drawing.Image img = PdfRasterizer.GetImageOfPage(fileName, 1);
                        //MemoryStream ms = new MemoryStream();
                        //img.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        //System.Windows.Media.Imaging.BitmapImage bImg = new System.Windows.Media.Imaging.BitmapImage();
                        //bImg.BeginInit();
                        //bImg.StreamSource = new MemoryStream(ms.ToArray());
                        //bImg.EndInit();
                        //auditFileImage.Source = bImg;
                    // }
                }
            }
        }

        private string GetListViewType()
        {
            return ((ComboBoxItem)(comboListView.SelectedItem)).Tag.ToString();
        }

        private void btnGo_Click(object sender, RoutedEventArgs e)
        {
            // Stop if required
            if (_bwThreadForPopulateList.IsBusy)
            {
                _bwThreadForPopulateList.CancelAsync();
                return;
            }

            // Clear list
            _auditDataColl.Clear();

            //int startVal = 100000000;
            //if (!_bwThreadForPopulateList.IsBusy)
            //{
            //    btnGo.Content = "Stop";
            //    btnNext.IsEnabled = false;
            //    btnSearch.IsEnabled = false;
            //    object[] args = { GetListViewType(), startVal, "", "" };
            //    _bwThreadForPopulateList.RunWorkerAsync(args);
            //}
        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            // Clear list
            _auditDataColl.Clear();

            //int startVal = 100000000;
            //Int32.TryParse(txtStart.Text, out startVal);
            //if (!_bwThreadForPopulateList.IsBusy)
            //{
            //    btnGo.Content = "Stop";
            //    btnNext.IsEnabled = false;
            //    btnSearch.IsEnabled = false;
            //    object[] args = { GetListViewType(), startVal, "", "" };
            //    _bwThreadForPopulateList.RunWorkerAsync(args);
            //}
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
            string srchText = txtSearch.Text;
            if (!_bwThreadForSearch.IsBusy)
            {
                EnableSearchButtons(false);
                object[] args = { srchText };
                _bwThreadForSearch.RunWorkerAsync(args);
            }

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

        }

        private void btnListLast_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnListPrev_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnListNext_Click(object sender, RoutedEventArgs e)
        {

        }
    }

    public class AuditData
    {
        public string UniqName {get; set;}
        public string FinalStatus { get; set; }
        public string FiledDocPresent { get; set; }
        public string MovedToFileName { get; set; }
        public string DocTypeFiledAs { get; set; }
    }

}
