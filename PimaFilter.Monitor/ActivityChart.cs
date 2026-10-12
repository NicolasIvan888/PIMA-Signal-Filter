namespace PimaFilter.Monitor;

public sealed class ActivityChart : Control
{
    private IReadOnlyList<StatusSample> _samples = [];

    public ActivityChart()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 31, 45);
        ForeColor = Color.Gainsboro;
        MinimumSize = new Size(250, 24);
    }

    public void SetSamples(IReadOnlyList<StatusSample> samples)
    {
        _samples = samples;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var area = new Rectangle(8, 18, Math.Max(1, Width - 16), Math.Max(1, Height - 24));

        using var gridPen = new Pen(Color.FromArgb(52, 63, 82), 1);
        for (var index = 0; index <= 2; index++)
        {
            var y = area.Top + area.Height * index / 2;
            graphics.DrawLine(gridPen, area.Left, y, area.Right, y);
        }

        using var labelBrush = new SolidBrush(Color.FromArgb(165, 175, 192));
        graphics.DrawString("Permise (verde) · erori (roșu)", Font, labelBrush, 8, 1);
        if (_samples.Count < 2)
        {
            graphics.DrawString("Se acumulează date…", Font, labelBrush, area.Left + 12, area.Top + 4);
            return;
        }

        var deltas = new List<(long Allowed, long Errors)>();
        for (var index = 1; index < _samples.Count; index++)
        {
            deltas.Add((
                Math.Max(0, _samples[index].Allowed - _samples[index - 1].Allowed),
                Math.Max(0, _samples[index].Errors - _samples[index - 1].Errors)));
        }

        var maximum = Math.Max(1, deltas.Max(item => item.Allowed));
        DrawSeries(graphics, area, deltas.Select(item => (double)item.Allowed).ToArray(), maximum, Color.FromArgb(52, 211, 153), 2.4f);
        DrawSeries(graphics, area, deltas.Select(item => (double)item.Errors).ToArray(), maximum, Color.FromArgb(248, 113, 113), 2.0f);

    }

    private static void DrawSeries(
        Graphics graphics,
        Rectangle area,
        IReadOnlyList<double> values,
        double maximum,
        Color color,
        float width)
    {
        if (values.Count < 2)
        {
            return;
        }

        var points = new PointF[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var x = area.Left + area.Width * index / (float)(values.Count - 1);
            var y = area.Bottom - area.Height * (float)(values[index] / maximum);
            points[index] = new PointF(x, y);
        }

        using var pen = new Pen(color, width);
        graphics.DrawLines(pen, points);
    }
}
