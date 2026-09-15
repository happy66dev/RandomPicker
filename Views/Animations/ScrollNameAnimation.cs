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
/// 「滚动名字」动画：中央大字不停地换名字，换得越来越慢，最后定格在中选者上。
/// </summary>
/// <remarks>
/// 驱动属性是 <see cref="Progress"/>，单位是<b>帧号</b>而不是 0~1 的比例。
/// 排片表给出的换帧时刻是等差递增的，两条关键帧之间线性插值，
/// 所以 <c>floor(Progress)</c> 天然就是「现在停在第一帧」，
/// 小数部分则是「正从这一帧滚到下一帧、滚了多少」。
/// <para/>
/// 用帧号当进度还有一层好处：帧数由排片表决定，界面上不需要再知道任何时刻表。
/// </remarks>
internal sealed class ScrollNameAnimation : RevealAnimationBase
{
    /// <summary>当前进度，单位是帧号（0 表示第一帧）。</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<ScrollNameAnimation, double>(nameof(Progress));

    static ScrollNameAnimation()
    {
        // 进度一变就重画。
        AffectsRender<ScrollNameAnimation>(ProgressProperty);
    }

    /// <summary>依次显示的名字，最后一帧必定是中选者。</summary>
    private IReadOnlyList<string> _frames = [];

    /// <summary>每一帧停留多久，单位：秒，长度和 <see cref="_frames"/> 一致。</summary>
    private IReadOnlyList<double> _intervals = [];

    /// <summary>一行文字占多高。滚动时上下两行就是这个间距。</summary>
    private double _lineHeight;

    /// <summary>算好的舞台尺寸。</summary>
    private Size _stageSize;

    public ScrollNameAnimation(double revealFontSize, Color accent)
        : base(revealFontSize, accent)
    {
        // 一行的高度留出 35% 的行距，换名字时才不会挤在一起。
        _lineHeight = RevealFontSize * 1.35;
    }

    /// <summary>当前进度，单位是帧号。</summary>
    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>宽度放下最长的一个名字，高度放下上下两行——滚动才有地方走。</summary>
    public override Size StageSize => _stageSize;

    /// <inheritdoc/>
    public override void Load(RevealAnimationPlan plan)
    {
        // 喵~防御：装错了计划类型（调用方写错）时什么都不画，也好过抛异常把结果卡住。
        if (plan is not ScrollPlan scroll)
        {
            _frames = [];
            _intervals = [];
            _stageSize = default;
            return;
        }

        _frames = scroll.Frames;
        _intervals = scroll.Intervals;
        _lineHeight = RevealFontSize * 1.35;

        // 舞台宽度要放得下最长的那一帧，不然换到长名字时会被裁掉。
        var widest = 0.0;
        foreach (var frame in _frames)
        {
            // 按最终字号量一遍宽度。
            var width = MeasureText(frame, RevealFontSize, Brushes.White).Width;
            if (width > widest)
            {
                widest = width;
            }
        }

        // 至少留出两个字加两侧留白的宽度，名单里全是单字名时才不会窄成一条。
        var stageWidth = Math.Max(RevealFontSize * 2.4, widest + RevealFontSize * 0.8);
        // 高度是上下两行，正好容下滚动过程中同时出现的那两帧。
        _stageSize = new Size(stageWidth, _lineHeight * 2);
    }

    /// <inheritdoc/>
    public override async Task PlayAsync(CancellationToken token)
    {
        // 喵~防御：没有可播的帧（装载失败或名单为空）时直接结束，
        // 上层照样会走「出结果」的收尾，不会因为动画缺席而丢结果。
        if (_frames.Count == 0 || _intervals.Count != _frames.Count)
        {
            return;
        }

        // 帧间线性插值，减速效果全部由「间隔越来越长」来表达。
        var animation = new Animation
        {
            FillMode = FillMode.Forward,
            Easing = new LinearEasing()
        };

        // 起点：第 0 帧，时刻 0。
        animation.Children.Add(new KeyFrame
        {
            KeyTime = TimeSpan.Zero,
            Setters = { new Setter(ProgressProperty, 0.0) }
        });

        var elapsed = TimeSpan.Zero;
        for (var i = 0; i < _frames.Count; i++)
        {
            // 第 i 帧停留的时长；坏值当 0 处理，总和仍然等于排片表给的总时长。
            var seconds = double.IsFinite(_intervals[i]) && _intervals[i] > 0 ? _intervals[i] : 0;
            elapsed += TimeSpan.FromSeconds(seconds);
            // 关键帧落在这个时刻，值就是帧号 i。
            animation.Children.Add(new KeyFrame
            {
                KeyTime = elapsed,
                Setters = { new Setter(ProgressProperty, (double)i) }
            });
        }

        await animation.RunAsync(this, token);
    }

    /// <inheritdoc/>
    public override void SnapToEnd() => Progress = Math.Max(0, _frames.Count - 1);

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // 喵~防御：尺寸还没量出来（0×0）或者没有帧时画不了，直接返回。
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || _frames.Count == 0)
        {
            return;
        }

        var width = Bounds.Width;
        var height = Bounds.Height;

        // 喵~防御：属性还没被动画写过时可能是 NaN，按第 0 帧处理。
        var raw = double.IsFinite(Progress) ? Progress : 0;
        // 整数部分就是当前帧号。
        var index = Math.Clamp((int)Math.Floor(raw), 0, _frames.Count - 1);
        // 小数部分是「正滚到下一帧、滚了多少」。
        var fraction = Math.Clamp(raw - index, 0, 1);
        // 下一帧；已经是最后一帧时就是它自己。
        var nextIndex = Math.Min(index + 1, _frames.Count - 1);

        // 上一帧往上滚出去时的纵向位置。
        var outgoingTop = height / 2 - _lineHeight / 2 - fraction * _lineHeight;
        // 下一帧从下面滚进来时的纵向位置。
        var incomingTop = height / 2 - _lineHeight / 2 + (1 - fraction) * _lineHeight;

        // 滚出去的那一帧越淡、滚进来的越清楚，两行字才不会糊成一团。
        var outgoingBrush = new SolidColorBrush(Colors.White, 1 - 0.62 * fraction);
        // 滚进来的那一帧的透明度与上一条相反。
        var incomingBrush = new SolidColorBrush(Colors.White, 0.38 + 0.62 * fraction);

        // 定格之后（两帧是同一个）只画一行，不重复画两遍。
        if (nextIndex == index)
        {
            DrawFittedText(context, _frames[index],
                new Rect(0, height / 2 - _lineHeight / 2, width, _lineHeight), RevealFontSize, Brushes.White);
            return;
        }

        // 先画滚出去的那一帧。
        DrawFittedText(context, _frames[index], new Rect(0, outgoingTop, width, _lineHeight),
            RevealFontSize, outgoingBrush);
        // 再画滚进来的那一帧，盖在上面。
        DrawFittedText(context, _frames[nextIndex], new Rect(0, incomingTop, width, _lineHeight),
            RevealFontSize, incomingBrush);
    }
}
