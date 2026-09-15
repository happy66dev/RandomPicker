using System;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using ClassIsland.RandomPicker.Models;
using ClassIsland.RandomPicker.Services;
using ClassIsland.RandomPicker.Views;

namespace ClassIsland.RandomPicker.Tests;

/// <summary>
/// 中央大字窗口的测试。
/// </summary>
/// <remarks>
/// <b>这个文件就是为 2026-09-15 那次事故建的。</b>
/// 当时给结果层加的 <c>DoubleTransition</c> 漏写了 <c>Property</c>，
/// 而那个过渡是在窗口<b>构造函数</b>里建的——于是每一次出结果都会抛
/// 「Transition has no property specified.」，用户点第一下插件就被宿主禁用了。
/// <para/>
/// 那一行代码编译 0 错误、197 个纯逻辑测试全绿：问题只在「窗口真的被建出来」的那一刻现形。
/// 所以这里要靠无头平台把窗口真的建一次。
/// </remarks>
public class RevealWindowTests
{
    /// <summary>测试用名单。</summary>
    private static readonly string[] Names = ["张三", "李小四", "王五"];

    /// <summary>一份默认设置，用来取字号档位。</summary>
    private static readonly PickerSettings BaseSettings = new();

    /// <summary>结果停留时长，测试里取短一点。</summary>
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 建窗口、显示文字、再收掉，全程不该抛异常。
    /// </summary>
    /// <remarks>
    /// 这条就是那次崩溃的复现路径：<c>Show</c> → <c>Ensure</c> → 窗口构造函数。
    /// 构造函数里任何一处装配写错（过渡漏属性、图层类型不对、附加属性名写错）
    /// 都会在这里炸出来。
    /// </remarks>
    [AvaloniaFact]
    public void Show_DoesNotThrow()
    {
        // 出大字这条路：动画选「无」，走的就是它。
        RevealWindow.Show("张三", BaseSettings.RevealFontSize, Hold, Colors.Cyan);
        RevealWindow.CloseCurrent();
    }

    /// <summary>
    /// 带小字说明的显示路径也不该抛异常。
    /// </summary>
    [AvaloniaFact]
    public void Show_WithCaption_DoesNotThrow()
    {
        RevealWindow.Show("张三", "拍照失败：未检测到人脸，已改为按名单抽取",
            BaseSettings.RevealFontSize, Hold, Colors.Cyan);
        RevealWindow.CloseCurrent();
    }

    /// <summary>
    /// 带排片表播一段动画，不该抛异常。
    /// </summary>
    /// <remarks>
    /// 这条覆盖的是动画那条路：窗口要先装载动画控件（量出舞台尺寸），
    /// 把尺寸写进卡片的 <c>MinWidth</c>/<c>MinHeight</c>，再起动画。
    /// 任何一步抛异常都会被宿主的渲染循环吞掉或直接让插件崩掉，所以必须在这里拦住。
    /// </remarks>
    [AvaloniaFact]
    public void Play_WithRealPlan_DoesNotThrow()
    {
        // 造一份真实的排片表，别用手搓的假数据——要为「计划真的能被控件消化」负责。
        var plan = RevealAnimationPlanner.Build(RevealAnimationStyle.Scroll, Names, "张三", BaseSettings);
        Assert.NotNull(plan);

        // 播完的回调在这里必须能被调用，但无头环境下动画时钟不会自己走完，
        // 所以只关心「装载 + 起播」这一段不抛异常。
        var completed = false;
        RevealWindow.Play(plan, BaseSettings.RevealFontSize, Colors.Cyan, Hold, () => completed = true);

        // 起播是同步完成的（第一个 await 之前的部分），走到这里说明没炸。
        Assert.False(completed);

        RevealWindow.CloseCurrent();
    }

    /// <summary>
    /// 每换一次显示，窗口都能被复用而不出问题。
    /// </summary>
    /// <remarks>
    /// 窗口是单例复用的：先播动画、再直接出结果，中间会清掉上一次的舞台尺寸、
    /// 卸掉动画控件。这条换层路径里如果有残留状态，第二下就会出问题。
    /// </remarks>
    [AvaloniaFact]
    public void Show_AfterPlay_ReusesWindow()
    {
        // 先播一段动画。
        var plan = RevealAnimationPlanner.Build(RevealAnimationStyle.Scroll, Names, "张三", BaseSettings);
        Assert.NotNull(plan);
        RevealWindow.Play(plan, BaseSettings.RevealFontSize, Colors.Cyan, Hold, () => { });

        // 再直接出一次结果（模拟用户切回「无动画」）。
        RevealWindow.Show("李四", BaseSettings.RevealFontSize, Hold, Colors.Cyan);

        // 再播一次动画。
        RevealWindow.Play(plan, BaseSettings.RevealFontSize, Colors.Cyan, Hold, () => { });

        RevealWindow.CloseCurrent();
    }
}
