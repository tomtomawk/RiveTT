using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RiveTT.Tools.Utilities;

/// <summary>Conservative geometric warnings, never a claim of full view visibility.</summary>
public static class ViewCropDiagnostics
{
    public static List<string> Inspect(View view, Element annotation)
    {
        var warnings = new List<string>();
        try
        {
            if (!view.CropBoxActive) return warnings;
            var crop = view.CropBox;
            var bounds = annotation.get_BoundingBox(view);
            if (crop == null || bounds == null)
            {
                warnings.Add($"Element {annotation.Id.Value}: crop visibility could not be checked (no bounding box in view {view.Id.Value}).");
                return warnings;
            }
            var minX = crop.Min.X; var maxX = crop.Max.X;
            var minY = crop.Min.Y; var maxY = crop.Max.Y;
            var annotationCrop = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.AsInteger() == 1;
            using var shape = view.GetCropRegionShapeManager();
            if (annotationCrop && shape.CanHaveAnnotationCrop)
            {
                // API offsets are in view/paper feet, converted here to model feet.
                minX -= shape.LeftAnnotationCropOffset * view.Scale;
                maxX += shape.RightAnnotationCropOffset * view.Scale;
                minY -= shape.BottomAnnotationCropOffset * view.Scale;
                maxY += shape.TopAnnotationCropOffset * view.Scale;
            }
            var inverse = crop.Transform.Inverse;
            var outside = false;
            foreach (var x in new[] { bounds.Min.X, bounds.Max.X })
            foreach (var y in new[] { bounds.Min.Y, bounds.Max.Y })
            foreach (var z in new[] { bounds.Min.Z, bounds.Max.Z })
            {
                var p = inverse.OfPoint(bounds.Transform.OfPoint(new XYZ(x, y, z)));
                outside |= IsOutside(p.X, p.Y, minX, minY, maxX, maxY);
            }
            if (outside)
                warnings.Add(annotationCrop
                    ? $"Element {annotation.Id.Value} extends outside the annotation crop of view {view.Id.Value}; Revit may hide the entire annotation. Adjust the crop or placement."
                    : $"Element {annotation.Id.Value} extends outside the model crop rectangle of view {view.Id.Value}. Referenced elements or supporting lines may be cropped; annotation visibility is not guaranteed.");
            if (shape.ShapeSet || shape.NumberOfSplitRegions > 1)
                warnings.Add($"View {view.Id.Value} has a shaped or split crop; this rectangle check does not prove visibility inside each region.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Element {annotation.Id.Value}: crop visibility could not be checked: {ex.Message}");
        }
        return warnings;
    }

    public static bool IsOutside(double x, double y, double minX, double minY, double maxX, double maxY)
        => x < minX - 1e-9 || x > maxX + 1e-9 || y < minY - 1e-9 || y > maxY + 1e-9;
}
