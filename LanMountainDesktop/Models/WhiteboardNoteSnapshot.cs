using System;
using System.Collections.Generic;

namespace LanMountainDesktop.Models;

public sealed class WhiteboardNoteSnapshot
{
    public int Version { get; set; } = 1;

    public DateTimeOffset SavedUtc { get; set; }

    public List<WhiteboardStrokeSnapshot> Strokes { get; set; } = [];

    public WhiteboardNoteSnapshot Clone()
    {
        var clone = (WhiteboardNoteSnapshot)MemberwiseClone();
        clone.Strokes = Strokes is { Count: > 0 }
            ? new List<WhiteboardStrokeSnapshot>(Strokes.ConvertAll(stroke => stroke?.Clone() ?? new WhiteboardStrokeSnapshot()))
            : [];
        return clone;
    }
}

public sealed class WhiteboardStrokeSnapshot
{
    public string Color { get; set; } = "#FF000000";

    public double InkThickness { get; set; } = 2.5d;

    public bool IgnorePressure { get; set; } = true;

    public List<WhiteboardStylusPointSnapshot> Points { get; set; } = [];

    public WhiteboardStrokeSnapshot Clone()
    {
        var clone = (WhiteboardStrokeSnapshot)MemberwiseClone();
        clone.Points = Points is { Count: > 0 }
            ? new List<WhiteboardStylusPointSnapshot>(Points.ConvertAll(point => point?.Clone() ?? new WhiteboardStylusPointSnapshot()))
            : [];
        return clone;
    }
}

public sealed class WhiteboardStylusPointSnapshot
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Pressure { get; set; } = 0.5d;

    public double Width { get; set; }

    public double Height { get; set; }

    public WhiteboardStylusPointSnapshot Clone()
    {
        return (WhiteboardStylusPointSnapshot)MemberwiseClone();
    }
}
