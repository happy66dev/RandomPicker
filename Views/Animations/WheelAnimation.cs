using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using ClassIsland.RandomPicker.Services;

namespace ClassIsland.RandomPicker.Views.Animations;

/// <summary>
/// 「拼多多转盘」动画：指针先空转几圈减速停在两格之间的缝上，再往左或往右挪半格滑进中选那一格。
/// </summary>
/// <remarks>
/// 驱动属性是 <see cref="Angle"/>，单位是<b>度</b>——轮盘本身转了多少（顺时针为正）。
/// 角度和「指针指着第几格」的换算完全交给 <see cref="WheelLayout.SectorUnderPointer"/>，
/// 界面上不另写一套判定：算错了会指着一个不是中选者的格子停下来，而且只在部分扇区数下出错，
/// 极难复现，所以判定只能有一个出处。
/// </remarks>
internal sealed class WheelAnimation : RevealAnimationBase
{
    /// <summary>轮盘转了多少度（顺时针为正）。</summary>
    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<WheelAnimation, double>(nameof(Angle));

    static WheelAnimation()
    {
        // 角度一变就重画。
        AffectsRender<WheelAnimation>(AngleProperty);
    }

    /// <summary>盘面上的名字，顺时针排列。</summary>
    private IReadOnlyList<string> _sectors = [];

    /// <summary>中选者在盘面上的位置。</summary>
    private int _winnerSector;

    /// <summary>停到那条缝上时的角度。</summary>
    private double _boundaryAngle;

    /// <summary>最终（滑进中选格）的角度。</summary>
    private double _finalAngle;

    /// <summary>转到缝上用多久。</summary>
    private TimeSpan _holdDuration = TimeSpan.FromSeconds(6);

    /// <summary>从缝滑进格子里用多久。</summary>
    private TimeSpan _slideDuration = TimeSpan.FromSeconds(0.35);

    /// <summary>转盘半径。</summary>
    private double _radius;

    /// <summary>算好的舞台尺寸。</summary>
    private Size _stageSize;

    public WheelAnimation(double revealFontSize, Color accent)
        : base(revealFontSize, accent)
    {
    }

    /// <summary>轮盘当前转了多少度。</summary>
    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    /// <summary>一个正方形：轮盘外圈，再加上顶部指针要占的那点地方。</summary>
    public override Size StageSize => _stageSize;

    /// <inheritdoc/>
    public override void Load(RevealAnimationPlan plan)
    {
        // 喵~防御：装错了计划类型或者盘面不足两格时什么都不画，也好过抛异常把结果卡住。
        if (plan is not WheelPlan wheel || wheel.Sectors.Count < 2)
        {
            _sectors = [];
            _stageSize = default;
            return;
        }

        _sectors = wheel.Sectors;
        _winnerSector = wheel.WinnerSector;
        _boundaryAngle = wheel.BoundaryAngle;
        _finalAngle = wheel.FinalAngle;
        _holdDuration = wheel.HoldDuration;
        _slideDuration = wheel.SlideDuration;

        // 半径按字号档位换算，保证和插件其它部分同一套视觉尺度。
        _radius = RevealFontSize * 2.2;
        // 舞台是正方形：直径加上顶部指针和一圈描边的余量。
        var side = (_radius + RevealFontSize * 0.6) * 2;
        _stageSize = new Size(side, side);
    }

    /// <inheritdoc/>
    public override async Task PlayAsync(CancellationToken token)
    {
        // 喵~防御：盘面不足两格（装载失败）时直接结束，上层照样会出结果。
        if (_sectors.Count < 2)
        {
            return;
        }

        // 第一段：空转几圈，减速停在两格之间的缝上。用强减速缓动，停之前有「刹住」的感觉。
        var spin = new Animation
        {
            Duration = _holdDuration,
            FillMode = FillMode.Forward,
            Easing = new CubicEaseOut()
        };
        spin.Children.Add(new KeyFrame
        {
            KeyTime = TimeSpan.Zero,
            Setters = { new Setter(AngleProperty, 0.0) }
        });
        spin.Children.Add(new KeyFrame
        {
            KeyTime = _holdDuration,
            Setters = { new Setter(AngleProperty, _boundaryAngle) }
        });
        await spin.RunAsync(this, token);

        // 第二段：从缝上挪半格滑进中选那一格。带一点回弹，像被指针「吸」进去。
        var slide = new Animation
        {
            Duration = _slideDuration,
            FillMode = FillMode.Forward,
            Easing = new BackEaseOut()
        };
        slide.Children.Add(new KeyFrame
        {
            KeyTime = TimeSpan.Zero,
            Setters = { new Setter(AngleProperty, _boundaryAngle) }
        });
        slide.Children.Add(new KeyFrame
        {
            KeyTime = _slideDuration,
            Setters = { new Setter(AngleProperty, _finalAngle) }
        });
        await slide.RunAsync(this, token);
    }

