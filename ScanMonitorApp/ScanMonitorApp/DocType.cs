using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace ScanMonitorApp
{
    public class DocType
    {
        public string docTypeName;
        public List<DocPatternText> mustHaveTexts;
        public List<DocPatternText> mustNottHaveText;
        public string thumbnailForDocType;

    }

    public class DocTypeMatchResult
    {
        public string docTypeName = "";
        public DateTime docDate = DateTime.MinValue;
        public bool matchesMustHaveTexts = false;
        public bool matchesMustNotHaveTexts = false;
    }

    public class DocPatternText
    {
        public string textToMatch;
        public DocRectangle textBounds;
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
            width = widthPercent;
            height = heightPercent;
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
