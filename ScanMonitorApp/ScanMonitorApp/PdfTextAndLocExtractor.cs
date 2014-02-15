using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    /// <summary>
    /// Taken from http://www.java-frameworks.com/java/itext/com/itextpdf/text/pdf/parser/LocationTextExtractionStrategy.java.html
    /// </summary>
    class LocationTextExtractionStrategyEx : LocationTextExtractionStrategy
    {
        private List<TextChunkEx> m_locationResult = new List<TextChunkEx>();
        private List<TextInfo> m_TextLocationInfo = new List<TextInfo>();
        public List<TextChunkEx> LocationResult
        {
            get { return m_locationResult; }
        }
        public List<TextInfo> TextLocationInfo
        {
            get { return m_TextLocationInfo; }
        }

        /// <summary>
        /// Creates a new LocationTextExtracationStrategyEx
        /// </summary>
        public LocationTextExtractionStrategyEx()
        {
        }

        /// <summary>
        /// Returns the result so far
        /// </summary>
        /// <returns>a String with the resulting text</returns>
        public override String GetResultantText()
        {
            m_locationResult.Sort();

            StringBuilder sb = new StringBuilder();
            TextChunkEx lastChunk = null;
            TextInfo lastTextInfo = null;
            foreach (TextChunkEx chunk in m_locationResult)
            {
                if (lastChunk == null)
                {
                    sb.Append(chunk.Text);
                    lastTextInfo = new TextInfo(chunk);
                    m_TextLocationInfo.Add(lastTextInfo);
                }
                else
                {
                    if (chunk.sameLine(lastChunk))
                    {
                        float dist = chunk.distanceFromEndOf(lastChunk);

                        // RobD: Changed this to split sections of text on same line but separated by more than normal whitespace into separate TextInfos
                        if (dist > chunk.CharSpaceWidth * 5)
                        {
                            sb.Append(' ');
                            sb.Append(chunk.Text);
                            lastTextInfo = new TextInfo(chunk);
                            m_TextLocationInfo.Add(lastTextInfo);
                        }
                        else
                        {
                            if (dist < -chunk.CharSpaceWidth)
                            {
                                sb.Append(' ');
                                lastTextInfo.addSpace();
                            }
                            //append a space if the trailing char of the prev string wasn't a space && the 1st char of the current string isn't a space
                            else if (dist > chunk.CharSpaceWidth / 2.0f && chunk.Text[0] != ' ' && lastChunk.Text[lastChunk.Text.Length - 1] != ' ')
                            {
                                sb.Append(' ');
                                lastTextInfo.addSpace();
                            }
                            sb.Append(chunk.Text);
                            lastTextInfo.appendText(chunk);
                        }
                    }
                    else
                    {
                        sb.Append('\n');
                        sb.Append(chunk.Text);
                        lastTextInfo = new TextInfo(chunk);
                        m_TextLocationInfo.Add(lastTextInfo);
                    }
                }
                lastChunk = chunk;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="renderInfo"></param>
        public override void RenderText(TextRenderInfo renderInfo)
        {
            iTextSharp.text.pdf.parser.LineSegment segment = renderInfo.GetBaseline();
            TextChunkEx location = new TextChunkEx(renderInfo.GetText(), segment.GetStartPoint(), segment.GetEndPoint(), renderInfo.GetSingleSpaceWidth(), renderInfo.GetAscentLine(), renderInfo.GetDescentLine());
            m_locationResult.Add(location);
        }

        public class TextChunkEx : IComparable, ICloneable
        {
            string m_text;
            iTextSharp.text.pdf.parser.Vector m_startLocation;
            iTextSharp.text.pdf.parser.Vector m_endLocation;
            iTextSharp.text.pdf.parser.Vector m_orientationVector;
            int m_orientationMagnitude;
            int m_distPerpendicular;
            float m_distParallelStart;
            float m_distParallelEnd;
            float m_charSpaceWidth;

            public iTextSharp.text.pdf.parser.LineSegment AscentLine;
            public iTextSharp.text.pdf.parser.LineSegment DecentLine;

            public object Clone()
            {
                TextChunkEx copy = new TextChunkEx(m_text, m_startLocation, m_endLocation, m_charSpaceWidth, AscentLine, DecentLine);
                return copy;
            }

            public string Text
            {
                get { return m_text; }
                set { m_text = value; }
            }
            public float CharSpaceWidth
            {
                get { return m_charSpaceWidth; }
                set { m_charSpaceWidth = value; }
            }
            public iTextSharp.text.pdf.parser.Vector StartLocation
            {
                get { return m_startLocation; }
                set { m_startLocation = value; }
            }
            public iTextSharp.text.pdf.parser.Vector EndLocation
            {
                get { return m_endLocation; }
                set { m_endLocation = value; }
            }

            /// <summary>
            /// Represents a chunk of text, it's orientation, and location relative to the orientation vector
            /// </summary>
            /// <param name="txt"></param>
            /// <param name="startLoc"></param>
            /// <param name="endLoc"></param>
            /// <param name="charSpaceWidth"></param>
            public TextChunkEx(string txt, iTextSharp.text.pdf.parser.Vector startLoc, iTextSharp.text.pdf.parser.Vector endLoc, float charSpaceWidth, iTextSharp.text.pdf.parser.LineSegment ascentLine, iTextSharp.text.pdf.parser.LineSegment decentLine)
            {
                m_text = txt;
                m_startLocation = startLoc;
                m_endLocation = endLoc;
                m_charSpaceWidth = charSpaceWidth;
                AscentLine = ascentLine;
                DecentLine = decentLine;

                m_orientationVector = m_endLocation.Subtract(m_startLocation).Normalize();
                m_orientationMagnitude = (int)(Math.Atan2(m_orientationVector[iTextSharp.text.pdf.parser.Vector.I2], m_orientationVector[iTextSharp.text.pdf.parser.Vector.I1]) * 1000);

                // see http://mathworld.wolfram.com/Point-LineDistance2-Dimensional.html
                // the two vectors we are crossing are in the same plane, so the result will be purely
                // in the z-axis (out of plane) direction, so we just take the I3 component of the result
                iTextSharp.text.pdf.parser.Vector origin = new iTextSharp.text.pdf.parser.Vector(0, 0, 1);
                m_distPerpendicular = (int)(m_startLocation.Subtract(origin)).Cross(m_orientationVector)[iTextSharp.text.pdf.parser.Vector.I3];

                m_distParallelStart = m_orientationVector.Dot(m_startLocation);
                m_distParallelEnd = m_orientationVector.Dot(m_endLocation);
            }

            /// <summary>
            /// true if this location is on the the same line as the other text chunk
            /// </summary>
            /// <param name="textChunkToCompare">the location to compare to</param>
            /// <returns>true if this location is on the the same line as the other</returns>
            public bool sameLine(TextChunkEx textChunkToCompare)
            {
                if (m_orientationMagnitude != textChunkToCompare.m_orientationMagnitude) return false;
                if (m_distPerpendicular != textChunkToCompare.m_distPerpendicular) return false;
                return true;
            }

            /// <summary>
            /// Computes the distance between the end of 'other' and the beginning of this chunk
            /// in the direction of this chunk's orientation vector.  Note that it's a bad idea
            /// to call this for chunks that aren't on the same line and orientation, but we don't
            /// explicitly check for that condition for performance reasons.
            /// </summary>
            /// <param name="other"></param>
            /// <returns>the number of spaces between the end of 'other' and the beginning of this chunk</returns>
            public float distanceFromEndOf(TextChunkEx other)
            {
                float distance = m_distParallelStart - other.m_distParallelEnd;
                return distance;
            }

            /// <summary>
            /// Compares based on orientation, perpendicular distance, then parallel distance
            /// </summary>
            /// <param name="obj"></param>
            /// <returns></returns>
            public int CompareTo(object obj)
            {
                if (obj == null) throw new ArgumentException("Object is now a TextChunk");

                TextChunkEx rhs = obj as TextChunkEx;
                if (rhs != null)
                {
                    if (this == rhs) return 0;

                    int rslt;
                    rslt = m_orientationMagnitude - rhs.m_orientationMagnitude;
                    if (rslt != 0) return rslt;

                    rslt = m_distPerpendicular - rhs.m_distPerpendicular;
                    if (rslt != 0) return rslt;

                    // note: it's never safe to check floating point numbers for equality, and if two chunks
                    // are truly right on top of each other, which one comes first or second just doesn't matter
                    // so we arbitrarily choose this way.
                    rslt = m_distParallelStart < rhs.m_distParallelStart ? -1 : 1;

                    return rslt;
                }
                else
                {
                    throw new ArgumentException("Object is now a TextChunk");
                }
            }
        }

        public class TextInfo
        {
            public iTextSharp.text.pdf.parser.Vector TopLeft;
            public iTextSharp.text.pdf.parser.Vector BottomRight;
            private string m_Text;

            public string Text
            {
                get { return m_Text; }
            }

            /// <summary>
            /// Create a TextInfo.
            /// </summary>
            /// <param name="initialTextChunk"></param>
            public TextInfo(TextChunkEx initialTextChunk)
            {
                TopLeft = initialTextChunk.AscentLine.GetStartPoint();
                BottomRight = initialTextChunk.DecentLine.GetEndPoint();
                m_Text = initialTextChunk.Text;
            }

            /// <summary>
            /// Add more text to this TextInfo.
            /// </summary>
            /// <param name="additionalTextChunk"></param>
            public void appendText(TextChunkEx additionalTextChunk)
            {
                BottomRight = additionalTextChunk.DecentLine.GetEndPoint();
                m_Text += additionalTextChunk.Text;
            }

            /// <summary>
            /// Add a space to the TextInfo.  This will leave the endpoint out of sync with the text.
            /// The assumtion is that you will add more text after the space which will correct the endpoint.
            /// </summary>
            public void addSpace()
            {
                m_Text += ' ';
            }


        }
    }

    class PdfTextAndLocExtractor
    {
        private string CoordStr(iTextSharp.text.pdf.parser.Vector coord)
        {
            return Convert.ToInt32(coord.Dot(new iTextSharp.text.pdf.parser.Vector(0, 1, 0))).ToString() + "," +
                Convert.ToInt32(coord.Dot(new iTextSharp.text.pdf.parser.Vector(0, 1, 0))).ToString();
        }

        public ScanDocAllInfo ExtractDocInfo(string fileName, int maxPagesToExtractFrom)
        {
            // Extract text and location from pdf pages
            using (Stream newpdfStream = new FileStream(fileName, FileMode.Open, FileAccess.Read))
            {
                List<List<LocationTextExtractionStrategyEx.TextInfo>> extractedTextAndLoc = new List<List<LocationTextExtractionStrategyEx.TextInfo>>();

                PdfReader pdfReader = new PdfReader(newpdfStream);
                int numPagesToUse = pdfReader.NumberOfPages;
                if (numPagesToUse > maxPagesToExtractFrom)
                    numPagesToUse = maxPagesToExtractFrom;
                for (int pageIdx = 1; pageIdx < numPagesToUse; pageIdx++)
                {
                    LocationTextExtractionStrategyEx locationStrategy = new LocationTextExtractionStrategyEx();
                    string text = PdfTextExtractor.GetTextFromPage(pdfReader, pageIdx, locationStrategy);
                    extractedTextAndLoc.Add(locationStrategy.TextLocationInfo);
                }

                // Create new structures for the information
                List<ScanDocAllInfo.ScanPageText> scanPages = new List<ScanDocAllInfo.ScanPageText>();
                foreach (List<LocationTextExtractionStrategyEx.TextInfo> pageInfo in extractedTextAndLoc)
                {
                    List<ScanDocAllInfo.ScanTextElem> scanTextElems = new List<ScanDocAllInfo.ScanTextElem>();
                    foreach (LocationTextExtractionStrategyEx.TextInfo txtInfo in pageInfo)
                    {
                        ScanDocAllInfo.ScanTextElem sti = new ScanDocAllInfo.ScanTextElem(CoordStr(txtInfo.TopLeft) + "," + CoordStr(txtInfo.BottomRight), txtInfo.Text);
                        scanTextElems.Add(sti);
                    }
                    ScanDocAllInfo.ScanPageText spt = new ScanDocAllInfo.ScanPageText(scanTextElems);
                    scanPages.Add(spt);
                }

                // File unique name
                string uniqueName = Path.GetFileNameWithoutExtension(fileName);
                DateTime fileDateTime = File.GetCreationTime(fileName);

                // Complete the document info
                ScanDocAllInfo scanDocAllInfo = new ScanDocAllInfo(uniqueName, pdfReader.NumberOfPages,
                            "", fileDateTime, DateTime.Now, "", scanPages, "");
                return scanDocAllInfo;
            }
        }
    }
}
