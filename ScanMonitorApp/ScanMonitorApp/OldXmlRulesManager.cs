using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Xml.XPath;
using System.IO;
using System.Text.RegularExpressions;

namespace ScanMan
{

    class OldXmlRulesManager
    {
        private string xmlFName = "";
        private List<DocType> docTypesCache;
        bool thumbnailsNeedRefresh = true;

        public OldXmlRulesManager(string xmlFileName)
        {
            xmlFName = xmlFileName;
        }

        public List<string> GetDocTypeNames()
        {
            List<string> docTypeNames = new List<string>();
            if (docTypesCache == null)
                docTypesCache = ReloadDocTypes();
            foreach (var docType in docTypesCache)
                docTypeNames.Add(docType.dtName);
            return docTypeNames;
        }

        public DocType GetDocTypeFromName(string docTypeName)
        {
            if (docTypesCache == null)
                docTypesCache = ReloadDocTypes();
            foreach (DocType dt in docTypesCache)
                if (dt.dtName == docTypeName)
                    return dt;
            return null;
        }

        public List<DocType> GetAllDocTypes()
        {
            if (docTypesCache == null)
                docTypesCache = ReloadDocTypes();
            return docTypesCache;
        }

        public bool ThumbnailsNeedRefresh()
        {
            return thumbnailsNeedRefresh;
        }

        private List<DocType> ReloadDocTypes()
        {
            List<DocType> docTypeList = new List<DocType>();
            XDocument xdoc = XDocument.Load(xmlFName);

            try
            {
                var docTypes = from docType in xdoc.Descendants("filetype")
                               orderby docType.Attribute("name").Value
                               select new
                               {
                                   name = docType.Attribute("name").Value,
                                   good = docType.Descendants("good"),
                                   bad = docType.Descendants("bad"),
                                   grabtext = docType.Descendants("grabtext"),
                                   docdate = docType.Descendants("docdate"),
                                   moveto = docType.Descendants("moveto"),
                                   renameto = docType.Descendants("renameto"),
                                   thumbs = docType.Descendants("thumb")
                               };

                //Loop through results
                foreach (var docType in docTypes)
                {
                    DocType dt = new DocType();
                    dt.dtName = docType.name;

                    // Strings required for match
                    foreach (var gd in docType.good)
                    {
                        CheckItem ci = new CheckItem(gd.Value, gd.Attribute("req").Value);
                        dt.goodStrings.Add(ci);
                    }

                    // Strings not required for match
                    foreach (var bd in docType.bad)
                    {
                        CheckItem ci = new CheckItem(bd.Value, bd.Attribute("req").Value);
                        dt.badStrings.Add(ci);
                    }

                    // Text that should be extracted from file
                    foreach (var gt in docType.grabtext)
                    {
                        GrabItem gi = new GrabItem(gt.Attribute("name").Value, gt.Attribute("from").Value, gt.Value);
                        dt.grabTexts.Add(gi);
                    }

                    // Move-to path
                    foreach (var mt in docType.moveto)
                    {
                        dt.moveTo = mt.Value;
                    }

                    // Rename-to value
                    foreach (var rt in docType.renameto)
                    {
                        dt.renameTo = rt.Value;
                    }

                    // Thumbnails
                    foreach (var th in docType.thumbs)
                    {
                        dt.thumbFileNames.Add(th.Value);
                    }

                    // Store the doc-type
                    docTypeList.Add(dt);
                }
            }
            catch
            {
            }
            return docTypeList;
        }

