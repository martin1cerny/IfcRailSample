﻿using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.IfcRail.GeometricConstraintResource;
using Xbim.IfcRail.GeometricModelResource;
using Xbim.IfcRail.GeometryResource;
using Xbim.IfcRail.PresentationAppearanceResource;
using Xbim.IfcRail.ProductExtension;
using Xbim.IfcRail.ProfileResource;
using Xbim.IfcRail.RepresentationResource;
using Xbim.IfcRail.UtilityResource;

namespace SampleCreator
{
    internal class AlignmentExporter
    {
        private readonly Document _document;
        private readonly IModel _model;
        private IEntityCollection i => _model.Instances;
        private readonly List<AlignmentRecord> _alignments = new List<AlignmentRecord>();
        private readonly ExportSettings _settings;
        private readonly DisplayUnitType _lengthUnit;
        private readonly DisplayUnitType _angleUnit;

        public AlignmentExporter(Document document, IModel model)
        {
            _document = document;
            _model = model;
            _settings = ExportSettings.GetFor(document);
            _lengthUnit = document.GetUnits().GetFormatOptions(UnitType.UT_Length).DisplayUnits;
            _angleUnit = document.GetUnits().GetFormatOptions(UnitType.UT_Angle).DisplayUnits;
        }

        public void Export()
        {
            // create alignments first
            CreateAlignments();

            // match elements to alignments using addaptive points from Revit
            MatchElementsToAlignments();

            // transform local placements to linear placements
            TransformPlacements();
        }

        private void TransformPlacements()
        {
            var sitePlacement = i.FirstOrDefault<IfcSite>()?.ObjectPlacement;
            foreach (var alignment in _alignments)
            {
                var segments = alignment.Segments.ToList();
                foreach (var kvp in alignment.Elements.Where(k => k.Value != null))
                {
                    var element = kvp.Key;
                    var ifcElement = kvp.Value;

                    var lPlacement = ifcElement.ObjectPlacement as IfcLocalPlacement;
                    if (lPlacement?.RelativePlacement == null)
                        continue;

                    // get placement aggregated to the level of site (excluding site)
                    var matrix = lPlacement.RelativePlacement.ToMatrix3D();
                    while (lPlacement.PlacementRelTo != null && lPlacement.PlacementRelTo != sitePlacement)
                    {
                        lPlacement = lPlacement.PlacementRelTo as IfcLocalPlacement;
                        if (lPlacement == null)
                            break;
                        matrix = matrix * lPlacement.RelativePlacement.ToMatrix3D();
                    }

                    var position = new XbimPoint3D(0, 0, 0);
                    position = matrix.Transform(position);

                    var vertDir = new XbimVector3D(0, 0, 1);
                    vertDir = matrix.Transform(vertDir).Normalized();

                    var xDir = new XbimVector3D(1, 0, 0);
                    xDir = matrix.Transform(xDir).Normalized();

                    // find the segment
                    // find the distance
                    // find the offset
                    // find the rotation
                    Intersection intersection = null;
                    var distance = 0d;
                    foreach (var segment in segments)
                    {
                        var line = segment.CurveGeometry as IfcLineSegment2D;
                        if (line == null)
                            throw new NotSupportedException("Only line segments are supported now");
                        intersection = GetIntersection2D(line, position);
                        if (intersection != null)
                            break;
                        distance += line.SegmentLength;
                    }

                    if (intersection == null)
                    {
                        Log.Warning($"Object placement for {ifcElement} could not be created because intersection was not found.");
                        continue;
                    }


                    ifcElement.ObjectPlacement = i.New<IfcLinearPlacement>(lp =>
                    {
                        lp.Distance = i.New<IfcDistanceExpression>(d =>
                        {
                            d.AlongHorizontal = true;
                            d.OffsetVertical = position.Z;
                            d.DistanceAlong = distance + intersection.DistanceAlong;
                            d.OffsetLateral = intersection.OffsetLateral;
                        });
                        lp.PlacementRelTo = sitePlacement;
                        lp.PlacementMeasuredAlong = alignment.Alignment.Axis;
                        lp.Orientation = i.New<IfcOrientationExpression>(o =>
                        {
                            o.VerticalAxisDirection = i.New<IfcDirection>(d => d.SetXYZ(vertDir.X, vertDir.Y, vertDir.Z));
                            o.LateralAxisDirection = i.New<IfcDirection>(d => d.SetXYZ(xDir.X, xDir.Y, xDir.Z));
                        });
                    });
                }
            }
        }

