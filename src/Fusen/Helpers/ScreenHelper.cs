using System;
using System.Windows;

namespace Fusen.Helpers
{
    /// <summary>
    /// 付箋をディスプレイの表示領域内に収めるための計算。
    /// 判定はすべて「全モニタを含む仮想デスクトップ」で行う。主モニタの幅・高さだけで判定すると、
    /// 主モニタの右や下に配置した2画面目の付箋が「画面外」と誤判定されるため。
    /// </summary>
    public static class ScreenHelper
    {
        /// <summary>ヘッダーを掴んで動かせると判断する最小の横幅。</summary>
        private const double MinGrabbableWidth = 100;

        /// <summary>
        /// 仮想デスクトップの範囲。取得できない異常時は <paramref name="ok"/> が false になる。
        /// </summary>
        private static (double Left, double Top, double Right, double Bottom) GetVirtualScreen(out bool ok)
        {
            double left = SystemParameters.VirtualScreenLeft;
            double top = SystemParameters.VirtualScreenTop;
            double right = left + SystemParameters.VirtualScreenWidth;
            double bottom = top + SystemParameters.VirtualScreenHeight;

            ok = right > left && bottom > top;
            return (left, top, right, bottom);
        }

        /// <summary>
        /// 指定サイズの付箋が仮想デスクトップの内側に入るよう位置を押し戻す。
        /// </summary>
        public static (double X, double Y) ClampToVirtualScreen(double x, double y, double width, double height)
        {
            var (left, top, right, bottom) = GetVirtualScreen(out bool ok);

            // 範囲が取得できない異常時は、要求された位置をそのまま使う
            if (!ok) return (x, y);

            // はみ出す側から順に押し戻す。左上の判定を後に置くのは、
            // 付箋より狭い領域だった場合に左上を優先して見えるようにするため。
            if (x + width > right) x = right - width;
            if (y + height > bottom) y = bottom - height;
            if (x < left) x = left;
            if (y < top) y = top;

            return (x, y);
        }

        /// <summary>
        /// ヘッダー（掴んで動かせる部分）が実際に見えているか。
        /// 付箋の一部が見えていてもヘッダーが画面外なら移動もリサイズもできないため、
        /// 判定は付箋全体ではなくヘッダー帯に対して行う。
        /// </summary>
        public static bool IsHeaderVisible(double x, double y, double width, double headerHeight)
        {
            var (left, top, right, bottom) = GetVirtualScreen(out bool ok);

            // 範囲が取得できない異常時は、動かさない方を選ぶ
            if (!ok) return true;

            if (y < top || y + headerHeight > bottom) return false;

            double visibleWidth = Math.Min(x + width, right) - Math.Max(x, left);
            return visibleWidth >= Math.Min(MinGrabbableWidth, width);
        }
    }
}