        public bool SaveOrNewDocType(string docTypeName, DocType dt, bool bNew)
        {
            bool bSaveOk = true;

            BackupXMLFile();

            XDocument xdoc = XDocument.Load(xmlFName);

            XElement xE = null;

            // Get new or renamed element
            if (bNew)
            {
                try
                {
                    XElement xRules= xdoc.XPathSelectElement("scanrules");
                    xE = new XElement("filetype", "", new XAttribute("name", dt.dtName));
                    xRules.Add(xE);
                }
                catch
                {
                    bSaveOk = false;
                }
            }
            else
            {
                try
                {
                    xE = xdoc.XPathSelectElement("scanrules/filetype[@name = \"" + docTypeName + "\"]");
                    // Change name if required
                    if (docTypeName != dt.dtName)
                        xE.SetAttributeValue("name", dt.dtName);
                }
                catch
                {
                    bSaveOk = false;
                }
            }

            try
            {
                // Remove any existing content
                xE.RemoveNodes();

                // Fill element contents
                foreach (CheckItem ci in dt.goodStrings)
                    xE.Add(new XElement("good", ci.checkString, new XAttribute("req", ci.numInstances)));
                foreach (CheckItem bd in dt.badStrings)
                    xE.Add(new XElement("bad", bd.checkString, new XAttribute("req", bd.numInstances)));
                foreach (GrabItem gi in dt.grabTexts)
                    xE.Add(new XElement("grabtext", gi.str, new XAttribute("from", gi.from), new XAttribute("name", gi.name)));
                xE.Add(new XElement("moveto", dt.moveTo));
                xE.Add(new XElement("renameto", dt.renameTo));
                xdoc.Save(xmlFName);
            }
            catch
            {
                bSaveOk = false;
            }

            // Cache of doc-types is now out of date
            docTypesCache = null;
            thumbnailsNeedRefresh = true;

            return bSaveOk;
        }

        public void AddThumbnailToDocType(string thumbFileName, string docTypeName)
        {
            // Check empty name
            if (docTypeName == "")
                return;

            // Find the doctype in xml file
            XDocument xdoc = XDocument.Load(xmlFName);
            XElement xE = null;

            try
            {
                xE = xdoc.XPathSelectElement("scanrules/filetype[@name =\" + docTypeName + \"]");
                // If no thumbnails currently there then the list needs to be refreshed - otherwise not
                if (xE.Elements("thumb").Count() == 0)
                    thumbnailsNeedRefresh = true;
                xE.Add(new XElement("thumb", thumbFileName));
                xdoc.Save(xmlFName);
            }
            catch
            {
            }
        }

        public List<DocType> GetDocTypesForThumbnails()
        {
            thumbnailsNeedRefresh = false;
            return GetAllDocTypes();
        }

        public void BackupXMLFile()
        {
            string backupFname = GetBackupFilename(xmlFName);
            try
            {
                File.Copy(xmlFName, backupFname);
            }
            catch
            {
            }
        }

        public string GetBackupFilename(string fName)
        {
            // Create the file name and loop incrementing the uniqueness counter until suitable file name is found
            // that doesn't already exist in the destination folder
            string uniqueFileName = "";
            int uniquenessVal = 0;
            // Split current name
            string pathOnly = Path.GetDirectoryName(fName);
            string fileWithoutExt = Path.GetFileNameWithoutExtension(fName);
            string fileExt = Path.GetExtension(fName);

            while (uniquenessVal < 100000)
            {
                // Form full name
                uniqueFileName = ComputeXMLFileName(pathOnly, fName, uniquenessVal, fileExt);

                // Deal with uniqueness
                if (File.Exists(uniqueFileName))
                {
                    //increment uniqueness to try to find a unique name
                    uniquenessVal++;
                }
                else
                {
                    // Not a duplicate
                    break;
                }
            }
            return uniqueFileName;
        }

        private string ComputeXMLFileName(string pathOnly, string fName, int uniqVal, string extForLog)
        {
            if (pathOnly.Substring(pathOnly.Length - 1) != "\\")
                pathOnly += "\\";
            if (uniqVal == 0)
                return Path.GetFileName(fName);
            string pp = Path.GetFileNameWithoutExtension(fName);
            pp += "_" + uniqVal.ToString("0000");
            pp += Path.GetExtension(fName);
            return pp;
        }

        public class CheckItem
        {
            public string checkString;
            public int numInstances = 1;

            public CheckItem(string str, string req)
            {
                checkString = str;
                try
                {
                    numInstances = Convert.ToInt32(req);
                }
                catch
                {
                }
            }

            public bool Compare(CheckItem other)
            {
                if (checkString != other.checkString)
                    return false;
                if (numInstances != other.numInstances)
                    return false;
                return true;
            }
        }

        public class GrabItem
        {
            public string name;
            public string from;
            public string str;

            public GrabItem(string aname, string afrom, string astr)
            {
                name = aname;
                from = afrom;
                str = astr;
            }
        }

        public class DocType
        {
            public enum CheckItemType
            {
                ciGOOD = 0,
                ciBAD = 1
            }

