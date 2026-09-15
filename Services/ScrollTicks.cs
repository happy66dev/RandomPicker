using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace ClassIsland.RandomPicker.Services;

/// <summary>
/// 「滚动名字」样式：换帧时刻表，以及排片表的生成。
/// </summary>
/// <remarks>
/// 玩法是中央大字飞速换名字，换着换着越来越慢，最后停在中选者上。
/// 「越来越慢」实现成<b>线性减速</b>：相邻两次换名字的间隔排成一个等差数列，
/// 首项短、末项长，全部加起来正好等于配置的总时长。
/// <para/>
/// 之所以离线算好整张表（而不是每一帧现乘一个减速系数），是为了让总时长精确可控：
/// 逐帧乘系数会带累积误差，配上「停留 X 秒」之后总时间就对不上了。
/// </remarks>
internal static class ScrollTicks
{
    /// <summary>首帧间隔，单位：秒。够快才有「飞起来」的感觉。</summary>
    public const double FirstIntervalSeconds = 0.035;

    /// <summary>末帧间隔，单位：秒。够慢才看得清最后几个名字。</summary>
    public const double LastIntervalSeconds = 0.30;

    /// <summary>帧数上限。总时长被拉到 20 秒时也不会算出上千帧。</summary>
    public const int MaxTicks = 240;

    /// <summary>
    /// 算出每一帧停留多久，单位：秒。
    /// </summary>
    /// <param name="totalSeconds">动画总时长，单位：秒。</param>
    /// <param name="firstSeconds">第一帧停留多久。</param>
    /// <param name="lastSeconds">最后一帧停留多久。</param>
    /// <returns>每一帧的时长，长度就是帧数。各项之和等于总时长。</returns>
    public static double[] BuildIntervals(double totalSeconds,
        double firstSeconds = FirstIntervalSeconds, double lastSeconds = LastIntervalSeconds)
    {
        // 喵~防御：总时长不是有限正数（配置被改成 0、负数、NaN 或 Infinity）时，
        // 反解出来的帧数会是 0 或负数，后面取间隔就会越界。这里退化成「只有一帧」，
        // 效果等同于瞬间定格在中选者上，而不是崩溃或卡住。
        if (!double.IsFinite(totalSeconds) || totalSeconds <= 0)
        {
            return [0];
        }

        // 首帧间隔本身也可能是坏值，坏了就用默认值顶上。
        var first = double.IsFinite(firstSeconds) && firstSeconds > 0 ? firstSeconds : FirstIntervalSeconds;
        // 末帧间隔同理。
        var last = double.IsFinite(lastSeconds) && lastSeconds > 0 ? lastSeconds : LastIntervalSeconds;

        // 等差数列求和公式反解帧数：和 = 帧数 × (首项 + 末项) / 2。
        var tickCount = (int)Math.Round(2 * totalSeconds / (first + last));
        // 至少 2 帧（不然「滚动」无从谈起），至多 MaxTicks 帧。
        tickCount = Math.Clamp(tickCount, 2, MaxTicks);

        // 反解末间隔：要让这些间隔加起来<b>正好</b>等于总时长，末项得按实际帧数回算，
        // 否则四舍五入的误差会累积成「动画比配置长了半秒」。
        var solvedLast = 2 * totalSeconds / tickCount - first;
        // 喵~防御：总时长特别短时，反解出的末间隔会小于首间隔（甚至为负），
        // 那等差数列就不成立了。这时退化成等间隔——每帧一样长，总和仍然精确等于配置值。
        var useEven = solvedLast < first;
        var actualFirst = useEven ? totalSeconds / tickCount : first;
        var actualLast = useEven ? totalSeconds / tickCount : solvedLast;

        // 逐项填等差数列。
        var intervals = new double[tickCount];
        for (var i = 0; i < tickCount; i++)
        {
            // 第 i 项在首末之间线性插值：越靠后越长，这就是「线性减速」。
            intervals[i] = actualFirst + (actualLast - actualFirst) * i / (tickCount - 1);
        }

        return intervals;
    }

    /// <summary>
    /// 生成「滚动名字」的排片表。
    /// </summary>
    /// <param name="names">当前名单，用来随机抽帧里要显示的名字。</param>
    /// <param name="winner">中选者，最后一帧一定是他。</param>
    /// <param name="totalSeconds">动画总时长，单位：秒。</param>
    /// <param name="pickIndexSelector">
    /// 从 0 到「候选人数减一」里挑一个下标的函数。
    /// 传 <c>null</c> 用操作系统的熵源；测试传固定函数，结果才可断言。
    /// </param>
    public static ScrollPlan BuildPlan(IReadOnlyList<string>? names, string winner, double totalSeconds,
        Func<int, int>? pickIndexSelector = null)
    {
        // 先算好每一帧停留多久。
        var intervals = BuildIntervals(totalSeconds);
        // 再按帧数挑出每一帧要显示的名字。
        var frames = BuildFrames(names, winner, intervals.Length, pickIndexSelector);
        // 总时长取各帧时长之和：反解过的序列加起来正好等于配置值，
        // 这里再求一次和是为了让「计划里写的时长」和「实际播的时长」永远一致。
        return new ScrollPlan(frames, intervals, TimeSpan.FromSeconds(intervals.Sum()), winner);
    }

    /// <summary>挑出每一帧要显示的名字，最后一帧固定是中选者。</summary>
    private static string[] BuildFrames(IReadOnlyList<string>? names, string winner, int frameCount,
        Func<int, int>? pickIndexSelector)
    {
        var frames = new string[frameCount];

        // 除中选者之外的其他人，用来在前面几帧里乱跳。
        // 喵~防御：名单为 null 时按空处理，下面会走「全部帧都显示中选者」的分支。
        var others = names is null
            ? []
            : names.Where(name => !string.Equals(name, winner, StringComparison.Ordinal)).ToArray();

        // 前面每一帧都从「其他人」里随机挑一个。
        // 最后留一帧给中选者，所以循环到 frameCount - 1 为止。
        for (var i = 0; i < frameCount - 1; i++)
        {
            // 名单里只有中选者一个人（或者名单是空的）：没有别人可跳，每帧都显示他。
            if (others.Length == 0)
            {
                frames[i] = winner;
                continue;
            }

            // 挑一个下标：生产环境用操作系统熵源，测试注入固定函数。
            var index = pickIndexSelector?.Invoke(others.Length) ?? RandomNumberGenerator.GetInt32(others.Length);
            // 喵~防御：注入的选择器可能给出越界下标，夹回合法范围再用。
            index = Math.Clamp(index, 0, others.Length - 1);
            frames[i] = others[index];
        }

        // 最后一帧必定是中选者——动画定格在他身上。
        frames[frameCount - 1] = winner;
        return frames;
    }
}
