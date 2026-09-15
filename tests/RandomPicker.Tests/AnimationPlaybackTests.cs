using System;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassIsland.RandomPicker.Models;
using ClassIsland.RandomPicker.Services;
using ClassIsland.RandomPicker.Views;
using ClassIsland.RandomPicker.Views.Animations;

namespace ClassIsland.RandomPicker.Tests;

/// <summary>
/// 动画<b>真的会播</b>的测试。
/// </summary>
/// <remarks>
/// <b>这个文件是为 2026-09-15「动画并没有触发」建的。</b>
/// 滚动名字那个控件建 <c>Animation</c> 时漏了 <c>Duration</c>，而 Avalonia 拿它当分母
/// 把每个关键帧的 <c>KeyTime</c> 换算成 cue（<c>cue = KeyTime / Duration</c>）：
/// 默认值是 <c>TimeSpan.Zero</c>，第一个关键帧算出 <c>0/0 = NaN</c>，
/// <c>Cue</c> 的构造函数当场抛异常，动画在起播的那一瞬间就失败了。
/// 那个异常被 <c>RevealWindow</c> 里「绝不吞掉结果」的兜底接住，
/// 于是症状是<b>结果照出、动画全无、还不报错</b>。
/// <para/>
/// 为什么原有测试全绿：<c>AnimationControlTests</c> 只验「量得出尺寸」和「画得出像素」——
/// 这两件事在<b>装载</b>阶段就完成了，和「时间轴跑不跑得起来」是两码事；
/// <c>RevealWindowTests.Play_WithRealPlan_DoesNotThrow</c> 又只看「起播那一下不抛异常」，
/// 而异常是<b>异步</b>发生的，早就被吞在后面了。
/// 这里补的就是这道缝：既要<b>结构上</b>验时长和时刻表对得上，
/// 也要<b>行为上</b>验「时钟没走，结果就不许出来」。
/// </remarks>
public class AnimationPlaybackTests
{
    /// <summary>测试用名单。</summary>
    private static readonly string[] Names = ["张三", "李小四", "王五"];

    /// <summary>一份默认设置，用来取字号档位。</summary>
    private static readonly PickerSettings BaseSettings = new();

    /// <summary>结果停留时长，测试里取短一点。</summary>
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(1);

    #region 结构：时长和时刻表必须对得上

    /// <summary>
    /// 滚动名字的动画必须带上总时长，且每个关键帧算出来的 cue 都落在 0~1 里。
    /// </summary>
    /// <remarks>
    /// 这一条直接复现 Avalonia 内部那条换算规则。只要能过，就不会再出现
    /// 「关键帧全都落在合法区间之外、动画起播即失败」。
    /// </remarks>
    [AvaloniaFact]
    public void ScrollAnimation_CarriesADurationAndKeepsEveryCueInRange()
    {
        // 造一份真实的排片表，从它身上取「应该播多久」。
        var plan = RevealAnimationPlanner.Build(
            RevealAnimationStyle.Scroll, Names, "张三", BaseSettings);
        var scroll = Assert.IsType<ScrollPlan>(plan);

        var control = new ScrollNameAnimation(BaseSettings.RevealFontSize, Colors.Cyan);
        control.Load(scroll);

        var animation = control.BuildAnimation();

        // 喵~防御：时长是零就是那个 bug 本身——Avalonia 会拿它当分母。
        Assert.True(animation.Duration > TimeSpan.Zero,
            "动画时长为零，Avalonia 拿它当分母算 cue，动画会在起播瞬间就失败");
        // 时长必须和排片表声明的那个值一致，否则 cue 会整体偏移。
        Assert.Equal(scroll.Total, animation.Duration);

        // 每个关键帧的 cue 都要落在 0~1 里，和 Avalonia 的算法同源。
        Assert.NotEmpty(animation.Children);
        foreach (var keyFrame in animation.Children)
        {
            var cue = CueOf(keyFrame, animation.Duration);
            Assert.InRange(cue, 0.0, 1.0);
        }

        // 最后一个关键帧正好落在总时长上，cue 因此是 1——动画的终点就是排片表的终点。
        Assert.Equal(animation.Duration, animation.Children[animation.Children.Count - 1].KeyTime);
    }

