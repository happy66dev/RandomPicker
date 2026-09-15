using System;
using System.Collections.Generic;

namespace ClassIsland.RandomPicker.Services;

/// <summary>
/// 一段抽选动画的「排片表」：要演什么、演多久。
/// </summary>
/// <remarks>
/// 每种样式各有一个子类。上层只认 <see cref="Total"/>（用来安排忙碌态和动画结束后的展示），
/// 具体怎么画由对应的动画控件自己解释。
/// <para/>
/// 这些计划都是纯数据、不含任何界面对象，所以能在单元测试里直接断言。
/// 它们也<b>不参与抽选</b>——中选者在计划生成之前就已经定好了，动画只负责把它演出来。
/// </remarks>
/// <param name="Total">这段动画总共要播多久。</param>
/// <param name="Winner">抽中的那个人。动画的最后一帧必须落在他身上。</param>
internal abstract record RevealAnimationPlan(TimeSpan Total, string Winner);

/// <summary>
/// 「滚动名字」的排片表。
/// </summary>
/// <param name="Frames">依次显示的名字，最后一个必定是中选者。</param>
/// <param name="Intervals">每一帧停留多久，单位：秒，线性增长（线性减速）。</param>
/// <param name="Total">总时长。</param>
/// <param name="Winner">中选者。</param>
internal sealed record ScrollPlan(
    IReadOnlyList<string> Frames,
    IReadOnlyList<double> Intervals,
    TimeSpan Total,
    string Winner) : RevealAnimationPlan(Total, Winner);

/// <summary>
/// 「CSGO 开箱」的排片表。
/// </summary>
/// <param name="Track">滚动条上的名字序列（名单重复展开若干轮）。</param>
/// <param name="WinnerTrackIndex">中选者在 <paramref name="Track"/> 里的下标。</param>
/// <param name="SlotWidth">一个方块的宽度，单位：逻辑像素。</param>
/// <param name="SlotHeight">一个方块的高度，单位：逻辑像素。</param>
/// <param name="Gap">方块之间的间距，单位：逻辑像素。</param>
/// <param name="ViewportWidth">可视区宽度，单位：逻辑像素。</param>
/// <param name="ViewportHeight">可视区高度，单位：逻辑像素。</param>
/// <param name="TargetOffset">滚到终点时的平移量，单位：逻辑像素。</param>
/// <param name="Rarities">每个方块对应的品质，下标和 <paramref name="Track"/> 一一对应。</param>
/// <param name="WinnerRarity">中选者的品质。它<b>只影响表演</b>，不影响抽到谁。</param>
/// <param name="Total">总时长。</param>
/// <param name="Winner">中选者。</param>
internal sealed record CsgoPlan(
    IReadOnlyList<string> Track,
    int WinnerTrackIndex,
    double SlotWidth,
    double SlotHeight,
    double Gap,
    double ViewportWidth,
    double ViewportHeight,
    double TargetOffset,
    IReadOnlyList<ItemRarity> Rarities,
    ItemRarity WinnerRarity,
    TimeSpan Total,
    string Winner) : RevealAnimationPlan(Total, Winner);

/// <summary>
/// 「老虎机」的排片表。
/// </summary>
/// <param name="SlotCount">格子数，等于当前名单里最长名字的字数（夹在 2~4 之间）。</param>
/// <param name="Candidates">每一格的候选字集合，下标就是格号。</param>
/// <param name="WinnerChars">中选者每一格的字；<c>null</c> 表示这一格留空（名字比格数短）。</param>
/// <param name="Step">每一格转多久，单位：秒。</param>
/// <param name="Total">总时长，等于 <paramref name="Step"/> × <paramref name="SlotCount"/>。</param>
/// <param name="Winner">中选者。</param>
internal sealed record SlotPlan(
    int SlotCount,
    IReadOnlyList<string> Candidates,
    IReadOnlyList<char?> WinnerChars,
    TimeSpan Step,
    TimeSpan Total,
    string Winner) : RevealAnimationPlan(Total, Winner);

/// <summary>
/// 「拼多多转盘」的排片表。
/// </summary>
/// <param name="Sectors">扇区上的名字，顺时针排列。</param>
/// <param name="WinnerSector">中选者在 <paramref name="Sectors"/> 里的下标。</param>
/// <param name="BoundaryIndex">指针第一阶段停在哪条边界上。</param>
/// <param name="SlideDirection">滑入方向：<c>-1</c> 向后、<c>+1</c> 向前。</param>
/// <param name="BoundaryAngle">第一阶段终点角度，单位：度。</param>
/// <param name="FinalAngle">第二阶段终点角度，单位：度。</param>
/// <param name="HoldDuration">转到边界用多久。</param>
/// <param name="SlideDuration">滑进扇区用多久。</param>
/// <param name="Total">总时长。</param>
/// <param name="Winner">中选者。</param>
internal sealed record WheelPlan(
    IReadOnlyList<string> Sectors,
    int WinnerSector,
    int BoundaryIndex,
    int SlideDirection,
    double BoundaryAngle,
    double FinalAngle,
    TimeSpan HoldDuration,
    TimeSpan SlideDuration,
    TimeSpan Total,
    string Winner) : RevealAnimationPlan(Total, Winner);
