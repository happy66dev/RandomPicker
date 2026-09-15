using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using ClassIsland.RandomPicker.Services;

namespace ClassIsland.RandomPicker.Views.Animations;

/// <summary>
/// 四种抽选动画的共同底座。
/// </summary>
/// <remarks>
/// 整体架构是「<b>属性存进度，Render 现算几何</b>」：动画只负责把某个进度属性从起点推到终点，
/// 画面上长什么样完全由 <see cref="Render"/> 按当前进度算出来。
/// <para/>
/// 这么做的好处是几何成了纯函数——同样的进度必定画出同样的画面。
/// 抽选动画最容易出的两类 bug（转盘停在旁边那一格、老虎机的字和中选者对不上）
/// 都能靠「对着进度数字验算」定位，而不是靠反复看动画猜。
/// 宿主里的 <c>SlantedMaskControl</c> 用的也是这套写法。
/// <para/>
/// 每个子类都<b>必须</b>给出 <see cref="StageSize"/>。裸 <see cref="Control"/> 默认量出 0×0，
/// 那样画面上会是一片空白<b>而且不报任何错</b>，排查起来最费劲。
/// </remarks>
internal abstract class RevealAnimationBase : Control
{
    /// <summary>建立一个动画控件。</summary>
    /// <param name="revealFontSize">中央大字的字号档位，动画里所有尺寸都按它换算。</param>
    /// <param name="accent">主题强调色，用来画中选者的高亮。</param>
    protected RevealAnimationBase(double revealFontSize, Color accent)
    {
        // 喵~防御：字号配置被手改成 0、负数或 NaN 时，按它换算出来的尺寸全是坏值，
        // 量出来的舞台会是负的，整个卡片会塌成一条线。这里兜一个能看的字号。
        RevealFontSize = double.IsFinite(revealFontSize) && revealFontSize > 0 ? revealFontSize : 48;
        Accent = accent;
        // 动画只在自己的地盘里画，滚出界的内容一律裁掉。
        ClipToBounds = true;
        // 舞台尺寸是算好的固定值，居中摆，不让布局把它拉伸开。
        // 注意右边得写全限定名：类里已经有一个同名属性，光写 HorizontalAlignment.Center 会被解析成那个属性。
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    /// <summary>中央大字的字号档位。动画里所有尺寸都按它换算，保证和插件其它部分同一套视觉尺度。</summary>
    protected double RevealFontSize { get; }

    /// <summary>主题强调色。</summary>
    protected Color Accent { get; }

    /// <summary>这块动画需要多大地方。装好排片表之后才是准确值。</summary>
    public abstract Size StageSize { get; }

    /// <summary>装载排片表。必须在 <see cref="PlayAsync"/> 之前调用。</summary>
    public abstract void Load(RevealAnimationPlan plan);

    /// <summary>播放这段动画。被取消时抛 <see cref="OperationCanceledException"/>。</summary>
    public abstract Task PlayAsync(CancellationToken token);

    /// <summary>把进度硬置到终点。</summary>
    /// <remarks>
    /// 只在动画<b>正常播完</b>时调用。被取消时绝不能调用——控件会被下一轮复用，
    /// 硬置的终值会让它一上来就显示上一轮的中选者。
    /// </remarks>
    public abstract void SnapToEnd();

    /// <summary>按舞台尺寸申请空间，别让布局量出 0×0。</summary>
    protected override Size MeasureOverride(Size availableSize) => StageSize;

    /// <summary>按指定字号排一段文字。</summary>
    protected FormattedText MeasureText(string text, double fontSize, IBrush brush) =>
        new(text ?? string.Empty,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            // 半粗体，和中央大字的字重保持一致。
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
            fontSize,
            brush);

    /// <summary>
    /// 在指定区域里居中画一段文字，放不下就按比例缩小字号。
    /// </summary>
    /// <param name="context">绘制上下文。</param>
    /// <param name="text">要画的文字。</param>
    /// <param name="area">文字可以占用的矩形区域。</param>
    /// <param name="fontSize">期望的字号。</param>
    /// <param name="brush">文字颜色。</param>
    protected void DrawFittedText(DrawingContext context, string text, Rect area,
        double fontSize, IBrush brush)
    {
        // 喵~防御：区域还没量出来（宽或高是 0、负数、NaN）时画不了，直接跳过。
        if (!double.IsFinite(area.Width) || !double.IsFinite(area.Height)
            || area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        // 喵~防御：名字理论上不会是空的，但名单是外部文件读进来的，空串直接跳过。
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var formatted = MeasureText(text, fontSize, brush);
        // 宽度超了就把字号按比例缩回去，最多缩到一半——再小就真看不清了。
        if (formatted.Width > area.Width)
        {
            var shrink = Math.Max(0.5, area.Width / formatted.Width);
            formatted = MeasureText(text, fontSize * shrink, brush);
        }

        // 横竖都居中摆。
        context.DrawText(formatted, new Point(
            area.X + (area.Width - formatted.Width) / 2,
            area.Y + (area.Height - formatted.Height) / 2));
    }

    /// <summary>画一个圆角方块。动画里的「格子」「名字方块」都用它。</summary>
    protected static void DrawSlot(DrawingContext context, Rect rect, IBrush background,
        IBrush border, double borderThickness, double cornerRadius) =>
        context.DrawRectangle(background, new Pen(border, borderThickness), rect, cornerRadius, cornerRadius);
}
