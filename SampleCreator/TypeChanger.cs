using System.Collections.Generic;
using System.Linq;
using Xbim.Common;
using Xbim.IfcRail.ElectricalDomain;
using Xbim.IfcRail.Kernel;
using Xbim.IfcRail.ProductExtension;
using Xbim.IfcRail.RailwayDomain;
using Xbim.IfcRail.SharedBldgElements;
using Xbim.IO.Memory;

namespace SampleCreator
{
    class TypeChanger
    {
        public static void ChangeTypes(MemoryModel model)
        {
            var typeRels = model.Instances.OfType<IfcRelDefinesByType>().ToList();
            var i = model.Instances;

            model.Replace<IfcBuilding, IfcRailway>(i.OfType<IfcBuilding>());
            model.Replace<IfcBuildingStorey, IfcRailwayPart>(i.OfType<IfcBuildingStorey>());


            // cable carriers "Kabelkanäle"
            var cableCarriers = i.Where<IfcBuildingElementProxy>(p => p.Name.ToString().StartsWith("Kabelkanäle")).ToList();
            var cableCarrierTypes = GetTypes<IfcBuildingElementProxyType>(cableCarriers, typeRels);
            model.Replace<IfcBuildingElementProxy, IfcCableCarrierSegment>(cableCarriers);
            model.Replace<IfcBuildingElementProxyType, IfcCableCarrierSegmentType>(cableCarrierTypes);


            // sleepers
            var sleepers = i.Where<IfcRailing>(r => r.Name.ToString().StartsWith("Betonschwelle")).ToList();
            var sleeperTypes = GetTypes<IfcRailingType>(sleepers, typeRels);
            model.Replace<IfcRailing, IfcRailElement>(sleepers, (o, n) => n.PredefinedType = IfcRailElementTypeEnum.SLEEPER);
            model.Replace<IfcRailingType, IfcRailElementType>(sleeperTypes, (o, n) => n.PredefinedType = IfcRailElementTypeEnum.SLEEPER);

            // check rails
            var checkRails = i.Where<IfcRailing>(r => r.Name.ToString().Contains("Radlenker")).ToList();
            var checkRailTypes = GetTypes<IfcRailingType>(checkRails, typeRels);
            model.Replace<IfcRailing, IfcRailElement>(checkRails, (o, n) => n.PredefinedType = IfcRailElementTypeEnum.CHECK_RAIL);
            model.Replace<IfcRailingType, IfcRailElementType>(checkRailTypes, (o, n) => n.PredefinedType = IfcRailElementTypeEnum.CHECK_RAIL);


            // rail joints
            var railJoints = i.Where<IfcRailing>(r =>
            {
                var name = r.Name.ToString();
                if (name.Contains("_Herzstück_"))
                    return true;
                if (name.Contains("_Herz_Ende_"))
                    return true;
                return false;
            }).ToList();
            var railJointTypes = GetTypes<IfcRailingType>(railJoints, typeRels);
            model.Replace<IfcRailing, IfcRailElement>(railJoints, (o, n) => n.PredefinedType = IfcRailElementTypeEnum.JOINT);
            model.Replace<IfcRailingType, IfcRailElementType>(railJointTypes, (o, n) => n.PredefinedType = IfcRailElementTypeEnum.JOINT);


            // rails
            var rails = i.Where<IfcRailing>(r => r.Name.ToString().Contains("Radlenker")).ToList();
            var railTypes = GetTypes<IfcRailingType>(rails, typeRels);
            model.Replace<IfcRailing, IfcRailElement>(checkRails, (o, n) => n.PredefinedType = IfcRailElementTypeEnum.RAIL);
            model.Replace<IfcRailingType, IfcRailElementType>(railTypes, (o, n) => n.PredefinedType = IfcRailElementTypeEnum.RAIL);

            // terrain
            var terrain = i.Where<IfcBuildingElementProxy>(r => r.Name.ToString().StartsWith("DirectShape(Geometry = Surface, Category = Topography")).ToList();
            var terrainTypes = GetTypes<IfcBuildingElementProxyType>(terrain, typeRels);
            model.Replace<IfcBuildingElementProxy, IfcGeographicElement>(terrain, (o, t) => t.PredefinedType = IfcGeographicElementTypeEnum.TERRAIN);
            model.Replace<IfcBuildingElementProxyType, IfcGeographicElementType>(terrainTypes, (o, t) => t.PredefinedType = IfcGeographicElementTypeEnum.TERRAIN);
        }

        private static IEnumerable<T> GetTypes<T>(IEnumerable<IPersistEntity> entities, IEnumerable<IfcRelDefinesByType> typeRels)
        {
            var test = new HashSet<IPersistEntity>(entities);
            return new HashSet<T>(typeRels
                .Where(r => r.RelatedObjects.Any(o => test.Contains(o)))
                .Select(r => r.RelatingType)
                .OfType<T>());
        }
    }
}
