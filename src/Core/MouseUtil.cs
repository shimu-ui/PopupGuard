using System.Runtime.InteropServices;

namespace Core;

public static class MouseUtil
{
    private const int VK_LBUTTON = 0x01;

    public static bool IsLeftButtonDown()
    {
        return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}


