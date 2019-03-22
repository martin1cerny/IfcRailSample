using Serilog;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xbim.Common;
using Xbim.Common.Exceptions;
using Xbim.Common.Metadata;
using Xbim.IfcRail;
using Xbim.IfcRail.Kernel;
using Xbim.IfcRail.UtilityResource;
using Xbim.IO;
using Xbim.IO.Memory;

namespace SampleCreator
{
    internal class ModelHelper
    {
        public static MemoryModel GetModel(string path)
        {
            var model = new MemoryModel(new EntityFactoryIfcRailPilot());
            model.LoadStep21(path);
            SetUpOwnerHistory(model);

            return model;
        }

        private static void SetUpOwnerHistory(IModel model)
        {
            var i = model.Instances;
            using (var txn = model.BeginTransaction("Owner history set up"))
            {
                // enhance header
                model.Header.FileDescription.Description.Clear(); // clear any MVD
                model.Header.FileName.AuthorizationMailingAddress.Clear();
                model.Header.FileName.AuthorizationMailingAddress.Add("info@xbim.net");
                model.Header.FileName.AuthorizationName = "";
                model.Header.FileName.AuthorName.Clear();
                model.Header.FileName.AuthorName.Add("xBIM Team");
                model.Header.FileName.Name = "IFC Rail Example";
                model.Header.FileName.Organization.Clear();
                model.Header.FileName.Organization.Add("buildingSMART");
                model.Header.FileName.OriginatingSystem = "xBIM Toolkit";
                model.Header.FileName.PreprocessorVersion = typeof(IModel).Assembly.ImageRuntimeVersion;


                var oh = i.FirstOrDefault<IfcOwnerHistory>();
                oh.OwningApplication.ApplicationDeveloper.Addresses.Clear();
                oh.OwningApplication.ApplicationDeveloper.Description = "xBIM Team";
                oh.OwningApplication.ApplicationDeveloper.Identification = "xBIM";
                oh.OwningApplication.ApplicationDeveloper.Name = "xBIM Team";
                oh.OwningApplication.ApplicationDeveloper.Roles.Clear();
                oh.OwningApplication.ApplicationFullName = "xBIM for IFC Rail";
                oh.OwningApplication.ApplicationIdentifier = "XBIM4RAIL";
                oh.OwningApplication.Version = "1.0";

                model.EntityNew += entity =>
                {
                    if (entity is IfcRoot root)
                    {
                        root.OwnerHistory = oh;
                        root.GlobalId = Guid.NewGuid();
                    }
                };


                txn.Commit();
            }
        }

    }
}
