using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SDK_HD.Dim
{
    internal sealed class WcUserSettings
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>();
        private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KTool", "WcDeployment.xml");
        internal string Get(string key, string fallback) => values.TryGetValue(key, out var value) ? value : fallback;
        internal void Set(string key, string value) { values[key] = value ?? ""; }
        internal static WcUserSettings Load()
        {
            var result = new WcUserSettings();
            try
            {
                if (File.Exists(FilePath)) foreach (var item in XDocument.Load(FilePath).Root.Elements("Setting"))
                    result.Set((string)item.Attribute("key"), (string)item.Attribute("value"));
            }
            catch (Exception) { /* A damaged file must not prevent opening the tool. */ }
            return result;
        }
        internal void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                new XDocument(new XElement("WcSettings", new XAttribute("version", "1"), values.OrderBy(p => p.Key)
                    .Select(p => new XElement("Setting", new XAttribute("key", p.Key), new XAttribute("value", p.Value))))).Save(temp);
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null); else File.Move(temp, FilePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
