using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LockLibrary
{
    public static class ScreenEffectManager
    {
        private static bool isGray = false;

        [DllImport("Magnification.dll", SetLastError = true)]
        private static extern bool MagInitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        private static extern bool MagUninitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        private static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT pEffect);

        [StructLayout(LayoutKind.Sequential)]
        private struct MAGCOLOREFFECT
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public float[] transform;
        }

        public static void ApplyGrayscale()
        {
            if (isGray) return;
            if (!MagInitialize())
            {
                MessageBox.Show("グレースケールを適用できません（管理者権限が必要）", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var effect = new MAGCOLOREFFECT
            {
                transform = new float[]
                {
                    0.3f, 0.3f, 0.3f, 0, 0,
                    0.6f, 0.6f, 0.6f, 0, 0,
                    0.1f, 0.1f, 0.1f, 0, 0,
                    0, 0, 0, 1, 0,
                    0, 0, 0, 0, 1
                }
            };

            if (!MagSetFullscreenColorEffect(ref effect))
            {
                MessageBox.Show("グレースケール適用に失敗しました", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                MagUninitialize();
                return;
            }

            isGray = true;
            NotifyHelper.Show("画面効果", "グレースケールを適用しました");
        }

        public static void RemoveGrayscale()
        {
            if (!isGray) return;
            MagUninitialize();
            isGray = false;
            NotifyHelper.Show("画面効果", "グレースケールを解除しました");
        }
    }
}
