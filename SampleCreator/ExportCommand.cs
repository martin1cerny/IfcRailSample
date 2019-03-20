using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Autodesk.Revit.UI;
using SampleGenerator;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xbim.IfcRail;
using Xbim.IfcRail.Kernel;
using Xbim.IO.Memory;

namespace SampleCreator
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var document = commandData?.Application?.ActiveUIDocument?.Document;
            if (document == null || document.IsFamilyDocument)
            {
                TaskDialog.Show("Information", "This tool only works for models, not for the family editor.");
                return Result.Cancelled;
            }

            var path = document.PathName;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                TaskDialog.Show("Information", "File must be saved first.");
                return Result.Cancelled;
            }
            Init.SetLogger(path);

            //export to IFC4 DTV
            var ifcPath = Path.ChangeExtension(document.PathName, ".ifc");
            var ifcFileName = Path.GetFileName(ifcPath);
            var ifcDir = Path.GetDirectoryName(ifcPath);
            var ifcOptions = new IFCExportOptions();
            var options = GetConfiguration();
            options.UpdateOptions(ifcOptions, null);
            using (var txn = new Transaction(document))
            {
                txn.Start("Exporting to IFC");
                Log.Information($"Exporting IFC: {ifcPath}");
                document.Export(ifcDir, ifcFileName, ifcOptions);
                Log.Information($"Exported IFC: {ifcPath}");

                // keep it clean
                txn.RollBack();
            }

            Log.Information("Transforming the file (changing schema, removing unnecessary comments)");
            FileTransformations.MakeIfcRail(ifcPath);

            using (var model = ModelHelper.GetModel(ifcPath))
            {
                var w = Stopwatch.StartNew();
                using (var txn = model.BeginTransaction("Model enhancements"))
                {
                    Log.Information("Transforming the model: Changing entity types to IFC Rail entities");
                    TypeChanger.ChangeBuildingToRailway(model);
                    w.Stop();
                    Log.Information($"Types changed in {w.ElapsedMilliseconds}ms");


                    Log.Information("Enriching the model: Exporting IfcAlignments from imported DWGs");
                    var alignment = new AlignmentExporter(document, model);
                    alignment.Export();

                    // enhance the model
                    txn.Commit();
                }


                Log.Information("Saving transformed and enhanced IFC.");
                using (var stream = File.Create(ifcPath))
                {
                    //// after a lot of transformations is it a good idea to purge
                    //using (var clean = ModelHelper.GetCleanModel(model))
                    //{
                    //    clean.SaveAsStep21(stream);
                    //}
                    model.SaveAsStep21(stream);
                }
            }

            TaskDialog.Show("Finished", "Export and data transformation finished");
            return Result.Succeeded;
        }



        private static IFCExportConfiguration GetConfiguration()
        {
            var options = IFCExportConfiguration.CreateDefaultConfiguration();
            //core options
            options.IFCFileType = IFCFileFormat.Ifc;
            options.IFCVersion = IFCVersion.IFC4DTV;
            options.SpaceBoundaries = 1; // space boundaries provide useful information
            options.SplitWallsAndColumns = false;
            options.IncludeSiteElevation = true;

            //combination of the following two options will use schedules which have PSET|IFC|COMMON in the name
            //as property sets. This allows for simple filtering and general overview
            options.ExportSchedulesAsPsets = false;
            options.ExportSpecificSchedules = false;

            // people from Autodesk already made an effort to create 
            // mapping for common properties and these are mostly sensible
            // but some of them are just made up on the fly
            options.ExportIFCCommonPropertySets = false;
            options.ExportInternalRevitPropertySets = true;
            options.ExportBaseQuantities = false;

            //options.ExportUserDefinedPsets
            //options.ExportUserDefinedPsetsFileName

            //this is only used for reference properties (that is hardly anything)
            //it doesn't solve poor naming of IfcElements (like '2000x3000')
            options.UseFamilyAndTypeNameForReference = true;

            //this should save some space
            options.ExportSolidModelRep = true;

            //TODO: Export linked files as other IFC outputs?
            //options.ExportLinkedFiles = true;

            options.StoreIFCGUID = false;

            return options;
        }
    }


}
