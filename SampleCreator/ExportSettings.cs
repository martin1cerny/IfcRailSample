using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleCreator
{
    class ExportSettings
    {
        public List<AlignmentSettings> Alignments { get; set; } = new List<AlignmentSettings>();

        public static ExportSettings GetFor(Document doc)
        {
            var path = doc.PathName;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new ExportSettings();

            path = Path.ChangeExtension(path, ".json");
            if (!File.Exists(path))
                return new ExportSettings();

            var data = File.ReadAllText(path);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<ExportSettings>(data);
        }

        public void SaveFor(Document doc)
        {
            var path = doc.PathName;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new ArgumentException("Document must be saved first");

            path = Path.ChangeExtension(path, ".json");
            var data = Newtonsoft.Json.JsonConvert.SerializeObject(this);
            File.WriteAllText(path, data);
        }
    }

    public class AlignmentSettings
    {
        public string Name { get; set; }
        public List<int> Elements { get; set; } = new List<int>();
        public double StartOffset { get; set; }
    }
}
