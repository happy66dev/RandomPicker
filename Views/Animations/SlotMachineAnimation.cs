using System;
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
/// 「老虎机」动画：一排格子，一格一格地抽字，每格停在有可能的那个字上。
/// </summary>
/// <remarks>
/// 驱动属性是四个 <c>Slot0Progress</c>~<c>Slot3Progress</c>，各占 0~1，
/// 分别表示「这一格转了多少」。同一时刻只有一格在转，所以拼起来就是一个完整的抽字过程。
/// <para/>
/// <b>最容易被写坏的地方：</b>中选者比格数短的时候（四字名的名单里抽到「张三」），
/// 末尾几格要留空。<see cref="SlotPlan.WinnerChars"/> 里对应项是 <c>null</c>，
/// 这一格<b>从头到尾都画空框、一个字都不转</b>——绝不能拿名单里别人的字去填，
/// 那样板面和真正中选的人对不上，是最伤信任的一类 bug。
/// </remarks>
internal sealed class SlotMachineAnimation : RevealAnimationBase
{
    /// <summary>第一格的进度：这一格转了多少（0~1）。</summary>
    public static readonly StyledProperty<double> Slot0ProgressProperty =
        AvaloniaProperty.Register<SlotMachineAnimation, double>(nameof(Slot0Progress));

    /// <summary>第二格的进度。</summary>
    public static readonly StyledProperty<double> Slot1ProgressProperty =
        AvaloniaProperty.Register<SlotMachineAnimation, double>(nameof(Slot1Progress));

    /// <summary>第三格的进度。</summary>
    public static readonly StyledProperty<double> Slot2ProgressProperty =
        AvaloniaProperty.Register<SlotMachineAnimation, double>(nameof(Slot2Progress));

    /// <summary>第四格的进度。</summary>
    public static readonly StyledProperty<double> Slot3ProgressProperty =
        AvaloniaProperty.Register<SlotMachineAnimation, double>(nameof(Slot3Progress));

    static SlotMachineAnimation()
    {
        // 四个进度属性里任何一个变了都要重画。
        AffectsRender<SlotMachineAnimation>(Slot0ProgressProperty, Slot1ProgressProperty,
            Slot2ProgressProperty, Slot3ProgressProperty);
    }

    /// <summary>每一格旋转时依次出现的字，最后一个必定是中选者的那个字；空序列表示这一格留空。</summary>
    private char[][] _spinSequences = [];

    /// <summary>当前实际用了几格（2~4）。</summary>
    private int _slotCount;

    /// <summary>每一格转多久，单位：秒。装载时从排片表抄下来。</summary>
    private double _stepSeconds = 1.5;

    /// <summary>
    /// 整段动画的总时长，含最后一格停住之后那段定格。装载时从排片表抄下来。
    /// </summary>
    /// <remarks>它<b>大于</b>「每格时长 × 格数」，多出来的那一截就是让主人看清名字的留白。</remarks>
    private TimeSpan _planTotal = TimeSpan.FromSeconds(1.5 * 2);

    /// <summary>一个格子的宽度。</summary>
    private double _cellWidth;

    /// <summary>一个格子的高度。</summary>
    private double _cellHeight;

    /// <summary>格子之间的间距。</summary>
    private double _gap;

    /// <summary>板面左右两侧的留白。</summary>
    private double _padding;

    /// <summary>算好的舞台尺寸。</summary>
    private Size _stageSize;

    public SlotMachineAnimation(double revealFontSize, Color accent)
        : base(revealFontSize, accent)
    {
    }

    /// <summary>第一格的进度。</summary>
    public double Slot0Progress
    {
        get => GetValue(Slot0ProgressProperty);
        set => SetValue(Slot0ProgressProperty, value);
    }

    /// <summary>第二格的进度。</summary>
    public double Slot1Progress
    {
        get => GetValue(Slot1ProgressProperty);
        set => SetValue(Slot1ProgressProperty, value);
    }

    /// <summary>第三格的进度。</summary>
    public double Slot2Progress
    {
        get => GetValue(Slot2ProgressProperty);
        set => SetValue(Slot2ProgressProperty, value);
    }

    /// <summary>第四格的进度。</summary>
    public double Slot3Progress
    {
        get => GetValue(Slot3ProgressProperty);
        set => SetValue(Slot3ProgressProperty, value);
    }

    /// <summary>板凳面：几格宽、一格高。</summary>
    public override Size StageSize => _stageSize;

