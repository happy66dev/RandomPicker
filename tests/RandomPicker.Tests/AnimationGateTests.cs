using System;
using ClassIsland.RandomPicker.Models;
using ClassIsland.RandomPicker.Services;

namespace ClassIsland.RandomPicker.Tests;

/// <summary>
/// 动画开关与时长解析的测试。
/// </summary>
/// <remarks>
/// 这里最重要的不是「功能对不对」，而是<b>玩家关掉动画时插件必须安静</b>，
/// 以及<b>默认安装下动画必须真的会播</b>——后者特别容易被写错，见下面那条测试的说明。
/// </remarks>
public sealed class AnimationGateTests
{
    /// <summary>一个默认设置，各测试按需改写其中一两项。</summary>
    private static PickerSettings DefaultSettings => new();

    #region 降级判定

    [Fact]
    public void Resolve_WhenUserChoseNone_StaysNone()
    {
        // 用户自己选了「无动画」，宿主的开关开着也一样不播。
        Assert.Equal(RevealAnimationStyle.None,
            AnimationGate.Resolve(RevealAnimationStyle.None, animationLevel: 2, transientDisabled: false));
    }

    [Fact]
    public void Resolve_WhenAnimationLevelIsOne_KeepsRequestedStyle()
    {
        // ★ 防回归的关键一条：宿主的默认动画等级就是 1。
        // 如果谁把判据写成「等级 >= 2」，默认安装下所有动画都会静默消失，
        // 而开发机上把等级调到 2 时又完全看不出问题——这条测试就是为了拦住这种改动。
        Assert.Equal(RevealAnimationStyle.Wheel,
            AnimationGate.Resolve(RevealAnimationStyle.Wheel, animationLevel: 1, transientDisabled: false));
    }

    [Fact]
    public void Resolve_WhenAnimationLevelIsTwo_KeepsRequestedStyle()
    {
        // 等级 2（全部动画）当然要播。
        Assert.Equal(RevealAnimationStyle.Csgo,
            AnimationGate.Resolve(RevealAnimationStyle.Csgo, animationLevel: 2, transientDisabled: false));
    }

    [Fact]
    public void Resolve_WhenAnimationLevelIsZero_ReturnsNone()
    {
        // 用户在 ClassIsland 里关掉了动画，插件必须跟着安静。
        Assert.Equal(RevealAnimationStyle.None,
            AnimationGate.Resolve(RevealAnimationStyle.Scroll, animationLevel: 0, transientDisabled: false));
    }

    [Fact]
    public void Resolve_WhenTransientDisabled_ReturnsNone()
    {
        // 宿主临时禁用动画期间（比如正在做别的重要动画）同样要安静。
        Assert.Equal(RevealAnimationStyle.None,
            AnimationGate.Resolve(RevealAnimationStyle.Slot, animationLevel: 2, transientDisabled: true));
    }

    [Fact]
    public void Resolve_WithNegativeAnimationLevel_ReturnsNone()
    {
        // 喵~防御：等级被手改成负数，按「关了动画」处理。
        Assert.Equal(RevealAnimationStyle.None,
            AnimationGate.Resolve(RevealAnimationStyle.Scroll, animationLevel: -1, transientDisabled: false));
    }

    #endregion

    #region 时长解析

    [Fact]
    public void DurationOf_Scroll_UsesConfiguredSeconds()
    {
        var settings = new PickerSettings { ScrollDurationSeconds = 7.5 };

        Assert.Equal(TimeSpan.FromSeconds(7.5), AnimationGate.DurationOf(RevealAnimationStyle.Scroll, settings));
    }

    [Fact]
    public void DurationOf_Scroll_ClampsOutOfRangeValues()
    {
        // 配置被改成荒谬的值时夹回区间，免得动画只有 0 秒或者拖到几分钟。
        Assert.Equal(TimeSpan.FromSeconds(0.5),
            AnimationGate.DurationOf(RevealAnimationStyle.Scroll, new PickerSettings { ScrollDurationSeconds = -3 }));
        Assert.Equal(TimeSpan.FromSeconds(20),
            AnimationGate.DurationOf(RevealAnimationStyle.Scroll, new PickerSettings { ScrollDurationSeconds = 999 }));
    }

    [Fact]
    public void DurationOf_WithNaNSetting_FallsBackToMinimum()
    {
        // 喵~防御：NaN 传进 Math.Clamp 会被原样放出来，时长就成了 NaN，
        // 动画永远结束不了。这里必须退回到最短时长。
        var duration = AnimationGate.DurationOf(RevealAnimationStyle.Csgo,
            new PickerSettings { CsgoDurationSeconds = double.NaN });

        Assert.Equal(TimeSpan.FromSeconds(0.5), duration);
    }

    [Fact]
    public void DurationOf_Slot_ScalesWithSlotCount()
    {
        // 老虎机的时间配置是「每格多少秒」，总时长要乘格数。
        var settings = new PickerSettings { SlotStepSeconds = 1.5 };

        Assert.Equal(TimeSpan.FromSeconds(6), AnimationGate.DurationOf(RevealAnimationStyle.Slot, settings, slotCount: 4));
        Assert.Equal(TimeSpan.FromSeconds(3), AnimationGate.DurationOf(RevealAnimationStyle.Slot, settings, slotCount: 2));
    }

