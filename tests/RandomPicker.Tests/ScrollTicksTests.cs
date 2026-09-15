using System;
using System.Linq;
using ClassIsland.RandomPicker.Services;

namespace ClassIsland.RandomPicker.Tests;

/// <summary>
/// 滚动名字样式的换帧时刻表测试。
/// </summary>
/// <remarks>
/// 这段的核心不变量只有两条：各帧时长加起来<b>正好</b>等于配置的总时长，以及
/// 时长逐帧变长（也就是线性减速）。两条塌了之一，用户看到的就是「动画比设置里写的长了一截」
/// 或者「说好的减速变成了匀速」。
/// </remarks>
public sealed class ScrollTicksTests
{
    /// <summary>固定选择器：永远挑第一个候选人，让结果可以断言。</summary>
    private static Func<int, int> AlwaysFirst => _ => 0;

    #region 换帧间隔

    [Fact]
    public void BuildIntervals_SumsToExactlyTheTotalDuration()
    {
        // 这是最关键的不变量：配置写了几秒，实际就得播几秒。
        // 逐帧乘减速系数的老做法会累积误差，这里靠反解末项把它消掉。
        foreach (var total in new[] { 0.5, 1.0, 2.5, 4.0, 7.3, 12.0, 20.0 })
        {
            var intervals = ScrollTicks.BuildIntervals(total);
            Assert.Equal(total, intervals.Sum(), 6);
        }
    }

    [Fact]
    public void BuildIntervals_IsStrictlyIncreasing()
    {
        // 线性减速：后一帧一定比前一帧停留得久。
        var intervals = ScrollTicks.BuildIntervals(4.0);

        for (var i = 1; i < intervals.Length; i++)
        {
            Assert.True(intervals[i] > intervals[i - 1],
                $"第 {i} 帧（{intervals[i]:F4}s）不比第 {i - 1} 帧（{intervals[i - 1]:F4}s）长，就不是减速了");
        }
    }

    [Fact]
    public void BuildIntervals_FirstIsMuchShorterThanLast()
    {
        // 首尾差距要拉开，「飞快滚过 → 慢慢停下」的感觉才出得来。
        var intervals = ScrollTicks.BuildIntervals(4.0);

        Assert.True(intervals[^1] > intervals[0] * 4,
            $"首帧 {intervals[0]:F4}s、末帧 {intervals[^1]:F4}s，差距太小");
    }

    [Fact]
    public void BuildIntervals_HasAtLeastTwoTicks()
    {
        // 至少两帧才谈得上「滚动」。
        Assert.True(ScrollTicks.BuildIntervals(4.0).Length >= 2);
    }

    [Fact]
    public void BuildIntervals_WithHugeDuration_IsCapped()
    {
        // 喵~防御：总时长被改成极大值时不能算出几千帧，否则每一帧都在重绘上白烧 CPU。
        var intervals = ScrollTicks.BuildIntervals(600.0);

        Assert.True(intervals.Length <= ScrollTicks.MaxTicks,
            $"帧数 {intervals.Length} 超过了上限 {ScrollTicks.MaxTicks}");
    }

    [Fact]
    public void BuildIntervals_WithZeroDuration_ReturnsSingleTick()
    {
        // 时长为 0：退化成只有一帧，效果等同于瞬间定格。
        Assert.Single(ScrollTicks.BuildIntervals(0));
    }

    [Fact]
    public void BuildIntervals_WithNegativeDuration_ReturnsSingleTick()
    {
        // 负数时长同理，不能算出负数的帧数来。
        Assert.Single(ScrollTicks.BuildIntervals(-5));
    }

    [Fact]
    public void BuildIntervals_WithNaN_ReturnsSingleTick()
    {
        // 喵~防御：配置被改成 NaN 时不能让它传播下去。
        Assert.Single(ScrollTicks.BuildIntervals(double.NaN));
    }

    [Fact]
    public void BuildIntervals_WithInfinity_ReturnsSingleTick()
    {
        // 无穷大同理。
        Assert.Single(ScrollTicks.BuildIntervals(double.PositiveInfinity));
    }

