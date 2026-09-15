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
/// <para/>
/// 「越来越慢」实现成<b>等比增长的间隔</b>：相邻两次换名字的间隔按固定倍数放大
/// （<see cref="GrowthRatio"/>，默认 1.12）。
/// <list type="bullet">
/// <item>开头几十毫秒就换一帧，看着就是一片残影；</item>
/// <item>中段变成几十上百毫秒，名字开始看得清；</item>
/// <item>最后两帧之间有近半秒，稳稳停住。</item>
/// </list>
/// <para/>
/// 为什么不用更容易想到的两条路：
/// <list type="bullet">
/// <item><b>等差数列</b>（间隔线性增长）：快慢变化太平缓，开头不够快、结尾不够慢，
///       缺少「刹车」的层次。</item>
/// <item><b>幂律时刻表</b>（第 i 帧在 总时长×(i/N)^k 那一刻出现）：看似更潮，
///       但 k=2 时相邻间隔之差是常数，<b>又退化成等差数列</b>；要真非线性就得取 k≥3，
///       而那会让开头几十帧全挤进几毫秒里，等于白演。</item>
/// </list>
/// 等比数列恰好避开了这两个坑。
/// <para/>
/// 时刻表离线算好（而不是每帧现乘一个系数），是为了让总时长精确可控：
/// 逐帧乘系数会带累积误差，配上「停留 X 秒」之后总时间就对不上了。
/// </remarks>
internal static class ScrollTicks
{
    /// <summary>相邻两帧间隔的增长倍数。大了转得太狠、小了又不够「刹车」。</summary>
    public const double GrowthRatio = 1.12;

    /// <summary>希望第一帧停留多久，单位：秒。用来反推帧数——够短才是残影而不是逐字念。</summary>
    public const double TargetFirstIntervalSeconds = 0.02;

    /// <summary>至少两帧，不然「滚动」无从谈起。</summary>
    public const int MinTicks = 2;

    /// <summary>帧数上限。总时长被拉到 20 秒时也不会算出上千帧。</summary>
    public const int MaxTicks = 240;

    /// <summary>
    /// 算出每一帧停留多久，单位：秒。
    /// </summary>
    /// <param name="totalSeconds">动画总时长，单位：秒。</param>
    /// <returns>每一帧的时长，长度就是帧数。各项之和等于总时长。</returns>
    /// <remarks>
    /// 间隔一定<b>严格递增</b>，这就是「先快后慢」在数字上的定义；
    /// 各项之和也一定<b>恰好等于</b>总时长，不然动画会比配置的长或短一截。
    /// 这两条都有单测钉着。
    /// </remarks>
    public static double[] BuildIntervals(double totalSeconds)
    {
        // 喵~防御：总时长不是有限正数（配置被改成 0、负数、NaN 或 Infinity）时，
        // 反推出来的帧数会是 0 或负数，后面取间隔就会越界。这里退化成「只有一帧」，
        // 效果等同于瞬间定格在中选者上，而不是崩溃或卡住。
        if (!double.IsFinite(totalSeconds) || totalSeconds <= 0)
        {
            return [0];
        }

        // 帧数：让首项大致等于目标值。
        // 等比数列首项 = 总和 × (q-1) / (q^N - 1)，把它当成公式解出 N。
        var tickCount = (int)Math.Round(
            Math.Log(1 + totalSeconds * (GrowthRatio - 1) / TargetFirstIntervalSeconds) / Math.Log(GrowthRatio));
        tickCount = Math.Clamp(tickCount, MinTicks, MaxTicks);

        // 再用实际的帧数把首项精确解出来：这样各项之和<b>恰好</b>等于总时长，
        // 不会有四舍五入累计出来的「动画比配置长半秒」。
        var first = totalSeconds * (GrowthRatio - 1) / (Math.Pow(GrowthRatio, tickCount) - 1);

        // 逐帧填：每一项都是上一项乘公比。
        var intervals = new double[tickCount];
        var current = first;
        for (var i = 0; i < tickCount; i++)
        {
            intervals[i] = current;
            // 下一项按固定倍数放大。
            current *= GrowthRatio;
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
        // 总时长取各帧时长之和：幂律时刻表的末项就是总时长，这里再求一次和是为了让
        // 「计划里写的时长」和「实际播的时长」永远一致。
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
