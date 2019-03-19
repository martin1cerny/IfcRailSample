using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleCreator
{
    internal class FileTransformations
    {
        public static void MakeIfcRail(string filePath)
        {
            var factory = new Xbim.IfcRail.EntityFactoryIfcRailPilot();
            var schemaId = factory.SchemasIds.FirstOrDefault();

            var tmp = Path.ChangeExtension(filePath, ".ifc.tmp");
            using (var w = File.CreateText(tmp))
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    // skip empty lines
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // remove comments clutter
                    if (line[0] == '/' || line[0] == '*')
                        continue;

                    // schema identifier change
                    if (line[0] == 'F' && line.StartsWith("FILE_SCHEMA"))
                    {
                        var idLine = line.Replace("IFC4", schemaId);
                        w.WriteLine(idLine);
                        continue;
                    }

                    // just copy the line
                    w.WriteLine(line);
                }
            }

            // replace original file with reilway model
            File.Replace(tmp, filePath, Path.ChangeExtension(filePath, ".bck"));
        }

        
    }
}
