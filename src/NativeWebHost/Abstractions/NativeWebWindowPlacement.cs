namespace NativeWebHost;

/// <summary>
/// 定义原生窗口首次显示时相对于工作区的定位方式。
/// </summary>
public enum NativeWebWindowPlacement
{
    /// <summary>沿用操作系统的默认定位策略。</summary>
    Default,

    /// <summary>在主屏幕工作区内居中显示。</summary>
    CenterScreen,

    /// <summary>停靠到主屏幕工作区右下角。</summary>
    BottomRight
}