    /// <inheritdoc/>
    public override void Load(RevealAnimationPlan plan)
    {
        // 喵~防御：装错了计划类型时什么都不画，也好过抛异常把结果卡住。
        if (plan is not SlotPlan slot)
        {
            _slotCount = 0;
            _planTotal = TimeSpan.Zero;
            _stageSize = default;
            return;
        }

        // 格数夹在 2~4：排片表本来就该给出这个范围，这里再兜一道。
        _slotCount = Math.Clamp(slot.SlotCount, SlotMachine.MinSlots, SlotMachine.MaxSlots);

        // 喵~防御：单格时长不是正数时总时长会变成 0，动画一闪而过什么也看不见。
        // 兜底成 1 秒，至少让用户看清板面。
        _stepSeconds = slot.Step.TotalSeconds > 0 ? slot.Step.TotalSeconds : 1;

        // 总时长照抄排片表：它已经含了定格那一段，最后一格的落点因此提前于它。
        _planTotal = slot.Total;

        _spinSequences = new char[_slotCount][];
        for (var i = 0; i < _slotCount; i++)
        {
            // 这一格的候选字（已排序去重，字符串里的每个字符就是一个候选）。
            var candidates = i < slot.Candidates.Count ? slot.Candidates[i] : null;
            // 中选者在这一格的字；null 表示这一格留空。
            var winnerChar = i < slot.WinnerChars.Count ? slot.WinnerChars[i] : null;
            // 这一格旋转时依次出现的字。
            _spinSequences[i] = BuildSpinSequence(candidates, winnerChar);
        }

        // 格子尺寸跟着中央大字的字号档位走。
        _cellHeight = RevealFontSize * 1.15;
        _cellWidth = RevealFontSize * 1.05;
        _gap = RevealFontSize * 0.14;
        _padding = RevealFontSize * 0.2;

        // 宽度 = 左右留白 + 所有格子 + 格子之间的缝；高度 = 一格再加一点上下留白放描边。
        _stageSize = new Size(
            _padding * 2 + _slotCount * _cellWidth + (_slotCount - 1) * _gap,
            _cellHeight + RevealFontSize * 0.3);
    }

    /// <inheritdoc/>
    public override async Task PlayAsync(CancellationToken token)
    {
        // 喵~防御：一格都没有（装载失败）时直接结束，上层照样会出结果。
        if (_slotCount <= 0)
        {
            return;
        }

        await BuildAnimation().RunAsync(this, token);
    }

    /// <summary>
    /// 按排片表造出这段动画。抽出来是为了让「定格那段留白真的存在」能被单测验到。
    /// </summary>
    /// <remarks>
    /// <b>最后一格的落点是 <c>总时长 - 定格时长</c>，后面那一截留白就是定格。</b>
    /// 排片表的 <see cref="SlotPlan.Total"/> 已经含了 <see cref="SlotMachine.SettleSeconds"/>，
    /// 而这里所有关键帧都落在「总时长 - 定格」之前，于是 Avalonia 在最后一个关键帧之后
    /// 保持终值不动（<c>FillMode.Forward</c> 会把最后一帧的值延续到结尾）——
    /// 板面就这样静止着让主人看清拼出来的名字。
    /// <para/>
    /// 要是哪次改动把这些关键帧推到了结尾，留白就没了，
    /// 「最后一个字展示一会会再继续」这条需求会静默失效，所以有测试盯着它。
    /// </remarks>
    internal Animation BuildAnimation()
    {
        // 直线推每一格的进度；「先快后慢」的效果放在 Render 里做，
        // 这样时间轴本身保持简单——每格占的时间窗一眼就能看出来。
        var animation = new Animation
        {
            // 总时长含定格那段，所以最后一格的落点会提前于它。
            Duration = _planTotal,
            FillMode = FillMode.Forward,
            Easing = new LinearEasing()
        };

        for (var i = 0; i < _slotCount; i++)
        {
            // 这一格对应的进度属性。
            var property = SlotProgressProperty(i);
            // 还没轮到这一格时钉在 0 上——板面上应该是个没字的空格子。
            animation.Children.Add(new KeyFrame
            {
                KeyTime = TimeSpan.FromSeconds(_stepSeconds * i),
                Setters = { new Setter(property, 0.0) }
            });
            // 轮到这一格：在它自己的时间窗里从 0 走到 1。
            animation.Children.Add(new KeyFrame
            {
                KeyTime = TimeSpan.FromSeconds(_stepSeconds * (i + 1)),
                Setters = { new Setter(property, 1.0) }
            });
        }

        return animation;
    }

    /// <inheritdoc/>
    public override void SnapToEnd()
    {
        // 四格全部置 1：留空的格子没有旋转序列，画的时候照样是空框。
        Slot0Progress = 1;
        Slot1Progress = 1;
        Slot2Progress = 1;
        Slot3Progress = 1;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // 喵~防御：尺寸还没量出来（0×0）或者没有格子时画不了，直接返回。
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || _slotCount <= 0)
        {
            return;
        }

