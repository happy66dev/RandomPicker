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
/// 「CSGO 开箱」动画：一排名字方块横向滚过，指针停在中间那一块上。
/// </summary>
/// <remarks>
/// 驱动属性是 <see cref="Offset"/>，单位是<b>像素</b>——整条轨道往左平移多少。
/// 方块该画在哪儿、指针底下是第几块，全部由它现算，和 <see cref="CsgoTrack"/> 里的几何是同一套公式：
/// 排片表给出的 <c>TargetOffset</c> 保证滚到终点时指针下的方块<b>必然</b>是中选者。
/// </remarks>
internal sealed class CsgoAnimation : RevealAnimationBase
{
    /// <summary>轨道往左平移了多少像素。</summary>
    public static readonly StyledProperty<double> OffsetProperty =
        AvaloniaProperty.Register<CsgoAnimation, double>(nameof(Offset));

    static CsgoAnimation()
    {
        // 平移量一变就重画。
        AffectsRender<CsgoAnimation>(OffsetProperty);
    }

    /// <summary>轨道上的名字序列（名单重复展开若干轮）。</summary>
    private IReadOnlyList<string> _track = [];

    /// <summary>一个方块的宽度，单位：逻辑像素。</summary>
    private double _slotWidth;

    /// <summary>一个方块的高度。</summary>
    private double _slotHeight;

    /// <summary>方块之间的间距。</summary>
    private double _gap;

    /// <summary>滚到终点时的平移量。用来判断「停稳了没有」。</summary>
    private double _targetOffset;

    /// <summary>这段动画的总时长，装载时从排片表抄下来。</summary>
    private TimeSpan _planTotal = TimeSpan.FromSeconds(4);

    /// <summary>算好的舞台尺寸，等于排片表给的可视区大小。</summary>
    private Size _stageSize;

    public CsgoAnimation(double revealFontSize, Color accent)
        : base(revealFontSize, accent)
    {
    }

    /// <summary>轨道当前往左平移了多少像素。</summary>
    public double Offset
    {
        get => GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    /// <summary>可视区大小。</summary>
    public override Size StageSize => _stageSize;

    /// <inheritdoc/>
    public override void Load(RevealAnimationPlan plan)
    {
        // 喵~防御：装错了计划类型时什么都不画，也好过抛异常把结果卡住。
        if (plan is not CsgoPlan csgo)
        {
            _track = [];
            _stageSize = default;
            return;
        }

        _track = csgo.Track;
        _slotWidth = csgo.SlotWidth;
        _slotHeight = csgo.SlotHeight;
        _gap = csgo.Gap;
        _targetOffset = csgo.TargetOffset;
        _planTotal = csgo.Total;
        // 可视区多大，舞台就多大——平移量本来就是按这个宽度算出来的，两者必须一致。
        _stageSize = new Size(csgo.ViewportWidth, csgo.ViewportHeight);
    }

    /// <inheritdoc/>
    public override async Task PlayAsync(CancellationToken token)
    {
        // 喵~防御：轨道是空的（装载失败）时直接结束，上层照样会出结果。
        if (_track.Count == 0)
        {
            return;
        }

        // 从静止开始，一路加速冲过去再减速停住。
        var animation = new Animation
        {
            Duration = _planTotal,
            FillMode = FillMode.Forward,
            // 强减速缓动：起手快、收尾一点点挪，才有「刹住」的手感。
            Easing = new CubicEaseOut()
        };

        // 起点：还没开始滚。
        animation.Children.Add(new KeyFrame
        {
            KeyTime = TimeSpan.Zero,
            Setters = { new Setter(OffsetProperty, 0.0) }
        });

        // 终点：中选者那一块正好压在指针下面。
        animation.Children.Add(new KeyFrame
        {
            KeyTime = _planTotal,
            Setters = { new Setter(OffsetProperty, _targetOffset) }
        });

        await animation.RunAsync(this, token);
    }

    /// <inheritdoc/>
    public override void SnapToEnd() => Offset = _targetOffset;

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // 喵~防御：尺寸还没量出来（0×0）或者轨道是空的时画不了，直接返回。
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || _track.Count == 0)
        {
            return;
        }

        var width = Bounds.Width;
        var height = Bounds.Height;

