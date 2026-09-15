using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassIsland.RandomPicker.Services;

/// <summary>
/// 「老虎机」样式：格数与逐格候选字集合。
/// </summary>
/// <remarks>
/// 玩法是屏幕上若干格子，一格一格地抽字：每格飞快轮播候选字，最后停在中选者的那个字上。
/// <para/>
/// 格数由<b>当前名单里最长名字的字数</b>决定（夹在 2~4 格之间）：
/// 名字都只有两个字就画 2 格，有几个四字名就画 4 格。
/// <para/>
/// 中选者比格数短时（比如四字名的名单里抽到「张三」），末尾几格<b>留空</b>——
/// 那几格直接画空框、不参与旋转，绝不能拿别人的字去填，否则板面和中选的人对不上。
/// <para/>
/// 名单里出现超过 4 个字的名字时这个样式用不了（超出的字显示不出来，
/// 会演成一个残缺的名字），此时 <see cref="IsUsable"/> 返回 <c>false</c>，
/// 由上层回退到别的样式。
/// </remarks>
internal static class SlotMachine
{
    /// <summary>最少格数。名字全是单字时也画两格，不然「抽」的感觉就没了。</summary>
    public const int MinSlots = 2;

    /// <summary>最多格数。超出的字显示不出来，所以这个样式对四字以上的名字不可用。</summary>
    public const int MaxSlots = 4;

    /// <summary>
    /// 最后一格停住之后，板面再静止多久，单位：秒。
    /// </summary>
    /// <remarks>
    /// <b>这段时间是必需的，不是装饰。</b>没有它的话最后一格恰好在「总时长」那一刻才停住，
    /// 动画当场结束、立刻切去出结果，拼好的名字一帧都留不下——
    /// 2026-09-15 主人报的「老虎机最后的抽取有问题，应该最后一个字展示一会会再继续」就是这个。
    /// <para/>
    /// 它算在动画时长里面（<see cref="Build"/> 会把它加进 <c>Total</c>），
    /// 所以「停留 X 秒」依旧只管动画结束之后，不会被它影响。
    /// </remarks>
    public const double SettleSeconds = 0.8;

    /// <summary>名单里最长名字有几个字。名单为空时返回 0。</summary>
    public static int LongestNameLength(IReadOnlyList<string>? names)
    {
        // 喵~防御：名单为空（或为 null）时没有长度可言，返回 0。
        if (names is null || names.Count == 0)
        {
            return 0;
        }

        var longest = 0;
        foreach (var name in names)
        {
            // 喵~防御：名单理论上不会有 null 项，但手工改过的文件可能出现，跳过它就行。
            if (name is not null && name.Length > longest)
            {
                longest = name.Length;
            }
        }

        return longest;
    }

    /// <summary>
    /// 这份名单能不能用老虎机样式。
    /// </summary>
    /// <returns>名单非空、且没有超过 4 个字的名字时为 <c>true</c>。</returns>
    public static bool IsUsable(IReadOnlyList<string>? names) =>
        // 名单为空本来就抽不出人；再排除掉带超长名字的名单。
        names is { Count: > 0 } && LongestNameLength(names) <= MaxSlots;

    /// <summary>
    /// 生成老虎机的排片表。
    /// </summary>
    /// <param name="names">当前名单。</param>
    /// <param name="winner">中选者。</param>
    /// <param name="step">每一格转多久。</param>
    /// <returns>排片表；名单或中选者不合法时返回 <c>null</c>（上层据此不播动画）。</returns>
    /// <remarks>
    /// 排片表里的 <c>Total</c> = 每格时长 × 格数 <b>再加上</b> <see cref="SettleSeconds"/> 那段定格。
    /// 也就是说最后一格在 <c>Total - SettleSeconds</c> 那一刻就停住了，
    /// 剩下的时间是让主人看清拼出来的名字的。
    /// </remarks>
    public static SlotPlan? Build(IReadOnlyList<string>? names, string? winner, TimeSpan step)
    {
        // 喵~防御：名单为空或没有中选者时没什么可演的。
        if (names is null || names.Count == 0 || string.IsNullOrEmpty(winner))
        {
            return null;
        }

        // 喵~防御：中选者不在名单里（动画开播前名单被改过）→ 作废。
        if (!names.Any(name => string.Equals(name, winner, StringComparison.Ordinal)))
        {
            return null;
        }

        // 格数 = 最长名字的字数，夹在 2~4 之间。
        var slotCount = Math.Clamp(LongestNameLength(names), MinSlots, MaxSlots);

        // 喵~防御：单格时长不是正数时，总时长会变成 0，动画一闪而过什么也看不见。
        // 兜底成 1 秒，至少让用户看清板面。
        var safeStep = step > TimeSpan.Zero ? step : TimeSpan.FromSeconds(1);

        var candidates = new string[slotCount];
        var winnerChars = new char?[slotCount];
        // 已经确定下来的前缀，第一个字定完就变成「张」，第二个字定完就变成「张小」。
        var prefix = string.Empty;

        for (var slot = 0; slot < slotCount; slot++)
        {
            // 这一格的候选字集合：前面几个字已经和已定前缀对上的那些名字，它们的第 slot 个字。
            // 比如名单是「张三、张四、李五」时，第一格是「张、李」；
            // 若第一格定下「张」，第二格就只剩「三、四」——候选范围逐格收窄，这正是老虎机的味道。
            var candidateChars = names
                .Where(name => name is not null
                               && name.Length > slot
                               && name.StartsWith(prefix, StringComparison.Ordinal))
                .Select(name => name[slot])
                .Distinct()
                .OrderBy(character => character)
                .ToArray();
            // 排过序，测试断言才是稳定的（不然每次跑出来的顺序都不一样）。
            candidates[slot] = new string(candidateChars);

            if (winner.Length > slot)
            {
                // 中选者在这一格有字：记下来，并把它并进前缀，让下一格的候选范围继续收窄。
                winnerChars[slot] = winner[slot];
                prefix += winner[slot];
            }
            else
            {
                // 中选者比格数短：这一格留空。
                // 注意此时 candidates[slot] 可能仍有值（名单里别人在这一格有字），
                // 但那一格不该参与旋转——用了它就会显示一个不属于中选者的字。
                winnerChars[slot] = null;
            }
        }

        return new SlotPlan(slotCount, candidates, winnerChars,
            safeStep, safeStep * slotCount + TimeSpan.FromSeconds(SettleSeconds), winner);
    }
}