    [Fact]
    public void BuildIntervals_WithTinyDuration_StillSumsToTotal()
    {
        // 时长特别短时等差数列不成立（反解出的末项比首项还短），
        // 这时退化成等间隔，但总和仍然必须精确等于配置值。
        var intervals = ScrollTicks.BuildIntervals(0.05);

        Assert.Equal(0.05, intervals.Sum(), 6);
    }

    [Fact]
    public void BuildIntervals_WithBadCustomIntervals_UsesDefaults()
    {
        // 喵~防御：首尾间隔被传成 0 或负数时不能算出乱序的序列，用默认值顶上。
        var intervals = ScrollTicks.BuildIntervals(4.0, firstSeconds: 0, lastSeconds: -1);

        Assert.Equal(4.0, intervals.Sum(), 6);
        Assert.True(intervals[^1] > intervals[0]);
    }

    #endregion

    #region 排片表

    [Fact]
    public void BuildPlan_LastFrameIsAlwaysTheWinner()
    {
        // 动画必须定格在中选者身上，这是「结果早就抽好了、动画只是演」这条设计的落点。
        var plan = ScrollTicks.BuildPlan(["张三", "李四", "王五"], "李四", 4.0, AlwaysFirst);

        Assert.Equal("李四", plan.Frames[^1]);
        Assert.Equal("李四", plan.Winner);
    }

    [Fact]
    public void BuildPlan_FrameCountMatchesIntervalCount()
    {
        // 每一帧都得有对应的停留时长，不然播放时会错位。
        var plan = ScrollTicks.BuildPlan(["张三", "李四"], "张三", 4.0, AlwaysFirst);

        Assert.Equal(plan.Intervals.Count, plan.Frames.Count);
    }

    [Fact]
    public void BuildPlan_TotalMatchesTheSumOfIntervals()
    {
        // 计划里写的总时长必须和实际要播的一致。
        var plan = ScrollTicks.BuildPlan(["张三", "李四"], "张三", 4.0, AlwaysFirst);

        Assert.Equal(plan.Intervals.Sum(), plan.Total.TotalSeconds, 6);
    }

    [Fact]
    public void BuildPlan_EarlyFramesNeverShowTheWinner()
    {
        // 前面几帧滚的都是「别人」，不然中选者会提前露面，悬念就没了。
        var plan = ScrollTicks.BuildPlan(["张三", "李四", "王五"], "李四", 4.0, AlwaysFirst);

        for (var i = 0; i < plan.Frames.Count - 1; i++)
        {
            Assert.NotEqual("李四", plan.Frames[i]);
        }
    }

    [Fact]
    public void BuildPlan_WithSingleName_EveryFrameShowsThatName()
    {
        // 名单里只有中选者一个人：没别人可滚，每帧都显示他。
        var plan = ScrollTicks.BuildPlan(["张三"], "张三", 4.0, AlwaysFirst);

        Assert.All(plan.Frames, frame => Assert.Equal("张三", frame));
    }

    [Fact]
    public void BuildPlan_WithNullRoster_EveryFrameShowsWinner()
    {
        // 喵~防御：名单为 null 时按空名单处理，而不是崩掉。
        var plan = ScrollTicks.BuildPlan(null, "张三", 4.0, AlwaysFirst);

        Assert.All(plan.Frames, frame => Assert.Equal("张三", frame));
    }

    [Fact]
    public void BuildPlan_WithOutOfRangeSelector_DoesNotThrow()
    {
        // 喵~防御：注入的选择器给出越界下标时，内部会夹回合法范围。
        var plan = ScrollTicks.BuildPlan(["张三", "李四", "王五"], "张三", 4.0, _ => 999);

        Assert.Equal("张三", plan.Frames[^1]);
    }

    [Fact]
    public void BuildPlan_WithZeroDuration_ProducesSingleFrame()
    {
        // 时长为 0 时只有一帧，而且就是中选者——等同于直接出结果。
        var plan = ScrollTicks.BuildPlan(["张三", "李四"], "李四", 0, AlwaysFirst);

        Assert.Single(plan.Frames);
        Assert.Equal("李四", plan.Frames[0]);
    }

    #endregion
}