    [Fact]
    public void DurationOf_Slot_WithoutSlotCount_AssumesMinimumSlots()
    {
        // 调用方还没算出格数时，按最少格数给一个像样的时长，而不是 0。
        var settings = new PickerSettings { SlotStepSeconds = 1.5 };

        Assert.Equal(TimeSpan.FromSeconds(3), AnimationGate.DurationOf(RevealAnimationStyle.Slot, settings));
    }

    [Fact]
    public void DurationOf_Slot_ClampsOutOfRangeSlotCount()
    {
        // 格数被传成 0 或 99 时夹回 2~4。
        var settings = new PickerSettings { SlotStepSeconds = 1.0 };

        Assert.Equal(TimeSpan.FromSeconds(2), AnimationGate.DurationOf(RevealAnimationStyle.Slot, settings, slotCount: -5));
        Assert.Equal(TimeSpan.FromSeconds(4), AnimationGate.DurationOf(RevealAnimationStyle.Slot, settings, slotCount: 99));
    }

    [Fact]
    public void DurationOf_Wheel_AddsTheFixedSlideSegment()
    {
        // 转盘的配置只管「转到边界」，滑进扇区那一下是固定的，要额外加上。
        var settings = new PickerSettings { WheelDurationSeconds = 6 };

        Assert.Equal(TimeSpan.FromSeconds(6) + WheelLayout.SlideDuration,
            AnimationGate.DurationOf(RevealAnimationStyle.Wheel, settings));
    }

    [Fact]
    public void DurationOf_None_IsZero()
    {
        // 无动画不花时间。
        Assert.Equal(TimeSpan.Zero, AnimationGate.DurationOf(RevealAnimationStyle.None, DefaultSettings));
    }

    [Fact]
    public void DurationOf_WithNullSettings_Throws()
    {
        // 喵~防御：设置对象不允许为 null，早报错好定位。
        Assert.Throws<ArgumentNullException>(() =>
            AnimationGate.DurationOf(RevealAnimationStyle.Scroll, null!));
    }

    #endregion

    #region 排片表工厂

    [Fact]
    public void Planner_BuildsScrollPlan()
    {
        var plan = RevealAnimationPlanner.Build(RevealAnimationStyle.Scroll,
            ["张三", "李四", "王五"], "李四", new PickerSettings());

        var scroll = Assert.IsType<ScrollPlan>(plan);
        // 最后一帧必须是中选者——动画定格在他身上。
        Assert.Equal("李四", scroll.Frames[^1]);
        Assert.Equal("李四", scroll.Winner);
    }

    [Fact]
    public void Planner_BuildsCsgoPlan()
    {
        var plan = RevealAnimationPlanner.Build(RevealAnimationStyle.Csgo,
            ["张三", "李四", "王五"], "王五", new PickerSettings());

        var csgo = Assert.IsType<CsgoPlan>(plan);
        // 指针底下那一块必须就是中选者。
        Assert.Equal("王五", csgo.Track[csgo.WinnerTrackIndex]);
    }

    [Fact]
    public void Planner_BuildsSlotPlan()
    {
        // 三个字的名字 → 3 个格子。
        var plan = RevealAnimationPlanner.Build(RevealAnimationStyle.Slot,
            ["张三", "李小四"], "李小四", new PickerSettings());

        var slot = Assert.IsType<SlotPlan>(plan);
        Assert.Equal(3, slot.SlotCount);
        Assert.Equal("李小四", slot.Winner);
    }

    [Fact]
    public void Planner_Slot_WithTooLongName_FallsBackToScroll()
    {
        // 名单里有超过 4 个字的名字时老虎机显示不完整，自动回退到滚动名字。
        var plan = RevealAnimationPlanner.Build(RevealAnimationStyle.Slot,
            ["张三", "司马相如字长卿"], "张三", new PickerSettings());

        Assert.IsType<ScrollPlan>(plan);
    }

    [Fact]
    public void Planner_BuildsWheelPlan()
    {
        var plan = RevealAnimationPlanner.Build(RevealAnimationStyle.Wheel,
            ["张三", "李四", "王五"], "张三", new PickerSettings());

        var wheel = Assert.IsType<WheelPlan>(plan);
        // 最终角度必须指向中选者那一格。
        Assert.Equal(wheel.WinnerSector, WheelLayout.SectorUnderPointer(wheel.FinalAngle, wheel.Sectors.Count));
    }

    [Fact]
    public void Planner_WithNone_ReturnsNull()
    {
        // 「无动画」不生成计划，上层据此直接出结果。
        Assert.Null(RevealAnimationPlanner.Build(RevealAnimationStyle.None,
            ["张三", "李四"], "张三", new PickerSettings()));
    }

    [Fact]
    public void Planner_WithEmptyRoster_ReturnsNull()
    {
        Assert.Null(RevealAnimationPlanner.Build(RevealAnimationStyle.Scroll,
            [], "张三", new PickerSettings()));
    }

    [Fact]
    public void Planner_WithNullWinner_ReturnsNull()
    {
        Assert.Null(RevealAnimationPlanner.Build(RevealAnimationStyle.Scroll,
            ["张三"], null, new PickerSettings()));
    }

    [Fact]
    public void Planner_WithNullSettings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RevealAnimationPlanner.Build(RevealAnimationStyle.Scroll, ["张三"], "张三", null!));
    }

    #endregion
}