        private Intersection GetIntersection2D(IfcLineSegment2D segment, XbimPoint3D point)
        {
            // 2D start point
            var start = new XbimPoint3D(segment.StartPoint.X, segment.StartPoint.Y, 0);

            // normal equation of the segment
            var a = Math.Tan(segment.StartDirection * Math.PI / 180.0);
            var b = start.Y - a * start.X;

            var c = Math.Tan((segment.StartDirection + 90) * Math.PI / 180.0);
            var d = point.Y - c * point.X;

            var x = (d - b) / (a - c);
            var y = c * x + d;

            var intersection = new XbimPoint3D(x, y, 0);

            // check if this is within the bounds of the segment
            var diff = intersection - start;
            var length = diff.Length;
            if (length > segment.SegmentLength)
            {
                return null;
            }

            var angle = Math.Atan2(diff.Y, diff.X) * 180.0 / Math.PI;
            // normalize the angle
            if (angle < 0.0)
                angle += 360.0;
            if (angle > 360.0)
                angle -= 360.0;

            if (Math.Abs(angle - segment.StartDirection) > 1e-5)
            {
                // it is possible that this is the first segment and the placement is
                // slightly before the start of the segment.
                // We should axtend the first segment in that case.
                return null;
            }


            var offset = point - intersection;
            var bearing = Math.Atan2(offset.Y, offset.X) * 180.0 / Math.PI;
            var sign = (bearing - segment.StartDirection) > 0.0 ? 1.0 : -1.0;

            return new Intersection
            {
                DistanceAlong = length,
                OffsetLateral = offset.Length * sign
            };

        }

        private double GetDistance(XbimPoint3D a, IfcCartesianPoint b)
        {
            return XbimPoint3D.Subtract(a, b.XbimPoint3D()).Length;
        }

        private void CreateAlignments()
        {
            var imports = new FilteredElementCollector(_document)
               .OfClass(typeof(ImportInstance))
               .ToElements()
               .Cast<ImportInstance>()
               .GroupBy(e =>
                 _settings.Alignments.FirstOrDefault(a => a.Elements.Contains(e.Id.IntegerValue))?.Name ??
                 (e.Category != null ?
                    $"{e.Category.Name}: {e.Name}" :
                    e.UniqueId))
               .ToList();
            foreach (var group in imports)
            {
                var name = group.Key;
                var polylines = OrderConnectedPolylines(group);
                if (polylines.Count == 0)
                    continue;
                var alignmentSettings = _settings.Alignments.FirstOrDefault(a => a.Name == name);

                // export as IfcAlignment and curve
                var alignment = i.New<IfcAlignment>(a =>
                {
                    a.Name = name;
                    //a.GlobalId = GetIfcGUID(dwg);
                    a.Representation = i.New<IfcProductDefinitionShape>(r => r.Representations.Add(i.New<IfcShapeRepresentation>(s =>
                    {
                        s.ContextOfItems = ModelContext;
                        s.Items.AddRange(GetSolids(polylines));
                        SetRedStyle(s.Items);
                    })));
                    a.ObjectPlacement = i.New<IfcLocalPlacement>(op =>
                    {
                        op.PlacementRelTo = i.FirstOrDefault<IfcSite>()?.ObjectPlacement;
                        op.RelativePlacement = i.New<IfcAxis2Placement3D>();
                    });
                    a.Axis = GetAlignmentCurve(polylines, alignmentSettings?.StartOffset ?? 0);
                });
                var record = new AlignmentRecord
                {
                    Alignment = alignment,
                    Polylines = polylines
                };
                _alignments.Add(record);
            }
        }

        private void MatchElementsToAlignments()
        {
            // find all elements for the curves (railings and sleepers)
            var railParts = new FilteredElementCollector(_document)
                .OfClass(typeof(FamilyInstance))
                .ToElements()
                .Cast<FamilyInstance>()
                .Where(e => AdaptiveComponentInstanceUtils.IsAdaptiveComponentInstance(e))
                .ToList();
            foreach (var element in railParts)
            {
                // get adaptive points
                var points = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(element)
                    .Select(r => _document.GetElement(r))
                    .Cast<ReferencePoint>()
                    .ToList();

                // match addaptive points to curves
                if (points.Count > 0)
                {
                    foreach (var record in _alignments)
                    {
                        if (ArePointsOnCurve(record.Polylines, points))
                        {
                            // find IFC element
                            IfcGloballyUniqueId id = GetIfcGUID(element);
                            var ifcElement = i.FirstOrDefault<IfcBuildingElement>(e => e.GlobalId == id);
                            record.Elements.Add(element, ifcElement);
                        }
                    }
                }
            }
        }

