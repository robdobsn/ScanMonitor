using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson;
using NLog;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media.Imaging;
using MongoDB.Bson.Serialization.Attributes;

namespace ScanMonitorApp
{
    public class DocType
    {
        public DocType()
        {
            docTypeName = "";
            matchExpression = "";
            dateExpression = "";
            thumbnailForDocType = "";
            moveFileToPath = "";
            renameFileTo = "";
            isEnabled = false;
            previousName = "";
            renamedTo = "";
        }
        public void CloneForRenaming(string newName, DocType prevDocType)
        {
            docTypeName = newName;
            matchExpression = prevDocType.matchExpression;
            dateExpression = prevDocType.dateExpression;
            thumbnailForDocType = prevDocType.thumbnailForDocType;
            moveFileToPath = prevDocType.moveFileToPath;
            renameFileTo = prevDocType.renameFileTo;
            isEnabled = false;
            previousName = prevDocType.docTypeName;
            renamedTo = "";
        }
        public static string GetFileNamePrefix(string docTypeName)
        {
            string rtn = docTypeName;
            int pos = docTypeName.IndexOf(" - ");
            if ((pos >= 0) && (pos+3 < docTypeName.Length))
                rtn = docTypeName.Substring(pos+3);
            return rtn.Trim();
        }

        [BsonId]
        [BsonIgnoreIfDefault]
        public ObjectId Id;
        public string docTypeName { get; set; }
        public string matchExpression { get; set; }
        public string dateExpression { get; set; }
        public string thumbnailForDocType { get; set; }
        public bool isEnabled { get; set; }
        public string previousName { get; set; }
        public string renamedTo { get; set; }
        public string moveFileToPath { get; set; }
        public string renameFileTo { get; set; }
    }

    public class DocTypeMatchResult
    {
        public DocTypeMatchResult()
        {
            docTypeName = "";
            docDate = DateTime.MinValue;
            matchCertaintyPercent = 0;
            matchFactor = 0;
            matchResultCode = MatchResultCodes.NOT_FOUND;
            datesFoundInDoc = new List<ExtractedDate>();
        }
        public enum MatchResultCodes { NOT_FOUND, FOUND_MATCH, NO_EXPR, DISABLED };
        public string docTypeName { get; set; }
        public DateTime docDate { get; set; }
        public int matchCertaintyPercent { get; set; }
        public double matchFactor { get; set; }
        public MatchResultCodes matchResultCode { get; set; }
        public List<ExtractedDate> datesFoundInDoc { get; set; }
    }

    public class DocRectangle
    {
        public DocRectangle(int x, int y, int wid, int hig)
        {
            X = x;
            Y = y;
            Width = wid;
            Height = hig;
            if (Width < 0 || Height < 0)
                FixNegatives();
        }
        public DocRectangle(double x, double y, double wid, double hig)
        {
            X = x;
            Y = y;
            Width = wid;
            Height = hig;
            if (Width < 0 || Height < 0)
                FixNegatives();
        }
        public double BottomRightX { get { return X + Width; } }
        public double BottomRightY { get { return Y + Height; } }

        public DocRectangle(string rectCoordStr)
        {
            X = 0;
            Y = 0;
            Width = 100;
            Height = 100;
            try
            {
                string[] splitStr = rectCoordStr.Split(',');
                if (splitStr.Length > 0)
                    Double.TryParse(splitStr[0], out X);
                if (splitStr.Length > 1)
                    Double.TryParse(splitStr[1], out Y);
                if (splitStr.Length > 2)
                    Double.TryParse(splitStr[2], out Width);
                if (splitStr.Length > 3)
                    Double.TryParse(splitStr[3], out Height);
                if (Width < 0 || Height < 0)
                    FixNegatives();
            }
            catch
            { }
        }

        public void SetVal(int valIdx, double val)
        {
            switch (valIdx)
            {
                case 0: { X = Convert.ToInt32(val); break; }
                case 1: { Y = Convert.ToInt32(val); break; }
                case 2: { Width = Convert.ToInt32(val); break; }
                case 3: { Height = Convert.ToInt32(val); break; }
            }
            if (Width < 0 || Height < 0)
                FixNegatives();
        }

        public double X;
        public double Y;
        public double Width;
        public double Height;