        // 板面纵向居中。
        var top = (Bounds.Height - _cellHeight) / 2;

        for (var i = 0; i < _slotCount; i++)
        {
            // 这一格在屏幕上的位置。
            var rect = new Rect(_padding + i * (_cellWidth + _gap), top, _cellWidth, _cellHeight);

            // 喵~防御：进度还没被动画写过时可能是 NaN，按「还没开始转」处理。
            var raw = SlotProgress(i);
            var progress = double.IsFinite(raw) ? Math.Clamp(raw, 0, 1) : 0;
            // 走到 1 就说明这一格定下来了。
            var settled = progress >= 1;

            // 这一格旋转时依次出现的字；长度 0 表示这一格留空。
            var sequence = _spinSequences[i];

            // 这一格当前该显示哪个字。留空格和还没开始的格子都是 null（什么都不画）。
            char? shown = null;
            if (sequence.Length > 0 && progress > 0)
            {
                // 把线性进度压成「起手飞快、收尾一点一点挪」，曲线和另外三种样式共用。
                var eased = RevealEasing.EaseOutCubic(progress);
                // 落到序列的第几个字上。
                var step = (int)Math.Floor(eased * (sequence.Length - 1));
                // 取出来夹一下，浮点误差不会让它越界。
                shown = sequence[Math.Clamp(step, 0, sequence.Length - 1)];
            }

            // 格子底色：没定下来的暗一些，定下来的亮一些。
            var background = new SolidColorBrush(settled
                ? Color.FromRgb(0x2A, 0x2A, 0x34)
                : Color.FromRgb(0x1E, 0x1E, 0x26));
            // 定下来的格子用强调色描边，其余是很淡的白边。
            var border = settled
                ? new SolidColorBrush(Accent)
                : new SolidColorBrush(Colors.White, 0.16);
            // 定格的字用强调色，滚动中的字是白的。
            IBrush textBrush = settled ? new SolidColorBrush(Accent) : Brushes.White;

            DrawSlot(context, rect, background, border, settled ? 2 : 1.2, _cellHeight * 0.16);

            // 有字才画字：留空的格子就是一个空框。
            if (shown is not null)
            {
                // 把单个字放大到格子中间，左右留一点边。
                DrawFittedText(context, shown.Value.ToString(),
                    rect.Deflate(new Thickness(_cellWidth * 0.1, _cellHeight * 0.1)),
                    _cellHeight * 0.62, textBrush);
            }
        }
    }

    /// <summary>按格号取对应的进度属性。</summary>
    private static StyledProperty<double> SlotProgressProperty(int index) => index switch
    {
        0 => Slot0ProgressProperty,
        1 => Slot1ProgressProperty,
        2 => Slot2ProgressProperty,
        // 格数最多 4 格，越界的只会是 3。
        _ => Slot3ProgressProperty
    };

    /// <summary>取第 <paramref name="index"/> 格的当前进度。</summary>
    private double SlotProgress(int index) => GetValue(SlotProgressProperty(index));

    /// <summary>
    /// 拼出某一格旋转时依次出现的字。
    /// </summary>
    /// <param name="candidates">这一格的候选字（已排序去重）。</param>
    /// <param name="winnerChar">中选者在这一格的字；<c>null</c> 表示这一格留空。</param>
    /// <returns>依次出现的字，最后一个是 <paramref name="winnerChar"/>；留空的格子返回空数组。</returns>
    private static char[] BuildSpinSequence(string? candidates, char? winnerChar)
    {
        // 喵~防御：留空的格子（中选者比格数短）没有字可转，给一个空序列。
        if (winnerChar is null || string.IsNullOrEmpty(candidates))
        {
            return [];
        }

        // 转 12 下收尾。前 11 个从候选里轮着取，够快才像在抽。
        const int steps = 12;
        var sequence = new char[steps];
        var cursor = 0;
        for (var i = 0; i < steps - 1; i++)
        {
            // 喵~防御：轮到的这个字如果正好是结果，就往前跳一个，
            // 免得倒数第二下就已经显示结果、看起来像「早就停了」。
            if (candidates[cursor % candidates.Length] == winnerChar.Value)
            {
                cursor++;
            }

            // 把当前这个字写进序列。
            sequence[i] = candidates[cursor % candidates.Length];
            cursor++;
        }

        // 最后一个字必定是中选者的那个字。
        sequence[steps - 1] = winnerChar.Value;
        return sequence;
    }
}
