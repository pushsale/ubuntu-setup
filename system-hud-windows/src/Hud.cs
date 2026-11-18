// Vẽ HUD: theme Orbital (nền tối) và Orbital Light (nền sáng), bố cục 5 khối.
// Port từ hud.js (makeOrbital + phần BỐ CỤC); giữ nguyên toạ độ thiết kế 480px.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;

namespace SystemHud
{
    public sealed class Palette
    {
        public double[] Main, Accent, Warn, Crit, Dim, Text, Bg, Bg2;
    }

    public sealed class OrbitalOptions
    {
        public double BgAlpha0 = 0.8, BgAlpha1 = 0.82;
        public double[] Glow = { 0.4, 0.7, 1.0 };
        public double GlowAlpha = 0.22, StarAlpha = 1;
    }

    internal static class Heat
    {
        static readonly (double, double[])[] Dark =
        {
            (0, new[] { 0.25, 0.88, 0.45 }), (50, new[] { 0.25, 0.88, 0.45 }), (65, new[] { 0.75, 0.9, 0.25 }),
            (75, new[] { 1.0, 0.82, 0.2 }), (83, new[] { 1.0, 0.55, 0.15 }), (90, new[] { 1.0, 0.24, 0.28 }),
            (100, new[] { 1.0, 0.24, 0.28 }),
        };

        static readonly (double, double[])[] Light =
        {
            (0, new[] { 0.1, 0.66, 0.3 }), (50, new[] { 0.1, 0.66, 0.3 }), (65, new[] { 0.5, 0.68, 0.05 }),
            (75, new[] { 0.88, 0.66, 0.0 }), (83, new[] { 0.95, 0.45, 0.05 }), (90, new[] { 0.86, 0.14, 0.2 }),
            (100, new[] { 0.86, 0.14, 0.2 }),
        };

        // <=50 xanh lá, chuyển dần vàng -> cam -> đỏ ở >=90
        static double[] From((double, double[])[] stops, double? p, double[] none)
        {
            if (p == null || double.IsNaN(p.Value))
                return none;
            double v = Math.Max(0, Math.Min(100, p.Value));
            for (int i = 1; i < stops.Length; i++)
            {
                var (p1, c1) = stops[i];
                if (v <= p1)
                {
                    var (p0, c0) = stops[i - 1];
                    double f = p1 == p0 ? 0 : (v - p0) / (p1 - p0);
                    return new[] { c0[0] + (c1[0] - c0[0]) * f, c0[1] + (c1[1] - c0[1]) * f, c0[2] + (c1[2] - c0[2]) * f };
                }
            }
            return stops[stops.Length - 1].Item2;
        }

        public static double[] OfDark(double? p) => From(Dark, p, new[] { 0.6, 0.65, 0.7 });
        public static double[] OfLight(double? p) => From(Light, p, new[] { 0.5, 0.55, 0.6 });
    }