        private void FixNegatives()
        {
            double brx = BottomRightX;
            double bry = BottomRightY;
            if (brx < X)
            {
                Width = X - brx;
                X = brx;
            }
            if (bry < Y)
            {
                Height = Y - bry;
                Y = bry;
            }
        }

        public bool Contains(DocRectangle rect)
        {
            return (this.X <= rect.X) &&
                            ((rect.X + rect.Width) <= (this.X + this.Width)) &&
                            (this.Y <= rect.Y) &&
                            ((rect.Y + rect.Height) <= (this.Y + this.Height));
        }

        public bool Intersects(DocRectangle rect)
        {
            if (X > rect.BottomRightX)
                return false;
            if (BottomRightX < rect.X)
                return false;
            if (Y > rect.BottomRightY)
                return false;
            if (BottomRightY < rect.Y)
                return false;
            return true;
        }

        public void RotateAt(double angleInDegs, double centreX, double centreY)
        {
            System.Windows.Media.Matrix rotMatrix = System.Windows.Media.Matrix.Identity;
            rotMatrix.RotateAt(angleInDegs, centreX, centreY);
            System.Windows.Point tl = new System.Windows.Point(X, Y);
            System.Windows.Point br = new System.Windows.Point(BottomRightX, BottomRightY);
            System.Windows.Point rotTl = rotMatrix.Transform(tl);
            System.Windows.Point rotBr = rotMatrix.Transform(br);
            X = Math.Min(rotTl.X, rotBr.X);
            Y = Math.Min(rotTl.Y, rotBr.Y);
            Width = Math.Abs(rotTl.X - rotBr.X);
            Height = Math.Abs(rotTl.Y - rotBr.Y);
        }
    }

    public class PathSubstMacro
    {
        [BsonId]
        [BsonIgnoreIfDefault]
        public ObjectId Id;
        public string origText { get; set; }
        public string replaceText { get; set; }
    }

    public class DocTypeHelper
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public static string GetFilenameFromThumbnailStr(string thumbnailStr)
        {
            string imgFileName = "";
            // Check for a path
            if (thumbnailStr.Contains('\\') || thumbnailStr.Contains('/'))
            {
                imgFileName = thumbnailStr;
            }
            else if (thumbnailStr.Contains("~~"))
            {
                string baseStr = thumbnailStr.Replace("~~", "");
                imgFileName = Path.Combine(Properties.Settings.Default.DocAdminImgFolderBase, @"PastedThumbs", baseStr + ".png");
            }
            else
            {
                string[] splitNameAndPageNum = thumbnailStr.Split('~');
                string uniqNameOnly = (splitNameAndPageNum.Length > 0) ? splitNameAndPageNum[0] : "";
                string pageNumStr = (splitNameAndPageNum.Length > 1) ? splitNameAndPageNum[1] : "";
                int pageNum = 1;
                if (pageNumStr.Trim().Length > 0)
                {
                    try { pageNum = Convert.ToInt32(pageNumStr); }
                    catch { pageNum = 1; }
                }
                imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, uniqNameOnly, pageNum, false);
            }
            return imgFileName;
        }

        public static BitmapImage LoadDocThumbnail(string thumbnailStr, int heightOfThumbnail)
        {
            // The thumbnailStr can be either a string like "uniqName~pageNum" OR a full file path
            BitmapImage bitmap = null;
            string imgFileName = GetFilenameFromThumbnailStr(thumbnailStr);
            if ((imgFileName == "") || (!File.Exists(imgFileName)))
            {
                logger.Info("Thumbnail file doesn't exist for {0}", imgFileName);
            }
            else
            {
                try
                {
                    bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri("File:" + imgFileName);
                    bitmap.DecodePixelHeight = heightOfThumbnail;
                    bitmap.EndInit();
                }
                catch (Exception excp)
                {
                    logger.Error("Loading thumbnail file {0} excp {1}", imgFileName, excp.Message);
                    bitmap = null;
                }
            } 
            return bitmap;
        }

        public static string GetNameForPastedThumbnail()
        {
            string thumbName = "~~Thumb" + DateTime.Now.ToString("yyyyMMddhhmmss");
            string thumbFileName = GetFilenameFromThumbnailStr(thumbName);
            if (File.Exists(thumbFileName))
                return "";
            return thumbName;
        }
    }


}
