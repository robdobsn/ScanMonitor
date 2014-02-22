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
        public ObjectId Id;
        public string docTypeName;
        public string matchExpression;
        public string thumbnailForDocType;
    }

    public class DocTypeMatchResult
    {
        public string docTypeName = "";
        public DateTime docDate = DateTime.MinValue;
        public int matchCertaintyPercent = 0;
    }

    public class DocMatchAction
    {
        public string moveTo;
        public string renameTo;
    }

    public class DocRectangle
    {
        public DocRectangle(int x, int y, int width, int height)
        {
            topLeftXPercent = x;
            topLeftYPercent = y;
            widthPercent = width;
            heightPercent = height;
        }
        public DocRectangle(double x, double y, double width, double height)
        {
            topLeftXPercent = Convert.ToInt32(x);
            topLeftYPercent = Convert.ToInt32(y);
            widthPercent = Convert.ToInt32(width);
            heightPercent = Convert.ToInt32(height);
        }
        public int topLeftXPercent;
        public int topLeftYPercent;
        public int widthPercent;
        public int heightPercent;

        public bool Contains(DocRectangle rect)
        {
            return (this.topLeftXPercent <= rect.topLeftXPercent) &&
                            ((rect.topLeftXPercent + rect.widthPercent) <= (this.topLeftXPercent + this.widthPercent)) &&
                            (this.topLeftYPercent <= rect.topLeftYPercent) &&
                            ((rect.topLeftYPercent + rect.heightPercent) <= (this.topLeftYPercent + this.heightPercent));
        }
    }
}