    /// <summary>
    /// 不给 <c>Duration</c> 的动画，算出来的 cue 一定越界，<c>Cue</c> 会抛异常。
    /// </summary>
    /// <remarks>
    /// <b>这条是故意写来「证伪」的</b>：它把那个 bug 的机理钉在测试里，
    /// 让后来的人一眼看清「少写一行 Duration」为什么会要命，
    /// 而不是只看到上面那条断言、不明白它防的是什么。
    /// </remarks>
    [AvaloniaFact]
    public void AnimationWithoutDuration_ProducesOutOfRangeCues()
    {
        // 复现当时的写法：只给 KeyTime，不给 Duration。
        var broken = new Animation { FillMode = FillMode.Forward, Easing = new LinearEasing() };
        broken.Children.Add(new KeyFrame
        {
            KeyTime = TimeSpan.Zero,
            Setters = { new Setter(ScrollNameAnimation.ProgressProperty, 0.0) }
        });
        broken.Children.Add(new KeyFrame
        {
            KeyTime = TimeSpan.FromSeconds(4),
            Setters = { new Setter(ScrollNameAnimation.ProgressProperty, 10.0) }
        });

        // Duration 默认就是零秒。
        Assert.Equal(TimeSpan.Zero, broken.Duration);

        // 照抄 Avalonia 的换算：KeyTime / Duration。
        // 第一个关键帧是 0/0 = NaN，第二个是 4/0 = 正无穷，两个都不在 0~1 里。
        Assert.Throws<ArgumentException>(() => _ = new Cue(broken.Children[0].KeyTime.TotalSeconds
                                                          / broken.Duration.TotalSeconds));
        Assert.Throws<ArgumentException>(() => _ = new Cue(broken.Children[1].KeyTime.TotalSeconds
                                                          / broken.Duration.TotalSeconds));
    }

    /// <summary>按 Avalonia 的算法，把一个关键帧换算成 cue。</summary>
    /// <remarks>关键帧写的是绝对时刻（<c>KeyFrameTimingMode.TimeSpan</c>），所以要除以总时长。</remarks>
    private static double CueOf(KeyFrame keyFrame, TimeSpan duration) =>
        keyFrame.KeyTime.TotalSeconds / duration.TotalSeconds;

    #endregion

    #region 行为：时钟没走，结果不许出来

    /// <summary>
    /// 四种样式都一样：排片表交出去之后，动画没跑完就不许把结果交出来。
    /// </summary>
    /// <remarks>
    /// 这条是为「动画没播还看不出来」补的。当时的 bug 会让动画<b>立刻失败</b>，
    /// 失败被吞掉之后收尾照走，结果马上就出来了——从外面看就是「动画并没有触发」。
    /// 所以这里把调度器上排着的活全干完，再断言结果<b>还没</b>出来：
    /// 时钟一点没走，谁也交不出结果。
    /// <para/>
    /// 时长特意取到上限（20 秒）：无头平台的时钟即使被推得很快，
    /// 也不可能在几十次空转里跑完 20 秒，这条断言因此是稳的。
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(RevealAnimationStyle.Scroll)]
    [InlineData(RevealAnimationStyle.Csgo)]
    [InlineData(RevealAnimationStyle.Slot)]
    [InlineData(RevealAnimationStyle.Wheel)]
    public async Task Play_DoesNotDeliverTheResultBeforeTheAnimationRuns(RevealAnimationStyle style)
    {
        // 把这一段拉长，让「还没播完」这件事在测试里足够稳定。
        var settings = new PickerSettings
        {
            AnimationStyle = style,
            ScrollDurationSeconds = 20,
            CsgoDurationSeconds = 20,
            SlotStepSeconds = 10,
            WheelDurationSeconds = 20
        };

        var plan = RevealAnimationPlanner.Build(style, Names, "张三", settings);
        Assert.NotNull(plan);

        var completed = false;
        RevealWindow.Play(plan, settings.RevealFontSize, Colors.Cyan, Hold, () => completed = true);

        // 把调度器上排着的活干完：动画的起播续体就在这条队列上。
        // 起播如果失败（比如当年的 cue 越界），收尾会立刻跟着跑完——那样下面这条断言就会红。
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        Assert.False(completed, $"{style} 的动画还没播就把结果交出来了，说明动画起播就失败了");

        RevealWindow.CloseCurrent();
    }

    #endregion
}
