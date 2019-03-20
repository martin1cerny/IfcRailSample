using System.Collections.Generic;
using System.Linq;
using Xbim.Common;
using Xbim.IfcRail.ElectricalDomain;
using Xbim.IfcRail.Kernel;
using Xbim.IfcRail.ProductExtension;
using Xbim.IfcRail.RailwayDomain;
using Xbim.IfcRail.SharedBldgElements;

namespace SampleCreator
{
    class TypeChanger
    {
        public static void ChangeTypes(IModel model)
        {
            var typeRels = model.Instances.OfType<IfcRelDefinesByType>().ToList();

            foreach (var building in model.Instances.OfType<IfcBuilding>().ToList())
            {
                var railway = ModelHelper.InsertCopy<IfcRailway>(model, building);
                ModelHelper.Replace(model, building, railway);
                model.Delete(building);
            }

            foreach (var storey in model.Instances.OfType<IfcBuildingStorey>().ToList())
            {
                var railwayPart = ModelHelper.InsertCopy<IfcRailwayPart>(model, storey);
                ModelHelper.Replace(model, storey, railwayPart);
                model.Delete(storey);
            }

            // cable carriers "Kabelkanäle"
            var cableCarriers = new HashSet<IfcBuildingElementProxy>(model
                .Instances.Where<IfcBuildingElementProxy>(p => p.Name.ToString().StartsWith("Kabelkanäle")));
            var cableCarrierTypes = typeRels
                .Where(r => r.RelatedObjects.Any(o => o is IfcBuildingElementProxy p && cableCarriers.Contains(p)))
                .Select(r => r.RelatingType)
                .OfType<IfcBuildingElementProxyType>()
                .ToList();
            ModelHelper.Replace<IfcCableCarrierSegment, IfcBuildingElementProxy>(model, cableCarriers);
            ModelHelper.Replace<IfcCableCarrierSegmentType, IfcBuildingElementProxyType>(model, cableCarrierTypes);
            foreach (var cc in cableCarriers)
                model.Delete(cc);
            foreach (var cc in cableCarrierTypes)
                model.Delete(cc);


            // sleepers
            foreach (var railings in model.Instances.Where<IfcRailing>(r => r.Name.ToString().StartsWith("Betonschwelle")).ToList())
            {
                var sleeper = ModelHelper.InsertCopy<IfcRailElement>(model, railings);
                sleeper.PredefinedType = IfcRailElementTypeEnum.SLEEPER;
                ModelHelper.Replace(model, railings, sleeper);
                model.Delete(railings);

                var type = sleeper.IsTypedBy.FirstOrDefault()?.RelatingType;
                if (type != null && !(type is IfcRailElementType))
                {
                    var sleeperType = ModelHelper.InsertCopy<IfcRailElementType>(model, type);
                    sleeperType.PredefinedType = IfcRailElementTypeEnum.SLEEPER;
                    ModelHelper.Replace(model, type, sleeperType);
                    model.Delete(type);
                }
            }

            // check rails
            foreach (var railings in model.Instances.Where<IfcRailing>(r => r.Name.ToString().Contains("Radlenker")).ToList())
            {
                var sleeper = ModelHelper.InsertCopy<IfcRailElement>(model, railings);
                sleeper.PredefinedType = IfcRailElementTypeEnum.CHECK_RAIL;
                ModelHelper.Replace(model, railings, sleeper);
                model.Delete(railings);

                var type = sleeper.IsTypedBy.FirstOrDefault()?.RelatingType;
                if (type != null && !(type is IfcRailElementType))
                {
                    var sleeperType = ModelHelper.InsertCopy<IfcRailElementType>(model, type);
                    sleeperType.PredefinedType = IfcRailElementTypeEnum.CHECK_RAIL;
                    ModelHelper.Replace(model, type, sleeperType);
                    model.Delete(type);
                }
            }

            // rail joints
            foreach (var railings in model.Instances.Where<IfcRailing>(r =>
            {
                var name = r.Name.ToString();
                if (name.Contains("_Herzstück_"))
                    return true;
                if (name.Contains("_Herz_Ende_"))
                    return true;
                return false;
            }).ToList())
            {
                var sleeper = ModelHelper.InsertCopy<IfcRailElement>(model, railings);
                sleeper.PredefinedType = IfcRailElementTypeEnum.JOINT;
                ModelHelper.Replace(model, railings, sleeper);
                model.Delete(railings);

                var type = sleeper.IsTypedBy.FirstOrDefault()?.RelatingType;
                if (type != null && !(type is IfcRailElementType))
                {
                    var sleeperType = ModelHelper.InsertCopy<IfcRailElementType>(model, type);
                    sleeperType.PredefinedType = IfcRailElementTypeEnum.JOINT;
                    ModelHelper.Replace(model, type, sleeperType);
                    model.Delete(type);
                }
            }

            // rails
            foreach (var railings in model.Instances.Where<IfcRailing>(r => r.Name.ToString().StartsWith("Schienen")).ToList())
            {
                var sleeper = ModelHelper.InsertCopy<IfcRailElement>(model, railings);
                sleeper.PredefinedType = IfcRailElementTypeEnum.RAIL;
                ModelHelper.Replace(model, railings, sleeper);
                model.Delete(railings);

                var type = sleeper.IsTypedBy.FirstOrDefault()?.RelatingType;
                if (type != null && !(type is IfcRailElementType))
                {
                    var sleeperType = ModelHelper.InsertCopy<IfcRailElementType>(model, type);
                    sleeperType.PredefinedType = IfcRailElementTypeEnum.RAIL;
                    ModelHelper.Replace(model, type, sleeperType);
                    model.Delete(type);
                }
            }

            // terrain
            foreach (var proxy in model.Instances.Where<IfcBuildingElementProxy>(r => r.Name.ToString().StartsWith("DirectShape(Geometry = Surface, Category = Topography")).ToList())
            {
                var terrain = ModelHelper.InsertCopy<IfcGeographicElement>(model, proxy);
                terrain.PredefinedType = IfcGeographicElementTypeEnum.TERRAIN;
                ModelHelper.Replace(model, proxy, terrain);
                model.Delete(proxy);

                var type = terrain.IsTypedBy.FirstOrDefault()?.RelatingType;
                if (type != null && !(type is IfcRailElementType))
                {
                    var sleeperType = ModelHelper.InsertCopy<IfcGeographicElementType>(model, type);
                    sleeperType.PredefinedType = IfcGeographicElementTypeEnum.TERRAIN;
                    ModelHelper.Replace(model, type, sleeperType);
                    model.Delete(type);
                }
            }
        }
    }
}
