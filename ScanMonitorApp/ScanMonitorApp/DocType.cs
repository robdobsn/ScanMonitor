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
        public string docTypeName { get; set; }
        public string matchExpression { get; set; }
        public string thumbnailForDocType { get; set; }
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
            topLeftXPercent = x;
            topLeftYPercent = y;
            widthPercent = width;
            heightPercent = height;
        }
        public double bottomRightXPercent { get { return topLeftXPercent + widthPercent; } }
        public double bottomRightYPercent { get { return topLeftYPercent + heightPercent; } }

        public DocRectangle(string rectCoordStr)
        {
            topLeftXPercent = 0;
            topLeftYPercent = 0;
            widthPercent = 100;
            heightPercent = 100;
            try
            {
                string[] splitStr = rectCoordStr.Split(',');
                if (splitStr.Length > 0)
                    topLeftXPercent = Convert.ToDouble(splitStr[0]);
                if (splitStr.Length > 1)
                    topLeftYPercent = Convert.ToDouble(splitStr[1]);
                if (splitStr.Length > 2)
                    widthPercent = Convert.ToDouble(splitStr[2]);
                if (splitStr.Length > 3)
                    heightPercent = Convert.ToDouble(splitStr[3]);
            }
            catch
            { }
        }
            
        public void SetVal(int valIdx, double val)
        {
            switch(valIdx)
            {
                case 0: { topLeftXPercent = Convert.ToInt32(val); break; }
                case 1: { topLeftYPercent = Convert.ToInt32(val); break; }
                case 2: { widthPercent = Convert.ToInt32(val); break; }
                case 3: { heightPercent = Convert.ToInt32(val); break; }                
            }
        }

        public double topLeftXPercent;
        public double topLeftYPercent;
        public double widthPercent;
        public double heightPercent;

        public bool Contains(DocRectangle rect)
        {
            return (this.topLeftXPercent <= rect.topLeftXPercent) &&
                            ((rect.topLeftXPercent + rect.widthPercent) <= (this.topLeftXPercent + this.widthPercent)) &&
                            (this.topLeftYPercent <= rect.topLeftYPercent) &&
                            ((rect.topLeftYPercent + rect.heightPercent) <= (this.topLeftYPercent + this.heightPercent));
        }
    }
}
