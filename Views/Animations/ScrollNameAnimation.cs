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
/// 排片表给出的换帧间隔是<b>等比增长</b>的（越换越慢），两条关键帧之间线性插值，
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

    /// <summary>这一段动画的总时长。既是 <c>Animation.Duration</c>，也就是排片表里的 Total。</summary>
    private TimeSpan _planTotal;

    /// <summary>动作停下的时刻。末帧落在这儿，之后到总时长为止是定格。</summary>
    private TimeSpan _motionTotal;

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
            _planTotal = TimeSpan.Zero;
            _motionTotal = TimeSpan.Zero;
            _stageSize = default;
            return;
        }

        _frames = scroll.Frames;
        _intervals = scroll.Intervals;
        // 总时长照抄排片表；动作在「总时长 - 定格」那一刻就停了，末帧落在那里。
        _planTotal = scroll.Total;
        _motionTotal = scroll.MotionTotal;
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


    /// <summary>
    /// 按排片表造出这段动画。抽出来是为了让「时长和时刻表对不对得上」能被单测验到。
    /// </summary>
    /// <remarks>
    /// <b><see cref="Animation.Duration"/> 必须写，漏了整段动画直接不播。</b>
    /// Avalonia 用它当分母把每个关键帧的 <c>KeyTime</c> 换算成 cue
    /// （<c>Animation.cs</c> 里 <c>cue = KeyTime.TotalSeconds / Duration.TotalSeconds</c>）。
    /// 默认值是 <see cref="TimeSpan.Zero"/>，于是第一个关键帧算出 <c>0/0 = NaN</c>，
    /// 而 <c>Cue</c> 的构造函数只接受 0~1、NaN 两个比较都不成立，
    /// 当场抛「This cue object's value should be within or equal to 0.0 and 1.0」。
    /// 那个异常会被上层「绝不吞掉结果」的兜底接住，表现是<b>结果照出、动画全无、还不报错</b>——
    /// 2026-09-15 主人报的「动画并没有触发」就是这个。
    /// <para/>
    /// 时长取排片表的总时长（含定格），而末帧落在 <c>MotionTotal</c> 上，
    /// 两者之间那一截就是定格：中选者的名字停住不动，让人看清。
    /// </remarks>
    internal override IReadOnlyList<Animation> BuildAnimations()
    {
        // 帧间线性插值，减速效果全部由「间隔越来越长」来表达。
        var animation = new Animation
        {
            // 总时长取排片表声明的那个值：它和末帧落在的时刻同源，末帧的 cue 才落在 0~1 里。
            Duration = _planTotal,
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
            // 末帧钉死在「动作结束时刻」上。两个理由：
            // 一是逐项 TimeSpan.FromSeconds 每次都会取整到 tick，240 项攒下来能差出几微秒，
            // 不钉的话末帧的 cue 会是 0.9999996 而不是 1；
            // 二是末帧之后到总时长为止要留出定格，落在总时长上就没有定格了。
            var at = i == _frames.Count - 1 ? _motionTotal : elapsed;
            // 关键帧落在这个时刻，值就是帧号 i。
            animation.Children.Add(new KeyFrame
            {
                KeyTime = at,
                Setters = { new Setter(ProgressProperty, (double)i) }
            });
        }

        return [animation];
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