        // 喵~防御：属性还没被动画写过时可能是 NaN，按「还没开始滚」处理。
        var offset = double.IsFinite(Offset) ? Offset : 0;
        // 相邻两块中心的距离。宽度至少是 1，不会除零。
        var pitch = _slotWidth + _gap;
        // 停稳了没有：滚到终点就说明中选者已经在指针下面了。
        var settled = Math.Abs(offset - _targetOffset) < 0.5;
        // 方块纵向居中摆。
        var slotTop = (height - _slotHeight) / 2;
        // 指针（固定在正中央）底下是第几块。几何判定完全交给 CsgoTrack，和排片表同源。
        var centerIndex = CsgoTrack.IndexAtOffset(offset, _slotWidth, _gap, width);

        for (var i = 0; i < _track.Count; i++)
        {
            // 这一块左边缘在屏幕上的位置。
            var left = i * pitch - offset;

            // 完全在可视区外面的方块直接跳过——名单展开好几轮时能省下大量绘制。
            if (left + _slotWidth < 0 || left > width)
            {
                continue;
            }

            var rect = new Rect(left, slotTop, _slotWidth, _slotHeight);
            // 是不是指针正下方那一块。
            var isCenter = i == centerIndex;
            // 离中心越远越淡，视线自然被拉到指针附近。
            var distance = Math.Min(1, Math.Abs(rect.Center.X - width / 2) / (width / 2));
            // 指针下那一块始终满亮，其余按距离衰减到四成。
            var opacity = isCenter ? 1.0 : 0.4 + 0.6 * (1 - distance);

            // 方块底色：和卡片的深色玻璃一个色系。
            var background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A));
            // 停稳后中选者用强调色描边，其余方块描一条很淡的白边。
            IBrush border = isCenter && settled
                ? new SolidColorBrush(Accent)
                : new SolidColorBrush(Colors.White, isCenter ? 0.42 : 0.16);
            // 中选者停稳后文字也换成强调色。
            IBrush textBrush = isCenter && settled ? new SolidColorBrush(Accent) : Brushes.White;

            using (context.PushOpacity(opacity))
            {
                DrawSlot(context, rect, background, border, isCenter ? 2 : 1.2, _slotHeight * 0.16);
                // 名字画在方块里，放不下会自动缩字号。
                DrawFittedText(context, _track[i],
                    rect.Deflate(new Thickness(_slotWidth * 0.08, _slotHeight * 0.12)),
                    _slotHeight * 0.42, textBrush);
            }
        }

        // 指针：方块区域上下各一个三角，顶点朝着方块，指哪停哪。
        DrawPointer(context, width, slotTop);
    }

    /// <summary>画正中央的指针。</summary>
    private void DrawPointer(DrawingContext context, double width, double slotTop)
    {
        var centerX = width / 2;
        // 三角的高。
        var size = _slotHeight * 0.16;
        var brush = new SolidColorBrush(Accent);

        // 方块上边缘往上一点点，画一条短竖线，让指针从画面外「伸」进来。
        context.DrawLine(new Pen(brush, 2),
            new Point(centerX, slotTop - size * 2.2), new Point(centerX, slotTop));
        // 方块下边缘往下，同样伸出去一小段。
        context.DrawLine(new Pen(brush, 2),
            new Point(centerX, slotTop + _slotHeight), new Point(centerX, slotTop + _slotHeight + size * 2.2));

        // 两个三角合成一个几何体，一次画完。
        var marker = new StreamGeometry();
        using (var geometry = marker.Open())
        {
            // 上面的三角：顶点朝下，指着方块的上边缘。
            geometry.BeginFigure(new Point(centerX - size, slotTop - size * 2.2), true);
            geometry.LineTo(new Point(centerX + size, slotTop - size * 2.2));
            geometry.LineTo(new Point(centerX, slotTop));
            geometry.EndFigure(true);

            // 下面的三角：顶点朝上，指着方块的下边缘。
            geometry.BeginFigure(new Point(centerX - size, slotTop + _slotHeight + size * 2.2), true);
            geometry.LineTo(new Point(centerX + size, slotTop + _slotHeight + size * 2.2));
            geometry.LineTo(new Point(centerX, slotTop + _slotHeight));
            geometry.EndFigure(true);
        }

        context.DrawGeometry(brush, null, marker);
    }
}