        private List<PolyLine> OrderConnectedPolylines(IEnumerable<ImportInstance> instances)
        {
            var polylines = instances.SelectMany(inst =>
            {
                var geom = inst.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Medium, IncludeNonVisibleObjects = false, ComputeReferences = false });
                return GetPolylines(geom);
            });
            return OrderConnectedPolylines(polylines.ToList());
        }


        private List<PolyLine> OrderConnectedPolylines(List<PolyLine> polylines)
        {
            // make own list we can modify
            polylines = polylines.ToList();
            if (polylines.Count == 0)
                return new List<PolyLine>();

            var ordered = new List<PolyLine> { polylines[0] };
            polylines.RemoveAt(0);

            while (polylines.Count > 0)
            {
                var first = ordered.First();
                var last = ordered.Last();

                var start = first.GetCoordinate(0);
                var end = last.GetCoordinate(last.NumberOfCoordinates - 1);

                var before = polylines.FirstOrDefault(p => p.GetCoordinate(p.NumberOfCoordinates - 1).IsAlmostEqualTo(start, 1e-3));
                var after = polylines.FirstOrDefault(p => p.GetCoordinate(0).IsAlmostEqualTo(end, 1e-3));

                if (before != null)
                {
                    ordered.Insert(0, before);
                    polylines.Remove(before);
                }

                if (after != null)
                {
                    ordered.Insert(ordered.Count, after);
                    polylines.Remove(after);
                }

                if (before == null && after == null && polylines.Count > 0)
                    throw new ArgumentException("Polylines don't form continuous curve");
            }

            return ordered;
        }

        private bool ArePointsOnCurve(IEnumerable<PolyLine> polylines, List<ReferencePoint> points)
        {
            return points.All(p => polylines.Any(polyline => IsPointOnCurve(polyline, p)));
        }

        private bool IsPointOnCurve(PolyLine polyline, ReferencePoint point)
        {
            var coords = polyline.GetCoordinates();
            for (int i = 0; i < coords.Count - 1; i++)
            {
                var start = coords[i];
                var end = coords[i + 1];
                var dir = end - start;

                var test = point.Position - start;
                var area = test.CrossProduct(dir);

                if (!area.IsZeroLength())
                    continue;
                var delta = Math.Abs(dir.GetLength() - test.GetLength() - (point.Position - end).GetLength());
                if (delta > 1e-5)
                    continue;
                return true;
            }
            return false;
        }

        private double FromFeets(double x)
        {
            return UnitUtils.ConvertFromInternalUnits(x, _lengthUnit);
        }

        private IfcCartesianPoint GetPoint2D(XYZ p)
        {
            return i.New<IfcCartesianPoint>(c => c.SetXY(FromFeets(p.X), FromFeets(p.Y)));
        }

        private IfcCartesianPoint GetPoint3D(XYZ p)
        {
            return i.New<IfcCartesianPoint>(c => c.SetXYZ(FromFeets(p.X), FromFeets(p.Y), FromFeets(p.Z)));
        }

        private XYZ GetXYWithUnits(XYZ p)
        {
            return new XYZ(FromFeets(p.X), FromFeets(p.Y), 0);
        }

        private IfcAlignmentCurve GetAlignmentCurve(List<PolyLine> lines, double startOffset)
        {
            var points = lines
                .SelectMany(l => l.GetCoordinates())
                .ToList();
            var segments = new List<IfcAlignment2DHorizontalSegment>();
            for (int j = 0; j < points.Count - 1; j++)
            {
                var start = points[j];
                var end = points[j + 1];
                if ((end - start).IsZeroLength())
                    continue;

                var dX = end.X - start.X;
                var dY = end.Y - start.Y;
                var direction = UnitUtils.Convert(Math.Atan2(dY, dX), DisplayUnitType.DUT_RADIANS, _angleUnit);
                var fullCircle = UnitUtils.Convert(360, DisplayUnitType.DUT_DECIMAL_DEGREES, _angleUnit);
                if (direction < 0)
                    direction += fullCircle;
                var length = FromFeets(Math.Sqrt(dX * dX + dY * dY));


                var segment = i.New<IfcAlignment2DHorizontalSegment>(s => s.CurveGeometry = i.New<IfcLineSegment2D>(l =>
                {
                    l.StartPoint = GetPoint2D(start);
                    l.SegmentLength = length;
                    l.StartDirection = direction;
                }));
                segments.Add(segment);
            }

            return i.New<IfcAlignmentCurve>(c =>
            {
                c.Horizontal = i.New<IfcAlignment2DHorizontal>(h =>
                {
                    // this should be set to what it actually is
                    h.StartDistAlong = 0;
                    h.Segments.AddRange(segments);
                    h.StartDistAlong = startOffset;
                });
            });
        }

        public static Guid GetIfcGUID(Element element)
        {
            string a = element.UniqueId;
            Guid episodeId = new Guid(a.Substring(0, 36));
            int elementId = int.Parse(a.Substring(37), NumberStyles.AllowHexSpecifier);
            int last_32_bits = int.Parse(a.Substring(28, 8), NumberStyles.AllowHexSpecifier);
            int xor = last_32_bits ^ elementId;
            a = a.Substring(0, 28) + xor.ToString("x8");
            return new Guid(a);
        }
        private IfcRepresentationContext ModelContext => i.FirstOrDefault<IfcRepresentationContext>(c => c.ContextIdentifier == "Body" && c.ContextType == "Model");

        private IfcDirection _zDirection;
        private IfcDirection ZDirection => _zDirection ?? (_zDirection = i.New<IfcDirection>(d => d.SetXYZ(0, 0, 1)));

        private IfcRectangleProfileDef _rectProfile;
        private IfcRectangleProfileDef RectProfile => _rectProfile ?? (_rectProfile = i.New<IfcRectangleProfileDef>(r => { r.ProfileType = IfcProfileTypeEnum.AREA; r.XDim = 0.02; r.YDim = 0.02; }));

        private IEnumerable<IfcFixedReferenceSweptAreaSolid> GetSolids(IEnumerable<PolyLine> polylines)
        {
            return polylines.Select(polyline =>
            {
                var points = polyline.GetCoordinates().Select(c => GetPoint3D(c));
                return i.New<IfcFixedReferenceSweptAreaSolid>(s =>
                {
                    s.SweptArea = RectProfile;
                    s.FixedReference = ZDirection;
                    s.Directrix = i.New<IfcPolyline>(p =>
                    {
                        p.Points.AddRange(points);
                    });
                });
            });
        }

        private IfcSurfaceStyle _redSurfaceStyle;
        private IfcSurfaceStyle RedSurfaceStyle => _redSurfaceStyle ?? (_redSurfaceStyle = i.New<IfcSurfaceStyle>(s =>
        {
            s.Side = IfcSurfaceSide.BOTH;
            s.Styles.Add(i.New<IfcSurfaceStyleShading>(sh =>
            {
                sh.SurfaceColour = i.New<IfcColourRgb>(c => c.Red = 1.0);
            }));
        }));

        private void SetRedStyle(IEnumerable<IfcRepresentationItem> items)
        {
            foreach (var item in items)
            {
                i.New<IfcStyledItem>(s =>
                {
                    s.Item = item;
                    s.Styles.Add(RedSurfaceStyle);
                });
            }
        }

        private static IEnumerable<PolyLine> GetPolylines(GeometryElement geom)
        {
            foreach (GeometryObject g in geom)
            {
                if (g is PolyLine polyline)
                {
                    yield return polyline;
                }
                if (g is GeometryInstance gi)
                {
                    foreach (var pl in GetPolylines(gi.SymbolGeometry))
                    {
                        yield return pl;
                    }
                }
            }
        }
    }

    internal class AlignmentRecord
    {
        public List<PolyLine> Polylines { get; set; } = new List<PolyLine>();
        public IfcAlignment Alignment { get; set; }
        public IEnumerable<IfcAlignment2DHorizontalSegment> Segments => (Alignment?.Axis as IfcAlignmentCurve)?.Horizontal.Segments;
        public Dictionary<Element, IfcBuildingElement> Elements { get; set; } = new Dictionary<Element, IfcBuildingElement>();
    }
}