            public List<CheckItem> goodStrings = new List<CheckItem>();
            public List<CheckItem> badStrings = new List<CheckItem>();
            public string dtName = "";
            public List<GrabItem> grabTexts = new List<GrabItem>();
            public string moveTo = "";
            public string renameTo = "";
            public List<string> thumbFileNames = new List<string>();

            public DocType()
            {
                Clear();
            }

            public void Clear()
            {
                dtName = "";
                goodStrings.Clear();
                badStrings.Clear();
                grabTexts.Clear();
                moveTo = "";
                renameTo = "";
                thumbFileNames.Clear();
            }

            public void SetFields(string name, string goodField, string badField, List<GrabItem> grabField, string moveToField, string renameToField)
            {
                dtName = name;

                // Extract field contents
                goodStrings = GetCheckItems(goodField);
                badStrings = GetCheckItems(badField);
                grabTexts = grabField;
                moveTo = moveToField;
                renameTo = renameToField;

                // Clear thumbs as this is for form data only
                thumbFileNames.Clear();
            }

            private List<CheckItem> GetCheckItems(string field)
            {
                List<CheckItem> cis = new List<CheckItem>();
                string[] ss = Regex.Split(field, ",");
                foreach (string s in ss)
                {
                    string strim = s.Trim();
                    if (strim == "")
                        continue;
                    string spog = strim;
                    Match m = Regex.Match(strim, @"( \((\d+)\))");
                    string req = "1";
                    if (m.Groups.Count > 1)
                    {
                        if (m.Groups.Count >= 3)
                            req = m.Groups[2].Value;
                        spog = strim.Substring(0, m.Index);
                    }
                    cis.Add(new CheckItem(spog, req));

                }
                return cis;
            }

            public string FormatCheckItems(CheckItemType citype)
            {
                string sss = "";
                List<CheckItem> cis = goodStrings;
                if (citype == CheckItemType.ciBAD)
                    cis = badStrings;
                for (int ii = 0; ii < cis.Count; ii++)
                {
                    sss += (sss == "" ? "" : ", ") + cis[ii].checkString;
                    if (cis[ii].numInstances > 1)
                        sss += " (" + cis[ii].numInstances + ")";
                }
                return sss;
            }

            public bool Compare(DocType other, ref Dictionary<string, bool> changedFields)
            {
                bool bDiff = false;
                if (!CompareCheckItems(goodStrings, other.goodStrings))
                {
                    bDiff = true;
                    changedFields.Add("goodStrings", true);
                }
                if (!CompareCheckItems(badStrings, other.badStrings))
                {
                    bDiff = true;
                    changedFields.Add("badStrings", true);
                }
                if (grabTexts.Count != other.grabTexts.Count)
                {
                    bDiff = true;
                    changedFields.Add("grabTexts", true);
                }
                if (moveTo != other.moveTo)
                {
                    bDiff = true;
                    changedFields.Add("moveTo", true);
                }
                if (renameTo != other.renameTo)
                {
                    bDiff = true;
                    changedFields.Add("renameTo", true);
                }
                if (dtName != other.dtName)
                {
                    bDiff = true;
                    changedFields.Add("dtName", true);
                }
                return bDiff;
            }

            private bool CompareCheckItems(List<CheckItem> cis, List<CheckItem> other)
            {
                if (cis.Count != other.Count)
                    return false;
                for (int i = 0; i < cis.Count; i++)
                    if (!cis[i].Compare(other[i]))
                        return false;
                return true;
            }

            public DocType Copy()
            {
                DocType dt = new DocType();
                dt.dtName = dtName;
                foreach (CheckItem ci in goodStrings)
                    dt.goodStrings.Add(new CheckItem(ci.checkString, ci.numInstances.ToString()));
                foreach (CheckItem ci in badStrings)
                    dt.badStrings.Add(new CheckItem(ci.checkString, ci.numInstances.ToString()));
                foreach (GrabItem gi in grabTexts)
                    dt.grabTexts.Add(new GrabItem(gi.name, gi.from, gi.str));
                dt.moveTo = moveTo;
                dt.renameTo = renameTo;
                foreach (string ss in thumbFileNames)
                    dt.thumbFileNames.Add(ss);
                return dt;
            }
        }

    }
}