    /// <inheritdoc/>
    public override void SnapToEnd() => Angle = _finalAngle;

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // 喵~防御：尺寸还没量出来（0×0）或者盘面不足两格时画不了，直接返回。
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || _sectors.Count < 2 || _radius <= 0)
        {
            return;
        }

        // 圆心。舞台是正方形，正中间就是圆心。
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        // 喵~防御：属性还没被动画写过时可能是 NaN，按「还没开始转」处理。
        var angle = double.IsFinite(Angle) ? Angle : 0;
        // 每一格占多少度。
        var sweep = 360.0 / _sectors.Count;
        // 停稳了没有：滑到终点就说明中选者已经在指针下面了。
        var settled = Math.Abs(angle - _finalAngle) < 0.5;
        // 文字横向能占多宽：扇形中线上那一段弦长，取九成，免得字压到相邻格子上。
        var labelRadius = _radius * 0.64;
        var maxTextWidth = 2 * labelRadius * Math.Sin(sweep / 2 * Math.PI / 180) * 0.9;
        // 一行文字占的高度。
        var lineHeight = RevealFontSize * 0.62;

        for (var i = 0; i < _sectors.Count; i++)
        {
            // 这一格在屏幕上占的角度区间；轮盘转了 angle 度，区间整体跟着挪。
            var start = WheelLayout.SectorStart(i, _sectors.Count) + angle;
            var end = start + sweep;
            // 这一格的扇形几何。
            var geometry = BuildSector(center, _radius, start, end);
            // 中选者那一格，而且在停稳之后，才用强调色点亮。
            var isWinner = settled && i == _winnerSector;

            // 底色深浅交替，相邻两格才分得开。
            var fill = isWinner
                ? new SolidColorBrush(Accent, 0.38)
                : new SolidColorBrush(i % 2 == 0
                    ? Color.FromRgb(0x24, 0x24, 0x2C)
                    : Color.FromRgb(0x1B, 0x1B, 0x22));
            // 描边：中选者亮一些。
            var border = isWinner
                ? new SolidColorBrush(Accent, 0.9)
                : new SolidColorBrush(Colors.White, 0.16);

            context.DrawGeometry(fill, new Pen(border, 1.2), geometry);

            // 这一格的中线方向，名字就摆在这条线上。
            var middle = start + sweep / 2;
            // 名字的中心点。
            var labelCenter = PointAt(center, labelRadius, middle);
            // 名字占用的矩形：横向按弦长限制，纵向是一行高。
            var labelArea = new Rect(labelCenter.X - maxTextWidth / 2,
                labelCenter.Y - lineHeight / 2, maxTextWidth, lineHeight);
            // 定格的中选者用强调色，其余是白的。
            IBrush textBrush = isWinner ? new SolidColorBrush(Accent) : Brushes.White;
            // 名字横向摆正画出来就行——扇形排成一圈，横排永远读得正。
            DrawFittedText(context, _sectors[i], labelArea, lineHeight * 0.82, textBrush);
        }

        // 圆心的小圆盖，把扇形收在一起的那一点遮掉。
        context.DrawEllipse(new SolidColorBrush(Accent, 0.85), null, center,
            RevealFontSize * 0.13, RevealFontSize * 0.13);
        // 指针画在最上层，永远盖住盘面。
        DrawPointer(context, center);
    }

    /// <summary>画正上方那个固定不动的指针。</summary>
    private void DrawPointer(DrawingContext context, Point center)
    {
        // 三角的底边半宽。
        var size = RevealFontSize * 0.24;
        // 三角顶点，压在轮盘外圈里面一点。
        var tip = new Point(center.X, center.Y - _radius + RevealFontSize * 0.12);
        // 三角底边所在的纵坐标。
        var baseY = center.Y - _radius - RevealFontSize * 0.42;

        var marker = new StreamGeometry();
        using (var geometry = marker.Open())
        {
            // 从底边左端出发，连到右端，再收到顶点——一个朝下的三角。
            geometry.BeginFigure(new Point(center.X - size, baseY), true);
            geometry.LineTo(new Point(center.X + size, baseY));
            geometry.LineTo(tip);
            geometry.EndFigure(true);
        }

        context.DrawGeometry(new SolidColorBrush(Accent), null, marker);
    }

    /// <summary>围出一块扇形。</summary>
    /// <param name="center">圆心。</param>
    /// <param name="radius">半径。</param>
    /// <param name="startDegrees">起始角，0 度是正上方。</param>
    /// <param name="endDegrees">结束角，必定大于起始角。</param>
    private static Geometry BuildSector(Point center, double radius, double startDegrees, double endDegrees)
    {
        // 弧的两个端点。
        var start = PointAt(center, radius, startDegrees);
        var end = PointAt(center, radius, endDegrees);

        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            // 从圆心出发，连到起始角那一点。
            path.BeginFigure(center, true);
            path.LineTo(start);
            // 沿外圈顺时针走到结束角。跨过半圈时必须置 IsLargeArc，
            // 否则弧会往短的那一边画，扇区就反了；正好半圈（两格）时置 false 才是对的。
            path.ArcTo(end, new Size(radius, radius), 0,
                endDegrees - startDegrees > 180, SweepDirection.Clockwise);
            // 闭合回圆心，围成一块扇形。
            path.EndFigure(true);
        }

        return geometry;
    }

    /// <summary>离圆心 <paramref name="radius"/>、方向是「正上方顺时针 <paramref name="degrees"/> 度」的那个点。</summary>
    private static Point PointAt(Point center, double radius, double degrees)
    {
        // 换成弧度。
        var radians = degrees * Math.PI / 180.0;
        // 屏幕坐标系 y 向下，所以「正上方」是减 y；角度增大往右偏，就是顺时针。
        return new Point(center.X + radius * Math.Sin(radians), center.Y - radius * Math.Cos(radians));
    }
}
