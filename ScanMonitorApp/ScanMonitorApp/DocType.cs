using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using MongoDB.Bson;

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
    }

    public class DocTypeMatchResult
    {
        public enum MatchResultCodes { NOT_FOUND, FOUND_MATCH, NO_EXPR, DISABLED };
        public string docTypeName = "";
        public DateTime docDate = DateTime.MinValue;
        public int matchCertaintyPercent = 0;
        public MatchResultCodes matchResultCode = MatchResultCodes.NOT_FOUND;
        public List<ExtractedDate> datesFoundInDoc = new List<ExtractedDate>();
    }

    public class DocMatchAction
    {
        public string moveTo;
        public string renameTo;
    }

    public class DocRectangle
    {
        public DocRectangle(int x, int y, int wid, int hig)
        {
            X = x;
            Y = y;
            Width = wid;
            Height = hig;
        }
        public DocRectangle(double x, double y, double wid, double hig)
        {
            X = x;
            Y = y;
            Width = wid;
            Height = hig;
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
            }
            catch
            { }
        }
            
        public void SetVal(int valIdx, double val)
        {
            switch(valIdx)
            {
                case 0: { X = Convert.ToInt32(val); break; }
                case 1: { Y = Convert.ToInt32(val); break; }
                case 2: { Width = Convert.ToInt32(val); break; }
                case 3: { Height = Convert.ToInt32(val); break; }                
            }
        }

        public double X;
        public double Y;
        public double Width;
        public double Height;

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
    }
}