    internal static class Fmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Bytes(double b, int digits = 1)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double v = b;
            while (v >= 1024 && i < u.Length - 1)
            {
                v /= 1024;
                i++;
            }
            return i == 0 ? $"{Round(v)} {u[i]}" : $"{v.ToString(v >= 100 ? "F0" : "F" + digits, Inv)} {u[i]}";
        }

        public static string Rate(double b) => Bytes(b) + "/s";

        // "202 / 223 GB", "0.41 / 1.82 TB": cùng đơn vị theo dung lượng tổng
        public static string SizePair(double used, double total, string sep = " / ", int tbDigits = 2)
        {
            double tb = Math.Pow(2, 40), gb = Math.Pow(2, 30);
            if (total >= tb)
                return $"{F(used / tb, tbDigits)}{sep}{F(total / tb, tbDigits)} TB";
            int d = total >= 100 * gb ? 0 : 1;
            return $"{F(used / gb, d)}{sep}{F(total / gb, d)} GB";
        }
        public static string GB(double b) => (b / Math.Pow(2, 30)).ToString("F1", Inv);
        public static string Temp(double? t) => t == null ? "--" : $"{Round(t.Value)}°C";
        public static string F(double v, int d) => v.ToString("F" + d, Inv);
        public static long Round(double v) => (long)Math.Floor(v + 0.5);

        public static string Uptime(double sec)
        {
            long s = (long)sec;
            long d = s / 86400, h = s % 86400 / 3600, m = s % 3600 / 60;
            return d > 0 ? $"{d}d {h:00}:{m:00}" : $"{h:00}:{m:00}";
        }
    }

    // ================================================================ THEME ORBITAL
    public sealed class Orbital
    {
        public readonly Palette C;
        readonly Func<double?, double[]> _heat;
        readonly OrbitalOptions _opt;
        public double Shift; // độ trượt đồ thị, đặt trước mỗi khung hình

        public Orbital(Palette c, Func<double?, double[]> heat, OrbitalOptions opt)
        {
            C = c;
            _heat = heat;
            _opt = opt;
        }

        public double[] Level(double? p) => _heat(p);
        public double[] TempColor(double? t) => t == null ? C.Dim : _heat(t);

        static Font F(FontRole r, double px) => Fonts.Get(r, px);

        // Nhãn: chữ hoa, giãn ký tự, màu dim
        public TextBox L(Graphics g, string str, double x, double y, double size = 10, double[] color = null, double alpha = 0.95,
            double spacing = 1.8, Align align = Align.Left, VAlign valign = VAlign.Top)
            => Gfx.Text(g, str.ToUpperInvariant(), x, y, F(FontRole.Label, size), color ?? C.Dim, alpha, align, valign, spacing);

        public void Panel(Graphics g, double W, double H)
        {
            using (var path = Gfx.RoundRect(1, 1, W - 2, H - 2, 8))
            {
                var bounds = new RectangleF(0, 0, (float)W, (float)H);
                using (var br = Gfx.LinearPad(new PointF(0, 0), new PointF((float)(W * 0.6), (float)H),
                           Gfx.Col(C.Bg, _opt.BgAlpha0), Gfx.Col(C.Bg2, _opt.BgAlpha1), bounds))
                    g.FillPath(br, path);

                // vầng sáng tinh vân ở góc trên
                var state = g.Save();
                g.SetClip(path);
                double gx = W * 0.85, gy = H * 0.08, gr = W * 0.9;
                using (var ell = new GraphicsPath())
                {
                    ell.AddEllipse((float)(gx - gr), (float)(gy - gr), (float)(2 * gr), (float)(2 * gr));
                    using (var pg = new PathGradientBrush(ell)
                           {
                               CenterPoint = new PointF((float)gx, (float)gy),
                               CenterColor = Gfx.Col(_opt.Glow, _opt.GlowAlpha),
                               SurroundColors = new[] { Gfx.Col(_opt.Glow, 0) },
                           })
                        g.FillPath(pg, ell);
                }
                g.Restore(state);

                using (var pen = new Pen(Gfx.Col(C.Accent, 0.6), 1.2f))
                    g.DrawPath(pen, path);
            }

            // sao (ít và nhạt để không rối)
            var rnd = Lcg(7);
            for (int i = 0; i < 50; i++)
            {
                double x = rnd() * W, y = rnd() * H, r = rnd() * 0.7 + 0.3, a = (0.06 + rnd() * 0.14) * _opt.StarAlpha;
                Gfx.FillCircle(g, x, y, r, C.Main, a);
            }

            // dấu + ở 4 góc
            foreach (var (px, py) in new[] { (10.0, 10.0), (W - 10, 10.0), (10.0, H - 10), (W - 10, H - 10) })
            {
                Gfx.Line(g, px - 6, py, px + 6, py, 1.5, C.Main, 1);
                Gfx.Line(g, px, py - 6, px, py + 6, 1.5, C.Main, 1);
            }
        }

        public void Header(Graphics g, double x, double y, double w, double h, HudState s)
        {
            var now = DateTime.Now;
            L(g, "node", x, y + 2, color: C.Accent);
            Gfx.Text(g, s.Sys.Hostname.ToUpperInvariant(), x, y + 13, F(FontRole.Light, 26), C.Main, spacing: 4);
            Gfx.Text(g, now.ToString("HH:mm", CultureInfo.InvariantCulture), x + w, y - 3, F(FontRole.Thin, 40), C.Main, align: Align.Right);
            L(g, now.ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture), x + w, y + 41, align: Align.Right);
            Gfx.Line(g, x, y + h - 10.5, x + w, y + h - 10.5, 1, C.Main, 0.4);
            double dx = x + w / 2, dy = y + h - 10.5;
            using (var b = new SolidBrush(Gfx.Col(C.Accent)))
                g.FillPolygon(b, new[]
                {
                    new PointF((float)dx, (float)(dy - 5)), new PointF((float)(dx + 5), (float)dy),
                    new PointF((float)dx, (float)(dy + 5)), new PointF((float)(dx - 5), (float)dy),
                });
            L(g, $"{s.Sys.Os} · up {Fmt.Uptime(s.Sys.Uptime)}", x, y + h - 5, size: 9.5, spacing: 1.2, alpha: 0.85);
            L(g, $"kernel {s.Sys.Kernel}", x + w, y + h - 5, size: 9.5, spacing: 1.2, alpha: 0.85, align: Align.Right);
        }

        public void Section(Graphics g, double x, double y, double w, string idx, string title, string sub)
        {
            Gfx.Text(g, idx, x, y + 1, F(FontRole.Mono, 12), C.Accent);
            Gfx.Text(g, title.ToUpperInvariant(), x + 24, y - 1, F(FontRole.Title, 14), C.Main, spacing: 3);
            L(g, sub, x + w, y + 3, align: Align.Right, spacing: 1, size: 10);
            Gfx.Line(g, x, y + 22.5, x + w, y + 22.5, 1, C.Main, 0.35);
            Gfx.Line(g, x, y + 22.5, x + 26, y + 22.5, 2, C.Accent, 1);
        }

        public void Gauge(Graphics g, double cx, double cy, double r, double pct, double? temp, string label, bool small)
        {
            double a0 = -Math.PI / 2;
            Gfx.StrokeCircle(g, cx, cy, r, 1.5, C.Main, 0.28);
            // giá trị
            double a1 = a0 + Gfx.TAU * Math.Max(0.002, Math.Min(1, pct / 100));
            var col = Level(pct);
            Gfx.StrokeArc(g, cx, cy, r, a0, a1, 12, col, 0.12, round: true);
            Gfx.StrokeArc(g, cx, cy, r, a0, a1, 5, col, 1, round: true);
            Gfx.FillCircle(g, cx + r * Math.Cos(a1), cy + r * Math.Sin(a1), 4.5, C.Main, 1);
            // nhiệt độ: cung bên trong
            double ri = r - 10;
            Gfx.StrokeCircle(g, cx, cy, ri, 1.5, C.Main, 0.2);
            if (temp != null)
                Gfx.StrokeArc(g, cx, cy, ri, a0, a0 + Gfx.TAU * Math.Min(1, temp.Value / 100), 3, TempColor(temp), 1);
            double size = small ? Math.Round(r * 0.66) : 46; // chữ theo cỡ vòng (r 70 -> 46)
            var t = Gfx.Text(g, Fmt.Round(pct).ToString(), cx - 5, cy - 2, F(FontRole.Thin, size), C.Main, align: Align.Center, valign: VAlign.Middle);
            Gfx.Text(g, "%", t.X + t.W + 1, cy - size * 0.3, F(FontRole.Light, size * 0.32), C.Dim);
            L(g, label, cx, cy + size * 0.42, align: Align.Center, size: 9);
            Gfx.Text(g, Fmt.Temp(temp), cx, cy - size * 0.5, F(FontRole.Mono, 11), TempColor(temp), align: Align.Center, valign: VAlign.Bottom);
        }

        // Gauge ổ đĩa (bản Windows thêm), cùng kiểu gauge CPU:
        // vòng ngoài + số lớn = % thời gian ổ bận (disk IO, như cột Disk của Task Manager);
        // vòng trong mảnh + chữ nhỏ phía trên số lớn = % dung lượng đã dùng (vị trí nhiệt độ ở gauge CPU).
        public void DiskGauge(Graphics g, double cx, double cy, double r, double? active, double usedPct)
        {
            double a0 = -Math.PI / 2;
            double io = active ?? 0;
            Gfx.StrokeCircle(g, cx, cy, r, 1.5, C.Main, 0.28);
            double a1 = a0 + Gfx.TAU * Math.Max(0.002, Math.Min(1, io / 100));
            var col = Level(io);
            Gfx.StrokeArc(g, cx, cy, r, a0, a1, 12, col, 0.12, round: true);
            Gfx.StrokeArc(g, cx, cy, r, a0, a1, 5, col, 1, round: true);
            Gfx.FillCircle(g, cx + r * Math.Cos(a1), cy + r * Math.Sin(a1), 4.5, C.Main, 1);

            double ri = r - 10;
            Gfx.StrokeCircle(g, cx, cy, ri, 1.5, C.Main, 0.2);
            Gfx.StrokeArc(g, cx, cy, ri, a0, a0 + Gfx.TAU * Math.Max(0, Math.Min(1, usedPct / 100)), 3, Level(usedPct), 1);

            double fs = Math.Round(r * 0.66);
            var t = Gfx.Text(g, active == null ? "--" : Fmt.Round(io).ToString(), cx - 4, cy - 1, F(FontRole.Thin, fs), C.Main,
                align: Align.Center, valign: VAlign.Middle);
            Gfx.Text(g, "%", t.X + t.W + 1, cy - fs * 0.3, F(FontRole.Light, fs * 0.32), C.Dim);
            Gfx.Text(g, $"{Fmt.Round(usedPct)}%", cx, cy - fs * 0.5, F(FontRole.Mono, 11), Level(usedPct), align: Align.Center, valign: VAlign.Bottom);
            L(g, "disk io", cx, cy + fs * 0.42, align: Align.Center, size: 8);
        }

        // Gauge giá trị % đơn giản (bản Windows thêm, dùng cho RAM): vòng ngoài = pct theo thang nhiệt,
        // chữ nhỏ phía trên số lớn (chỗ nhiệt độ ở gauge CPU) = top, vòng trong chỉ trang trí.
        public void ValueGauge(Graphics g, double cx, double cy, double r, double pct, string label, string top, double[] topColor)
        {
            double a0 = -Math.PI / 2;
            Gfx.StrokeCircle(g, cx, cy, r, 1.5, C.Main, 0.28);
            double a1 = a0 + Gfx.TAU * Math.Max(0.002, Math.Min(1, pct / 100));
            var col = Level(pct);
            Gfx.StrokeArc(g, cx, cy, r, a0, a1, 12, col, 0.12, round: true);
            Gfx.StrokeArc(g, cx, cy, r, a0, a1, 5, col, 1, round: true);
            Gfx.FillCircle(g, cx + r * Math.Cos(a1), cy + r * Math.Sin(a1), 4.5, C.Main, 1);
            Gfx.StrokeCircle(g, cx, cy, r - 10, 1.5, C.Main, 0.2);
            double fs = Math.Round(r * 0.66);
            var t = Gfx.Text(g, Fmt.Round(pct).ToString(), cx - 4, cy - 1, F(FontRole.Thin, fs), C.Main, align: Align.Center, valign: VAlign.Middle);
            Gfx.Text(g, "%", t.X + t.W + 1, cy - fs * 0.3, F(FontRole.Light, fs * 0.32), C.Dim);
            Gfx.Text(g, top, cx, cy - fs * 0.5, F(FontRole.Mono, 11), topColor, align: Align.Center, valign: VAlign.Bottom);
            L(g, label, cx, cy + fs * 0.42, align: Align.Center, size: 9);
        }

        // Gauge tốc độ mạng (bản Windows thêm): vòng = % băng thông đang dùng, tuyến tính theo max (tốc độ link).
        // Khi chưa tới 5% link, 5% đầu vòng tính theo tốc độ so với đỉnh gần đây (peak của lịch sử): bằng đỉnh -> 5%,
        // nửa đỉnh -> 2.5%, để lúc mạng nhẹ vẫn thấy vòng nhúc nhích. Từ 5% link trở lên là tỉ lệ thật; hai đoạn
        // nối liền ở mốc 5% (vì rate <= peak). Từng thử log cả vòng: vài KB/s đã chiếm 1/4 vòng, dễ hiểu nhầm nên bỏ.
        // Số lớn = tốc độ, dưới là đơn vị, trên là nhãn.
        public const double NetLowBand = 0.05;

        public static double RateFraction(double rate, double max, double peak)
        {
            if (max <= 0 || rate <= 0)
                return 0;
            double lin = Math.Min(1, rate / max);
            if (lin >= NetLowBand)
                return lin;
            double rel = peak > 0 ? Math.Min(1, rate / peak) : 0;
            return Math.Max(lin, NetLowBand * rel);
        }

        public void RateGauge(Graphics g, double cx, double cy, double r, double rate, double max, double peak, double[] col, string label)
        {
            double a0 = -Math.PI / 2;
            double p = RateFraction(rate, max, peak);
            Gfx.StrokeCircle(g, cx, cy, r, 1.5, C.Main, 0.28);
            double a1 = a0 + Gfx.TAU * Math.Max(0.002, p);
            Gfx.StrokeArc(g, cx, cy, r, a0, a1, 12, col, 0.12, round: true);
            Gfx.StrokeArc(g, cx, cy, r, a0, a1, 5, col, 1, round: true);
            Gfx.FillCircle(g, cx + r * Math.Cos(a1), cy + r * Math.Sin(a1), 4.5, C.Main, 1);
            Gfx.StrokeCircle(g, cx, cy, r - 10, 1.5, C.Main, 0.2);

            string s = Fmt.Rate(rate);
            int sp = s.IndexOf(' ');
            string num = sp > 0 ? s.Substring(0, sp) : s, unit = sp > 0 ? s.Substring(sp + 1) : "";
            double fs = Math.Round(r * 0.66);
            Gfx.Text(g, num, cx, cy - 1, F(FontRole.Thin, num.Length > 3 ? fs * 0.86 : fs), C.Main, align: Align.Center, valign: VAlign.Middle);
            L(g, label, cx, cy - fs * 0.5, size: 9, color: col, align: Align.Center, valign: VAlign.Bottom);
            L(g, unit, cx, cy + fs * 0.42, align: Align.Center, size: 9);
        }

        public void Bar(Graphics g, double x, double y, double w, double h, double pct, double[] color = null)
        {
            double yy = y + h / 2;
            for (int i = 0; i <= 10; i++)
            {
                double tx = x + w * i / 10 + 0.5;
                Gfx.Line(g, tx, yy + 5, tx, yy + (i % 5 == 0 ? 10 : 7), 1, C.Main, 0.5);
            }
            Gfx.Line(g, x, yy, x + w, yy, 3, C.Main, 0.25);
            double fx = x + w * Math.Min(1, Math.Max(0, pct) / 100);
            if (fx > x)
                Gfx.Line(g, x, yy, fx, yy, h > 6 ? 5 : 3, color ?? Level(pct), 1);
            Gfx.FillRect(g, fx - 1.25, yy - 7, 2.5, 14, C.Accent, 1);
        }

        public void Graph(Graphics g, double x, double y, double w, double h, (double[] data, double[] color)[] series, double max, bool mirror = false)
        {
            Gfx.Line(g, x, y + h + 0.5, x + w, y + h + 0.5, 1, C.Main, 0.3);
            if (mirror)
                Gfx.Line(g, x, y + h / 2 + 0.5, x + w, y + h / 2 + 0.5, 1, C.Main, 0.3);
            double mid = mirror ? y + h / 2 : y + h;
            double half = mirror ? h / 2 - 3 : h - 3;
            var state = g.Save();
            var clip = new RectangleF((float)x, (float)(y - 6), (float)w, (float)(h + 12));
            g.SetClip(clip);
            for (int si = 0; si < series.Length; si++)
            {
                var data = Pad(series[si].data);
                var color = series[si].color;
                double step = w / (Sensors.HISTORY - 1);
                int sign = mirror && si == 1 ? 1 : -1;
                var pts = data.Select((v, i) => new PointF((float)(x + (i + Shift) * step),
                    (float)(mid + sign * Math.Min(1, max > 0 ? v / max : 0) * half))).ToArray();

                using (var curve = new GraphicsPath())
                {
                    for (int i = 1; i < pts.Length; i++)
                    {
                        var p = pts[i - 1];
                        var q = pts[i];
                        float mx = (p.X + q.X) / 2;
                        curve.AddBezier(p, new PointF(mx, p.Y), new PointF(mx, q.Y), q);
                    }
                    using (var area = (GraphicsPath)curve.Clone())
                    {
                        area.AddLine(pts[pts.Length - 1], new PointF((float)(x + w + 2 * step), (float)mid));
                        area.AddLine(new PointF((float)(x + w + 2 * step), (float)mid), new PointF((float)x, (float)mid));
                        area.CloseFigure();
                        using (var br = Gfx.LinearPad(new PointF(0, (float)(mid + sign * half)), new PointF(0, (float)mid),
                                   Gfx.Col(color, 0.42), Gfx.Col(color, 0.04), clip))
                            g.FillPath(br, area);
                    }
                    using (var pen = new Pen(Gfx.Col(color), 2f) { LineJoin = LineJoin.Round })
                        g.DrawPath(pen, curve);
                }
                var last = pts[pts.Length - 1];
                Gfx.FillCircle(g, last.X, last.Y, 3, color, 1);
            }
            g.Restore(state);
        }

        public void Cores(Graphics g, double x, double y, double w, double h, double[] vals)
        {
            int n = Math.Max(1, vals.Length);
            double cw = w / n;
            double lw = Math.Min(4, cw * 0.6); // máy nhiều luồng: thu nhỏ vạch cho khỏi dính nhau
            for (int i = 0; i < vals.Length; i++)
            {
                double cx = x + i * cw + cw / 2;
                Gfx.Line(g, cx, y, cx, y + h, lw, C.Main, 0.16);
                double vh = Math.Max(1.5, vals[i] / 100 * h);
                Gfx.Line(g, cx, y + h, cx, y + h - vh, lw, Level(vals[i]), 1);
            }
        }

        public void Stat(Graphics g, double x, double y, double w, string label, string value, double[] color = null)
        {
            L(g, label, x, y + 2, size: 10);
            Gfx.Text(g, value, x + w, y - 1, F(FontRole.Mono, 14.5), color ?? C.Main, align: Align.Right);
        }

        public void Big(Graphics g, double x, double y, string value, string unit, double[] color, Align align = Align.Right)
        {
            var u = Gfx.Measure(g, unit, F(FontRole.Light, 14));
            var v = Gfx.Measure(g, value, F(FontRole.Thin, 40));
            double total = v.W + 2 + u.W;
            double vx = align == Align.Right ? x - total : align == Align.Center ? x - total / 2 : x;
            Gfx.Text(g, value, vx, y - 4, F(FontRole.Thin, 40), color);
            Gfx.Text(g, unit, vx + v.W + 2, y + 18, F(FontRole.Light, 14), C.Dim);
        }

        public TextBox Label(Graphics g, double x, double y, string str, double size = 10, double[] color = null,
            Align align = Align.Left, double alpha = 0.95)
            => L(g, str, x, y, size: size, color: color, align: align, alpha: alpha);

        public TextBox Value(Graphics g, double x, double y, string str, double size = 22, double[] color = null, Align align = Align.Left)
            => Gfx.Text(g, str, x, y, F(FontRole.Light, size), color ?? C.Main, align: align);

        public void Footer(Graphics g, double x, double y, double w, double h, HudState s)
        {
            L(g, "orbital systems interface", x, y + 8, size: 9, alpha: 0.75);
            L(g, "sync", x + w - 14, y + 8, size: 9, align: Align.Right, alpha: 0.85);
            Gfx.FillCircle(g, x + w - 4, y + 14, 3.5, C.Accent, 0.35 + 0.65 * (Math.Floor(s.Tick) % 2));
        }

        static double[] Pad(double[] data)
        {
            if (data.Length >= Sensors.HISTORY)
                return data;
            var r = new double[Sensors.HISTORY];
            Array.Copy(data, 0, r, Sensors.HISTORY - data.Length, data.Length);
            return r;
        }

        // PRNG cố định để hoa văn trang trí không nhảy mỗi lần vẽ (giống lcg trong hud.js)
        static Func<double> Lcg(uint seed)
        {
            uint s = seed;
            return () =>
            {
                s = unchecked(s * 1664525u + 1013904223u);
                return s / 4294967296.0;
            };
        }
    }

    // ================================================================ BỐ CỤC
    public static class Hud
    {
        public static readonly Dictionary<string, Orbital> Themes = new Dictionary<string, Orbital>
        {
            ["orbital"] = new Orbital(new Palette
            {
                Main = new[] { 1.0, 1.0, 1.0 }, Accent = new[] { 0.35, 0.86, 1.0 }, Warn = new[] { 1.0, 0.8, 0.3 },
                Crit = new[] { 1.0, 0.3, 0.36 }, Dim = new[] { 0.78, 0.87, 1.0 }, Text = new[] { 1.0, 1.0, 1.0 },
                Bg = new[] { 0.09, 0.17, 0.36 }, Bg2 = new[] { 0.2, 0.12, 0.4 },
            }, Heat.OfDark, new OrbitalOptions()),
            // Bản nền trắng: chữ navy đậm, thang màu đậm hơn để nổi trên nền sáng
            ["orbitalLight"] = new Orbital(new Palette
            {
                Main = new[] { 0.09, 0.13, 0.24 }, Accent = new[] { 0.0, 0.47, 0.86 }, Warn = new[] { 0.85, 0.6, 0.0 },
                Crit = new[] { 0.86, 0.14, 0.2 }, Dim = new[] { 0.33, 0.4, 0.53 }, Text = new[] { 0.09, 0.13, 0.24 },
                Bg = new[] { 1.0, 1.0, 1.0 }, Bg2 = new[] { 0.9, 0.94, 1.0 },
            }, Heat.OfLight, new OrbitalOptions { BgAlpha0 = 0.88, BgAlpha1 = 0.9, Glow = new[] { 0.55, 0.75, 1.0 }, GlowAlpha = 0.35, StarAlpha = 0.5 }),
        };

        const double FooterH = 22, Pad = 20, DesignW = 480;

        static readonly (double h, Action<Graphics, Orbital, HudState, double, double, double, double> fn)[] Sections =
        {
            (74, (g, T, s, x, y, w, h) => T.Header(g, x, y, w, h, s)),
            (172, (g, T, s, x, y, w, h) => DrawCpu(g, T, s, x, y, w)),
            (172, (g, T, s, x, y, w, h) => DrawMem(g, T, s, x, y, w)),
            (172, (g, T, s, x, y, w, h) => DrawGpu(g, T, s, x, y, w)),
            (186, (g, T, s, x, y, w, h) => DrawDisk(g, T, s, x, y, w)),
            (172, (g, T, s, x, y, w, h) => DrawNet(g, T, s, x, y, w)),
        };

        public static readonly double NeededH = Sections.Sum(x => x.h) + FooterH + Pad * 2 + Sections.Length * 8;

        public static double[] Profile; // --bench: tổng ms theo từng khối
        static string _bgKey;
        static Bitmap _bg;

        // Nền panel không đổi giữa các khung hình -> vẽ một lần vào bộ đệm rồi dán lại
        public static void Draw(Graphics g, int w, int h, HudState s, string themeName)
        {
            if (!Themes.TryGetValue(themeName ?? "", out var T))
                T = Themes["orbital"];
            T.Shift = s.Shift;
            Gfx.Setup(g);
            g.Clear(Color.Transparent);

            double k = Math.Min(w / DesignW, h / NeededH);
            double W = w / k, H = h / k;

            string key = $"{themeName}:{w}x{h}";
            if (_bgKey != key)
            {
                _bg?.Dispose();
                _bg = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (var bg = Graphics.FromImage(_bg))
                {
                    Gfx.Setup(bg);
                    bg.Clear(Color.Transparent);
                    bg.ScaleTransform((float)k, (float)k);
                    T.Panel(bg, W, H);
                }
                _bgKey = key;
            }
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(_bg, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;

            var root = g.Save();
            g.ScaleTransform((float)k, (float)k);
            Gfx.Scale = k; // bật cache ảnh chữ cho khung hình này
            double x = Pad, cw = W - Pad * 2;
            double used = Sections.Sum(a => a.h) + FooterH;
            double gap = Math.Max(8, (H - Pad * 2 - used) / Sections.Length);
            double y = Pad;
            for (int i = 0; i < Sections.Length; i++)
            {
                var sec = Sections[i];
                long t0 = Stopwatch.GetTimestamp();
                var st = g.Save();
                sec.fn(g, T, s, x, y, cw, sec.h);
                g.Restore(st);
                y += sec.h + gap;
                if (Profile != null)
                    Profile[i] += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            }
            T.Footer(g, x, H - Pad - FooterH, cw, FooterH, s);
            Gfx.Scale = 0;
            g.Restore(root);
        }

        static void DrawCpu(Graphics g, Orbital T, HudState s, double x, double y, double w)
        {
            var c = s.Cpu;
            T.Section(g, x, y, w, "01", "CPU", $"{c.Model} · {c.Threads} threads");
            double top = y + 32;
            // Bản Windows: vòng tròn cỡ GPU / ổ đĩa (r 58) thay vì r 70; bỏ dòng TEMP (nhiệt độ đã trong vòng tròn).
            // Cột phải gọn trong chiều cao vòng tròn: CLOCK, PROC / THR, THREADS rồi đồ thị tải ngay bên dưới
            T.Gauge(g, x + 68, top + 68, 58, c.Usage, c.Temp, "cpu load", true);
            double rx = x + 150, rw = w - 150, ry = top + 2;
            T.Stat(g, rx, ry, rw, "CLOCK", c.Freq != null ? $"{Fmt.F(c.Freq.Value, 2)} GHz" : "--");
            ry += 22;
            // Windows không có load average: thay bằng số tiến trình / luồng
            T.Stat(g, rx, ry, rw, "PROC / THR", $"{Fmt.Round(c.Procs)} / {Fmt.Round(c.ThreadCount)}");
            ry += 26;
            T.Label(g, rx, ry, $"THREADS ×{c.PerCore.Length}");
            ry += 15;
            T.Cores(g, rx, ry, rw, 38, c.PerCore);
            ry += 44;
            T.Graph(g, rx, ry, rw, top + 134 - ry, new[] { (c.History, T.C.Main) }, 100);
        }

        static void DrawMem(Graphics g, Orbital T, HudState s, double x, double y, double w)
        {
            // Bản Windows: vòng tròn kiểu GPU (số lớn = % RAM, chữ nhỏ trên = GB đã dùng), bên phải USED / AVAILABLE /
            // CACHED / PAGEFILE + thanh pagefile. Bản gốc: hàng USED / % / TOTAL + thanh + CACHE / SWAP.
            var m = s.Mem;
            T.Section(g, x, y, w, "02", "MEMORY", $"{Fmt.GB(m.Total)} GB total");
            double top = y + 32;
            T.ValueGauge(g, x + 68, top + 68, 58, m.Percent, "ram used", $"{Fmt.GB(m.Used)} GB", T.Level(m.Percent));
            double rx = x + 150, rw = w - 150, ry = top + 2;
            T.Stat(g, rx, ry, rw, "USED", $"{Fmt.GB(m.Used)} / {Fmt.GB(m.Total)} GB");
            ry += 28;
            T.Stat(g, rx, ry, rw, "AVAILABLE", $"{Fmt.GB(Math.Max(0, m.Total - m.Used))} GB");
            ry += 28;
            T.Stat(g, rx, ry, rw, "CACHED", $"{Fmt.GB(m.Cached)} GB");
            ry += 32;
            T.Stat(g, rx, ry, rw, "PAGEFILE", $"{Fmt.GB(m.SwapUsed)} / {Fmt.GB(m.SwapTotal)} GB",
                m.SwapPercent > 50 ? T.C.Warn : null);
            ry += 20;
            T.Bar(g, rx, ry, rw, 10, m.SwapPercent);
        }

        static void DrawGpu(Graphics g, Orbital T, HudState s, double x, double y, double w)
        {
            var gp = s.Gpu;
            T.Section(g, x, y, w, "03", "GPU", gp.Available ? gp.Name : "not detected");
            double top = y + 32;
            if (!gp.Available)
            {
                T.Label(g, x, top + 10, "NO TELEMETRY");
                return;
            }
            T.Gauge(g, x + 68, top + 68, 58, gp.Util, gp.Temp, "core", true);
            // Bản Windows bỏ dòng TEMP: nhiệt độ đã hiện trong vòng tròn; 3 dòng còn lại giãn ra cho đủ chiều cao cũ
            double rx = x + 150, rw = w - 150, ry = top + 2;
            T.Stat(g, rx, ry, rw, "POWER", gp.Power == null ? "--"
                : $"{Fmt.F(gp.Power.Value, 0)} / {(gp.PowerLimit == null ? "--" : Fmt.F(gp.PowerLimit.Value, 0))} W");
            ry += 28;
            T.Stat(g, rx, ry, rw, "FAN", gp.Fan == null ? "--" : $"{Fmt.Round(gp.Fan.Value)}%");
            ry += 28;
            T.Stat(g, rx, ry, rw, "CLOCK", gp.Clock == null ? "--" : $"{Fmt.Round(gp.Clock.Value)} MHz");
            ry += 32;
            T.Stat(g, rx, ry, rw, "VRAM", gp.MemTotal > 0
                ? $"{Fmt.F(gp.MemUsed / Math.Pow(2, 30), 1)} / {Fmt.F(gp.MemTotal / Math.Pow(2, 30), 1)} GB"
                : $"{Fmt.F(gp.MemUsed / Math.Pow(2, 30), 1)} GB");
            ry += 20;
            T.Bar(g, rx, ry, rw, 10, gp.MemPercent);
        }

        static void DrawDisk(Graphics g, Orbital T, HudState s, double x, double y, double w)
        {
            // Bản Windows: mỗi ổ vật lý một gauge (tối đa 2, ổ hệ thống trước), khác bản gốc chỉ có ổ "/"
            var all = s.Disks ?? new DiskState[0];
            string sub = all.Length == 0 ? "no disks"
                : $"{all.Length} disk{(all.Length > 1 ? "s" : "")} · {Fmt.SizePair(all.Sum(d => d.Used), all.Sum(d => d.Total))}";
            T.Section(g, x, y, w, "04", "STORAGE", sub);
            double top = y + 32;
            if (all.Length == 0)
            {
                T.Label(g, x, top + 10, "NO TELEMETRY");
                return;
            }
            int n = Math.Min(2, all.Length);
            double gap = 16, cw = (w - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
                DiskCell(g, T, all[i], x + i * (cw + gap), top, cw);
        }

        // Một ổ: gauge bên trái (cỡ gauge GPU, số lớn = % disk IO), bên phải 3 mục READ / WRITE / USED
        // (nhãn trên, số dưới vì ô chỉ rộng khoảng 200), dưới cùng là ký tự ổ · model
        static void DiskCell(Graphics g, Orbital T, DiskState d, double x, double top, double w)
        {
            const double r = 58;
            double cx = x + r + 4, cy = top + 66;
            T.DiskGauge(g, cx, cy, r, d.Active, d.Percent);

            double rx = cx + r + 12;
            var vf = Fonts.Get(FontRole.Mono, 12.5);
            void Item(double ry, string label, string value, double[] color)
            {
                T.Label(g, rx, ry, label, size: 8.5);
                Gfx.Text(g, value, rx, ry + 11, vf, color);
            }
            Item(cy - 54, "READ", Fmt.Rate(d.ReadRate), T.C.Main);
            Item(cy - 16, "WRITE", Fmt.Rate(d.WriteRate), T.C.Accent);
            Item(cy + 22, "USED", Fmt.SizePair(d.Used, d.Total, "/", 1), T.Level(d.Percent));

            // Ký tự ổ · model (· nhiệt độ nếu có), cắt bớt model cho vừa ô
            string head = d.Mount + " · ", tail = d.Temp != null ? $" · {Fmt.Temp(d.Temp)}" : "";
            string model = d.Device;
            var hf = Fonts.Get(FontRole.Label, 8.5);
            while (model.Length > 4 && Gfx.Measure(g, (head + model + tail).ToUpperInvariant(), hf, 1.8).W > w)
                model = model.Substring(0, model.Length - 2).TrimEnd('-', ' ') + "…";
            T.Label(g, x, top + 140, head + model + tail, size: 8.5, alpha: 0.7);
        }

        static void DrawNet(Graphics g, Orbital T, HudState s, double x, double y, double w)
        {
            // Bản Windows: hai vòng tròn DOWNLINK / UPLINK (cỡ gauge GPU), mỗi vòng kèm PEAK / AVG / TOTAL bên phải,
            // đồ thị đọc/ghi gương bên dưới. Bản gốc: hai số lớn + đồ thị cao + dòng RX / SCALE / TX.
            var n = s.Net;
            string link = n.LinkSpeed <= 0 ? "" : n.LinkSpeed * 8 >= 1e9 ? $" · {Fmt.F(n.LinkSpeed * 8 / 1e9, 1)} Gbps" : $" · {Fmt.Round(n.LinkSpeed * 8 / 1e6)} Mbps";
            T.Section(g, x, y, w, "05", "NETWORK", (n.Iface.Length > 0 ? n.Iface : "offline") + (n.Ip.Length > 0 ? $" · {n.Ip}" : "") + link);
            double top = y + 32;
            double max = n.LinkSpeed > 0 ? n.LinkSpeed : 125e6; // không rõ tốc độ link: coi như 1 Gbps
            double gap = 16, cw = (w - gap) / 2;
            NetCell(g, T, x, top, cw, "▼ down", n.RxRate, n.RxHist, n.RxTotal, max, T.C.Main);
            NetCell(g, T, x + cw + gap, top, cw, "up ▲", n.TxRate, n.TxHist, n.TxTotal, max, T.C.Accent);
            // đồ thị lịch sử đã bỏ để nhường chiều cao cho vòng tròn MEMORY; PEAK / AVG thay phần thông tin đó
        }

        static void NetCell(Graphics g, Orbital T, double x, double top, double w, string label, double rate, double[] hist,
            double total, double max, double[] col)
        {
            const double r = 58;
            double cx = x + r + 4, cy = top + 66;
            double peak = Math.Max(rate, hist.Length > 0 ? hist.Max() : 0);
            T.RateGauge(g, cx, cy, r, rate, max, peak, col, label);
            double rx = cx + r + 12;
            var vf = Fonts.Get(FontRole.Mono, 12.5);
            void Item(double ry, string name, string value, double[] c)
            {
                T.Label(g, rx, ry, name, size: 8.5);
                Gfx.Text(g, value, rx, ry + 11, vf, c);
            }
            // đỉnh / trung bình trên cửa sổ lịch sử (HISTORY mẫu), tổng từ lúc card mạng bật
            Item(cy - 54, "PEAK", Fmt.Rate(peak), col);
            Item(cy - 16, "AVG", Fmt.Rate(hist.Length > 0 ? hist.Average() : 0), T.C.Main);
            Item(cy + 22, "TOTAL", Fmt.Bytes(total), T.C.Main);
        }
    }
}
