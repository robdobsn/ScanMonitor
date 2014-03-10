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
        public enum MatchResultCodes { NOT_FOUND, FOUND_MATCH, NO_EXPR, DISABLED };
        public string docTypeName = "";
        public DateTime docDate = DateTime.MinValue;
        public int matchCertaintyPercent = 0;
        public double matchFactor = 0;
        public MatchResultCodes matchResultCode = MatchResultCodes.NOT_FOUND;
        public List<ExtractedDate> datesFoundInDoc = new List<ExtractedDate>();
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
                    X = Convert.ToDouble(splitStr[0]);
                if (splitStr.Length > 1)
                    Y = Convert.ToDouble(splitStr[1]);
                if (splitStr.Length > 2)
                    Width = Convert.ToDouble(splitStr[2]);
                if (splitStr.Length > 3)
                    Height = Convert.ToDouble(splitStr[3]);
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
        public ObjectId Id;
        public string origText { get; set; }
        public string replaceText { get; set; }
    }

    public class DocTypeHelper
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public static BitmapImage LoadDocThumbnail(string uniqName, int heightOfThumbnail)
        {
            BitmapImage bitmap = null;
            string[] splitNameAndPageNum = uniqName.Split('~');
            string uniqNameOnly = (splitNameAndPageNum.Length > 0) ? splitNameAndPageNum[0] : "";
            string pageNumStr = (splitNameAndPageNum.Length > 1) ? splitNameAndPageNum[1] : "";
            int pageNum = 1;
            if (pageNumStr.Trim().Length > 0)
            {
                try { pageNum = Convert.ToInt32(pageNumStr); }
                catch { pageNum = 1; }
            }
            string imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, uniqNameOnly, pageNum, false);
            if (!File.Exists(imgFileName))
            {
                logger.Info("Thumbnail file doesn't exist for {0}", uniqNameOnly);
            }
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

            return bitmap;
        }
    }


}
